import { defineConfig } from 'vite';

export default defineConfig({
  build: {
    target: 'es2022',
    sourcemap: true,
    rollupOptions: {
      output: {
        entryFileNames: 'assets/app.js',
        chunkFileNames: 'assets/[name].js',
        manualChunks(id) {
          if (id.includes('pdfjs-dist')) return 'pdf-engine';
          if (id.includes('libarchive.js') || id.includes('comlink')) return 'rar-engine';
          if (id.includes('@capacitor/screen-orientation') || id.includes('@capacitor/core')) return 'native-orientation';
          return undefined;
        },
        assetFileNames: (asset) => asset.names?.some((name) => name.endsWith('.css')) ? 'assets/style.css' : 'assets/[name][extname]',
      },
    },
  },
});
