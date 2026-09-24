# OIM — notes de travail

Orchestrateur de processus YAML sur **DurableTask.Core + SQL Server** (`Microsoft.DurableTask.SqlServer`),
API ASP.NET Core 10, interface **Vue 3 + utd-webcomponents** (tableau de bord, suivi, concepteur Vue Flow).
Le README est la référence fonctionnelle (syntaxe YAML, API, tests métier, configuration) : ne pas la dupliquer ici.

Ancienne version (moteur maison) : `../OAM/OAM` — référence métier seulement.

## Lancer

```powershell
sqllocaldb start OIM                      # instance dédiée : (localdb)\MSSQLLocalDB refuse les connexions (logon trigger)
cd sources/OIM.Api; dotnet run            # http://localhost:5080, déploie ../../definitions au démarrage
cd sources/OIM.Frontend; npm run dev      # http://localhost:5173 (proxy /api)
dotnet test OIM.slnx                      # 89 tests; intégration sur (localdb)\OIM, base OIM_Tests
```

- Arrêter l'API (`taskkill /F /IM OIM.Api.exe`) avant `dotnet build` : elle verrouille `OIM.Moteur.dll`.
- `dotnet test` ne reconstruit pas `OIM.Api` : après un changement du moteur, `dotnet build OIM.slnx` avant de
  relancer l'API, sinon elle tourne avec l'ancien `OIM.Moteur.dll`.
- Frontend : `npx vue-tsc --build --force` puis `npm run build-only`.
- Vérifier l'UI : `playwright-core` + Edge installé (`chromium.launch({ channel: 'msedge' })`); Edge headless
  en ligne de commande ne produit rien (politique d'entreprise).

## Structure

- `sources/OIM.Moteur` : `Definitions` (lecture/validation YAML, paquet), `Expressions` ({{ }}),
  `Orchestration` (interpréteur + activités), `Stockage` (schéma `oim`), `Pilotage` (services API, requêtes
  sur `dt.vInstances`/`dt.vHistory`), `Hebergement` (DI, worker), `Tests` (tests métier).
- `sources/OIM.Api` : minimal APIs (`EndpointsDefinitions`, `EndpointsInstances`).
- `sources/OIM.Frontend` : `lib/modele.ts` (YAML ↔ modèle), `lib/graphe.ts` (liens dérivés du modèle + dagre),
  `lib/catalogue.ts` (types d'étapes côté UI — garder synchronisé avec `CatalogueEtapes.cs`).
- `definitions/<equipe>/<processus>/` : paquet déployable (processus.yml, gabarits, `tests/*.yml`).
- `sources/OIM.Api/Securite/` : fournisseurs de jetons (un schéma JwtBearer par émetteur, choisi par `iss`),
  émission du jeton interne (`/api/auth/jeton`, Windows), calcul des `Habilitations` par requête.
- `base-de-donnees/` : script de création pour un DBA, généré par `Generer-ScriptBase.ps1` (à relancer si le
  schéma `oim` ou le paquet DurableTask change).

## Règles importantes

- **Déterminisme DurableTask.** Les instances en cours sont rejouées avec le code courant. Tout changement qui
  ajoute ou retire une activité, une minuterie ou un sous-processus doit être conditionné à
  `EntreeOrchestration.VersionMoteur` (incrémenter `VersionMoteurCourante`, documenter dans `Contrats.cs`).
  Changer seulement les *données* d'une activité (entrée/résultat inutilisé) est sans risque.
- Pas d'horloge ni d'accès externe dans l'orchestration : `context.CurrentUtcDateTime`, expressions pures,
  tout effet de bord dans une activité.
- **Frontière JSON** (`Orchestration/Json.cs`) : DurableTask sérialise en Newtonsoft. Entrées d'orchestration et
  événements en `JRaw`; paramètres/résultats d'activité voyagent comme *chaîne* JSON. Le moteur utilise
  System.Text.Json. Lire les nombres avec `Expression.Double`/`Nombre` (`GetValue<double>` échoue sur un int).
- **Connexions SQL partagées avec DurableTask** (même chaîne, même pool) : jamais de transaction `Serializable`
  (DurableTask exige READ COMMITTED pour `READPAST`); sérialiser avec `sp_getapplock` (voir `DepotDefinitionsSql`).
- `TaskHubParApplication=true` : task hub = nom d'application (sinon l'utilisateur SQL), requis pour IIS multi-nœuds.
- Brouillons de tests : id `~equipe.x~…`, instances `equipe.~test-…`, exclus du suivi (`Name NOT LIKE '~%'`) et purgés.
- Activités « au moins une fois » : clé `Idempotency-Key` / `X-OIM-Cle` transmise aux services.

- **Équipes et habilitations.** Toute méthode publique des services de pilotage reçoit `Habilitations`
  (`Habilitations.Systeme` pour le moteur) et fait son contrôle d'abord; les endpoints n'ont aucune logique de
  sécurité (paramètre `Habilitations` injecté). Ids qualifiés `equipe.x` (processus et instances, voir
  `IdsEquipe`); le filtrage des instances se fait sur le préfixe d'`InstanceID`. Hors de portée → 404.
  Seuls les *sujets* du jeton comptent : ne jamais lire un claim propre à un émetteur ailleurs que dans
  `Fournisseurs.cs`.
- Pas de migration du schéma `oim` : une base antérieure aux équipes est refusée au démarrage (à recréer).

## Conventions

- Code, identifiants et messages en **français**; commentaires sobres, au niveau du code existant.
- UTD : balises `utd-*` en custom elements; dialogues via `setAttribute('afficher')` (`components/Dialogue.vue`);
  confirmations/notifications via `helpers/messages.ts`; `utd-onglets` accepté (onglets toujours rendus).
  Icônes d'étapes en caractères Unicode (la police de symboles UTD est un sous-ensemble).
- Scripts d'édition : les heredocs bash contenant du texte français peuvent casser (apostrophes); écrire le
  script Python dans le scratchpad puis l'exécuter.

## Pistes non faites

- ECS25A : `definitions/ecs/frw-3003` seulement (tests sur mocks). Services de `TypesService` à exposer et brancher
  (`Oim:Services`), URL des gabarits http à confirmer; PJ du courriel de confirmation et événement d'affaire CAC non repris.
  Autres formulaires (TRDOC, 6478, 6505, 3002, SR2604, SR2609, 6666, `*.confirmation`) à convertir.
- Lire le format Markdown des cas de test d'OAM (`workflows/tests.*.md`).
- Limiter le contexte tracé par `oim.reponse` aux seules valeurs utilisées (données personnelles, volume).
- Base SQL pour les tests d'intégration en CI (sinon « non concluants »).
- Fournisseur `Externe` par autorité OIDC (Entra ID) : configuré et prévu, validé seulement avec une clé HS256.
