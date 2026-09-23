using System.Text.Json.Nodes;
using DurableTask.Core;
using OIM.Moteur.Stockage;

namespace OIM.Moteur.Orchestration.Activites;

/// <summary>
/// Charge le YAML d'une version de processus. Passer par une activité rend le chargement
/// déterministe : lors d'une relecture, le résultat provient de l'historique.
/// </summary>
public sealed class ChargerDefinitionActivite(IDepotDefinitions depot) : ActiviteJson
{
    protected override async Task<JsonNode?> ExecuterAsync(TaskContext contexte, JsonObject p)
    {
        var id = Requis(p, "definitionId");
        int? version = p["version"]?.GetValue<int>();

        var def = await depot.ObtenirAsync(id, version)
                  ?? throw new ErreurProcessus(version is null
                      ? $"Processus « {id} » introuvable."
                      : $"Processus « {id} » v{version} introuvable.");

        return new JsonObject { ["definitionId"] = def.DefinitionId, ["version"] = def.Version, ["yaml"] = def.Yaml };
    }
}
