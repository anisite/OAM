using System.Text.Json;
using Microsoft.Extensions.Logging;
using OAM.Domain.Entities;
using OAM.Domain.Enums;
using OAM.Domain.Interfaces;
using OAM.Workflow.Core.Connecteurs;
using OAM.Workflow.Core.Handlebars;
using OAM.Workflow.Core.Yaml;

namespace OAM.Workflow.Core.Engine;

public class MoteurWorkflow(
    IDefinitionWorkflowRepository definitionRepo,
    IInstanceWorkflowRepository instanceRepo,
    IExecutionTacheRepository tacheRepo,
    INotificateurWorkflow notificateur,
    RegistreConnecteurs connecteurs,
    IMockResolver mockResolver,
    ILogger<MoteurWorkflow> logger) : IMoteurWorkflow
{
    public async Task<InstanceWorkflow> DemarrerAsync(Guid definitionId, string? donneesEntree = null, string? correlationId = null)
    {
        var definition = await definitionRepo.ObtenirParIdAsync(definitionId)
            ?? throw new InvalidOperationException($"Définition {definitionId} introuvable");

        correlationId ??= Guid.NewGuid().ToString("N");

        var instance = new InstanceWorkflow
        {
            DefinitionWorkflowId = definition.Id,
            CorrelationId = correlationId,
            HashVersionConfig = definition.HashVersion,
            Etat = EtatWorkflow.EnCours,
            DonneesEntree = donneesEntree,
            ContexteExecution = donneesEntree ?? "{}",
            DateDebut = DateTime.UtcNow
        };

        await instanceRepo.CreerAsync(instance);
        await notificateur.NotifierChangementEtatAsync(instance.Id, EtatWorkflow.EnCours, correlationId);

        logger.LogInformation("Workflow {Nom} démarré: Instance={InstanceId} [CorrelationId={CorrelationId}]",
            definition.Nom, instance.Id, correlationId);

        var workflowYaml = YamlParser.ParseDefinition(definition.ContenuYaml);
        if (workflowYaml.Taches is null || workflowYaml.Taches.Count == 0)
            throw new InvalidOperationException($"La définition '{definition.Nom}' ne contient aucune tâche. Vérifiez le contenu YAML.");

        await ExecuterTachesAsync(instance, workflowYaml);

        return instance;
    }

    public async Task ReprendreAsync(Guid instanceId)
    {
        var instance = await instanceRepo.ObtenirParIdAsync(instanceId)
            ?? throw new InvalidOperationException($"Instance {instanceId} introuvable");

        if (instance.Etat is not (EtatWorkflow.EnPause or EtatWorkflow.EnErreur))
            throw new InvalidOperationException($"L'instance {instanceId} ne peut pas être reprise (état: {instance.Etat})");

        var definition = await definitionRepo.ObtenirParIdAsync(instance.DefinitionWorkflowId)
            ?? throw new InvalidOperationException("Définition introuvable");

        instance.Etat = EtatWorkflow.EnCours;
        await instanceRepo.MettreAJourAsync(instance);
        await notificateur.NotifierChangementEtatAsync(instance.Id, EtatWorkflow.EnCours, instance.CorrelationId);

        var workflowYaml = YamlParser.ParseDefinition(definition.ContenuYaml);
        await ExecuterTachesAsync(instance, workflowYaml);
    }

    public async Task ReprendreTacheAsync(Guid instanceId, string nomTache, string? donneesEntreeCorrigees = null)
    {
        var instance = await instanceRepo.ObtenirParIdAsync(instanceId)
            ?? throw new InvalidOperationException($"Instance {instanceId} introuvable");

        var tache = instance.Taches.FirstOrDefault(t => t.NomTache == nomTache)
            ?? throw new InvalidOperationException($"Tâche '{nomTache}' introuvable dans l'instance {instanceId}");

        if (donneesEntreeCorrigees is not null)
            tache.DonneesEntree = donneesEntreeCorrigees;

        tache.Etat = EtatTache.EnAttente;
        tache.Erreur = null;
        tache.NombreTentatives++;
        await tacheRepo.MettreAJourAsync(tache);

        await ReprendreAsync(instanceId);
    }

    public async Task PauserAsync(Guid instanceId)
    {
        var instance = await instanceRepo.ObtenirParIdAsync(instanceId)
            ?? throw new InvalidOperationException($"Instance {instanceId} introuvable");

        instance.Etat = EtatWorkflow.EnPause;
        await instanceRepo.MettreAJourAsync(instance);
        await notificateur.NotifierChangementEtatAsync(instance.Id, EtatWorkflow.EnPause, instance.CorrelationId);
    }

    public async Task AnnulerAsync(Guid instanceId)
    {
        var instance = await instanceRepo.ObtenirParIdAsync(instanceId)
            ?? throw new InvalidOperationException($"Instance {instanceId} introuvable");

        instance.Etat = EtatWorkflow.Annule;
        instance.DateFin = DateTime.UtcNow;
        await instanceRepo.MettreAJourAsync(instance);
        await notificateur.NotifierChangementEtatAsync(instance.Id, EtatWorkflow.Annule, instance.CorrelationId);
    }

    private async Task ExecuterTachesAsync(InstanceWorkflow instance, DefinitionWorkflowYaml workflowYaml)
    {
        var contexte = DeserialiserContexte(instance.ContexteExecution);
        var tachesDef = workflowYaml.Taches;
        var indexTache = TrouverProchaineTache(instance, tachesDef);

        while (indexTache < tachesDef.Count)
        {
            if (instance.Etat is EtatWorkflow.EnPause or EtatWorkflow.Annule)
                break;

            var tacheDef = tachesDef[indexTache];
            var execution = ObtenirOuCreerExecution(instance, tacheDef, indexTache);

            // Vérifier condition
            if (!string.IsNullOrEmpty(tacheDef.Condition))
            {
                var conditionResult = HandlebarsResolver.Resoudre(tacheDef.Condition, contexte);
                if (!EstVrai(conditionResult))
                {
                    execution.Etat = EtatTache.Ignoree;
                    await tacheRepo.MettreAJourAsync(execution);
                    indexTache++;
                    continue;
                }
            }

            // Exécuter la tâche
            execution.Etat = EtatTache.EnCours;
            execution.DateDebut = DateTime.UtcNow;
            await tacheRepo.MettreAJourAsync(execution);
            await notificateur.NotifierTacheDemarreeAsync(instance.Id, execution, instance.CorrelationId);

            var parametresResolus = HandlebarsResolver.ResoudreParametres(tacheDef.Parametres, contexte);
            if (tacheDef.HttpClientId is not null)
                parametresResolus["httpClientId"] = tacheDef.HttpClientId;

            // Vérifier si un mock est enregistré pour cette tâche (par son id)
            var typeCible = tacheDef.Type;
            if (await mockResolver.ResoudreAsync(tacheDef.Id, parametresResolus) is not null)
                typeCible = "mock";

            var connecteur = connecteurs.Obtenir(typeCible);
            if (connecteur is null)
            {
                await MarquerEnErreurAsync(instance, execution, $"Type de connecteur inconnu: {tacheDef.Type}");
                return;
            }

            // Passer l'id de la tâche comme mockId pour que MockConnecteur sache quoi résoudre
            parametresResolus["mock"] = tacheDef.Id;
            execution.DonneesEntree = JsonSerializer.Serialize(parametresResolus);

            var resultat = await connecteur.ExecuterAsync(new ContexteConnecteur(
                tacheDef.Nom,
                instance.CorrelationId,
                parametresResolus,
                contexte));

            if (!resultat.Succes)
            {
                await MarquerEnErreurAsync(instance, execution, resultat.Erreur ?? "Erreur inconnue");
                return;
            }

            // Hook = pause en attente
            if (tacheDef.Hook)
            {
                execution.Etat = EtatTache.EnPause;
                execution.DonneesSortie = JsonSerializer.Serialize(resultat.Donnees);
                await tacheRepo.MettreAJourAsync(execution);
                instance.Etat = EtatWorkflow.EnPause;
                await instanceRepo.MettreAJourAsync(instance);
                await notificateur.NotifierChangementEtatAsync(instance.Id, EtatWorkflow.EnPause, instance.CorrelationId);
                return;
            }

            // Succès
            execution.Etat = EtatTache.Reussie;
            execution.DateFin = DateTime.UtcNow;
            execution.DonneesSortie = JsonSerializer.Serialize(resultat.Donnees);
            await tacheRepo.MettreAJourAsync(execution);
            await notificateur.NotifierTacheTermineeAsync(instance.Id, execution, instance.CorrelationId);

            // Stocker output dans le contexte
            StockerSortie(contexte, tacheDef, resultat);

            instance.ContexteExecution = JsonSerializer.Serialize(contexte);
            await instanceRepo.MettreAJourAsync(instance);

            // Navigation : branches ou suivant
            indexTache = DeterminerProchainIndex(tacheDef, tachesDef, contexte, indexTache);
        }

        // Toutes les tâches terminées
        if (instance.Etat == EtatWorkflow.EnCours)
        {
            instance.Etat = EtatWorkflow.Termine;
            instance.DateFin = DateTime.UtcNow;
            await instanceRepo.MettreAJourAsync(instance);
            await notificateur.NotifierWorkflowTermineAsync(instance.Id, instance.CorrelationId);
        }
    }

    private static int TrouverProchaineTache(InstanceWorkflow instance, List<TacheYaml> tachesDef)
    {
        if (instance.Taches.Count == 0) return 0;

        var derniereTacheEnAttente = instance.Taches
            .Where(t => t.Etat is EtatTache.EnAttente or EtatTache.EnPause)
            .MinBy(t => t.Ordre);

        if (derniereTacheEnAttente is not null)
            return derniereTacheEnAttente.Ordre;

        var derniereTacheReussie = instance.Taches
            .Where(t => t.Etat == EtatTache.Reussie)
            .MaxBy(t => t.Ordre);

        return derniereTacheReussie is not null ? derniereTacheReussie.Ordre + 1 : 0;
    }

    private ExecutionTache ObtenirOuCreerExecution(InstanceWorkflow instance, TacheYaml tacheDef, int index)
    {
        var existante = instance.Taches.FirstOrDefault(t => t.NomTache == tacheDef.Id);
        if (existante is not null) return existante;

        var execution = new ExecutionTache
        {
            InstanceWorkflowId = instance.Id,
            NomTache = tacheDef.Id,
            TypeConnecteur = tacheDef.Type,
            Ordre = index
        };
        instance.Taches.Add(execution);
        tacheRepo.CreerAsync(execution).GetAwaiter().GetResult();
        return execution;
    }

    private async Task MarquerEnErreurAsync(InstanceWorkflow instance, ExecutionTache execution, string erreur)
    {
        execution.Etat = EtatTache.EnErreur;
        execution.Erreur = erreur;
        execution.DateFin = DateTime.UtcNow;
        await tacheRepo.MettreAJourAsync(execution);
        await notificateur.NotifierTacheEnErreurAsync(instance.Id, execution, instance.CorrelationId);

        instance.Etat = EtatWorkflow.EnErreur;
        instance.Erreur = $"Tâche '{execution.NomTache}': {erreur}";
        await instanceRepo.MettreAJourAsync(instance);
        await notificateur.NotifierChangementEtatAsync(instance.Id, EtatWorkflow.EnErreur, instance.CorrelationId);

        logger.LogError("Tâche {NomTache} en erreur: {Erreur} [CorrelationId={CorrelationId}]",
            execution.NomTache, erreur, instance.CorrelationId);
    }

    private static void StockerSortie(Dictionary<string, object?> contexte, TacheYaml tacheDef, ResultatConnecteur resultat)
    {
        var tacheContexte = new Dictionary<string, object?> { ["output"] = resultat.Donnees };
        if (!contexte.ContainsKey("tache"))
            contexte["tache"] = new Dictionary<string, object?>();

        if (contexte["tache"] is Dictionary<string, object?> taches)
            taches[tacheDef.Id] = tacheContexte;

        // Résoudre les mappings output explicites
        if (tacheDef.Output is not null && resultat.Donnees is not null)
        {
            var jsonSortie = JsonSerializer.Serialize(resultat.Donnees);
            foreach (var (cle, jsonPath) in tacheDef.Output)
            {
                var valeur = HandlebarsResolver.ExtraireJsonPath(jsonSortie, jsonPath);
                contexte[cle] = valeur;
            }
        }
    }

    private static int DeterminerProchainIndex(TacheYaml tacheDef, List<TacheYaml> tachesDef, Dictionary<string, object?> contexte, int indexActuel)
    {
        // Branches conditionnelles
        if (tacheDef.Branches is not null)
        {
            foreach (var branche in tacheDef.Branches)
            {
                var resultatCondition = HandlebarsResolver.Resoudre(branche.Condition, contexte);
                if (EstVrai(resultatCondition))
                {
                    var idx = tachesDef.FindIndex(t => t.Id == branche.Aller);
                    if (idx >= 0) return idx;
                }
            }
        }

        // Suivant explicite
        if (!string.IsNullOrEmpty(tacheDef.Suivant))
        {
            var idx = tachesDef.FindIndex(t => t.Id == tacheDef.Suivant);
            if (idx >= 0) return idx;
        }

        return indexActuel + 1;
    }

    private static bool EstVrai(string value) =>
        !string.IsNullOrEmpty(value)
        && !value.Equals("false", StringComparison.OrdinalIgnoreCase)
        && value != "0"
        && !value.Equals("null", StringComparison.OrdinalIgnoreCase)
        && !value.Equals("non", StringComparison.OrdinalIgnoreCase);

    private static Dictionary<string, object?> DeserialiserContexte(string? json)
    {
        if (string.IsNullOrEmpty(json)) return new Dictionary<string, object?>();
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, object?>>(json) ?? new();
        }
        catch
        {
            return new Dictionary<string, object?>();
        }
    }
}
