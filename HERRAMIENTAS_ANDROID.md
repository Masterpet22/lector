# Herramientas para trabajar en la rama `android`

La rama contiene **NebulaReader**, una aplicación .NET MAUI dirigida a Android
(`net10.0-android`). No es un proyecto Android/Gradle independiente: se compila
desde `NebulaReader.slnx` y `NebulaReader/NebulaReader.csproj`.

## Requisitos comunes

- **Git**: clonar el repositorio, cambiar a la rama `android` y gestionar cambios.
- **.NET SDK 10**: compilar la aplicación y ejecutar las pruebas.
- **Cargas de trabajo .NET para Android y MAUI**: proporcionan los componentes
  de compilación Android usados por .NET MAUI.
- **Android SDK**: instalar con Android Studio o el SDK Manager. Se necesitan
  las plataformas y herramientas de compilación requeridas por la carga de
  trabajo .NET instalada, además de **Android SDK Platform-Tools** (`adb`).
- **Java JDK** compatible con la versión instalada de .NET para Android.
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

## Para publicar

Se requiere además un certificado/keystore de firma Android y su configuración
segura para generar una versión de distribución, independientemente del entorno
de edición. No se deben incluir claves ni contraseñas en el repositorio.
