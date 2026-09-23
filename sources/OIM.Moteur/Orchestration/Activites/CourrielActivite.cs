using System.Net;
using System.Net.Mail;
using System.Text.Json.Nodes;
using DurableTask.Core;
using HandlebarsDotNet;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OIM.Moteur.Definitions;
using OIM.Moteur.Expressions;
using OIM.Moteur.Hebergement;
using OIM.Moteur.Stockage;

namespace OIM.Moteur.Orchestration.Activites;

/// <summary>
/// Étape <c>type: courriel</c>. Le gabarit (<c>gabarits/&lt;nom&gt;.yml</c>) est un YAML :
/// <code>
/// de: ne-pas-repondre@exemple.gouv.qc.ca   # optionnel
/// a: "{{entrees.courriel}}"                # optionnel si l'étape précise « a »
/// sujet: "Votre demande {{entrees.dossierId}}"
/// format: html                             # html (défaut) ou texte
/// corps: |
///   &lt;p&gt;Bonjour…&lt;/p&gt;
/// </code>
/// Le sujet et le corps sont des gabarits Handlebars sur le contexte du processus
/// (entrees, etapes, variables, evenement…), complété par les <c>donnees</c> de l'étape.
/// </summary>
public sealed class CourrielActivite(IDepotDefinitions depot, IOptions<OptionsOim> options, ILogger<CourrielActivite> journal)
    : ActiviteJson
{
    private static readonly IHandlebars HandlebarsHtml = Handlebars.Create();
    private static readonly IHandlebars HandlebarsTexte = Handlebars.Create(new HandlebarsConfiguration { NoEscape = true });

    protected override async Task<JsonNode?> ExecuterAsync(TaskContext contexte, JsonObject p)
    {
        var reglages = options.Value.Courriel;
        var id = Requis(p, "definitionId");
        var version = p["version"]!.GetValue<int>();
        var nomGabarit = Requis(p, "gabarit");

        var definition = await depot.ObtenirAsync(id, version)
                         ?? throw new ErreurProcessus($"Processus « {id} » v{version} introuvable.");
        var fichier = definition.Paquet().TrouverGabarit(nomGabarit)
                      ?? throw new ErreurProcessus($"Gabarit de courriel « {nomGabarit} » introuvable.");
        var gabarit = YamlJson.Lire(fichier.Contenu) as JsonObject
                      ?? throw new ErreurProcessus($"Gabarit « {fichier.Chemin} » invalide.");

        var modele = Json.VersObjet(p["contexte"]) ?? new Dictionary<string, object?>();
        string Rendre(IHandlebars hb, JsonNode? source) =>
            source is null ? string.Empty : hb.Compile(Expression.EnTexte(source))(modele);

        var estHtml = !string.Equals(gabarit["format"]?.GetValue<string>(), "texte", StringComparison.OrdinalIgnoreCase);
        var sujet = Rendre(HandlebarsTexte, gabarit["sujet"]).ReplaceLineEndings(" ").Trim();
        var corps = Rendre(estHtml ? HandlebarsHtml : HandlebarsTexte, gabarit["corps"]);

        var a = Adresses(p["a"]) is { Count: > 0 } aEtape ? aEtape : Adresses(JsonValue.Create(Rendre(HandlebarsTexte, gabarit["a"])));
        var cc = Adresses(p["cc"]).Concat(Adresses(JsonValue.Create(Rendre(HandlebarsTexte, gabarit["cc"])))).ToList();
        var cci = Adresses(p["cci"]);
        if (a.Count == 0) throw new ErreurProcessus($"Aucun destinataire pour le courriel « {nomGabarit} ».");

        var de = gabarit["de"] is { } d ? Rendre(HandlebarsTexte, d) : reglages.De;

        using var message = new MailMessage { From = new MailAddress(de), Subject = sujet, Body = corps, IsBodyHtml = estHtml };
        var redirection = reglages.RedirigerVers;
        foreach (var x in redirection is null ? a : [redirection]) message.To.Add(x);
        if (redirection is null)
        {
            foreach (var x in cc) message.CC.Add(x);
            foreach (var x in cci) message.Bcc.Add(x);
        }
        message.Headers.Add("X-OIM-Instance", contexte.OrchestrationInstance?.InstanceId ?? string.Empty);
        if (p["cle"]?.GetValue<string>() is { } cle) message.Headers.Add("X-OIM-Cle", cle);

        // Mode test : le gabarit est entièrement rendu (erreurs détectées) mais rien n'est envoyé.
        if (p["test"]?.GetValue<bool>() == true)
            return new JsonObject
            {
                ["a"] = new JsonArray(a.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray()),
                ["cc"] = new JsonArray(cc.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray()),
                ["sujet"] = sujet,
                ["corps"] = corps,
                ["gabarit"] = fichier.Chemin,
                ["simule"] = true
            };

        using var smtp = CreerClient(reglages);
        await smtp.SendMailAsync(message);

        journal.LogInformation("Courriel « {Sujet} » transmis à {Destinataires}", sujet, string.Join(", ", a));

        return new JsonObject
        {
            ["a"] = new JsonArray(a.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray()),
            ["sujet"] = sujet,
            ["gabarit"] = fichier.Chemin,
            ["redirige"] = redirection is not null
        };
    }

    private static SmtpClient CreerClient(OptionsCourriel r)
    {
        if (!string.IsNullOrWhiteSpace(r.DossierDepot))
        {
            var dossier = Path.GetFullPath(r.DossierDepot);
            Directory.CreateDirectory(dossier);
            return new SmtpClient { DeliveryMethod = SmtpDeliveryMethod.SpecifiedPickupDirectory, PickupDirectoryLocation = dossier };
        }

        if (string.IsNullOrWhiteSpace(r.Hote))
            throw new InvalidOperationException("Oim:Courriel:Hote ou Oim:Courriel:DossierDepot doit être configuré.");

        var client = new SmtpClient(r.Hote, r.Port) { EnableSsl = r.Ssl };
        if (!string.IsNullOrEmpty(r.Utilisateur))
            client.Credentials = new NetworkCredential(r.Utilisateur, r.MotDePasse);
        return client;
    }

    private static List<string> Adresses(JsonNode? valeur) => valeur switch
    {
        null => [],
        JsonArray t => t.SelectMany(Adresses).ToList(),
        _ => Expression.EnTexte(valeur).Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList()
    };
}
