# Plan de mejoras de NebulaReader

Este plan organiza las próximas mejoras del lector CBZ, CBR y PDF para Android y Windows. La prioridad es facilitar la búsqueda de documentos, retomar la lectura y recuperar el acceso a los archivos sin perder el progreso.

La Entrega 1 está implementada; las entregas posteriores y las validaciones manuales indicadas siguen pendientes. Las funciones actuales y las instrucciones de instalación y compilación se describen en [Readme.md](Readme.md).

## Base actual

La aplicación utiliza .NET 10 y .NET MAUI, con arquitectura MVVM, CommunityToolkit.Mvvm, SkiaSharp, SharpCompress y SQLite. Ya dispone de biblioteca con portadas, agrupación de series, progreso local, lectura individual y doble, desplazamiento vertical, modo manga, filtros visuales y controles de zoom.

Las siguientes entregas amplían esta base. Los criterios de aceptación deben comprobarse antes de marcar una tarea como terminada.

## Prioridades

| Prioridad | Mejora | Beneficio |
| --- | --- | --- |
| Alta | Búsqueda y filtros | Encontrar documentos por título, serie, etiqueta, formato o estado. |
| Alta | Continuar leyendo | Retomar rápidamente los documentos abiertos recientemente. |
| Alta | Administración de biblioteca | Quitar entradas, cambiar su estado y corregir metadatos. |
| Alta | Recuperación de archivos inaccesibles | Restaurar rutas o permisos conservando el progreso. |
| Media | Marcadores y miniaturas | Guardar páginas y navegar visualmente por el documento. |
| Media | Preferencias por documento | Recordar la configuración adecuada para cada lectura. |
| Media | Importación con progreso y cancelación | Controlar el escaneo de colecciones grandes y conocer sus errores. |
| Media | Exportación e importación de datos | Respaldar y recuperar progreso, marcadores y preferencias. |

## Entrega 1: Buscar y continuar la lectura

### Búsqueda y filtros

- [x] Añadir búsqueda por título, serie y etiquetas.
- [x] Permitir filtrar por CBZ, CBR y PDF.
- [x] Permitir filtrar por estado: no leído, leyendo y completado.
- [x] Combinar búsqueda y filtros con la agrupación de series existente.
- [x] Ofrecer una acción para limpiar los filtros y un mensaje cuando no haya resultados.

**Criterios de aceptación:** la búsqueda encuentra documentos sin distinguir mayúsculas y minúsculas; los filtros se pueden combinar y limpiar; abrir un resultado mantiene el documento y su progreso correctos. Las series muestran únicamente los documentos que cumplen los criterios activos.

### Sección «Continuar leyendo»

- [x] Mostrar los documentos en lectura, ordenados por fecha de última apertura.
- [x] Incluir portada, página actual, total de páginas y porcentaje cuando el total sea conocido.
- [x] Añadir una acción directa para continuar cada documento.
- [x] Actualizar la sección al volver del lector.

**Criterios de aceptación:** abrir un documento desde esta sección restaura la página guardada; el orden refleja la última lectura; un documento completado deja de aparecer entre las lecturas pendientes. Si todavía no se conoce el total de páginas, no se muestra un porcentaje engañoso.

**Punto de partida:** [BookItem.cs](NebulaReader/Models/BookItem.cs) ya almacena la fecha de última lectura, el estado y el progreso. La presentación de la biblioteca se gestiona en [LibraryViewModel.cs](NebulaReader/ViewModels/LibraryViewModel.cs) y [LibraryPage.xaml](NebulaReader/Views/LibraryPage.xaml).

**Implementación:** las consultas se centralizan en [LibraryBrowseService.cs](NebulaReader/Services/LibraryBrowseService.cs). Se conserva la identidad de las series aunque un filtro deje un solo volumen, y las tarjetas usan el libro original para restaurar el progreso. «Continuar leyendo» es independiente de los filtros. El lector guarda también las aperturas sin cambio de página y el botón Atrás utiliza la salida que persiste el progreso antes de volver a la biblioteca. Las actualizaciones de portadas y metadatos no sobrescriben el progreso guardado.

**Validación automatizada:** [LibraryBrowseTests.cs](NebulaReader.Tests/LibraryBrowseTests.cs) cubre búsqueda, filtros, agrupación, lecturas recientes y presentación del progreso. [LibraryPersistenceTests.cs](NebulaReader.Tests/LibraryPersistenceTests.cs) comprueba la persistencia SQLite y que los documentos completados salen de las lecturas pendientes.

**Resultado de comprobación inicial:** 26 pruebas aprobadas en Release; compilaciones de Windows x64 en Release y Android ARM64 en Debug correctas. En esa comprobación Windows mostró cuatro advertencias de optimización de bindings XAML; Android Debug terminó sin advertencias ni errores.

**Validación manual pendiente:** completar las comprobaciones de búsqueda, navegación dentro de series, botón Continuar y retorno del lector en Windows y Android físico, incluidas pantallas pequeñas. El servicio de control de aplicaciones de Windows no estuvo disponible para realizar la comprobación visual. Las comprobaciones de filtros en Android descritas a continuación sí se realizaron.

### Corrección del cierre de los filtros en Android

**Error reproducido:** `ArgumentOutOfRangeException` en `Microsoft.Maui.Controls.Picker.UpdateSelectedItem`. En el teléfono se reprodujo seleccionando CBZ, después «Leyendo» sin coincidencias y finalmente «No leído», que volvía a mostrar libros.

**Causa:** los filtros estaban enlazados al contexto heredado de la cabecera de `CollectionView`. Al reemplazar los resultados y pasar de una vista vacía a una con documentos, el reciclado de la cabecera podía retirar ese contexto y vaciar las opciones mientras el selector nativo todavía procesaba el índice elegido. El control intentaba consultar una posición de una lista que ya no estaba disponible.

**Corrección:** en [LibraryPage.xaml](NebulaReader/Views/LibraryPage.xaml), los enlaces de `ItemsSource` y `SelectedIndex` de ambos filtros usan explícitamente `LibraryRootPage.ViewModel`. Esta propiedad tipada se asigna antes de inicializar el XAML. Así se conservan las opciones y la selección durante el reciclado de la cabecera, sin capturar ni silenciar la excepción.

**Verificación del 14 de noviembre de 2026:**

- [x] Generar la APK Android ARM64 en Release e instalarla en un Moto G Stylus 5G.
- [x] Con un CBZ de prueba en lectura, alternar «Completado» sin coincidencias y «Leyendo» con un resultado.
- [x] Alternar «No leído» sin coincidencias y «Leyendo» con un resultado.
- [x] Alternar CBR y PDF sin coincidencias con CBZ con un resultado.
- [x] Comprobar que el selector conserva el texto de la opción elegida después de cada transición.
- [x] Comprobar que «Limpiar filtros» restablece formato y estado, y recupera los resultados tras una búsqueda sin coincidencias.
- [x] Confirmar que el proceso de la aplicación permanece activo, sin reinicios ni nuevas excepciones en el buffer de cierres de logcat.
- [x] Volver a ejecutar las 26 pruebas automatizadas, todas aprobadas.
- [x] Compilar Windows x64 en Release sin errores. En esta primera comprobación hubo ocho advertencias de optimización de bindings XAML, resueltas posteriormente como se describe a continuación.

La APK corregida se encuentra en `releases/android/com.masterpet.nebulareader-Signed.apk`. El registro de la aplicación durante la verificación se guardó en `releases/logs/nebulareader-picker-corregido-20261114.log`. Estas salidas locales están excluidas de Git. Se utilizó un cómic de prueba porque la biblioteca aparecía vacía al iniciar la verificación de la nueva instalación.

### Corrección de las advertencias XAML

- [x] Activar `MauiEnableXamlCBindingWithSourceCompilation` en [NebulaReader.csproj](NebulaReader/NebulaReader.csproj).
- [x] Exponer el ViewModel tipado e inicializarlo antes de `InitializeComponent` en [LibraryPage.xaml.cs](NebulaReader/Views/LibraryPage.xaml.cs).
- [x] Indicar el tipo de la página en cada binding con `Source` y mantener el tipo `LibraryDisplayItem` para los parámetros de las tarjetas, eliminando los `x:DataType` nulos.
- [x] Compilar Windows x64 en Release y Android ARM64 en Debug con **cero advertencias y cero errores**, sin suprimir los avisos con `NoWarn`.

Si VS Code conserva diagnósticos de una compilación anterior, actualizar el panel ejecutando **Developer: Reload Window** desde la paleta de comandos. La solución de código sigue las instrucciones de [enlaces compilados de .NET MAUI](https://learn.microsoft.com/dotnet/maui/fundamentals/data-binding/compiled-bindings?view=net-maui-10.0#compile-bindings-that-define-the-source-property).

## Entrega 2: Administrar y recuperar la biblioteca

### Administración por documento

- [ ] Incorporar un menú de acciones en cada libro.
- [ ] Permitir quitar una entrada de la biblioteca conservando el archivo original.
- [ ] Permitir marcar como leído o no leído, con reglas coherentes para el progreso.
- [ ] Permitir corregir el título y la serie.
- [ ] Conservar las correcciones manuales durante posteriores extracciones de metadatos.

**Criterios de aceptación:** las modificaciones persisten al reiniciar; quitar una entrada no elimina el archivo; los cambios actualizan los filtros y las agrupaciones. La interfaz deja claro qué ocurre con el progreso al cambiar el estado.

### Recuperación de archivos inaccesibles

- [ ] Distinguir entre archivo ausente, permiso perdido y documento dañado.
- [ ] Ofrecer «Volver a localizar» para un archivo movido.
- [ ] Ofrecer «Conceder acceso» cuando falten permisos, especialmente en Android.
- [ ] Conservar la identidad del libro, su progreso y sus metadatos al actualizar la ubicación.
- [ ] Comprobar que el archivo elegido corresponde al documento esperado y evitar entradas duplicadas.

**Criterios de aceptación:** mover un documento y volver a localizarlo permite continuar la lectura; recuperar permisos no crea un libro nuevo; cancelar la recuperación conserva los datos existentes.

### Errores y reintentos de portadas

- [ ] Registrar los errores de generación de portadas con contexto suficiente para diagnosticarlos.
- [ ] Mostrar un estado comprensible en los libros cuya portada no pudo generarse.
- [ ] Ofrecer una acción «Reintentar».
- [ ] Mantener operativa la biblioteca aunque falle un archivo.

**Punto de partida:** [LibraryService.cs](NebulaReader/Services/LibraryService.cs) silencia algunos errores durante la generación de portadas. El comportamiento debe permitir diagnosticar y recuperar estos fallos sin interrumpir los demás documentos.

## Entrega 3: Marcadores y navegación visual

### Marcadores

- [ ] Persistir los marcadores asociados a cada documento.
- [ ] Añadir acciones para guardar y quitar el marcador de la página actual.
- [ ] Mostrar una lista de páginas marcadas y permitir abrirlas directamente.
- [ ] Preservar los marcadores al actualizar el progreso o recuperar una ruta.

**Punto de partida:** [ReadingProgress.cs](NebulaReader/Models/ReadingProgress.cs) declara `BookmarkedPages`, pero [LibraryDatabase.cs](NebulaReader/Services/LibraryDatabase.cs) no persiste esa lista. Es necesario completar el almacenamiento y la interfaz.

**Criterios de aceptación:** los marcadores sobreviven al reinicio, no se duplican y abren la página correspondiente. La actualización de la base de datos conserva la biblioteca y el progreso existentes.

### Cuadrícula de miniaturas

- [ ] Añadir una vista de miniaturas con número de página.
- [ ] Señalar la página actual y las páginas marcadas.
- [ ] Generar miniaturas bajo demanda y limitar su consumo de memoria.
- [ ] Cancelar las cargas pendientes al cerrar la vista o cambiar de documento.

**Criterios de aceptación:** seleccionar una miniatura abre la página correcta; recorrer un documento extenso no mantiene todas sus imágenes decodificadas en memoria; cerrar la cuadrícula libera el trabajo y los recursos que ya no se necesitan.

## Entrega 4: Preferencias por documento

- [ ] Guardar por libro el modo de vista, el ajuste, la dirección de lectura y el filtro visual.
- [ ] Usar las preferencias generales como valores iniciales para documentos sin personalización.
- [ ] Permitir restablecer un documento a los valores generales.
- [ ] Mantener las preferencias al volver a localizar el archivo.

**Criterios de aceptación:** un manga conserva la lectura invertida y un PDF conserva el ajuste al ancho sin afectar a otros libros. Las preferencias sobreviven al reinicio y el restablecimiento elimina la personalización del documento.

## Entrega 5: Importación controlada y respaldo

### Importación con progreso y cancelación

- [ ] Mostrar archivos procesados y total cuando se conozca.
- [ ] Separar el progreso de indexación del trabajo pendiente de portadas.
- [ ] Permitir cancelar el escaneo sin borrar los documentos ya importados.
- [ ] Continuar con el resto de la colección si falla un archivo.
- [ ] Presentar un resumen de documentos añadidos, existentes y fallidos.

**Criterios de aceptación:** la interfaz sigue respondiendo durante el escaneo; cancelar detiene el trabajo pendiente; repetir una importación no duplica entradas; los fallos se pueden identificar y reintentar.

### Exportación e importación de datos

- [ ] Definir un formato de respaldo versionado.
- [ ] Exportar progreso, marcadores, metadatos corregidos y preferencias.
- [ ] Validar el respaldo antes de modificar los datos locales.
- [ ] Definir cómo combinar datos existentes y resolver conflictos.
- [ ] Permitir asociar los documentos con sus ubicaciones en el equipo de destino.
- [ ] Explicar que el respaldo de datos no incluye los archivos CBZ, CBR o PDF originales.

**Criterios de aceptación:** una exportación seguida de una importación restaura los datos de lectura; un respaldo inválido no daña la biblioteca; las rutas de otro dispositivo se pueden recuperar sin perder el progreso.

## Validación antes de distribuir

Estas comprobaciones acompañan las entregas y deben completarse antes de una distribución amplia.

- [ ] Validar compilaciones Release en Windows y en un dispositivo Android físico.
- [ ] Comprobar apertura, cierre y reapertura de documentos con restauración del progreso.
- [ ] Probar cambios de orientación, zoom, modos de lectura y retorno desde segundo plano.
- [ ] Probar archivos corruptos, rutas movidas y permisos revocados.
- [ ] Medir tiempo de apertura, respuesta al cambiar de página y consumo máximo de memoria con documentos reales grandes, incluidos archivos superiores a 500 MB y PDF de más de 300 páginas.
- [ ] Verificar bibliotecas grandes, escaneos repetidos y cancelación de importaciones.
- [ ] Añadir pruebas automatizadas para filtros, persistencia de marcadores, recuperación de rutas e importación de respaldos a medida que se implementen.
- [ ] Configurar y conservar de forma segura una firma Android propia para distribuir actualizaciones.
- [ ] Actualizar [Readme.md](Readme.md) con las funciones efectivamente terminadas y los pasos de instalación vigentes.

La suite actual en [NebulaReader.Tests/](NebulaReader.Tests/) cubre partes del núcleo y escenarios de estrés. Debe complementarse con las pruebas de integración y de uso real anteriores.

## Orden recomendado

1. Búsqueda, filtros y «Continuar leyendo».
2. Administración de biblioteca, recuperación de archivos y reintento de portadas.
3. Marcadores y navegación mediante miniaturas.
4. Preferencias por documento.
5. Importación con progreso y respaldo de datos.

Cada entrega se considera terminada cuando cumple sus criterios de aceptación, pasa las comprobaciones pertinentes y su comportamiento queda documentado.
