# Génère oim-creation.sql : création complète de la base OIM par un DBA.
#
# Les scripts DurableTask sont ceux intégrés au paquet Microsoft.DurableTask.SqlServer (exactement ce
# que le fournisseur exécute au premier démarrage); le schéma oim provient de OIM.Moteur.
# À relancer à chaque mise à jour du paquet : le DBA exécute alors le nouveau script (idempotent).
#
#   .\Generer-ScriptBase.ps1 [-Schema dt]

param([string] $Schema = 'dt')

$ErrorActionPreference = 'Stop'
$racine = Split-Path $PSScriptRoot -Parent
$projet = Join-Path $racine 'sources\OIM.Moteur\OIM.Moteur.csproj'

$version = ([xml](Get-Content $projet)).Project.ItemGroup.PackageReference |
    Where-Object { $_.Include -eq 'Microsoft.DurableTask.SqlServer' } | Select-Object -ExpandProperty Version
$dll = Join-Path $env:USERPROFILE ".nuget\packages\microsoft.durabletask.sqlserver\$version\lib\netstandard2.0\DurableTask.SqlServer.dll"
if (-not (Test-Path $dll)) { throw "Paquet absent ($dll) : exécuter « dotnet restore » d'abord." }

$assembly = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes($dll))
$versionComplete = ($assembly.GetCustomAttributes([Reflection.AssemblyInformationalVersionAttribute], $false))[0].InformationalVersion

function Ressource([string] $nom) {
    $flux = $assembly.GetManifestResourceStream("DurableTask.SqlServer.Scripts.$nom")
    $texte = (New-Object IO.StreamReader($flux)).ReadToEnd()
    return $texte.Replace('__SchemaNamePlaceholder__', $Schema).TrimEnd()
}

# Scripts de schéma dans l'ordre des versions, puis logique et permissions (comme SqlDbManager).
$schemas = $assembly.GetManifestResourceNames() |
    Where-Object { $_ -match '\.schema-(\d+\.\d+\.\d+)\.sql$' } |
    ForEach-Object { [pscustomobject]@{ Version = [version]$Matches[1]; Nom = $_ -replace '^DurableTask\.SqlServer\.Scripts\.', '' } } |
    Sort-Object Version

$sortie = New-Object Text.StringBuilder
function Ajouter([string] $texte) { [void]$sortie.AppendLine($texte) }
function Section([string] $titre, [string] $texte) {
    Ajouter "-- ═══════════════════════════════════════════════════════════════════════════"
    Ajouter "-- $titre"
    Ajouter "-- ═══════════════════════════════════════════════════════════════════════════"
    Ajouter $texte
    Ajouter "GO"
    Ajouter ""
}

Ajouter @"
-- Création complète de la base OIM (DurableTask SQL Server $versionComplete + schéma oim).
-- Généré par base-de-donnees/Generer-ScriptBase.ps1 — ne pas modifier à la main.
--
-- À exécuter par un DBA (db_owner) dans la base cible, avec sqlcmd ou SSMS. Le script est
-- idempotent : il sert aussi à mettre à niveau une base existante après une mise à jour du paquet.
--
-- Créer la base avec la même collation que le fournisseur DurableTask (les données JSON sont
-- stockées en varchar : UTF-8 requis pour conserver tous les caractères) :
--     CREATE DATABASE [OIM] COLLATE Latin1_General_100_BIN2_UTF8;
--
-- Ensuite, donner au compte de l'application (ex. pool IIS) le rôle oim_application :
--     CREATE USER [DOMAINE\CompteApp] FOR LOGIN [DOMAINE\CompteApp];
--     ALTER ROLE oim_application ADD MEMBER [DOMAINE\CompteApp];
-- et configurer l'application avec Oim:CreerBaseSiAbsente = false.

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF CONVERT(sysname, DATABASEPROPERTYEX(DB_NAME(), 'Collation')) NOT LIKE '%UTF8'
    RAISERROR('Avertissement : la collation de la base n''est pas UTF-8 (attendu : Latin1_General_100_BIN2_UTF8). Les caractères hors Windows-1252 seront altérés dans les données des instances.', 10, 1) WITH NOWAIT;
GO

"@

foreach ($s in $schemas) { Section "DurableTask : $($s.Nom)" (Ressource $s.Nom) }
Section 'DurableTask : logic.sql' (Ressource 'logic.sql')
Section "DurableTask : permissions.sql (rôle ${Schema}_runtime)" (Ressource 'permissions.sql')

Section 'DurableTask : version du schéma et mode du task hub' @"
-- Version reconnue par le fournisseur : au démarrage, il ne tente aucune mise à niveau si elle est à jour.
EXEC $Schema._UpdateVersion @SemanticVersion = '$versionComplete';

-- Task hub déterminé par le nom d'application (Oim:TaskHubParApplication = true) plutôt que par
-- l'utilisateur SQL. Réservé à un administrateur : l'application ne peut pas le faire elle-même.
EXEC $Schema.SetGlobalSetting @Name = 'TaskHubMode', @Value = 0;
"@

$oim = Get-Content (Join-Path $racine 'sources\OIM.Moteur\Stockage\SchemaOim.sql') -Raw -Encoding UTF8
Section 'OIM : schéma oim (sources/OIM.Moteur/Stockage/SchemaOim.sql)' $oim.TrimEnd()

Section "Rôle de l'application : oim_application" @"
IF DATABASE_PRINCIPAL_ID('oim_application') IS NULL
    CREATE ROLE oim_application;

-- Moteur DurableTask (procédures stockées du fournisseur).
ALTER ROLE ${Schema}_runtime ADD MEMBER oim_application;

-- Suivi des instances (tableau de bord) : lecture directe des vues DurableTask.
GRANT SELECT ON OBJECT::$Schema.vInstances TO oim_application;
GRANT SELECT ON OBJECT::$Schema.vHistory TO oim_application;

-- Définitions de processus et brouillons de tests.
GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::oim TO oim_application;
"@

$fichier = Join-Path $PSScriptRoot 'oim-creation.sql'
[IO.File]::WriteAllText($fichier, $sortie.ToString(), (New-Object Text.UTF8Encoding($true)))
Write-Host "Écrit : $fichier (DurableTask $versionComplete, schéma $Schema)"
