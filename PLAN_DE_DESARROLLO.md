# Plan de Desarrollo: App Multiplataforma de Lectura (CBR, CBZ y PDF) con .NET MAUI

Este plan de desarrollo está estructurado en **5 fases progresivas** para construir un lector de cómics y documentos multiplataforma (**Android y Windows**) con **.NET MAUI**, optimizado para bajo consumo de memoria, navegación rápida y código compartido.

---

## Resumen del Stack Tecnológico

| Capa | Tecnología / Paquete NuGet | Propósito |
| :--- | :--- | :--- |
| **Framework** | .NET 8 / .NET 9 MAUI | Proyecto único (*Single Project*) enfocado en `net8.0-android` y `net8.0-windows10.0.19041.0`. |
| **Patrón Arquitectónico** | MVVM (`CommunityToolkit.Mvvm`) | Desacoplamiento limpio entre vistas XAML, estado y lógica de lectura. |
| **Descompresión (CBR/CBZ)** | `System.IO.Compression` + `SharpCompress` | Lectura de streams ZIP y RAR (incluyendo RAR5) sin extraer archivos completos al disco. |
| **Renderizado PDF** | `PdfRenderer` (Android) / `Windows.Data.Pdf` (Windows) | Uso de las APIs nativas de cada sistema operativo vía compilación condicional o clases parciales. |
| **Gráficos y Caché** | `SkiaSharp.Views.Maui.Controls` + Caché LRU | Renderizado de alto rendimiento, *pinch-to-zoom* fluido, paneo y gestión estricta de RAM. |
| **Base de Datos Local** | `sqlite-net-pcl` | Persistencia de biblioteca, progreso de lectura, marcadores y rutas de miniaturas. |

---

## Fase 1: Configuración Base y Arquitectura del Proyecto ✅ Completada

El objetivo de esta fase es preparar la estructura limpia para que el 90 % del código funcione sin cambios en ambos sistemas operativos.

- [x] **Limpieza de Targets:** Configurar el archivo `.csproj` para compilar exclusivamente hacia **Android** y **Windows**, eliminando iOS y MacCatalyst para agilizar las compilaciones.
- [x] **Estructura de Carpetas:**
  * `Models/`: Entidades (`BookItem`, `ReadingProgress`, `PageInfo`).
  * `Services/`: Contratos (`IBookSource`, `ILibraryService`, `IFilePickerService`).
  * `ViewModels/`: Lógica de pantalla con `[ObservableProperty]` y `[RelayCommand]`.
  * `Views/`: Pantallas XAML (`LibraryPage`, `ReaderPage`, `SettingsPage`).
  * `Platforms/Android` y `Platforms/Windows`: Implementaciones específicas de permisos y renderizado PDF.
- [x] **Inyección de Dependencias (`MauiProgram.cs`):** Registrar servicios como *Singleton* (gestor de biblioteca y caché) y páginas/ViewModels como *Transient*.

---

## Fase 2: Motor de Lectura y Abstracción de Formatos (`Core`) ✅ Completada

Aquí se construye el núcleo que unifica los tres formatos bajo una misma interfaz para que el visor no tenga que saber si está abriendo un PDF o un cómic comprimido.

- [x] **Interfaz Unificada (`IBookSource`):**
   ```csharp
   public interface IBookSource : IDisposable
   {
       string Title { get; }
       int PageCount { get; }
       Task<Stream> GetPageStreamAsync(int pageIndex, CancellationToken ct = default);
       Task<Stream> GetCoverThumbnailAsync();
   }
   ```
- [x] **Implementación CBZ (`CbzBookSource`):**
   * Abrir el archivo usando `ZipArchive` en modo lectura.
   * Filtrar entradas por extensiones de imagen (`.jpg`, `.jpeg`, `.png`, `.webp`) ignorando carpetas ocultas (`__MACOSX`).
   * **Ordenamiento Natural (*Natural Sort*):** Implementar un comparador alfanumérico para que `pagina_2.jpg` aparezca antes que `pagina_10.jpg`.
- [x] **Implementación CBR (`CbrBookSource`):**
   * Utilizar `RarArchive.OpenArchive(stream)` de **SharpCompress**.
   * Indexar las entradas de imagen al abrir el archivo para permitir acceso directo por índice.
- [x] **Implementación PDF (`PdfBookSource`):**
   * Usar **clases parciales** (`PdfBookSource.Android.cs` y `PdfBookSource.Windows.cs`).
   * Renderizar cada página solicitada a un `Stream` en formato PNG/JPEG escalado a la densidad de pantalla del dispositivo (`DeviceDisplay.Current.MainDisplayInfo.Density`) para evitar imágenes borrosas o excesivamente pesadas.
- [x] **Fábrica (`BookSourceFactory`):** Clase que recibe la ruta o URI del archivo, inspecciona su extensión o cabecera (*magic bytes*) y devuelve la instancia correspondiente de `IBookSource`.

---

## Fase 3: Sistema de Biblioteca, Indexación y Permisos ✅ Completada

En Windows el acceso a carpetas es directo, pero en Android es obligatorio respetar *Scoped Storage* para evitar bloqueos en versiones modernas del sistema.

- [x] **Selector de Archivos y Carpetas:**
   * Usar `FilePicker` de .NET MAUI para abrir archivos individuales (`.cbr`, `.cbz`, `.pdf`).
   * Implementar un selector de carpetas nativo (`Intent.ActionOpenDocumentTree` en Android con permisos persistentes y `FolderPicker` de WinUI en Windows) para escanear colecciones completas.
- [x] **Base de Datos SQLite (`LibraryDatabase`):**
   * Almacenar ruta/URI del archivo, título, formato, total de páginas, última página leída, fecha de última lectura y estado (No leído, Leyendo, Completado).
- [x] **Generación de Portadas en Segundo Plano:**
   * Al importar archivos nuevos, extraer únicamente la página `0`, redimensionarla a un ancho fijo (ej. 300 px) con **SkiaSharp** y guardarla en `FileSystem.CacheDirectory` para que la cuadrícula de la biblioteca cargue de forma instantánea.
- [x] **Interfaz de Biblioteca (`LibraryPage`):**
   * Usar `CollectionView` con `GridItemsLayout` adaptable: 2–3 columnas en pantallas táctiles verticales (Android) y 5–7 columnas dinámicas al redimensionar la ventana en escritorio (Windows).

---

## Fase 4: Visor de Lectura y Gestión de Memoria (`ReaderPage`) ✅ Completada

Esta es la fase más crítica para la experiencia de usuario, donde se garantiza fluidez al pasar páginas y hacer zoom.

- [x] **Caché de Páginas en Memoria (LRU Cache):**
   * Mantener en RAM únicamente una ventana deslizante de **3 páginas**: `[página actual - 1, página actual, página actual + 1]`.
   * Al avanzar a la siguiente página, precargar en segundo plano la página `actual + 2` y liberar (`Dispose()`) el bitmap de `actual - 2` para prevenir errores `OutOfMemoryException` en Android.
- [x] **Lienzo de Renderizado con SkiaSharp (`SKCanvasView`):**
   * Dibujar la imagen centrada y ajustada a pantalla.
   * Implementar una matriz de transformación (`SKMatrix`) para soportar:
     * **Android:** Gestos táctiles (`PinchGestureRecognizer` para zoom, `PanGestureRecognizer` para moverse por la imagen ampliada y doble toque para zoom rápido).
     * **Windows:** Rueda del ratón (`PointerWheelChanged` con `Ctrl` para zoom y sin `Ctrl` para desplazamiento vertical) y clic y arrastre para mover la página.
- [x] **Modos de Lectura:**
   * **Paginado Horizontal:** Izquierda a derecha (cómic occidental) y derecha a izquierda (manga).
   * **Desplazamiento Vertical Continuo:** Ideal para documentos PDF y formato *Webtoon*.
   * **Doble Página (Escritorio/Tablet):** Mostrar dos páginas lado a lado cuando el ancho de ventana supere los 800 dp en modo horizontal.
- [x] **Controles Superpuestos (*HUD*):**
   * Barra superior e inferior translúcidas que se ocultan al tocar el centro de la pantalla, incluyendo barra deslizante (*Slider*) de progreso de páginas, indicador `Página X / Y` y botón de bloqueo de rotación.

---

## Fase 5: Pulido, Pruebas y Empaquetado ✅ Completada

- [x] **Atajos de Teclado (Enfoque Windows):**
   * Agregar soporte para flechas izquierda/derecha (`PageUp`/`PageDown` para cambiar de página), tecla `F11` para pantalla completa y `Esc` para volver a la biblioteca.
- [x] **Filtros Visuales Opcionales:**
   * Aprovechar los *ColorFilters* de SkiaSharp para ofrecer un **Modo Lectura Nocturna** (inversión suave de luminosidad o filtro sepia) muy útil para leer PDFs con fondo blanco por la noche.
- [x] **Pruebas de Estrés:**
   * Suite automatizada `NebulaReader.Tests` verificada sobre archivo CBZ de 320 páginas y ventana LRU de 320 bitmaps 4K (`3840x2160`) con liberación determinista (pendiente validación manual en dispositivo Android físico con archivos reales >500 MB / PDF >300 páginas).
- [x] **Compilación y Distribución:**
   * **Android:** Generado `.apk` en modo `Release` (`android-arm64`) con *Trimming* activado (`com.masterpet.nebulareader-Signed.apk`, firma de desarrollo/depuración; sustituir keystore al publicar en Google Play).
   * **Windows:** Generada publicación autocontenida (*Self-Contained*, `win-x64`) en formato `.exe` (`NebulaReader.exe`).

