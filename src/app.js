import {
  deleteBook, getAllBooks, getBook, getSetting, getStorageEstimate,
  openDatabase, requestPersistentStorage, saveBook, setSetting, updateBook,
} from './db.js';
import { baseTitle, convertRarToCbz, inspectComic, openComicArchive, stableBookId } from './archive.js';

const $ = (selector) => document.querySelector(selector);
const ui = {
  libraryView: $('#library-view'), readerView: $('#reader-view'), libraryGrid: $('#library-grid'),
  emptyLibrary: $('#empty-library'), fileInput: $('#file-input'), dropzone: $('#dropzone'),
  bookCount: $('#book-count'), storageUsage: $('#storage-usage'), settings: $('#settings-dialog'),
  confirm: $('#confirm-dialog'), stage: $('#page-stage'), paged: $('#paged-pages'), vertical: $('#vertical-pages'),
  topbar: $('#reader-topbar'), bottombar: $('#reader-bottombar'), title: $('#reader-title'),
  counter: $('#page-counter'), slider: $('#page-slider'), loader: $('#reader-loader'), loaderText: $('#loader-text'),
  chapterSelect: $('#chapter-select'), previousChapter: $('#previous-chapter'), nextChapter: $('#next-chapter'),
  zoomValue: $('#zoom-value'), fitSelect: $('#fit-select'), orientationSelect: $('#orientation-select'),
  eyeRange: $('#eye-filter-range'), eyeValue: $('#eye-filter-value'), eyeFilter: $('#eye-filter'),
  modeControl: $('#reading-mode-control'), themeControl: $('#theme-control'), convertButton: $('#convert-cbr'),
  currentBookActions: $('#current-book-actions'), installButton: $('#install-button'), offlineBadge: $('#offline-badge'),
  quickMode: $('#quick-mode'), readerHint: $('#reader-hint'),
};

const DEFAULT_SETTINGS = { theme: 'dark', readingMode: 'page', fit: 'best', orientation: 'any', eyeFilter: 0, zoom: 1 };
const state = {
  books: [], currentBook: null, archive: null, currentIndex: 0, settings: { ...DEFAULT_SETTINGS },
  sessionId: 0, renderId: 0, urlPromises: new Map(), objectUrls: new Map(), verticalObserver: null,
  hudTimer: null, hintTimer: null, saveTimer: null, installPrompt: null, touchDistance: 0, touchZoom: 1,
  mousePan: null, suppressZoneClick: false,
};

function formatBytes(bytes = 0) {
  if (!bytes) return '0 MB';
  const units = ['B', 'KB', 'MB', 'GB'];
  const power = Math.min(Math.floor(Math.log(bytes) / Math.log(1024)), units.length - 1);
  return `${(bytes / (1024 ** power)).toFixed(power > 1 ? 1 : 0)} ${units[power]}`;
}

function toast(message, type = '') {
  const element = document.createElement('div');
  element.className = `toast ${type}`;
  element.textContent = message;
  $('#toast-region').append(element);
  window.setTimeout(() => element.remove(), 4200);
}

function setLoading(active, text = 'Procesando…') {
  ui.loaderText.textContent = text;
  ui.loader.classList.toggle('hidden', !active);
}

function extOf(name) { return name.split('.').pop()?.toLowerCase() || ''; }

function createBookCard(book) {
  const article = document.createElement('article');
  article.className = 'book-card';
  article.dataset.id = book.id;

  const open = document.createElement('button');
  open.type = 'button';
  open.className = 'cover-button';
  open.setAttribute('aria-label', `Leer ${book.title}`);
  open.addEventListener('click', () => openBook(book.id));

  const cover = document.createElement('div');
  cover.className = 'cover';
  if (book.cover) {
    const image = new Image();
    const coverUrl = URL.createObjectURL(book.cover);
    image.alt = `Portada de ${book.title}`;
    image.src = coverUrl;
    image.onload = () => URL.revokeObjectURL(coverUrl);
    image.onerror = () => URL.revokeObjectURL(coverUrl);
    cover.append(image);
  } else {
    const placeholder = document.createElement('div');
    placeholder.className = 'cover-placeholder';
    placeholder.textContent = book.title;
    cover.append(placeholder);
  }

  const progress = book.pageCount ? Math.min(100, ((book.currentPage || 0) + 1) / book.pageCount * 100) : 0;
  const track = document.createElement('div');
  track.className = 'progress-track';
  const fill = document.createElement('span');
  fill.style.width = `${progress}%`;
  track.append(fill);
  cover.append(track);

  const info = document.createElement('div');
  info.className = 'book-info';
  const title = document.createElement('strong');
  title.textContent = book.title;
  const detail = document.createElement('small');
  detail.textContent = `${book.pageCount || '—'} páginas · ${extOf(book.fileName).toUpperCase()}`;
  info.append(title, detail);
  open.append(cover, info);

  const menu = document.createElement('button');
  menu.type = 'button';
  menu.className = 'icon-button book-menu';
  menu.textContent = '×';
  menu.title = 'Eliminar de la biblioteca';
  menu.setAttribute('aria-label', `Eliminar ${book.title}`);
  menu.addEventListener('click', () => confirmDelete(book));
  article.append(open, menu);
  return article;
}

async function refreshLibrary() {
  state.books = await getAllBooks();
  ui.libraryGrid.replaceChildren(...state.books.map(createBookCard));
  ui.emptyLibrary.classList.toggle('hidden', state.books.length > 0);
  ui.bookCount.textContent = `${state.books.length} ${state.books.length === 1 ? 'capítulo' : 'capítulos'}`;
  const estimate = await getStorageEstimate();
  ui.storageUsage.textContent = estimate ? `${formatBytes(estimate.usage)} usados` : '';
  refreshChapterPicker();
}

async function importFiles(files) {
  const accepted = [...files].filter((file) => ['cbz', 'cbr', 'zip', 'pdf'].includes(extOf(file.name)));
  if (!accepted.length) return toast('Selecciona archivos CBZ, CBR, ZIP o PDF.', 'error');
  await requestPersistentStorage();

  for (let index = 0; index < accepted.length; index += 1) {
    const file = accepted[index];
    setLoading(true, `Importando ${index + 1} de ${accepted.length}: ${file.name}`);
    try {
      const id = stableBookId(file);
      const existing = await getBook(id);
      const details = await inspectComic(file, file.name);
      await saveBook({
        id, title: baseTitle(file.name), normalizedTitle: baseTitle(file.name).toLocaleLowerCase(), fileName: file.name,
        format: extOf(file.name), blob: file, cover: details.cover, pageCount: details.pageCount,
        currentPage: existing?.currentPage || 0, addedAt: existing?.addedAt || Date.now(), updatedAt: Date.now(), size: file.size,
      });
    } catch (error) {
      console.error(error);
      toast(`No se pudo importar ${file.name}: ${error.message}`, 'error');
    }
  }
  setLoading(false);
  ui.fileInput.value = '';
  await refreshLibrary();
}

function confirmDelete(book) {
  $('#confirm-title').textContent = 'Eliminar capítulo';
  $('#confirm-message').textContent = `Se quitará “${book.title}” y su progreso de este dispositivo.`;
  ui.confirm.showModal();
  ui.confirm.addEventListener('close', async function onClose() {
    ui.confirm.removeEventListener('close', onClose);
    if (ui.confirm.returnValue !== 'confirm') return;
    await deleteBook(book.id);
    await refreshLibrary();
    toast('Capítulo eliminado.');
  });
}

async function closeArchiveSession() {
  state.renderId += 1;
  state.sessionId += 1;
  state.verticalObserver?.disconnect();
  state.verticalObserver = null;
  for (const url of state.objectUrls.values()) URL.revokeObjectURL(url);
  state.objectUrls.clear();
  state.urlPromises.clear();
  const archive = state.archive;
  state.archive = null;
  if (archive) {
    try { await archive.close(); } catch (error) { console.warn('No se pudo cerrar el archivo', error); }
  }
}

async function openBook(id) {
  const book = await getBook(id);
  if (!book) return toast('El capítulo ya no está en la biblioteca.', 'error');
  ui.libraryView.classList.add('hidden');
  ui.readerView.classList.remove('hidden');
  setLoading(true, `Abriendo ${book.title}…`);
  await closeArchiveSession();
  const sessionId = state.sessionId;
  state.currentBook = book;
  state.currentIndex = Math.min(book.currentPage || 0, Math.max(0, (book.pageCount || 1) - 1));

  try {
    const archive = await openComicArchive(book.blob, book.fileName);
    if (sessionId !== state.sessionId) return archive.close();
    state.archive = archive;
    if (archive.pages.length !== book.pageCount) {
      state.currentBook = await updateBook(book.id, { pageCount: archive.pages.length });
      await refreshLibrary();
    }
    ui.title.textContent = book.title;
    ui.slider.max = archive.pages.length;
    refreshChapterPicker();
    applyReadingMode();
    applyZoom();
    setLoading(false);
    resetHudTimer();
    showReaderHint();
    await applyOrientation(state.settings.orientation, false);
  } catch (error) {
    console.error(error);
    setLoading(false);
    toast(`No se pudo abrir el capítulo: ${error.message}`, 'error');
    await closeReader();
  }
}

async function getPageUrl(index) {
  if (!state.archive?.pages[index]) return null;
  if (state.objectUrls.has(index)) return state.objectUrls.get(index);
  if (state.urlPromises.has(index)) return state.urlPromises.get(index);
  const sessionId = state.sessionId;
  const promise = state.archive.pages[index].getBlob().then((blob) => {
    if (sessionId !== state.sessionId) return null;
    const url = URL.createObjectURL(blob);
    state.objectUrls.set(index, url);
    return url;
  }).catch((error) => {
    console.error(`Error al extraer página ${index + 1}`, error);
    return null;
  }).finally(() => state.urlPromises.delete(index));
  state.urlPromises.set(index, promise);
  return promise;
}

function cleanupPageUrls(center) {
  if (state.settings.readingMode === 'vertical') return;
  const keep = new Set([center - 2, center - 1, center, center + 1, center + 2, center + 3]);
  for (const [index, url] of state.objectUrls) {
    if (!keep.has(index)) {
      URL.revokeObjectURL(url);
      state.objectUrls.delete(index);
    }
  }
}

function makePageImage(url, index) {
  const frame = document.createElement('div');
  frame.className = 'page-frame';
  const image = new Image();
  image.className = 'page-image';
  image.alt = `Página ${index + 1}`;
  image.decoding = 'async';
  image.addEventListener('load', () => layoutPagedImages(false), { once: true });
  if (url) image.src = url;
  else {
    frame.classList.add('error');
    frame.textContent = `No se pudo mostrar la página ${index + 1}`;
  }
  frame.append(image);
  return frame;
}

async function renderPaged() {
  if (!state.archive) return;
  const renderId = ++state.renderId;
  const spread = state.settings.readingMode === 'spread';
  const indexes = [state.currentIndex];
  if (spread && state.currentIndex + 1 < state.archive.pages.length) indexes.push(state.currentIndex + 1);
  ui.paged.style.opacity = '.3';
  const urls = await Promise.all(indexes.map(getPageUrl));
  if (renderId !== state.renderId || !state.archive) return;
  ui.paged.replaceChildren(...indexes.map((index, position) => makePageImage(urls[position], index)));
  await Promise.all([...ui.paged.querySelectorAll('img')].map((image) => (image.decode?.() || Promise.resolve()).catch(() => {})));
  if (renderId !== state.renderId || !state.archive) return;
  layoutPagedImages(false);
  ui.paged.style.opacity = '1';
  updateReaderMeta();
  cleanupPageUrls(state.currentIndex);
  getPageUrl(state.currentIndex + (spread ? 2 : 1));
}

function setupVerticalReader() {
  state.renderId += 1;
  state.verticalObserver?.disconnect();
  const fragment = document.createDocumentFragment();
  state.archive.pages.forEach((_, index) => {
    const frame = document.createElement('div');
    frame.className = 'vertical-page';
    frame.dataset.index = index;
    frame.setAttribute('aria-label', `Página ${index + 1}`);
    fragment.append(frame);
  });
  ui.vertical.replaceChildren(fragment);
  state.verticalObserver = new IntersectionObserver(handleVerticalIntersection, { root: ui.stage, rootMargin: '120% 0px', threshold: [0, .25, .65] });
  ui.vertical.querySelectorAll('.vertical-page').forEach((page) => state.verticalObserver.observe(page));
  applyZoom();
  requestAnimationFrame(() => ui.vertical.children[state.currentIndex]?.scrollIntoView({ block: 'start' }));
  updateReaderMeta();
}

async function handleVerticalIntersection(entries) {
  const visible = entries.filter((entry) => entry.isIntersecting).sort((a, b) => b.intersectionRatio - a.intersectionRatio);
  for (const entry of visible) {
    const index = Number(entry.target.dataset.index);
    if (!entry.target.querySelector('img')) {
      const url = await getPageUrl(index);
      if (url && entry.target.isConnected) {
        const image = new Image();
        image.alt = `Página ${index + 1}`;
        image.decoding = 'async';
        image.src = url;
        entry.target.replaceChildren(image);
      }
    }
  }
  const current = visible.find((entry) => entry.intersectionRatio >= .25) || visible[0];
  if (current) {
    const index = Number(current.target.dataset.index);
    if (index !== state.currentIndex) {
      state.currentIndex = index;
      updateReaderMeta();
      queueProgressSave();
    }
  }
}

function updateReaderMeta() {
  if (!state.archive) return;
  const total = state.archive.pages.length;
  const spread = state.settings.readingMode === 'spread' && state.currentIndex + 1 < total;
  ui.counter.textContent = spread ? `Páginas ${state.currentIndex + 1}–${state.currentIndex + 2} / ${total}` : `Página ${state.currentIndex + 1} / ${total}`;
  ui.slider.value = state.currentIndex + 1;
  const step = state.settings.readingMode === 'spread' ? 2 : 1;
  $('#previous-page').disabled = state.currentIndex <= 0;
  $('#next-page').disabled = state.currentIndex + step >= total;
}

function queueProgressSave() {
  if (!state.currentBook) return;
  clearTimeout(state.saveTimer);
  state.saveTimer = setTimeout(async () => {
    const id = state.currentBook?.id;
    if (!id) return;
    await updateBook(id, { currentPage: state.currentIndex });
    const cached = state.books.find((book) => book.id === id);
    if (cached) cached.currentPage = state.currentIndex;
  }, 350);
}

function goToPage(index, behavior = 'auto') {
  if (!state.archive) return;
  state.currentIndex = Math.max(0, Math.min(index, state.archive.pages.length - 1));
  if (state.settings.readingMode === 'vertical') {
    ui.vertical.children[state.currentIndex]?.scrollIntoView({ behavior, block: 'start' });
    updateReaderMeta();
  } else renderPaged();
  queueProgressSave();
}

function nextPage() { goToPage(state.currentIndex + (state.settings.readingMode === 'spread' ? 2 : 1), 'smooth'); }
function previousPage() { goToPage(state.currentIndex - (state.settings.readingMode === 'spread' ? 2 : 1), 'smooth'); }

function applyReadingMode() {
  const mode = state.settings.readingMode;
  ui.stage.className = `page-stage mode-${mode}`;
  ui.paged.classList.toggle('hidden', mode === 'vertical');
  ui.vertical.classList.toggle('hidden', mode !== 'vertical');
  ui.paged.classList.remove('fit-best', 'fit-width', 'fit-height');
  ui.paged.classList.add(`fit-${state.settings.fit}`);
  ui.modeControl.querySelectorAll('button').forEach((button) => button.classList.toggle('active', button.dataset.value === mode));
  ui.quickMode.textContent = ({ page: '1 página', spread: '2 páginas', vertical: 'Vertical' })[mode];
  if (!state.archive) return;
  if (mode === 'vertical') setupVerticalReader(); else {
    state.verticalObserver?.disconnect();
    state.verticalObserver = null;
    renderPaged();
  }
}

function viewportAnchor() {
  return {
    x: (ui.stage.scrollLeft + ui.stage.clientWidth / 2) / Math.max(ui.stage.scrollWidth, 1),
    y: (ui.stage.scrollTop + ui.stage.clientHeight / 2) / Math.max(ui.stage.scrollHeight, 1),
  };
}

function layoutPagedImages(preserveCenter = true, anchor = preserveCenter ? viewportAnchor() : null) {
  const images = [...ui.paged.querySelectorAll('.page-image')].filter((image) => image.naturalWidth > 0);
  if (!images.length || state.settings.readingMode === 'vertical') return;
  const slots = state.settings.readingMode === 'spread' ? 2 : 1;
  const availableWidth = Math.max(1, (ui.stage.clientWidth - (slots - 1) * 2) / slots);
  const availableHeight = Math.max(1, ui.stage.clientHeight);

  for (const image of images) {
    const widthScale = availableWidth / image.naturalWidth;
    const heightScale = availableHeight / image.naturalHeight;
    let baseScale = Math.min(widthScale, heightScale);
    if (state.settings.fit === 'width') baseScale = widthScale;
    if (state.settings.fit === 'height') baseScale = heightScale;
    const scale = Math.max(.05, baseScale * state.settings.zoom);
    const width = Math.max(1, Math.round(image.naturalWidth * scale));
    const height = Math.max(1, Math.round(image.naturalHeight * scale));
    image.style.width = `${width}px`;
    image.style.height = `${height}px`;
    image.parentElement.style.width = `${width}px`;
    image.parentElement.style.height = `${height}px`;
  }

  requestAnimationFrame(() => {
    if (anchor) {
      ui.stage.scrollLeft = Math.max(0, anchor.x * ui.stage.scrollWidth - ui.stage.clientWidth / 2);
      ui.stage.scrollTop = Math.max(0, anchor.y * ui.stage.scrollHeight - ui.stage.clientHeight / 2);
    } else {
      ui.stage.scrollLeft = Math.max(0, (ui.stage.scrollWidth - ui.stage.clientWidth) / 2);
      ui.stage.scrollTop = Math.max(0, (ui.stage.scrollHeight - ui.stage.clientHeight) / 2);
    }
  });
}

function setZoom(value) {
  const anchor = viewportAnchor();
  state.settings.zoom = Math.max(.5, Math.min(3, Math.round(value * 4) / 4));
  applyZoom(anchor);
  saveSettings();
}

function applyZoom(anchor = null) {
  const zoom = state.settings.zoom;
  ui.zoomValue.value = `${Math.round(zoom * 100)}%`;
  ui.vertical.querySelectorAll('.vertical-page').forEach((page) => { page.style.width = `min(${zoom * 100}%, ${Math.round(900 * zoom)}px)`; });
  layoutPagedImages(Boolean(anchor), anchor);
}

function setReadingMode(mode) {
  state.settings.readingMode = mode;
  saveSettings();
  applyReadingMode();
}

function hideReaderHint() {
  clearTimeout(state.hintTimer);
  ui.readerHint.classList.add('hidden');
}

function showReaderHint() {
  if (sessionStorage.getItem('nebula-reader-hint-seen')) return;
  sessionStorage.setItem('nebula-reader-hint-seen', 'true');
  ui.readerHint.classList.remove('hidden');
  state.hintTimer = setTimeout(hideReaderHint, 5200);
}

function applyTheme() {
  document.documentElement.dataset.theme = state.settings.theme;
  const colors = { dark: '#0b1020', oled: '#000000', sepia: '#efe2c4', light: '#f3f6fa' };
  $('meta[name="theme-color"]').content = colors[state.settings.theme] || colors.dark;
  ui.themeControl.querySelectorAll('button').forEach((button) => button.classList.toggle('active', button.dataset.value === state.settings.theme));
}

function applyEyeFilter() {
  ui.eyeRange.value = state.settings.eyeFilter;
  ui.eyeValue.value = `${state.settings.eyeFilter}%`;
  ui.eyeFilter.style.opacity = String(state.settings.eyeFilter / 100 * .72);
}

async function applyOrientation(value, notify = true) {
  try {
    if (window.Capacitor?.isNativePlatform?.()) {
      const { ScreenOrientation } = await import('@capacitor/screen-orientation');
      if (value === 'any') await ScreenOrientation.unlock();
      else await ScreenOrientation.lock({ orientation: value });
      return true;
    }
    if (!screen.orientation?.lock) throw new Error('API no disponible');
    if (value === 'any') { screen.orientation.unlock?.(); return true; }
    if (!document.fullscreenElement && document.documentElement.requestFullscreen) await document.documentElement.requestFullscreen();
    await screen.orientation.lock(value);
    return true;
  } catch (error) {
    console.warn('Bloqueo de orientación no disponible', error);
    if (notify) toast('Este navegador no permite bloquear la orientación. Instala la PWA o usa la app móvil.', 'error');
    return false;
  }
}

async function saveSettings() { await setSetting('reader-preferences', state.settings); }

function syncSettingsUi() {
  ui.fitSelect.value = state.settings.fit;
  ui.orientationSelect.value = state.settings.orientation;
  applyTheme();
  applyEyeFilter();
  applyReadingMode();
  applyZoom();
}

function refreshChapterPicker() {
  ui.chapterSelect.replaceChildren(...state.books.map((book) => {
    const option = document.createElement('option');
    option.value = book.id;
    option.textContent = book.title;
    option.selected = book.id === state.currentBook?.id;
    return option;
  }));
  const index = state.books.findIndex((book) => book.id === state.currentBook?.id);
  ui.previousChapter.disabled = index <= 0;
  ui.nextChapter.disabled = index < 0 || index >= state.books.length - 1;
  ui.currentBookActions.classList.toggle('hidden', state.currentBook?.format !== 'cbr');
}

async function changeChapter(offset) {
  const index = state.books.findIndex((book) => book.id === state.currentBook?.id);
  const target = state.books[index + offset];
  if (target) await openBook(target.id);
}

async function closeReader() {
  clearTimeout(state.saveTimer);
  if (state.currentBook) await updateBook(state.currentBook.id, { currentPage: state.currentIndex });
  await closeArchiveSession();
  state.currentBook = null;
  ui.readerView.classList.add('hidden');
  ui.libraryView.classList.remove('hidden');
  if (document.fullscreenElement) document.exitFullscreen().catch(() => {});
  screen.orientation?.unlock?.();
  await refreshLibrary();
}

function showSettings() {
  refreshChapterPicker();
  if (!ui.settings.open) ui.settings.showModal();
}

function resetHudTimer() {
  ui.topbar.classList.add('visible');
  ui.bottombar.classList.add('visible');
  clearTimeout(state.hudTimer);
  state.hudTimer = setTimeout(() => {
    if (!ui.settings.open) {
      ui.topbar.classList.remove('visible');
      ui.bottombar.classList.remove('visible');
    }
  }, 3200);
}

async function convertCurrentBook() {
  const book = state.currentBook;
  if (!book || book.format !== 'cbr') return;
  ui.settings.close();
  setLoading(true, 'Preparando conversión…');
  try {
    const cbz = await convertRarToCbz(book.blob, book.fileName, (done, total) => setLoading(true, `Convirtiendo página ${done} de ${total}…`));
    const url = URL.createObjectURL(cbz);
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = `${baseTitle(book.fileName)}.cbz`;
    anchor.click();
    setTimeout(() => URL.revokeObjectURL(url), 30_000);
    toast('CBZ convertido. Revisa tus descargas.');
  } catch (error) {
    console.error(error);
    toast(`No se pudo convertir: ${error.message}`, 'error');
  } finally { setLoading(false); }
}

function bindEvents() {
  $('#import-button').addEventListener('click', () => ui.fileInput.click());
  ui.fileInput.addEventListener('change', () => importFiles(ui.fileInput.files));
  ui.dropzone.addEventListener('click', () => ui.fileInput.click());
  ui.dropzone.addEventListener('keydown', (event) => { if (event.key === 'Enter' || event.key === ' ') { event.preventDefault(); ui.fileInput.click(); } });
  for (const eventName of ['dragenter', 'dragover']) ui.dropzone.addEventListener(eventName, (event) => { event.preventDefault(); ui.dropzone.classList.add('dragging'); });
  for (const eventName of ['dragleave', 'drop']) ui.dropzone.addEventListener(eventName, (event) => { event.preventDefault(); ui.dropzone.classList.remove('dragging'); });
  ui.dropzone.addEventListener('drop', (event) => importFiles(event.dataTransfer.files));
  $('#open-settings').addEventListener('click', showSettings);
  $('#reader-settings').addEventListener('click', showSettings);
  $('#brand-home').addEventListener('click', () => { if (!ui.readerView.classList.contains('hidden')) closeReader(); });
  $('#close-reader').addEventListener('click', closeReader);
  $('#next-page').addEventListener('click', nextPage);
  $('#previous-page').addEventListener('click', previousPage);
  $('#next-page-zone').addEventListener('click', () => { if (!state.suppressZoneClick) nextPage(); });
  $('#previous-page-zone').addEventListener('click', () => { if (!state.suppressZoneClick) previousPage(); });
  ui.slider.addEventListener('input', () => goToPage(Number(ui.slider.value) - 1));
  $('#zoom-in').addEventListener('click', () => setZoom(state.settings.zoom + .25));
  $('#zoom-out').addEventListener('click', () => setZoom(state.settings.zoom - .25));
  $('#zoom-reset').addEventListener('click', () => setZoom(1));
  ui.quickMode.addEventListener('click', () => {
    const modes = ['page', 'spread', 'vertical'];
    setReadingMode(modes[(modes.indexOf(state.settings.readingMode) + 1) % modes.length]);
  });
  $('#reader-fullscreen').addEventListener('click', async () => {
    if (document.fullscreenElement) await document.exitFullscreen(); else await document.documentElement.requestFullscreen().catch(() => toast('Pantalla completa no disponible.', 'error'));
    await applyOrientation(state.settings.orientation, false);
  });
  ui.modeControl.addEventListener('click', (event) => {
    const button = event.target.closest('[data-value]');
    if (!button) return;
    setReadingMode(button.dataset.value);
  });
  ui.fitSelect.addEventListener('change', () => {
    state.settings.fit = ui.fitSelect.value;
    saveSettings();
    applyReadingMode();
  });
  ui.orientationSelect.addEventListener('change', async () => {
    state.settings.orientation = ui.orientationSelect.value;
    await saveSettings();
    if (state.currentBook) await applyOrientation(state.settings.orientation);
  });
  ui.eyeRange.addEventListener('input', () => {
    state.settings.eyeFilter = Number(ui.eyeRange.value);
    applyEyeFilter();
    saveSettings();
  });
  ui.themeControl.addEventListener('click', (event) => {
    const button = event.target.closest('[data-value]');
    if (!button) return;
    state.settings.theme = button.dataset.value;
    applyTheme();
    saveSettings();
  });
  ui.convertButton.addEventListener('click', convertCurrentBook);
  ui.chapterSelect.addEventListener('change', () => openBook(ui.chapterSelect.value));
  ui.previousChapter.addEventListener('click', () => changeChapter(-1));
  ui.nextChapter.addEventListener('click', () => changeChapter(1));
  ui.stage.addEventListener('mousemove', resetHudTimer);
  ui.stage.addEventListener('click', (event) => { if (!event.target.closest('.page-zone')) resetHudTimer(); });
  ui.stage.addEventListener('pointerdown', (event) => {
    if (event.pointerType !== 'mouse' || event.button !== 0 || state.settings.readingMode === 'vertical') return;
    state.mousePan = { x: event.clientX, y: event.clientY, left: ui.stage.scrollLeft, top: ui.stage.scrollTop, moved: false };
    ui.stage.classList.add('is-panning');
    ui.stage.setPointerCapture(event.pointerId);
    hideReaderHint();
  });
  ui.stage.addEventListener('pointermove', (event) => {
    if (!state.mousePan) return;
    const deltaX = event.clientX - state.mousePan.x;
    const deltaY = event.clientY - state.mousePan.y;
    if (Math.abs(deltaX) + Math.abs(deltaY) > 4) state.mousePan.moved = true;
    ui.stage.scrollLeft = state.mousePan.left - deltaX;
    ui.stage.scrollTop = state.mousePan.top - deltaY;
    event.preventDefault();
  });
  const finishMousePan = (event) => {
    if (!state.mousePan) return;
    const moved = state.mousePan.moved;
    state.mousePan = null;
    ui.stage.classList.remove('is-panning');
    if (ui.stage.hasPointerCapture(event.pointerId)) ui.stage.releasePointerCapture(event.pointerId);
    if (moved) {
      state.suppressZoneClick = true;
      setTimeout(() => { state.suppressZoneClick = false; }, 0);
    }
  };
  ui.stage.addEventListener('pointerup', finishMousePan);
  ui.stage.addEventListener('pointercancel', finishMousePan);
  ui.stage.addEventListener('wheel', (event) => {
    hideReaderHint();
    if (event.ctrlKey) {
      event.preventDefault();
      setZoom(state.settings.zoom + (event.deltaY < 0 ? .25 : -.25));
    } else if (event.shiftKey && state.settings.readingMode !== 'vertical') {
      event.preventDefault();
      ui.stage.scrollLeft += event.deltaY || event.deltaX;
    }
  }, { passive: false });
  ui.stage.addEventListener('touchstart', (event) => {
    resetHudTimer();
    hideReaderHint();
    if (event.touches.length === 2) {
      state.touchDistance = Math.hypot(event.touches[0].clientX - event.touches[1].clientX, event.touches[0].clientY - event.touches[1].clientY);
      state.touchZoom = state.settings.zoom;
    }
  }, { passive: true });
  ui.stage.addEventListener('touchmove', (event) => {
    if (event.touches.length !== 2 || !state.touchDistance) return;
    event.preventDefault();
    const distance = Math.hypot(event.touches[0].clientX - event.touches[1].clientX, event.touches[0].clientY - event.touches[1].clientY);
    setZoom(state.touchZoom * distance / state.touchDistance);
  }, { passive: false });
  window.addEventListener('keydown', (event) => {
    if (ui.readerView.classList.contains('hidden') || ui.settings.open) return;
    if (event.shiftKey && ['ArrowRight', 'ArrowLeft', 'ArrowUp', 'ArrowDown'].includes(event.key)) {
      event.preventDefault();
      const distance = Math.max(100, Math.round(Math.min(ui.stage.clientWidth, ui.stage.clientHeight) * .22));
      ui.stage.scrollBy({
        left: event.key === 'ArrowRight' ? distance : event.key === 'ArrowLeft' ? -distance : 0,
        top: event.key === 'ArrowDown' ? distance : event.key === 'ArrowUp' ? -distance : 0,
        behavior: 'smooth',
      });
      return;
    }
    if (event.key === 'ArrowRight' || event.key === ' ') { event.preventDefault(); nextPage(); }
    if (event.key === 'ArrowLeft') { event.preventDefault(); previousPage(); }
    if (event.key === 'Escape') closeReader();
    if (event.key === '+' || event.key === '=') setZoom(state.settings.zoom + .25);
    if (event.key === '-') setZoom(state.settings.zoom - .25);
    resetHudTimer();
  });
  window.addEventListener('online', updateConnectionBadge);
  window.addEventListener('offline', updateConnectionBadge);
  window.addEventListener('resize', () => layoutPagedImages(true));
  window.addEventListener('beforeinstallprompt', (event) => {
    event.preventDefault();
    state.installPrompt = event;
    ui.installButton.classList.remove('hidden');
  });
  ui.installButton.addEventListener('click', async () => {
    await state.installPrompt?.prompt();
    state.installPrompt = null;
    ui.installButton.classList.add('hidden');
  });
}

function updateConnectionBadge() {
  ui.offlineBadge.textContent = navigator.onLine ? 'Disponible offline' : 'Modo sin conexión';
}

async function init() {
  try {
    await openDatabase();
    state.settings = { ...DEFAULT_SETTINGS, ...await getSetting('reader-preferences', DEFAULT_SETTINGS) };
    bindEvents();
    syncSettingsUi();
    updateConnectionBadge();
    await refreshLibrary();
    if ('serviceWorker' in navigator) {
      if (import.meta.env?.PROD && (location.protocol === 'https:' || location.hostname === 'localhost')) {
        navigator.serviceWorker.register('/sw.js').catch((error) => console.warn('Service worker no registrado', error));
      } else {
        const registrations = await navigator.serviceWorker.getRegistrations();
        await Promise.all(registrations.map((registration) => registration.unregister()));
        if ('caches' in window) {
          const cacheNames = await caches.keys();
          await Promise.all(cacheNames.filter((name) => name.startsWith('nebula-reader-')).map((name) => caches.delete(name)));
        }
      }
    }
  } catch (error) {
    console.error(error);
    toast(`No se pudo iniciar la biblioteca: ${error.message}`, 'error');
  }
}

init();
