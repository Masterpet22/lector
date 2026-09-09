import './google-drive.css';

const DRIVE_SCOPE = 'https://www.googleapis.com/auth/drive.readonly';
const FOLDER_MIME = 'application/vnd.google-apps.folder';
const SUPPORTED = new Set(['cbz', 'cbr', 'zip', 'pdf']);
const FOLDER_STORAGE_KEY = 'nebula-drive-folder';

let importFilesHandler = null;
let tokenClient = null;
let accessToken = null;
let pickerReady = false;
let gisReady = false;
let selectedFolder = null;
let remoteFiles = [];
let selectedIds = new Set();

const $ = (selector, root = document) => root.querySelector(selector);

function getConfig() {
  return window.NEBULA_GOOGLE_CONFIG || {};
}

function configured() {
  const { clientId, apiKey } = getConfig();
  return Boolean(clientId && apiKey);
}

function extOf(name = '') {
  return name.split('.').pop()?.toLowerCase() || '';
}

function compatible(file) {
  return SUPPORTED.has(extOf(file.name)) || file.mimeType === 'application/pdf';
}

function toast(message, type = '') {
  const region = $('#toast-region');
  if (!region) return;
  const element = document.createElement('div');
  element.className = `toast ${type}`;
  element.textContent = message;
  region.append(element);
  window.setTimeout(() => element.remove(), 4200);
}

function setLoading(active, text = 'Procesando…') {
  const loader = $('#reader-loader');
  const loaderText = $('#loader-text');
  if (!loader || !loaderText) return;
  loaderText.textContent = text;
  loader.classList.toggle('hidden', !active);
}

function loadScript(src, marker) {
  return new Promise((resolve, reject) => {
    if ($(marker)) {
      const existing = $(marker);
      if (existing.dataset.loaded === 'true') return resolve();
      existing.addEventListener('load', resolve, { once: true });
      existing.addEventListener('error', reject, { once: true });
      return;
    }
    const script = document.createElement('script');
    script.src = src;
    script.async = true;
    script.defer = true;
    script.dataset.nebulaLoader = marker.replace(/[^a-z0-9]/gi, '');
    script.addEventListener('load', () => { script.dataset.loaded = 'true'; resolve(); }, { once: true });
    script.addEventListener('error', () => reject(new Error(`No se pudo cargar ${src}`)), { once: true });
    document.head.append(script);
  });
}

async function ensureGoogleLibraries() {
  if (!navigator.onLine) throw new Error('Necesitas conexión a Internet para usar Google Drive.');

  if (!gisReady) {
    await loadScript('https://accounts.google.com/gsi/client', 'script[src*="accounts.google.com/gsi/client"]');
    gisReady = Boolean(window.google?.accounts?.oauth2);
  }

  if (!pickerReady) {
    await loadScript('https://apis.google.com/js/api.js', 'script[src*="apis.google.com/js/api.js"]');
    await new Promise((resolve, reject) => {
      window.gapi.load('picker', { callback: resolve, onerror: () => reject(new Error('No se pudo cargar Google Picker.')) });
    });
    pickerReady = Boolean(window.google?.picker);
  }

  if (!gisReady || !pickerReady) throw new Error('No se pudieron iniciar los servicios de Google.');
}

function ensureTokenClient() {
  const { clientId } = getConfig();
  if (!clientId) throw new Error('Falta configurar el Client ID de Google.');
  if (tokenClient) return tokenClient;
  tokenClient = window.google.accounts.oauth2.initTokenClient({
    client_id: clientId,
    scope: DRIVE_SCOPE,
    callback: () => {},
  });
  return tokenClient;
}

async function requestToken(prompt = '') {
  await ensureGoogleLibraries();
  const client = ensureTokenClient();
  return new Promise((resolve, reject) => {
    client.callback = (response) => {
      if (response.error) return reject(new Error(response.error_description || response.error));
      accessToken = response.access_token;
      resolve(accessToken);
    };
    client.requestAccessToken({ prompt });
  });
}

async function apiFetch(url, retry = true) {
  if (!accessToken) await requestToken('consent');
  const response = await fetch(url, { headers: { Authorization: `Bearer ${accessToken}` } });
  if (response.status === 401 && retry) {
    accessToken = null;
    await requestToken('');
    return apiFetch(url, false);
  }
  if (!response.ok) {
    let detail = '';
    try { detail = (await response.json())?.error?.message || ''; } catch {}
    throw new Error(detail || `Google Drive respondió ${response.status}.`);
  }
  return response;
}

function saveFolder(folder) {
  selectedFolder = folder;
  localStorage.setItem(FOLDER_STORAGE_KEY, JSON.stringify(folder));
}

function restoreFolder() {
  if (selectedFolder) return selectedFolder;
  try {
    const stored = JSON.parse(localStorage.getItem(FOLDER_STORAGE_KEY) || 'null');
    if (stored?.id && stored?.name) selectedFolder = stored;
  } catch {}
  return selectedFolder;
}

async function openFolderPicker() {
  if (!configured()) return showConfigurationRequired();
  await ensureGoogleLibraries();
  if (!accessToken) await requestToken('consent');
  const { apiKey, appId } = getConfig();

  return new Promise((resolve) => {
    const view = new window.google.picker.DocsView(window.google.picker.ViewId.FOLDERS)
      .setIncludeFolders(true)
      .setSelectFolderEnabled(true)
      .setMode(window.google.picker.DocsViewMode.LIST);

    let builder = new window.google.picker.PickerBuilder()
      .addView(view)
      .setOAuthToken(accessToken)
      .setDeveloperKey(apiKey)
      .setTitle('Selecciona la carpeta de tu biblioteca')
      .setCallback((data) => {
        if (data.action === window.google.picker.Action.PICKED) {
          const doc = data.docs?.[0];
          if (doc?.id) {
            const folder = { id: doc.id, name: doc.name || 'Carpeta de Google Drive' };
            saveFolder(folder);
            resolve(folder);
          }
        } else if (data.action === window.google.picker.Action.CANCEL) resolve(null);
      });

    if (appId) builder = builder.setAppId(appId);
    builder.build().setVisible(true);
  });
}

async function listChildren(folderId) {
  const params = new URLSearchParams({
    q: `'${folderId.replaceAll("'", "\\'")}' in parents and trashed = false`,
    pageSize: '1000',
    orderBy: 'folder,name_natural',
    fields: 'nextPageToken,files(id,name,mimeType,size,modifiedTime,capabilities(canDownload))',
  });
  const files = [];
  let pageToken = '';
  do {
    if (pageToken) params.set('pageToken', pageToken); else params.delete('pageToken');
    const payload = await (await apiFetch(`https://www.googleapis.com/drive/v3/files?${params}`)).json();
    files.push(...(payload.files || []));
    pageToken = payload.nextPageToken || '';
  } while (pageToken);
  return files;
}

async function scanFolderTree(rootFolder) {
  const compatibleFiles = [];
  const queue = [{ id: rootFolder.id, path: rootFolder.name }];
  const seen = new Set();

  while (queue.length) {
    const folder = queue.shift();
    if (seen.has(folder.id)) continue;
    seen.add(folder.id);
    const children = await listChildren(folder.id);
    for (const child of children) {
      if (child.mimeType === FOLDER_MIME) {
        queue.push({ id: child.id, path: `${folder.path}/${child.name}` });
      } else if (compatible(child) && child.capabilities?.canDownload !== false) {
        compatibleFiles.push({ ...child, folderPath: folder.path });
      }
    }
  }
  return compatibleFiles;
}

function formatBytes(value) {
  const bytes = Number(value || 0);
  if (!bytes) return '—';
  const units = ['B', 'KB', 'MB', 'GB'];
  const power = Math.min(Math.floor(Math.log(bytes) / Math.log(1024)), units.length - 1);
  return `${(bytes / (1024 ** power)).toFixed(power > 1 ? 1 : 0)} ${units[power]}`;
}

function ensureDialog() {
  let dialog = $('#drive-dialog');
  if (dialog) return dialog;

  dialog = document.createElement('dialog');
  dialog.id = 'drive-dialog';
  dialog.className = 'settings-dialog drive-dialog';
  dialog.innerHTML = `
    <div class="dialog-card drive-card">
      <div class="dialog-heading">
        <div><p class="eyebrow">Google Drive</p><h2>Biblioteca en la nube</h2></div>
        <button id="drive-close" class="icon-button" type="button" aria-label="Cerrar">×</button>
      </div>
      <div id="drive-config-warning" class="drive-setup hidden">
        <p>La integración está instalada, pero faltan las credenciales públicas de Google Cloud para activar el inicio de sesión.</p>
        <p class="drive-help">Debes configurar <strong>Client ID OAuth</strong>, <strong>API Key</strong> y opcionalmente <strong>App ID</strong>. Nunca uses un <code>client_secret</code> en esta aplicación.</p>
      </div>
      <div id="drive-browser" class="hidden">
        <div id="drive-folder-summary" class="drive-folder-summary"></div>
        <div class="drive-toolbar">
          <label class="search-box"><span>⌕</span><input id="drive-search" type="search" placeholder="Buscar dentro de la carpeta" /></label>
          <button id="drive-rescan" class="button ghost" type="button">↻ Sincronizar</button>
          <button id="drive-change-folder" class="button ghost" type="button">Cambiar carpeta</button>
        </div>
        <div id="drive-status" class="drive-status"></div>
        <div id="drive-file-list" class="drive-file-list"></div>
        <div class="dialog-actions drive-actions">
          <button id="drive-disconnect" class="button ghost" type="button">Desconectar</button>
          <button id="drive-import" class="button primary" type="button" disabled>Importar seleccionados</button>
        </div>
      </div>
    </div>`;
  document.body.append(dialog);

  $('#drive-close', dialog).addEventListener('click', () => dialog.close());
  $('#drive-change-folder', dialog).addEventListener('click', async () => {
    const folder = await openFolderPicker();
    if (folder) await loadFolder(folder);
  });
  $('#drive-rescan', dialog).addEventListener('click', async () => {
    const folder = restoreFolder();
    if (folder) await loadFolder(folder);
  });
  $('#drive-search', dialog).addEventListener('input', renderFiles);
  $('#drive-import', dialog).addEventListener('click', importSelected);
  $('#drive-disconnect', dialog).addEventListener('click', disconnectDrive);
  return dialog;
}

function showConfigurationRequired() {
  const dialog = ensureDialog();
  $('#drive-config-warning', dialog).classList.remove('hidden');
  $('#drive-browser', dialog).classList.add('hidden');
  if (!dialog.open) dialog.showModal();
}

function showBrowser() {
  const dialog = ensureDialog();
  $('#drive-config-warning', dialog).classList.add('hidden');
  $('#drive-browser', dialog).classList.remove('hidden');
}

function renderFiles() {
  const dialog = ensureDialog();
  const query = $('#drive-search', dialog).value.trim().toLowerCase();
  const visible = remoteFiles.filter((file) => !query || `${file.name} ${file.folderPath}`.toLowerCase().includes(query));
  const list = $('#drive-file-list', dialog);

  list.replaceChildren(...visible.map((file) => {
    const label = document.createElement('label');
    label.className = 'drive-file';
    const checkbox = document.createElement('input');
    checkbox.type = 'checkbox';
    checkbox.checked = selectedIds.has(file.id);
    checkbox.addEventListener('change', () => {
      if (checkbox.checked) selectedIds.add(file.id); else selectedIds.delete(file.id);
      updateSelectionButton();
    });
    const icon = document.createElement('span');
    icon.className = 'drive-file-icon';
    icon.textContent = extOf(file.name).toUpperCase();
    const meta = document.createElement('span');
    meta.className = 'drive-file-meta';
    const name = document.createElement('strong');
    name.textContent = file.name;
    const detail = document.createElement('small');
    detail.textContent = `${file.folderPath} · ${formatBytes(file.size)} · ${new Date(file.modifiedTime).toLocaleDateString()}`;
    meta.append(name, detail);
    label.append(checkbox, icon, meta);
    return label;
  }));

  $('#drive-status', dialog).textContent = remoteFiles.length
    ? `${remoteFiles.length} archivo${remoteFiles.length === 1 ? '' : 's'} compatible${remoteFiles.length === 1 ? '' : 's'} en esta carpeta y sus subcarpetas.`
    : 'No encontré archivos CBZ, CBR, ZIP o PDF en esta carpeta.';
  updateSelectionButton();
}

function updateSelectionButton() {
  const button = $('#drive-import', ensureDialog());
  button.disabled = selectedIds.size === 0;
  button.textContent = selectedIds.size
    ? `Importar ${selectedIds.size} seleccionado${selectedIds.size === 1 ? '' : 's'}`
    : 'Importar seleccionados';
}

async function loadFolder(folder) {
  showBrowser();
  const dialog = ensureDialog();
  $('#drive-folder-summary', dialog).innerHTML = `<span aria-hidden="true">☁</span><strong></strong>`;
  $('#drive-folder-summary strong', dialog).textContent = folder.name;
  $('#drive-status', dialog).textContent = 'Leyendo la carpeta de Google Drive…';
  $('#drive-file-list', dialog).replaceChildren();
  selectedIds.clear();

  try {
    if (!accessToken) await requestToken('consent');
    remoteFiles = await scanFolderTree(folder);
    renderFiles();
  } catch (error) {
    console.error(error);
    $('#drive-status', dialog).textContent = `No se pudo leer la carpeta: ${error.message}`;
    toast(`Google Drive: ${error.message}`, 'error');
  }
}

async function downloadRemoteFile(file) {
  const response = await apiFetch(`https://www.googleapis.com/drive/v3/files/${encodeURIComponent(file.id)}?alt=media`);
  const blob = await response.blob();
  return new File([blob], file.name, {
    type: file.mimeType || blob.type,
    lastModified: Date.parse(file.modifiedTime) || Date.now(),
  });
}

async function importSelected() {
  const chosen = remoteFiles.filter((file) => selectedIds.has(file.id));
  if (!chosen.length || !importFilesHandler) return;

  const files = [];
  try {
    for (let index = 0; index < chosen.length; index += 1) {
      const item = chosen[index];
      setLoading(true, `Descargando de Drive ${index + 1} de ${chosen.length}: ${item.name}`);
      files.push(await downloadRemoteFile(item));
    }
    ensureDialog().close();
    await importFilesHandler(files);
    toast(`${files.length} archivo${files.length === 1 ? '' : 's'} importado${files.length === 1 ? '' : 's'} desde Google Drive.`);
  } catch (error) {
    console.error(error);
    toast(`No se pudo importar desde Drive: ${error.message}`, 'error');
  } finally {
    setLoading(false);
  }
}

function disconnectDrive() {
  if (accessToken && window.google?.accounts?.oauth2) {
    window.google.accounts.oauth2.revoke(accessToken, () => {});
  }
  accessToken = null;
  tokenClient = null;
  selectedFolder = null;
  remoteFiles = [];
  selectedIds.clear();
  localStorage.removeItem(FOLDER_STORAGE_KEY);
  ensureDialog().close();
  toast('Google Drive desconectado de este navegador.');
}

async function openDriveLibrary() {
  if (!configured()) return showConfigurationRequired();
  const dialog = ensureDialog();
  if (!dialog.open) dialog.showModal();

  try {
    await ensureGoogleLibraries();
    const folder = restoreFolder();
    if (folder) await loadFolder(folder);
    else {
      const picked = await openFolderPicker();
      if (picked) await loadFolder(picked);
      else dialog.close();
    }
  } catch (error) {
    console.error(error);
    toast(`No se pudo conectar con Google Drive: ${error.message}`, 'error');
  }
}

function installButton() {
  const localButton = $('#import-button');
  if (!localButton || $('#drive-button')) return;
  const button = document.createElement('button');
  button.id = 'drive-button';
  button.type = 'button';
  button.className = 'button secondary drive-button';
  button.innerHTML = '<span aria-hidden="true">☁</span> Google Drive';
  button.addEventListener('click', openDriveLibrary);
  localButton.insertAdjacentElement('afterend', button);
}

export function initGoogleDrive(importFiles) {
  importFilesHandler = importFiles;
  installButton();
}
