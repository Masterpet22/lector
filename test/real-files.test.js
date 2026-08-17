import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { execFileSync } from 'node:child_process';
import JSZip from 'jszip';
import { getDocument } from 'pdfjs-dist/legacy/build/pdf.mjs';

const fixtureDirectory = dirname(fileURLToPath(import.meta.url));
const imagePattern = /\.(avif|gif|jpe?g|png|webp)$/i;

test('abre el CBZ real y extrae su primera página', { timeout: 120_000 }, async (context) => {
  const data = await readFile(join(fixtureDirectory, 'Action Comics 000 (1994) (digital) (Glorith-Novus-HD).cbz'));
  const zip = await JSZip.loadAsync(data);
  const pages = Object.values(zip.files).filter((entry) => !entry.dir && imagePattern.test(entry.name));
  assert.ok(pages.length > 0, 'El CBZ debe contener imágenes');
  context.diagnostic(`${pages.length} páginas encontradas en el CBZ real.`);
  const firstPage = await pages[0].async('uint8array');
  assert.ok(firstPage.byteLength > 0, 'La primera página debe poder extraerse');
});

test('valida el CBR real y extrae su primera página', { timeout: 120_000 }, (context) => {
  const filePath = join(fixtureDirectory, '3.- Superior Spider-Man #33.cbr');
  const listing = execFileSync('tar', ['-tf', filePath], { encoding: 'utf8', maxBuffer: 16 * 1024 * 1024 });
  const pages = listing.split(/\r?\n/).filter((name) => imagePattern.test(name) && !name.includes('__MACOSX') && !name.split('/').at(-1).startsWith('._'));
  assert.ok(pages.length > 0, 'El CBR debe contener imágenes');
  context.diagnostic(`${pages.length} páginas encontradas en el CBR real.`);
  const firstPage = execFileSync('tar', ['-xOf', filePath, pages[0]], { encoding: 'buffer', maxBuffer: 64 * 1024 * 1024 });
  assert.ok(firstPage.byteLength > 0, 'La primera página debe poder extraerse');
});

test('abre el PDF real y obtiene su primera página', { timeout: 120_000 }, async (context) => {
  const data = new Uint8Array(await readFile(join(fixtureDirectory, 'DB VOLUMEN1.pdf')));
  const loadingTask = getDocument({ data, isEvalSupported: false });
  try {
    const pdf = await loadingTask.promise;
    assert.ok(pdf.numPages > 0, 'El PDF debe contener páginas');
    context.diagnostic(`${pdf.numPages} páginas encontradas en el PDF real.`);
    const firstPage = await pdf.getPage(1);
    assert.ok(firstPage.getViewport({ scale: 1 }).width > 0);
  } finally {
    await loadingTask.destroy();
  }
});
