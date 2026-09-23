import { stringify } from 'yaml'
import type { ModeleProcessus } from './modele'

/** Contenu initial des fichiers annexes créés depuis le concepteur. */
export function gabaritFichier(type: 'requete' | 'gabarit', nom: string): string {
  const cle = nom.replace(/-([a-z])/g, (_, l: string) => l.toUpperCase())
  return type === 'requete'
    ? `# Gabarit YamlHttpClient. Modèle Handlebars : les « donnees » de l'étape.
http_client:
  ${cle}:
    method: GET
    url: https://services.exemple.gouv.qc.ca/ressources/{{id}}
    use_default_credentials: true
    headers:
      Accept: application/json
    # Réponse simulée : retirez ce bloc pour appeler le vrai service.
    mock:
      enabled: true
      status_code: 200
      headers:
        Content-Type: application/json
      body: '{ "ok": true }'
`
    : `# Gabarit de courriel (Handlebars sur le contexte du processus)
# a: destinataire@exemple.gouv.qc.ca     # optionnel si l'étape précise « a »
sujet: "Objet du courriel {{entrees.dossierId}}"
corps: |
  <p>Bonjour,</p>
  <p>…</p>
`
}

/** Processus d'exemple proposé dans un concepteur vide. */
export const EXEMPLE_YAML = `id: traitement-demande
nom: Traitement d'une demande
entrees:
  dossierId: { type: int, requis: true }
  courriel: { type: string, requis: true }
etapes:
  - id: valider
    type: http
    requete: valider-dossier.yml
    donnees: { id: "{{ entrees.dossierId }}" }
    retry: { tentatives: 3, delai: 00:00:30, backoff: 2 }
    statut: Validation du dossier
    suivant: approbation
  - id: approbation
    type: attendreEvenement
    evenement: decision
    delai: 5.00:00:00
    statut: En attente d'approbation
    siDelaiExpire: relance
    suivant:
      - { si: "{{ evenement.approuve == true }}", aller: confirmer }
      - { sinon: refuser }
  - id: relance
    type: courriel
    gabarit: relance-approbateur
    suivant: approbation
  - id: confirmer
    type: courriel
    gabarit: confirmation
    a: "{{ entrees.courriel }}"
    statut: Demande approuvée
    message: "Confirmation #{{ etapes.valider.sortie.numero }}"
    fin: true
  - id: refuser
    type: courriel
    gabarit: refus
    a: "{{ entrees.courriel }}"
    statut: Demande refusée
    fin: true
`

/** Ajoute un fichier annexe prérempli (s'il n'existe pas) et retourne son chemin. */
export function ajouterFichier(fichiers: Record<string, string>, type: 'requete' | 'gabarit', nom: string): string {
  const base = nom.replace(/\.ya?ml$/, '').split('#')[0]
  const chemin = type === 'requete' ? `${base}.yml` : `gabarits/${base}.yml`
  if (!(chemin in fichiers)) fichiers[chemin] = gabaritFichier(type, base)
  return chemin
}

const EXEMPLES: Record<string, unknown> = { string: 'texte', int: 1, number: 1.5, bool: true, date: '2026-01-01', object: {}, array: [] }

/** Cas de test prérempli : entrées d'exemple, un mock par étape http/sousProcessus, scénario de la première attente. */
export function casTestInitial(m: ModeleProcessus, nom: string): string {
  const cas: Record<string, unknown> = {
    nom,
    entrees: Object.fromEntries(m.entrees.map((e) => [e.nom, e.defaut ?? EXEMPLES[e.type] ?? ''])),
    mocks: Object.fromEntries(
      m.etapes
        .filter((e) => e.type === 'http' || e.type === 'sousProcessus')
        .map((e) => [e.id, e.type === 'http' ? { statut: 200, corps: {} } : { variables: {} }])
    ),
    scenario: m.etapes
      .filter((e) => e.type === 'attendreEvenement')
      .slice(0, 1)
      .map((e) => ({ attendre: e.id, evenement: { [e.props.evenement ?? 'evenement']: {} } })),
    attendu: { statut: 'Completed', parcours: m.etapes.map((e) => e.id) }
  }
  return `# Cas de test métier : exécuté sur le vrai moteur, sans appel ni courriel réel.
# mocks : réponse simulée par étape http (liste = une réponse par passage).
# scenario : « attendre » une étape d'attente, puis « evenement » ou « delaiExpire: true ».
# attendu : comparaison partielle (statut, etapeFinale, parcours, message, reponse, sorties, courriels, erreur…).
${stringify(cas, { lineWidth: 0 })}`
}
