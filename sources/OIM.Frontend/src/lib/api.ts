import type {
  DefinitionDetail,
  EvenementHistorique,
  InstanceDetail,
  PageInstances,
  ReponseDeploiement,
  RapportTests,
  ReponseValidation,
  ResumeDefinition,
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

async function appel<T>(methode: string, url: string, corps?: unknown): Promise<T> {
  const init: RequestInit = { method: methode, credentials: 'include', headers: {} }
  if (corps instanceof FormData) {
    init.body = corps
  } else if (corps !== undefined) {
    init.body = JSON.stringify(corps)
    ;(init.headers as Record<string, string>)['Content-Type'] = 'application/json'
  }

  const reponse = await fetch(`/api${url}`, init)
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
  tableauDeBord: () => appel<Statistiques>('GET', '/tableau-de-bord'),
  instances: (filtre: {
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
  definitions: () => appel<ResumeDefinition[]>('GET', '/definitions'),
  definition: (id: string, version?: number) =>
    appel<DefinitionDetail>('GET', `/definitions/${enc(id)}${qs({ version })}`),
  valider: (yaml: string, fichiers: Record<string, string>) =>
    appel<ReponseValidation>('POST', '/definitions/valider', { yaml, fichiers }),
  deployer: (yaml: string, fichiers: Record<string, string>, commentaire?: string, ignorerTests = false) =>
    appel<ReponseDeploiement>('POST', `/definitions${qs({ ignorerTests })}`, { yaml, fichiers, commentaire }),
  /** Tests métier d'un paquet non déployé (concepteur). */
  testerPaquet: (yaml: string, fichiers: Record<string, string>, options: { cas?: string; conserver?: boolean } = {}) =>
    appel<RapportTests>('POST', `/definitions/tests${qs(options)}`, { yaml, fichiers }),
  /** Tests métier d'une version déployée. */
  tester: (id: string, options: { version?: number; cas?: string; conserver?: boolean } = {}) =>
    appel<RapportTests>('POST', `/definitions/${enc(id)}/tests${qs(options)}`),
  deployerZip: (fichier: File, commentaire?: string) => {
    const f = new FormData()
    f.append('fichier', fichier)
    return appel<ReponseDeploiement>('POST', `/definitions/zip${qs({ commentaire })}`, f)
  },
  activer: (id: string, actif: boolean) => appel<void>('PUT', `/definitions/${enc(id)}/actif`, { actif }),
  urlZip: (id: string, version?: number) => `/api/definitions/${enc(id)}/zip${qs({ version })}`
}
