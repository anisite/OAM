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

public sealed class ServiceDefinitions(IDepotDefinitions depot, ServiceTests tests, IOptions<OptionsOim> options)
{
    public ResultatLecture Valider(PaquetDefinition paquet) => ValidateurDefinition.Valider(paquet);

    /// <summary>
    /// Valide, exécute les tests métier du paquet (selon <see cref="OptionsTests.AuDeploiement"/>), puis déploie.
    /// Rien n'est enregistré si la validation comporte des erreurs, ni si un test échoue en mode bloquant
    /// (sauf <paramref name="ignorerTests"/>).
    /// </summary>
    public async Task<ResultatDeploiementComplet> DeployerAsync(PaquetDefinition paquet, string? par, string? commentaire,
        CancellationToken ct = default, bool executerTests = true, bool ignorerTests = false)
    {
        var validation = Valider(paquet);
        if (!validation.Valide) return new ResultatDeploiementComplet(validation, null);

        RapportTests? rapport = null;
        var mode = options.Value.Tests.AuDeploiement;
        if (executerTests && mode != ModeTestsDeploiement.Desactive && CasTest.Trouver(paquet).Any())
        {
            rapport = await tests.ExecuterAsync(paquet, ct: ct);
            if (!rapport.Reussi && mode == ModeTestsDeploiement.Bloquant && !ignorerTests)
                return new ResultatDeploiementComplet(validation, null, rapport);
        }

        var resultat = await depot.DeployerAsync(validation.Definition!, paquet, par, commentaire, ct);
        return new ResultatDeploiementComplet(validation, resultat, rapport);
    }

    /// <summary>Tests métier d'une version déployée (ou de la version courante).</summary>
    public async Task<RapportTests> TesterAsync(string id, int? version, string? cas, bool conserver, CancellationToken ct = default) =>
        await tests.ExecuterAsync((await ObtenirAsync(id, version, ct)).Paquet(), cas, conserver, ct);

    /// <summary>Tests métier d'un paquet non déployé (concepteur, CI).</summary>
    public Task<RapportTests> TesterAsync(PaquetDefinition paquet, string? cas, bool conserver, CancellationToken ct = default) =>
        tests.ExecuterAsync(paquet, cas, conserver, ct);

    public Task<IReadOnlyList<ResumeDefinition>> ListerAsync(CancellationToken ct = default) => depot.ListerAsync(ct);

    public Task<IReadOnlyList<ResumeVersion>> VersionsAsync(string id, CancellationToken ct = default) => depot.VersionsAsync(id, ct);

    public async Task<VersionDefinition> ObtenirAsync(string id, int? version, CancellationToken ct = default) =>
        await depot.ObtenirAsync(id, version, ct)
        ?? throw ErreurPilotage.Introuvable(version is null ? $"Processus « {id} » introuvable." : $"Processus « {id} » v{version} introuvable.");

    public async Task DefinirActifAsync(string id, bool actif, CancellationToken ct = default)
    {
        if (!await depot.DefinirActifAsync(id, actif, ct))
            throw ErreurPilotage.Introuvable($"Processus « {id} » introuvable.");
    }
}
