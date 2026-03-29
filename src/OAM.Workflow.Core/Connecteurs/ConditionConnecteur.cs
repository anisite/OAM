using Microsoft.Extensions.Logging;
using OAM.Domain.Interfaces;
using OAM.Workflow.Core.Handlebars;

namespace OAM.Workflow.Core.Connecteurs;

/// <summary>
/// Connecteur de condition / branchement logique.
/// </summary>
public class ConditionConnecteur(ILogger<ConditionConnecteur> logger) : IConnecteur
{
    public string Type => "condition";

    public Task<ResultatConnecteur> ExecuterAsync(ContexteConnecteur contexte)
    {
        var expression = contexte.Parametres.GetValueOrDefault("expression")?.ToString();
        if (string.IsNullOrEmpty(expression))
            return Task.FromResult(new ResultatConnecteur(false, null, "Expression de condition manquante"));

        var resultat = HandlebarsResolver.Resoudre(expression, contexte.Variables);
        var estVrai = resultat.Equals("true", StringComparison.OrdinalIgnoreCase)
                      || resultat == "1"
                      || resultat.Equals("oui", StringComparison.OrdinalIgnoreCase);

        logger.LogInformation(
            "Condition évaluée pour {NomTache}: {Expression} = {Resultat} [CorrelationId={CorrelationId}]",
            contexte.NomTache, expression, estVrai, contexte.CorrelationId);

        return Task.FromResult(new ResultatConnecteur(true, estVrai));
    }
}
