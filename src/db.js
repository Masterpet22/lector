const DB_NAME = 'nebula-reader';
const DB_VERSION = 1;
const BOOKS = 'books';
const SETTINGS = 'settings';

let databasePromise;

function requestAsPromise(request) {
  return new Promise((resolve, reject) => {
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(request.error);
  });
}

function transactionDone(transaction) {
  return new Promise((resolve, reject) => {
    transaction.oncomplete = () => resolve();
    transaction.onerror = () => reject(transaction.error);
    transaction.onabort = () => reject(transaction.error || new Error('Transacción cancelada'));
  });
}

export function openDatabase() {
  if (databasePromise) return databasePromise;

  databasePromise = new Promise((resolve, reject) => {
    const request = indexedDB.open(DB_NAME, DB_VERSION);
    request.onupgradeneeded = () => {
      const db = request.result;
      if (!db.objectStoreNames.contains(BOOKS)) {
        const books = db.createObjectStore(BOOKS, { keyPath: 'id' });
        books.createIndex('addedAt', 'addedAt');
        books.createIndex('normalizedTitle', 'normalizedTitle');
      }
      if (!db.objectStoreNames.contains(SETTINGS)) {
        db.createObjectStore(SETTINGS, { keyPath: 'key' });
      }
    };
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(request.error);
    request.onblocked = () => reject(new Error('Cierra otras pestañas de Nebula Reader para actualizar la biblioteca.'));
  });

  return databasePromise;
}

export async function getAllBooks() {
  const db = await openDatabase();
  const transaction = db.transaction(BOOKS, 'readonly');
  const books = await requestAsPromise(transaction.objectStore(BOOKS).getAll());
  return books.sort((a, b) => a.title.localeCompare(b.title, undefined, { numeric: true, sensitivity: 'base' }));
}

export async function getBook(id) {
  const db = await openDatabase();
  return requestAsPromise(db.transaction(BOOKS, 'readonly').objectStore(BOOKS).get(id));
}

export async function saveBook(book) {
  const db = await openDatabase();
  const transaction = db.transaction(BOOKS, 'readwrite');
  transaction.objectStore(BOOKS).put(book);
  await transactionDone(transaction);
  return book;
}

export async function updateBook(id, patch) {
  const current = await getBook(id);
  if (!current) return null;
  const updated = { ...current, ...patch, updatedAt: Date.now() };
  await saveBook(updated);
  return updated;
}

export async function deleteBook(id) {
  const db = await openDatabase();
  const transaction = db.transaction(BOOKS, 'readwrite');
  transaction.objectStore(BOOKS).delete(id);
  await transactionDone(transaction);
}

export async function getSetting(key, fallback) {
  const db = await openDatabase();
  const record = await requestAsPromise(db.transaction(SETTINGS, 'readonly').objectStore(SETTINGS).get(key));
  return record?.value ?? fallback;
}

export async function setSetting(key, value) {
  const db = await openDatabase();
  const transaction = db.transaction(SETTINGS, 'readwrite');
  transaction.objectStore(SETTINGS).put({ key, value });
  await transactionDone(transaction);
}

export async function requestPersistentStorage() {
  if (!navigator.storage?.persist) return false;
  try { return await navigator.storage.persist(); } catch { return false; }
}

export async function getStorageEstimate() {
  if (!navigator.storage?.estimate) return null;
  try { return await navigator.storage.estimate(); } catch { return null; }
}
