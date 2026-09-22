namespace OAM.Domain.Interfaces;

/// <summary>
/// Code bas niveau, technique et générique (ex: HttpConnecteur).
/// Ne connaît aucune logique d'affaires, il ne fait qu'exécuter un protocole.
/// </summary>
public interface IConnecteur
{
    string Type { get; }
    Task<ResultatConnecteur> ExecuterAsync(ContexteConnecteur contexte);
}

public record ContexteConnecteur(
    string NomTache,
    string CorrelationId,
    Dictionary<string, object?> Parametres,
    Dictionary<string, object?> Variables
);

public record ResultatConnecteur(
    bool Succes,
    object? Donnees,
    string? Erreur = null
);
