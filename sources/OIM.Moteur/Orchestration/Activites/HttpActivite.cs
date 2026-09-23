using System.Text.Json.Nodes;
using DurableTask.Core;
using Microsoft.Extensions.Logging;
using OIM.Moteur.Definitions;
using OIM.Moteur.Expressions;
using OIM.Moteur.Stockage;
using YamlHttpClient;
using YamlHttpClient.Settings;
using YamlHttpClient.Utils;

namespace OIM.Moteur.Orchestration.Activites;

/// <summary>
/// Étape <c>type: http</c> : appel d'un service décrit par un gabarit YamlHttpClient
/// (<c>requete: valider-dossier.yml</c> ou <c>requete: services.yml#validerDossier</c>).
/// Les <c>donnees</c> de l'étape sont la racine du modèle Handlebars du gabarit.
/// Un statut HTTP hors 2xx fait échouer l'activité (et déclenche le retry de l'étape).
/// </summary>
public sealed partial class HttpActivite(IDepotDefinitions depot, ILogger<HttpActivite> journal) : ActiviteJson
{
    protected override async Task<JsonNode?> ExecuterAsync(TaskContext contexte, JsonObject p)
    {
        var id = Requis(p, "definitionId");
        var version = p["version"]!.GetValue<int>();
        var reference = Requis(p, "requete");

        var definition = await depot.ObtenirAsync(id, version)
                         ?? throw new ErreurProcessus($"Processus « {id} » v{version} introuvable.");
        var fichier = definition.Paquet().TrouverRequete(reference)
                      ?? throw new ErreurProcessus($"Gabarit de requête « {reference} » introuvable.");

        var reglages = new YamlHttpClientConfigBuilder().LoadFromString(fichier.Contenu, RequeteHttp.ChoisirCle(fichier.Contenu, reference));
        reglages.Headers ??= new Dictionary<string, string>();
        if (p["correlation"]?.GetValue<string>() is { } correlation)
            reglages.Headers.TryAdd("X-Correlation-Id", correlation);
        if (p["cle"]?.GetValue<string>() is { } cle)
            reglages.Headers.TryAdd("Idempotency-Key", cle);

        var client = new YamlHttpClientFactory(reglages);
        var donnees = Json.VersObjet(p["donnees"]) ?? new Dictionary<string, object?>();

        if (p["test"]?.GetValue<bool>() == true)
            return Simuler(client, donnees, p);

        using var reponse = await client.AutoCallAsync(donnees);
        var corps = reponse.Content is null ? string.Empty : await reponse.Content.ReadAsStringAsync();

        journal.LogInformation("HTTP {Methode} {Url} → {Statut}", reglages.Method, client.LastResolvedUrl, (int)reponse.StatusCode);

        if (!reponse.IsSuccessStatusCode)
            throw new ErreurProcessus($"HTTP {(int)reponse.StatusCode} {reponse.ReasonPhrase} sur {client.LastResolvedUrl} : {Gabarit.Tronquer(corps, 500)}");

        await client.CheckResponseAsync(reponse);

        return new JsonObject
        {
            ["statut"] = (int)reponse.StatusCode,
            ["corps"] = Json.Lire(corps)
        };
    }
}

public sealed partial class HttpActivite
{
    /// <summary>
    /// Mode test : la requête est construite (gabarit, URL, en-têtes : les erreurs de gabarit sont donc
    /// détectées) mais n'est pas envoyée; le mock du cas de test tient lieu de réponse.
    /// Mock : <c>{ statut: 200, corps: {…} }</c>. Un statut hors 2xx échoue comme un vrai appel (retry, siErreur).
    /// </summary>
    private static JsonNode? Simuler(YamlHttpClientFactory client, object donnees, JsonObject p)
    {
        using var requete = client.BuildRequestMessage(donnees);
        var etape = p["etape"]?.GetValue<string>() ?? "?";
        var mock = p["mock"] as JsonObject
                   ?? throw new ErreurProcessus($"Mode test : aucun mock pour l'étape « {etape} » ({requete.Method} {requete.RequestUri}).");

        var statut = mock["statut"] is JsonValue v ? (int)Expression.Nombre(v) : 200;
        var corps = mock["corps"]?.DeepClone();
        if (statut is < 200 or > 299)
            throw new ErreurProcessus($"HTTP {statut} (simulé) sur {requete.RequestUri} : {Gabarit.Tronquer(corps?.ToJsonString() ?? "", 500)}");

        return new JsonObject { ["statut"] = statut, ["corps"] = corps, ["simule"] = true, ["url"] = requete.RequestUri?.ToString() };
    }
}

public static class RequeteHttp
{
    /// <summary>
    /// Clé <c>http_client</c> à utiliser : celle après <c>#</c>, sinon l'unique clé du fichier,
    /// sinon le nom du fichier sans extension.
    /// </summary>
    public static string ChoisirCle(string contenuYaml, string reference)
    {
        var cles = (YamlJson.Lire(contenuYaml) as JsonObject)?["http_client"] as JsonObject
                   ?? throw new InvalidDataException("section « http_client » absente.");

        var morceaux = reference.Split('#', 2);
        if (morceaux.Length == 2)
            return cles.ContainsKey(morceaux[1]) ? morceaux[1] : throw new InvalidDataException($"clé « {morceaux[1]} » absente de http_client.");

        if (cles.Count == 1) return cles.First().Key;

        var nom = Path.GetFileNameWithoutExtension(morceaux[0]);
        return cles.ContainsKey(nom)
            ? nom
            : throw new InvalidDataException($"plusieurs clés dans http_client ({string.Join(", ", cles.Select(c => c.Key))}) : précisez « {morceaux[0]}#cle ».");
    }
}
