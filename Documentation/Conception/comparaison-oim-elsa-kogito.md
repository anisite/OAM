# OIM vs Elsa Workflow et Kogito
### Pourquoi un orchestrateur maison pour une organisation gouvernementale

> Document d'aide à la décision — comparaison d'OIM (Orchestrateur d'Actions Métier) avec deux solutions d'orchestration de workflows établies : **Elsa Workflow** et **Kogito** (Red Hat).

---

## 1. En une phrase

OIM n'est pas « meilleur dans l'absolu » qu'Elsa ou Kogito — il est **mieux ajusté à notre contexte**. Il épouse l'infrastructure Windows / IIS / SQL Server / NTLM et le design UTD déjà en place, sans imposer de nouvelle pile technologique à acquérir et à homologuer, tout en offrant traçabilité, mutualisation inter-équipes et pleine maîtrise du code.

---

## 2. Ce que comparent réellement ces trois outils

| | **OIM (notre projet)** | **Elsa Workflow** | **Kogito (Red Hat)** |
|---|---|---|---|
| Nature | Orchestrateur maison, ciblé | Librairie / moteur .NET générique | Plateforme d'automatisation cloud-native |
| Pile technique | .NET 10 / ASP.NET Core | .NET | Java / Quarkus |
| Définition des processus | YAML simple + Handlebars | C#, designer visuel ou JSON | BPMN 2.0 / DMN (standards OMG) |
| Infrastructure visée | Serveurs Windows / IIS existants | À intégrer | Conteneurs / Kubernetes (que nous avons) |
| Cible | Coordination inter-systèmes interne | Workflows applicatifs intégrés | Microservices décisionnels / processus |

---

## 3. Là où OIM est réellement supérieur pour notre contexte

### 3.1 Alignement total avec l'infrastructure gouvernementale existante

C'est l'argument le plus fort. OIM utilise **ce qui est déjà en place** au ministère :

- Authentification **NTLM → JWT** (l'authentification Windows déjà déployée)
- Déploiement **IIS** sur les serveurs Windows existants
- Base de données **SQL Server** (et SQLite en développement)
- Le **design system UTD du gouvernement du Québec** intégré nativement dans l'interface

> **Comparaison** : Elsa exigerait un travail d'intégration de l'authentification et de l'interface. Nous disposons bien de **Kubernetes**, donc l'aspect conteneurs de Kogito n'est pas un frein en soi — mais Kogito repose sur une pile **Java / Quarkus**, distincte de notre expertise et de notre chaîne d'outils .NET / Windows / IIS / SQL Server / NTLM. Adopter Kogito signifierait maintenir deux écosystèmes en parallèle (compétences, sécurité, déploiement, intégration à l'auth Windows et au design UTD), là où OIM s'inscrit directement dans l'environnement existant.

### 3.2 Conçu pour la mutualisation inter-équipes dès le départ

OIM est pensé comme une **plateforme gouvernementale partagée** :

- Cloisonnement par équipe (champ `Equipe`)
- Partage des connecteurs vers les systèmes : une connexion développée par une équipe profite à toutes les autres
- Comportements standards (gestion d'erreurs, journalisation, suivi) inclus automatiquement

> **Comparaison** : Elsa et Kogito sont des moteurs génériques. La gouvernance multi-équipes (qui voit quoi, qui déploie quoi) est à concevoir et à construire par-dessus.

### 3.3 Surface d'apprentissage minimale

Modifier un processus = éditer un **fichier YAML** avec validation et autocomplétion dans l'éditeur (`workflow-schema.json`).

> **Comparaison** : Pas besoin de compétence C# (Elsa), ni de maîtrise de BPMN/DMN combinée à Quarkus (Kogito). Une équipe métier peut s'approprier OIM rapidement.

### 3.4 Traçabilité et auditabilité « gouvernementales »

- Versionnage de chaque processus par **hash SHA256**
- Historique complet de chaque déploiement, horodaté et attribué à un auteur (`VersionsDefinitionWorkflow`, `DeployePar`)
- **`CorrelationId`** propagé bout-en-bout : logs backend, header HTTP sortant (`X-Correlation-Id`), événements temps réel

> **Comparaison** : Pour un organisme soumis à des exigences de reddition de comptes, cette traçabilité est native et lisible, sans outillage supplémentaire.

### 3.5 Maîtrise totale du code (souveraineté)

- Aucune dépendance à un éditeur ni à une licence commerciale
- Contrôle complet de la feuille de route, de la sécurité et des correctifs

> **Comparaison** : Elsa propose une offre commerciale payante ; Kogito dépend de l'écosystème Red Hat. OIM appartient entièrement à l'organisation — un argument fort en secteur public.

### 3.6 Suivi temps réel et reprise sur erreur intégrés

- **SignalR** pour le suivi en direct de chaque instance
- Pause / reprise sur erreur avec **correction ciblée des données** puis redémarrage à la tâche fautive
- **Hooks d'approbation humaine** (mise en pause en attente d'une validation externe)

> **Comparaison** : Fonctionnel sans assemblage de briques externes.

---

## 4. Les contreparties à reconnaître honnêtement

Pour que la comparaison soit crédible, il faut nommer là où Elsa et Kogito gardent l'avantage :

| Enjeu | Situation | Mesure d'atténuation |
|---|---|---|
| **Maturité & communauté** | Elsa et Kogito sont des produits établis, éprouvés à large échelle, documentés, avec support. OIM est un outil maison. | Documentation, tests automatisés, équipe d'entretien identifiée pour réduire le « bus factor ». |
| **Standards ouverts** | Kogito parle **BPMN / DMN**, standards reconnus et transférables. OIM a un DSL propriétaire. | Schéma YAML documenté ; DSL volontairement simple et lisible. |
| **Designer visuel** | Elsa et Kogito offrent un éditeur graphique. OIM reste en YAML. | Validation IDE ; visualisation par graphe de séquence déjà disponible. |
| **Fonctions avancées** | Timers, sous-processus, parallélisme complexe, compensation / saga matures chez les concurrents. | À développer dans OIM au fil des besoins réels. |
| **Scalabilité** | La file d'attente et les mocks d'OIM sont **en mémoire** ; Kogito est conçu pour le scaling horizontal cloud-native. | Adéquat aux volumes actuels ; évolution prévue si la charge le justifie. |

---

## 5. Perspective d'avenir : remplacer CAC Asynchrone

À moyen terme, OIM est un **candidat naturel pour remplacer CAC Asynchrone**. Les traitements aujourd'hui pris en charge par CAC Asynchrone correspondent exactement à ce qu'OIM sait faire : enchaîner des appels à plusieurs systèmes de façon non bloquante, suivre l'état de chaque traitement et reprendre proprement sur erreur.

Les bénéfices d'une telle consolidation :

- **Une plateforme de moins à maintenir** : on retire un composant dédié au profit d'un orchestrateur partagé.
- **Visibilité unifiée** : les traitements asynchrones deviennent visibles dans la même interface de suivi temps réel que les autres processus.
- **Évolution sans livraison** : les enchaînements seraient décrits en configuration plutôt que codés, donc modifiables par l'équipe responsable.
- **Traçabilité homogène** : versionnage, historique et `CorrelationId` bout-en-bout pour tous les traitements, asynchrones compris.

> **À valider** : un inventaire des cas portés par CAC Asynchrone (volumétrie, contraintes de délai, dépendances) est nécessaire avant d'arrêter une stratégie de migration. La file d'attente en mémoire d'OIM (voir §4) devra notamment être évaluée au regard des volumes concernés.

---

## 6. Conclusion pour la direction

> OIM n'est pas « meilleur dans l'absolu » qu'Elsa ou Kogito — il est **mieux ajusté**. Il épouse l'infrastructure Windows / IIS / SQL Server / NTLM et le design UTD déjà en place, sans imposer de nouvelle pile à homologuer, tout en offrant traçabilité, mutualisation inter-équipes et souveraineté du code. Son coût d'adoption est faible et ses bénéfices croissent à mesure que les équipes le partagent.
>
> Le prix à payer est celui d'un outil maison : maturité et pérennité à sécuriser par de la documentation, des tests et une équipe d'entretien clairement identifiée.
