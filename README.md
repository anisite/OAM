# OIM — Orchestrateur de processus

Orchestrateur de processus métier décrits en **YAML déployable**, exécutés par
**DurableTask.Core** avec persistance **SQL Server** (`Microsoft.DurableTask.SqlServer`),
et piloté par une interface **Vue 3 + utd-webcomponents** (tableau de bord, suivi des
instances, concepteur par glisser-déposer).

```
┌──────────────────────────── OIM.Frontend (Vue 3, UTD, Vue Flow) ───────────────────────────┐
│ Choix de l'équipe · Tableau de bord · Instances · Processus · Concepteur (par équipe)     │
└──────────────────────────────────────────┬─────────────────────────────────────────────────┘
                                           │ /api (servi par OIM.Api, même site IIS)
┌──────────────────────────────────────────▼─────────────────────────────────────────────────┐
│ OIM.Api — minimal APIs, jeton Bearer (un ou plusieurs émetteurs) → habilitations           │
├────────────────────────────────────────────────────────────────────────────────────────────┤
│ OIM.Moteur                                                                                  │
│  Definitions   lecture/validation YAML, paquet (YAML + gabarits), durées, entrées          │
│  Expressions   langage {{ … }} pur et déterministe (conditions, interpolation)             │
│  Orchestration OrchestrationProcessus : UN interpréteur pour toutes les définitions        │
│                activités : chargerDefinition · http (YamlHttpClient) · courriel            │
│  Stockage      équipes, définitions versionnées (schéma oim)                                │
│  Pilotage      habilitations, équipes, démarrer, événements, pilotage, requêtes de suivi   │
│  Hebergement   TaskHubWorker + TaskHubClient (SqlOrchestrationService)                      │
└──────────────────────────────────────────┬─────────────────────────────────────────────────┘
                                           │
                SQL Server — schéma dt (DurableTask) + schéma oim (équipes, définitions)
```

## Démarrage (développement)

Prérequis : .NET 10, Node 22, SQL Server ou LocalDB.

```powershell
# 1. Base : instance LocalDB dédiée (la base OIM est créée au démarrage)
sqllocaldb create OIM -s

# 2. API + moteur (http://localhost:5080) — déploie automatiquement ./definitions/<equipe>/<processus>
cd sources/OIM.Api
dotnet run

# 3. Interface (http://localhost:5173, proxy /api → 5080)
cd sources/OIM.Frontend
npm install
npm run dev
```

En développement, les courriels sont écrits en `.eml` dans `sources/OIM.Api/App_Data/courriels`
et l'authentification est désactivée (`Oim:Securite:Active = false` : l'appelant est administrateur).
L'exemple est déployé dans l'équipe `demo` (créée au démarrage).

Essai rapide :

```powershell
$api = "http://localhost:5080/api"
Invoke-RestMethod "$api/processus/demo.traitement-demande/instances?instanceId=dossier-42" -Method Post `
  -ContentType application/json -Body '{ "dossierId": 42, "courriel": "citoyen@exemple.com" }'
Invoke-RestMethod "$api/instances/demo.dossier-42/statut"       # En attente d'approbation
Invoke-RestMethod "$api/instances/demo.dossier-42/evenements/decision" -Method Post `
  -ContentType application/json -Body '{ "approuve": true }'
Invoke-RestMethod "$api/instances/demo.dossier-42/statut"       # message : Confirmation #D-2026-00042
```

## Équipes

Chaque équipe a ses processus et ses instances, invisibles des autres équipes (sauf pour les
administrateurs et le support OIM). Les membres d'une équipe sont des **sujets** de jeton : un compte
ou un groupe AD (`MES\gr_equipe_sgd`), ou un groupe d'un autre fournisseur de jetons (avec son préfixe).
Les administrateurs les gèrent dans l'interface (**Administration**) ou par `/api/equipes`.

- **Identifiants qualifiés** : un processus s'appelle `equipe.id` (ex. `sgd.traitement-demande`); le YAML
  garde son `id` court et l'équipe vient du déploiement. Deux équipes peuvent avoir le même `id`.
  Une instance s'appelle `equipe.id` (`sgd.dossier-42`) : l'`instanceId` fourni au démarrage est préfixé
  par l'équipe, et deux équipes peuvent utiliser le même. Un sous-processus est toujours cherché dans
  l'équipe du parent.
- **Hors de portée = introuvable** : une ressource d'une autre équipe répond 404, sans révéler qu'elle existe.
- **Interface** : l'équipe fait partie de l'adresse (`/sgd/instances/dossier-42`); l'accueil propose le
  choix de l'équipe (directement l'équipe s'il n'y en a qu'une).

| Droits | Administrateur | Support | Membre |
|---|---|---|---|
| Voir (processus, instances, historique) | toutes les équipes | toutes | ses équipes |
| Piloter (démarrer, événements, suspendre, relancer, interrompre, supprimer) | toutes | toutes | ses équipes |
| Déployer, activer, tests métier | toutes | — | ses équipes |
| Gérer les équipes et leurs membres | ✓ | — | — |

Un appelant qui n'est ni administrateur, ni support, ni membre d'une équipe active reçoit 403.

## Définir un processus

Un **paquet** = le YAML du processus + ses fichiers annexes (gabarits de requêtes et de
courriels). Voir l'exemple complet dans [definitions/demo/traitement-demande](definitions/demo/traitement-demande).

```yaml
# yaml-language-server: $schema=../../schemas/processus.schema.json
id: traitement-demande
nom: Traitement d'une demande
entrees:
  dossierId: { type: int, requis: true }
  courriel:  { type: string, requis: true }

etapes:
  - id: valider
    type: http
    requete: valider-dossier.yml
    donnees: { id: "{{ entrees.dossierId }}" }
    retry: { tentatives: 3, delai: 00:00:30, backoff: 2 }
    statut: "Validation du dossier"
    suivant: approbation

  - id: approbation
    type: attendreEvenement
    evenement: decision
    delai: 5.00:00:00
    statut: "En attente d'approbation"
    siDelaiExpire: relance
    suivant:
      - si: "{{ evenement.approuve == true }}"
        aller: confirmer
      - sinon: refuser
  # …
```

### Types d'étapes

| Type | Propriétés | Traduction DurableTask |
|------|------------|------------------------|
| `http` | `requete` (gabarit YamlHttpClient, `fichier.yml` ou `fichier.yml#cle`), `donnees` | activité `oim.http` (+ `retry`) |
| `attendreEvenement` | `evenement`, `delai`, `siDelaiExpire` | événement externe + `CreateTimer` (la première des deux gagne) |
| `courriel` | `gabarit`, `a`, `cc`, `cci`, `donnees` | activité `oim.courriel` (+ `retry`) |
| `delai` | `duree` ou `jusqua` | `CreateTimer` durable |
| `decision` | branches dans `suivant` | — |
| `definir` | `variables: { nom: valeur }` | — (état de l'orchestration) |
| `sousProcessus` | `processus`, `version`, `entrees` | `CreateSubOrchestrationInstance` (+ `retry`) |
| `reponse` | `statutHttp` (défaut 200), `corps` | statut personnalisé lu par l'API — le processus continue |
| `boiteGenerique` | `blocs` (`si`, `a` par palier ou `bsq { table, cle, region }`, `gabarit`, `suffixeObjet`), `gabarit`, `langue`, `donnees` | activités `oim.resoudreBoite` puis `oim.courriel` : premier bloc applicable, table BSQ du paquet (`{palier}` dans le nom) |
| `chargerDocuments`, `apparierGdi`, `validerDossierAnterieur`, `genererPageGarde`, `deposerGed` | voir `schemas/processus.schema.json` | activité `oim.service` : POST des propriétés résolues vers `Oim:Services:<type>:Url`; `mocks` en test |

Les cinq derniers types (reprise d'ECS25A, exemple `definitions/ecs/frw-3003`) ne sont que des **contrats** :
les services qui les exécutent (GDI, GED/GCO228, GCO219, dossier antérieur, documents) restent à exposer.
Les documents circulent en **références**, jamais en octets dans l'historique.
Dans un courriel, `de`, `a`, `cc`, `sujet` et `corps` peuvent être déclinés par palier (`{ unitaire, production, tous, defaut }`)
ou par langue (`{ fr, en }`).

Propriétés communes : `statut` (statut métier affiché), `message` (retourné au client, évalué
après l'étape), `suivant`, `fin`, `siErreur`, `retry { tentatives, delai, backoff, delaiMax }`.

- **Départ** : la première étape de la liste.
- **Suivant** : `suivant: etape`, ou une liste de `{ si, aller }` terminée par `{ sinon }`.
  Sans `suivant` (ou avec `fin: true`), le processus se termine.
- **Erreurs** : après épuisement des reprises, `siErreur` mène à une étape de compensation
  (message disponible dans `erreur.message`); sinon l'instance passe **en échec** et peut
  être **relancée** depuis le tableau de bord (rewind : seule l'activité échouée est rejouée).
- **Événement reçu en avance** : un événement arrivé alors que l'étape d'attente n'est pas
  encore active (ex. pendant une relance) est conservé pour la prochaine attente de ce nom.
- **Durées** : `00:00:30`, `5.00:00:00`, `30s`, `15m`, `2h`, `5j`, `P5D`.

### Expressions `{{ … }}`

Une valeur composée d'une seule expression conserve son type (`"{{ entrees.dossierId }}"` → nombre);
sinon les expressions sont interpolées en texte.

| Variables | |
|---|---|
| `entrees.<nom>` | entrées normalisées de l'instance |
| `etapes.<id>.sortie` | sortie d'une étape (corps JSON d'un appel HTTP, données d'un événement…) |
| `evenement` | données du dernier événement reçu |
| `variables.<nom>` | valeurs calculées par une étape `definir` |
| `erreur.message`, `erreur.etape` | après un `siErreur` |
| `instance.id`, `instance.processus`, `instance.version`, `maintenant` | contexte d'exécution |

Opérateurs : `== != > < >= <=`, `&& || !` (ou `et ou non`), `+ - * / %`, `a ?? b`, `c ? a : b`.
Fonctions : `longueur`, `vide`, `contient`, `commencePar`, `minuscule`, `majuscule`, `texte`,
`nombre`, `arrondi`, `premier`, `joindre`, `json`, `ajouterJours`, `ajouterHeures`, `formaterDate`,
`si(cond, a, b)`, `sousChaine(x, debut, longueur?)`, `remplacer(x, a, b)`, `formaterNom` (majuscules sans accents), `formaterNAS`.
Les expressions sont **pures** (aucune horloge, aucun accès externe) : c'est ce qui les rend
sûres lors des relectures DurableTask.

### Gabarits

- **Requête HTTP** : fichier YamlHttpClient (`http_client: <cle>: …`). Le modèle Handlebars est
  la valeur de `donnees` de l'étape (par défaut, tout le contexte). Un statut non 2xx fait échouer
  l'activité (donc déclenche `retry`). Le bloc `mock` permet de simuler le service.
- **Courriel** : `gabarits/<nom>.yml` avec `sujet`, `corps` (HTML par défaut, `format: texte`
  sinon), `a`/`cc`/`de` optionnels. Syntaxe Handlebars sur le contexte (`{{entrees.x}}`,
  `{{#if evenement.motif}}…{{/if}}`).

## Tests métier

Chaque paquet peut contenir des cas de test : `tests/<nom>.yml` (schéma :
[schemas/cas-test.schema.json](schemas/cas-test.schema.json)). Un cas décrit les entrées, les
réponses simulées des services, ce que fait le monde extérieur, et le résultat attendu.

```yaml
nom: Relance de l'approbateur après le délai
entrees: { dossierId: 44, courriel: citoyen@exemple.com }

mocks:                                  # par id d'étape http (liste = une réponse par passage)
  valider: { statut: 200, corps: { valide: true, numero: D-44 } }

scenario:
  - attendre: approbation
    delaiExpire: true                   # simule les 5 jours écoulés
  - attendre: approbation
    evenement: { decision: { approuve: true } }

attendu:                                # comparaison partielle
  statut: Completed
  parcours: [valider, accuser, approbation, relance, approbation, confirmer]
  reponse: { statutHttp: 201, corps: { numero: D-44 } }
  courriels:
    - a: [approbateurs@exemple.gouv.qc.ca]
    - a: [citoyen@exemple.com]
```

**Exécution** : sur le vrai moteur DurableTask, en mode test. Le paquet est enregistré comme
brouillon (table `oim.Brouillons`, lisible par tous les nœuds). Chaque cas démarre une instance :
- **appels HTTP** : le gabarit est chargé et la requête construite (URL, en-têtes : les erreurs de
  gabarit sont détectées), mais c'est le mock qui répond. Un statut hors 2xx échoue comme un vrai
  appel (reprises accélérées, puis `siErreur` ou échec);
- **courriels** : entièrement rendus, jamais envoyés (comparables dans `attendu.courriels`);
- **délais** : aucune minuterie réelle; l'expiration d'une attente est déclenchée par le scénario;
- **sous-processus** : remplacés par leur mock (la sortie du processus enfant).

Les instances de test (`equipe.~test-…`) sont purgées à la fin et n'apparaissent jamais au tableau de bord
(option « conserver » pour les inspecter).

| `attendu` | Vérifie |
|-----------|---------|
| `statut` | `Completed`, `Running` (en attente), `Failed`, `Terminated` — ou `Termine`, `EnCours`, `EnEchec` |
| `etape`, `attente` | étape et événement attendus quand l'instance reste en attente |
| `etapeFinale`, `parcours` | dernière étape; liste exacte des étapes visitées |
| `statutMetier`, `message` | statut personnalisé et message au client |
| `reponse` | réponse synchrone `{ statutHttp, corps }` |
| `sorties`, `variables` | sorties par étape, variables (partiel) |
| `courriels` | courriels dans l'ordre : `gabarit`, `a`, `cc`, `sujet`, `corps` (partiel) |
| `erreur` | extrait du message d'erreur |

**Où ils s'exécutent**

| | |
|---|---|
| Déploiement (API, interface) | avant d'enregistrer la version. `Oim:Tests:AuDeploiement` : `Bloquant` (défaut : un échec refuse la version, HTTP 422 avec le rapport; `?ignorerTests=true` pour forcer), `Avertissement`, `Desactive`. Pas au démarrage (dossier), le moteur n'étant pas encore lancé |
| API | `POST /api/definitions/{id}/tests?version=&cas=&conserver=` (version déployée), `POST /api/definitions/tests?equipe=` `{ yaml, fichiers }` (paquet non déployé) |
| Interface | onglet **Tests métier** d'un processus; bouton **Exécuter les tests** du concepteur (+ **Cas de test** prérempli dans « Fichiers annexes ») |
| CI | `dotnet test` : chaque `tests/*.yml` de `definitions/<equipe>/<processus>/` devient un test MSTest (`TestsMetierTests.Cas_metier`) |

Le validateur signale aussi, en continu, les incohérences d'un cas avec la définition
(mock ou étape de scénario inexistants, étape http sans mock).

## Déployer une définition

Chaque déploiement dont le contenu change crée une **version immuable** (empreinte SHA-256).
Les instances en cours restent sur leur version; les nouvelles utilisent la version courante.

| Moyen | Comment |
|-------|---------|
| Concepteur | onglet **Concepteur** de l'équipe → *Déployer* (validation serveur en continu) |
| Fichier | page **Processus** de l'équipe → *Déployer un fichier* (`.zip` du dossier, ou `.yml` seul) |
| API | `POST /api/definitions?equipe=` `{ yaml, fichiers: { "chemin": "contenu" }, commentaire }` ou `POST /api/definitions/zip?equipe=` (multipart `fichier`). `equipe` est facultatif si l'appelant n'est membre que d'une équipe |
| Dossier | `Oim:DossierDefinitions/<equipe>/<processus>/` est déployé au démarrage (équipe créée au besoin, sans membres) |

`POST /api/definitions/valider` retourne les diagnostics sans rien enregistrer (idéal en CI).

## API

**Authentification.** Toutes les routes `/api` exigent un jeton `Authorization: Bearer`. Plusieurs
émetteurs peuvent être acceptés (`Oim:Securite:Fournisseurs`) : le jeton est validé par le fournisseur
dont l'émetteur (`iss`) correspond. Quel que soit l'émetteur, OIM n'en retient que le nom et les
**sujets** (le nom et les groupes, avec le préfixe du fournisseur), d'où il déduit les droits :
administrateur (`Securite:Administrateurs`), support (`Securite:Support`) et équipes (membres
enregistrés dans OIM, pris en compte sans renouveler le jeton).

Le fournisseur **Interne** émet lui-même ses jetons, par authentification Windows (Negotiate) :

| | |
|---|---|
| `GET /api/auth/jeton` | `{ jeton, expiration, utilisateur }` : JWT HS256 signé par OIM, avec le compte AD (`name`) et ceux de ses groupes AD qui servent à OIM (`role`). 403 si le compte n'a accès à aucune équipe |
| `GET /api/auth/jeton-dev` | développement seulement : jeton du compte Windows courant (le proxy Vite ne relaie pas la négociation Windows) |

Un service .NET l'obtient avec son compte de service
(`new HttpClient(new HttpClientHandler { UseDefaultCredentials = true })`), puis le renouvelle avant
`expiration`. L'interface le garde en mémoire et le renouvelle automatiquement.

**Appelant et équipes** : `GET /api/moi` (droits, équipes); `GET /api/equipes` (ses équipes, toutes pour
admin et support, avec compteurs d'instances), `GET /api/equipes/{id}`; administrateurs :
`POST /api/equipes` `{ id, nom, description, membres }`, `PUT /api/equipes/{id}` `{ nom, description, actif }`,
`PUT /api/equipes/{id}/membres` `{ sujets }`, `DELETE /api/equipes/{id}` (refusé si l'équipe a des processus).

**Applications clientes**

| | |
|---|---|
| `POST /api/processus/{equipe.id}/instances?instanceId=&version=&attendre=` | démarre (corps = entrées); retourne l'id complet `equipe.instanceId`. `instanceId` rend l'appel idempotent : 409 si une instance active porte déjà cet id. `attendre` : voir ci-dessous |
| `GET /api/instances/{id}/reponse?attendre=` | reprend l'attente d'une réponse synchrone |
| `GET /api/instances/{id}/statut` | `statut`, `statutMetier`, `message`, `evenementAttendu`, `sortie` |
| `POST /api/instances/{id}/evenements/{nom}` | envoie un événement (corps JSON = `evenement`) |

### Réponse synchrone à l'appelant

Avec `?attendre=30s`, le démarrage reste ouvert jusqu'à ce que le processus ait quelque chose à répondre :

```yaml
  - id: accuser
    type: reponse
    statutHttp: 201
    corps:
      numero: "{{ etapes.valider.sortie.numero }}"
      statut: "Demande reçue, en attente d'approbation"
    suivant: approbation        # le processus continue après avoir répondu
```

| Situation | Réponse HTTP |
|-----------|--------------|
| une étape `reponse` s'exécute | son `statutHttp` et son `corps`, tels quels |
| le processus se termine sans étape `reponse` | `200` : `statutMetier`, `message`, `sortie` |
| le processus échoue ou est interrompu | `500` ProblemDetails |
| délai dépassé | `202` + `suivi` : `GET /api/instances/{id}/reponse?attendre=30s` |

Les en-têtes `X-Oim-Instance` et `Location` donnent toujours l'instance. L'attente est plafonnée par
`Oim:AttenteSynchroneMax` (2 min). La réactivité dépend de `Oim:ScrutationMax` (500 ms).

**Pilotage** : `GET /api/tableau-de-bord?equipe=`, `GET /api/instances?equipe=&statut=&processus=&recherche=&attente=&page=`,
`GET /api/instances/{id}`, `GET /api/instances/{id}/historique`,
`POST /api/instances/{id}/{terminer|suspendre|reprendre|relancer|redemarrer}`, `DELETE /api/instances/{id}` (purge),
`GET /api/definitions?equipe=`, `GET|PUT /api/definitions/{equipe.id}/...`, `GET /api/catalogue`. En développement : documentation interactive **Scalar** sur `/scalar` (jeton Bearer à coller dans *Authentication*) et OpenAPI sur `/openapi/v1.json`.

## Interface

- **Accueil** : choix de l'équipe (cartes avec instances actives, en attente, en échec; recherche).
  Toutes les pages suivantes sont celles de l'équipe choisie (`/<equipe>/...`).
- **Administration** (administrateurs) : équipes, membres, activation.
- **Tableau de bord** : compteurs (en cours, en attente d'un événement, suspendues, 24 h),
  instances par processus et statut, attentes avec échéance, échecs récents avec *Relancer*.
- **Instances** : recherche (id, statut métier, valeur d'entrée), filtres, pagination.
  Détail : statut métier et message, **graphe du parcours** (étapes visitées, étape courante,
  chemin emprunté), données, historique DurableTask; actions : envoyer un événement,
  suspendre, reprendre, relancer, interrompre, redémarrer, supprimer.
- **Processus** : versions, YAML, fichiers, graphe, démarrage par un formulaire généré depuis `entrees`.
- **Concepteur** : palette glisser-déposer, liaisons à la souris (suivant, conditions, sinon,
  erreur, délai expiré), panneau de propriétés, YAML synchronisé, gabarits annexes,
  validation serveur en continu et déploiement.

## Configuration (`Oim`)

| Clé | Défaut | |
|-----|--------|--|
| `ConnectionStrings:Oim` / `Oim:ConnexionSql` | | base partagée : schémas `dt` et `oim` |
| `TaskHub` | `oim` | isole plusieurs applications dans la même base |
| `SchemaDurableTask` | `dt` | schéma des tables DurableTask |
| `TaskHubParApplication` | `true` | task hub déterminé par l'application (et non l'utilisateur SQL) : IIS, développeurs et outils voient les mêmes instances |
| `CreerBaseSiAbsente` | `true` (dev) | |
| `DossierDefinitions` | | déploiement au démarrage |
| `AttenteSynchroneMax` | `00:02:00` | plafond de `?attendre=` |
| `ScrutationMax` | `00:00:00.5` | intervalle max de scrutation du worker (DurableTask : 3 s) |
| `MaxActivitesConcurrentes`, `MaxOrchestrationsActives` | | par nœud |
| `Courriel:Hote/Port/Ssl/Utilisateur/MotDePasse/De` | | SMTP |
| `Courriel:DossierDepot` | | écrit des `.eml` au lieu d'envoyer |
| `Courriel:RedirigerVers` | | redirige tous les courriels (environnements de test) |
| `Palier` | `unitaire` | palier courant (`unitaire`, `acceptation`, `techno`, `production`) : variantes des courriels, tables BSQ |
| `Services:<type>:Url`, `UtiliserIdentiteWindows`, `Delai` | | services des étapes `chargerDocuments`, `apparierGdi`, `validerDossierAnterieur`, `genererPageGarde`, `deposerGed` |
| `Tests:AuDeploiement` | `Bloquant` | tests métier au déploiement : `Bloquant`, `Avertissement`, `Desactive` |
| `Securite:Active` | `true` | jeton Bearer exigé sur `/api` (`false` : aucun jeton, l'appelant est administrateur) |
| `Securite:Administrateurs` | `[]` | sujets administrateurs OIM (ex. `MES\gr_oim_admin`) |
| `Securite:Support` | `[]` | sujets du support OIM |
| `Securite:Fournisseurs:<nom>` | `oim` | émetteurs de jetons acceptés (au moins un; un seul `Interne`) |
| `…:Type` | `Interne` | `Interne` (émis par `/api/auth/jeton`) ou `Externe` (validé seulement) |
| `…:Emetteur`, `Audience` | `OIM` | `iss` et `aud` attendus; l'émetteur désigne le fournisseur d'un jeton |
| `…:Cle` | | clé HS256, ≥ 32 caractères, **identique sur tous les nœuds**; hors du dépôt (ex. `Oim__Securite__Fournisseurs__oim__Cle`, secret du pipeline) |
| `…:Autorite` | | `Externe` : autorité OIDC (clés de signature lues dans ses métadonnées), au lieu de `Cle` |
| `…:ClaimNom`, `ClaimGroupes` | `name`, `role` | claims du nom et des groupes |
| `…:Prefixe` | | préfixe des sujets de ce fournisseur (ex. `entra:`) |
| `…:DureeMinutes` | `480` | `Interne` : durée de vie des jetons émis |

Exemple d'émetteur externe ajouté plus tard, sans autre changement :

```json
"entra": { "Type": "Externe", "Autorite": "https://login.microsoftonline.com/<tenant>/v2.0", "Emetteur": "https://login.microsoftonline.com/<tenant>/v2.0",
           "Audience": "api://oim", "ClaimNom": "preferred_username", "ClaimGroupes": "groups", "Prefixe": "entra:" }
```

## Production (IIS)

```powershell
cd sources/OIM.Frontend; npm ci; npm run build     # → sources/OIM.Api/wwwroot
cd ../OIM.Api; dotnet publish -c Release -o ./publish
```

Un seul site IIS (API + interface). Activer **à la fois** l'authentification Windows (pour
`/api/auth/jeton`) et l'authentification anonyme (les autres routes portent un jeton Bearer, que
IIS ne doit pas intercepter). Définir `Oim:Securite:Fournisseurs:oim:Cle` et `Oim:Securite:Administrateurs`.

### Base de données

Deux options :

- **Création par l'application** : le compte du pool d'applications doit pouvoir créer les schémas
  au premier démarrage (ou exécuter une fois avec un compte `db_owner`).
- **Création par un DBA** : [base-de-donnees/oim-creation.sql](base-de-donnees/oim-creation.sql) crée tout
  (tables, procédures et rôle `dt_runtime` de DurableTask, schéma `oim`, mode de task hub, rôle
  `oim_application`). Créer la base en `COLLATE Latin1_General_100_BIN2_UTF8` (comme le fournisseur),
  exécuter le script, puis ajouter le compte de l'application au rôle `oim_application` et mettre
  `Oim:CreerBaseSiAbsente` à `false`. L'application n'a alors besoin d'aucun droit DDL.

Le script est idempotent. Après une mise à jour du paquet `Microsoft.DurableTask.SqlServer`, le
régénérer (`base-de-donnees/Generer-ScriptBase.ps1`) et le faire exécuter avant de déployer
l'application : avec un compte `oim_application`, elle refuse de démarrer si le schéma n'est pas à jour.

### Plusieurs serveurs sur la même base

Chaque nœud exécute un worker; DurableTask répartit orchestrations et activités par verrous
SQL (`LockedBy`/`LockExpiration`, lecture `READPAST`) : un épisode ou une activité n'est traité
que par un nœud à la fois. Événements, démarrages synchrones (`?attendre=`) et tableau de bord
fonctionnent quel que soit le nœud appelé. Le démarrage d'un même `instanceId` est atomique (409).
Au démarrage simultané, la création du schéma `oim` et le déploiement du dossier de définitions
sont sérialisés par `sp_getapplock`.

Validé avec 2 nœuds sur une base neuve, démarrés au même instant : un seul déploiement v1,
40 instances réparties sur les deux nœuds, 40 courriels, aucun doublon.

**Livraison « au moins une fois »** : si un nœud tombe après avoir exécuté une activité
(appel HTTP, courriel) mais avant d'en enregistrer le résultat, l'activité est réexécutée par
un autre nœud à l'expiration du verrou. Pour que le service appelé puisse ignorer ce doublon,
OIM transmet une clé stable par exécution d'étape (identique entre les reprises) :
en-tête `Idempotency-Key` sur les appels HTTP, `X-OIM-Cle` sur les courriels.

## Tests

```powershell
dotnet test   # unitaires + intégration (LocalDB « OIM », base OIM_Tests)
```

Les tests d'intégration exécutent le vrai worker DurableTask sur SQL Server : approbation,
refus, délai expiré → relance, événement reçu en avance, `siErreur` après reprises, échec,
sous-processus, suspension/reprise. Ils sont ignorés si LocalDB est indisponible.
