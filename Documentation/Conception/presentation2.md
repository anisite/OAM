# OIM — Orchestrateur d'Actions Métier
### Présentation à la directrice générale principale

---

## 1. C'est quoi OIM?

OIM est un **outil maison** qui automatise les processus d'affaires qui impliquent plusieurs systèmes informatiques.

Pensez à un processus comme le traitement d'un rendez-vous : il faut vérifier la disponibilité dans un système, réserver dans un autre, puis envoyer une confirmation. Aujourd'hui, ce genre de séquence est souvent programmé à l'intérieur d'une application — ce qui veut dire que chaque changement dans le processus demande une livraison informatique, avec les délais et les coûts que ça implique.

Avec OIM, **ce processus est décrit dans un simple fichier de configuration**. L'équipe responsable peut le modifier et le redéployer elle-même, sans toucher au code. L'outil s'occupe d'exécuter les étapes dans le bon ordre, de surveiller ce qui se passe, et de gérer les erreurs.

---

## 2. À quoi ça sert concrètement?

### Le problème qu'on règle

Quand un processus d'affaires touche plusieurs systèmes, il faut aujourd'hui :
- un développeur pour coder et modifier la logique,
- un déploiement informatique à chaque changement,
- souvent, personne ne sait en temps réel où en est un traitement en cours.

### Ce que OIM change

| Situation | Avant OIM | Avec OIM |
|---|---|---|
| Modifier un processus | Développeur + délai de livraison | L'équipe responsable modifie le fichier et redéploie |
| Suivre un traitement en cours | Logs techniques, accès limité | Écran de suivi en direct, accessible à l'équipe |
| Une étape échoue | L'ensemble plante, reprise manuelle complexe | Pause automatique, correction ciblée, reprise sans perte |
| Tester avant de passer en production | Difficile sans accès aux vrais systèmes | Simulation intégrée des systèmes visés |
| Savoir quelle version tourne | Souvent flou | Chaque version est horodatée et attribuée à un auteur |

---

## 3. Comment on développe OIM?

### Notre approche

On développe OIM de façon **itérative et planifiée** : chaque nouvelle fonctionnalité est d'abord définie dans un plan partagé, développée, validée par des tests automatisés, puis livrée via un pipeline de déploiement continu.

L'outil est hébergé sur les infrastructures existantes du ministère (serveurs Windows) et utilise l'authentification Windows déjà en place — pas de nouvelle infrastructure à gérer.

### Ce qui garantit la qualité

- **Chaque version est identifiée de façon unique** : on sait exactement quelle version d'un processus est en train de tourner et qui l'a déployée.
- **Les tests sont automatisés** : avant de livrer, on valide que les processus se comportent comme prévu.
- **L'interface respecte le standard visuel du gouvernement du Québec**, ce qui assure une expérience cohérente pour les utilisateurs.

### Ce qu'on peut ajouter facilement

OIM est conçu pour grandir : de nouveaux types d'actions peuvent être ajoutés à la plateforme sans toucher à ce qui fonctionne déjà.

---

## 4. Stratégie de mutualisation

C'est là que l'investissement prend tout son sens.

### Une seule plateforme pour plusieurs équipes

OIM est pensé dès le départ pour être **partagé entre plusieurs équipes** sans multiplier les installations. Chaque équipe voit ses propres processus dans l'outil, mais elles tournent toutes sur la même plateforme.

### Ce qu'on ne refait pas à chaque fois

Quand une équipe développe une façon de communiquer avec un système (ex. : le système de rendez-vous, le système RH), cette connexion devient disponible pour toutes les autres équipes. De même, les comportements standard (gestion des erreurs, journalisation, suivi) sont inclus automatiquement dans chaque processus — sans que chaque équipe ait à les reprogrammer.

### Les bénéfices directs

- **Moins de coûts** : une seule plateforme à maintenir, pas une par équipe ou par projet
- **Moins de risques** : un seul endroit à sécuriser, à mettre à jour, à surveiller
- **Plus de vitesse** : une équipe profite du travail fait par une autre
- **Meilleure gouvernance** : la direction a une vue d'ensemble sur les processus qui tournent, leur état, leurs auteurs

### Ce qui vient ensuite

- Tableau de bord de direction avec vue consolidée sur tous les processus actifs
- Gestion fine des accès par équipe
- Connexion à davantage de systèmes au fil des besoins

---

## En résumé

> OIM est un **outil de coordination interne** qui automatise les processus d'affaires complexes, donne une visibilité en temps réel sur leur état, et permet aux équipes d'évoluer rapidement sans dépendre des délais informatiques à chaque changement. Conçu pour être partagé entre équipes, il génère des économies croissantes à mesure qu'il est adopté — parce que chaque investissement bénéficie à l'ensemble de l'organisation.
