import { createRouter, createWebHistory } from 'vue-router'
import { chargerMoi, peutVoirEquipe } from '@/lib/session'

declare module 'vue-router' {
  interface RouteMeta {
    titre?: string
  }
}

const introuvable = () => import('@/pages/PageNonTrouvee.vue')

const router = createRouter({
  history: createWebHistory(),
  routes: [
    // Accueil : choix de l'équipe. Les segments fixes (admin, accessibilite) sont des ids d'équipe réservés.
    { path: '/', component: () => import('@/pages/Accueil.vue'), meta: { titre: 'Choix de l’équipe' } },
    { path: '/admin/equipes', component: () => import('@/pages/AdminEquipes.vue'), meta: { titre: 'Équipes' } },
    { path: '/accessibilite', component: () => import('@/pages/Accessibilite.vue'), meta: { titre: 'Accessibilité' } },

    // Anciennes adresses, sans équipe.
    { path: '/instances/:reste(.*)*', redirect: '/' },
    { path: '/processus/:reste(.*)*', redirect: '/' },
    { path: '/concepteur/:reste(.*)*', redirect: '/' },

    {
      path: '/:equipe',
      component: () => import('@/pages/EspaceEquipe.vue'),
      props: true,
      children: [
        { path: '', component: () => import('@/pages/TableauDeBord.vue'), meta: { titre: 'Tableau de bord' } },
        { path: 'instances', component: () => import('@/pages/Instances.vue'), meta: { titre: 'Instances' } },
        { path: 'instances/:id', component: () => import('@/pages/InstanceDetail.vue'), props: true, meta: { titre: 'Instance' } },
        { path: 'processus', component: () => import('@/pages/Processus.vue'), meta: { titre: 'Processus' } },
        { path: 'processus/:id', component: () => import('@/pages/ProcessusDetail.vue'), props: true, meta: { titre: 'Processus' } },
        { path: 'concepteur/:id?', component: () => import('@/pages/Concepteur.vue'), props: true, meta: { titre: 'Concepteur' } },
        { path: ':pathMatch(.*)*', component: introuvable, meta: { titre: 'Page non trouvée' } }
      ]
    },
    { path: '/:pathMatch(.*)*', name: 'introuvable', component: introuvable, meta: { titre: 'Page non trouvée' } }
  ],
  scrollBehavior: () => ({ top: 0 })
})

// Une équipe hors de portée est « introuvable », comme côté API.
router.beforeEach(async (to) => {
  const equipe = to.params.equipe
  if (typeof equipe !== 'string') return true
  try {
    if (peutVoirEquipe(await chargerMoi(), equipe)) return true
  } catch {
    return '/' // l'accueil explique le refus (aucune équipe, jeton refusé)
  }
  return { name: 'introuvable', params: { pathMatch: to.path.slice(1).split('/') } }
})

export default router
