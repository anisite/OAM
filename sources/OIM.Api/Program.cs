using Microsoft.AspNetCore.Diagnostics;
using OIM.Api;
using OIM.Moteur.Hebergement;
using OIM.Moteur.Pilotage;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AjouterOim(builder.Configuration);

// Les chemins relatifs de la configuration (dossier de définitions, dépôt de courriels)
// sont résolus depuis la racine de l'application, et non depuis le répertoire courant.
builder.Services.PostConfigure<OptionsOim>(o =>
{
    var racine = builder.Environment.ContentRootPath;
    if (!string.IsNullOrWhiteSpace(o.DossierDefinitions)) o.DossierDefinitions = Path.GetFullPath(o.DossierDefinitions, racine);
    if (!string.IsNullOrWhiteSpace(o.Courriel.DossierDepot)) o.Courriel.DossierDepot = Path.GetFullPath(o.Courriel.DossierDepot, racine);
});

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
});
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

// Sécurité : jeton Bearer obtenu par authentification Windows et, optionnellement, appartenance à un groupe AD.
var securite = builder.Configuration.GetSection(OptionsSecurite.Section).Get<OptionsSecurite>() ?? new OptionsSecurite();
if (securite.Active)
    builder.Services.AjouterSecurite(securite);

var app = builder.Build();

app.UseExceptionHandler(e => e.Run(async contexte =>
{
    var erreur = contexte.Features.Get<IExceptionHandlerFeature>()?.Error;
    var (statut, titre, details) = erreur switch
    {
        ErreurPilotage p => (p.StatutHttp, p.Message, p.Details),
        BadHttpRequestException b => (400, b.Message, (IReadOnlyList<string>)[]),
        System.Text.Json.JsonException j => (400, $"JSON invalide : {j.Message}", []),
        InvalidDataException d => (400, d.Message, []),
        _ => (500, "Erreur technique.", [])
    };
    contexte.Response.StatusCode = statut;
    await Results.Problem(title: titre, statusCode: statut,
        extensions: details.Count > 0 ? new Dictionary<string, object?> { ["details"] = details } : null).ExecuteAsync(contexte);
}));

if (securite.Active)
{
    app.UseAuthentication();
    app.UseAuthorization();
}

app.UseDefaultFiles();
app.UseStaticFiles();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.MapAuth(securite, app.Environment);

var api = app.MapGroup("/api");
if (securite.Active) api.RequireAuthorization(Securite.Politique);

// Erreurs fonctionnelles (400/404/409) : réponse ProblemDetails sans journaliser d'erreur technique.
api.AddEndpointFilter(async (contexte, suivant) =>
{
    try
    {
        return await suivant(contexte);
    }
    catch (ErreurPilotage p)
    {
        return Results.Problem(title: p.Message, statusCode: p.StatutHttp,
            extensions: p.Details.Count > 0 ? new Dictionary<string, object?> { ["details"] = p.Details } : null);
    }
    catch (InvalidDataException d)
    {
        return Results.Problem(title: d.Message, statusCode: 400);
    }
});

api.MapDefinitions();
api.MapInstances();

// Application Vue (routage côté client); une route d'API inconnue reste un 404.
app.MapFallback("/api/{**chemin}", () => Results.NotFound());
app.MapFallbackToFile("index.html");

app.Run();

public partial class Program;
