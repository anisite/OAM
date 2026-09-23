// Métadonnées d'affichage des types d'étapes (palette, noeuds, formulaires).
// La validation fait autorité côté serveur (CatalogueEtapes.cs) : garder les deux synchronisés.

export type TypeChamp = 'texte' | 'expression' | 'duree' | 'objet' | 'requete' | 'gabarit' | 'processus' | 'etape'

export interface ChampEtape {
  nom: string
  libelle: string
  type: TypeChamp
  requis?: boolean
  precision?: string
  exemple?: string
}

export interface TypeEtapeUi {
  type: string
  libelle: string
  description: string
  couleur: string
  icone: string
  champs: ChampEtape[]
  retry: boolean
  /** Poignée « délai expiré » (siDelaiExpire). */
  delaiExpire?: boolean
}

export const TYPES_ETAPES: TypeEtapeUi[] = [
  {
    type: 'http',
    libelle: 'Appel HTTP',
    description: 'Appelle un service via un gabarit YamlHttpClient',
    couleur: '#095797',
    icone: '⇄',
    retry: true,
    champs: [
      { nom: 'requete', libelle: 'Gabarit de requête', type: 'requete', requis: true, precision: 'Fichier YamlHttpClient du paquet (fichier.yml ou fichier.yml#cle).' },
      { nom: 'donnees', libelle: 'Données transmises au gabarit', type: 'objet', precision: 'Modèle Handlebars du gabarit. Par défaut : tout le contexte.' }
    ]
  },
  {
    type: 'attendreEvenement',
    libelle: 'Attente d’événement',
    description: 'Attend un événement externe, avec délai',
    couleur: '#a86200',
    icone: '⏳',
    retry: false,
    delaiExpire: true,
    champs: [
      { nom: 'evenement', libelle: 'Nom de l’événement', type: 'texte', requis: true, exemple: 'decision' },
      { nom: 'delai', libelle: 'Délai d’attente', type: 'duree', precision: 'Ex. 5.00:00:00, 30m, 2h, 5j, P5D. Vide = sans limite.' }
    ]
  },
  {
    type: 'courriel',
    libelle: 'Courriel',
    description: 'Envoie un courriel à partir d’un gabarit',
    couleur: '#0d7a8c',
    icone: '✉',
    retry: true,
    champs: [
      { nom: 'gabarit', libelle: 'Gabarit', type: 'gabarit', requis: true, precision: 'gabarits/<nom>.yml : sujet, corps, a (optionnel).' },
      { nom: 'a', libelle: 'Destinataire(s)', type: 'expression', exemple: '{{ entrees.courriel }}' },
      { nom: 'cc', libelle: 'Copie conforme', type: 'expression' },
      { nom: 'donnees', libelle: 'Données supplémentaires', type: 'objet' }
    ]
  },
  {
    type: 'delai',
    libelle: 'Délai',
    description: 'Pause durable (durée ou date)',
    couleur: '#6b778a',
    icone: '⏱',
    retry: false,
    champs: [
      { nom: 'duree', libelle: 'Durée', type: 'duree', exemple: '1.00:00:00' },
      { nom: 'jusqua', libelle: 'Jusqu’à (date)', type: 'expression', exemple: '{{ entrees.dateLimite }}' }
    ]
  },
  {
    type: 'decision',
    libelle: 'Décision',
    description: 'Aiguillage selon des conditions',
    couleur: '#0f7a3d',
    icone: '◇',
    retry: false,
    champs: []
  },
  {
    type: 'definir',
    libelle: 'Variables',
    description: 'Calcule et conserve des valeurs',
    couleur: '#4a5a73',
    icone: '{ }',
    retry: false,
    champs: [{ nom: 'variables', libelle: 'Variables', type: 'objet', requis: true, precision: 'Accessibles ensuite via variables.<nom>.' }]
  },
  {
    type: 'sousProcessus',
    libelle: 'Sous-processus',
    description: 'Démarre un autre processus et attend sa fin',
    couleur: '#8c246b',
    icone: '⧉',
    retry: true,
    champs: [
      { nom: 'processus', libelle: 'Processus', type: 'processus', requis: true },
      { nom: 'version', libelle: 'Version', type: 'texte', precision: 'Vide = version courante au démarrage.' },
      { nom: 'entrees', libelle: 'Entrées', type: 'objet' }
    ]
  },
  {
    type: 'reponse',
    libelle: 'Réponse à l’appelant',
    description: 'Répond à l’appel synchrone (?attendre=), puis continue',
    couleur: '#1b6b4a',
    icone: '↩',
    retry: false,
    champs: [
      { nom: 'statutHttp', libelle: 'Statut HTTP', type: 'texte', exemple: '200', precision: 'Défaut 200.' },
      { nom: 'corps', libelle: 'Corps de la réponse (JSON)', type: 'objet', precision: 'Retourné tel quel à l’appelant de POST …/instances?attendre=30s.' }
    ]
  }
]

export function typeEtape(type: string): TypeEtapeUi {
  return (
    TYPES_ETAPES.find((t) => t.type === type) ?? {
      type,
      libelle: type || 'Type inconnu',
      description: '',
      couleur: '#cb381f',
      icone: '?',
      retry: false,
      champs: []
    }
  )
}

export const COULEURS_LIENS = {
  suivant: '#095797',
  branche: '#0f7a3d',
  sinon: '#6b778a',
  erreur: '#cb381f',
  delai: '#a86200',
  parcouru: '#0f7a3d'
}
