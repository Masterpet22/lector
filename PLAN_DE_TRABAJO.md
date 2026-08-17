# Plan de trabajo — Nebula Reader

## Decisión técnica

Una PWA offline-first es el núcleo común. Vite genera el sitio web instalable y Capacitor empaqueta exactamente esa compilación para Android/iOS. IndexedDB guarda los archivos y el progreso; Cache Storage conserva la aplicación; JSZip procesa CBZ, libarchive.js procesa RAR/CBR mediante WebAssembly y PDF.js rasteriza documentos PDF localmente.

Esta combinación mantiene una sola base de código, permite usar el lector desde el navegador y ofrece plugins nativos cuando una API web —por ejemplo, el bloqueo de orientación— no es uniforme.

## Fases

1. **Estabilización del lector — completada**
   - Separar interfaz, persistencia y motor de archivos.
   - Evitar respuestas asíncronas obsoletas al cambiar de página/capítulo.
   - Corregir título, zona de carga completa, errores de imagen y limpieza de URLs.

2. **Biblioteca offline — completada**
   - Importación múltiple y arrastrar/soltar.
   - Portada, número de páginas, formato y uso de almacenamiento.
   - Conservación del archivo original y progreso en IndexedDB.
   - Solicitud de almacenamiento persistente cuando el navegador lo admite.

3. **Formatos y capítulos — completada**
   - CBZ/ZIP con JSZip.
   - CBR (RAR 4/5) con libarchive.js + Web Worker + WebAssembly.
   - PDF con PDF.js, worker local, fuentes y mapas de caracteres offline.
   - Selector y navegación anterior/siguiente entre capítulos.
   - Conversión local CBR → CBZ descargable.

4. **Experiencia de lectura — completada**
   - Una página, doble página y desplazamiento vertical.
   - Zoom de 50% a 300%, botones, teclado, rueda y gesto de pellizco.
   - Ajuste óptimo, por ancho o por alto.
   - Tema oscuro, OLED, sepia y claro.
   - Filtro ocular regulable.
   - Bloqueo automático, vertical u horizontal con API web y plugin nativo.

5. **Distribución — completada para web y Android**
   - Manifest, iconos y service worker sin dependencias remotas.
   - Compilación reproducible con Vite.
   - Proyecto Android de Capacitor generado.
   - Configuración iOS incluida; su proyecto y firma deben generarse en macOS con Xcode.

6. **Validación — en curso continuo**
   - Pruebas unitarias del archivo, orden y metadatos.
   - Compilación web y sincronización Android en cada entrega.
   - Pendiente de producto: pruebas con una colección real grande, CBR cifrados y dispositivos físicos de gama baja.

## Criterios de aceptación

- La aplicación arranca y muestra la biblioteca sin conexión después de instalarse o visitarse una vez.
- Ningún cómic sale del dispositivo.
- El progreso sobrevive al cierre de la pestaña/app.
- Un cambio rápido de página o capítulo no puede mostrar contenido de una sesión anterior.
- Todos los controles críticos son utilizables con teclado y en pantallas táctiles.
- Android usa el mismo contenido compilado que la web.
