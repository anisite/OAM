using Microsoft.Extensions.Options;
using OIM.Moteur.Definitions;
using OIM.Moteur.Hebergement;
using OIM.Moteur.Stockage;
using OIM.Moteur.Tests;

namespace OIM.Moteur.Pilotage;

public sealed record ResultatDeploiementComplet(ResultatLecture Validation, ResultatDeploiement? Deploiement, RapportTests? Tests = null)
{
    /// <summary>Déploiement refusé parce que des tests métier ont échoué (mode bloquant).</summary>
    public bool RefuseParTests => Deploiement is null && Validation.Valide && Tests is { Reussi: false };
}

/// <summary>Définitions de processus, par équipe. Les ids sont qualifiés (« equipe.processus »).</summary>
public sealed class ServiceDefinitions(IDepotDefinitions depot, ServiceTests tests, ServiceEquipes equipes, IOptions<OptionsOim> options)
{
    public ResultatLecture Valider(PaquetDefinition paquet) => ValidateurDefinition.Valider(paquet);

    /// <summary>
    /// Valide, exécute les tests métier du paquet (selon <see cref="OptionsTests.AuDeploiement"/>), puis déploie
    /// dans l'équipe. Rien n'est enregistré si la validation comporte des erreurs, ni si un test échoue en
    /// mode bloquant (sauf <paramref name="ignorerTests"/>).
    /// </summary>
    public async Task<ResultatDeploiementComplet> DeployerAsync(Habilitations h, string equipe, PaquetDefinition paquet, string? commentaire,
        CancellationToken ct = default, bool executerTests = true, bool ignorerTests = false)
    {
        h.ExigerDeploiement(equipe, $"Équipe « {equipe} » introuvable.");
        await equipes.ExigerActiveAsync(equipe, ct);

        var validation = Valider(paquet);
        if (!validation.Valide) return new ResultatDeploiementComplet(validation, null);

        RapportTests? rapport = null;
        var mode = options.Value.Tests.AuDeploiement;
        if (executerTests && mode != ModeTestsDeploiement.Desactive && CasTest.Trouver(paquet).Any())
        {
            rapport = await tests.ExecuterAsync(equipe, paquet, ct: ct);
            if (!rapport.Reussi && mode == ModeTestsDeploiement.Bloquant && !ignorerTests)
                return new ResultatDeploiementComplet(validation, null, rapport);
        }

        var resultat = await depot.DeployerAsync(equipe, validation.Definition!, paquet, h.Utilisateur, commentaire, ct);
        return new ResultatDeploiementComplet(validation, resultat, rapport);
    }

    /// <summary>Tests métier d'une version déployée (ou de la version courante).</summary>
    public async Task<RapportTests> TesterAsync(Habilitations h, string id, int? version, string? cas, bool conserver, CancellationToken ct = default)
    {
        var equipe = IdsEquipe.Equipe(id);
        h.ExigerDeploiement(equipe, Introuvable(id, version));
        return await tests.ExecuterAsync(equipe!, (await ObtenirAsync(h, id, version, ct)).Paquet(), cas, conserver, ct);
    }

    /// <summary>Tests métier d'un paquet non déployé (concepteur, CI).</summary>
    public async Task<RapportTests> TesterAsync(Habilitations h, string equipe, PaquetDefinition paquet, string? cas, bool conserver,
        CancellationToken ct = default)
    {
        h.ExigerDeploiement(equipe, $"Équipe « {equipe} » introuvable.");
        await equipes.ExigerActiveAsync(equipe, ct);
        return await tests.ExecuterAsync(equipe, paquet, cas, conserver, ct);
    }

    /// <param name="equipe">Une seule équipe, ou toutes celles visibles.</param>
    public Task<IReadOnlyList<ResumeDefinition>> ListerAsync(Habilitations h, string? equipe = null, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(equipe)) return depot.ListerAsync(h.FiltreEquipes?.ToList(), ct);
        h.ExigerLecture(equipe, $"Équipe « {equipe} » introuvable.");
        return depot.ListerAsync([equipe], ct);
    }

    public async Task<IReadOnlyList<ResumeVersion>> VersionsAsync(Habilitations h, string id, CancellationToken ct = default)
    {
        h.ExigerLecture(IdsEquipe.Equipe(id), Introuvable(id, null));
        return await depot.VersionsAsync(id, ct);
    }

    public async Task<VersionDefinition> ObtenirAsync(Habilitations h, string id, int? version, CancellationToken ct = default)
    {
        h.ExigerLecture(IdsEquipe.Equipe(id), Introuvable(id, version));
        return await depot.ObtenirAsync(id, version, ct) ?? throw ErreurPilotage.Introuvable(Introuvable(id, version));
    }

    public async Task DefinirActifAsync(Habilitations h, string id, bool actif, CancellationToken ct = default)
    {
        h.ExigerDeploiement(IdsEquipe.Equipe(id), Introuvable(id, null));
        if (!await depot.DefinirActifAsync(id, actif, ct))
            throw ErreurPilotage.Introuvable(Introuvable(id, null));
    }

    private static string Introuvable(string id, int? version) =>
        version is null ? $"Processus « {id} » introuvable." : $"Processus « {id} » v{version} introuvable.";
}
