import { createApp } from 'vue'
import { createPinia } from 'pinia'
import ElementPlus from 'element-plus'
import 'element-plus/dist/index.css'
import 'element-plus/theme-chalk/dark/css-vars.css'
import * as ElementPlusIconsVue from '@element-plus/icons-vue'
import './style.css'
import App from './App.vue'
import router from './router'

// Warn if VITE_COS_BASE_URL is not configured
if (import.meta.env.DEV && !import.meta.env.VITE_COS_BASE_URL) {
  console.warn('[Dev] VITE_COS_BASE_URL is not set. Images (avatars, covers) will not load. Add it to your .env file, e.g.: VITE_COS_BASE_URL=https://ovodm.top')
}

const app = createApp(App)
const pinia = createPinia()

app.use(pinia)
app.use(router)
app.use(ElementPlus)

for (const [key, component] of Object.entries(ElementPlusIconsVue)) {
  app.component(key, component)
}

app.mount('#app')
