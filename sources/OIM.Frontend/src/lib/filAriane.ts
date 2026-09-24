import { watch } from 'vue'
import type { Router } from 'vue-router'

declare let utd: any

export interface ElementFil {
  libelle: string
  /** Absent pour la page courante (dernier élément). */
  lien?: string
}

const echapper = (texte: string): string =>
  texte.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;')

/**
 * Fil d'Ariane UTD (utd.filAriane.definir) : ajouté sous le menu, dans le <header>.
 * Les libellés sont insérés en HTML par UTD : on les échappe (noms d'équipe saisis par les utilisateurs).
 * Sans élément, le fil est retiré (accueil).
 */
export function afficherFil(elements: ElementFil[]): void {
  const nav = document.querySelector('nav.utd-fil-ariane')
  if (!elements.length) {
    nav?.parentElement?.remove()
    return
  }
  utd.filAriane.definir(elements.map((e) => ({ libelle: echapper(e.libelle), href: e.lien ? echapper(e.lien) : '' })))
}

/** Fil d'Ariane de la page, tenu à jour tant qu'elle est affichée. */
export function useFilAriane(source: () => ElementFil[]): void {
  watch(source, afficherFil, { immediate: true, deep: true })
}

/**
 * UTD génère des liens simples : on les fait passer par le routeur (pas de rechargement de page).
 * Le fil est vidé à chaque changement de page; la nouvelle page déclare le sien.
 */
export function installerFilAriane(router: Router): void {
  router.beforeEach((to, from) => {
    if (to.path !== from.path) afficherFil([])
  })
  document.addEventListener('click', (ev) => {
    const lien = (ev.target as Element | null)?.closest?.('nav.utd-fil-ariane a') as HTMLAnchorElement | null
    const href = lien?.getAttribute('href')
    if (!href || ev.button !== 0 || ev.ctrlKey || ev.metaKey || ev.shiftKey || ev.altKey) return
    ev.preventDefault()
    router.push(href)
  })
}
