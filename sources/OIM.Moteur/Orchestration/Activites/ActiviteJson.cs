using System.Text.Json.Nodes;
using DurableTask.Core;

namespace OIM.Moteur.Orchestration.Activites;

/// <summary>Base des activités : entrée et sortie JSON (voir <see cref="Json"/> pour le transport).</summary>
public abstract class ActiviteJson : TaskActivity
{
    protected abstract Task<JsonNode?> ExecuterAsync(TaskContext contexte, JsonObject parametres);

    public override string Run(TaskContext context, string input) =>
        throw new NotSupportedException("Utiliser RunAsync.");

    public override async Task<string> RunAsync(TaskContext context, string input) =>
        Json.EcrireResultatActivite(await ExecuterAsync(context, Json.LireParametreActivite(input)));

    protected static string Requis(JsonObject p, string cle) =>
        p[cle]?.GetValue<string>() is { Length: > 0 } s ? s : throw new ArgumentException($"Paramètre « {cle} » manquant.");
}
