$ErrorActionPreference = 'Stop'
$projectRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$outputDirectory = Join-Path $projectRoot 'releases'
$sourceApk = Join-Path $projectRoot 'android\app\build\outputs\apk\debug\app-debug.apk'
$destinationApk = Join-Path $outputDirectory 'NebulaReader-debug.apk'

Push-Location $projectRoot
try {
    npm run build
    if ($LASTEXITCODE -ne 0) { throw 'Falló la compilación web.' }
    npm run assets:android
    if ($LASTEXITCODE -ne 0) { throw 'Falló la generación de iconos Android.' }
    npx cap sync android
    if ($LASTEXITCODE -ne 0) { throw 'Falló la sincronización de Capacitor.' }
    Push-Location (Join-Path $projectRoot 'android')
    try {
        & .\gradlew.bat assembleDebug
        if ($LASTEXITCODE -ne 0) { throw 'Falló la compilación Android.' }
    } finally {
        Pop-Location
    }
    New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
    Copy-Item -LiteralPath $sourceApk -Destination $destinationApk -Force
    Write-Host "APK instalable generado en: $destinationApk"
} finally {
    Pop-Location
}
