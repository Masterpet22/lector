const IMAGE_PATTERN = /\.(avif|gif|jpe?g|png|webp)$/i;
const collator = new Intl.Collator(undefined, { numeric: true, sensitivity: 'base' });
let archiveModulePromise;
let pdfModulePromise;

function extension(name) {
  return name.split('.').pop()?.toLowerCase() || '';
}

function isUsableImage(name) {
  const parts = name.replaceAll('\\', '/').split('/');
  return IMAGE_PATTERN.test(name) && !parts.includes('__MACOSX') && !parts.at(-1).startsWith('._');
}

function mimeFor(name) {
  return ({ avif: 'image/avif', gif: 'image/gif', jpg: 'image/jpeg', jpeg: 'image/jpeg', png: 'image/png', webp: 'image/webp' })[extension(name)] || 'application/octet-stream';
}

async function loadLibarchive() {
  archiveModulePromise ||= import('libarchive.js').then((module) => {
    module.Archive.init({ workerUrl: '/vendor/worker-bundle.js' });
    return module;
  });
  return archiveModulePromise;
}

async function loadPdfJs() {
  pdfModulePromise ||= import('pdfjs-dist/build/pdf.mjs').then((module) => {
    module.GlobalWorkerOptions.workerSrc = '/vendor/pdfjs/pdf.worker.min.mjs';
    return module;
  });
  return pdfModulePromise;
}

async function openZip(blob) {
  if (!window.JSZip) throw new Error('El motor ZIP no está disponible.');
  const zip = await window.JSZip.loadAsync(blob);
  const entries = [];
  zip.forEach((_, entry) => {
    if (!entry.dir && isUsableImage(entry.name)) entries.push(entry);
  });
  entries.sort((a, b) => collator.compare(a.name, b.name));
  if (!entries.length) throw new Error('El archivo no contiene imágenes compatibles.');
  return {
    type: 'zip',
    pages: entries.map((entry) => ({
      name: entry.name,
      getBlob: () => entry.async('blob').then((blob) => blob.type ? blob : new Blob([blob], { type: mimeFor(entry.name) })),
    })),
    close() {},
  };
}

async function openRar(blob, fileName) {
  const { Archive } = await loadLibarchive();
  const file = new File([blob], fileName, { type: blob.type || 'application/vnd.rar' });
  const archive = await Archive.open(file);
  const listed = await archive.getFilesArray();
  const entries = listed
    .filter(({ file: compressed, path }) => compressed && isUsableImage(`${path}${compressed.name}`))
    .map(({ file: compressed, path }) => ({ compressed, name: `${path}${compressed.name}` }))
    .sort((a, b) => collator.compare(a.name, b.name));
  if (!entries.length) {
    await archive.close();
    throw new Error('El CBR no contiene imágenes compatibles o usa cifrado.');
  }
  return {
    type: 'rar',
    pages: entries.map(({ compressed, name }) => ({
      name,
      getBlob: async () => {
        const extracted = await compressed.extract();
        return new Blob([extracted], { type: mimeFor(name) });
      },
    })),
    close: () => archive.close(),
  };
}

async function openPdf(blob) {
  const pdfjs = await loadPdfJs();
  const loadingTask = pdfjs.getDocument({
    data: new Uint8Array(await blob.arrayBuffer()),
    cMapUrl: '/vendor/pdfjs/cmaps/',
    cMapPacked: true,
    standardFontDataUrl: '/vendor/pdfjs/standard_fonts/',
    wasmUrl: '/vendor/pdfjs/wasm/',
    isEvalSupported: false,
  });
  const documentProxy = await loadingTask.promise;
  let closed = false;

  return {
    type: 'pdf',
    pages: Array.from({ length: documentProxy.numPages }, (_, index) => ({
      name: `${String(index + 1).padStart(4, '0')}.png`,
      getBlob: async () => {
        if (closed) throw new Error('El documento PDF ya está cerrado.');
        const page = await documentProxy.getPage(index + 1);
        const baseViewport = page.getViewport({ scale: 1 });
        const longestSide = Math.max(baseViewport.width, baseViewport.height);
        const density = Math.max(1.5, Math.min(window.devicePixelRatio || 1, 2) * 1.5);
        const scale = Math.min(density, 2800 / longestSide);
        const viewport = page.getViewport({ scale });
        const canvas = document.createElement('canvas');
        canvas.width = Math.max(1, Math.floor(viewport.width));
        canvas.height = Math.max(1, Math.floor(viewport.height));
        await page.render({ canvas, viewport }).promise;
        page.cleanup();
        const rendered = await new Promise((resolve, reject) => canvas.toBlob(
          (result) => result ? resolve(result) : reject(new Error('No se pudo rasterizar la página PDF.')),
          'image/webp',
          0.92,
        ));
        canvas.width = 1;
        canvas.height = 1;
        return rendered;
      },
    })),
    close: async () => {
      if (closed) return;
      closed = true;
      await loadingTask.destroy();
    },
  };
}

export async function openComicArchive(blob, fileName) {
  const ext = extension(fileName);
  if (ext === 'cbr') return openRar(blob, fileName);
  if (ext === 'cbz' || ext === 'zip') return openZip(blob);
  if (ext === 'pdf') return openPdf(blob);
  throw new Error('Formato no soportado. Usa CBZ, CBR, ZIP o PDF.');
}

export async function inspectComic(blob, fileName) {
  const archive = await openComicArchive(blob, fileName);
  try {
    const cover = await archive.pages[0].getBlob();
    return { pageCount: archive.pages.length, cover };
  } finally {
    await archive.close();
  }
}

export async function convertRarToCbz(blob, fileName, onProgress = () => {}) {
  if (!window.JSZip) throw new Error('El motor ZIP no está disponible.');
  const archive = await openRar(blob, fileName);
  const zip = new window.JSZip();
  try {
    for (let index = 0; index < archive.pages.length; index += 1) {
      const page = archive.pages[index];
      zip.file(page.name.replaceAll('\\', '/'), await page.getBlob());
      onProgress(index + 1, archive.pages.length);
    }
    return await zip.generateAsync({ type: 'blob', compression: 'DEFLATE', compressionOptions: { level: 6 } });
  } finally {
    await archive.close();
  }
}

export function baseTitle(fileName) {
  return fileName.replace(/\.(cbz|cbr|zip|pdf)$/i, '').trim();
}

export function stableBookId(file) {
  const input = `${file.name}|${file.size}|${file.lastModified}`;
  let hash = 2166136261;
  for (let index = 0; index < input.length; index += 1) {
    hash ^= input.charCodeAt(index);
    hash = Math.imul(hash, 16777619);
  }
  return `book-${(hash >>> 0).toString(16)}`;
}
