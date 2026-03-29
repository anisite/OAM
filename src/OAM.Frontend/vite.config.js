import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

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
