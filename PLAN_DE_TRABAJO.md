# Plan de trabajo — Nebula Reader

## Visión del producto

Nebula Reader busca ser un lector de cómics y documentos moderno, privado y multiplataforma, con una experiencia fluida tanto en navegador como en Android/iOS. El núcleo es una PWA offline-first, con soporte para bibliotecas locales y conexión opcional a Google Drive.

## Decisión técnica

- **Frontend:** HTML, CSS y JavaScript modular con Vite.
- **Web/PWA:** GitHub Pages + Service Worker + Manifest.
- **Persistencia local:** IndexedDB para archivos, progreso, notas y preferencias.
- **Archivos:** JSZip para CBZ/ZIP, libarchive.js + WebAssembly para CBR/RAR y PDF.js para PDF.
- **Móvil:** Capacitor reutilizando la misma base de código.
- **Nube:** Google OAuth + Google Picker + Google Drive API, con acceso de solo lectura.

La arquitectura prioriza una sola base de código, privacidad local, funcionamiento offline y compatibilidad progresiva con fuentes externas.

---

## Estado general

| Fase | Estado | Prioridad |
|---|---|---|
| 1. Estabilización del lector | ✅ Completada | Alta |
| 2. Biblioteca offline | ✅ Completada | Alta |
| 3. Formatos y capítulos | ✅ Completada | Alta |
| 4. Experiencia de lectura | ✅ Completada | Alta |
| 5. Organización avanzada | ✅ Completada | Media |
| 6. Distribución web/Android | ✅ Completada | Alta |
| 7. Validación continua | 🟡 En curso | Alta |
| 8. UX premium | ✅ Completada | Alta |
| 9. Biblioteca cloud real | ⏳ Pendiente | Muy alta |
| 10. Escalabilidad y almacenamiento | ⏳ Pendiente | Muy alta |
| 11. Seguridad y producción pública | ⏳ Pendiente | Alta |
| 12. Pulido móvil y rendimiento | ⏳ Pendiente | Media |

---

## 1. Estabilización del lector — completada

- Separación básica entre interfaz, persistencia y motor de archivos.
- Protección contra respuestas asíncronas obsoletas al cambiar de página o capítulo.
- Limpieza de URLs temporales y recursos cargados.
- Manejo de errores al abrir imágenes, PDF, CBZ y CBR.
- Flujo consistente entre biblioteca y lector.

## 2. Biblioteca offline — completada

- Importación múltiple.
- Arrastrar y soltar.
- Portadas automáticas.
- Conteo de páginas.
- Conservación del archivo original.
- Progreso persistente.
- Solicitud de almacenamiento persistente cuando el navegador lo permite.
- Uso de IndexedDB como almacenamiento principal.

## 3. Formatos y capítulos — completada

- CBZ/ZIP mediante JSZip.
- CBR/RAR mediante libarchive.js + Web Worker + WebAssembly.
- PDF mediante PDF.js con recursos locales.
- Navegación anterior/siguiente entre capítulos.
- Selector de capítulos.
- Conversión local CBR → CBZ.

## 4. Experiencia de lectura — completada

- Una página.
- Doble página.
- Desplazamiento vertical.
- Lectura occidental y manga.
- Zoom entre 50% y 300%.
- Zoom por botones, rueda, teclado y gesto táctil.
- Zoom inteligente mediante doble toque/clic.
- Ajuste por ancho, alto u óptimo.
- Temas oscuro, OLED, sepia y claro.
- Filtro ocular.
- Brillo manual y adaptado.
- Marcadores por página.
- Notas.
- Pantalla completa.
- Orientación automática, vertical u horizontal.
- Soporte táctil y teclado.

## 5. Organización avanzada — completada

- Favoritos.
- Búsqueda.
- Filtros.
- Vista de estanterías.
- Agrupación por series.
- Detección automática de series desde nombres de archivo.
- Edición manual de:
  - título,
  - serie,
  - número,
  - autor,
  - editorial,
  - año,
  - etiquetas,
  - sinopsis.
- ComicInfo.xml.
- Metadatos PDF.
- Portadas personalizadas.
- Historial de lectura.
- Tiempo leído.
- Páginas vistas.
- Recomendaciones locales.

## 6. Distribución — completada para web y Android

- Vite para compilación web.
- GitHub Pages para publicación.
- Workflow automático de despliegue.
- Manifest PWA.
- Service Worker.
- Iconos de instalación.
- Proyecto Android mediante Capacitor.
- Configuración iOS preparada para generarse en macOS/Xcode.

## 7. Validación — en curso continuo

### Completado

- Pruebas unitarias básicas.
- Compilación web automática.
- Validación con archivos reales:
  - CBZ: 23 páginas.
  - CBR: 29 páginas.
  - PDF: 223 páginas.

### Pendiente

- Colecciones de cientos o miles de capítulos.
- Archivos muy grandes.
- CBR cifrados.
- Archivos dañados.
- Dispositivos móviles de gama baja.
- Navegadores móviles con poca memoria.
- Pérdida de conexión durante lectura cloud.
- Almacenamiento casi lleno.

## 8. UX premium — completada

- Inicio dinámico con **Continuar leyendo**.
- Sección **Agregados recientemente**.
- Carruseles horizontales responsive.
- Vista de series.
- Acceso a capítulos ordenados desde cada serie.
- Navegación móvil inferior con:
  - Inicio,
  - Continuar,
  - Series,
  - Espacio.
- Panel de almacenamiento.
- Visualización de uso del sitio.
- Tamaño aproximado de biblioteca.
- Tamaño de caché.
- Limpieza de caché temporal sin borrar la biblioteca.
- Carga prioritaria de imágenes visibles.

---

# Próximas fases

## 9. Biblioteca cloud real — pendiente

### Objetivo

Convertir Google Drive de un simple importador a una fuente real de biblioteca sincronizada.

### Tareas

- Añadir un modelo de origen por libro:
  - `local`
  - `drive`
- Guardar metadatos cloud:
  - `driveFileId`
  - `folderId`
  - `driveModifiedTime`
  - `drivePath`
- Mostrar archivos de Drive en la biblioteca sin descargarlos completos.
- Descargar el archivo solamente al abrirlo.
- Opción **Disponible offline** por capítulo o serie.
- Sincronización manual.
- Sincronización automática al iniciar.
- Detectar:
  - nuevos archivos,
  - cambios de nombre,
  - archivos modificados,
  - archivos eliminados.
- Tratar subcarpetas como series cuando sea conveniente.
- Indicador de estado cloud/offline.
- Gestión de errores de token OAuth.
- Reconexión sin perder metadatos locales.

## 10. Escalabilidad y almacenamiento — pendiente

### Base de datos v2

Separar metadatos de archivos pesados.

Crear índices para:

- `series`
- `favorite`
- `lastReadAt`
- `addedAt`
- `source`
- `normalizedTitle`

### Almacenamiento

- Biblioteca sin descargar archivo completo por defecto.
- Cachear solo capítulos utilizados recientemente.
- Opción para conservar:
  - últimos 5,
  - últimos 10,
  - últimos 20 capítulos.
- Limpieza automática según espacio disponible.
- Mostrar claramente:
  - biblioteca local,
  - archivos offline,
  - caché temporal,
  - espacio libre aproximado.

### Rendimiento

- Evitar cargar todos los blobs al iniciar.
- Paginación o virtualización de bibliotecas grandes.
- Renderizado progresivo de tarjetas.
- Portadas optimizadas y thumbnails separados del archivo original.

## 11. Seguridad y producción pública — pendiente

### Google OAuth

- Completar verificación de Google para `drive.readonly`.
- Video de demostración del flujo OAuth.
- Página pública de privacidad.
- Página pública de términos.
- Revisar scopes y mantener solo lectura.
- Rotar cualquier `client_secret` que haya sido expuesto durante pruebas.
- No incluir secretos en frontend ni repositorio.

### API Key

- Mantener restricciones por dominio.
- Limitar APIs autorizadas a:
  - Google Drive API,
  - Google Picker API.
- Revisar periódicamente cuotas y uso.

### Dependencias

- Auditar vulnerabilidades npm.
- Actualizar dependencias críticas.
- Revisar especialmente:
  - PDF.js,
  - libarchive.js,
  - JSZip,
  - Vite,
  - Capacitor.

## 12. Pulido móvil y rendimiento — pendiente

- Optimizar experiencia táctil en pantallas pequeñas.
- Mejorar transiciones entre biblioteca y lector.
- Precarga de página siguiente y anterior.
- Precarga opcional del siguiente capítulo.
- Mejor soporte para dispositivos con poca RAM.
- Reducir consumo de memoria en modo vertical.
- Lazy loading de motores PDF/CBR.
- Optimizar imágenes de branding e iconos.
- Diferenciar configuración Vite para:
  - GitHub Pages,
  - Capacitor Android,
  - Capacitor iOS.
- Pruebas físicas en Android.
- Proyecto iOS generado y probado en macOS.

---

# Mejoras opcionales de producto

## Biblioteca

- Colecciones personalizadas.
- Etiquetas visuales.
- Ordenar por:
  - título,
  - fecha,
  - progreso,
  - serie,
  - número,
  - tamaño.
- Ocultar completados.
- Marcar serie completa como favorita.

## Lectura

- Modo automático de cambio de página.
- Recordar zoom por serie.
- Detección más robusta de páginas dobles.
- Más opciones de fondo.
- Personalización de zonas táctiles.

## Series

- Portada única por serie.
- Pantalla dedicada a serie.
- Descripción general de serie.
- Progreso total de serie.
- Botón **Continuar siguiente capítulo**.
- Orden manual de capítulos.
- Detección avanzada de nombres como:
  - `Vol. 01`
  - `Chapter 001`
  - `Ch. 12.5`
  - `Issue 03`

## Respaldo

- Exportar metadatos y progreso.
- Importar respaldo.
- Sincronización opcional de progreso entre dispositivos.

---

# Criterios de aceptación del producto

- La aplicación debe abrir la biblioteca sin conexión después de una visita inicial.
- Los archivos locales no deben salir del dispositivo sin acción explícita del usuario.
- Google Drive debe operar únicamente con autorización del usuario.
- El progreso debe sobrevivir al cierre de la pestaña o aplicación.
- Un cambio rápido de página o capítulo no debe mostrar contenido obsoleto.
- Los controles principales deben funcionar con ratón, teclado y táctil.
- Una colección grande no debe bloquear la interfaz.
- El usuario debe saber qué contenido está local, cloud o disponible offline.
- La experiencia web y móvil debe conservar el mismo comportamiento funcional.

---

# Prioridad recomendada actual

1. **Biblioteca cloud real**.
2. **Base de datos v2 y almacenamiento inteligente**.
3. **Pruebas de escala y rendimiento**.
4. **Verificación OAuth para producción**.
5. **Auditoría de dependencias y seguridad**.
6. **Pulido móvil y optimización final**.
