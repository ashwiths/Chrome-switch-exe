import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import { resolve } from 'path';
import { copyFileSync, existsSync, mkdirSync } from 'fs';

function copyManifestPlugin() {
  return {
    name: 'copy-manifest',
    closeBundle() {
      if (!existsSync('dist')) {
        mkdirSync('dist', { recursive: true });
      }
      if (existsSync('manifest.json')) {
        copyFileSync('manifest.json', 'dist/manifest.json');
      }
    }
  };
}

export default defineConfig({
  plugins: [react(), copyManifestPlugin()],
  build: {
    outDir: 'dist',
    emptyOutDir: true,
    rollupOptions: {
      input: {
        popup: resolve(__dirname, 'popup.html'),
        'service-worker': resolve(__dirname, 'src/background/service-worker.ts')
      },
      output: {
        entryFileNames: (chunkInfo) => {
          if (chunkInfo.name === 'service-worker') {
            return 'service-worker.js';
          }
          return 'assets/[name]-[hash].js';
        },
        chunkFileNames: 'assets/[name]-[hash].js',
        assetFileNames: 'assets/[name]-[hash].[ext]'
      }
    }
  }
});
