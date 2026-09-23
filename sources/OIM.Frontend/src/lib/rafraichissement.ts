import { onBeforeUnmount, onMounted, ref, watch } from 'vue'

/**
 * Actualisation périodique d'une page de suivi. L'actualisation est suspendue quand l'onglet
 * du navigateur est masqué, et reprend dès qu'il redevient visible.
 */
export function useRafraichissement(charger: () => Promise<void>, intervalleMs = 5000) {
  const automatique = ref(true)
  const chargement = ref(false)
  const derniereMaj = ref<Date | null>(null)
  let minuterie: number | undefined

  const executer = async (): Promise<void> => {
    if (chargement.value) return
    chargement.value = true
    try {
      await charger()
      derniereMaj.value = new Date()
    } finally {
      chargement.value = false
    }
  }

  const planifier = (): void => {
    window.clearInterval(minuterie)
    if (automatique.value)
      minuterie = window.setInterval(() => {
        if (document.visibilityState === 'visible') executer().catch(() => undefined)
      }, intervalleMs)
  }

  onMounted(() => {
    executer().catch(() => undefined)
    planifier()
  })
  onBeforeUnmount(() => window.clearInterval(minuterie))
  watch(automatique, planifier)

  return { automatique, chargement, derniereMaj, actualiser: executer }
}
