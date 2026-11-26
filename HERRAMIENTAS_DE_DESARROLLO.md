# Herramientas de desarrollo

La rama contiene **Nebula Reader**, una aplicación .NET MAUI dirigida a Android
(`net10.0-android`). No es un proyecto Android/Gradle independiente: se compila
desde `NebulaReader.slnx` y `NebulaReader/NebulaReader.csproj`.

## Requisitos comunes

- **Git**: clonar el repositorio, cambiar a la rama `main` y gestionar cambios.
- **.NET SDK 10**: compilar la aplicación y ejecutar las pruebas.
- **Cargas de trabajo .NET para Android y MAUI**: proporcionan los componentes
  de compilación Android usados por .NET MAUI.
- **Android SDK**: instalar con Android Studio o el SDK Manager. Se necesitan
  las plataformas y herramientas de compilación requeridas por la carga de
  trabajo .NET instalada, además de **Android SDK Platform-Tools** (`adb`).
- **Java JDK** compatible con la versión instalada de .NET para Android.
- **Python 3** para ejecutar la auditoría reproducible del AAB; el script usa solo la biblioteca estándar.
- **Conexión a Internet** para restaurar paquetes NuGet y componentes del SDK
  que aún no estén instalados.
- **Dispositivo Android** con depuración USB habilitada, o un **emulador Android**.

## Visual Studio Code

- **Visual Studio Code**: editor de código.
- **Extensión C#** y **C# Dev Kit**: edición, navegación y depuración de C#.
- **Terminal integrada**: ejecutar los comandos .NET y `adb` indicados abajo.
- **Android Studio (opcional)**: administrar el Android SDK y crear emuladores;
  no es necesario usarlo como editor.

## Codex

- **Codex** (aplicación o CLI) con acceso al repositorio y a una terminal.
- **Git, .NET SDK 10, cargas de trabajo MAUI/Android, Android SDK y JDK**:
  Codex puede trabajar con el código, pero estos componentes locales siguen
  siendo necesarios para compilar, probar o desplegar la aplicación.
- **Dispositivo Android o emulador** para comprobar la ejecución. Android
  Studio puede usarse opcionalmente para administrar el SDK y los emuladores.

## Antigravity

- **Antigravity** con el repositorio abierto y acceso a su terminal integrada.
- **Git, .NET SDK 10, cargas de trabajo MAUI/Android, Android SDK y JDK**:
  el entorno de Antigravity no los reemplaza; deben estar instalados y
  configurados en el equipo donde se compila.
- **Dispositivo Android o emulador** para ejecutar y depurar la app. Android
  Studio es opcional y sirve para administrar el SDK y los emuladores.

## Comandos de compilación y prueba

Los mismos comandos aplican desde la terminal de cualquiera de los tres entornos.
Ejecutarlos desde la raíz del repositorio:

```powershell
dotnet workload list
dotnet workload install maui-android
dotnet restore NebulaReader.slnx
dotnet build NebulaReader/NebulaReader.csproj -f net10.0-android
dotnet test NebulaReader.Tests/NebulaReader.Tests.csproj
adb devices
```

`dotnet workload install maui-android` solo hace falta si la carga de trabajo
no está instalada. Para desplegar en un dispositivo o emulador conectado:

```powershell
dotnet build NebulaReader/NebulaReader.csproj -t:Run -f net10.0-android
```

La compilación **Debug** incluye los ensamblados dentro de la APK para que
también pueda instalarse directamente con `adb install -r` y abrirse sin el
despliegue adicional de Visual Studio. Por eso su APK ocupa más espacio. La
publicación **Release** sigue siendo la opción para distribuir o probar el
rendimiento final.

## Auditoría del AAB Release

Desde la raíz del repositorio, genera primero un AAB oficial **local sin la clave de publicación**. Este paquete sirve para revisar el contenido; usa las instrucciones de [REGLAS_DE_COMPILACION.md](REGLAS_DE_COMPILACION.md) para firmar el AAB que se enviará a Google Play.

```powershell
dotnet clean NebulaReader/NebulaReader.csproj -f net10.0-android -c Release
dotnet publish NebulaReader/NebulaReader.csproj -f net10.0-android -c Release `
  -p:AndroidPackageFormats=aab -p:AndroidKeyStore=false

python scripts/auditar_aab.py `
  NebulaReader/bin/Release/net10.0-android/app.nebulareader.mobile.aab `
  --expected-id app.nebulareader.mobile
```

La auditoría usa `dexdump` de Android SDK Build-Tools. Comprueba `r8.json`, `proguard.map`, símbolos nativos, tamaño DEX, ofuscación mínima del 25 %, `EdgeToEdge.enable`, eliminación de `SheetDialog` y llamadas a los setters de barras del sistema. Informa por separado las llamadas que quedan en AndroidX y las llamadas de Material protegidas para API anteriores a 35. Devuelve un código distinto de cero si falla una condición verificable. Repite la auditoría sobre el AAB firmado con la clave de subida antes de enviarlo a Google Play.

Para compilar e instalar un APK de pruebas que coexista con la app oficial, usa el bloque de [REGLAS_DE_COMPILACION.md](REGLAS_DE_COMPILACION.md); conserva los identificadores y directorios separados.

## Para publicar

Se requiere además un certificado/keystore de firma Android y su configuración
segura para generar una versión de distribución, independientemente del entorno
de edición. No se deben incluir claves ni contraseñas en el repositorio.
