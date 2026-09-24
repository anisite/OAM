using System.Text;
using System.Text.Json.Nodes;
using DurableTask.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OIM.Moteur.Expressions;
using OIM.Moteur.Hebergement;

namespace OIM.Moteur.Orchestration.Activites;

/// <summary>
/// Étapes dédiées de transmission de documents (chargerDocuments, apparierGdi, validerDossierAnterieur,
/// genererPageGarde, deposerGed) : les propriétés résolues de l'étape sont transmises en JSON (POST) au
/// service configuré pour ce type (<c>Oim:Services:&lt;type&gt;:Url</c>); sa réponse JSON devient la sortie.
/// Les documents voyagent par référence (noms dans le dépôt), jamais par contenu.
/// En-têtes : <c>Idempotency-Key</c> (identique entre les reprises), <c>X-Correlation-Id</c> (instance).
/// </summary>
public sealed class ServiceActivite(IOptions<OptionsOim> options, ILogger<ServiceActivite> journal) : ActiviteJson
{
    protected override async Task<JsonNode?> ExecuterAsync(TaskContext contexte, JsonObject p)
    {
        var service = Requis(p, "service");
        var etape = p["etape"]?.GetValue<string>() ?? service;

        // Mode test : le mock du cas de test tient lieu de réponse (même contrat que la sortie du service).
        if (p["test"]?.GetValue<bool>() == true)
            return p["mock"]?.DeepClone()
                   ?? throw new ErreurProcessus($"Mode test : un mock est requis pour l'étape « {etape} » ({service}).");

        if (!options.Value.Services.TryGetValue(service, out var config) || string.IsNullOrWhiteSpace(config.Url))
            throw new ErreurProcessus($"Service « {service} » non configuré (Oim:Services:{service}:Url).");

        using var client = new HttpClient(new HttpClientHandler { UseDefaultCredentials = config.UtiliserIdentiteWindows })
        {
            Timeout = config.Delai
        };
        using var requete = new HttpRequestMessage(HttpMethod.Post, config.Url)
        {
            Content = new StringContent((p["parametres"] ?? new JsonObject()).ToJsonString(Json.Options), Encoding.UTF8, "application/json")
        };
        if (p["cle"]?.GetValue<string>() is { } cle) requete.Headers.TryAddWithoutValidation("Idempotency-Key", cle);
        if (p["correlation"]?.GetValue<string>() is { } correlation) requete.Headers.TryAddWithoutValidation("X-Correlation-Id", correlation);

        using var reponse = await client.SendAsync(requete);
        var corps = await reponse.Content.ReadAsStringAsync();
        journal.LogInformation("Service {Service} ({Etape}) → {Statut}", service, etape, (int)reponse.StatusCode);

        if (!reponse.IsSuccessStatusCode)
            throw new ErreurProcessus($"Service « {service} » : HTTP {(int)reponse.StatusCode} {reponse.ReasonPhrase} : {Gabarit.Tronquer(corps, 500)}");

        return Json.Lire(corps);
    }
}
