// Métadonnées d'affichage des types d'étapes (palette, noeuds, formulaires).
// La validation fait autorité côté serveur (CatalogueEtapes.cs) : garder les deux synchronisés.

// json : liste ou objet imbriqué édité en JSON (EditeurObjet transformerait une liste en objet).
export type TypeChamp = 'texte' | 'expression' | 'duree' | 'objet' | 'json' | 'requete' | 'gabarit' | 'processus' | 'etape'

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
      { nom: 'donnees', libelle: 'Données supplémentaires', type: 'objet' },
      { nom: 'langue', libelle: 'Langue', type: 'expression', precision: 'fr ou en : choisit les valeurs { fr, en } du gabarit.' }
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
  },
  // ── Transmission de documents (reprise d'ECS25A) : service configuré (Oim:Services:<type>), simulé par mock en test.
  {
    type: 'chargerDocuments',
    libelle: 'Charger les documents',
    description: 'Rassemble les documents de la soumission (références)',
    couleur: '#5b4a8b',
    icone: '▤',
    retry: true,
    champs: [
      { nom: 'documents', libelle: 'Documents FRW', type: 'expression', exemple: '{{ entrees.documents }}' },
      { nom: 'fichiersBruts', libelle: 'Fichiers bruts', type: 'expression', exemple: '{{ entrees.fichiersBruts }}' },
      { nom: 'piecesJointes', libelle: 'Noms des pièces jointes', type: 'objet', precision: 'nomEntrant → { fr, en }.' },
      { nom: 'fichiersFrw', libelle: 'Télécharger les fichiers FRW', type: 'texte', exemple: 'true' },
      { nom: 'pjDepotEcs', libelle: 'Pièces jointes du dépôt ECS', type: 'texte', exemple: 'true' },
      { nom: 'langue', libelle: 'Langue', type: 'expression', exemple: '{{ entrees.langue }}' }
    ]
  },
  {
    type: 'apparierGdi',
    libelle: 'Apparier au GDI',
    description: 'Recherche l’individu au GDI, le crée au besoin',
    couleur: '#5b4a8b',
    icone: '👤',
    retry: true,
    champs: [
      { nom: 'identite', libelle: 'Identité', type: 'json', requis: true, precision: 'nam, nas, cp12, nom, prenom, dateNaissance, sexe, adresse…' },
      { nom: 'comparaisons', libelle: 'Comparaisons', type: 'json', precision: 'Liste { nomChamp, valeur, champGdi, type, poids, comparer }.' },
      { nom: 'seuilGdi', libelle: 'Seuil GDI', type: 'texte', exemple: '100' },
      { nom: 'seuilVirq', libelle: 'Seuil VIRQ', type: 'texte', exemple: '100' },
      { nom: 'creer', libelle: 'Créer si introuvable', type: 'texte', exemple: 'true' }
    ]
  },
  {
    type: 'validerDossierAnterieur',
    libelle: 'Dossier antérieur',
    description: 'ASF allégé, présence INE, code secteur emploi',
    couleur: '#5b4a8b',
    icone: '🗂',
    retry: true,
    champs: [{ nom: 'noGdi', libelle: 'Numéro GDI', type: 'expression', requis: true, exemple: '{{ etapes.apparier.sortie.noGdi }}' }]
  },
  {
    type: 'genererPageGarde',
    libelle: 'Page de garde',
    description: 'Génère la page de garde PDF (GCO)',
    couleur: '#5b4a8b',
    icone: '📄',
    retry: true,
    champs: [
      { nom: 'gabarit', libelle: 'Gabarit GCO', type: 'texte', requis: true, exemple: 'PageGarde.docx' },
      { nom: 'titre', libelle: 'Titre', type: 'texte' },
      { nom: 'sousTitre', libelle: 'Sous-titre', type: 'expression' },
      { nom: 'identite', libelle: 'Identité', type: 'expression', exemple: '{{ entrees.contexteECS }}' },
      { nom: 'documents', libelle: 'Documents', type: 'expression', exemple: '{{ etapes.documents.sortie.documents }}' },
      { nom: 'fusionner', libelle: 'Fusionner au formulaire', type: 'texte', exemple: 'false' },
      { nom: 'nomFichier', libelle: 'Nom du fichier', type: 'texte' }
    ]
  },
  {
    type: 'deposerGed',
    libelle: 'Déposer à la GED',
    description: 'Dépose les documents à la GED (envois filtrés)',
    couleur: '#5b4a8b',
    icone: '🗄',
    retry: true,
    champs: [
      { nom: 'envois', libelle: 'Envois', type: 'json', requis: true, precision: 'Liste { actif, filtres, sujet {fr,en}, type, informationsSupplementaires, lignesAffaires }.' },
      { nom: 'documents', libelle: 'Documents', type: 'expression', requis: true },
      { nom: 'individu', libelle: 'Individu', type: 'objet' },
      { nom: 'informationsSupplementaires', libelle: 'Informations supplémentaires', type: 'objet' },
      { nom: 'langue', libelle: 'Langue', type: 'expression' }
    ]
  },
  {
    type: 'boiteGenerique',
    libelle: 'Boîte générique',
    description: 'Choisit la boîte (conditions, table BSQ) et lui écrit',
    couleur: '#0d7a8c',
    icone: '📬',
    retry: true,
    champs: [
      { nom: 'blocs', libelle: 'Blocs', type: 'json', requis: true, precision: 'Liste { si, a (par palier) | bsq { table, cle, region }, gabarit?, suffixeObjet? } : le premier applicable est retenu.' },
      { nom: 'gabarit', libelle: 'Gabarit', type: 'gabarit', requis: true },
      { nom: 'langue', libelle: 'Langue', type: 'expression' },
      { nom: 'donnees', libelle: 'Données supplémentaires', type: 'objet' }
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
