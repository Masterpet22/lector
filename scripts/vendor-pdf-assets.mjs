import { cp, mkdir, readdir, rm, writeFile } from 'node:fs/promises';
import { dirname, join, relative, resolve, sep } from 'node:path';
import { fileURLToPath } from 'node:url';

const projectRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const packageRoot = join(projectRoot, 'node_modules', 'pdfjs-dist');
const destination = join(projectRoot, 'public', 'vendor', 'pdfjs');

if (!destination.startsWith(`${projectRoot}${sep}`) || destination === projectRoot) {
  throw new Error(`Destino de recursos PDF no seguro: ${destination}`);
}

await rm(destination, { recursive: true, force: true });
await mkdir(destination, { recursive: true });

for (const directory of ['cmaps', 'standard_fonts', 'wasm']) {
  await cp(join(packageRoot, directory), join(destination, directory), { recursive: true });
}
await cp(join(packageRoot, 'build', 'pdf.worker.min.mjs'), join(destination, 'pdf.worker.min.mjs'));
await cp(join(packageRoot, 'LICENSE'), join(destination, 'LICENSE'));

async function collectFiles(directory) {
  const entries = await readdir(directory, { withFileTypes: true });
  const files = [];
  for (const entry of entries) {
    const absolute = join(directory, entry.name);
    if (entry.isDirectory()) files.push(...await collectFiles(absolute));
    else files.push(absolute);
  }
  return files;
}

const assets = (await collectFiles(destination))
  .filter((file) => !file.endsWith('asset-manifest.json'))
  .map((file) => `/vendor/pdfjs/${relative(destination, file).split(sep).join('/')}`)
  .sort();

await writeFile(join(destination, 'asset-manifest.json'), `${JSON.stringify({ assets }, null, 2)}\n`, 'utf8');
console.log(`PDF.js preparado para uso offline: ${assets.length} recursos.`);
