import { ref } from 'vue'
import { api } from './api'
import type { Moi } from './types'

/** Appelant courant (/api/moi) : droits et équipes, chargé une fois. */
export const moi = ref<Moi | null>(null)

let enCours: Promise<Moi> | null = null

export function chargerMoi(): Promise<Moi> {
  enCours ??= api.moi().then(
    (m) => (moi.value = m),
    (e) => {
      enCours = null
      throw e
    }
  )
  return enCours
}

/** Accès à l'espace d'une équipe (admin et support : toutes). */
export const peutVoirEquipe = (m: Moi, equipe: string): boolean => m.admin || m.support || m.equipes.includes(equipe)
