-- Schéma propre à OIM (équipes, définitions de processus versionnées).
-- Les instances, l'historique et les files sont gérés par DurableTask dans le schéma « dt ».
-- Script idempotent, exécuté au démarrage.

IF SCHEMA_ID('oim') IS NULL
    EXEC('CREATE SCHEMA oim');

-- Base créée avant les équipes : les ids de processus et d'instances ne sont pas qualifiés.
IF OBJECT_ID('oim.Definitions') IS NOT NULL AND COL_LENGTH('oim.Definitions', 'Equipe') IS NULL
    THROW 50001, 'Schéma oim antérieur aux équipes : recréer la base (aucune migration automatique).', 1;

-- Équipes : chacune a ses processus et ses instances, invisibles des autres (sauf admin et support).
-- Id : [a-z0-9-], sans point (séparateur des ids qualifiés « equipe.processus »).
IF OBJECT_ID('oim.Equipes') IS NULL
CREATE TABLE oim.Equipes (
    Id           varchar(50)    NOT NULL CONSTRAINT PK_Equipes PRIMARY KEY,
    Nom          nvarchar(200)  NOT NULL,
    Description  nvarchar(1000) NULL,
    Actif        bit            NOT NULL CONSTRAINT DF_Equipes_Actif DEFAULT (1),
    CreeLe       datetime2(3)   NOT NULL CONSTRAINT DF_Equipes_CreeLe DEFAULT (SYSUTCDATETIME())
);

-- Membres : sujets tels qu'ils apparaissent dans les jetons (compte ou groupe AD « DOMAINE\nom »,
-- groupe ou rôle d'un fournisseur externe, avec son préfixe).
IF OBJECT_ID('oim.EquipeMembres') IS NULL
CREATE TABLE oim.EquipeMembres (
    EquipeId  varchar(50)    NOT NULL,
    Sujet     nvarchar(256)  NOT NULL,
    CONSTRAINT PK_EquipeMembres PRIMARY KEY (EquipeId, Sujet),
    CONSTRAINT FK_EquipeMembres_Equipes FOREIGN KEY (EquipeId) REFERENCES oim.Equipes (Id) ON DELETE CASCADE
);

-- Id qualifié « equipe.processus ».
IF OBJECT_ID('oim.Definitions') IS NULL
CREATE TABLE oim.Definitions (
    Id               varchar(160)   NOT NULL CONSTRAINT PK_Definitions PRIMARY KEY,
    Equipe           varchar(50)    NOT NULL CONSTRAINT FK_Definitions_Equipes REFERENCES oim.Equipes (Id),
    Nom              nvarchar(300)  NULL,
    Description      nvarchar(2000) NULL,
    VersionCourante  int            NOT NULL,
    Actif            bit            NOT NULL CONSTRAINT DF_Definitions_Actif DEFAULT (1),
    CreeLe           datetime2(3)   NOT NULL CONSTRAINT DF_Definitions_CreeLe DEFAULT (SYSUTCDATETIME()),
    ModifieLe        datetime2(3)   NOT NULL CONSTRAINT DF_Definitions_ModifieLe DEFAULT (SYSUTCDATETIME())
);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Definitions_Equipe' AND object_id = OBJECT_ID('oim.Definitions'))
    CREATE INDEX IX_Definitions_Equipe ON oim.Definitions (Equipe);

IF OBJECT_ID('oim.DefinitionVersions') IS NULL
CREATE TABLE oim.DefinitionVersions (
    DefinitionId  varchar(160)   NOT NULL,
    Version       int            NOT NULL,
    Yaml          nvarchar(max)  NOT NULL,
    Empreinte     char(64)       NOT NULL,
    DeployePar    nvarchar(200)  NULL,
    DeployeLe     datetime2(3)   NOT NULL CONSTRAINT DF_DefinitionVersions_DeployeLe DEFAULT (SYSUTCDATETIME()),
    Commentaire   nvarchar(1000) NULL,
    CONSTRAINT PK_DefinitionVersions PRIMARY KEY (DefinitionId, Version),
    CONSTRAINT FK_DefinitionVersions_Definitions FOREIGN KEY (DefinitionId) REFERENCES oim.Definitions (Id)
);

-- Clé ≤ 900 octets : 160 + 4 + 2 × 360.
IF OBJECT_ID('oim.DefinitionFichiers') IS NULL
CREATE TABLE oim.DefinitionFichiers (
    DefinitionId  varchar(160)   NOT NULL,
    Version       int            NOT NULL,
    Chemin        nvarchar(360)  NOT NULL,
    Contenu       nvarchar(max)  NOT NULL,
    CONSTRAINT PK_DefinitionFichiers PRIMARY KEY (DefinitionId, Version, Chemin),
    CONSTRAINT FK_DefinitionFichiers_Versions FOREIGN KEY (DefinitionId, Version) REFERENCES oim.DefinitionVersions (DefinitionId, Version)
);

-- Paquets en cours de test (tests métier) : lisibles par tous les serveurs pendant l'exécution,
-- supprimés à la fin. Id « ~equipe.processus~xxxxxxxx » (jamais un id de processus valide).
IF OBJECT_ID('oim.Brouillons') IS NULL
CREATE TABLE oim.Brouillons (
    Id        varchar(200)   NOT NULL CONSTRAINT PK_Brouillons PRIMARY KEY,
    Yaml      nvarchar(max)  NOT NULL,
    Fichiers  nvarchar(max)  NOT NULL,
    CreeLe    datetime2(3)   NOT NULL CONSTRAINT DF_Brouillons_CreeLe DEFAULT (SYSUTCDATETIME())
);
