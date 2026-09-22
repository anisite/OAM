using Microsoft.Extensions.Logging;
using OAM.Domain.Interfaces;
using OAM.Workflow.Core.Engine;

namespace OAM.Workflow.Core.Connecteurs;

/// <summary>
/// Connecteur "reponse" : signale la réponse synchrone au caller HTTP et continue le workflow.
/// Le workflow continue en arrière-plan après avoir libéré la connexion HTTP.
/// </summary>
public class ReponseConnecteur(
    IAttenteReponse attenteReponse,
    ILogger<ReponseConnecteur> logger) : IConnecteur
{
    public string Type => "reponse";

    public Task<ResultatConnecteur> ExecuterAsync(ContexteConnecteur contexte)
    {
        attenteReponse.Signaler(contexte.CorrelationId, contexte.Parametres);

        logger.LogInformation(
            "Réponse signalée pour [CorrelationId={CorrelationId}] tâche={NomTache}",
            contexte.CorrelationId, contexte.NomTache);

        return Task.FromResult(new ResultatConnecteur(true, contexte.Parametres));
    }
}
