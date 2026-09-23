// Contrats de l'API OIM (camelCase).

export type StatutRuntime =
  | 'Pending'
  | 'Running'
  | 'Suspended'
  | 'Completed'
  | 'Failed'
  | 'Terminated'
  | 'Canceled'
  | 'ContinuedAsNew'

export interface InstanceResume {
  instanceId: string
  processus: string
  version?: string
  statut: StatutRuntime
  creee: string
  miseAJour: string
  terminee?: string
  statutMetier?: string
  etape?: string
  evenementAttendu?: string
  echeance?: string
  erreur?: string
  parentInstanceId?: string
}

export interface PageInstances {
  elements: InstanceResume[]
  total: number
  page: number
  taille: number
}

export interface StatutPersonnalise {
  etape?: string
  typeEtape?: string
  statut?: string
  message?: string
  attente?: { evenement?: string; echeance?: string }
  erreur?: string
  transitions?: number
  parcours?: string[]
  miseAJour?: string
}

export interface InstanceDetail {
  instanceId: string
  processus: string
  version?: string
  statut: StatutRuntime
  creee: string
  miseAJour: string
  terminee?: string
  parentInstanceId?: string
  entrees?: unknown
  sortie?: any
  statutPersonnalise?: StatutPersonnalise
  erreur?: { type?: string; message: string; pile?: string }
  etiquettes?: Record<string, string>
}

export interface EvenementHistorique {
  executionId: string
  sequence: number
  type: string
  nom?: string
  tacheId?: number
  horodatage: string
  statut?: string
  donnees?: string
}

export interface Statistiques {
  parStatut: Record<string, number>
  parProcessus: { processus: string; statut: string; nombre: number }[]
  demarrees24h: number
  terminees24h: number
  echouees24h: number
  enAttenteEvenement: number
  echecsRecents: InstanceResume[]
  attentesProches: InstanceResume[]
}

export interface ResumeDefinition {
  id: string
  nom?: string
  description?: string
  versionCourante: number
  actif: boolean
  modifieLe: string
  deployePar?: string
  nbVersions: number
}

export interface ResumeVersion {
  version: number
  empreinte: string
  deployePar?: string
  deployeLe: string
  commentaire?: string
}

export interface DefinitionDetail {
  id: string
  version: number
  yaml: string
  fichiers: { chemin: string; contenu: string }[]
  empreinte: string
  deployePar?: string
  deployeLe: string
  commentaire?: string
  definition?: any
  versions: ResumeVersion[]
}

export interface Diagnostic {
  gravite: 'erreur' | 'avertissement'
  message: string
  etape?: string
  ligne?: number
}

export interface ReponseValidation {
  valide: boolean
  diagnostics: Diagnostic[]
  definition?: any
}

export interface ReponseDeploiement {
  id: string
  version: number
  nouvelleVersion: boolean
  diagnostics: Diagnostic[]
  tests?: RapportTests
}

export interface ResultatCas {
  fichier: string
  nom: string
  reussi: boolean
  ecarts: string[]
  dureeMs: number
  instanceId?: string
  obtenu?: Record<string, any>
}

export interface RapportTests {
  processus: string
  cas: ResultatCas[]
  diagnostics: Diagnostic[]
  reussis: number
  echoues: number
  reussi: boolean
}
