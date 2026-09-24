using System.Text.Json.Nodes;
using DurableTask.Core;
using Microsoft.Extensions.Options;
using OIM.Moteur.Definitions;
using OIM.Moteur.Expressions;
using OIM.Moteur.Hebergement;
using OIM.Moteur.Stockage;

namespace OIM.Moteur.Orchestration.Activites;

/// <summary>
/// Étape <c>boiteGenerique</c> : adresse de la boîte retenue (le bloc est choisi par l'orchestration).
/// <list type="bullet">
///   <item><c>a</c> : adresse, ou adresses par palier <c>{ unitaire: …, acceptation: …, production: … }</c>;</item>
///   <item><c>bsq: { table: courriels.BSQ.{palier}.yml, cle: "074", region?: "12" }</c> : table du paquet
///   (format ECS25A : région → CLE → courriel, et <c>default.default.courriel</c> si la CLE est introuvable).</item>
/// </list>
/// Lecture seule du paquet : aucun effet de bord, exécutée aussi en mode test.
/// </summary>
public sealed class ResoudreBoiteActivite(IDepotDefinitions depot, IOptions<OptionsOim> options) : ActiviteJson
{
    protected override async Task<JsonNode?> ExecuterAsync(TaskContext contexte, JsonObject p)
    {
        var palier = options.Value.Palier;
        var bloc = p["bloc"] as JsonObject ?? throw new ErreurProcessus("Bloc de boîte générique absent.");
        var resultat = new JsonObject
        {
            ["index"] = p["index"]?.DeepClone(),
            ["suffixeObjet"] = bloc["suffixeObjet"]?.DeepClone(),
            ["palier"] = palier
        };

        if (bloc["bsq"] is JsonObject bsq)
        {
            var id = Requis(p, "definitionId");
            var version = p["version"]!.GetValue<int>();
            var definition = await depot.ObtenirAsync(id, version) ?? throw new ErreurProcessus($"Processus « {id} » v{version} introuvable.");

            var nomTable = Expression.EnTexte(bsq["table"]).Replace("{palier}", palier);
            var contenu = definition.Paquet().TrouverFichier(nomTable)
                          ?? throw new ErreurProcessus($"Table BSQ « {nomTable} » introuvable dans le paquet.");
            var table = YamlJson.Lire(contenu) as JsonObject ?? throw new ErreurProcessus($"Table BSQ « {nomTable} » invalide.");

            var cle = Expression.EnTexte(bsq["cle"]).Trim();
            if (cle.Length is > 0 and < 3) cle = cle.PadLeft(3, '0');
            var region = bsq["region"] is { } r ? Expression.EnTexte(r) : null;

            var (courriel, regionTrouvee) = ChercherCle(table, cle, region);
            resultat["source"] = courriel is null ? "defaut" : "bsq";
            resultat["cle"] = cle;
            resultat["region"] = regionTrouvee;
            resultat["a"] = courriel ?? Courriel(table["default"]?["default"]);
            resultat["table"] = nomTable;
        }
        else
        {
            resultat["source"] = "conditions";
            resultat["a"] = Expression.EnTexte(Variantes.Choisir(bloc["a"], palier));
        }

        if (string.IsNullOrWhiteSpace(resultat["a"]?.GetValue<string>()))
            throw new ErreurProcessus($"Aucune adresse de boîte générique pour le palier « {palier} ».");
        return resultat;
    }

    /// <summary>
    /// La CLE dans la région indiquée, sinon dans toutes les régions de la table; à défaut, l'adresse par
    /// défaut de la région indiquée. Les défauts de région peuvent être des fusions YAML (<c>&lt;&lt;: *ancre</c>).
    /// </summary>
    private static (string? Courriel, string? Region) ChercherCle(JsonObject table, string cle, string? region)
    {
        IEnumerable<KeyValuePair<string, JsonNode?>> regions = table.Where(e => e.Key != "default");
        if (region is not null) regions = regions.OrderBy(e => e.Key == region ? 0 : 1);

        if (cle.Length > 0)
            foreach (var (nomRegion, contenuRegion) in regions)
                if (contenuRegion is JsonObject o && Courriel(o[cle]) is { } courriel)
                    return (courriel, nomRegion);

        return region is not null && Courriel(table[region]?["default"]) is { } defautRegion
            ? (defautRegion, region)
            : (null, null);
    }

    private static string? Courriel(JsonNode? entree) =>
        entree is JsonObject o && (o["courriel"] ?? o["<<"]?["courriel"]) is { } c ? Expression.EnTexte(c) : null;
}
