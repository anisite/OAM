using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using OAM.Api.Auth;
using OAM.Api.Features.Auth;
using OAM.Api.Features.Definitions;
using OAM.Api.Features.Instances;
using OAM.Api.Features.Mocks;
using OAM.Api.Features.Tests;
using OAM.Api.Hubs;
using OAM.Domain.Interfaces;
using OAM.Infrastructure;
using OAM.Infrastructure.Data;
using OAM.Workflow.Core;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Configuration
var jwtSettings = builder.Configuration.GetSection("Jwt").Get<JwtSettings>()!;
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("Jwt"));

// Authentification : NTLM (pour /api/auth/token) + JWT Bearer (partout ailleurs)
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSettings.Issuer,
        ValidAudience = jwtSettings.Audience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Secret))
    };

    // Support SignalR : le token est passé en query string
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            var path = context.HttpContext.Request.Path;
            if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
            {
                context.Token = accessToken;
            }
            return Task.CompletedTask;
        }
    };
})
.AddNegotiate();

builder.Services.AddAuthorization();

// Infrastructure & Workflow Core
var connectionString = builder.Configuration.GetConnectionString("OamDb")!;
var useSqlite = connectionString.Contains(".db", StringComparison.OrdinalIgnoreCase)
                || connectionString.StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase)
                   && !connectionString.Contains("Server", StringComparison.OrdinalIgnoreCase);
builder.Services.AddOamInfrastructure(connectionString, useSqlite);
builder.Services.AddOamWorkflowCore();

// SignalR
builder.Services.AddSignalR();
builder.Services.AddSingleton<INotificateurWorkflow, SignalRNotificateur>();

// Minimal API — sérialisation des enums en string
builder.Services.ConfigureHttpJsonOptions(opts =>
    opts.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddOpenApi();

// CORS pour le frontend Vue.js
builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        policy.WithOrigins(
                builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:5173"])
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

var app = builder.Build();

// Charger la configuration des clients HTTP
var configHttp = app.Services.GetRequiredService<OAM.Workflow.Core.Connecteurs.ConfigurationYamlHttp>();
var httpClientsEnv = Path.Combine(Directory.GetCurrentDirectory(), $"http-clients.{app.Environment.EnvironmentName}.yml");
var httpClientsBase = Path.Combine(Directory.GetCurrentDirectory(), "http-clients.yml");
var httpClientsPath = File.Exists(httpClientsEnv) ? httpClientsEnv : httpClientsBase;
if (File.Exists(httpClientsPath))
{
    //configHttp.ChargerDepuisYaml(File.ReadAllText(httpClientsPath));
    app.Logger.LogInformation("Clients HTTP chargés depuis {Path}", httpClientsPath);
}

// En développement : créer la BD et seeder les mocks automatiquement
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<OamDbContext>();
    db.Database.EnsureCreated();
    app.MapOpenApi();
    app.MapScalarApiReference(options => options.WithTitle("OAM — Orchestrateur d'Actions Métier"));

    // Auto-seed des mocks depuis seed-mocks.json
    var seedPath = Path.Combine(AppContext.BaseDirectory, "seed-mocks.json");
    if (!File.Exists(seedPath))
        seedPath = Path.Combine(Directory.GetCurrentDirectory(), "seed-mocks.json");
    if (File.Exists(seedPath))
    {
        var gestionnaire = app.Services.GetRequiredService<OAM.Workflow.Core.Engine.GestionnaireMock>();
        var entries = System.Text.Json.JsonSerializer.Deserialize<List<SeedMockEntry>>(
            File.ReadAllText(seedPath),
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        entries?.ForEach(e => gestionnaire.AjouterMock(e.Id, OAM.Workflow.Core.Engine.GestionnaireMock.Global, e.Condition, e.ReponseJson));
        app.Logger.LogInformation("Mocks seedés depuis {Path} ({Count} entrées)", seedPath, entries?.Count ?? 0);
    }
}

app.UseCors("Frontend");
app.UseAuthentication();
app.UseAuthorization();

// Servir les fichiers statiques du frontend (build Vue.js dans wwwroot)
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapAuth();
app.MapDefinitions();
app.MapInstances();
app.MapMocks();
app.MapTests();
app.MapHub<WorkflowHub>("/hubs/workflow");

// Fallback SPA : renvoyer index.html pour les routes Vue.js
app.MapFallbackToFile("index.html");

app.Run();

internal record SeedMockEntry(string Id, string? Condition, string ReponseJson);
