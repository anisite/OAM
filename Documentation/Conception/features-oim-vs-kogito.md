# OIM vs Kogito — Tableau des fonctionnalités

> Comparaison fonctionnalité par fonctionnalité entre **OIM** (Orchestrateur d'Intégrations Métier) et **Kogito** (Red Hat).
>
> **Légende** : ✅ implémenté · 🟡 partiel / à construire · ❌ absent · ⚠️ déclaré mais non implémenté
>
> ⚠️ **Note de fiabilité** : la colonne **OIM** est établie à partir du code source vérifié (juillet 2026). La colonne **Kogito** repose sur la connaissance générale du produit et doit être validée contre la version ciblée avant toute diffusion officielle.

---

## 1. Fonctionnalités présentes dans OIM

| # | Fonctionnalité | OIM | Kogito |
|---|----------------|-----|--------|
| 1 | **Définition des processus en fichier de configuration** (éditable sans code) | ✅ YAML déclaratif | 🟡 BPMN 2.0 (XML) principalement ; YAML possible via Serverless Workflow |
| 2 | **Résolution de variables dans les paramètres** | ✅ Handlebars `{{ }}` | ✅ expressions (jq / EL) |
| 3 | **Extraction de valeurs des réponses** | ✅ JSONPath (`$.data...`) | ✅ jq / mapping de données |
| 4 | **Branchements conditionnels** | ✅ `branches` + condition | ✅ passerelles (gateways) |
| 5 | **Navigation explicite entre étapes** (`suivant`) | ✅ | ✅ flux de séquence |
| 6 | **Connecteur HTTP/REST déclaratif** | ✅ via `YamlHttpClient` | ✅ fonctions REST / OpenAPI |
| 7 | **Étape conditionnelle dédiée** | ✅ connecteur `condition` | ✅ |
| 8 | **Mocks intégrés pour les tests** (catalogue de réponses) | ✅ connecteur `mock` + auto-seed | 🟡 outillage de test, pas de catalogue de mocks natif |
| 9 | **Approbation humaine / mise en pause** (hook) | ✅ connecteur `hook` | ✅ tâches utilisateur (mature) |
| 10 | **Reprise sur erreur avec correction des données** | ✅ corriger l'entrée puis reprendre la tâche | 🟡 gestion d'erreurs/compensation oui, mais pas cette UX de correction-reprise |
| 11 | **Pause / annulation d'une instance** | ✅ | ✅ gestion des instances |
| 12 | **Réponse synchrone au démarrage** (attente corrélée, timeout 30s) | ✅ connecteur `reponse` + `AttenteReponse` | 🟡 plutôt orienté asynchrone / événementiel |
| 13 | **Versionnage par empreinte (hash SHA256) + historique** | ✅ avec auteur et horodatage | 🟡 versionnage de processus, UX différente |
| 14 | **Traçabilité bout-en-bout par CorrelationId** | ✅ logs + header HTTP + événements | ✅ clés de corrélation + OpenTelemetry |
| 15 | **Suivi en temps réel dans l'interface** | ✅ SignalR / WebSocket | 🟡 événements (Kafka/CloudEvents), pas de push UI natif équivalent |
| 16 | **Cloisonnement par équipe** | ✅ champ `Equipe` + filtrage | ❌ pas de multi-tenant natif de premier plan |
| 17 | **Exécution asynchrone en arrière-plan** (file + service) | ✅ `WorkflowQueue` + background service | ✅ service de jobs |
| 18 | **Reprise après crash serveur** (claim atomique + heartbeat) | ✅ `OrphelinRecuperateur` (seuil 2 min) | ✅ persistance + jobs service |
| 19 | **Authentification Windows** (NTLM → JWT) | ✅ natif | ❌ orienté Keycloak / OIDC, NTLM non natif |
| 20 | **Interface au standard UTD (gouv. du Québec)** | ✅ composants UTD PIV | ❌ à développer sur mesure |
| 21 | **Persistance SQL Server / SQLite** (EF Core) | ✅ SQL Server (prod) / SQLite (dev) | 🟡 PostgreSQL / MongoDB / Infinispan ; SQL Server non prioritaire, SQLite non |
| 22 | **Tests pilotés par cas** (MDAT / Markdown) | ✅ entité `CasTests` + MDAT | 🟡 tests JUnit / outillage, pas piloté Markdown |
| 23 | **Validation dans l'éditeur** (autocomplétion) | ✅ schéma JSON pour le YAML | ✅ éditeurs BPMN/DMN, extensions VS Code |
| 24 | **Compteur de tentatives** | 🟡 compteur incrémenté à la reprise **manuelle** | ✅ politiques de retry automatiques |

---

## 2. Fonctionnalités déclarées dans OIM mais non implémentées

| # | Fonctionnalité | OIM | Kogito |
|---|----------------|-----|--------|
| 25 | **Connecteur Pub/Sub (messagerie)** | ⚠️ présent dans l'énum, aucun connecteur | ✅ événementiel natif (Kafka/CloudEvents) |
| 26 | **Connecteur MDAT** (système métier) | ⚠️ présent dans l'énum, aucun connecteur | ❌ non applicable |
| 27 | **Boucles** | ⚠️ présent dans l'énum, aucun connecteur | ✅ sous-processus multi-instances |

---

## 3. Fonctionnalités où Kogito devance OIM

| # | Fonctionnalité | OIM | Kogito |
|---|----------------|-----|--------|
| 28 | **Exécution parallèle (fork / join)** | ❌ exécution séquentielle | ✅ passerelles parallèles |
| 29 | **Minuteries / délais programmés** | ❌ | ✅ événements timer |
| 30 | **Saga / compensation** (annulation transactionnelle) | ❌ | ✅ natif |
| 31 | **Retry automatique avec back-off** | ❌ (reprise manuelle uniquement) | ✅ |
| 32 | **Éditeur visuel de processus** | ❌ (YAML uniquement) | ✅ éditeur graphique |
| 33 | **Moteur de règles de décision (DMN)** | ❌ | ✅ DMN natif |
| 34 | **Standards ouverts** (BPMN 2.0 / DMN) | ❌ DSL propriétaire | ✅ standards OMG |
| 35 | **Scalabilité horizontale cloud-native** | 🟡 amorcée (claim atomique ; attente de réponse en mémoire) | ✅ conçu pour Kubernetes |

---

## 4. Lecture d'ensemble

- **OIM gagne** sur l'ajustement à l'environnement gouvernemental : auth Windows, interface UTD, persistance SQL Server, réponse synchrone, correction-reprise, cloisonnement par équipe, mocks intégrés — autant de besoins concrets couverts nativement.
- **Kogito gagne** sur la richesse d'un moteur mature : parallélisme, minuteries, saga, retry automatique, règles de décision, éditeur visuel et standards ouverts.
- **À surveiller côté OIM** : les types `PubSub`, `Mdat` et `Boucle` sont déclarés mais pas encore implémentés ; le retry est manuel ; la scalabilité multi-serveur est amorcée mais l'attente de réponse reste en mémoire (bascule prévue vers un backend partagé).
