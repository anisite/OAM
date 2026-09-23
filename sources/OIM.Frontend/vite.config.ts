import { fileURLToPath, URL } from 'node:url'
import { defineConfig, type Plugin } from 'vite'
import vue from '@vitejs/plugin-vue'

// Remplace %%CACHE_BUSTER%% dans index.html (force le rafraîchissement des fichiers UTD).
function cacheBusterPlugin(): Plugin {
  const cacheBuster = Date.now().toString()
  return {
    name: 'html-cache-buster',
    transformIndexHtml: {
      order: 'pre',
      handler: (html) => html.replace(/%%CACHE_BUSTER%%/g, cacheBuster)
    }
  }
}

export default defineConfig({
  plugins: [
    vue({
      template: {
        compilerOptions: {
          // Les balises utd-* sont des composants web : Vue ne doit pas les interpréter.
          isCustomElement: (tag) => tag.startsWith('utd-')
        }
      }
    }),
    cacheBusterPlugin()
  ],
  server: {
    port: 5173,
    proxy: {
      '/api': 'http://localhost:5080'
    }
  },
  build: {
    // L'application est servie par OIM.Api (un seul site IIS).
    outDir: '../OIM.Api/wwwroot',
    emptyOutDir: true,
    chunkSizeWarningLimit: 1500
  },
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url))
    }
  }
})
