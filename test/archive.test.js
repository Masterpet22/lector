import test from 'node:test';
import assert from 'node:assert/strict';
import JSZip from 'jszip';
import { baseTitle, inspectComic, stableBookId } from '../src/archive.js';

test('normaliza el título quitando extensiones compatibles', () => {
  assert.equal(baseTitle('Saga 012.CBZ'), 'Saga 012');
  assert.equal(baseTitle('Capítulo final.cbr'), 'Capítulo final');
  assert.equal(baseTitle('Edición especial.pdf'), 'Edición especial');
});

test('genera identificadores estables y distingue archivos', () => {
  const first = { name: 'comic.cbz', size: 100, lastModified: 123 };
  const copy = { ...first };
  const other = { ...first, size: 101 };
  assert.equal(stableBookId(first), stableBookId(copy));
  assert.notEqual(stableBookId(first), stableBookId(other));
});

test('inspecciona un CBZ y ordena sus páginas de forma natural', async () => {
  global.window = { JSZip };
  const zip = new JSZip();
  zip.file('10.jpg', 'page-ten');
  zip.file('2.jpg', 'page-two');
  zip.file('__MACOSX/._1.jpg', 'ignored');
  const comic = await zip.generateAsync({ type: 'uint8array' });
  const result = await inspectComic(comic, 'demo.cbz');
  assert.equal(result.pageCount, 2);
  assert.ok(result.cover instanceof Blob);
});
