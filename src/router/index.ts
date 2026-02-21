import { createRouter, createWebHashHistory } from 'vue-router'
import MainLayout from '../layouts/MainLayout.vue'
import DanmakuList from '../components/DanmakuList.vue'
import HomeView from '../views/HomeView.vue'
import VupList from '../components/VupList.vue'
import SongRequests from '../components/SongRequests.vue'
import AdminView from '../views/AdminView.vue'

const router = createRouter({
  history: createWebHashHistory(import.meta.env.BASE_URL),
  routes: [
    {
      path: '/admin',
      name: 'admin',
      component: AdminView
    },
    {
      path: '/',
      component: MainLayout,
      children: [
        {
          path: '',
          name: 'home',
          component: HomeView
        },
        {
          path: 'vup-list', // 保留旧的 VUP 列表路由作为备用，或者给 HomeView 内部使用
          name: 'vup-list',
          component: VupList
        },
        {
          path: 'vup/:uid',
          children: [
            {
              path: '',
              name: 'streamer-danmaku',
              component: DanmakuList
            },
            {
              path: 'songs',
              name: 'streamer-songs',
              component: SongRequests
            }
          ]
        }
      ]
    }
  ]
})

export default router
