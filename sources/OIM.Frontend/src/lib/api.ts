import { enteteAutorisation } from './auth'
import type {
  DefinitionDetail,
  EquipeDetail,
  EvenementHistorique,
  InstanceDetail,
  Moi,
  PageInstances,
  ReponseDeploiement,
  RapportTests,
  ReponseValidation,
  ResumeDefinition,
  ResumeEquipe,
  Statistiques
} from './types'

/** Erreur d'API (ProblemDetails) : titre lisible + détails éventuels. */
export class ErreurApi extends Error {
  constructor(
    message: string,
    public statut: number,
    public details: string[] = [],
    public corps?: any
  ) {
    super(message)
  }
}

/** Appel authentifié; sur 401 (jeton expiré ou clé changée), un nouveau jeton est demandé une fois. */
async function envoyer(methode: string, url: string, corps?: unknown): Promise<Response> {
  const init: RequestInit = { method: methode }
  const entetes: Record<string, string> = {}
  if (corps instanceof FormData) {
    init.body = corps
  } else if (corps !== undefined) {
    init.body = JSON.stringify(corps)
    entetes['Content-Type'] = 'application/json'
  }

  let reponse = await fetch(`/api${url}`, { ...init, headers: { ...entetes, ...(await enteteAutorisation()) } })
  if (reponse.status === 401)
    reponse = await fetch(`/api${url}`, { ...init, headers: { ...entetes, ...(await enteteAutorisation(true)) } })
  return reponse
}

async function appel<T>(methode: string, url: string, corps?: unknown): Promise<T> {
  const reponse = await envoyer(methode, url, corps)
  const texte = await reponse.text()
  const json = texte ? JSON.parse(texte) : undefined

  if (!reponse.ok) {
    throw new ErreurApi(json?.title ?? `Erreur ${reponse.status}`, reponse.status, json?.details ?? [], json)
  }
  return json as T
}

const enc = encodeURIComponent

function qs(params: Record<string, unknown>): string {
  const p = new URLSearchParams()
  for (const [k, v] of Object.entries(params)) {
    if (v !== undefined && v !== null && v !== '' && v !== false) p.set(k, String(v))
  }
  const s = p.toString()
  return s ? `?${s}` : ''
}

export const api = {
  // Suivi
  tableauDeBord: (equipe: string) => appel<Statistiques>('GET', `/tableau-de-bord${qs({ equipe })}`),
  instances: (filtre: {
    equipe: string
    statut?: string
    processus?: string
    recherche?: string
    attente?: boolean
    page?: number
    taille?: number
  }) => appel<PageInstances>('GET', `/instances${qs(filtre)}`),
  instance: (id: string) => appel<InstanceDetail>('GET', `/instances/${enc(id)}`),
  historique: (id: string) => appel<EvenementHistorique[]>('GET', `/instances/${enc(id)}/historique`),

  // Pilotage
  demarrer: (processus: string, entrees: unknown, options: { version?: number; instanceId?: string } = {}) =>
    appel<{ instanceId: string; processus: string; version: number }>(
      'POST',
      `/processus/${enc(processus)}/instances${qs(options)}`,
      entrees
    ),
  evenement: (id: string, nom: string, donnees: unknown) =>
    appel<void>('POST', `/instances/${enc(id)}/evenements/${enc(nom)}`, donnees),
  commande: (id: string, commande: 'terminer' | 'suspendre' | 'reprendre' | 'relancer', raison?: string) =>
    appel<void>('POST', `/instances/${enc(id)}/${commande}`, { texte: raison }),
  redemarrer: (id: string, versionCourante: boolean) =>
    appel<{ instanceId: string }>('POST', `/instances/${enc(id)}/redemarrer`, { versionCourante }),
  purger: (id: string) => appel<void>('DELETE', `/instances/${enc(id)}`),

  // Définitions
  definitions: (equipe: string) => appel<ResumeDefinition[]>('GET', `/definitions${qs({ equipe })}`),
  definition: (id: string, version?: number) =>
    appel<DefinitionDetail>('GET', `/definitions/${enc(id)}${qs({ version })}`),
  valider: (yaml: string, fichiers: Record<string, string>) =>
    appel<ReponseValidation>('POST', '/definitions/valider', { yaml, fichiers }),
  deployer: (equipe: string, yaml: string, fichiers: Record<string, string>, commentaire?: string, ignorerTests = false) =>
    appel<ReponseDeploiement>('POST', `/definitions${qs({ equipe, ignorerTests })}`, { yaml, fichiers, commentaire }),
  /** Tests métier d'un paquet non déployé (concepteur). */
  testerPaquet: (equipe: string, yaml: string, fichiers: Record<string, string>, options: { cas?: string; conserver?: boolean } = {}) =>
    appel<RapportTests>('POST', `/definitions/tests${qs({ equipe, ...options })}`, { yaml, fichiers }),
  /** Tests métier d'une version déployée. */
  tester: (id: string, options: { version?: number; cas?: string; conserver?: boolean } = {}) =>
    appel<RapportTests>('POST', `/definitions/${enc(id)}/tests${qs(options)}`),
  deployerZip: (equipe: string, fichier: File, commentaire?: string) => {
    const f = new FormData()
    f.append('fichier', fichier)
    return appel<ReponseDeploiement>('POST', `/definitions/zip${qs({ equipe, commentaire })}`, f)
  },
  activer: (id: string, actif: boolean) => appel<void>('PUT', `/definitions/${enc(id)}/actif`, { actif }),
  // Équipes
  moi: () => appel<Moi>('GET', '/moi'),
  equipes: () => appel<ResumeEquipe[]>('GET', '/equipes'),
  equipe: (id: string) => appel<EquipeDetail>('GET', `/equipes/${enc(id)}`),
  creerEquipe: (e: { id: string; nom: string; description?: string; membres?: string[] }) =>
    appel<EquipeDetail>('POST', '/equipes', e),
  modifierEquipe: (id: string, e: { nom: string; description?: string; actif: boolean }) =>
    appel<void>('PUT', `/equipes/${enc(id)}`, e),
  definirMembres: (id: string, sujets: string[]) => appel<void>('PUT', `/equipes/${enc(id)}/membres`, { sujets }),
  supprimerEquipe: (id: string) => appel<void>('DELETE', `/equipes/${enc(id)}`),

  /** Téléchargement du paquet : un lien simple ne transmettrait pas le jeton. */
  telechargerZip: async (id: string, version?: number) => {
    const reponse = await envoyer('GET', `/definitions/${enc(id)}/zip${qs({ version })}`)
    if (!reponse.ok) throw new ErreurApi(`Erreur ${reponse.status}`, reponse.status)
    const nom = /filename\*?=(?:UTF-8'')?"?([^";]+)/i.exec(reponse.headers.get('Content-Disposition') ?? '')?.[1]
    const lien = document.createElement('a')
    lien.href = URL.createObjectURL(await reponse.blob())
    lien.download = nom ? decodeURIComponent(nom) : `${id}.zip`
    lien.click()
    URL.revokeObjectURL(lien.href)
  }
}
