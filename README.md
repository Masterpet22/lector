# Nebula Reader

Lector y biblioteca offline de cómics CBZ/CBR/PDF para navegador, PWA y Android.

## Desarrollo web

```powershell
npm install
npm run dev
```

Producción:

```powershell
npm test
npm run build
npm run preview
```

La carpeta `dist/` es el sitio estático publicable. Debe servirse mediante HTTPS (o `localhost`) para instalar la PWA y usar el service worker.

## Android

El proyecto nativo ya existe en `android/`:

```powershell
npm run mobile:android
```

El comando compila, sincroniza y abre Android Studio. Desde allí se ejecuta en un dispositivo/emulador y se genera el APK/AAB firmado.

Para generar directamente un APK instalable de pruebas:

```powershell
npm run android:apk
```

El resultado visible queda en `releases/NebulaReader-debug.apk`. Android utiliza APK o AAB; el formato PKG pertenece a macOS.

## Controles del lector

- Arrastra con el ratón o desliza con un dedo para recorrer páginas ampliadas.
- Pellizca o usa `Ctrl + rueda` para cambiar el zoom.
- Usa `Mayús + flechas` para desplazar la vista sin pasar de página.
- El botón de modo de la barra inferior alterna una página, dos páginas y lectura vertical.
- Un doble clic o doble toque amplía la viñeta detectada; repítelo para volver al ajuste normal.
- Las flechas laterales y los botones inferiores respetan la dirección occidental o manga.

## Biblioteca y datos de lectura

- Usa la estrella para marcar favoritos y el menú `⋮` para editar metadatos o cambiar la portada.
- La vista `▦` agrupa los libros en estanterías por serie; búsqueda y filtros funcionan completamente offline.
- Marcadores, notas, páginas vistas, tiempo leído y progreso se guardan por archivo.
- Las recomendaciones se calculan solo con series, autores y etiquetas presentes en la biblioteca local.

## iOS

En macOS, después de instalar Xcode:

```bash
npx cap add ios
npm run mobile:ios
```

## Datos y privacidad

- Cómics, portadas, progreso y preferencias se guardan en IndexedDB.
- Los archivos de la aplicación se almacenan en Cache Storage.
- No existen endpoints, analítica ni cargas a servidores.
- Las bibliotecas WebAssembly, ZIP y PDF.js están incluidas localmente en `public/vendor/`.

El almacenamiento depende de la cuota del navegador y el usuario siempre puede borrarlo. La conversión CBR → CBZ necesita memoria adicional proporcional al capítulo; conviene convertir archivos especialmente grandes desde escritorio.

Consulta [PLAN_DE_TRABAJO.md](./PLAN_DE_TRABAJO.md) para la arquitectura, fases y criterios de aceptación.
