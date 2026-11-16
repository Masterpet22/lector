# Nebula Reader

Lector de cómics y documentos **CBZ, CBR y PDF** para **Android y Windows**, desarrollado con .NET 10 y .NET MAUI. Permite organizar una biblioteca local, continuar la lectura desde la última página y adaptar el visor a cómics, manga y documentos.

## Funciones

- Apertura de archivos individuales e importación de colecciones mediante el escaneo de carpetas.
- Escaneo de carpetas con progreso, cancelación, resumen de archivos añadidos, existentes y fallidos, y reintento de fallos. Las portadas pendientes se muestran por separado.
- Exportación e importación de respaldos JSON versionados con progreso, marcadores, metadatos corregidos, preferencias por documento y ajustes generales.
- Biblioteca con portadas, progreso de lectura y agrupación de series a partir de metadatos y títulos.
- Búsqueda por título, serie o etiqueta, con filtros combinables por formato y estado de lectura.
- Sección «Continuar leyendo» con lecturas recientes, página guardada y porcentaje cuando se conoce el total.
- Acciones por documento para corregir título y serie, cambiar el estado, quitar la entrada, recuperar el acceso y reintentar una portada fallida.
- Marcadores de páginas por documento y navegación mediante una lista de marcadores o una cuadrícula de miniaturas.
- Ajustes de vista, escala, dirección de lectura y filtro visual por documento, con opción para volver a los valores generales.
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

1. Copia `releases/android/app.nebulareader.mobile-Signed.apk` al dispositivo Android ARM64.
2. Abre la APK y, si Android lo solicita, permite la instalación desde la aplicación con la que abriste el archivo.
3. Completa la instalación y abre **Nebula Reader**.

La compilación local utiliza un certificado de desarrollo. Para distribuir actualizaciones con una identidad de firma propia o publicar en una tienda, configura un keystore de distribución y conserva sus credenciales fuera del repositorio.

### Windows

1. Extrae completamente `releases/NebulaReader-windows-x64.zip`, si está disponible, o copia la carpeta `releases/windows-x64/` completa.
2. Ejecuta `NebulaReader.exe` dentro de esa carpeta.

La publicación indicada abajo es autónoma para Windows x64 e incluye .NET y Windows App SDK. Mantén el ejecutable junto con todos los archivos y subcarpetas de la publicación.

## Primeros pasos

1. Pulsa **Añadir** y elige **Abrir archivo** para importar y abrir un CBZ, CBR o PDF, o **Escanear carpeta** para añadir una colección.
2. En Android, concede acceso al archivo o a la carpeta seleccionada cuando lo solicite el selector del sistema.
3. En **Biblioteca**, busca por título, serie o etiqueta y combina los filtros de formato y estado. En una pantalla estrecha, pulsa **Filtrar** para mostrarlos; en una ventana amplia aparecen en el panel lateral. La pantalla enumera los criterios activos y **Limpiar filtros** los restablece. Los grupos de series muestran solo los libros coincidentes.
4. En **Biblioteca**, pulsa **Leer** en un documento o selecciona su portada. Si aparece un grupo de serie, pulsa **Abrir serie** para ver sus documentos; también puedes seleccionarlo con el teclado. En **Inicio**, pulsa **Continuar** para retomar una lectura desde la página guardada. Las lecturas recientes pasan a nuevas filas según el ancho disponible y son independientes de los filtros de Biblioteca.
5. Usa **Anterior**, **Siguiente** o la barra de progreso para navegar. El botón **Guardar marcador** o **Quitar marcador** cambia la marca de la página actual. En modo manga, los botones indican la acción correspondiente a cada lado.
   Pulsa **Ir a página** y cambia entre **Marcadores** y **Miniaturas**; toca una página para abrirla. El punto indica la página actual y la estrella una página marcada. **← Volver** cierra el panel.
   **Opciones de lectura** reúne disposición, ajuste, dirección, filtro, rotación, pantalla completa y restablecimiento. Los cambios se guardan para el libro abierto; el panel indica si usa valores generales o una personalización. Al restablecer, vuelve a aplicar los valores generales. En Android, toca el centro de la página para mostrar u ocultar los controles; en Windows aparecen al mover el ratón. Se ocultan tras unos segundos de inactividad.
6. En **Gestión → Configuración**, elige los valores generales de disposición, ajuste de página, dirección de lectura y filtros visuales para los libros sin ajustes propios.
7. Al volver del lector, se actualiza la biblioteca. Los documentos completados dejan de aparecer en **Continuar leyendo**. Si todavía no se conoce el total de páginas, se indica **Total desconocido** y se oculta el porcentaje.
8. Pulsa **Acciones** en un documento para editar su título o serie. **Marcar como leído** guarda la última página como progreso y requiere conocer el total; **Marcar como no leído** vuelve a la página 1. **Quitar de la biblioteca** elimina la entrada y su progreso, pero conserva el archivo original.
9. Si se movió un archivo o se perdió el permiso, usa **Volver a localizar / Conceder acceso** y elige el documento mediante el selector. Se conservan sus datos de lectura. La aplicación rechaza una ruta ya importada y comprueba el formato, el número de páginas y, cuando existe, la huella de la primera página. Si falla una portada, el estado aparece en la tarjeta y **Reintentar portada** vuelve a generarla.
10. Durante **Escanear carpeta**, Biblioteca muestra el avance en una franja compacta. Pulsa **Detalles** para abrir **Gestión**, donde puedes ver las portadas pendientes y cancelar el escaneo. Al terminar, revisa allí el resumen y usa **Reintentar fallidos** si hay archivos con error. **Cancelar escaneo** conserva lo ya añadido.
11. En **Gestión**, pulsa **Exportar datos** para guardar un respaldo JSON. En otro dispositivo, pulsa **Importar respaldo** y después asocia cada documento pendiente desde **Acciones → Volver a localizar / Conceder acceso**. El respaldo contiene datos de lectura, pero no los archivos CBZ, CBR o PDF originales: cópialos también al destino.

### Combinación de respaldos

El formato actual es `nebula-reader-data` versión 1. La importación valida todo el archivo antes de modificar la biblioteca y aplica los cambios en una transacción. Si ya existe un libro con el mismo ID, conserva su ruta local; gana el progreso con fecha de lectura más reciente y, en empate, el local. Se conservan las correcciones manuales locales de título y serie, se unen los marcadores y se mantienen las preferencias por documento locales cuando existen. Los ajustes generales del respaldo se aplican al importar. Los libros nuevos aparecen pendientes de asociación con sus archivos originales.

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
| `Esc` | Cerrar un panel abierto; de otro modo, salir de pantalla completa o volver a la biblioteca. |

En Android, utiliza los gestos táctiles del visor para ampliar y desplazar la página.

## Desarrollo

### Requisitos

- **.NET SDK 10**.
- Cargas de trabajo **MAUI para Android** y, en Windows, **MAUI para Windows**.
- **Android SDK** y **Java JDK** compatibles con la carga de trabajo Android instalada.
- Para compilar Windows: un equipo Windows y el **Windows SDK** requerido por el proyecto.
- Acceso a NuGet para restaurar dependencias.

El proyecto declara Android API 21 y Windows 10, versión 1809, como mínimos de plataforma. Los destinos de compilación son `net10.0-android` y `net10.0-windows10.0.19041.0`; este último solo se agrega al compilar desde Windows. La compatibilidad de ejecución también depende de los requisitos de las bibliotecas nativas utilizadas.

Consulta [Herramientas de desarrollo](HERRAMIENTAS_DE_DESARROLLO.md) para más detalles del entorno.

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

El paquete instalable se genera como `releases/android/app.nebulareader.mobile-Signed.apk`. Esta configuración compila exclusivamente para ARM64.

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
