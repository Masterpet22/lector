import test from 'node:test';
import assert from 'node:assert/strict';
import { getDocument } from 'pdfjs-dist/legacy/build/pdf.mjs';

function makeOnePagePdf() {
  const objects = [
    '1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n',
    '2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n',
    '3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 400] /Resources << >> /Contents 4 0 R >>\nendobj\n',
    '4 0 obj\n<< /Length 0 >>\nstream\n\nendstream\nendobj\n',
  ];
  let source = '%PDF-1.4\n';
  const offsets = [0];
  for (const object of objects) {
    offsets.push(new TextEncoder().encode(source).length);
    source += object;
  }
  const xrefOffset = new TextEncoder().encode(source).length;
  source += `xref\n0 ${objects.length + 1}\n0000000000 65535 f \n`;
  source += offsets.slice(1).map((offset) => `${String(offset).padStart(10, '0')} 00000 n \n`).join('');
  source += `trailer\n<< /Size ${objects.length + 1} /Root 1 0 R >>\nstartxref\n${xrefOffset}\n%%EOF\n`;
  return new TextEncoder().encode(source);
}

test('PDF.js abre un documento PDF local y detecta sus páginas', async () => {
  const task = getDocument({ data: makeOnePagePdf(), isEvalSupported: false });
  const pdf = await task.promise;
  assert.equal(pdf.numPages, 1);
  const page = await pdf.getPage(1);
  assert.equal(page.getViewport({ scale: 1 }).height, 400);
  await task.destroy();
});
