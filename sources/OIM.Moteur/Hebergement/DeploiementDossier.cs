using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OIM.Moteur.Definitions;
using OIM.Moteur.Pilotage;

namespace OIM.Moteur.Hebergement;

/// <summary>
/// Déploiement par dépôt de fichiers : chaque sous-dossier de <see cref="OptionsOim.DossierDefinitions"/>
/// contenant un YAML de processus est déployé au démarrage. Un contenu inchangé ne crée pas de version.
/// </summary>
public sealed class DeploiementDossier(ServiceDefinitions definitions, IOptions<OptionsOim> options, ILogger<DeploiementDossier> journal)
{
    public async Task DeployerAsync(CancellationToken ct)
    {
        var racine = options.Value.DossierDefinitions;
        if (string.IsNullOrWhiteSpace(racine)) return;
        racine = Path.GetFullPath(racine);
        if (!Directory.Exists(racine))
        {
            journal.LogWarning("Dossier de définitions « {Dossier} » introuvable.", racine);
            return;
        }

        foreach (var dossier in Directory.EnumerateDirectories(racine))
        {
            try
            {
                var paquet = PaquetDefinition.DepuisDossier(dossier);
                // Le moteur n'est pas encore démarré : les tests métier ne peuvent pas s'exécuter ici.
                var resultat = await definitions.DeployerAsync(paquet, "demarrage", $"Dossier {Path.GetFileName(dossier)}", ct, executerTests: false);
                if (resultat.Deploiement is { } d)
                    journal.LogInformation("Processus « {Id} » v{Version} {Etat}.", d.Id, d.Version, d.NouvelleVersion ? "déployé" : "inchangé");
                else
                    journal.LogError("Processus du dossier « {Dossier} » invalide : {Erreurs}", dossier,
                        string.Join(" | ", resultat.Validation.Erreurs.Select(e => (e.Etape is null ? "" : $"[{e.Etape}] ") + e.Message)));
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or Microsoft.Data.SqlClient.SqlException or TimeoutException)
            {
                journal.LogError("Dossier « {Dossier} » ignoré : {Message}", dossier, ex.Message);
            }
        }
    }
}
