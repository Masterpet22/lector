# Plan de desarrollo de la interfaz de Nebula Reader

## Objetivo y punto de partida

Organizar la aplicación para que sea fácil retomar una lectura, encontrar un documento y leerlo tanto en Android como en Windows. La interfaz debe adaptarse al espacio disponible: una ventana estrecha de Windows necesita una presentación compacta y una tableta Android puede aprovechar una distribución amplia.

La búsqueda, los filtros, la agrupación de series, «Continuar leyendo», la importación, los respaldos, los marcadores y las preferencias por documento ya funcionan. Este plan reorganiza su presentación sin cambiar el significado de los datos ni el comportamiento de lectura. La pantalla principal se encuentra en [LibraryPage.xaml](NebulaReader/Views/LibraryPage.xaml), su adaptación actual en [LibraryPage.xaml.cs](NebulaReader/Views/LibraryPage.xaml.cs), el visor en [ReaderPage.xaml](NebulaReader/Views/ReaderPage.xaml) y la navegación inicial en [AppShell.xaml](NebulaReader/AppShell.xaml).

## Estructura propuesta

| Área | Contenido principal | Acciones secundarias |
| --- | --- | --- |
| Inicio | Lecturas recientes con progreso y acceso a la colección | Ninguna acción administrativa en el bloque principal |
| Biblioteca | Búsqueda, filtros, series y documentos | Añadir archivo, escanear carpeta y acciones de cada documento |
| Gestión | Estado de importación, fallos, reintentos y respaldos | Explicación de qué datos incluye el respaldo |
| Configuración | Preferencias generales de lectura y ventana | Valores predeterminados para documentos sin personalización |
| Lector | Página, progreso y navegación | Opciones de lectura, marcadores y miniaturas |

«Inicio» y «Biblioteca» son destinos de navegación. «Añadir» y «Filtrar» son acciones dentro de Biblioteca. Gestión y Configuración quedan accesibles desde un menú secundario; no necesitan competir con las lecturas recientes ni con las portadas.

En Android compacto, usar una barra inferior para Inicio y Biblioteca. En pantallas amplias y Windows, usar un panel lateral para esos destinos y el acceso a Gestión y Configuración. La navegación debe responder al ancho de la ventana, no únicamente al tipo de dispositivo. El lector mantiene una ruta clara para volver a la biblioteca y puede ocupar toda la pantalla durante la lectura.

## Entrega 1: Simplificar la biblioteca

**Prioridad: alta.**

- [x] Sustituir la cabecera extensa por un título compacto, búsqueda visible y acción principal **Añadir**.
- [x] Agrupar **Abrir archivo** y **Escanear carpeta** dentro de Añadir, conservando ambos flujos actuales.
- [x] Mover exportación, importación de respaldo y sus explicaciones a Gestión.
- [x] Mostrar el progreso de un escaneo activo en una franja compacta con acceso a su detalle; conservar cancelación, resumen de fallos y reintento en Gestión.
- [x] Mantener «Continuar leyendo» como primer bloque de contenido de la pantalla principal, antes de la búsqueda y la colección. Su destino independiente se añadirá en la Entrega 2.
- [x] Mantener las acciones por documento junto a cada tarjeta; distinguir visualmente abrir el libro de administrar su entrada.

**Criterios de aceptación:** en un teléfono vertical se ve al menos el comienzo de las portadas o las lecturas recientes sin atravesar bloques administrativos; abrir un archivo y escanear una carpeta siguen disponibles en dos pasos como máximo desde Biblioteca; un escaneo activo muestra su estado y puede cancelarse; exportar e importar respaldos siguen accesibles y explican que no incluyen los documentos originales.

**Archivos de partida:** [LibraryPage.xaml](NebulaReader/Views/LibraryPage.xaml), [LibraryPage.xaml.cs](NebulaReader/Views/LibraryPage.xaml.cs) y [LibraryViewModel.cs](NebulaReader/ViewModels/LibraryViewModel.cs).

**Estado de la implementación:** la biblioteca muestra Añadir y Gestión en una cabecera compacta. Gestión reutiliza el estado de la biblioteca para mostrar el detalle del escaneo, sus fallos, los respaldos y el acceso a Configuración. Las tarjetas individuales distinguen **Leer** de **Acciones**. Las compilaciones Debug de Windows y Android terminaron sin advertencias ni errores y pasaron las 32 pruebas automatizadas.

**Validación visual pendiente:** comprobar en un teléfono Android vertical y horizontal que se ven las lecturas recientes o el comienzo de la colección, abrir ambos tipos de importación desde Añadir y confirmar el avance y la cancelación de un escaneo real. Repetir los flujos en Windows con ventana estrecha. El canal de control visual de Windows devolvió `Computer Use native pipe is unavailable` durante esta entrega.

## Entrega 2: Navegación y búsqueda adaptables

**Prioridad: alta.**

- [x] Incorporar los destinos Inicio y Biblioteca a la navegación principal, conservando la ubicación del usuario al volver del lector.
- [x] Presentar búsqueda y filtros de formato y estado en una fila compacta cuando quepan; en ancho reducido, abrir los filtros en un panel con indicador de criterios activos y acción **Limpiar**.
- [x] En ventanas amplias, mostrar los filtros en una columna lateral y la colección en el área principal.
- [x] Calcular columnas de portadas según el ancho real del área de contenido, incluyendo el espacio ocupado por el panel lateral.
- [x] Conservar la identidad de las series y el resultado de la búsqueda al entrar y salir de un grupo.
- [x] Comprobar en un dispositivo Android que los selectores nativos conservan su origen de datos al cambiar de filtros y de vista.

**Criterios de aceptación:** la búsqueda, los filtros combinados y **Limpiar** producen los mismos resultados actuales; se distingue qué filtros están activos; una ventana de Windows reducida cambia a una distribución compacta sin cortar controles; al redimensionar o rotar se preservan búsqueda, selección y posición de navegación; ninguna tarjeta queda bajo el panel lateral.

**Archivos de partida:** [AppShell.xaml](NebulaReader/AppShell.xaml), [LibraryPage.xaml](NebulaReader/Views/LibraryPage.xaml), [LibraryPage.xaml.cs](NebulaReader/Views/LibraryPage.xaml.cs) y [LibraryBrowseService.cs](NebulaReader/Services/LibraryBrowseService.cs).

**Estado de la implementación:** Inicio y Biblioteca comparten el mismo `LibraryViewModel` dentro de la pantalla principal, para conservar búsqueda, filtros, serie activa y destino al volver del lector. La navegación aparece abajo en ancho reducido y a la izquierda en ventanas amplias. Los filtros usan el panel lateral, una fila horizontal o un panel plegable según el ancho; sus enlaces apuntan a la página para mantener estables las opciones durante el reciclado de la cabecera. La cuadrícula resta el ancho del panel antes de calcular las columnas y recuerda el primer elemento visible al cambiar de destino o volver del lector. Pasaron las 32 pruebas automatizadas; Windows y Android compilaron en Debug.

**Validación Android realizada:** instalada la APK Release en un teléfono físico. Se comprobó Inicio, el paso a Biblioteca, la apertura del panel y los selectores nativos, la secuencia CBZ → Leyendo → No leído → Limpiar con resultados vacíos y la conservación del filtro CBZ al pasar por Inicio y volver. No hubo cierre de la aplicación y los selectores conservaron sus opciones.

**Ajuste adaptable posterior:** las columnas de Biblioteca se calculan con el ancho medido del contenido y reservan espacio para el desplazamiento vertical. Inicio distribuye las lecturas recientes en varias filas; la cabecera, la búsqueda y las acciones de las tarjetas cambian de posición cuando el ancho se reduce. Las miniaturas y los controles del lector también reajustan sus columnas y filas al redimensionar. Se añadieron pruebas para comprobar que las columnas y tarjetas recientes caben dentro de anchos de 200 a 2000 DIP.

**Densidad de la colección:** se intentó reducir las columnas mínimas de las vistas de iconos y adaptar la altura de las portadas. La captura de Windows del 19 de noviembre de 2026 demuestra que la distribución todavía falla: con solo dos grupos visibles, una tarjeta puede ocupar casi todo el ancho de la colección. La Entrega 5 sustituye este criterio incompleto por límites de ancho y una validación visual explícita.

**Validación manual pendiente:** probar la transición de resultados vacíos a libros visibles con una biblioteca no vacía, la rotación y el retorno desde el lector; revisar Windows con ventanas estrecha, mediana y maximizada. El teléfono usado en la comprobación tenía la biblioteca vacía y el canal de control visual de Windows sigue sin conectarse.

## Entrega 3: Controles del lector por frecuencia de uso

**Prioridad: alta.**

- [x] Dejar en la interfaz inmediata **Volver**, página actual, anterior/siguiente, progreso y marcador.
- [x] Reunir disposición, ajuste de página, dirección de lectura, filtro visual y restablecimiento en **Opciones de lectura**. Mostrar claramente si se editan ajustes de este libro o valores generales.
- [x] Unificar **Marcadores** y **Miniaturas** en un panel **Ir a página** con dos vistas internas.
- [x] En Android, mostrar los controles al tocar la página y ocultarlos durante la lectura; mantener zonas táctiles suficientes y evitar que tapen demasiado contenido.
- [x] En Windows, revelar controles al mover el ratón y permitir un panel lateral de opciones que pueda cerrarse; conservar teclado, rueda, arrastre y F11.
- [x] Mantener la navegación coherente con la dirección manga y con los modos de página individual, doble y desplazamiento continuo.

**Criterios de aceptación:** la página sigue siendo el elemento dominante en teléfono y escritorio; las acciones frecuentes no requieren recorrer una barra horizontal; todas las preferencias actuales siguen disponibles y se guardan por libro; los marcadores y las miniaturas abren la página correcta; la lectura sigue siendo operable con tacto, ratón y teclado.

**Archivos de partida:** [ReaderPage.xaml](NebulaReader/Views/ReaderPage.xaml), [ReaderPage.xaml.cs](NebulaReader/Views/ReaderPage.xaml.cs) y [ReaderViewModel.cs](NebulaReader/ViewModels/ReaderViewModel.cs).

**Estado de la implementación:** el HUD muestra Volver, Ir a página, Opciones de lectura, progreso, navegación y marcador sin desplazamiento horizontal. Ir a página ofrece Marcadores y Miniaturas; Opciones de lectura ocupa un panel lateral cerrable y distingue los ajustes del libro de los valores generales. Los controles se ocultan tras cinco segundos de inactividad; el toque central y el movimiento del ratón los muestran de nuevo. Los botones de navegación indican la acción de cada lado en modo manga. Pasaron las 32 pruebas automatizadas y Android y Windows compilaron sin advertencias.

**Validación manual pendiente:** comprobar en un documento real la selección de marcadores y miniaturas, las tres disposiciones y la dirección manga en Android y Windows, además de la ocupación del HUD en teléfono horizontal y ventana estrecha. No había un dispositivo Android conectado durante esta entrega.

## Entrega 4: Coherencia visual y accesibilidad

**Prioridad: media.**

- [x] Definir componentes reutilizables para cabeceras, botones de acción, filtros, tarjetas y paneles.
- [x] Mantener nombres y ubicación consistentes para **Añadir**, **Filtrar**, **Ir a página**, **Opciones de lectura** y **Volver**.
- [x] Revisar jerarquía tipográfica, contraste, estados de foco y selección, y áreas táctiles en las dos plataformas.
- [x] Evitar que la información dependa solo de color o iconos: mostrar texto para progreso, filtros activos, errores de importación y estado de acceso a un archivo.
- [x] Ajustar Configuración para que sus grupos sean fáciles de recorrer en teléfono y no se estiren excesivamente en ventanas grandes.

**Criterios de aceptación:** cada acción tiene una etiqueta comprensible; el foco es visible al navegar con teclado; los controles importantes son utilizables con toque; texto ampliado y ventanas estrechas no producen solapamientos ni recortes.

**Archivos de partida:** [Styles.xaml](NebulaReader/Resources/Styles/Styles.xaml), [Colors.xaml](NebulaReader/Resources/Styles/Colors.xaml) y [SettingsPage.xaml](NebulaReader/Views/SettingsPage.xaml).

**Estado de la implementación:** los estilos compartidos reúnen paneles, tarjetas, títulos, filtros y botones. Los botones, selectores y la búsqueda muestran un estado de foco; se elevó el contraste del texto atenuado. Biblioteca muestra los criterios activos en texto, conserva el estado en un pie de hasta dos líneas y ofrece **Abrir serie** como botón accesible por teclado. Configuración distingue los valores generales, limita su ancho y reduce márgenes en ventanas estrechas; Gestión usa el mismo límite. En el lector, las acciones superiores pasan a dos filas en ancho compacto para mantener sus nombres completos. Pasaron las 32 pruebas automatizadas; Android compiló sin advertencias y Windows con una advertencia `NU1801` por falta de acceso a NuGet.

**Validación visual pendiente:** revisar foco de teclado, texto ampliado, lectores de pantalla y áreas táctiles en Windows y Android, además de la rotación y las ventanas estrechas con documentos reales. El canal de control visual de Windows devolvió `Computer Use native pipe is unavailable` y no había un dispositivo Android conectado durante la verificación.

## Entrega 5: Tarjetas compactas y cuadrícula estable

**Prioridad: alta. Estado: implementación realizada; validación visual pendiente.** La biblioteca debe aprovechar el espacio para mostrar más documentos, sin estirar una tarjeta cuando hay pocos resultados ni recortarla al reducir la ventana. La captura aportada muestra un grupo de tres documentos ocupando casi todo el ancho disponible en la vista **Iconos medianos**.

### Diseño objetivo

| Vista | Ancho objetivo por tarjeta | Distribución |
| --- | --- | --- |
| Iconos muy grandes | 240–280 DIP | Columnas adaptables, alineadas a la izquierda |
| Iconos grandes | 210–240 DIP | Columnas adaptables, alineadas a la izquierda |
| Iconos medianos | 180–210 DIP | Tantas columnas como quepan sin reducir cada tarjeta por debajo de 180 DIP |
| Iconos pequeños / Iconos | 96–160 DIP según la vista | Más columnas, con título abreviado y acciones accesibles |
| Lista / Detalles / Contenido | Fila del ancho disponible | La portada y el texto tienen regiones separadas; la fila no se presenta como una tarjeta de portada gigante |

En una pantalla estrecha que solo admita una columna, la tarjeta puede ocupar el ancho útil para conservar controles legibles. En pantallas amplias, el ancho máximo de cada tarjeta se mantiene aunque solo exista un resultado; los resultados comienzan a la izquierda y el espacio sobrante queda al final de la fila. La separación entre tarjetas será constante, aproximadamente 8–12 DIP.

### Trabajo de implementación

- [x] Medir el ancho **real** de la zona desplazable después de restar panel lateral, márgenes y barra de desplazamiento. Registrar el ancho, el modo de vista y el número de columnas en la salida de depuración para detectar medidas iniciales o antiguas.
- [x] Centralizar el cálculo de columnas y ancho de tarjeta en una función verificable: elegir el mayor número de columnas que respete el ancho mínimo de la vista, limitar el ancho de cada tarjeta a su máximo y no asignar nunca una tarjeta más ancha que su celda.
- [x] Hacer que el primer renderizado, el cambio de vista, el redimensionamiento y la rotación usen el mismo cálculo. Evitar que el valor inicial de `Span`, una medida de la ventana anterior o el número de resultados determine el ancho final de la tarjeta.
- [x] Sustituir `GridItemsLayout`, que puede conservar tamaños incorrectos al redimensionar en Windows, por filas de tarjetas con ancho acotado dentro de un `CollectionView` vertical. Cada fila se virtualiza y sus tarjetas empiezan a la izquierda.
- [x] Ajustar las plantillas de tarjeta: portada individual completa con su proporción original; mosaico de grupo compacto sin recortar portadas ni dejar celdas vacías cuando hay menos de cuatro imágenes; título de una línea con `…`, numeración y formato en espacios propios; botones dentro de los límites de la tarjeta.
- [x] Mantener búsqueda, filtros, orden, agrupación, progreso, acciones y posición de desplazamiento al cambiar la distribución. No modificar los títulos ni otros datos guardados para resolver un problema de presentación.
- [ ] Revisar visualmente en Windows y Android los anchos, el ajuste de los controles y la posición de desplazamiento con bibliotecas reales. La verificación del código por sí sola no cierra esta entrega.

**Criterios de aceptación:** en una zona útil de unos 1060 DIP, **Iconos medianos** presenta alrededor de cinco columnas de hasta 210 DIP; uno o dos resultados siguen siendo tarjetas compactas y contiguas al comienzo de la fila. Al reducir esa zona a unos 750 DIP, aparecen aproximadamente cuatro columnas; a unos 500 DIP, dos; y en un teléfono estrecho, una. Ninguna tarjeta, portada, insignia, título o botón queda fuera de su celda. No aparece desplazamiento horizontal de la biblioteca. Las portadas conservan su proporción y muestran la imagen completa.

**Verificación:** probar bibliotecas con 1, 2 y más de 20 elementos; documentos individuales y grupos de 1 a 4 portadas; títulos largos con y sin numeración; búsqueda que deja un solo resultado; cambio entre las ocho vistas; ventana de Windows maximizada, mediana y estrecha; Android vertical y horizontal; texto del sistema ampliado. Comparar capturas antes y después de redimensionar, además de ejecutar pruebas del cálculo de columnas y las compilaciones de ambas plataformas. La entrega solo se cierra tras revisar visualmente los casos de Windows y Android.

**Archivos de partida:** [LibraryPage.xaml](NebulaReader/Views/LibraryPage.xaml), [LibraryPage.xaml.cs](NebulaReader/Views/LibraryPage.xaml.cs), [LibraryViewModel.cs](NebulaReader/ViewModels/LibraryViewModel.cs), [ResponsiveLayout.cs](NebulaReader/Services/ResponsiveLayout.cs) y [ResponsiveLayoutTests.cs](NebulaReader.Tests/ResponsiveLayoutTests.cs).

**Estado de la implementación:** las vistas de iconos usan filas virtualizadas con tarjetas de ancho calculado, agrupadas según el ancho medido de la colección. Los modos Lista, Detalles y Contenido conservan una fila por documento. El mosaico de grupo adapta su composición a una, dos, tres o cuatro portadas disponibles. La salida Debug registra ancho, vista, columnas y ancho de tarjeta. Windows publicó un EXE y un ZIP x64; Android publicó un APK Release ARM64 con firma verificada. Pasaron 61 pruebas, incluidas las de anchos, agrupación de filas, resultados vacíos y mosaicos.

**Validación pendiente para cerrar la entrega:** el canal de control visual de Windows devolvió `Computer Use native pipe is unavailable` y `adb devices` no mostró ningún dispositivo Android. Faltan las capturas y la revisión manual de la misma biblioteca en las anchuras y estados indicados arriba; hasta entonces, el estado permanece pendiente de validación visual.

## Verificación por entrega

1. Revisar Android en teléfono vertical y horizontal, y en tableta o emulador amplio. Revisar Windows con ventana estrecha, mediana y maximizada.
2. Recorrer estos flujos completos: continuar lectura; buscar y filtrar; abrir una serie; importar archivo y carpeta; cancelar y reintentar escaneo; exportar e importar respaldo; abrir marcadores y miniaturas; cambiar y restablecer preferencias por libro.
3. Comprobar rotación y redimensionamiento durante búsqueda, escaneo y lectura, sin pérdida de estado ni saltos inesperados.
4. Ejecutar `dotnet test NebulaReader.Tests/NebulaReader.Tests.csproj` y compilar Android y Windows. Añadir pruebas automatizadas solo donde la reorganización introduzca lógica nueva de navegación o estado; validar la distribución visual en dispositivos reales.
5. Actualizar [Readme.md](Readme.md) al finalizar cada entrega para que las instrucciones coincidan con los nombres y rutas visibles en la aplicación.

Una entrega se marca como completada cuando cumple sus criterios de aceptación en ambas plataformas y sus flujos existentes siguen funcionando.
