import dagre from '@dagrejs/dagre'
import { MarkerType, type Edge } from '@vue-flow/core'
import { COULEURS_LIENS } from './catalogue'
import type { EtapeModele, ModeleProcessus } from './modele'

export const LARGEUR_NOEUD = 230
export const HAUTEUR_NOEUD = 110

/** Poignées de sortie : suivant (ou sinon), b<i> (branche conditionnelle i), erreur, delai. */
export type Poignee = 'suivant' | 'erreur' | 'delai' | `b${number}`

export interface InfoLien {
  source: string
  poignee: Poignee
  cible: string
}

const tronquer = (s: string, n = 28): string => (s.length > n ? `${s.slice(0, n - 1)}…` : s)

export function conditionLisible(si: string): string {
  return si.replace(/^\s*\{\{\s*|\s*\}\}\s*$/g, '')
}

/** Transitions d'une étape, avec la poignée d'où elles partent. */
export function transitions(e: EtapeModele): { poignee: Poignee; cible: string; libelle?: string; nature: keyof typeof COULEURS_LIENS }[] {
  const liste: { poignee: Poignee; cible: string; libelle?: string; nature: keyof typeof COULEURS_LIENS }[] = []
  if (!e.fin) {
    const conditionnelles = e.suivant.filter((b) => b.si !== null)
    e.suivant.forEach((b) => {
      if (!b.aller) return
      if (b.si !== null)
        liste.push({ poignee: `b${conditionnelles.indexOf(b)}`, cible: b.aller, libelle: tronquer(conditionLisible(b.si)), nature: 'branche' })
      else liste.push({ poignee: 'suivant', cible: b.aller, libelle: conditionnelles.length ? 'sinon' : undefined, nature: conditionnelles.length ? 'sinon' : 'suivant' })
    })
  }
  if (e.siErreur) liste.push({ poignee: 'erreur', cible: e.siErreur, libelle: 'erreur', nature: 'erreur' })
  if (e.props.siDelaiExpire) liste.push({ poignee: 'delai', cible: String(e.props.siDelaiExpire), libelle: 'délai expiré', nature: 'delai' })
  return liste
}

/**
 * Liens du graphe dérivés du modèle : le modèle est la seule source de vérité,
 * les liens ne sont jamais stockés séparément.
 */
export function aretes(m: ModeleProcessus, parcourus: Set<string> = new Set()): Edge[] {
  const parId = new Map(m.etapes.map((e) => [e.id, e.uid]))
  const liste: Edge[] = []
  for (const e of m.etapes) {
    for (const t of transitions(e)) {
      const cible = parId.get(t.cible)
      if (!cible) continue
      const parcouru = parcourus.has(`${e.id}>${t.cible}`)
      const couleur = parcouru ? COULEURS_LIENS.parcouru : COULEURS_LIENS[t.nature]
      liste.push({
        id: `${e.uid}|${t.poignee}`,
        source: e.uid,
        sourceHandle: t.poignee,
        target: cible,
        targetHandle: 'entree',
        label: t.libelle,
        type: 'default',
        animated: parcouru,
        markerEnd: { type: MarkerType.ArrowClosed, color: couleur },
        style: {
          stroke: couleur,
          strokeWidth: parcouru ? 3 : 2,
          strokeDasharray: t.nature === 'erreur' || t.nature === 'delai' ? '6 4' : undefined
        },
        labelBgStyle: { fill: '#fff' },
        labelStyle: { fill: couleur, fontSize: '11px', fontWeight: 600 },
        data: { nature: t.nature }
      })
    }
  }
  return liste
}

/** Positions calculées automatiquement (haut → bas). */
export function disposer(m: ModeleProcessus): Record<string, { x: number; y: number }> {
  const g = new dagre.graphlib.Graph()
  g.setGraph({ rankdir: 'TB', nodesep: 90, ranksep: 110, marginx: 20, marginy: 20 })
  g.setDefaultEdgeLabel(() => ({}))
  for (const e of m.etapes) g.setNode(e.uid, { width: LARGEUR_NOEUD, height: HAUTEUR_NOEUD })

  const parId = new Map(m.etapes.map((e) => [e.id, e.uid]))
  const ordre = new Map(m.etapes.map((e, i) => [e.id, i]))
  for (const e of m.etapes)
    for (const t of transitions(e)) {
      const cible = parId.get(t.cible)
      // Les retours en arrière (boucles) ne doivent pas influencer les rangs.
      if (cible && (ordre.get(t.cible) ?? 0) > (ordre.get(e.id) ?? 0)) g.setEdge(e.uid, cible)
    }

  dagre.layout(g)
  const positions: Record<string, { x: number; y: number }> = {}
  for (const e of m.etapes) {
    const n = g.node(e.uid)
    positions[e.uid] = { x: n.x - LARGEUR_NOEUD / 2, y: n.y - HAUTEUR_NOEUD / 2 }
  }
  return positions
}

/** Paires « source>cible » effectivement parcourues, d'après le parcours de l'instance. */
export function transitionsParcourues(parcours: string[]): Set<string> {
  const s = new Set<string>()
  for (let i = 1; i < parcours.length; i++) s.add(`${parcours[i - 1]}>${parcours[i]}`)
  return s
}
