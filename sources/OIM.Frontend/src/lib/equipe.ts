import { computed } from 'vue'
import { useRoute } from 'vue-router'

/**
 * L'équipe fait partie de l'URL (/:equipe/...). Côté API, processus et instances ont des ids qualifiés
 * « equipe.id »; dans l'URL, on garde la partie propre à l'équipe.
 */
export const qualifier = (equipe: string, id: string): string => `${equipe}.${id}`

/** Partie propre à l'équipe d'un id qualifié (« demo.traitement » → « traitement »). */
export const idLocal = (id: string): string => id.slice(id.indexOf('.') + 1)

export function useEquipe() {
  const route = useRoute()
  const equipe = computed(() => String(route.params.equipe ?? ''))
  const enc = encodeURIComponent

  return {
    equipe,
    /** Id qualifié d'un processus ou d'une instance de l'équipe courante. */
    qualifier: (id: string) => qualifier(equipe.value, id),
    /** Lien dans l'espace de l'équipe (chemin commençant par « / », ou vide pour son tableau de bord). */
    lien: (chemin = '') => `/${enc(equipe.value)}${chemin}`,
    lienProcessus: (id: string) => `/${enc(equipe.value)}/processus/${enc(idLocal(id))}`,
    lienInstance: (id: string) => `/${enc(equipe.value)}/instances/${enc(idLocal(id))}`,
    lienConcepteur: (id?: string) => `/${enc(equipe.value)}/concepteur${id ? `/${enc(idLocal(id))}` : ''}`
  }
}
