import { initGoogleDrive } from './google-drive.js';
import { initPremiumUx } from './premium.js';

async function handFilesToReader(files) {
  const input = document.querySelector('#file-input');
  if (!input) throw new Error('No se encontró el importador local del lector.');
  const transfer = new DataTransfer();
  for (const file of files) transfer.items.add(file);
  input.files = transfer.files;
  input.dispatchEvent(new Event('change', { bubbles: true }));
}

function start() {
  initGoogleDrive(handFilesToReader);
  initPremiumUx();
}

if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start, { once: true });
else start();
