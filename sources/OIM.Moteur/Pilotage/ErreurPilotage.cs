namespace OIM.Moteur.Pilotage;

/// <summary>Erreur destinée à l'appelant de l'API (convertie en ProblemDetails).</summary>
public sealed class ErreurPilotage(int statutHttp, string message, IReadOnlyList<string>? details = null) : Exception(message)
{
    public int StatutHttp { get; } = statutHttp;
    public IReadOnlyList<string> Details { get; } = details ?? [];

    public static ErreurPilotage Introuvable(string message) => new(404, message);
    public static ErreurPilotage Conflit(string message) => new(409, message);
    public static ErreurPilotage Invalide(string message, IReadOnlyList<string>? details = null) => new(400, message, details);
}
