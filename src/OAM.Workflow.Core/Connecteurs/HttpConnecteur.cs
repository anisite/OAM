using Microsoft.Extensions.Logging;
using OAM.Domain.Interfaces;
using YamlHttpClient;
using YamlHttpClient.Settings;
using YamlHttpClient.Utils;

namespace OAM.Workflow.Core.Connecteurs;

/// <summary>
/// Connecteur HTTP utilisant YamlHttpClient.
/// Propagation automatique du CorrelationId dans les headers sortants.
/// </summary>
public class HttpConnecteur(
    ConfigurationYamlHttp configHttp,
    ILogger<HttpConnecteur> logger) : IConnecteur
{
    public string Type => "http";

    public async Task<ResultatConnecteur> ExecuterAsync(ContexteConnecteur contexte)
    {
        try
        {
            var httpClientId = contexte.Parametres.GetValueOrDefault("httpClientId")?.ToString();
            if (string.IsNullOrEmpty(httpClientId))
                return new ResultatConnecteur(false, null, "httpClientId est requis pour un connecteur HTTP");

            var settings = configHttp.ObtenirSettings(httpClientId);
            if (settings is null)
                return new ResultatConnecteur(false, null, $"Configuration HTTP '{httpClientId}' introuvable");

            // Ajouter le CorrelationId aux headers
            settings.Headers ??= new Dictionary<string, string>();
            settings.Headers["X-Correlation-Id"] = contexte.CorrelationId;

            var factory = new YamlHttpClientFactory(settings);

            var variables = contexte.Variables
                .Where(kv => kv.Value is not null)
                .ToDictionary(kv => kv.Key, kv => kv.Value!);

            var response = await factory.AutoCallAsync(variables);

            logger.LogInformation(
                "HTTP {HttpClientId} exécuté pour tâche {NomTache} [CorrelationId={CorrelationId}]",
                httpClientId, contexte.NomTache, contexte.CorrelationId);

            return new ResultatConnecteur(true, response);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Erreur HTTP pour tâche {NomTache} [CorrelationId={CorrelationId}]",
                contexte.NomTache, contexte.CorrelationId);
            return new ResultatConnecteur(false, null, ex.Message);
        }
    }
}

/// <summary>
/// Gestionnaire de configuration YAML pour les appels HTTP.
/// Charge les configurations depuis YamlHttpClientConfigBuilder.
/// </summary>
public class ConfigurationYamlHttp
{
    private readonly YamlHttpClientConfigBuilder _config = new();

    public void ChargerDepuisYaml(string yaml, string? nom = null)
    {
        _config.LoadFromString(yaml, nom ?? "default");
    }

    public HttpClientSettings? ObtenirSettings(string id)
    {
        return _config.HttpClient?.GetValueOrDefault(id);
    }

    public YamlHttpClientConfigBuilder Config => _config;
}
