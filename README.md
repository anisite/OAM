# OAM — Orchestrateur d'Actions Métier

Plateforme d'orchestration de workflows basée sur des définitions YAML, avec suivi en temps réel, authentification NTLM→JWT et déploiement continu vers IIS Windows.

---

## Table des matières

1. [Vue d'ensemble](#1-vue-densemble)
2. [Architecture](#2-architecture)
3. [Prérequis](#3-prérequis)
4. [Démarrage rapide](#4-démarrage-rapide)
5. [Structure du projet](#5-structure-du-projet)
6. [Couche Domain](#6-couche-domain)
7. [Couche Infrastructure](#7-couche-infrastructure)
8. [Moteur de workflow](#8-moteur-de-workflow)
9. [API REST](#9-api-rest)
10. [Frontend Vue.js](#10-frontend-vuejs)
11. [Temps réel — SignalR](#11-temps-réel--signalr)
12. [Authentification](#12-authentification)
13. [Définitions YAML](#13-définitions-yaml)
14. [Système de mocks](#14-système-de-mocks)
15. [Tests](#15-tests)
16. [CI/CD Azure DevOps](#16-cicd-azure-devops)
17. [Configuration](#17-configuration)
18. [Déploiement IIS](#18-déploiement-iis)

---

## 1. Vue d'ensemble

OAM permet aux équipes de définir, déployer et surveiller des workflows d'actions métier sans intervention de l'équipe technique. Chaque workflow est décrit en YAML, versionné par hash SHA256, et exécuté par un moteur qui supporte :

- Appels HTTP configurables via YAML (`YamlHttpClient`)
- Conditions et branchements dynamiques
- Pause/reprise sur point d'entrée externe (hooks)
- Mocks intégrés pour les environnements de test
- Résolution de variables via Handlebars + extraction JSONPath
- Traçabilité bout-en-bout par `CorrelationId`
- Suivi temps réel via SignalR

---

## 2. Architecture

```
┌─────────────────────────────────────────────────────┐
│                   OAM.Frontend                      │
│          Vue.js 3 + Pinia + SignalR client          │
└─────────────────┬───────────────────────────────────┘
                  │ HTTP + WebSocket
┌─────────────────▼───────────────────────────────────┐
│                    OAM.Api                          │
│   ASP.NET Core 10 · Controllers · SignalR Hub       │
│   Authentification JWT Bearer + NTLM Negotiate      │
└────────┬──────────────────────┬─────────────────────┘
         │                      │
┌────────▼───────┐   ┌──────────▼──────────────────────┐
│  OAM.Domain    │   │      OAM.Workflow.Core           │
│  Entities      │   │  MoteurWorkflow · Connecteurs    │
│  Interfaces    │   │  YAML Parser · Handlebars        │
│  Enums         │   │  GestionnaireMock                │
└────────┬───────┘   └──────────┬──────────────────────┘
         │                      │
┌────────▼──────────────────────▼─────────────────────┐
│                OAM.Infrastructure                   │
│          EF Core · Repositories · OamDbContext      │
│          SQL Server (prod) / SQLite (dev)           │
└─────────────────────────────────────────────────────┘
```

---

## 3. Prérequis

| Composant | Version minimale |
|-----------|-----------------|
| .NET SDK | 10.0 |
| Node.js | 18.16.1 |
| SQL Server | 2019 (prod) / SQLite (dev) |
| Visual Studio | 2022 Community ou supérieur |
| IIS + ASP.NET Core Hosting Bundle | Pour déploiement |

---

## 4. Démarrage rapide

### Backend (développement)

```powershell
cd src\OAM.Api
dotnet run
# Démarre sur http://localhost:5000
# Crée automatiquement oam_dev.db (SQLite)
# Seede les mocks depuis seed-mocks.json
```

Ou depuis **Visual Studio** : sélectionner le profil **IIS Express** et lancer.

### Frontend (développement)

```bash
cd src/OAM.Frontend
npm install
npm run dev
# Démarre sur http://localhost:5173
# Proxy /api et /hubs vers http://localhost:5000
```

Ouvrir `http://localhost:5173` dans le navigateur.

### Déployer un workflow exemple

```powershell
# Obtenir un token dev
$token = (Invoke-RestMethod "http://localhost:5000/api/auth/dev-token").token

# Déployer la définition
$headers = @{ Authorization = "Bearer $token"; "Content-Type" = "application/json" }
Invoke-RestMethod "http://localhost:5000/api/definitions/deployer" -Method POST -Headers $headers -InFile src\OAM.Api\deploy-test.json

# Démarrer une instance
$body = @{ definitionId = "<id retourné>"; donneesEntree = '{"rendezVousId":"RDV-001"}' } | ConvertTo-Json
Invoke-RestMethod "http://localhost:5000/api/instances/demarrer" -Method POST -Headers $headers -Body $body
```

---

## 5. Structure du projet

```
OAM/
├── OAM.sln
├── nuget.config
├── README.md
├── src/
│   ├── OAM.Domain/               # Entités, interfaces, enums
│   ├── OAM.Infrastructure/       # EF Core, repositories
│   ├── OAM.Workflow.Core/        # Moteur, connecteurs, YAML, Handlebars
│   ├── OAM.Api/                  # ASP.NET Core API + Frontend intégré
│   │   ├── Controllers/
│   │   ├── Auth/
│   │   ├── Hubs/
│   │   ├── Dtos/
│   │   ├── Properties/launchSettings.json
│   │   ├── appsettings.json
│   │   ├── appsettings.Development.json
│   │   ├── seed-mocks.json       # Mocks auto-seedés en développement
│   │   ├── deploy-test.json      # Payload de déploiement exemple
│   │   └── web.config
│   └── OAM.Frontend/             # Vue.js 3 SPA
│       ├── src/
│       │   ├── pages/
│       │   ├── components/
│       │   ├── stores/
│       │   ├── composables/
│       │   ├── router/
│       │   └── assets/
│       ├── index.html
│       ├── vite.config.js
│       └── package.json
├── tests/
│   └── OAM.Tests/                # Tests unitaires MSTest
├── workflows/
│   ├── workflow.exemple.yml
│   ├── extensions.exemple.yml
│   └── workflow-schema.json      # Schéma JSON pour validation IDE
└── pipelines/
    └── azure-pipelines.yml
```

---

## 6. Couche Domain

Contient les contrats et entités métier. Aucune dépendance vers d'autres couches.

### Entités

| Entité | Description |
|--------|-------------|
| `DefinitionWorkflow` | Gabarit d'un workflow (YAML + hash SHA256) |
| `VersionDefinitionWorkflow` | Historique de chaque déploiement (audit trail) |
| `InstanceWorkflow` | Exécution concrète d'une définition |
| `ExecutionTache` | Exécution d'une tâche individuelle dans une instance |

### Enums

**EtatWorkflow** : `EnAttente` · `EnCours` · `EnPause` · `EnErreur` · `Termine` · `Annule`

**EtatTache** : `EnAttente` · `EnCours` · `Reussie` · `EnErreur` · `Ignoree` · `EnPause`

### Interfaces clés

- **`IMoteurWorkflow`** — `DemarrerAsync`, `ReprendreAsync`, `ReprendreTacheAsync`, `PauserAsync`, `AnnulerAsync`
- **`IConnecteur`** — `ExecuterAsync(ContexteConnecteur)` → `ResultatConnecteur`
- **`INotificateurWorkflow`** — Notifications SignalR pour chaque transition d'état
- **`IDefinitionWorkflowRepository`**, **`IInstanceWorkflowRepository`**, **`IExecutionTacheRepository`**

---

## 7. Couche Infrastructure

### Base de données

OAM utilise **Entity Framework Core** avec support dual :
- **SQL Server** en production (connexion par string contenant `Server=`)
- **SQLite** en développement (connexion `Data Source=oam_dev.db`)

La détection est automatique dans `Program.cs` à partir de la chaîne de connexion.

En développement, `EnsureCreated()` crée automatiquement le schéma au démarrage.

### Tables principales

| Table | Clé | Description |
|-------|-----|-------------|
| `DefinitionsWorkflow` | Nom (unique) | Gabarits de workflows |
| `VersionsDefinitionWorkflow` | FK vers définition | Historique des versions |
| `InstancesWorkflow` | CorrelationId (indexé) | Instances d'exécution |
| `ExecutionsTache` | (InstanceId, NomTache) | Tâches par instance |

Les états (`Etat`) sont stockés en base sous forme de **chaîne de caractères** (ex. `"EnCours"`) pour la lisibilité.

---

## 8. Moteur de workflow

### Cycle d'exécution

```
DemarrerAsync()
  ├─ Créer InstanceWorkflow (état: EnCours)
  ├─ Notifier SignalR
  └─ ExecuterTachesAsync()
       ├─ Pour chaque tâche:
       │   ├─ Évaluer condition Handlebars → si faux: Ignoree, passer à suivant
       │   ├─ Résoudre paramètres (Handlebars)
       │   ├─ Sélectionner connecteur (mock prioritaire si disponible)
       │   ├─ Exécuter connecteur
       │   ├─ Si succès: stocker sortie dans contexte, déterminer suivant
       │   ├─ Si erreur: MarquerEnErreurAsync → arrêter
       │   └─ Si hook: mettre en pause → arrêter, attendre callback externe
       └─ Toutes tâches terminées → Etat: Termine
```

### Résolution de variables

Les paramètres de chaque tâche sont résolus via **Handlebars** :

```yaml
parametres:
  url: "https://api.example.com/rdv/{{ rendezVousId }}"
  token: "{{ config.auth.token }}"
  id: "{{ tache.rechercherRendezVous.output.id }}"
```

Le contexte d'exécution (`ContexteExecution`) est un dictionnaire JSON persisté en base, mis à jour après chaque tâche réussie.

### Extraction JSONPath

Les mappings `output` extraient des valeurs de la réponse via JSONPath :

```yaml
output:
  rendezVousId: "$.data.rendezVous.id"
  courrielClient: "$.data.rendezVous.courriel"
```

### Navigation conditionnelle

```yaml
branches:
  - condition: "{{ rendezVousId }}"   # truthy si non-vide, non-false, non-null
    aller: notifierClient
  - condition: "true"
    aller: aucunRendezVous
```

**Règles de vérité** : toute valeur non-vide, non `false`, non `0`, non `null`, non `non` est considérée vraie.

### Connecteurs disponibles

| Type | Description |
|------|-------------|
| `http` | Appel HTTP via `YamlHttpClient` (config YAML déclarative) |
| `condition` | Évalue une expression Handlebars pour le branchement |
| `mock` | Retourne une réponse préconfigurée du catalogue de mocks |
| `hook` | Pause le workflow en attente d'un callback externe |

### Reprise sur erreur

```
Tâche en erreur → état EnErreur
  ↓
UI affiche l'erreur + données d'entrée
  ↓
Utilisateur corrige les données (PATCH /api/instances/{id}/taches)
  ↓
POST /api/instances/{id}/reprendre-tache { nomTache, donneesEntreeCorrigees }
  ↓
Moteur reprend depuis la tâche corrigée
```

---

## 9. API REST

Toutes les routes sont protégées par **JWT Bearer** sauf mention contraire.

### Définitions

| Méthode | Route | Description |
|---------|-------|-------------|
| `GET` | `/api/definitions` | Lister les définitions (filtre `?equipe=`) |
| `GET` | `/api/definitions/{id}` | Détail d'une définition |
| `GET` | `/api/definitions/{id}/yaml` | Contenu YAML brut |
| `GET` | `/api/definitions/{id}/versions` | Historique des versions |
| `POST` | `/api/definitions/deployer` | Déployer une nouvelle définition |

**Body de déploiement** :
```json
{
  "nom": "MonWorkflow",
  "description": "Description optionnelle",
  "equipe": "Equipe-A",
  "contenuYaml": "nom: MonWorkflow\n..."
}
```

### Instances

| Méthode | Route | Description |
|---------|-------|-------------|
| `GET` | `/api/instances` | Lister les instances (filtre `?etat=`, `?definitionId=`) |
| `GET` | `/api/instances/{id}` | Détail avec tâches |
| `GET` | `/api/instances/erreurs` | Instances en erreur |
| `POST` | `/api/instances/demarrer` | Démarrer un workflow |
| `POST` | `/api/instances/{id}/reprendre` | Reprendre (pause/erreur) |
| `POST` | `/api/instances/{id}/reprendre-tache` | Reprendre une tâche spécifique |
| `POST` | `/api/instances/{id}/pauser` | Mettre en pause |
| `POST` | `/api/instances/{id}/annuler` | Annuler |
| `PATCH` | `/api/instances/{id}/taches` | Corriger des données d'entrée en lot |
| `POST` | `/api/instances/{id}/hook/{nomTache}` | Callback externe (AllowAnonymous) |

### Mocks

| Méthode | Route | Description |
|---------|-------|-------------|
| `POST` | `/api/mocks` | Ajouter un mock |
| `POST` | `/api/mocks/batch` | Ajouter des mocks en lot |

### Authentification

| Méthode | Route | Description |
|---------|-------|-------------|
| `GET` | `/api/auth/token` | Token JWT via NTLM (production) |
| `GET` | `/api/auth/dev-token` | Token JWT sans NTLM (développement uniquement) |

---

## 10. Frontend Vue.js

### Pages

| Route | Page | Description |
|-------|------|-------------|
| `/` | `TableauDeBord.vue` | Statistiques et activité récente |
| `/definitions` | `Definitions.vue` | Liste des définitions (DataTable) |
| `/definitions/:id` | `DefinitionDetail.vue` | YAML, versions, démarrage |
| `/instances` | `Instances.vue` | Liste des instances (DataTable + filtre état) |
| `/instances/:id` | `InstanceDetail.vue` | Tâches, données entrée/sortie, correction |

### Stores Pinia

**`auth.js`** — Gestion du token JWT
- Stocke le token et le nom d'utilisateur dans `localStorage`
- En développement : appelle `/api/auth/dev-token`
- En production : appelle `/api/auth/token` via NTLM

**`workflow.js`** — Données métier
- Définitions, instances, instances en erreur
- Toutes les actions API (charger, démarrer, reprendre, corriger...)
- Helper `apiFetch` avec injection automatique du header `Authorization`

### Tableaux dynamiques (DataTables)

Les pages Définitions et Instances utilisent **DataTables 2.x** avec le style UTD :

- `utd.datatables.definirParametresDefaut()` appelé avant chaque initialisation
- Rendu conditionnel par type (`display` vs `filter`/`sort`) pour la recherche
- Navigation SPA via event delegation + `router.push()`
- DataTables CSS/JS chargés **avant** UTD (requis par la doc UTD)

### Composants

**`EtatBadge.vue`** — Pastille colorée pour les états
- Accepte une valeur numérique ou string
- Styles CSS globaux (non scopés) pour compatibilité DataTables

### Design system

L'interface utilise les **composants web UTD** (Gouvernement du Québec) :
- `utd-piv-entete` / `utd-piv-pied-page` — En-tête et pied de page PIV
- `utd-menu-horizontal` / `utd-menu-horizontal-item` — Menu de navigation
- `utd-section` — Sections de contenu
- `utd-champ-form` — Champs de formulaire
- `utd-btn` — Boutons
- Classes tableau : `utd-table`, `gris`, `rayee`, `bordureslignes`, etc.

---

## 11. Temps réel — SignalR

### Hub

`/hubs/workflow` — JWT requis (passé en `?access_token=` pour WebSocket)

**Groupes** :
- `instance-{instanceId}` — Mises à jour d'une instance spécifique
- `correlation-{correlationId}` — Traçabilité bout-en-bout
- `All` — Événements globaux (tableau de bord)

### Événements serveur → client

| Événement | Portée | Données |
|-----------|--------|---------|
| `ChangementEtat` | instance + correlation | `{ instanceId, etat, correlationId, timestamp }` |
| `ChangementEtatGlobal` | All | idem |
| `TacheDemarree` | instance + correlation | `{ instanceId, tacheId, nomTache, correlationId }` |
| `TacheTerminee` | instance + correlation | `{ ..., donneesSortie }` |
| `TacheEnErreur` | instance + correlation | `{ ..., erreur }` |
| `TacheEnErreurGlobal` | All | idem |
| `WorkflowTermine` | instance + correlation | `{ instanceId, correlationId }` |
| `WorkflowTermineGlobal` | All | idem |

### CorrelationId

Chaque instance a un `CorrelationId` unique propagé :
- Dans les logs backend (`[CorrelationId={id}]`)
- Dans le header HTTP sortant `X-Correlation-Id`
- Dans tous les événements SignalR
- Permet le filtrage dans les systèmes externes (logs, APM, etc.)

---

## 12. Authentification

### Flux de production

```
Navigateur (NTLM) → GET /api/auth/token → JWT retourné
→ Stocké dans localStorage
→ Envoyé en header "Authorization: Bearer {token}" sur chaque requête
→ Passé en "?access_token={token}" pour SignalR WebSocket
```

### Flux de développement

```
GET /api/auth/dev-token → JWT retourné (utilise Environment.UserName)
```

Le frontend détecte automatiquement l'environnement via `import.meta.env.DEV`.

### Configuration JWT

Dans `appsettings.json` :
```json
"Jwt": {
  "Secret": "CHANGER-EN-PRODUCTION",
  "Issuer": "OAM",
  "Audience": "OAM-Frontend",
  "ExpirationMinutes": 480
}
```

---

## 13. Définitions YAML

### Structure

```yaml
# yaml-language-server: $schema=workflow-schema.json
nom: NomDuWorkflow
description: Description optionnelle
equipe: Equipe-A

variables:
  monId: "{{ input.monId }}"

taches:
  - id: premiereTache
    nom: Nom lisible
    type: http
    httpClientId: monApi
    mock: monApiMock
    parametres:
      id: "{{ monId }}"
    output:
      resultat: "$.data.resultat"

  - id: branchement
    type: condition
    parametres:
      expression: "{{ resultat }}"
    branches:
      - condition: "{{ resultat }}"
        aller: tacheSucces
      - condition: "true"
        aller: tacheEchec

  - id: tacheSucces
    type: http
    httpClientId: autreApi
    parametres:
      data: "{{ resultat }}"
    suivant: fin

  - id: tacheEchec
    type: hook
    hook: true

  - id: fin
    type: condition
    parametres:
      expression: "true"
```

### Types de tâches

| Type | Description | Paramètres requis |
|------|-------------|-------------------|
| `http` | Appel HTTP configuré via YAML | `httpClientId` |
| `condition` | Évalue une expression pour le branchement | `expression` |
| `mock` | Réponse fictive du catalogue | *(hérité de `mock:` sur la tâche)* |
| `hook` | Pause et attend un callback | `hook: true` |

### Validation IDE

Ajouter `# yaml-language-server: $schema=workflow-schema.json` en première ligne pour activer l'autocomplétion et la validation dans VS Code.

---

## 14. Système de mocks

Les mocks permettent de tester les workflows sans appels HTTP réels.

### Définition dans le YAML

```yaml
- id: maTache
  type: http
  httpClientId: monApi
  mock: monMockId        # Si le mock est disponible, il prend priorité sur le HTTP
  parametres:
    id: "{{ monId }}"
```

### Auto-seed en développement

Au démarrage en mode Development, OAM charge automatiquement `seed-mocks.json` :

```json
[
  {
    "id": "monMockId",
    "condition": null,
    "reponseJson": "{\"data\":{\"succes\":true,\"id\":\"123\"}}"
  }
]
```

### Ajout via API (runtime)

```bash
# Un seul mock
POST /api/mocks
{ "id": "monMockId", "condition": null, "reponseJson": "{...}" }

# Plusieurs mocks
POST /api/mocks/batch
[{ "id": "mock1", ... }, { "id": "mock2", ... }]
```

> **Note** : Les mocks sont en mémoire et perdus au redémarrage. En développement, ils sont rechargés automatiquement depuis `seed-mocks.json`.

---

## 15. Tests

```bash
cd tests/OAM.Tests
dotnet test
```

### Couverture

| Classe testée | Tests |
|---------------|-------|
| `YamlParser` | Parsing définition, parsing tâches, calcul hash |
| `HandlebarsResolver` | Résolution simple, imbriquée, batch, JSONPath |
| `RegistreConnecteurs` | Enregistrement, résolution, types disponibles |
| `GestionnaireMock` | Ajout, résolution conditionnelle, valeur par défaut |

### MDAT (Markdown-Driven Tests)

Le projet supporte les tests pilotés par Markdown via la librairie **MDAT** (`mtessdev/mdat`). Les fichiers `tests/**/*.md` sont inclus dans le build de test.

---

## 16. CI/CD Azure DevOps

Fichier : `pipelines/azure-pipelines.yml`

### Déclencheurs

- Branches : `main`, `develop`, `release/*`

### Étapes

```
Build
  ├─ dotnet restore + build + test
  ├─ npm ci + npm run build (Vue.js → wwwroot)
  └─ dotnet publish → artifact

Deploy_SAT    (branche develop)
Deploy_ACCP   (après SAT)
Deploy_IT     (après ACCP)
Deploy_PROD   (branche release/* + après IT)
```

### Déploiement IIS

Chaque environnement utilise `IISWebAppDeploymentOnMachineGroup` vers les serveurs respectifs (`SAT-OAM`, `ACCP-OAM`, `IT-OAM`, `PROD-OAM`).

---

## 17. Configuration

### appsettings.json (API)

```json
{
  "ConnectionStrings": {
    "OamDb": "Server=...;Database=OAM;Trusted_Connection=True;"
  },
  "Jwt": {
    "Secret": "CLE-SECRETE-MINIMUN-32-CARACTERES",
    "Issuer": "OAM",
    "Audience": "OAM-Frontend",
    "ExpirationMinutes": 480
  },
  "Cors": {
    "Origins": ["https://oam.mondomaine.com"]
  }
}
```

### appsettings.Development.json

```json
{
  "ConnectionStrings": {
    "OamDb": "Data Source=oam_dev.db"
  }
}
```

### Variables d'environnement importantes

| Variable | Description |
|----------|-------------|
| `ASPNETCORE_ENVIRONMENT` | `Development` ou `Production` |

---

## 18. Déploiement IIS

### Prérequis serveur

1. IIS avec le module **ASP.NET Core Hosting Bundle** (.NET 10)
2. Fonctionnalité Windows **WebSocket Protocol** activée
3. Authentication Windows activée dans IIS

### web.config (production)

Le `web.config` utilise les variables `%LAUNCHER_PATH%` et `%LAUNCHER_ARGS%` remplacées par Visual Studio en dev et par `dotnet publish` en production.

### Build de production

```bash
# Build du frontend (inclus dans wwwroot)
cd src/OAM.Frontend
npm run build

# Publication de l'API (inclut wwwroot)
cd ../OAM.Api
dotnet publish -c Release -o ./publish
```

Le dossier `publish/` contient le tout — API + frontend — déployable directement sur IIS.

---

## Licence

Usage interne — Gouvernement du Québec.
