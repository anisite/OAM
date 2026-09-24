import { createApp } from 'vue'
import App from './App.vue'
import router from './router'
import { installerFilAriane } from './lib/filAriane'
import '@vue-flow/core/dist/style.css'
import '@vue-flow/core/dist/theme-default.css'
import '@vue-flow/controls/dist/style.css'
import '@vue-flow/minimap/dist/style.css'
import './assets/base.css'

router.afterEach((to) => {
  document.title = to.meta.titre ? `${to.meta.titre} - OIM` : 'OIM'
})

installerFilAriane(router)

createApp(App).use(router).mount('#app')
