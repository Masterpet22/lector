# NebulaReader

Lector de cómics y documentos **CBZ, CBR y PDF** para **Android y Windows**, desarrollado con .NET 10 y .NET MAUI. Permite organizar una biblioteca local, continuar la lectura desde la última página y adaptar el visor a cómics, manga y documentos.

## Funciones

- Apertura de archivos individuales e importación de colecciones mediante el escaneo de carpetas.
- Biblioteca con portadas, progreso de lectura y agrupación de series a partir de metadatos y títulos.
- Búsqueda por título, serie o etiqueta, con filtros combinables por formato y estado de lectura.
- Sección «Continuar leyendo» con lecturas recientes, página guardada y porcentaje cuando se conoce el total.
- Orden natural de páginas y títulos: `2` aparece antes que `10`.
- Lectura en página individual, doble página cuando el espacio lo permite y desplazamiento vertical continuo.
- Dirección de lectura de izquierda a derecha o de derecha a izquierda para manga.
- Ajuste a página completa, ancho o alto; zoom y desplazamiento de la imagen.
- Filtro nocturno, filtro sepia, pantalla completa y control de orientación en Android.
- Guardado local del progreso en SQLite y de las preferencias del lector.
- Renderizado con SkiaSharp, caché de páginas y precarga de páginas cercanas.

## Formatos compatibles

| Formato | Procesamiento |
| --- | --- |
| CBZ | Lectura del archivo ZIP con `System.IO.Compression`. |
| CBR | Lectura del archivo RAR con SharpCompress. |
| PDF | Renderizado mediante `PdfRenderer` en Android y `Windows.Data.Pdf` en Windows. |

## Instalación de las compilaciones locales

Los siguientes archivos se generan al publicar el proyecto; la carpeta `releases/` está excluida de Git y no se incluye al clonar el repositorio.

### Android

1. Copia `releases/android/com.masterpet.nebulareader-Signed.apk` al dispositivo Android ARM64.
2. Abre la APK y, si Android lo solicita, permite la instalación desde la aplicación con la que abriste el archivo.
3. Completa la instalación y abre **NebulaReader**.

La compilación local utiliza un certificado de desarrollo. Para distribuir actualizaciones con una identidad de firma propia o publicar en una tienda, configura un keystore de distribución y conserva sus credenciales fuera del repositorio.

### Windows

1. Extrae completamente `releases/NebulaReader-windows-x64.zip`, si está disponible, o copia la carpeta `releases/windows-x64/` completa.
2. Ejecuta `NebulaReader.exe` dentro de esa carpeta.

La publicación indicada abajo es autónoma para Windows x64 e incluye .NET y Windows App SDK. Mantén el ejecutable junto con todos los archivos y subcarpetas de la publicación.

## Primeros pasos

1. Pulsa **Abrir archivo** para importar y abrir un CBZ, CBR o PDF, o **Escanear carpeta** para añadir una colección.
2. En Android, concede acceso al archivo o a la carpeta seleccionada cuando lo solicite el selector del sistema.
3. Busca por título, serie o etiqueta y combina los selectores de formato y estado para encontrar documentos. **Limpiar filtros** restablece los tres criterios; los grupos de series muestran solo los libros coincidentes.
4. Selecciona una portada de la biblioteca para leer. Si aparece un grupo de serie, ábrelo para ver sus documentos. En **Continuar leyendo**, pulsa **Continuar** para abrir una lectura pendiente desde la página guardada; esta sección muestra las lecturas de toda la biblioteca, independientemente de los filtros.
5. Usa **Anterior**, **Siguiente** o la barra de progreso para navegar por el documento.
6. En **Configuración**, elige la disposición, el ajuste de página, la dirección de lectura y los filtros visuales.
7. Al volver del lector, se actualiza la biblioteca. Los documentos completados dejan de aparecer en **Continuar leyendo**. Si todavía no se conoce el total de páginas, se indica **Total desconocido** y se oculta el porcentaje.

### Controles en Windows

| Control | Acción |
| --- | --- |
| Flechas izquierda / derecha | Navegar en la dirección visual, respetando el modo manga. |
| `PageUp` / `PageDown` | Ir a páginas anteriores o siguientes. |
| Flechas arriba / abajo | Desplazar la lectura vertical o la imagen cuando admite desplazamiento. |
| Barra espaciadora | Avanzar en el modo vertical continuo. |
| Rueda del ratón | Desplazar la vista o cambiar de página, según el modo y el zoom. |
| `Ctrl` + rueda | Acercar o alejar la página. |
| Clic y arrastre | Mover la imagen ampliada o la lectura vertical. |
| `F11` | Activar o desactivar la pantalla completa. |
| `Esc` | Salir de pantalla completa; si ya está en ventana, volver a la biblioteca. |

En Android, utiliza los gestos táctiles del visor para ampliar y desplazar la página.

## Desarrollo

### Requisitos

- **.NET SDK 10**.
- Cargas de trabajo **MAUI para Android** y, en Windows, **MAUI para Windows**.
- **Android SDK** y **Java JDK** compatibles con la carga de trabajo Android instalada.
- Para compilar Windows: un equipo Windows y el **Windows SDK** requerido por el proyecto.
- Acceso a NuGet para restaurar dependencias.

El proyecto declara Android API 21 y Windows 10, versión 1809, como mínimos de plataforma. Los destinos de compilación son `net10.0-android` y `net10.0-windows10.0.19041.0`; este último solo se agrega al compilar desde Windows. La compatibilidad de ejecución también depende de los requisitos de las bibliotecas nativas utilizadas.

Consulta [Herramientas Android](HERRAMIENTAS_ANDROID.md) para más detalles del entorno.

### Preparar el entorno

Ejecuta desde la raíz del repositorio:

```powershell
dotnet --info
dotnet workload list
```

Si faltan las cargas de trabajo, instala las correspondientes a la plataforma:

```powershell
dotnet workload install maui-android
# En Windows, para generar la aplicación de escritorio:
dotnet workload install maui-windows
```

### Generar una APK Android ARM64

```powershell
dotnet publish NebulaReader/NebulaReader.csproj -f net10.0-android -p:TargetFrameworks=net10.0-android -r android-arm64 -c Release -p:AndroidPackageFormats=apk -o releases/android
```

El paquete instalable se genera como `releases/android/com.masterpet.nebulareader-Signed.apk`. Esta configuración compila exclusivamente para ARM64.

### Generar el ejecutable Windows x64

Ejecuta en Windows:

```powershell
dotnet publish NebulaReader/NebulaReader.csproj -f net10.0-windows10.0.19041.0 -p:TargetFrameworks=net10.0-windows10.0.19041.0 -c Release -r win-x64 --self-contained true -p:WindowsAppSDKSelfContained=true -p:WindowsPackageType=None -o releases/windows-x64
```

La salida incluye `releases/windows-x64/NebulaReader.exe` y sus dependencias. Para empaquetar la carpeta completa:

```powershell
Compress-Archive -Path releases/windows-x64/* -DestinationPath releases/NebulaReader-windows-x64.zip -Force
```

Los comandos de publicación restauran los paquetes necesarios. Compila las plataformas por separado con los destinos y las arquitecturas indicados.

### Pruebas

```powershell
dotnet test NebulaReader.Tests/NebulaReader.Tests.csproj
```

La suite utiliza xUnit e incluye pruebas de orden natural, lectura CBZ, extracción de metadatos, agrupación de biblioteca, generación de portadas y gestión de caché. Comprueba además la búsqueda y los filtros combinados, la pertenencia a series, el orden de lecturas recientes, los totales desconocidos y la persistencia SQLite del progreso frente a actualizaciones tardías de portadas. También contiene escenarios con un CBZ de 320 páginas y liberación de bitmaps 4K. Estas pruebas no sustituyen la validación del visor en Android y Windows con documentos reales.

## Estructura del proyecto

| Ruta | Contenido |
| --- | --- |
| [NebulaReader/](NebulaReader/) | Aplicación .NET MAUI. |
| [Models/](NebulaReader/Models/) | Modelos de libros, progreso y preferencias del visor. |
| [Views/](NebulaReader/Views/) | Pantallas XAML de biblioteca, lector y configuración. |
| [ViewModels/](NebulaReader/ViewModels/) | Estado y comandos de las pantallas mediante MVVM. |
| [Services/](NebulaReader/Services/) | Lectura de formatos, biblioteca, persistencia, metadatos y caché. |
| [Platforms/](NebulaReader/Platforms/) | Integraciones nativas de Android y Windows. |
| [Resources/](NebulaReader/Resources/) | Iconos, imágenes, fuentes y estilos. |
| [NebulaReader.Tests/](NebulaReader.Tests/) | Pruebas del núcleo y escenarios de estrés. |

Las dependencias principales son CommunityToolkit.Mvvm, SharpCompress, SkiaSharp y sqlite-net-pcl. Las versiones se declaran en [NebulaReader.csproj](NebulaReader/NebulaReader.csproj).

## Datos locales

La biblioteca y el progreso se almacenan en `nebulareader_library.db3`, dentro del directorio de datos de la aplicación. Las portadas se guardan en el directorio de caché y las preferencias se conservan con el almacenamiento de preferencias de MAUI. Mantén disponibles los archivos originales y sus permisos de acceso para poder volver a abrirlos.

## Documentación adicional

- [Configuración y herramientas para Android](HERRAMIENTAS_ANDROID.md).
- [Plan de desarrollo y seguimiento de fases](PLAN_DE_DESARROLLO.md).

La configuración vigente de compilación se encuentra en [NebulaReader.csproj](NebulaReader/NebulaReader.csproj). El plan distingue las mejoras implementadas de las tareas y validaciones pendientes.
