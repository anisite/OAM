import { createRouter, createWebHistory } from 'vue-router'

const routes = [
  {
    path: '/',
    name: 'tableau-de-bord',
    component: () => import('../pages/TableauDeBord.vue')
  },
  {
    path: '/definitions',
    name: 'definitions',
    component: () => import('../pages/Definitions.vue')
  },
  {
    path: '/definitions/:id',
    name: 'definition-detail',
    component: () => import('../pages/DefinitionDetail.vue')
  },
  {
    path: '/instances',
    name: 'instances',
    component: () => import('../pages/Instances.vue')
  },
  {
    path: '/instances/:id',
    name: 'instance-detail',
    component: () => import('../pages/InstanceDetail.vue')
  }
]

const router = createRouter({
  history: createWebHistory(),
  routes
})

export default router
