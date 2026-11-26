# Plan de desarrollo: Recomendaciones de Google Play Console para la próxima actualización

## Objetivo y diagnóstico de punto de partida

Resolver las tres recomendaciones técnicas y de experiencia de usuario notificadas en **Google Play Console** (`Prueba y lanza` → `Para tu próxima versión`) sobre el paquete publicado `5.aab (2.1.0)` (`Target SDK 36`, `Min SDK 24+`), garantizando que el próximo lanzamiento cumpla con los umbrales de optimización de Google Play y los requisitos de pantalla de borde a borde de Android 15 y Android 16 (SDK 35/36) sin regresiones en Android ni en Windows.

### Resumen de recomendaciones detectadas en Google Play Console

| # | Recomendación en Play Console | Categoría / Severidad | Diagnóstico reportado en `5.aab (2.1.0)` | Fecha límite / Meta |
| --- | --- | --- | --- | --- |
| 1 | **La optimización del código DEX está por debajo de nuestro umbral** | `Requiere atención` · `Uso de memoria` · `Comportamiento inadecuado` | Optimización DEX: `Baja` · Ofuscación: `1%` · Optimización: `-` · Reducción (Shrinking): `-` · Configuración de R8: `-` · Tamaño DEX sin comprimir: `15.3 MB` | Solucionar antes de **feb 2027** (Ofuscación **≥ 25%**, reducción y optimización R8 activas) |
| 2 | **Es posible que la pantalla de borde a borde no se muestre para todos los usuarios** | `Experiencia del usuario` | No se detecta invocación a `EdgeToEdge.enable()` / `enableEdgeToEdge()` para retrocompatibilidad ni manejo integral de `WindowInsets` de borde a borde. | Habilitar `EdgeToEdge.Enable(this)` en `MainActivity` y gestionar `WindowInsets` en vistas y lector |
| 3 | **Tu app usa APIs o parámetros obsoletos para la pantalla de borde a borde** | `Experiencia del usuario` | Uso de `android.view.Window.setStatusBarColor` y `android.view.Window.setNavigationBarColor` en:<br>• `com.google.android.material.bottomsheet.BottomSheetDialog.onCreate`<br>• `com.google.android.material.internal.EdgeToEdgeUtils.applyEdgeToEdge`<br>• `com.google.android.material.sidesheet.SheetDialog.onCreate` | Eliminar/migrar llamadas obsoletas actualizando `Xamarin.Google.Android.Material` y depurando clases no usadas con R8 |

---

## Análisis de causa raíz en el proyecto

1. **Optimización y ofuscación DEX (`1%` de ofuscación y `15.3 MB` de DEX):**
   - El paquete `5.aab` subido a Play Console contiene `base/dex/classes.dex` (`8.85 MB`) y `base/dex/classes2.dex` (`6.47 MB`), sumando `15.3 MB` compilados con `D8` en una compilación incremental previa y sin los metadatos `BUNDLE-METADATA/com.android.tools/r8.json` ni `BUNDLE-METADATA/com.android.tools.build.obfuscation/proguard.map`.
   - En `.NET 10 for Android` (`Microsoft.Android.Sdk 36.1.118`), definir únicamente `<AndroidLinkTool>r8</AndroidLinkTool>` deja la propiedad `AndroidR8ObfuscationMode` en su valor predeterminado `disabled`. Con `disabled`, el SDK incorpora `proguard-android.txt` (que incluye `-dontoptimize`) y desactiva la ofuscación en la tarea `R8`.
   - Al establecer `<AndroidR8ObfuscationMode>private-members</AndroidR8ObfuscationMode>`, el SDK pasa a utilizar `proguard-android-optimize.txt` (habilitando optimizaciones y `-allowaccessmodification`), activa la ofuscación en R8 y empaqueta tanto `r8.json` como `mapping.txt` dentro del `.aab`.
   - Adicionalmente, el paso `GenerateProguardConfiguration` de ILLink genera `proguard_project_references.cfg` con reglas `-keep` para los tipos enlazados de AndroidX, Material, Glide y Gson. Es necesario verificar el porcentaje real de símbolos ofuscados en `mapping.txt` y complementar con un archivo `Platforms/Android/proguard.cfg` (`ProguardConfiguration`) si las reglas autogeneradas impiden superar el umbral del **25%** exigido por Google Play.

2. **APIs obsoletas de barras del sistema (`setStatusBarColor` / `setNavigationBarColor`):**
   - El proyecto resuelve transitivamente `Xamarin.Google.Android.Material/1.12.0.5` (`com.google.android.material:material:1.12.0`), donde `BottomSheetDialog.onCreate`, `EdgeToEdgeUtils.applyEdgeToEdge` y `SheetDialog.onCreate` contienen llamadas directas a `Window.setStatusBarColor` y `Window.setNavigationBarColor`.
   - Como `5.aab` no pasó por el encogimiento efectivo de R8, `SheetDialog` y otros componentes de Material no utilizados permanecieron en el bytecode DEX. Además, `BottomSheetDialog` se conserva en `proguard_project_references.cfg` por referencia de bindings de MAUI.
   - La solución combina actualizar `Xamarin.Google.Android.Material` a una versión `1.13.0+` / `1.14.x` compatible con `net10.0-android` y aplicar el encogimiento/optimización de R8 para eliminar las clases y métodos de Material no referenciados en tiempo de ejecución.

3. **Pantalla de borde a borde (`EdgeToEdge`) e inserciones de ventana (`WindowInsets`):**
   - `Xamarin.AndroidX.Activity/1.10.1.3` ya está presente en el grafo de dependencias y expone `AndroidX.Activity.EdgeToEdge.Enable(ComponentActivity)`, pero [MainActivity.cs](NebulaReader/Platforms/Android/MainActivity.cs) aún no lo invoca en `OnCreate`.
   - [FullScreenService.Android.cs](NebulaReader/Platforms/Android/FullScreenService.Android.cs) llama a `WindowCompat.SetDecorFitsSystemWindows(window, !enabled)` al alternar el modo de pantalla completa. Con borde a borde activo de forma predeterminada (`SetDecorFitsSystemWindows(window, false)` / `EdgeToEdge.Enable(this)`), la ventana siempre debe extenderse detrás de las barras del sistema, y el modo inmersivo del lector solo debe ocultar o mostrar las barras (`WindowInsetsCompat.Type.SystemBars()`) mediante `WindowInsetsControllerCompat` sin desactivar el diseño de borde a borde.
   - Las vistas principales ([LibraryPage.xaml](NebulaReader/Views/LibraryPage.xaml), [ManagementPage.xaml](NebulaReader/Views/ManagementPage.xaml), [SettingsPage.xaml](NebulaReader/Views/SettingsPage.xaml)) y las barras HUD del visor ([ReaderPage.xaml](NebulaReader/Views/ReaderPage.xaml)) deben respetar los `WindowInsets` (`SystemBars` y `DisplayCutout`) para que ningún botón, cabecera o control deslizable quede oculto bajo la barra de estado, la cámara frontal o la barra de navegación por gestos/botones, mientras que el lienzo SkiaSharp (`CanvasHost`) en [ReaderPage.xaml](NebulaReader/Views/ReaderPage.xaml) continúa dibujándose a pantalla completa de borde a borde.

---

## Entrega 1: Optimización, reducción y ofuscación DEX con R8 (≥ 25%)

**Prioridad: alta (fecha límite de Play Console: febrero de 2027).**

- [x] Configurar en [NebulaReader.csproj](NebulaReader/NebulaReader.csproj) para compilaciones `Release` de Android:
  - `<AndroidLinkTool>r8</AndroidLinkTool>`
  - `<AndroidR8ObfuscationMode>private-members</AndroidR8ObfuscationMode>`
  - `<AndroidCreateProguardMappingFile>true</AndroidCreateProguardMappingFile>`
- [x] Incorporar un archivo de reglas personalizadas `NebulaReader/Platforms/Android/proguard.cfg` registrado mediante `<ProguardConfiguration Include="Platforms\Android\proguard.cfg" Condition="'$(Configuration)' == 'Release' and $([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'android'" />` en [NebulaReader.csproj](NebulaReader/NebulaReader.csproj).
- [x] Ajustar la configuración de R8 y las reglas de `proguard.cfg` para asegurar que:
  - No se incluyan directivas `-dontshrink`, `-dontoptimize` ni `-dontobfuscate`.
  - Se preserven únicamente los puntos de entrada necesarios en tiempo de ejecución (actividades, `MauiApplication`, callbacks JNI/ACW de C# como `ChoreographerFrameCallback` en [ReaderPage.Android.cs](NebulaReader/Platforms/Android/ReaderPage.Android.cs), `SkiaSharp` y `sqlite-net-pcl`).
  - El porcentaje de clases/miembros ofuscados en el DEX final supere holgadamente el umbral mínimo del **25%** requerido por Google Play Console.
- [x] Auditar el proceso de compilación limpia (`dotnet clean` + `dotnet publish -f net10.0-android -c Release`) para garantizar que no se reutilicen artefactos `classes.dex` previos compilados con `D8` y que el `.aab` generado incluya en `BUNDLE-METADATA/`:
  - `com.android.tools/r8.json`
  - `com.android.tools.build.obfuscation/proguard.map`
  - `com.android.tools.build.debugsymbols/<abi>/libxamarin-app.so.sym`

**Criterios de aceptación:**
- El archivo `.aab` de `Release` contiene `BUNDLE-METADATA/com.android.tools/r8.json` y `BUNDLE-METADATA/com.android.tools.build.obfuscation/proguard.map`.
- El tamaño total sin comprimir de los archivos `base/dex/classes*.dex` dentro del `.aab` se reduce significativamente respecto a los `15.3 MB` actuales gracias a `EnableShrinking=True` y `proguard-android-optimize.txt`.
- El análisis de `mapping.txt` y del bytecode DEX confirma que la proporción de elementos ofuscados es **≥ 25%** (objetivo recomendado: **≥ 30%**) y la aplicación inicia, importa documentos, renderiza páginas con SkiaSharp y guarda en SQLite sin `ClassNotFoundException` ni `NoSuchMethodError` en `Release`.

**Verificación (26 de noviembre de 2026):** AAB `Release` generado con Android SDK 36.1.118; `r8.json` informa **28,19% de ofuscación**, optimización y reducción activas. El DEX sin comprimir mide **6.118.472 bytes** y el AAB contiene `r8.json`, `proguard.map` y símbolos nativos de las cuatro arquitecturas. Pasaron **152 pruebas automatizadas**. En un dispositivo Android 12, una variante `Release` con R8 instalada en paralelo inició, importó y renderizó un PDF y un CBZ; ambos documentos persistieron después de cerrar y reabrir la aplicación. El objetivo recomendado de 30% aún no se alcanza.

**Archivos de partida:** [NebulaReader.csproj](NebulaReader/NebulaReader.csproj), `NebulaReader/Platforms/Android/proguard.cfg` (nuevo) y [ReaderPage.Android.cs](NebulaReader/Platforms/Android/ReaderPage.Android.cs).

---

## Entrega 2: Migración de APIs obsoletas de barras del sistema y actualización de Material Components

**Prioridad: alta.**

- [x] Evaluar y fijar en [NebulaReader.csproj](NebulaReader/NebulaReader.csproj) (para `net10.0-android`) una referencia explícita a una versión actualizada de `Xamarin.Google.Android.Material` (`1.13.0.2` o `1.14.0.6`, verificando compatibilidad de bindings con `Microsoft.Maui.Controls 10.0.110` y `.NET Android SDK 36.1.118`).
- [x] Verificar que no existan llamadas directas ni indirectas en el código propio de la plataforma Android ([MainActivity.cs](NebulaReader/Platforms/Android/MainActivity.cs), [FullScreenService.Android.cs](NebulaReader/Platforms/Android/FullScreenService.Android.cs), `Resources/values/colors.xml` o temas personalizados) a `Window.SetStatusBarColor`, `Window.SetNavigationBarColor`, `android:statusBarColor` o `android:navigationBarColor` obsoletos en Android 15+.
- [x] Configurar R8 / ProGuard (`Platforms/Android/proguard.cfg` y el postprocesado de reglas de referencias si fuera necesario) para que las clases de Material no utilizadas por la aplicación (como `com.google.android.material.sidesheet.SheetDialog` y componentes no instanciados) sean eliminadas durante el shrinking.
- [x] Inspeccionar el bytecode DEX resultante (`classes*.dex` del `.aab` de `Release`) con herramientas del Android SDK (`apkanalyzer` / `dexdump` / búsqueda de referencias a métodos en DEX) para comprobar si persiste alguna referencia a `Landroid/view/Window;->setStatusBarColor(I)V` o `Landroid/view/Window;->setNavigationBarColor(I)V` en `BottomSheetDialog`, `EdgeToEdgeUtils` o `SheetDialog`.

**Criterios de aceptación:**
- La compilación `Release` de Android con R8 no presenta errores de enlace Java/AndroidX tras actualizar `Xamarin.Google.Android.Material`.
- En el DEX final del paquete `.aab`, `com.google.android.material.sidesheet.SheetDialog` queda eliminado por R8 y las llamadas a `Window.setStatusBarColor` y `Window.setNavigationBarColor` reportadas por Google Play Console desaparecen o quedan migradas a la implementación compatible con Android 15/16.

**Verificación de la entrega 2 (26 de noviembre de 2026):** Se fijó `Xamarin.Google.Android.Material 1.14.0.6` solo para Android. La auditoría del código propio y los recursos no encontró setters ni atributos obsoletos de las barras.

- La compilación `Release` de pruebas (`app.nebulareader.pruebas`, ARM64, `RuntimeIdentifier=android-arm64`) generó APK y AAB con R8 y con las reglas de ProGuard del enlazador presentes. `r8.json` informa **28,57 % de ofuscación** y el DEX sin comprimir mide **6.410.196 bytes**.
- `SheetDialog` y `EdgeToEdgeUtils` no están en el DEX. `BottomSheetDialog.onCreate` conserva una llamada a `setStatusBarColor` condicionada a API < 35; `MaterialDatePicker.onStart` conserva llamadas a ambas barras con la misma condición. `androidx.core.view.WindowCompat.enableEdgeToEdge` contiene dos llamadas adicionales, pero ninguna instrucción del DEX invoca ese método.
- El APK de pruebas se instaló junto a `app.nebulareader.mobile` y abrió Inicio, Biblioteca y el menú Añadir sin excepción. La restauración emitió advertencias `NU1608` por restricciones de versiones transitivas de AndroidX; no hubo errores de compilación ni fallos en esta prueba de ejecución.

**Archivos de partida:** [NebulaReader.csproj](NebulaReader/NebulaReader.csproj), [colors.xml](NebulaReader/Platforms/Android/Resources/values/colors.xml) y `NebulaReader/Platforms/Android/proguard.cfg`.

---

## Entrega 3: Habilitación de pantalla de borde a borde (`EdgeToEdge`) y gestión de `WindowInsets`

**Prioridad: alta.**

- [x] Llamar a `AndroidX.Activity.EdgeToEdge.Enable(this)` en `MainActivity.OnCreate` ([MainActivity.cs](NebulaReader/Platforms/Android/MainActivity.cs)) con un estilo oscuro acorde al tema de la aplicación (`#08070E` / `SystemBarStyle.Dark`) para habilitar el modo de borde a borde con retrocompatibilidad desde API 24 hasta API 36, satisfaciendo la verificación estática y de tiempo de ejecución de Google Play Console.
- [x] Actualizar [FullScreenService.Android.cs](NebulaReader/Platforms/Android/FullScreenService.Android.cs) para que sea coherente con una ventana permanentemente en modo borde a borde:
  - Mantener `WindowCompat.SetDecorFitsSystemWindows(window, false)` activo en todo momento (en lugar de revertirlo a `true` al salir de pantalla completa).
  - Limitar `ApplyFullScreenPlatform(bool enabled)` a ocultar o mostrar `WindowInsetsCompat.Type.SystemBars()` mediante `WindowInsetsControllerCompat` con `BehaviorShowTransientBarsBySwipe`.
- [x] Implementar la gestión de inserciones de ventana (`ViewCompat.SetOnApplyWindowInsetsListener` con `WindowInsetsCompat.Type.SystemBars() | WindowInsetsCompat.Type.DisplayCutout()`) en Android para que el contenido interactivo respete las áreas seguras sin perder el fondo de borde a borde (`#08070E`):
  - En las pantallas de navegación y formularios ([LibraryPage.xaml](NebulaReader/Views/LibraryPage.xaml), [ManagementPage.xaml](NebulaReader/Views/ManagementPage.xaml) y [SettingsPage.xaml](NebulaReader/Views/SettingsPage.xaml)), aplicar los insets del sistema al contenedor raíz o barra inferior para que la cabecera, la barra lateral y la navegación inferior nunca queden solapadas con la barra de estado, el recorte de pantalla (*cutout*) o la barra de navegación.
  - En el visor ([ReaderPage.xaml](NebulaReader/Views/ReaderPage.xaml) y [ReaderPage.xaml.cs](NebulaReader/Views/ReaderPage.xaml.cs)), mantener `CanvasHost` ocupando el 100% de la ventana de borde a borde y aplicar los márgenes seguros de `WindowInsets` únicamente a los paneles flotantes (`TopHudBar`, `BottomHudBar`, `ReadingOptionsPanel` y `BookmarksPanel`) cuando las barras del sistema estén visibles.
- [x] Garantizar que la adaptación de `WindowInsets` en Android no altere los márgenes ni el diseño adaptable en Windows (`net10.0-windows10.0.19041.0`).

**Criterios de aceptación:**
- `MainActivity.OnCreate` invoca `EdgeToEdge.Enable(this)` y el bytecode DEX contiene la referencia a `Landroidx/activity/EdgeToEdge;->enable`.
- Al alternar el modo de pantalla completa desde Gestión o al entrar/salir del lector ([ReaderPage.xaml](NebulaReader/Views/ReaderPage.xaml)), la transición entre barras visibles y barras ocultas no produce saltos de redimensionamiento bruscos ni desactiva el modo de borde a borde.
- En teléfonos y tabletas Android (tanto con navegación por gestos como con barra de 3 botones, en orientación vertical y horizontal), ningún botón de cabecera, tarjeta, filtro, barra inferior o HUD del lector queda cubierto por la barra de estado, el *cutout* o la barra de navegación.

**Verificación de la entrega 3 (26 de noviembre de 2026):** La variante `Release` de pruebas (`app.nebulareader.pruebas`, `versionCode 1005`, ARM64) compiló sin errores, generó APK y AAB y se instaló junto a la versión oficial. El DEX final contiene una referencia a `Landroidx/activity/EdgeToEdge;->enable` y `r8.json` informa **28,58 % de ofuscación**. También compiló `Release` para Windows sin errores. En el teléfono conectado (Android 12, API 31, navegación de tres botones) se comprobaron Inicio, Biblioteca, Gestión y Configuración; se ocultaron y restauraron las barras desde Gestión; se importó, renderizó y reabrió un PDF de dos páginas; y se comprobaron el HUD, Opciones de lectura y Marcadores en vertical y horizontal. El lienzo permanece a pantalla completa y los controles observados respetan las barras y el recorte. No hubo cierres inesperados. La validación en Android 15/16, tabletas y navegación por gestos queda pendiente para la verificación ampliada de la entrega 4.

**Archivos de partida:** [MainActivity.cs](NebulaReader/Platforms/Android/MainActivity.cs), [FullScreenService.Android.cs](NebulaReader/Platforms/Android/FullScreenService.Android.cs), [LibraryPage.xaml](NebulaReader/Views/LibraryPage.xaml), [LibraryPage.xaml.cs](NebulaReader/Views/LibraryPage.xaml.cs), [ReaderPage.xaml](NebulaReader/Views/ReaderPage.xaml), [ReaderPage.xaml.cs](NebulaReader/Views/ReaderPage.xaml.cs), [ManagementPage.xaml](NebulaReader/Views/ManagementPage.xaml) y [SettingsPage.xaml](NebulaReader/Views/SettingsPage.xaml).

---

## Entrega 4: Verificación del paquete `.aab`, pruebas y preparación del lanzamiento

**Prioridad: media-alta.**

- [x] Incrementar la versión de publicación en [NebulaReader.csproj](NebulaReader/NebulaReader.csproj) para la nueva actualización de Google Play Console (por ejemplo, `<ApplicationDisplayVersion>2.1.1</ApplicationDisplayVersion>` y `<ApplicationVersion>6</ApplicationVersion>`).
- [x] Ejecutar la batería de pruebas unitarias (`dotnet test NebulaReader.Tests/NebulaReader.Tests.csproj`) y añadir pruebas unitarias si se introduce lógica compartida de cálculo de márgenes seguros (*safe area* / *insets*).
- [x] Compilar en limpio y empaquetar `Release` para Android (`dotnet publish NebulaReader/NebulaReader.csproj -f net10.0-android -c Release`) y verificar también la compilación de Windows (`dotnet build NebulaReader/NebulaReader.csproj -f net10.0-windows10.0.19041.0 -c Release`).
- [x] Ejecutar una auditoría técnica automatizada sobre el `.aab` generado antes de subirlo a Google Play Console:
  1. Comprobar la presencia de `BUNDLE-METADATA/com.android.tools/r8.json`, `BUNDLE-METADATA/com.android.tools.build.obfuscation/proguard.map` y los símbolos nativos `com.android.tools.build.debugsymbols/**`.
  2. Medir el tamaño descomprimido de `base/dex/classes*.dex` y calcular el porcentaje de clases/métodos ofuscados en `mapping.txt` (verificando que sea **≥ 25%**).
  3. Analizar `base/dex/classes*.dex` para confirmar la presencia de `androidx.activity.EdgeToEdge.enable` y la ausencia de llamadas a `Window.setStatusBarColor` / `Window.setNavigationBarColor` en clases de Material eliminadas o actualizadas.
- [x] Actualizar la documentación técnica en [HERRAMIENTAS_DE_DESARROLLO.md](HERRAMIENTAS_DE_DESARROLLO.md) o [Readme.md](Readme.md) si se añaden comandos de publicación o verificación de R8 para el `.aab`.

**Criterios de aceptación:**
- Todas las pruebas automatizadas en `NebulaReader.Tests` pasan sin errores.
- Las compilaciones `Debug` y `Release` de Android y Windows finalizan sin errores ni advertencias de trimming/R8.
- El paquete `.aab` generado cumple verificablemente las tres condiciones exigidas por Google Play Console (optimización/ofuscación R8 ≥ 25%, invocación a `EdgeToEdge.enable` con soporte de `WindowInsets` y eliminación/migración de llamadas obsoletas a `setStatusBarColor`/`setNavigationBarColor`).

**Verificación de la entrega 4 (26 de noviembre de 2026):** La versión oficial se incrementó a `2.1.1` (`versionCode 6`). Pasaron las 152 pruebas unitarias. Android `Release` se publicó en limpio como AAB local; Android `Debug` y Windows `Debug`/`Release` compilaron sin errores ni advertencias de trimming/R8. La variante Android `Release` de pruebas (`app.nebulareader.pruebas`, `versionCode 1006`) compiló, se instaló junto a `app.nebulareader.mobile` y abrió Inicio y el PDF de dos páginas en el teléfono Android 12 conectado sin cierres inesperados. La auditoría reproducible de [scripts/auditar_aab.py](scripts/auditar_aab.py) pasó en los AAB oficial y de pruebas: DEX descomprimido de 6.420.740 bytes, R8 con 28,58 % de ofuscación global, `r8.json`, `proguard.map` y símbolos de cuatro ABI presentes; `mapping` registra 220/7.531 clases (2,92 %) y 96.870/252.792 entradas de métodos (38,32 %) renombradas. El DEX invoca `EdgeToEdge.enable`, no contiene `SheetDialog` y las llamadas de Material restantes a los setters de barras están protegidas para API < 35. Se documentaron los comandos en [HERRAMIENTAS_DE_DESARROLLO.md](HERRAMIENTAS_DE_DESARROLLO.md) y las reglas de separación de variantes en [REGLAS_DE_COMPILACION.md](REGLAS_DE_COMPILACION.md).

**Límites de la verificación local:** El 28,58 % corresponde a la métrica global de R8; el porcentaje de clases renombradas en `mapping` es 2,92 %, por debajo de 25 %, mientras que las entradas de métodos renombradas alcanzan 38,32 %. Persisten llamadas a setters de barras dentro de AndroidX; no se comprobó aún cómo clasificará Play Console el AAB actualizado. El AAB local no está firmado con la clave de publicación. También queda pendiente repetir la prueba visual en Android 15/16, tableta y navegación por gestos, porque el dispositivo disponible usa Android 12 con tres botones.

**Corrección posterior de `NU1608` (26 de noviembre de 2026):** Se alinearon en [NebulaReader.csproj](NebulaReader/NebulaReader.csproj) las extensiones KTX de AndroidX con las versiones de Activity, Collection, Fragment, Lifecycle y SavedState resueltas por MAUI y Material, sin silenciar advertencias. Se corrigió además la comprobación de nulidad de la URI de contenido en [LibraryBackup.cs](NebulaReader/Models/LibraryBackup.cs). La restauración, Windows `Release` y Android `Release` de pruebas terminaron con **cero advertencias y cero errores**; pasaron 152 pruebas unitarias. El AAB de pruebas conserva 28,58 % de ofuscación global R8 y pasó la auditoría; la APK `app.nebulareader.pruebas` (`versionCode 1007`, ARM64) se instaló junto a la app oficial y abrió el PDF de dos páginas sin cierres inesperados.
