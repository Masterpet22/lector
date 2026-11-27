# Reglas de compilación: pruebas y distribución

Estas reglas aplican a las compilaciones **Android** de Nebula Reader. Permiten mantener instalada la versión de Google Play junto a una versión de prueba, incluso cuando sus certificados de firma son distintos. Ejecuta los comandos de PowerShell desde la raíz del repositorio.

La APK de pruebas y el AAB oficial se generan desde **el mismo proyecto y los mismos archivos fuente**. Los cambios de código, XAML, recursos y dependencias probados deben estar presentes en el árbol de trabajo antes de compilar el AAB. Las bases de datos, preferencias y documentos importados de cada instalación son datos del dispositivo: cambiar de identificador de aplicación no los copia al otro canal.

## Identidades que no se deben intercambiar

| Canal | Identificador Android | Nombre visible | Formato | Firma |
| --- | --- | --- | --- | --- |
| Oficial | `app.nebulareader.mobile` | Nebula Reader | AAB para Google Play | Keystore de distribución o clave de subida correspondiente |
| Pruebas | `app.nebulareader.pruebas` | Nebula Reader Pruebas | APK para instalación local | Certificado de desarrollo o keystore exclusivo de pruebas |

1. **Nunca compiles una prueba instalable con `app.nebulareader.mobile`.** Cambiar solo entre `Debug` y `Release` no cambia el identificador. Si una APK local tiene el identificador oficial y otra firma, Android rechazará la actualización; desinstalar la aplicación oficial borraría sus datos locales.
2. **Mantén fijo `app.nebulareader.pruebas` para las pruebas.** Incrementa su `ApplicationVersion` cuando corresponda y conserva la misma clave de pruebas para poder actualizarla con `adb install -r`. Si cambia esa clave, desinstala únicamente `app.nebulareader.pruebas` antes de instalarla de nuevo.
3. **Mantén fijo `app.nebulareader.mobile` para Google Play.** Antes de cada publicación oficial, incrementa `ApplicationVersion` por encima de la versión ya distribuida y ajusta `ApplicationDisplayVersion` en `NebulaReader.csproj`. No uses el identificador ni la clave de pruebas en un AAB oficial.
4. **Separa también los artefactos intermedios.** El canal oficial utiliza los directorios predeterminados `NebulaReader/obj` y `NebulaReader/bin`; el canal de pruebas utiliza `--artifacts-path releases/pruebas` en `clean`, `build` y `publish`. `-o` cambia la salida publicada, pero por sí solo no separa todo `obj` y `bin`. Las carpetas `releases/` ya están excluidas de Git. Al compilar una sola arquitectura de pruebas, establece `RuntimeIdentifier` y `RuntimeIdentifiers` en ambos comandos: el proyecto declara cuatro arquitecturas por defecto y R8 necesita las reglas de ProGuard del enlace correspondiente.
5. **No mezcles claves.** El proyecto no contiene un keystore de distribución. El sufijo `-Signed.aab` por sí solo no demuestra que un paquete esté firmado con la clave correcta para Google Play. Guarda el keystore y los archivos de contraseña fuera del repositorio.

## APK Release de pruebas, con R8

La variante de pruebas conserva la configuración `Release` de trimming, reducción y ofuscación. El ejemplo compila solo ARM64 para el dispositivo de pruebas; ajusta `RuntimeIdentifier` si necesitas otra arquitectura. Aumenta `$testVersionCode` en las siguientes versiones de prueba.

```powershell
$project = 'NebulaReader/NebulaReader.csproj'
$testArtifacts = Join-Path (Get-Location) 'releases/pruebas'
$testVersionCode = 1013

dotnet clean $project -f net10.0-android -c Release --artifacts-path $testArtifacts `
  -p:ApplicationId=app.nebulareader.pruebas -p:RuntimeIdentifier=android-arm64 `
  -p:RuntimeIdentifiers=android-arm64

dotnet publish $project -f net10.0-android -c Release --artifacts-path $testArtifacts `
  -p:ApplicationId=app.nebulareader.pruebas `
  '-p:ApplicationTitle=Nebula Reader Pruebas' `
  "-p:ApplicationVersion=$testVersionCode" `
  -p:RuntimeIdentifier=android-arm64 -p:RuntimeIdentifiers=android-arm64 `
  -p:AndroidPackageFormats=apk -p:AndroidKeyStore=false

$testApk = Get-ChildItem -LiteralPath $testArtifacts -Recurse -Filter 'app.nebulareader.pruebas-Signed.apk' |
  Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $testApk) { throw 'No se generó la APK de pruebas firmada.' }
adb install -r $testApk.FullName
```

El nombre visible distinto ayuda a elegir la aplicación correcta cuando Android ofrece ambas para abrir un PDF o CBZ. Cada identificador tiene su propio directorio de datos y su propia base SQLite. Para retirar la prueba sin tocar la app de Play: `adb uninstall app.nebulareader.pruebas`.

## AAB oficial para Google Play

Antes de ejecutar este bloque, comprueba con `git status --short` y `git diff` que el árbol contiene las modificaciones validadas en la APK de pruebas; revisa también los archivos nuevos sin seguimiento. Compilar desde el mismo proyecto incluye los cambios fuente presentes, independientemente de que estén confirmados en Git. No copies la APK de pruebas ni sus carpetas `obj`/`bin` al canal oficial. Actualiza la versión oficial en `NebulaReader.csproj` y prepara **fuera del repositorio** el keystore de distribución, su alias y los archivos con las contraseñas. Para AAB usa el prefijo `file:`: la documentación de .NET MAUI indica que `env:` no está admitido al firmar este formato.

```powershell
$project = 'NebulaReader/NebulaReader.csproj'
$officialAabPath = Join-Path (Get-Location) 'NebulaReader/bin/Release/net10.0-android/android-arm64/publish/app.nebulareader.mobile-Signed.aab'
$keystore = 'C:\ruta\segura\upload.keystore'
$keyAlias = 'alias-de-subida'
$keyPasswordFile = 'C:\ruta\segura\clave-key.txt'
$storePasswordFile = 'C:\ruta\segura\clave-store.txt'

foreach ($path in @($keystore, $keyPasswordFile, $storePasswordFile)) {
  if (-not (Test-Path -LiteralPath $path)) { throw "Falta el archivo: $path" }
}

dotnet clean $project -f net10.0-android -c Release -p:RuntimeIdentifier=android-arm64
if ($LASTEXITCODE -ne 0) { throw 'Falló la limpieza oficial.' }

$publishStarted = Get-Date
dotnet publish $project -f net10.0-android -c Release `
  -p:RuntimeIdentifier=android-arm64 `
  -p:AndroidPackageFormats=aab -p:AndroidKeyStore=true `
  "-p:AndroidSigningKeyStore=$keystore" `
  "-p:AndroidSigningKeyAlias=$keyAlias" `
  "-p:AndroidSigningKeyPass=file:$keyPasswordFile" `
  "-p:AndroidSigningStorePass=file:$storePasswordFile"
if ($LASTEXITCODE -ne 0) { throw 'Falló la publicación oficial.' }

if (-not (Test-Path -LiteralPath $officialAabPath)) { throw 'No se generó el AAB oficial firmado.' }
$officialAab = Get-Item -LiteralPath $officialAabPath
if ($officialAab.LastWriteTime -lt $publishStarted) { throw 'El AAB encontrado es anterior a esta compilación.' }
$officialAab.FullName
(Get-FileHash -LiteralPath $officialAab.FullName -Algorithm SHA256).Hash
```

El `RuntimeIdentifier` del comando fija ARM64 como arquitectura principal, pero `RuntimeIdentifiers` del proyecto conserva ARM64, ARM de 32 bits, x86 y x86-64 en el AAB. En el dispositivo probado, omitir este parámetro produjo recortes visuales en la biblioteca. El SDK de Android borra `RuntimeIdentifier` si se declara junto a `RuntimeIdentifiers` dentro del proyecto; por eso debe pasarse al publicar.

Comprueba antes de subirlo que el paquete sea `app.nebulareader.mobile`, que el código de versión sea nuevo, que su certificado corresponda a la clave de subida registrada y que el AAB conserve `r8.json`, `proguard.map` y los símbolos nativos. Comprueba las cuatro ABI **dentro del AAB exacto que vas a subir**:

```powershell
Add-Type -AssemblyName System.IO.Compression
$bundle = [System.IO.Compression.ZipFile]::OpenRead($officialAab.FullName)
try {
  $actualAbi = @($bundle.Entries | Where-Object FullName -like 'base/lib/*/libxamarin-app.so' |
    ForEach-Object { $_.FullName.Split('/')[2] } | Sort-Object -Unique)
  $expectedAbi = @('arm64-v8a', 'armeabi-v7a', 'x86', 'x86_64') | Sort-Object
  if (@(Compare-Object $expectedAbi $actualAbi).Count -ne 0) {
    throw "ABI incorrectas en el AAB: $($actualAbi -join ', ')"
  }
  foreach ($entry in @('BUNDLE-METADATA/com.android.tools/r8.json',
                      'BUNDLE-METADATA/com.android.tools.build.obfuscation/proguard.map')) {
    if (-not $bundle.GetEntry($entry)) { throw "Falta $entry en el AAB" }
  }
  foreach ($abi in $expectedAbi) {
    $symbols = "BUNDLE-METADATA/com.android.tools.build.debugsymbols/$abi/libxamarin-app.so.sym"
    if (-not $bundle.GetEntry($symbols)) { throw "Faltan símbolos nativos para $abi" }
  }
} finally {
  $bundle.Dispose()
}
```

Anota la ruta y el SHA-256 del archivo validado y selecciona **esa misma ruta** al subirlo a Play Console. Hay otros AAB con el mismo nombre bajo `bin`; no los elijas por fecha de modificación. **Distribuye el AAB oficial mediante Google Play**; una APK local firmada con la clave de subida puede no actualizar la app instalada desde Play si Google firma las APK entregadas con otra clave.

Tras publicar, actualiza la aplicación desde Google Play en un dispositivo de prueba y confirma con `adb shell dumpsys package app.nebulareader.mobile` que `versionCode` y `versionName` corresponden al lanzamiento. La publicación en Play Console no instala de inmediato la actualización en el teléfono. Revisa visualmente la biblioteca de la versión instalada, incluidos el borde del panel «Tu biblioteca» y las tarjetas. En la verificación de 2.1.3, el teléfono todavía tenía 2.1.1 hasta pulsar **Actualizar** en Play Store.

## Comprobación de convivencia

```powershell
adb shell pm path app.nebulareader.mobile
adb shell pm path app.nebulareader.pruebas
```

Ambos comandos deben devolver una ruta después de instalar la APK de pruebas. Nunca ejecutes `adb uninstall app.nebulareader.mobile` como parte de una prueba. Si usas `--no-restore` o `--no-build`, repite **la misma** opción `--artifacts-path` del canal en todos los comandos dependientes.

En Windows el proyecto usa `WindowsPackageType=None`: se publican ejecutables desempaquetados, no dos paquetes instalables. Mantén sus carpetas de salida separadas; estas reglas de identificador y firma se refieren a Android.

## Referencias

- [Publicar y firmar una aplicación Android con .NET MAUI](https://learn.microsoft.com/en-us/dotnet/maui/android/deployment/publish-cli?view=net-maui-10.0).
- [Opción `--artifacts-path` de `dotnet publish`](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-publish#options).
