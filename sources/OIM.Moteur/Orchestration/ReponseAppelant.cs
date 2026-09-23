using System.Globalization;
using System.Text.Json.Nodes;
using OIM.Moteur.Expressions;

namespace OIM.Moteur.Orchestration;

/// <summary>
/// Évaluation d'une étape « reponse », partagée par l'orchestration (qui publie la réponse) et
/// l'activité oim.reponse (qui la trace dans l'historique) : même fonction pure, même résultat.
/// </summary>
public static class ReponseAppelant
{
    public static (int StatutHttp, JsonNode? Corps) Evaluer(JsonNode? statutHttp, JsonNode? corps, JsonObject contexte)
    {
        var code = 200;
        if (statutHttp is not null)
        {
            var texte = Expression.EnTexte(Gabarit.Resoudre(statutHttp, contexte));
            if (!int.TryParse(texte, NumberStyles.Integer, CultureInfo.InvariantCulture, out code) || code is < 100 or > 599)
                throw new ErreurProcessus($"Statut HTTP « {texte} » invalide.");
        }
        return (code, Gabarit.Resoudre(corps, contexte));
    }
}
