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
  libraryStats: $('#library-stats'), librarySearch: $('#library-search'), libraryFilter: $('#library-filter'),
  libraryViewMode: $('#library-view-mode'), recommendations: $('#recommendations'), recommendationList: $('#recommendation-list'),
  readerFavorite: $('#reader-favorite'), pageBookmark: $('#page-bookmark'), notesDialog: $('#notes-dialog'),
  bookDialog: $('#book-dialog'), directionControl: $('#reading-direction-control'), marginRange: $('#page-margin-range'),
  marginValue: $('#page-margin-value'), brightnessRange: $('#brightness-range'), brightnessValue: $('#brightness-value'),
  adaptiveBrightness: $('#adaptive-brightness'), bookmarkList: $('#bookmark-list'),
};

const DEFAULT_SETTINGS = {
  theme: 'dark', readingMode: 'page', readingDirection: 'ltr', fit: 'best', orientation: 'any',
  eyeFilter: 0, zoom: 1, pageMargin: 2, brightness: 100, adaptiveBrightness: false, libraryView: 'grid',
};
const state = {
  books: [], currentBook: null, archive: null, currentIndex: 0, settings: { ...DEFAULT_SETTINGS },
  sessionId: 0, renderId: 0, urlPromises: new Map(), objectUrls: new Map(), verticalObserver: null,
  hudTimer: null, hintTimer: null, saveTimer: null, installPrompt: null, touchDistance: 0, touchZoom: 1,
  mousePan: null, suppressZoneClick: false,
  libraryQuery: '', libraryFilter: 'all', readingStartedAt: 0, sessionPages: new Set(), panelCache: new Map(), lastTap: null,
};

function inferSeries(title = '') {
  return title.replace(/\([^)]*(digital|scan|\d{4})[^)]*\)/gi, '').replace(/(?:#|n[º°.]?|vol(?:umen)?|cap(?:ítulo)?|issue)\s*\d+[\w.-]*$/i, '').replace(/[\s_-]+\d{1,4}$/i, '').trim();
}

function normalizeBook(book) {
  return {
    favorite: false, bookmarks: [], tags: [], author: '', publisher: '', year: '', summary: '', number: '',
    series: inferSeries(book.title), totalReadingMs: 0, pagesRead: [], lastReadAt: 0, ...book,
    tags: Array.isArray(book.tags) ? book.tags : String(book.tags || '').split(',').map((tag) => tag.trim()).filter(Boolean),
    bookmarks: Array.isArray(book.bookmarks) ? book.bookmarks : [], pagesRead: Array.isArray(book.pagesRead) ? book.pagesRead : [],
  };
}

function formatDuration(milliseconds = 0) {
  const minutes = Math.floor(milliseconds / 60000);
  return minutes < 60 ? `${minutes} min` : `${Math.floor(minutes / 60)} h ${minutes % 60} min`;
}

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

function legacyCreateBookCard(book) {
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

function createBookCard(book) {
  const article = document.createElement('article');
  article.className = 'book-card'; article.dataset.id = book.id;
  const open = document.createElement('button');
  open.type = 'button'; open.className = 'cover-button'; open.setAttribute('aria-label', `Leer ${book.title}`); open.addEventListener('click', () => openBook(book.id));
  const cover = document.createElement('div'); cover.className = 'cover';
  if (book.cover) {
    const image = new Image(); const url = URL.createObjectURL(book.cover); image.alt = `Portada de ${book.title}`; image.src = url;
    image.onload = image.onerror = () => URL.revokeObjectURL(url); cover.append(image);
  } else { const placeholder = document.createElement('div'); placeholder.className = 'cover-placeholder'; placeholder.textContent = book.title; cover.append(placeholder); }
  const progress = book.pageCount ? Math.min(100, ((book.currentPage || 0) + 1) / book.pageCount * 100) : 0;
  const track = document.createElement('div'); track.className = 'progress-track'; const fill = document.createElement('span'); fill.style.width = `${progress}%`; track.append(fill); cover.append(track);
  const info = document.createElement('div'); info.className = 'book-info';
  const title = document.createElement('strong'); title.textContent = book.title;
  const detail = document.createElement('small'); detail.textContent = `${book.series || book.author || extOf(book.fileName).toUpperCase()} · ${book.pageCount || '—'} páginas`;
  info.append(title, detail); open.append(cover, info);
  const actions = document.createElement('div'); actions.className = 'book-menu';
  const favorite = document.createElement('button'); favorite.type = 'button'; favorite.className = `icon-button book-favorite${book.favorite ? ' active' : ''}`; favorite.textContent = book.favorite ? '★' : '☆'; favorite.title = 'Favorito'; favorite.setAttribute('aria-label', `Marcar ${book.title} como favorito`); favorite.addEventListener('click', () => toggleFavorite(book));
  const edit = document.createElement('button'); edit.type = 'button'; edit.className = 'icon-button book-edit'; edit.textContent = '⋮'; edit.title = 'Editar'; edit.setAttribute('aria-label', `Editar ${book.title}`); edit.addEventListener('click', () => openBookEditor(book));
  actions.append(favorite, edit); article.append(open, actions); return article;
}

async function refreshLibrary() {
  state.books = (await getAllBooks()).map(normalizeBook);
  renderLibrary(); renderStatistics(); renderRecommendations();
  ui.emptyLibrary.classList.toggle('hidden', state.books.length > 0);
  ui.bookCount.textContent = `${state.books.length} ${state.books.length === 1 ? 'capítulo' : 'capítulos'}`;
  const estimate = await getStorageEstimate();
  ui.storageUsage.textContent = estimate ? `${formatBytes(estimate.usage)} usados` : '';
  refreshChapterPicker();
}

function visibleBooks() {
  const query = state.libraryQuery.toLocaleLowerCase();
  return state.books.filter((book) => {
    const searchable = [book.title, book.series, book.author, book.publisher, ...book.tags].join(' ').toLocaleLowerCase();
    if (query && !searchable.includes(query)) return false;
    if (state.libraryFilter === 'favorites') return book.favorite;
    if (state.libraryFilter === 'reading') return (book.currentPage || 0) > 0 && (book.currentPage || 0) < book.pageCount - 1;
    if (state.libraryFilter === 'unread') return !(book.currentPage || 0);
    return true;
  });
}

function renderLibrary() {
  const books = visibleBooks(); ui.libraryGrid.classList.toggle('shelf-view', state.settings.libraryView === 'shelf');
  if (state.settings.libraryView === 'shelf') {
    const groups = books.reduce((map, book) => map.set(book.series || 'Independientes', [...(map.get(book.series || 'Independientes') || []), book]), new Map());
    ui.libraryGrid.replaceChildren(...[...groups].map(([series, items]) => {
      const section = document.createElement('section'); section.className = 'shelf'; const heading = document.createElement('h3'); heading.textContent = series;
      const row = document.createElement('div'); row.className = 'shelf-row'; row.append(...items.map(createBookCard)); section.append(heading, row); return section;
    }));
  } else ui.libraryGrid.replaceChildren(...books.map(createBookCard));
  ui.libraryViewMode.textContent = state.settings.libraryView === 'shelf' ? '▦' : '▤';
}

function renderStatistics() {
  const values = [['◷', formatDuration(state.books.reduce((sum, book) => sum + (book.totalReadingMs || 0), 0)), 'Tiempo leído'], ['▤', state.books.reduce((sum, book) => sum + new Set(book.pagesRead || []).size, 0), 'Páginas vistas'], ['★', state.books.filter((book) => book.favorite).length, 'Favoritos'], ['✓', state.books.filter((book) => book.pageCount && book.currentPage >= book.pageCount - 1).length, 'Completados']];
  ui.libraryStats.replaceChildren(...values.map(([icon, value, label]) => { const item = document.createElement('div'); item.className = 'stat-card'; const symbol = document.createElement('span'); symbol.textContent = icon; const strong = document.createElement('strong'); strong.textContent = value; const small = document.createElement('small'); small.textContent = label; item.append(symbol, strong, small); return item; }));
}

function renderRecommendations() {
  const reference = [...state.books].sort((a, b) => (b.lastReadAt || 0) - (a.lastReadAt || 0))[0];
  const items = reference ? state.books.filter((book) => book.id !== reference.id).map((book) => ({ book, score: (book.series && book.series === reference.series ? 5 : 0) + (book.author && book.author === reference.author ? 2 : 0) + book.tags.filter((tag) => reference.tags.includes(tag)).length * 2 + (!(book.currentPage || 0) ? 1 : 0) })).filter(({ score }) => score > 0).sort((a, b) => b.score - a.score).slice(0, 4) : [];
  ui.recommendations.classList.toggle('hidden', !items.length);
  ui.recommendationList.replaceChildren(...items.map(({ book }) => { const button = document.createElement('button'); button.type = 'button'; button.className = 'recommendation-card'; button.textContent = `› ${book.title}`; button.addEventListener('click', () => openBook(book.id)); return button; }));
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
      const metadata = details.metadata || {};
      const title = existing?.title || metadata.title || baseTitle(file.name);
      await saveBook(normalizeBook({
        ...existing, id, title, normalizedTitle: title.toLocaleLowerCase(), fileName: file.name,
        format: extOf(file.name), blob: file, cover: existing?.cover || details.cover, pageCount: details.pageCount,
        series: existing?.series || metadata.series || inferSeries(title), number: existing?.number || metadata.number || '',
        author: existing?.author || metadata.author || '', publisher: existing?.publisher || metadata.publisher || '',
        year: existing?.year || metadata.year || '', summary: existing?.summary || metadata.summary || '', tags: existing?.tags || metadata.tags || [],
        currentPage: existing?.currentPage || 0, addedAt: existing?.addedAt || Date.now(), updatedAt: Date.now(), size: file.size,
      }));
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

async function toggleFavorite(book = state.currentBook) {
  if (!book) return;
  const updated = normalizeBook(await updateBook(book.id, { favorite: !book.favorite }));
  if (state.currentBook?.id === updated.id) state.currentBook = updated;
  const cached = state.books.findIndex((item) => item.id === updated.id); if (cached >= 0) state.books[cached] = updated;
  updateReaderActions(); renderLibrary(); renderStatistics();
}

function openBookEditor(book) {
  const fields = { '#book-id': book.id, '#book-title-input': book.title, '#book-series-input': book.series, '#book-number-input': book.number, '#book-author-input': book.author, '#book-publisher-input': book.publisher, '#book-year-input': book.year, '#book-tags-input': book.tags.join(', '), '#book-summary-input': book.summary };
  for (const [selector, value] of Object.entries(fields)) $(selector).value = value || '';
  $('#book-cover-input').value = ''; ui.bookDialog.showModal();
}

async function saveBookEditor(event) {
  event.preventDefault(); const id = $('#book-id').value; const book = await getBook(id); if (!book) return;
  const coverFile = $('#book-cover-input').files[0];
  const patch = { title: $('#book-title-input').value.trim(), series: $('#book-series-input').value.trim(), number: $('#book-number-input').value.trim(), author: $('#book-author-input').value.trim(), publisher: $('#book-publisher-input').value.trim(), year: $('#book-year-input').value.trim(), tags: $('#book-tags-input').value.split(',').map((tag) => tag.trim()).filter(Boolean), summary: $('#book-summary-input').value.trim() };
  patch.normalizedTitle = patch.title.toLocaleLowerCase(); if (coverFile) patch.cover = coverFile;
  const updated = normalizeBook(await updateBook(id, patch)); if (state.currentBook?.id === id) { state.currentBook = updated; ui.title.textContent = updated.title; }
  ui.bookDialog.close(); await refreshLibrary(); toast('Información guardada.');
}

function currentBookmark() { return state.currentBook?.bookmarks.find((item) => item.page === state.currentIndex); }

function updateReaderActions() {
  if (!state.currentBook) return;
  ui.readerFavorite.textContent = state.currentBook.favorite ? '★' : '☆'; ui.readerFavorite.classList.toggle('active', state.currentBook.favorite);
  const marked = Boolean(currentBookmark()); ui.pageBookmark.textContent = marked ? '♠' : '♧'; ui.pageBookmark.classList.toggle('active', marked);
}

async function saveBookmarks(bookmarks) {
  state.currentBook = normalizeBook(await updateBook(state.currentBook.id, { bookmarks }));
  const cached = state.books.findIndex((book) => book.id === state.currentBook.id); if (cached >= 0) state.books[cached] = state.currentBook;
  updateReaderActions(); renderBookmarkList();
}

async function togglePageBookmark() {
  const existing = currentBookmark(); const bookmarks = state.currentBook.bookmarks.filter((item) => item.page !== state.currentIndex);
  if (!existing) bookmarks.push({ page: state.currentIndex, note: '', createdAt: Date.now() });
  await saveBookmarks(bookmarks); toast(existing ? 'Marcador eliminado.' : 'Página marcada.');
}

function renderBookmarkList() {
  if (!state.currentBook) return;
  ui.bookmarkList.replaceChildren(...[...state.currentBook.bookmarks].sort((a, b) => a.page - b.page).map((bookmark) => {
    const row = document.createElement('button'); row.type = 'button'; row.className = 'bookmark-item'; row.textContent = `Página ${bookmark.page + 1}${bookmark.note ? ` · ${bookmark.note}` : ''}`;
    row.addEventListener('click', () => { ui.notesDialog.close(); goToPage(bookmark.page, 'smooth'); }); return row;
  }));
}

function openNotes() {
  const bookmark = currentBookmark(); $('#note-page-number').textContent = state.currentIndex + 1; $('#page-note-input').value = bookmark?.note || '';
  $('#remove-page-note').disabled = !bookmark; renderBookmarkList(); ui.notesDialog.showModal();
}

async function saveNote(event) {
  event.preventDefault(); const note = $('#page-note-input').value.trim(); const bookmarks = state.currentBook.bookmarks.filter((item) => item.page !== state.currentIndex);
  bookmarks.push({ page: state.currentIndex, note, createdAt: currentBookmark()?.createdAt || Date.now() }); await saveBookmarks(bookmarks); ui.notesDialog.close(); toast('Nota guardada.');
}

async function removeNote() { await saveBookmarks(state.currentBook.bookmarks.filter((item) => item.page !== state.currentIndex)); ui.notesDialog.close(); }

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
  await commitReadingSession();
  await closeArchiveSession();
  const sessionId = state.sessionId;
  state.currentBook = normalizeBook(book);
  state.currentIndex = Math.min(book.currentPage || 0, Math.max(0, (book.pageCount || 1) - 1));
  state.readingStartedAt = Date.now(); state.sessionPages = new Set(state.currentBook.pagesRead); state.sessionPages.add(state.currentIndex);

  try {
    const archive = await openComicArchive(book.blob, book.fileName);
    if (sessionId !== state.sessionId) return archive.close();
    state.archive = archive;
    if (archive.pages.length !== book.pageCount) {
      state.currentBook = await updateBook(book.id, { pageCount: archive.pages.length });
      await refreshLibrary();
    }
    ui.title.textContent = state.currentBook.title;
    ui.slider.max = archive.pages.length;
    refreshChapterPicker();
    applyReadingMode();
    applyZoom();
    setLoading(false);
    resetHudTimer();
    showReaderHint();
    updateReaderActions();
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
  frame.dataset.index = index;
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
  const displayIndexes = spread && state.settings.readingDirection === 'rtl' ? [...indexes].reverse() : indexes;
  ui.paged.style.opacity = '.3';
  const urls = await Promise.all(displayIndexes.map(getPageUrl));
  if (renderId !== state.renderId || !state.archive) return;
  ui.paged.replaceChildren(...displayIndexes.map((index, position) => makePageImage(urls[position], index)));
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
      state.sessionPages.add(index);
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
  const rtl = state.settings.readingDirection === 'rtl';
  $('#previous-page').textContent = rtl ? '→' : '←'; $('#next-page').textContent = rtl ? '←' : '→';
  updateReaderActions();
}

function queueProgressSave() {
  if (!state.currentBook) return;
  clearTimeout(state.saveTimer);
  state.saveTimer = setTimeout(async () => {
    const id = state.currentBook?.id;
    if (!id) return;
    await updateBook(id, { currentPage: state.currentIndex, lastReadAt: Date.now(), pagesRead: [...state.sessionPages] });
    const cached = state.books.find((book) => book.id === id);
    if (cached) cached.currentPage = state.currentIndex;
  }, 350);
}

function goToPage(index, behavior = 'auto') {
  if (!state.archive) return;
  state.currentIndex = Math.max(0, Math.min(index, state.archive.pages.length - 1));
  state.sessionPages.add(state.currentIndex);
  if (state.settings.readingMode === 'vertical') {
    ui.vertical.children[state.currentIndex]?.scrollIntoView({ behavior, block: 'start' });
    updateReaderMeta();
  } else renderPaged();
  queueProgressSave();
}

function navigateVisual(side) {
  const next = state.settings.readingDirection === 'rtl' ? side === 'left' : side === 'right';
  if (next) nextPage(); else previousPage();
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
  const modeInfo = { page: ['▯', 'Modo de una página'], spread: ['▯▯', 'Modo de doble página'], vertical: ['↧', 'Lectura fluida'] }[mode];
  ui.quickMode.textContent = modeInfo[0]; ui.quickMode.setAttribute('aria-label', modeInfo[1]); ui.quickMode.title = modeInfo[1];
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
  const availableWidth = Math.max(1, (ui.stage.clientWidth - (slots - 1) * state.settings.pageMargin) / slots);
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

function detectPanels(image) {
  const key = image.src; if (state.panelCache.has(key)) return state.panelCache.get(key);
  const scale = Math.min(1, 420 / Math.max(image.naturalWidth, image.naturalHeight)); const canvas = document.createElement('canvas');
  canvas.width = Math.max(1, Math.round(image.naturalWidth * scale)); canvas.height = Math.max(1, Math.round(image.naturalHeight * scale));
  const context = canvas.getContext('2d', { willReadFrequently: true }); context.drawImage(image, 0, 0, canvas.width, canvas.height);
  const data = context.getImageData(0, 0, canvas.width, canvas.height).data;
  const gutters = (vertical) => {
    const length = vertical ? canvas.width : canvas.height; const cross = vertical ? canvas.height : canvas.width; const result = [];
    for (let position = 0; position < length; position += 1) { let ink = 0; for (let other = 0; other < cross; other += 3) { const offset = (vertical ? other * canvas.width + position : position * canvas.width + other) * 4; if ((data[offset] + data[offset + 1] + data[offset + 2]) / 3 < 205) ink += 1; } if (ink / Math.ceil(cross / 3) < .018) result.push(position); }
    const bands = []; for (const position of result) { const last = bands.at(-1); if (last && position <= last[1] + 1) last[1] = position; else bands.push([position, position]); }
    return bands.filter(([start, end]) => end - start >= 2).map(([start, end]) => (start + end) / 2 / length).filter((value) => value > .08 && value < .92);
  };
  const xs = [0, ...gutters(true), 1]; const ys = [0, ...gutters(false), 1]; const panels = [];
  if ((xs.length - 1) * (ys.length - 1) <= 12) for (let y = 0; y < ys.length - 1; y += 1) for (let x = 0; x < xs.length - 1; x += 1) if (xs[x + 1] - xs[x] > .16 && ys[y + 1] - ys[y] > .12) panels.push({ x: xs[x], y: ys[y], w: xs[x + 1] - xs[x], h: ys[y + 1] - ys[y] });
  state.panelCache.set(key, panels); return panels;
}

function smartZoom(image, clientX, clientY) {
  if (state.settings.readingMode === 'vertical') return;
  if (state.settings.zoom > 1.4) { setZoom(1); return; }
  const rect = image.getBoundingClientRect(); const x = Math.max(0, Math.min(1, (clientX - rect.left) / rect.width)); const y = Math.max(0, Math.min(1, (clientY - rect.top) / rect.height));
  const panel = detectPanels(image).find((candidate) => x >= candidate.x && x <= candidate.x + candidate.w && y >= candidate.y && y <= candidate.y + candidate.h) || { x: Math.max(0, x - .25), y: Math.max(0, y - .25), w: .5, h: .5 };
  const target = Math.max(1.5, Math.min(3, Math.min(1 / panel.w, 1 / panel.h) * .9)); setZoom(target);
  requestAnimationFrame(() => { const frame = image.parentElement; const centerX = frame.offsetLeft + (panel.x + panel.w / 2) * image.clientWidth; const centerY = frame.offsetTop + (panel.y + panel.h / 2) * image.clientHeight; ui.stage.scrollTo({ left: centerX - ui.stage.clientWidth / 2, top: centerY - ui.stage.clientHeight / 2, behavior: 'smooth' }); });
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

function applyDisplaySettings() {
  ui.marginRange.value = state.settings.pageMargin; ui.marginValue.value = `${state.settings.pageMargin} px`;
  ui.paged.style.setProperty('--page-gap', `${state.settings.pageMargin}px`); ui.vertical.style.setProperty('--page-gap', `${state.settings.pageMargin}px`);
  ui.brightnessRange.value = state.settings.brightness; ui.brightnessValue.value = `${state.settings.brightness}%`; ui.adaptiveBrightness.checked = state.settings.adaptiveBrightness;
  const factor = state.settings.adaptiveBrightness && matchMedia('(prefers-color-scheme: dark)').matches ? .82 : 1;
  const filter = `brightness(${Math.round(state.settings.brightness * factor)}%)`; ui.paged.style.filter = filter; ui.vertical.style.filter = filter;
  ui.directionControl.querySelectorAll('button').forEach((button) => button.classList.toggle('active', button.dataset.value === state.settings.readingDirection));
  layoutPagedImages(true);
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
  applyDisplaySettings();
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

async function commitReadingSession() {
  if (!state.currentBook || !state.readingStartedAt) return;
  const elapsed = Math.max(0, Date.now() - state.readingStartedAt);
  state.currentBook = normalizeBook(await updateBook(state.currentBook.id, { currentPage: state.currentIndex, lastReadAt: Date.now(), pagesRead: [...state.sessionPages], totalReadingMs: (state.currentBook.totalReadingMs || 0) + elapsed }));
  state.readingStartedAt = 0;
}

async function closeReader() {
  clearTimeout(state.saveTimer);
  await commitReadingSession();
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
  ui.librarySearch.addEventListener('input', () => { state.libraryQuery = ui.librarySearch.value; renderLibrary(); });
  ui.libraryFilter.addEventListener('change', () => { state.libraryFilter = ui.libraryFilter.value; renderLibrary(); });
  ui.libraryViewMode.addEventListener('click', () => { state.settings.libraryView = state.settings.libraryView === 'grid' ? 'shelf' : 'grid'; saveSettings(); renderLibrary(); });
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
  $('#next-page-zone').addEventListener('click', () => { if (!state.suppressZoneClick) navigateVisual('right'); });
  $('#previous-page-zone').addEventListener('click', () => { if (!state.suppressZoneClick) navigateVisual('left'); });
  ui.readerFavorite.addEventListener('click', () => toggleFavorite());
  ui.pageBookmark.addEventListener('click', togglePageBookmark);
  $('#open-notes').addEventListener('click', openNotes);
  $('#close-notes').addEventListener('click', () => ui.notesDialog.close());
  $('#note-form').addEventListener('submit', saveNote);
  $('#remove-page-note').addEventListener('click', removeNote);
  $('#close-book-dialog').addEventListener('click', () => ui.bookDialog.close());
  $('#book-form').addEventListener('submit', saveBookEditor);
  $('#delete-book').addEventListener('click', async () => { const book = state.books.find((item) => item.id === $('#book-id').value); ui.bookDialog.close(); if (book) confirmDelete(book); });
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
  ui.directionControl.addEventListener('click', (event) => {
    const button = event.target.closest('[data-value]'); if (!button) return;
    state.settings.readingDirection = button.dataset.value; saveSettings(); applyDisplaySettings(); if (state.archive && state.settings.readingMode !== 'vertical') renderPaged(); else updateReaderMeta();
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
  ui.marginRange.addEventListener('input', () => { state.settings.pageMargin = Number(ui.marginRange.value); applyDisplaySettings(); saveSettings(); });
  ui.brightnessRange.addEventListener('input', () => { state.settings.brightness = Number(ui.brightnessRange.value); applyDisplaySettings(); saveSettings(); });
  ui.adaptiveBrightness.addEventListener('change', () => { state.settings.adaptiveBrightness = ui.adaptiveBrightness.checked; applyDisplaySettings(); saveSettings(); });
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
  ui.stage.addEventListener('dblclick', (event) => { if (event.target.matches('.page-image')) { event.preventDefault(); smartZoom(event.target, event.clientX, event.clientY); } });
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
  ui.stage.addEventListener('touchend', (event) => {
    if (event.changedTouches.length !== 1 || !event.target.matches('.page-image')) return;
    const touch = event.changedTouches[0]; const now = Date.now();
    if (state.lastTap && now - state.lastTap.time < 320 && Math.hypot(touch.clientX - state.lastTap.x, touch.clientY - state.lastTap.y) < 35) { event.preventDefault(); smartZoom(event.target, touch.clientX, touch.clientY); state.lastTap = null; } else state.lastTap = { time: now, x: touch.clientX, y: touch.clientY };
  }, { passive: false });
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
    if (event.key === 'ArrowRight') { event.preventDefault(); navigateVisual('right'); }
    if (event.key === 'ArrowLeft') { event.preventDefault(); navigateVisual('left'); }
    if (event.key === ' ') { event.preventDefault(); nextPage(); }
    if (event.key === 'Escape') closeReader();
    if (event.key === '+' || event.key === '=') setZoom(state.settings.zoom + .25);
    if (event.key === '-') setZoom(state.settings.zoom - .25);
    resetHudTimer();
  });
  window.addEventListener('online', updateConnectionBadge);
  window.addEventListener('offline', updateConnectionBadge);
  window.addEventListener('resize', () => layoutPagedImages(true));
  document.addEventListener('visibilitychange', async () => { if (document.hidden) await commitReadingSession(); else if (state.currentBook) state.readingStartedAt = Date.now(); });
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
