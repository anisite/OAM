import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import { createHash } from 'node:crypto'

// @vitejs/plugin-vue@6 utilise globalThis.crypto.hash qui n'existe pas sur WebCrypto
if (!globalThis.crypto) globalThis.crypto = {}
if (!globalThis.crypto.hash) {
  globalThis.crypto.hash = (algorithm, data, outputEncoding) =>
    createHash(algorithm).update(data).digest(outputEncoding)
}

export default defineConfig({
  plugins: [
    vue({
      template: {
        compilerOptions: {
          // UTD web components (utd-*) ne doivent pas être compilés par Vue
          isCustomElement: (tag) => tag.startsWith('utd-')
        }
      }
    })
  ],
  server: {
    port: 5173,
    proxy: {
      '/api': 'http://localhost:5000',
      '/hubs': {
        target: 'http://localhost:5000',
        ws: true
      }
    }
  },
  build: {
    outDir: '../OAM.Api/wwwroot',
    emptyOutDir: true
  }
})
