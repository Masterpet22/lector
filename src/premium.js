import './premium.css';
import { getAllBooks, getStorageEstimate } from './db.js';

const $ = (selector, root = document) => root.querySelector(selector);
const $$ = (selector, root = document) => [...root.querySelectorAll(selector)];
let renderTimer = null;
let premiumRoot = null;
let seriesDialog = null;
let storageDialog = null;

function formatBytes(bytes = 0) {
  if (!bytes) return '0 MB';
  const units = ['B', 'KB', 'MB', 'GB', 'TB'];
  const power = Math.min(Math.floor(Math.log(bytes) / Math.log(1024)), units.length - 1);
  return `${(bytes / (1024 ** power)).toFixed(power > 1 ? 1 : 0)} ${units[power]}`;
}

function progressOf(book) {
  if (!book.pageCount) return 0;
  return Math.max(0, Math.min(100, (((book.currentPage || 0) + 1) / book.pageCount) * 100));
}

function chapterOrder(book) {
  const numeric = Number.parseFloat(String(book.number || '').replace(',', '.'));
  if (Number.isFinite(numeric)) return numeric;
  const match = String(book.title || book.fileName || '').match(/(?:cap(?:itulo)?|ch(?:apter)?|#|vol(?:umen)?|issue)?\s*(\d+(?:[.,]\d+)?)/i);
  return match ? Number.parseFloat(match[1].replace(',', '.')) : Number.MAX_SAFE_INTEGER;
}

function coverNode(book, className = 'premium-cover') {
  const cover = document.createElement('span');
  cover.className = className;
  if (book.cover instanceof Blob) {
    const img = new Image();
    const url = URL.createObjectURL(book.cover);
    img.alt = '';
    img.src = url;
    img.decoding = 'async';
    img.onload = img.onerror = () => URL.revokeObjectURL(url);
    cover.append(img);
  } else {
    cover.textContent = (book.series || book.title || 'Nebula').slice(0, 24);
  }
  return cover;
}

function resetLibraryFilters() {
  const search = $('#library-search');
  const filter = $('#library-filter');
  if (search && search.value) {
    search.value = '';
    search.dispatchEvent(new Event('input', { bubbles: true }));
  }
  if (filter && filter.value !== 'all') {
    filter.value = 'all';
    filter.dispatchEvent(new Event('change', { bubbles: true }));
  }
}

function openExistingBook(id) {
  resetLibraryFilters();
  requestAnimationFrame(() => {
    const selector = `.book-card[data-id="${CSS.escape(String(id))}"] .cover-button`;
    const button = $(selector);
    if (button) button.click();
    else {
      const grid = $('#library-grid');
      grid?.scrollIntoView({ behavior: 'smooth', block: 'start' });
    }
  });
}

function makeBookCard(book, subtitle) {
  const button = document.createElement('button');
  button.type = 'button';
  button.className = 'premium-card';
  button.append(coverNode(book));
  const meta = document.createElement('span');
  const title = document.createElement('strong');
  title.textContent = book.title || book.fileName || 'Sin titulo';
  const detail = document.createElement('small');
  detail.textContent = subtitle;
  const progress = document.createElement('span');
  progress.className = 'premium-progress';
  const fill = document.createElement('span');
  fill.style.width = `${progressOf(book)}%`;
  progress.append(fill);
  meta.append(title, detail, progress);
  button.append(meta);
  button.addEventListener('click', () => openExistingBook(book.id));
  return button;
}

function ensurePremiumRoot() {
  if (premiumRoot?.isConnected) return premiumRoot;
  premiumRoot = document.createElement('section');
  premiumRoot.id = 'premium-home';
  premiumRoot.className = 'premium-home';
  const librarySection = $('.library-section');
  librarySection?.before(premiumRoot);
  return premiumRoot;
}

function section(title, eyebrow, id) {
  const wrapper = document.createElement('section');
  wrapper.className = 'premium-section';
  if (id) wrapper.id = id;
  const heading = document.createElement('div');
  heading.className = 'premium-heading';
  const text = document.createElement('div');
  const p = document.createElement('p');
  p.className = 'eyebrow'; p.textContent = eyebrow;
  const h2 = document.createElement('h2'); h2.textContent = title;
  text.append(p, h2); heading.append(text); wrapper.append(heading);
  return { wrapper, heading };
}

function seriesGroups(books) {
  const groups = new Map();
  for (const book of books) {
    const key = (book.series || '').trim() || 'Independientes';
    if (!groups.has(key)) groups.set(key, []);
    groups.get(key).push(book);
  }
  for (const items of groups.values()) items.sort((a, b) => chapterOrder(a) - chapterOrder(b) || String(a.title).localeCompare(String(b.title), undefined, { numeric: true }));
  return [...groups.entries()].sort((a, b) => a[0].localeCompare(b[0], undefined, { numeric: true }));
}

function ensureSeriesDialog() {
  if (seriesDialog?.isConnected) return seriesDialog;
  seriesDialog = document.createElement('dialog');
  seriesDialog.className = 'settings-dialog premium-dialog';
  seriesDialog.innerHTML = `<div class="dialog-card"><div class="dialog-heading"><div><p class="eyebrow">Serie</p><h2 id="premium-series-title"></h2></div><button class="icon-button" type="button" data-close aria-label="Cerrar">x</button></div><div id="premium-series-list" class="series-list"></div></div>`;
  document.body.append(seriesDialog);
  $('[data-close]', seriesDialog).addEventListener('click', () => seriesDialog.close());
  return seriesDialog;
}

function openSeries(name, books) {
  const dialog = ensureSeriesDialog();
  $('#premium-series-title', dialog).textContent = name;
  const list = $('#premium-series-list', dialog);
  list.replaceChildren(...books.map((book, index) => {
    const button = document.createElement('button');
    button.type = 'button'; button.className = 'series-chapter';
    const meta = document.createElement('span');
    const strong = document.createElement('strong'); strong.textContent = book.title || `Capitulo ${index + 1}`;
    const small = document.createElement('small'); small.textContent = `${book.pageCount || '—'} paginas · ${Math.round(progressOf(book))}%`;
    meta.append(strong, small);
    const marker = document.createElement('span'); marker.textContent = (book.currentPage || 0) > 0 ? 'Continuar ›' : 'Leer ›';
    button.append(meta, marker);
    button.addEventListener('click', () => { dialog.close(); openExistingBook(book.id); });
    return button;
  }));
  dialog.showModal();
}

async function cacheBytes() {
  if (!('caches' in window)) return 0;
  let total = 0;
  try {
    for (const name of await caches.keys()) {
      const cache = await caches.open(name);
      for (const request of await cache.keys()) {
        const response = await cache.match(request);
        const length = Number(response?.headers.get('content-length') || 0);
        if (length) total += length;
      }
    }
  } catch {}
  return total;
}

function ensureStorageDialog() {
  if (storageDialog?.isConnected) return storageDialog;
  storageDialog = document.createElement('dialog');
  storageDialog.className = 'settings-dialog premium-dialog';
  storageDialog.innerHTML = `<div class="dialog-card"><div class="dialog-heading"><div><p class="eyebrow">Dispositivo</p><h2>Almacenamiento</h2></div><button class="icon-button" type="button" data-close aria-label="Cerrar">x</button></div><div id="premium-storage-content"></div></div>`;
  document.body.append(storageDialog);
  $('[data-close]', storageDialog).addEventListener('click', () => storageDialog.close());
  return storageDialog;
}

async function openStorage() {
  const dialog = ensureStorageDialog();
  const content = $('#premium-storage-content', dialog);
  content.innerHTML = '<p>Calculando almacenamiento...</p>';
  dialog.showModal();
  const [estimate, books, cachesSize] = await Promise.all([getStorageEstimate(), getAllBooks(), cacheBytes()]);
  const libraryBytes = books.reduce((sum, book) => sum + Number(book.size || book.blob?.size || 0), 0);
  const usage = estimate?.usage || 0;
  const quota = estimate?.quota || 0;
  const percent = quota ? Math.min(100, usage / quota * 100) : 0;
  content.replaceChildren();
  const grid = document.createElement('div'); grid.className = 'storage-grid';
  const values = [[formatBytes(usage), 'Uso del sitio'], [formatBytes(libraryBytes), 'Biblioteca local'], [formatBytes(cachesSize), 'Cache de la app']];
  for (const [value, label] of values) {
    const card = document.createElement('div'); card.className = 'storage-card';
    const strong = document.createElement('strong'); strong.textContent = value;
    const small = document.createElement('small'); small.textContent = label;
    card.append(strong, small); grid.append(card);
  }
  const bar = document.createElement('div'); bar.className = 'storage-bar'; const fill = document.createElement('span'); fill.style.width = `${percent}%`; bar.append(fill);
  const note = document.createElement('p'); note.textContent = quota ? `${Math.round(percent)}% de ${formatBytes(quota)} disponible para este navegador.` : 'El navegador no informa una cuota total.';
  const actions = document.createElement('div'); actions.className = 'premium-actions';
  const clear = document.createElement('button'); clear.type = 'button'; clear.className = 'button ghost'; clear.textContent = 'Limpiar cache temporal';
  clear.addEventListener('click', async () => {
    if ('caches' in window) await Promise.all((await caches.keys()).map((name) => caches.delete(name)));
    clear.textContent = 'Cache limpiada'; clear.disabled = true;
  });
  actions.append(clear); content.append(grid, note, bar, actions);
}

function installMobileNav() {
  if ($('#premium-mobile-nav')) return;
  const nav = document.createElement('nav');
  nav.id = 'premium-mobile-nav'; nav.className = 'mobile-premium-nav';
  const items = [
    ['⌂', 'Inicio', () => scrollTo({ top: 0, behavior: 'smooth' })],
    ['▶', 'Continuar', () => $('#premium-continue')?.scrollIntoView({ behavior: 'smooth', block: 'start' })],
    ['▦', 'Series', () => $('#premium-series')?.scrollIntoView({ behavior: 'smooth', block: 'start' })],
    ['◫', 'Espacio', openStorage],
  ];
  for (const [icon, label, action] of items) {
    const button = document.createElement('button'); button.type = 'button';
    const span = document.createElement('span'); span.textContent = icon;
    const text = document.createElement('small'); text.textContent = label;
    button.append(span, text); button.addEventListener('click', action); nav.append(button);
  }
  document.body.append(nav);
}

function enhanceReaderPreload() {
  const stage = $('#page-stage');
  if (!stage || stage.dataset.premiumPreload) return;
  stage.dataset.premiumPreload = 'true';
  const tune = () => {
    const images = $$('.page-image', stage);
    images.forEach((image, index) => {
      image.decoding = 'async';
      image.loading = index < 3 ? 'eager' : 'lazy';
      if (index < 2) image.fetchPriority = 'high';
      if (index < 3 && image.complete === false) image.decode?.().catch(() => {});
    });
  };
  new MutationObserver(tune).observe(stage, { childList: true, subtree: true, attributes: true, attributeFilter: ['src'] });
  $('#page-slider')?.addEventListener('input', () => requestAnimationFrame(tune));
  tune();
}

async function renderPremium() {
  const root = ensurePremiumRoot();
  if (!root) return;
  const books = await getAllBooks();
  root.replaceChildren();
  if (!books.length) {
    root.classList.add('premium-hidden');
    return;
  }
  root.classList.remove('premium-hidden');

  const continueBooks = books.filter((book) => (book.currentPage || 0) > 0 || (book.lastReadAt || 0) > 0).sort((a, b) => (b.lastReadAt || 0) - (a.lastReadAt || 0)).slice(0, 8);
  if (continueBooks.length) {
    const { wrapper } = section('Continua leyendo', 'Retoma al instante', 'premium-continue');
    const row = document.createElement('div'); row.className = 'premium-scroll';
    row.append(...continueBooks.map((book) => makeBookCard(book, `Pagina ${(book.currentPage || 0) + 1} de ${book.pageCount || '—'}`)));
    wrapper.append(row); root.append(wrapper);
  }

  const recent = [...books].sort((a, b) => (b.addedAt || 0) - (a.addedAt || 0)).slice(0, 10);
  const { wrapper: recentSection } = section('Agregados recientemente', 'Nuevos en tu biblioteca', 'premium-recent');
  const recentRow = document.createElement('div'); recentRow.className = 'premium-scroll';
  recentRow.append(...recent.map((book) => makeBookCard(book, `${book.series || 'Independiente'} · ${book.pageCount || '—'} paginas`)));
  recentSection.append(recentRow); root.append(recentSection);

  const groups = seriesGroups(books);
  if (groups.length) {
    const { wrapper: seriesSection, heading } = section('Series', 'Colecciones organizadas', 'premium-series');
    const storageButton = document.createElement('button'); storageButton.type = 'button'; storageButton.className = 'premium-button'; storageButton.textContent = 'Gestionar espacio'; storageButton.addEventListener('click', openStorage); heading.append(storageButton);
    const row = document.createElement('div'); row.className = 'premium-scroll';
    row.append(...groups.slice(0, 12).map(([name, items]) => {
      const card = document.createElement('button'); card.type = 'button'; card.className = 'premium-card series-card';
      const meta = document.createElement('span'); meta.className = 'series-meta';
      const left = document.createElement('span'); const strong = document.createElement('strong'); strong.textContent = name;
      const small = document.createElement('small'); small.textContent = items.find((book) => (book.currentPage || 0) > 0)?.title || items[0]?.title || '';
      left.append(strong, small); const count = document.createElement('span'); count.className = 'series-count'; count.textContent = `${items.length} cap.`;
      meta.append(left, count); card.append(meta); card.addEventListener('click', () => openSeries(name, items)); return card;
    }));
    seriesSection.append(row); root.append(seriesSection);
  }
}

function scheduleRender() {
  clearTimeout(renderTimer);
  renderTimer = setTimeout(() => renderPremium().catch(console.error), 120);
}

export function initPremiumUx() {
  ensurePremiumRoot();
  installMobileNav();
  enhanceReaderPreload();
  scheduleRender();
  const grid = $('#library-grid');
  if (grid) new MutationObserver(scheduleRender).observe(grid, { childList: true, subtree: false });
  window.addEventListener('focus', scheduleRender);
  document.addEventListener('visibilitychange', () => { if (!document.hidden) scheduleRender(); });
}
