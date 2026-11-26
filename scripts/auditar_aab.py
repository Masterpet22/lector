"""Audita los metadatos de R8 y las llamadas de barras del sistema en un AAB."""

import argparse
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
from tempfile import TemporaryDirectory
from zipfile import ZipFile


R8_JSON = "BUNDLE-METADATA/com.android.tools/r8.json"
MAPPING = "BUNDLE-METADATA/com.android.tools.build.obfuscation/proguard.map"
SYMBOL_PREFIX = "BUNDLE-METADATA/com.android.tools.build.debugsymbols/"
DEX_PREFIX = "base/dex/"
WINDOW_SETTERS = (
    "Landroid/view/Window;.setStatusBarColor:",
    "Landroid/view/Window;.setNavigationBarColor:",
)


def find_dexdump(explicit: str | None) -> Path:
    if explicit:
        path = Path(explicit)
        if path.is_file():
            return path
        raise FileNotFoundError(f"No existe dexdump: {path}")

    executable = shutil.which("dexdump")
    if executable:
        return Path(executable)

    roots = [os.environ.get("ANDROID_SDK_ROOT"), os.environ.get("ANDROID_HOME")]
    if os.environ.get("LOCALAPPDATA"):
        roots.append(str(Path(os.environ["LOCALAPPDATA"]) / "Android" / "Sdk"))
    for root in roots:
        if not root:
            continue
        build_tools = Path(root) / "build-tools"
        if build_tools.is_dir():
            for version in sorted(build_tools.iterdir(), reverse=True):
                for name in ("dexdump.exe", "dexdump"):
                    path = version / name
                    if path.is_file():
                        return path
    raise FileNotFoundError("No se encontró dexdump; indique --dexdump.")


def count_mapping(mapping: str) -> tuple[int, int, int, int]:
    classes = renamed_classes = 0
    methods = renamed_methods = 0
    current_class = ""
    for line in mapping.splitlines():
        if line and not line[0].isspace() and not line.startswith("#") and " -> " in line and line.endswith(":"):
            original, renamed = line[:-1].split(" -> ", 1)
            if renamed.startswith("R8$$REMOVED$$"):
                current_class = ""
                continue
            current_class = original
            classes += 1
            renamed_classes += original != renamed
        elif current_class and line.startswith("    ") and " -> " in line:
            original, renamed = line.strip().rsplit(" -> ", 1)
            if "(" in original and ")" in original:
                name = original[: original.index("(")].split()[-1].rsplit(".", 1)[-1]
                methods += 1
                renamed_methods += name != renamed
    return classes, renamed_classes, methods, renamed_methods


def inspect_dex(dexdump: Path, dex_path: Path) -> tuple[set[str], bool, list[tuple[str, str, str, bool]], int]:
    process = subprocess.Popen(
        [str(dexdump), "-d", str(dex_path)],
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        text=True,
        errors="replace",
    )
    assert process.stdout is not None
    descriptors: set[str] = set()
    edge_enable = False
    window_calls: list[tuple[str, str, str, bool]] = []
    window_compat_uses = 0
    current_class = current_method = ""
    recent_instructions: list[str] = []

    for line in process.stdout:
        if "Class descriptor" in line:
            current_class = line.split(":", 1)[-1].strip().strip("'")
            descriptors.add(current_class)
            recent_instructions.clear()
        elif line.strip().startswith("name          :"):
            current_method = line.split(":", 1)[-1].strip().strip("'")
            recent_instructions.clear()
        elif re.match(r"^[0-9a-f]+:", line):
            recent_instructions.append(line)
            if len(recent_instructions) > 32:
                recent_instructions.pop(0)

        if "invoke-" in line and "Landroidx/activity/EdgeToEdge;.enable:" in line:
            edge_enable = True
        if "invoke-" in line and "Landroidx/core/view/WindowCompat;.enableEdgeToEdge:" in line:
            window_compat_uses += 1
        if "invoke-" in line and any(setter in line for setter in WINDOW_SETTERS):
            recent = "".join(recent_instructions)
            guarded = "#int 35" in recent and "if-ge" in recent
            setter = "setStatusBarColor" if "setStatusBarColor" in line else "setNavigationBarColor"
            window_calls.append((current_class, current_method, setter, guarded))

    stderr = process.stderr.read() if process.stderr is not None else ""
    if process.wait() != 0:
        raise RuntimeError(f"dexdump falló: {stderr.strip()}")
    return descriptors, edge_enable, window_calls, window_compat_uses


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("aab", type=Path)
    parser.add_argument("--expected-id", help="Identificador Android esperado")
    parser.add_argument("--dexdump", help="Ruta de dexdump del Android SDK")
    parser.add_argument("--min-obfuscation", type=float, default=25.0)
    args = parser.parse_args()

    if not args.aab.is_file():
        parser.error(f"No existe el AAB: {args.aab}")
    dexdump = find_dexdump(args.dexdump)
    failures: list[str] = []

    with ZipFile(args.aab) as bundle, TemporaryDirectory() as temp_dir:
        names = set(bundle.namelist())
        for required in (R8_JSON, MAPPING):
            if required not in names:
                failures.append(f"Falta {required}")
        symbols = sorted(name for name in names if name.startswith(SYMBOL_PREFIX) and name.endswith(".sym"))
        if not symbols:
            failures.append("Faltan los símbolos nativos .sym")
        dex_names = sorted(name for name in names if name.startswith(DEX_PREFIX) and name.endswith(".dex"))
        if not dex_names:
            failures.append("Faltan los archivos DEX")
        if args.expected_id:
            manifest = bundle.read("base/manifest/AndroidManifest.xml")
            if args.expected_id.encode() not in manifest:
                failures.append(f"El manifiesto no contiene {args.expected_id}")

        if failures:
            for failure in failures:
                print("ERROR:", failure)
            return 1

        r8 = json.loads(bundle.read(R8_JSON))
        options = r8["options"]
        stats = r8["stats"]
        for option in ("isObfuscationEnabled", "isOptimizationsEnabled", "isShrinkingEnabled"):
            if not options.get(option):
                failures.append(f"R8 no activó {option}")
        obfuscation = 100.0 - float(stats["noObfuscationPercentage"])
        if obfuscation < args.min_obfuscation:
            failures.append(f"Ofuscación {obfuscation:.2f}% < {args.min_obfuscation:.2f}%")

        mapping_counts = count_mapping(bundle.read(MAPPING).decode("utf-8", errors="replace"))
        dex_bytes = 0
        descriptors: set[str] = set()
        edge_enable = False
        window_calls: list[tuple[str, str, str, bool]] = []
        window_compat_uses = 0
        for name in dex_names:
            data = bundle.read(name)
            dex_bytes += len(data)
            dex_path = Path(temp_dir) / Path(name).name
            dex_path.write_bytes(data)
            classes, edge, calls, compat_uses = inspect_dex(dexdump, dex_path)
            descriptors.update(classes)
            edge_enable |= edge
            window_calls.extend(calls)
            window_compat_uses += compat_uses

        if not edge_enable:
            failures.append("No hay referencia DEX a EdgeToEdge.enable")
        if "Lcom/google/android/material/sidesheet/SheetDialog;" in descriptors:
            failures.append("R8 conservó SheetDialog")
        for owner, method, setter, guarded in window_calls:
            if owner.startswith("Lcom/google/android/material/") and not guarded:
                failures.append(f"Llamada Material sin guarda API 35: {owner}.{method} -> {setter}")
        if window_compat_uses:
            failures.append("El DEX invoca WindowCompat.enableEdgeToEdge, que conserva setters obsoletos")

        print(f"AAB: {args.aab}")
        print(f"DEX: {len(dex_names)} archivo(s), {dex_bytes:,} bytes sin comprimir")
        print(f"R8: ofuscación {obfuscation:.2f}%; optimización y reducción activas")
        class_percent = 100 * mapping_counts[1] / mapping_counts[0] if mapping_counts[0] else 0
        method_percent = 100 * mapping_counts[3] / mapping_counts[2] if mapping_counts[2] else 0
        print(f"Mapping: {mapping_counts[1]:,}/{mapping_counts[0]:,} clases ({class_percent:.2f}%) y "
              f"{mapping_counts[3]:,}/{mapping_counts[2]:,} entradas de métodos ({method_percent:.2f}%) renombradas")
        print("Símbolos nativos:", ", ".join(Path(name).parent.name for name in symbols))
        print("EdgeToEdge.enable: presente; SheetDialog: ausente")
        for owner, method, setter, guarded in window_calls:
            print(f"  {owner}.{method} -> {setter}: {'API < 35' if guarded else 'sin guarda detectada'}")
        print("Invocaciones a WindowCompat.enableEdgeToEdge:", window_compat_uses)

    for failure in failures:
        print("ERROR:", failure)
    return 1 if failures else 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (FileNotFoundError, KeyError, RuntimeError) as error:
        print("ERROR:", error, file=sys.stderr)
        sys.exit(1)
