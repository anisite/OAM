using OAM.Domain.Interfaces;

namespace OAM.Workflow.Core.Engine;

/// <summary>
/// Registre des connecteurs disponibles, indexés par type.
/// </summary>
public class RegistreConnecteurs
{
    private readonly Dictionary<string, IConnecteur> _connecteurs = new(StringComparer.OrdinalIgnoreCase);

    public void Enregistrer(IConnecteur connecteur) =>
        _connecteurs[connecteur.Type] = connecteur;

    public IConnecteur? Obtenir(string type) =>
        _connecteurs.GetValueOrDefault(type);

    public IReadOnlyCollection<string> TypesDisponibles => _connecteurs.Keys;
}
