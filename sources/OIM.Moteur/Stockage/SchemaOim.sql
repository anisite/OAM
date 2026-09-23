-- Schéma propre à OIM (définitions de processus versionnées).
-- Les instances, l'historique et les files sont gérés par DurableTask dans le schéma « dt ».
-- Script idempotent, exécuté au démarrage.

IF SCHEMA_ID('oim') IS NULL
    EXEC('CREATE SCHEMA oim');

IF OBJECT_ID('oim.Definitions') IS NULL
CREATE TABLE oim.Definitions (
    Id               varchar(100)   NOT NULL CONSTRAINT PK_Definitions PRIMARY KEY,
    Nom              nvarchar(300)  NULL,
    Description      nvarchar(2000) NULL,
    VersionCourante  int            NOT NULL,
    Actif            bit            NOT NULL CONSTRAINT DF_Definitions_Actif DEFAULT (1),
    CreeLe           datetime2(3)   NOT NULL CONSTRAINT DF_Definitions_CreeLe DEFAULT (SYSUTCDATETIME()),
    ModifieLe        datetime2(3)   NOT NULL CONSTRAINT DF_Definitions_ModifieLe DEFAULT (SYSUTCDATETIME())
);

IF OBJECT_ID('oim.DefinitionVersions') IS NULL
CREATE TABLE oim.DefinitionVersions (
    DefinitionId  varchar(100)   NOT NULL,
    Version       int            NOT NULL,
    Yaml          nvarchar(max)  NOT NULL,
    Empreinte     char(64)       NOT NULL,
    DeployePar    nvarchar(200)  NULL,
    DeployeLe     datetime2(3)   NOT NULL CONSTRAINT DF_DefinitionVersions_DeployeLe DEFAULT (SYSUTCDATETIME()),
    Commentaire   nvarchar(1000) NULL,
    CONSTRAINT PK_DefinitionVersions PRIMARY KEY (DefinitionId, Version),
    CONSTRAINT FK_DefinitionVersions_Definitions FOREIGN KEY (DefinitionId) REFERENCES oim.Definitions (Id)
);

IF OBJECT_ID('oim.DefinitionFichiers') IS NULL
CREATE TABLE oim.DefinitionFichiers (
    DefinitionId  varchar(100)   NOT NULL,
    Version       int            NOT NULL,
    Chemin        nvarchar(400)  NOT NULL,
    Contenu       nvarchar(max)  NOT NULL,
    CONSTRAINT PK_DefinitionFichiers PRIMARY KEY (DefinitionId, Version, Chemin),
    CONSTRAINT FK_DefinitionFichiers_Versions FOREIGN KEY (DefinitionId, Version) REFERENCES oim.DefinitionVersions (DefinitionId, Version)
);

-- Paquets en cours de test (tests métier) : lisibles par tous les serveurs pendant l'exécution,
-- supprimés à la fin. L'id commence par « ~ » (jamais un id de processus valide).
IF OBJECT_ID('oim.Brouillons') IS NULL
CREATE TABLE oim.Brouillons (
    Id        varchar(150)   NOT NULL CONSTRAINT PK_Brouillons PRIMARY KEY,
    Yaml      nvarchar(max)  NOT NULL,
    Fichiers  nvarchar(max)  NOT NULL,
    CreeLe    datetime2(3)   NOT NULL CONSTRAINT DF_Brouillons_CreeLe DEFAULT (SYSUTCDATETIME())
);
