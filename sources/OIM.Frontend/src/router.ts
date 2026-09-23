import { createRouter, createWebHistory } from 'vue-router'

declare module 'vue-router' {
  interface RouteMeta {
    titre?: string
  }
}

const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: '/', component: () => import('@/pages/TableauDeBord.vue'), meta: { titre: 'Tableau de bord' } },
    { path: '/instances', component: () => import('@/pages/Instances.vue'), meta: { titre: 'Instances' } },
    { path: '/instances/:id', component: () => import('@/pages/InstanceDetail.vue'), props: true, meta: { titre: 'Instance' } },
    { path: '/processus', component: () => import('@/pages/Processus.vue'), meta: { titre: 'Processus' } },
    { path: '/processus/:id', component: () => import('@/pages/ProcessusDetail.vue'), props: true, meta: { titre: 'Processus' } },
    { path: '/concepteur/:id?', component: () => import('@/pages/Concepteur.vue'), props: true, meta: { titre: 'Concepteur' } },
    { path: '/accessibilite', component: () => import('@/pages/Accessibilite.vue'), meta: { titre: 'Accessibilité' } },
    { path: '/:pathMatch(.*)*', component: () => import('@/pages/PageNonTrouvee.vue'), meta: { titre: 'Page non trouvée' } }
  ],
  scrollBehavior: () => ({ top: 0 })
})

export default router
