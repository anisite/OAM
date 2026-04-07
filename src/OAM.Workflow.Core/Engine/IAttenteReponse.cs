namespace OAM.Workflow.Core.Engine;

/// <summary>
/// Mécanisme d'attente pour la réponse synchrone d'un workflow.
/// Implémentation mémoire pour serveur unique, Garnet/Redis pour multi-serveurs.
/// </summary>
public interface IAttenteReponse
{
    /// <summary>
    /// Attend qu'un workflow signale sa réponse via le correlationId.
    /// Retourne null si le timeout expire sans signal (le caller retourne l'instanceId).
    /// </summary>
    Task<object?> AttendrePourAsync(string correlationId, TimeSpan timeout);

    /// <summary>
    /// Signale que le workflow a produit une réponse (tâche de type "reponse").
    /// </summary>
    void Signaler(string correlationId, object? donnees);
}
