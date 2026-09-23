import { Document, isMap, isPair, isSeq, parse, visit, type Scalar } from 'yaml'

// ── Modèle d'édition d'un processus (miroir du YAML) ────────────────────────

export interface BrancheModele {
  /** null : transition inconditionnelle, ou branche « sinon » lorsqu'il y a des conditions. */
  si: string | null
  aller: string
}

export interface RetryModele {
  tentatives: number
  delai: string
  backoff: number
}

export interface EtapeModele {
  uid: string
  id: string
  type: string
  description?: string
  statut?: string
  message?: string
  fin: boolean
  suivant: BrancheModele[]
  siErreur?: string
  retry?: RetryModele | null
  /** Propriétés propres au type (requete, donnees, evenement, delai, siDelaiExpire, gabarit, a…). */
  props: Record<string, any>
}

export interface EntreeModele {
  uid: string
  nom: string
  type: string
  requis: boolean
  defaut?: any
  libelle?: string
  description?: string
}

export interface ModeleProcessus {
  id: string
  nom?: string
  description?: string
  entrees: EntreeModele[]
  etapes: EtapeModele[]
  /** Clés racine non gérées par l'interface (limites…), conservées telles quelles. */
  extras: Record<string, any>
}

let compteur = 0
export const nouvelUid = (): string => `u${Date.now().toString(36)}${(compteur++).toString(36)}`

const CLES_ETAPE = new Set(['id', 'type', 'description', 'statut', 'message', 'suivant', 'fin', 'siErreur', 'retry'])
const CLES_RACINE = new Set(['id', 'nom', 'description', 'entrees', 'etapes'])

const texte = (v: unknown): string | undefined =>
  v === undefined || v === null ? undefined : typeof v === 'string' ? v : String(v)

/** Objet JS (YAML analysé ou « definition » de l'API) → modèle. Tolérant aux erreurs. */
export function depuisObjet(obj: any): ModeleProcessus {
  const entrees: EntreeModele[] = Object.entries(obj?.entrees ?? {}).map(([nom, def]: [string, any]) =>
    typeof def === 'string'
      ? { uid: nouvelUid(), nom, type: def, requis: false }
      : {
          uid: nouvelUid(),
          nom,
          type: def?.type ?? 'string',
          requis: def?.requis === true,
          defaut: def?.defaut,
          libelle: def?.libelle,
          description: def?.description
        }
  )

  const etapes: EtapeModele[] = (Array.isArray(obj?.etapes) ? obj.etapes : []).map((e: any, i: number) => {
    const props: Record<string, any> = {}
    for (const [k, v] of Object.entries(e ?? {})) if (!CLES_ETAPE.has(k)) props[k] = v
    return {
      uid: nouvelUid(),
      id: texte(e?.id) ?? `etape${i + 1}`,
      type: texte(e?.type) ?? '',
      description: texte(e?.description),
      statut: texte(e?.statut),
      message: texte(e?.message),
      fin: e?.fin === true,
      suivant: lireSuivant(e?.suivant),
      siErreur: texte(e?.siErreur),
      retry: e?.retry
        ? { tentatives: Number(e.retry.tentatives ?? 3), delai: texte(e.retry.delai) ?? '00:00:30', backoff: Number(e.retry.backoff ?? 1) }
        : null,
      props
    }
  })

  const extras: Record<string, any> = {}
  for (const [k, v] of Object.entries(obj ?? {})) if (!CLES_RACINE.has(k)) extras[k] = v

  return { id: texte(obj?.id) ?? '', nom: texte(obj?.nom), description: texte(obj?.description), entrees, etapes, extras }
}

function lireSuivant(s: any): BrancheModele[] {
  if (s === undefined || s === null) return []
  if (!Array.isArray(s)) return [{ si: null, aller: String(s) }]
  return s.map((b: any) =>
    typeof b !== 'object' || b === null
      ? { si: null, aller: String(b) }
      : 'sinon' in b
        ? { si: null, aller: String(b.sinon ?? '') }
        : { si: String(b.si ?? ''), aller: String(b.aller ?? '') }
  )
}

export function depuisYaml(yaml: string): ModeleProcessus {
  return depuisObjet(parse(yaml) ?? {})
}

export function modeleVide(): ModeleProcessus {
  return { id: 'nouveau-processus', nom: 'Nouveau processus', entrees: [], etapes: [], extras: {} }
}

// ── Modèle → YAML ───────────────────────────────────────────────────────────

const vide = (v: unknown): boolean =>
  v === undefined || v === null || v === '' || (typeof v === 'object' && !Array.isArray(v) && Object.keys(v as object).length === 0)

/** Objet ordonné prêt à sérialiser (ordre des clés stable et lisible). */
export function versObjet(m: ModeleProcessus): any {
  const racine: any = { id: m.id }
  if (m.nom) racine.nom = m.nom
  if (m.description) racine.description = m.description

  if (m.entrees.length) {
    racine.entrees = {}
    for (const e of m.entrees) {
      const def: any = { type: e.type }
      if (e.requis) def.requis = true
      if (!vide(e.defaut)) def.defaut = e.defaut
      if (e.libelle) def.libelle = e.libelle
      if (e.description) def.description = e.description
      racine.entrees[e.nom] = def
    }
  }
  Object.assign(racine, m.extras)

  racine.etapes = m.etapes.map((e) => {
    const o: any = { id: e.id, type: e.type }
    if (e.description) o.description = e.description
    for (const [k, v] of Object.entries(e.props)) if (!vide(v) && k !== 'siDelaiExpire') o[k] = v
    if (e.retry) o.retry = { tentatives: e.retry.tentatives, delai: e.retry.delai, backoff: e.retry.backoff }
    if (e.statut) o.statut = e.statut
    if (!vide(e.props.siDelaiExpire)) o.siDelaiExpire = e.props.siDelaiExpire
    if (e.siErreur) o.siErreur = e.siErreur
    if (e.message) o.message = e.message
    if (!e.fin) {
      const branches = e.suivant.filter((b) => b.aller || b.si)
      const conditionnelles = branches.filter((b) => b.si !== null)
      if (branches.length === 1 && conditionnelles.length === 0) o.suivant = branches[0].aller
      else if (branches.length > 0)
        o.suivant = [
          ...conditionnelles.map((b) => ({ si: b.si, aller: b.aller })),
          ...branches.filter((b) => b.si === null).map((b) => ({ sinon: b.aller }))
        ]
    } else o.fin = true
    return o
  })

  return racine
}

export function versYaml(m: ModeleProcessus): string {
  const doc = new Document(versObjet(m))
  // Style compact pour les petits objets : entrees, retry, donnees simples, branches.
  visit(doc, {
    Map(_, noeud, chemin) {
      const simple = noeud.items.length <= 4 && noeud.items.every((p: any) => !isMap(p.value) && !isSeq(p.value))
      if (!simple) return
      const cles = chemin.filter((c: any) => isPair(c)).map((c: any) => (c.key as Scalar).value)
      const cle = cles[cles.length - 1]
      const dansSuivant = isSeq(chemin[chemin.length - 1]) && cle === 'suivant'
      if (cle === 'retry' || cle === 'donnees' || cle === 'variables' || cles.includes('entrees') || dansSuivant)
        noeud.flow = true
    }
  })
  return doc.toString({ lineWidth: 0, flowCollectionPadding: true })
}

// ── Opérations d'édition ────────────────────────────────────────────────────

/** Renomme une étape et met à jour toutes les références (suivant, siErreur, siDelaiExpire). */
export function renommerEtape(m: ModeleProcessus, ancien: string, nouveau: string): void {
  for (const e of m.etapes) {
    if (e.id === ancien) e.id = nouveau
    for (const b of e.suivant) if (b.aller === ancien) b.aller = nouveau
    if (e.siErreur === ancien) e.siErreur = nouveau
    if (e.props.siDelaiExpire === ancien) e.props.siDelaiExpire = nouveau
  }
}

/** Retire une étape et les transitions qui y mènent. */
export function supprimerEtape(m: ModeleProcessus, uid: string): void {
  const etape = m.etapes.find((e) => e.uid === uid)
  if (!etape) return
  m.etapes = m.etapes.filter((e) => e.uid !== uid)
  for (const e of m.etapes) {
    e.suivant = e.suivant.filter((b) => b.aller !== etape.id || b.si !== null).map((b) => (b.aller === etape.id ? { ...b, aller: '' } : b))
    if (e.siErreur === etape.id) e.siErreur = undefined
    if (e.props.siDelaiExpire === etape.id) delete e.props.siDelaiExpire
  }
}

export function idUnique(m: ModeleProcessus, base: string): string {
  const ids = new Set(m.etapes.map((e) => e.id))
  if (!ids.has(base)) return base
  let i = 2
  while (ids.has(`${base}${i}`)) i++
  return `${base}${i}`
}

/** Valeurs par défaut à la création d'une étape depuis la palette. */
export function nouvelleEtape(m: ModeleProcessus, type: string): EtapeModele {
  const base: Record<string, { id: string; props: Record<string, any>; statut?: string }> = {
    http: { id: 'appel', props: { requete: '' } },
    attendreEvenement: { id: 'attente', props: { evenement: 'decision', delai: '5.00:00:00' }, statut: 'En attente' },
    courriel: { id: 'courriel', props: { gabarit: '', a: '{{ entrees.courriel }}' } },
    delai: { id: 'pause', props: { duree: '1.00:00:00' } },
    decision: { id: 'decision', props: {} },
    definir: { id: 'variables', props: { variables: { resultat: '' } } },
    sousProcessus: { id: 'sousProcessus', props: { processus: '', entrees: {} } },
    reponse: { id: 'reponse', props: { statutHttp: 200, corps: { statut: 'Demande reçue' } } }
  }
  const d = base[type] ?? { id: type || 'etape', props: {} }
  return {
    uid: nouvelUid(),
    id: idUnique(m, d.id),
    type,
    statut: d.statut,
    fin: false,
    suivant: type === 'decision' ? [{ si: '{{ true }}', aller: '' }, { si: null, aller: '' }] : [],
    retry: null,
    props: structuredClone(d.props)
  }
}
