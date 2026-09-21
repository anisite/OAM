# OIM vs Elsa Workflow et Kogito
### Pourquoi un outil maison — version pour la direction

> Document d'aide à la décision. Pourquoi avoir développé OIM plutôt que d'adopter une solution d'orchestration existante comme **Elsa Workflow** ou **Kogito**.

---

## 1. L'essentiel en une phrase

OIM n'est pas « meilleur dans l'absolu » que les solutions du marché — il est **mieux adapté à notre réalité**. Il s'appuie sur les systèmes et les outils déjà en place chez nous, sans nous obliger à acquérir et à faire approuver une nouvelle technologie, tout en nous laissant la pleine maîtrise de l'outil.

---

## 2. De quoi parle-t-on?

OIM, Elsa et Kogito servent tous à **automatiser des processus d'affaires** qui touchent plusieurs systèmes informatiques. La différence se joue sur **l'adéquation à notre environnement**, pas sur la fonction de base.

| | **OIM (notre projet)** | **Elsa Workflow** | **Kogito** |
|---|---|---|---|
| Origine | Développé par notre équipe | Outil externe | Outil externe (Red Hat) |
| Technologie | Celle que nos équipes maîtrisent déjà | À intégrer | Différente de la nôtre |
| Pour modifier un processus | L'équipe responsable elle-même | Développeurs spécialisés | Développeurs spécialisés |
| Maîtrise de l'outil | Totale | Partielle | Dépend du fournisseur |

---

## 3. Les avantages d'OIM pour notre organisation

### 3.1 Il fonctionne sur ce que nous avons déjà

OIM utilise les serveurs, la base de données, le système de connexion des employés et l'identité visuelle du gouvernement du Québec **déjà en place**, et il est bâti avec les technologies que nos équipes maîtrisent au quotidien.

> Adopter Kogito ne poserait pas de problème d'hébergement — nous disposons déjà de l'infrastructure moderne nécessaire. Le véritable écart est ailleurs : Kogito repose sur une **technologie différente de la nôtre**, qu'il faudrait apprendre, sécuriser et maintenir en parallèle de nos outils actuels. OIM, lui, s'inscrit directement dans ce que nous savons déjà faire.

### 3.2 Il est pensé pour être partagé entre les équipes

Une seule plateforme sert plusieurs équipes. Quand une équipe crée une connexion vers un système, celle-ci devient disponible pour toutes les autres. Chaque équipe voit ses propres processus, mais tout le monde profite du travail commun.

> Les solutions externes sont des outils génériques : tout ce partage et cette organisation seraient à construire nous-mêmes par-dessus.

### 3.3 Il est simple à prendre en main

Un processus se décrit dans un **fichier de configuration**, pas dans du code complexe. L'équipe responsable peut le modifier et le mettre à jour elle-même, sans dépendre des délais informatiques à chaque changement.

> Les autres solutions exigent des compétences techniques spécialisées pour la moindre modification.

### 3.4 Tout est tracé et vérifiable

Chaque version d'un processus est **horodatée et attribuée à son auteur**. On sait en tout temps quelle version tourne, qui l'a déployée, et où en est chaque traitement en cours.

> Pour une organisation soumise à des exigences de reddition de comptes, cette traçabilité est intégrée d'office.

### 3.5 Nous en sommes pleinement propriétaires

Aucune licence à payer, aucune dépendance à un fournisseur externe. Nous contrôlons les priorités, la sécurité et l'évolution de l'outil.

> Un argument de souveraineté important dans le secteur public.

### 3.6 Le suivi et la correction d'erreurs sont intégrés

Les équipes voient l'avancement des traitements **en temps réel**. Si une étape échoue, le processus se met en pause au lieu de tout faire planter : on corrige précisément ce qui ne va pas, puis on reprend sans recommencer depuis le début. Certaines étapes peuvent aussi attendre une **validation humaine** avant de continuer.

---

## 4. Ce qu'il faut reconnaître honnêtement

Pour décider en toute connaissance de cause, voici où les solutions externes gardent un avantage — et comment nous y répondons.

| Enjeu | Avantage des solutions externes | Notre réponse |
|---|---|---|
| **Maturité** | Produits éprouvés à grande échelle, avec support. | Documentation, tests et une équipe d'entretien clairement identifiée. |
| **Standards reconnus** | Reposent sur des standards transférables. | Format volontairement simple, lisible et documenté. |
| **Éditeur visuel** | Permettent de dessiner les processus graphiquement. | Visualisation déjà disponible ; éditeur à enrichir au besoin. |
| **Fonctions très avancées** | Couvrent des cas complexes dès le départ. | Ajoutées au fur et à mesure des besoins réels. |
| **Très grands volumes** | Conçues pour de très fortes charges. | Adapté à nos volumes actuels ; évolutif si nécessaire. |

> Le principal risque d'un outil maison est la **dépendance à quelques personnes**. Nous l'atténuons par la documentation, les tests automatisés et une équipe responsable identifiée.

---

## 5. Une perspective : remplacer CAC Asynchrone

À terme, OIM pourrait **remplacer CAC Asynchrone**. Les traitements qu'il gère aujourd'hui sont précisément le genre de processus pour lesquels OIM a été conçu : enchaîner plusieurs systèmes en arrière-plan, suivre l'avancement et reprendre sans tout recommencer en cas de problème.

Regrouper ces traitements dans OIM permettrait :

- d'avoir **un outil de moins à entretenir** ;
- de **voir tous les traitements au même endroit**, dans une interface de suivi commune ;
- de **modifier les processus sans livraison informatique** à chaque changement ;
- d'appliquer la **même traçabilité** à l'ensemble des traitements.

> Avant de s'engager, il faudra examiner ce que fait aujourd'hui CAC Asynchrone (volumes, délais, dépendances) pour confirmer la faisabilité d'une migration progressive.

---

## 6. Conclusion

> OIM n'est pas « meilleur dans l'absolu » que les solutions du marché — il est **mieux adapté**. Il s'appuie sur ce que nous avons déjà, sans nouvelle technologie à faire approuver, tout en offrant traçabilité, partage entre équipes et pleine propriété de l'outil. Son coût d'adoption est faible et ses bénéfices grandissent à mesure que les équipes l'utilisent.
>
> En échange, nous devons sécuriser sa pérennité par de la documentation, des tests et une équipe d'entretien clairement identifiée — un engagement que nous assumons.
