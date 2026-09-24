-- Création complète de la base OIM (DurableTask SQL Server 1.8.1+e7d35db87d50c9269af135441c823b24c083bce8 + schéma oim).
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

-- ═══════════════════════════════════════════════════════════════════════════
-- DurableTask : schema-1.0.0.sql
-- ═══════════════════════════════════════════════════════════════════════════
-- Copyright (c) Microsoft Corporation.
-- Licensed under the MIT License.

-- PERSISTENT SCHEMA OBJECTS (tables, indexes, etc.)
--
-- The contents of this file must never be changed after
-- being published. Any schema changes must be done in
-- new schema-{major}.{minor}.{patch}.sql scripts.

-- All objects must be created under the "dt" schema or under a custom schema.
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'dt')
    EXEC('CREATE SCHEMA dt');

-- Create tables

-- Rule #1: Use varchar instead of nvarchar
-- Rule #2: Do not use varchar(MAX) except in the Payloads table
-- Rule #3: Try to follow existing naming and ordering conventions

IF OBJECT_ID(N'dt.Versions', 'U') IS NULL
BEGIN
    CREATE TABLE dt.Versions (
        SemanticVersion varchar(100) NOT NULL CONSTRAINT PK_Versions_SemanticVersion PRIMARY KEY WITH (IGNORE_DUP_KEY = ON),
        UpgradeTime datetime2 NOT NULL CONSTRAINT DF_Versions_UpgradeTime DEFAULT SYSUTCDATETIME()
    )
END
GO

IF OBJECT_ID(N'dt.Payloads', 'U') IS NULL
BEGIN
    CREATE TABLE dt.Payloads (
        [TaskHub] varchar(50) NOT NULL,
        [InstanceID] varchar(100) NOT NULL,
        [PayloadID] uniqueidentifier NOT NULL,
        [Text] varchar(max) NULL,
        [Reason] varchar(max) NULL,
        -- NOTE: no FK constraint to Instances table because we want to allow events to create new instances

        CONSTRAINT PK_Payloads PRIMARY KEY (TaskHub, InstanceID, PayloadID),
    )
END
GO

IF OBJECT_ID(N'dt.Instances', 'U') IS NULL
BEGIN
	CREATE TABLE dt.Instances (
		[TaskHub] varchar(50) NOT NULL,
        [InstanceID] varchar(100) NOT NULL,
		[ExecutionID] varchar(50) NOT NULL CONSTRAINT DF_Instances_ExecutionID DEFAULT (NEWID()), -- expected to be system generated
        [Name] varchar(300) NOT NULL, -- the type name of the orchestration or entity
        [Version] varchar(100) NULL, -- the version of the orchestration (optional)
		[CreatedTime] datetime2 NOT NULL CONSTRAINT DF_Instances_CreatedTime DEFAULT SYSUTCDATETIME(),
		[LastUpdatedTime] datetime2 NULL,
        [CompletedTime] datetime2 NULL,
		[RuntimeStatus] varchar(20) NOT NULL,
        [LockedBy] varchar(100) NULL,
        [LockExpiration] datetime2 NULL,
		[InputPayloadID] uniqueidentifier NULL,
		[OutputPayloadID] uniqueidentifier NULL,
		[CustomStatusPayloadID] uniqueidentifier NULL,
        [ParentInstanceID] varchar(100) NULL,

        CONSTRAINT PK_Instances PRIMARY KEY (TaskHub, InstanceID),
        -- NOTE: No FK constraints for the Payloads table because of high performance cost and deadlock risk
	)

    -- This index is used by LockNext and Purge logic
    CREATE INDEX IX_Instances_RuntimeStatus ON dt.Instances(TaskHub, RuntimeStatus)
        INCLUDE ([LockExpiration], [CreatedTime], [CompletedTime])
    
    -- This index is intended to help the performance of multi-instance query
    CREATE INDEX IX_Instances_CreatedTime ON dt.Instances(TaskHub, CreatedTime)
        INCLUDE ([RuntimeStatus], [CompletedTime], [InstanceID])
END

IF OBJECT_ID(N'dt.NewEvents', 'U') IS NULL
BEGIN
    CREATE TABLE dt.NewEvents (
        [SequenceNumber] bigint IDENTITY NOT NULL, -- order is important for FIFO
        [Timestamp] datetime2 NOT NULL CONSTRAINT DF_NewEvents_Timestamp DEFAULT SYSUTCDATETIME(),
        [VisibleTime] datetime2 NULL, -- for scheduled messages
        [DequeueCount] int NOT NULL CONSTRAINT DF_NewEvents_DequeueCount DEFAULT 0,
		[TaskHub] varchar(50) NOT NULL,
        [InstanceID] varchar(100) NOT NULL,
        [ExecutionID] varchar(50) NULL,
        [EventType] varchar(40) NOT NULL,
        [RuntimeStatus] varchar(30) NULL,
        [Name] varchar(300) NULL,
        [TaskID] int NULL,
        [PayloadID] uniqueidentifier NULL,

        CONSTRAINT PK_NewEvents PRIMARY KEY (TaskHub, InstanceID, SequenceNumber),
        -- NOTE: no FK constraint to Instances and Payloads tables because of high performance cost and deadlock risk.
        --       Also, we want to allow events to create new instances, which means an Instances row might not yet exist.
    )
END

IF OBJECT_ID(N'dt.History', 'U') IS NULL
BEGIN
    CREATE TABLE dt.History (
        [TaskHub] varchar(50) NOT NULL,
        [InstanceID] varchar(100) NOT NULL,
	    [ExecutionID] varchar(50) NOT NULL,
        [SequenceNumber] bigint NOT NULL,
	    [EventType] varchar(40) NOT NULL,
	    [TaskID] int NULL,
	    [Timestamp] datetime2 NOT NULL CONSTRAINT DF_History_Timestamp DEFAULT SYSUTCDATETIME(),
	    [IsPlayed] bit NOT NULL CONSTRAINT DF_History_IsPlayed DEFAULT 0,
	    [Name] varchar(300) NULL,
	    [RuntimeStatus] varchar(20) NULL,
        [VisibleTime] datetime2 NULL,
	    [DataPayloadID] uniqueidentifier NULL,

        CONSTRAINT PK_History PRIMARY KEY (TaskHub, InstanceID, ExecutionID, SequenceNumber),
        -- NOTE: no FK constraint to Payloads or Instances tables because of high performance cost and deadlock risk
    )
END

IF OBJECT_ID(N'dt.NewTasks', 'U') IS NULL
BEGIN
    CREATE TABLE dt.NewTasks (
        [TaskHub] varchar(50) NOT NULL,
        [SequenceNumber] bigint IDENTITY NOT NULL,  -- order is important for FIFO
        [InstanceID] varchar(100) NOT NULL,
        [ExecutionID] varchar(50) NULL,
        [Name] varchar(300) NULL,
        [TaskID] int NOT NULL,
        [Timestamp] datetime2 NOT NULL CONSTRAINT DF_NewTasks_Timestamp DEFAULT SYSUTCDATETIME(),
        [VisibleTime] datetime2 NULL,
        [DequeueCount] int NOT NULL CONSTRAINT DF_NewTasks_DequeueCount DEFAULT 0,
        [LockedBy] varchar(100) NULL,
        [LockExpiration] datetime2 NULL,
        [PayloadID] uniqueidentifier NULL,
        [Version] varchar(100) NULL,

        CONSTRAINT PK_NewTasks PRIMARY KEY (TaskHub, SequenceNumber),
        -- NOTE: no FK constraint to Payloads or Instances tables because of high performance cost and deadlock risk
    )

    -- This index is used by vScaleHints
    CREATE NONCLUSTERED INDEX IX_NewTasks_InstanceID ON dt.NewTasks(TaskHub, InstanceID)
        INCLUDE ([SequenceNumber], [Timestamp], [LockExpiration], [VisibleTime])
END
GO

IF OBJECT_ID(N'dt.GlobalSettings', 'U') IS NULL
BEGIN
    CREATE TABLE dt.GlobalSettings (
        [Name] varchar(300) NOT NULL PRIMARY KEY,
        [Value] sql_variant NULL,
        [Timestamp] datetime2 NOT NULL CONSTRAINT DF_GlobalSettings_Timestamp DEFAULT SYSUTCDATETIME(),
        [LastModifiedBy] nvarchar(128) NOT NULL CONSTRAINT DF_GlobalSettings_LastModifiedby DEFAULT USER_NAME()
    )
    
    -- Default task hub mode is 1, or "User ID"
    INSERT INTO dt.GlobalSettings ([Name], [Value]) VALUES ('TaskHubMode', 1)
END
GO
GO

-- ═══════════════════════════════════════════════════════════════════════════
-- DurableTask : schema-1.2.0.sql
-- ═══════════════════════════════════════════════════════════════════════════
-- Copyright (c) Microsoft Corporation.
-- Licensed under the MIT License.

-- PERSISTENT SCHEMA OBJECTS (tables, indexes, types, etc.)
--
-- The contents of this file must never be changed after
-- being published. Any schema changes must be done in
-- new schema-{major}.{minor}.{patch}.sql scripts.

-- Add a new TraceContext column to the Instances table
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dt.Instances') AND name = 'TraceContext')
    ALTER TABLE dt.Instances ADD TraceContext varchar(800) NULL

-- Add a new TraceContext column to the History table
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dt.History') AND name = 'TraceContext')
    ALTER TABLE dt.History ADD TraceContext varchar(800) NULL

-- Add a new TraceContext column to the NewEvents table
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dt.NewEvents') AND name = 'TraceContext')
    ALTER TABLE dt.NewEvents ADD TraceContext varchar(800) NULL

-- Add a new TraceContext column to the NewTasks table
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dt.NewTasks') AND name = 'TraceContext')
    ALTER TABLE dt.NewTasks ADD TraceContext varchar(800) NULL

-- Drop custom types that have schema changes. They will be recreated in logic.sql, which executes last.
-- In this release, we have changes to HistoryEvents, OrchestrationEvents, and TaskEvents to add TraceContext fields.
-- In order to drop the types, we must first drop all stored procedures that depend on them, and then drop the types.
-- One way to discover all the stored procs that depend on the types is to query sys.sql_expression_dependencies
-- (credit to https://www.mssqltips.com/sqlservertip/6114/how-to-alter-user-defined-table-type-in-sql-server/):

/*
    SELECT DISTINCT [types].name FROM (
	    SELECT s.name as [schema], o.name, def = OBJECT_DEFINITION(d.referencing_id), d.referenced_entity_name
	      FROM sys.sql_expression_dependencies AS d
	      INNER JOIN sys.objects AS o
		     ON d.referencing_id = o.[object_id]
	      INNER JOIN sys.schemas AS s
		     ON o.[schema_id] = s.[schema_id]
	      WHERE d.referenced_database_name IS NULL
		    AND d.referenced_class_desc = 'TYPE'
            AND d.referenced_entity_name IN ('HistoryEvents', 'OrchestrationEvents', 'TaskEvents')
    ) [types]
*/

-- First, drop the referencing stored procedures
IF OBJECT_ID('dt._AddOrchestrationEvents') IS NOT NULL
    DROP PROCEDURE dt._AddOrchestrationEvents
IF OBJECT_ID('dt._CheckpointOrchestration') IS NOT NULL
    DROP PROCEDURE dt._CheckpointOrchestration
IF OBJECT_ID('dt._CompleteTasks') IS NOT NULL
    DROP PROCEDURE dt._CompleteTasks

-- Next, drop the types that we are changing
IF TYPE_ID('dt.HistoryEvents') IS NOT NULL
    DROP TYPE dt.HistoryEvents
IF TYPE_ID('dt.OrchestrationEvents') IS NOT NULL
    DROP TYPE dt.OrchestrationEvents
IF TYPE_ID('dt.TaskEvents') IS NOT NULL
    DROP TYPE dt.TaskEvents
GO

-- ═══════════════════════════════════════════════════════════════════════════
-- DurableTask : schema-1.6.0.sql
-- ═══════════════════════════════════════════════════════════════════════════
-- Copyright (c) Microsoft Corporation.
-- Licensed under the MIT License.

-- PERSISTENT SCHEMA OBJECTS (tables, indexes, types, etc.)
--
-- The contents of this file must never be changed after
-- being published. Any schema changes must be done in
-- new schema-{major}.{minor}.{patch}.sql scripts.

-- Add a new Tags column to the Instances table (JSON blob of string key-value pairs).
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dt.Instances') AND name = 'Tags')
    ALTER TABLE dt.Instances ADD [Tags] varchar(8000) NULL

-- Add a new Tags column to the NewTasks table so that orchestration tags
-- propagate to activity task workers via OrchestrationExecutionContext.
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dt.NewTasks') AND name = 'Tags')
    ALTER TABLE dt.NewTasks ADD [Tags] varchar(8000) NULL

-- Add Tags columns to the OrchestrationEvents and TaskEvents table types.
-- To change a type we must first drop all stored procedures that reference it,
-- then drop the type itself. The types and sprocs will be recreated by logic.sql
-- which executes afterwards.
IF OBJECT_ID('dt._AddOrchestrationEvents') IS NOT NULL
    DROP PROCEDURE dt._AddOrchestrationEvents
IF OBJECT_ID('dt._CheckpointOrchestration') IS NOT NULL
    DROP PROCEDURE dt._CheckpointOrchestration
IF OBJECT_ID('dt._CompleteTasks') IS NOT NULL
    DROP PROCEDURE dt._CompleteTasks

IF TYPE_ID('dt.OrchestrationEvents') IS NOT NULL
    DROP TYPE dt.OrchestrationEvents
IF TYPE_ID('dt.TaskEvents') IS NOT NULL
    DROP TYPE dt.TaskEvents
GO

-- ═══════════════════════════════════════════════════════════════════════════
-- DurableTask : logic.sql
-- ═══════════════════════════════════════════════════════════════════════════
-- Copyright (c) Microsoft Corporation.
-- Licensed under the MIT License.

-- Create custom types. This must be done before creating stored procedures.
-- IMPORTANT: If you make any changes to these types, you must also add a line in the schema upgrade script
--            (for example, schema-1.X.0) to drop them so that those changes are properly reflected.
IF TYPE_ID(N'dt.InstanceIDs') IS NULL
    CREATE TYPE dt.InstanceIDs AS TABLE (
        [InstanceID] varchar(100) NOT NULL
    )
GO

IF TYPE_ID(N'dt.MessageIDs') IS NULL
    -- WARNING: Reordering fields is a breaking change!
    CREATE TYPE dt.MessageIDs AS TABLE (
        [InstanceID] varchar(100) NULL,
        [SequenceNumber] bigint NULL
    )
GO

IF TYPE_ID(N'dt.HistoryEvents') IS NULL
    -- WARNING: Reordering fields is a breaking change!
    CREATE TYPE dt.HistoryEvents AS TABLE (
        [InstanceID] varchar(100) NULL,
        [ExecutionID] varchar(50) NULL,
        [SequenceNumber] bigint NULL,
        [EventType] varchar(40) NULL,
        [Name] varchar(300) NULL,
        [RuntimeStatus] varchar(30) NULL,
        [TaskID] int NULL,
        [Timestamp] datetime2 NULL,
        [IsPlayed] bit NULL,
        [VisibleTime] datetime2 NULL,
        [Reason] varchar(max) NULL,
        [PayloadText] varchar(max) NULL,
        [PayloadID] uniqueidentifier NULL,
        [ParentInstanceID] varchar(100) NULL,
        [Version] varchar(100) NULL,
        [TraceContext] varchar(800) NULL
    )
GO

IF TYPE_ID(N'dt.OrchestrationEvents') IS NULL
    -- WARNING: Reordering fields is a breaking change!
    CREATE TYPE dt.OrchestrationEvents AS TABLE (
        [InstanceID] varchar(100) NULL,
        [ExecutionID] varchar(50) NULL,
        [SequenceNumber] bigint NULL,
        [EventType] varchar(40) NULL,
        [Name] varchar(300) NULL,
        [RuntimeStatus] varchar(30) NULL,
        [TaskID] int NULL,
        [VisibleTime] datetime2 NULL,
        [Reason] varchar(max) NULL,
        [PayloadText] varchar(max) NULL,
        [PayloadID] uniqueidentifier NULL,
        [ParentInstanceID] varchar(100) NULL,
        [Version] varchar(100) NULL,
        [TraceContext] varchar(800) NULL,
        [Tags] varchar(8000) NULL
    )
GO

IF TYPE_ID(N'dt.TaskEvents') IS NULL
    -- WARNING: Reordering fields is a breaking change!
    CREATE TYPE dt.TaskEvents AS TABLE (
        [InstanceID] varchar(100) NULL,
        [ExecutionID] varchar(50) NULL,
        [Name] varchar(300) NULL,
        [EventType] varchar(40) NULL,
        [TaskID] int NULL,
        [VisibleTime] datetime2 NULL,
        [LockedBy] varchar(100) NULL,
        [LockExpiration] datetime2 NULL,
        [Reason] varchar(max) NULL,
        [PayloadText] varchar(max) NULL,
        [PayloadID] uniqueidentifier NULL,
        [Version] varchar(100) NULL,
        [TraceContext] varchar(800) NULL,
        [Tags] varchar(8000) NULL
    )
GO


CREATE OR ALTER FUNCTION dt.CurrentTaskHub()
    RETURNS varchar(50)
    WITH EXECUTE AS CALLER
AS
BEGIN
    -- Task Hub modes:
    -- 0: Task hub names are set by the app
    -- 1: Task hub names are inferred from the user credential
    DECLARE @taskHubMode sql_variant = (SELECT TOP 1 [Value] FROM GlobalSettings WHERE [Name] = 'TaskHubMode');

    DECLARE @taskHub varchar(150)

    IF @taskHubMode = 0
        SET @taskHub = APP_NAME()
    IF @taskHubMode = 1
        SET @taskHub = USER_NAME()

    IF @taskHub IS NULL
        SET @taskHub = 'default'

    -- if the name is too long, keep the first 16 characters and hash the rest
    IF LEN(@taskHub) > 50
        SET @taskHub = CONVERT(varchar(16), @taskHub) + '__' + CONVERT(varchar(32), HASHBYTES('MD5', @taskHub), 2)

    RETURN @taskHub
END
GO


CREATE OR ALTER FUNCTION dt.GetScaleMetric()
    RETURNS INT
    WITH EXECUTE AS CALLER
AS
BEGIN
    DECLARE @taskHub varchar(50) = dt.CurrentTaskHub()
    DECLARE @now datetime2 = SYSUTCDATETIME()

    DECLARE @liveInstances bigint = 0
    DECLARE @liveTasks bigint = 0

    SELECT
        @liveInstances = COUNT_BIG(DISTINCT E.[InstanceID]),
        @liveTasks = COUNT_BIG(T.[InstanceID])
    FROM Instances I WITH (NOLOCK)
        LEFT OUTER JOIN NewEvents E WITH (NOLOCK) ON E.[TaskHub] = @taskHub AND E.[InstanceID] = I.[InstanceID]
        LEFT OUTER JOIN NewTasks T WITH (NOLOCK) ON T.[TaskHub] = @taskHub AND T.[InstanceID] = I.[InstanceID]
    WHERE
        I.[TaskHub] = @taskHub
        AND I.[RuntimeStatus] IN ('Pending', 'Running')
        AND (E.[VisibleTime] IS NULL OR @now > E.[VisibleTime])

    RETURN @liveInstances + @liveTasks
END
GO


CREATE OR ALTER FUNCTION dt.GetScaleRecommendation(@MaxOrchestrationsPerWorker real, @MaxActivitiesPerWorker real)
    RETURNS INT
    WITH EXECUTE AS CALLER
AS
BEGIN
    DECLARE @taskHub varchar(50) = dt.CurrentTaskHub()
    DECLARE @now datetime2 = SYSUTCDATETIME()

    DECLARE @liveInstances bigint = 0
    DECLARE @liveTasks bigint = 0

    SELECT
        @liveInstances = COUNT_BIG(DISTINCT E.[InstanceID]),
        @liveTasks = COUNT_BIG(T.[InstanceID])
    FROM Instances I WITH (NOLOCK)
        LEFT OUTER JOIN NewEvents E WITH (NOLOCK) ON E.[TaskHub] = @taskHub AND E.[InstanceID] = I.[InstanceID]
        LEFT OUTER JOIN NewTasks T WITH (NOLOCK) ON T.[TaskHub] = @taskHub AND T.[InstanceID] = I.[InstanceID]
    WHERE
        I.[TaskHub] = @taskHub
        AND I.[RuntimeStatus] IN ('Pending', 'Running')
        AND (E.[VisibleTime] IS NULL OR E.[VisibleTime] < @now)

    IF @MaxOrchestrationsPerWorker < 1 SET @MaxOrchestrationsPerWorker = 1
    IF @MaxActivitiesPerWorker < 1 SET @MaxActivitiesPerWorker = 1

    DECLARE @recommendedWorkersForOrchestrations int = CEILING(@liveInstances / @MaxOrchestrationsPerWorker)
    DECLARE @recommendedWorkersForActivities int = CEILING(@liveTasks / @MaxActivitiesPerWorker)

    RETURN @recommendedWorkersForOrchestrations + @recommendedWorkersForActivities
END
GO


CREATE OR ALTER VIEW dt.vInstances
AS
    SELECT
        I.[TaskHub],
        I.[InstanceID],
        I.[ExecutionID],
        I.[Name],
        I.[Version],
        I.[CreatedTime],
        I.[LastUpdatedTime],
        I.[CompletedTime],
        I.[RuntimeStatus],
        I.[TraceContext],
        (SELECT TOP 1 [Text] FROM Payloads P WHERE
            P.[TaskHub] = dt.CurrentTaskHub() AND
            P.[InstanceID] = I.[InstanceID] AND
            P.[PayloadID] = I.[CustomStatusPayloadID]) AS [CustomStatusText],
        (SELECT TOP 1 [Text] FROM Payloads P WHERE
            P.[TaskHub] = dt.CurrentTaskHub() AND
            P.[InstanceID] = I.[InstanceID] AND
            P.[PayloadID] = I.[InputPayloadID]) AS [InputText],
        (SELECT TOP 1 [Text] FROM Payloads P WHERE 
            P.[TaskHub] = dt.CurrentTaskHub() AND
            P.[InstanceID] = I.[InstanceID] AND
            P.[PayloadID] = I.[OutputPayloadID]) AS [OutputText],
        I.[ParentInstanceID]
    FROM Instances I
    WHERE
        I.[TaskHub] = dt.CurrentTaskHub()
GO

CREATE OR ALTER VIEW dt.vHistory
AS
    SELECT
        H.[TaskHub],
        H.[InstanceID],
	    H.[ExecutionID],
        H.[SequenceNumber],
	    H.[EventType],
	    H.[TaskID],
	    H.[Timestamp],
	    H.[IsPlayed],
	    H.[Name],
	    H.[RuntimeStatus],
        H.[VisibleTime],
        H.[TraceContext],
	    (SELECT TOP 1 [Text] FROM Payloads P WHERE
            P.[TaskHub] = dt.CurrentTaskHub() AND
            P.[InstanceID] = H.[InstanceID] AND
            P.[PayloadID] = H.[DataPayloadID]) AS [Payload]
    FROM History H
    WHERE
        H.[TaskHub] = dt.CurrentTaskHub()
GO


CREATE OR ALTER PROCEDURE dt.CreateInstance
    @Name varchar(300),
    @Version varchar(100) = NULL,
    @InstanceID varchar(100) = NULL,
    @ExecutionID varchar(50) = NULL,
    @InputText varchar(MAX) = NULL,
    @StartTime datetime2 = NULL,
    @DedupeStatuses varchar(MAX) = 'Pending,Running',
    @TraceContext varchar(800) = NULL,
    @Tags varchar(8000) = NULL
AS
BEGIN
    DECLARE @TaskHub varchar(50) = dt.CurrentTaskHub()
    DECLARE @EventType varchar(30) = 'ExecutionStarted'
    DECLARE @RuntimeStatus varchar(30) = 'Pending'

    -- Check for instance ID collisions
    IF @InstanceID IS NULL
    BEGIN
        SET @InstanceID = NEWID()
    END
    ELSE
    BEGIN
        BEGIN TRANSACTION

        DECLARE @existingStatus varchar(30) = (
            SELECT TOP 1 existing.[RuntimeStatus]
            FROM Instances existing WITH (HOLDLOCK)
            WHERE [TaskHub] = @TaskHub AND [InstanceID] = @InstanceID
        )

        -- Instance IDs can be overwritten only if the orchestration is in a terminal state
        --IF @existingStatus IN ('Pending', 'Running')
        IF @existingStatus IN (SELECT value FROM STRING_SPLIT(@DedupeStatuses, ','))
        BEGIN
            DECLARE @msg nvarchar(4000) = FORMATMESSAGE('Cannot create instance with ID ''%s'' because a pending or running instance with ID already exists.', @InstanceID);
            ROLLBACK TRANSACTION;
            THROW 50001, @msg, 1;
        END
        ELSE IF @existingStatus IS NOT NULL
        BEGIN
            -- Purge the existing instance data so that it can be overwritten
            DECLARE @instancesToPurge InstanceIDs
            INSERT INTO @instancesToPurge VALUES (@InstanceID)
            EXEC dt.PurgeInstanceStateByID @instancesToPurge
        END

        COMMIT TRANSACTION
    END

    IF @ExecutionID IS NULL
    BEGIN
        SET @ExecutionID = NEWID()
    END

    BEGIN TRANSACTION
    
    -- *** IMPORTANT ***
    -- To prevent deadlocks, it is important to maintain consistent table access
    -- order across all stored procedures that execute within a transaction.
    -- Table order for this sproc: Payloads --> Instances --> NewEvents
    
    DECLARE @InputPayloadID uniqueidentifier
    IF @InputText IS NOT NULL
    BEGIN
        SET @InputPayloadID = NEWID()
        INSERT INTO Payloads ([TaskHub], [InstanceID], [PayloadID], [Text])
        VALUES (@TaskHub, @InstanceID, @InputPayloadID, @InputText)
    END

    INSERT INTO Instances (
        [Name],
        [Version],
        [TaskHub],
        [InstanceID],
        [ExecutionID],
        [RuntimeStatus],
        [InputPayloadID],
        [TraceContext],
        [Tags])
    VALUES (
        @Name,
        @Version,
        @TaskHub,
        @InstanceID,
        @ExecutionID,
        @RuntimeStatus,
        @InputPayloadID,
        @TraceContext,
        @Tags
    )

    INSERT INTO NewEvents (
        [Name],
        [TaskHub],
        [InstanceID],
        [ExecutionID],
        [RuntimeStatus],
        [VisibleTime],
        [EventType],
        [TraceContext],
        [PayloadID]
    ) VALUES (
        @Name,
        @TaskHub,
        @InstanceID,
        @ExecutionID,
        @RuntimeStatus,
        @StartTime,
        @EventType,
        @TraceContext,
        @InputPayloadID)

    COMMIT TRANSACTION
END
GO


CREATE OR ALTER PROCEDURE dt.GetInstanceHistory
    @InstanceID varchar(100),
    @GetInputsAndOutputs bit = 0
AS
BEGIN
    DECLARE @TaskHub varchar(50) = dt.CurrentTaskHub()
    DECLARE @ParentInstanceID varchar(100)
    DECLARE @Version varchar(100)
    DECLARE @Tags varchar(8000)
    
    SELECT
        @ParentInstanceID = [ParentInstanceID],
        @Version = [Version],
        @Tags = [Tags]
    FROM Instances WHERE [InstanceID] = @InstanceID

    SELECT
        H.[InstanceID],
        H.[ExecutionID],
        H.[SequenceNumber],
        H.[EventType],
        H.[Name],
        H.[RuntimeStatus],
        H.[TaskID],
        H.[Timestamp],
        H.[IsPlayed],
        H.[VisibleTime],
        P.[Reason],
        (CASE WHEN @GetInputsAndOutputs = 0 THEN NULL ELSE P.[Text] END) AS [PayloadText],
        [PayloadID],
        @ParentInstanceID as [ParentInstanceID],
        @Version as [Version],
        H.[TraceContext],
        @Tags as [Tags]
    FROM History H WITH (INDEX (PK_History))
        LEFT OUTER JOIN Payloads P ON
            P.[TaskHub] = @TaskHub AND
            P.[InstanceID] = H.[InstanceID] AND
            P.[PayloadID] = H.[DataPayloadID]
    WHERE
        H.[TaskHub] = @TaskHub AND
        H.[InstanceID] = @InstanceID
    ORDER BY H.[SequenceNumber] ASC
END
GO


CREATE OR ALTER PROCEDURE dt.RaiseEvent
    @Name varchar(300),
    @InstanceID varchar(100) = NULL,
    @PayloadText varchar(MAX) = NULL,
    @DeliveryTime datetime2 = NULL
AS
BEGIN
    BEGIN TRANSACTION

    DECLARE @TaskHub varchar(50) = dt.CurrentTaskHub()

    -- External event messages must target new instances or they must use
    -- the "auto start" instance ID format of @orchestrationname@identifier.
    IF NOT EXISTS (
        SELECT 1
        FROM Instances I
        WHERE [TaskHub] = @TaskHub AND I.[InstanceID] = @InstanceID)
    BEGIN
        INSERT INTO Instances (
            [TaskHub],
            [InstanceID],
            [ExecutionID],
            [Name],
            [Version],
            [RuntimeStatus])
        SELECT
            @TaskHub,
            @InstanceID,
            NEWID(),
            SUBSTRING(@InstanceID, 2, CHARINDEX('@', @InstanceID, 2) - 2),
            '',
            'Pending'
        WHERE LEFT(@InstanceID, 1) = '@' AND CHARINDEX('@', @InstanceID, 2) > 2

        -- The instance ID is not auto-start and doesn't already exist, so we fail.
        IF @@ROWCOUNT = 0
        BEGIN
          ROLLBACK TRANSACTION;
          THROW 50000, 'The instance does not exist.', 1; 
        END
    END

    -- Payloads are stored separately from the events
    DECLARE @PayloadID uniqueidentifier = NULL
    IF @PayloadText IS NOT NULL
    BEGIN
        SET @PayloadID = NEWID()
        INSERT INTO Payloads ([TaskHub], [InstanceID], [PayloadID], [Text])
        VALUES (@TaskHub, @InstanceID, @PayloadID, @PayloadText)
    END

    INSERT INTO NewEvents (
        [Name],
        [TaskHub],
        [InstanceID],
        [EventType],
        [VisibleTime],
        [PayloadID]
    ) VALUES (
        @Name,
        @TaskHub,
        @InstanceID,
        'EventRaised',
        @DeliveryTime,
        @PayloadID)

    COMMIT TRANSACTION

END
GO

CREATE OR ALTER PROCEDURE dt.TerminateInstance
    @InstanceID varchar(100),
    @Reason varchar(max) = NULL
AS
BEGIN
    BEGIN TRANSACTION

    -- *** IMPORTANT ***
    -- To prevent deadlocks, it is important to maintain consistent table access
    -- order across all stored procedures that execute within a transaction.
    -- Table order for this sproc: Instances --> (Payloads --> Instances --> NewEvents)  

    DECLARE @TaskHub varchar(50) = dt.CurrentTaskHub()

    DECLARE @existingStatus varchar(30)
    DECLARE @existingLockExpiration datetime2(7)

    -- Get the status of an existing orchestration
    SELECT TOP 1
        @existingStatus = existing.[RuntimeStatus],
        @existingLockExpiration = existing.[LockExpiration]
    FROM Instances existing WITH (HOLDLOCK)
    WHERE [TaskHub] = @TaskHub AND [InstanceID] = @InstanceID

    IF @existingStatus IS NULL
    BEGIN
        ROLLBACK TRANSACTION;
        THROW 50000, 'The instance does not exist.', 1;
    END

    DECLARE @now datetime2(7) = SYSUTCDATETIME()

    IF @existingStatus IN ('Running', 'Pending', 'Suspended')
    BEGIN
        -- Create a payload to store the reason, if any
        DECLARE @PayloadID uniqueidentifier = NULL
        IF @Reason IS NOT NULL
        BEGIN
            -- Note that we don't use the Reason column for the Reason with terminate events
            SET @PayloadID = NEWID()
            INSERT INTO Payloads ([TaskHub], [InstanceID], [PayloadID], [Text])
            VALUES (@TaskHub, @InstanceID, @PayloadID, @Reason)
        END

        -- Check the status of the orchestration to determine which termination path to take
        IF @existingStatus = 'Pending' AND (@existingLockExpiration IS NULL OR @existingLockExpiration <= @now)
        BEGIN
            -- The orchestration hasn't started yet - transition it directly to the Terminated state and delete
            -- any pending messages
            UPDATE Instances SET
                [RuntimeStatus] = 'Terminated',
                [LastUpdatedTime] = @now,
                [CompletedTime] = @now,
                [OutputPayloadID] = @PayloadID,
                [LockExpiration] = NULL -- release the lock, if any
            WHERE [TaskHub] = @TaskHub AND [InstanceID] = @InstanceID

            DELETE FROM NewEvents WHERE [TaskHub] = @TaskHub AND [InstanceID] = @InstanceID
        END
        ELSE
        BEGIN
            -- The orchestration has actually started running in this case
            IF NOT EXISTS (
                SELECT TOP (1) 1 FROM NewEvents
                WHERE [TaskHub] = @TaskHub AND [InstanceID] = @InstanceID AND [EventType] = 'ExecutionTerminated'
            )
            BEGIN
                INSERT INTO NewEvents (
                    [TaskHub],
                    [InstanceID],
                    [EventType],
                    [PayloadID]
                ) VALUES (
                    @TaskHub,
                    @InstanceID,
                    'ExecutionTerminated',
                    @PayloadID)
            END
        END
    END

    COMMIT TRANSACTION
END
GO


CREATE OR ALTER PROCEDURE dt.PurgeInstanceStateByID
    @InstanceIDs InstanceIDs READONLY
AS
BEGIN
    DECLARE @TaskHub varchar(50) = dt.CurrentTaskHub()

    BEGIN TRANSACTION

    DELETE FROM NewEvents WHERE [TaskHub] = @TaskHub AND [InstanceID] IN (SELECT [InstanceID] FROM @InstanceIDs)
    DELETE FROM NewTasks  WHERE [TaskHub] = @TaskHub AND [InstanceID] IN (SELECT [InstanceID] FROM @InstanceIDs)
    DELETE FROM Instances WHERE [TaskHub] = @TaskHub AND [InstanceID] IN (SELECT [InstanceID] FROM @InstanceIDs)
    DECLARE @deletedInstances int = @@ROWCOUNT
    DELETE FROM History  WHERE [TaskHub] = @TaskHub AND [InstanceID] IN (SELECT [InstanceID] FROM @InstanceIDs)
    DELETE FROM Payloads WHERE [TaskHub] = @TaskHub AND [InstanceID] IN (SELECT [InstanceID] FROM @InstanceIDs)

    COMMIT TRANSACTION

    -- return the number of deleted instances
    RETURN @deletedInstances
END
GO


CREATE OR ALTER PROCEDURE dt.PurgeInstanceStateByTime
    @ThresholdTime datetime2,
    @FilterType tinyint = 0
AS
BEGIN
    DECLARE @TaskHub varchar(50) = dt.CurrentTaskHub()

    DECLARE @instanceIDs InstanceIDs

    IF @FilterType = 0 -- created time
    BEGIN
        INSERT INTO @instanceIDs
            SELECT [InstanceID] FROM Instances
            WHERE [TaskHub] = @TaskHub AND [RuntimeStatus] IN ('Completed', 'Terminated', 'Failed')
                AND [CreatedTime] <= @ThresholdTime
    END
    ELSE IF @FilterType = 1 -- completed time
    BEGIN
        INSERT INTO @instanceIDs
            SELECT [InstanceID] FROM Instances
            WHERE [TaskHub] = @TaskHub AND [RuntimeStatus] IN ('Completed', 'Terminated', 'Failed')
                AND [CompletedTime] <= @ThresholdTime
    END
    ELSE
    BEGIN
        DECLARE @msg nvarchar(100) = FORMATMESSAGE('Unknown or unsupported filter type: %d', @FilterType);
        THROW 50000, @msg, 1;
    END

    DECLARE @deletedInstances int
    EXECUTE @deletedInstances = dt.PurgeInstanceStateByID @instanceIDs
    RETURN @deletedInstances
END
GO

CREATE OR ALTER PROCEDURE dt.SetGlobalSetting
    @Name varchar(300),
    @Value sql_variant
AS
BEGIN
    BEGIN TRANSACTION
 
    UPDATE GlobalSettings WITH (UPDLOCK, HOLDLOCK)
    SET
        [Value] = @Value,
        [Timestamp] = SYSUTCDATETIME(),
        [LastModifiedBy] = USER_NAME()
    WHERE
        [Name] = @Name
 
    IF @@ROWCOUNT = 0
    BEGIN
        INSERT INTO GlobalSettings ([Name], [Value]) VALUES (@Name, @Value)
    END
 
    COMMIT TRANSACTION
END
GO


CREATE OR ALTER PROCEDURE dt._LockNextOrchestration
    @BatchSize int,
    @LockedBy varchar(100),
    @LockExpiration datetime2,
    -- Orchestration type: NULL = any, 0 = orchestration, 1 = entity
    @OrchestrationType BIT = NULL
AS
BEGIN
    DECLARE @now datetime2 = SYSUTCDATETIME()
    DECLARE @instanceID varchar(100)
    DECLARE @parentInstanceID varchar(100)
    DECLARE @version varchar(100)
    DECLARE @runtimeStatus varchar(30)
    DECLARE @tags varchar(8000)
    DECLARE @TaskHub varchar(50) = dt.CurrentTaskHub()

    BEGIN TRANSACTION

    -- *** IMPORTANT ***
    -- To prevent deadlocks, it is important to maintain consistent table access
    -- order across all stored procedures that execute within a transaction.
    -- Table order for this sproc: Instances --> NewEvents --> Payloads --> History

    -- Lock the first active instance that has pending messages.
    -- Delayed events from durable timers will have a non-null VisibleTime value.
    -- Non-active instances will never have their messages or history read.
    UPDATE TOP (1) Instances WITH (READPAST)
    SET
        [LockedBy] = @LockedBy,
	    [LockExpiration] = @LockExpiration,
        @instanceID = I.[InstanceID],
        @parentInstanceID = I.[ParentInstanceID],
        @runtimeStatus = I.[RuntimeStatus],
        @version = I.[Version],
        @tags = I.[Tags]
    FROM 
        Instances I WITH (READPAST) INNER JOIN NewEvents E WITH (READPAST) ON
            E.[TaskHub] = @TaskHub AND
            E.[InstanceID] = I.[InstanceID]
    WHERE
        I.TaskHub = @TaskHub AND
	    (I.[LockExpiration] IS NULL OR I.[LockExpiration] < @now) AND
        (E.[VisibleTime] IS NULL OR E.[VisibleTime] < @now) AND
        (@OrchestrationType IS NULL OR
            (@OrchestrationType = 0 AND I.[InstanceID] NOT LIKE '@%@%') OR
            (@OrchestrationType = 1 AND I.[InstanceID] LIKE '@%@%')
        )

    -- Result #1: The list of new events to fetch.
    -- IMPORTANT: DO NOT CHANGE THE ORDER OF RETURNED COLUMNS!
    -- TODO: Update the dequeue count
    SELECT TOP (@BatchSize)
        N.[SequenceNumber],
        N.[Timestamp],
        N.[VisibleTime],
        N.[DequeueCount],
        N.[InstanceID],
        N.[ExecutionID],
        N.[EventType],
        N.[Name],
        N.[RuntimeStatus],
        N.[TaskID],
        P.[Reason],
        P.[Text] AS [PayloadText],
        P.[PayloadID],
        DATEDIFF(SECOND, [Timestamp], @now) AS [WaitTime],
        @parentInstanceID as [ParentInstanceID],
        @version as [Version],
        N.[TraceContext],
        @tags as [Tags]
    FROM NewEvents N
        LEFT OUTER JOIN dt.[Payloads] P ON 
            P.[TaskHub] = @TaskHub AND
            P.[InstanceID] = N.[InstanceID] AND
            P.[PayloadID] = N.[PayloadID]
    WHERE
        N.[TaskHub] = @TaskHub AND
        N.[InstanceID] = @instanceID AND
        (N.[VisibleTime] IS NULL OR N.[VisibleTime] < @now)

    -- Bail if no events are returned - this implies that another thread already took them (???)
    IF @@ROWCOUNT = 0
    BEGIN
        ROLLBACK TRANSACTION
        RETURN
    END

    -- Result #2: Basic information about this instance, including its runtime status
    SELECT @instanceID AS [InstanceID], @runtimeStatus AS [RuntimeStatus]

    -- Result #3: The full event history for the locked instance
    -- NOTE: This must be kept consistent with the dt.HistoryEvents custom data type
    SELECT
        H.[InstanceID],
        H.[ExecutionID],
        H.[SequenceNumber],
        H.[EventType],
        H.[Name],
        H.[RuntimeStatus],
        H.[TaskID],
        H.[Timestamp],
        H.[IsPlayed],
        H.[VisibleTime],
        P.[Reason],
        -- Optimization: Do not load the data payloads for these history events - they are not needed since they are never replayed
        (CASE WHEN [EventType] IN ('TaskScheduled', 'SubOrchestrationInstanceCreated') THEN NULL ELSE P.[Text] END) AS [PayloadText],
        [PayloadID],
        @parentInstanceID as [ParentInstanceID],
        @version as [Version],
        H.[TraceContext],
        @tags as [Tags]
    FROM History H WITH (INDEX (PK_History))
        LEFT OUTER JOIN Payloads P ON
            P.[TaskHub] = @TaskHub AND
            P.[InstanceID] = H.[InstanceID] AND
            P.[PayloadID] = H.[DataPayloadID]
    WHERE H.[TaskHub] = @TaskHub AND H.[InstanceID] = @instanceID
    ORDER BY H.[SequenceNumber] ASC

    COMMIT TRANSACTION
END
GO


CREATE OR ALTER PROCEDURE dt._CheckpointOrchestration
    @InstanceID varchar(100),
    @ExecutionID varchar(50),
    @RuntimeStatus varchar(30),
    @CustomStatusPayload varchar(MAX),
    @DeletedEvents MessageIDs READONLY,
    @NewHistoryEvents HistoryEvents READONLY,
    @NewOrchestrationEvents OrchestrationEvents READONLY,
    @NewTaskEvents TaskEvents READONLY
AS
BEGIN
    BEGIN TRANSACTION

    DECLARE @TaskHub varchar(50) = dt.CurrentTaskHub()

    DECLARE @InputPayloadID uniqueidentifier
    DECLARE @CustomStatusPayloadID uniqueidentifier
    DECLARE @ExistingOutputPayloadID uniqueidentifier
    DECLARE @ExistingCustomStatusPayload varchar(MAX)
    DECLARE @ExistingExecutionID varchar(50)

    -- Check for an existing custom status payload for this instance. If one exists, compare it
    -- to the new one to know if we need to update the existing entry or not.
    -- At the same time, grab the execution ID so we can learn if this is a ContinueAsNew.
    SELECT TOP 1 
        @InputPayloadID = I.[InputPayloadID],
        @CustomStatusPayloadID = I.[CustomStatusPayloadID],
        @ExistingOutputPayloadID = I.[OutputPayloadID],
        @ExistingCustomStatusPayload = P.[Text],
        @ExistingExecutionID = I.[ExecutionID]
    FROM Payloads P RIGHT OUTER JOIN Instances I ON
        P.[TaskHub] = @TaskHub AND
        P.[InstanceID] = I.[InstanceID] AND
        P.[PayloadID] = I.[CustomStatusPayloadID]
    WHERE I.[TaskHub] = @TaskHub AND I.[InstanceID] = @InstanceID

    -- ContinueAsNew case: delete all existing runtime state (history and payloads), but be careful
    -- not to delete payloads of unprocessed state, like new events.
    DECLARE @IsContinueAsNew BIT = 0
    IF @ExistingExecutionID IS NOT NULL AND @ExistingExecutionID <> @ExecutionID
    BEGIN
        DECLARE @PayloadIDsToDelete TABLE ([PayloadID] uniqueidentifier NULL)
        INSERT INTO @PayloadIDsToDelete
        VALUES (@InputPayloadID), (@CustomStatusPayloadID), (@ExistingOutputPayloadID)

        DELETE FROM History
        OUTPUT DELETED.[DataPayloadID] INTO @PayloadIDsToDelete
        WHERE [TaskHub] = @TaskHub AND [InstanceID] = @InstanceID

        DELETE FROM Payloads
        WHERE
            [TaskHub] = @TaskHub AND
            [InstanceID] = @InstanceID AND
            [PayloadID] IN (SELECT [PayloadID] FROM @PayloadIDsToDelete)

        -- The existing payload got purged in the previous statement 
        SET @ExistingCustomStatusPayload = NULL
        SET @IsContinueAsNew = 1
    END

    -- Custom status case #1: Setting the custom status for the first time
    IF @ExistingCustomStatusPayload IS NULL AND @CustomStatusPayload IS NOT NULL
    BEGIN
        SET @CustomStatusPayloadID = NEWID()
        INSERT INTO Payloads ([TaskHub], [InstanceID], [PayloadID], [Text])
        VALUES (@TaskHub, @InstanceID, @CustomStatusPayloadID, @CustomStatusPayload)
    END

    -- Custom status case #2: Updating an existing custom status payload
    IF @ExistingCustomStatusPayload IS NOT NULL AND @ExistingCustomStatusPayload <> @CustomStatusPayload
    BEGIN
        UPDATE Payloads SET [Text] = @CustomStatusPayload WHERE 
            [TaskHub] = @TaskHub AND
            [InstanceID] = @InstanceID AND
            [PayloadID] = @CustomStatusPayloadID
    END

    -- Need to update the input payload ID if this is a ContinueAsNew
    IF @IsContinueAsNew = 1
    BEGIN
        SET @InputPayloadID = (
            SELECT TOP 1 [PayloadID]
            FROM @NewHistoryEvents
            WHERE [EventType] = 'ExecutionStarted'
            ORDER BY [SequenceNumber] DESC
        )
    END

    DECLARE @IsCompleted bit
    SET @IsCompleted = (CASE WHEN @RuntimeStatus IN ('Completed', 'Failed', 'Terminated') THEN 1 ELSE 0 END)

    -- The output payload will only exist when the orchestration has completed.
    -- Fetch its payload ID now so that we can update it in the Instances table further down.
    DECLARE @OutputPayloadID uniqueidentifier
    IF @IsCompleted = 1
    BEGIN
        SET @OutputPayloadID = (
            SELECT TOP 1 [PayloadID]
            FROM @NewHistoryEvents
            WHERE [EventType] = 'ExecutionCompleted' OR [EventType] = 'ExecutionTerminated'
            ORDER BY [SequenceNumber] DESC
        )
    END

    -- Insert data payloads into the Payloads table as a single statement.
    -- The [PayloadText] value will be NULL if there is no payload or if a payload is already known to exist in the DB.
    -- The [PayloadID] value might be set even if [PayloadText] and [Reason] are both NULL.
    -- This needs to be done before the UPDATE to Instances because the Instances table needs to reference the output payload.
    INSERT INTO Payloads ([TaskHub], [InstanceID], [PayloadID], [Text], [Reason])
        SELECT @TaskHub, [InstanceID], [PayloadID], [PayloadText], [Reason]
        FROM @NewHistoryEvents
        WHERE [PayloadText] IS NOT NULL OR [Reason] IS NOT NULL

    UPDATE Instances
    SET
        [ExecutionID] = @ExecutionID,
        [RuntimeStatus] = @RuntimeStatus,
        [LastUpdatedTime] = SYSUTCDATETIME(),
        [CompletedTime] = (CASE WHEN @IsCompleted = 1 THEN SYSUTCDATETIME() ELSE NULL END),
        [LockExpiration] = NULL, -- release the lock
        [CustomStatusPayloadID] = @CustomStatusPayloadID,
        [InputPayloadID] = @InputPayloadID,
        [OutputPayloadID] = @OutputPayloadID
    FROM Instances
    WHERE [TaskHub] = @TaskHub and [InstanceID] = @InstanceID

    IF @@ROWCOUNT = 0
    BEGIN
        ROLLBACK TRANSACTION;
        THROW 50000, 'The instance does not exist.', 1;
    END
    -- External event messages can create new instances
    -- NOTE: There is a chance this could result in deadlocks if two 
    --       instances are sending events to each other at the same time
    INSERT INTO Instances (
        [TaskHub],
        [InstanceID],
        [ExecutionID],
        [Name],
        [Version],
        [RuntimeStatus],
        [TraceContext])
    SELECT DISTINCT
        @TaskHub,
        E.[InstanceID],
        NEWID(),
        SUBSTRING(E.[InstanceID], 2, CHARINDEX('@', E.[InstanceID], 2) - 2),
        '',
        'Pending',
        E.[TraceContext]
    FROM @NewOrchestrationEvents E
    WHERE LEFT(E.[InstanceID], 1) = '@'
        AND CHARINDEX('@', E.[InstanceID], 2) > 0
        AND NOT EXISTS (
            SELECT 1
            FROM Instances I
            WHERE [TaskHub] = @TaskHub AND I.[InstanceID] = E.[InstanceID])
    GROUP BY E.[InstanceID], E.[TraceContext]
    ORDER BY E.[InstanceID] ASC

    -- Create sub-orchestration instances
    INSERT INTO Instances (
        [TaskHub],
        [InstanceID],
        [ExecutionID],
        [Name],
        [Version],
        [ParentInstanceID],
        [RuntimeStatus],
        [TraceContext],
        [Tags])
    SELECT DISTINCT
        @TaskHub,
        E.[InstanceID],
        E.[ExecutionID],
        E.[Name],
        E.[Version],
        E.[ParentInstanceID],
        'Pending',
        E.[TraceContext],
        E.[Tags]
    FROM @NewOrchestrationEvents E
    WHERE E.[EventType] IN ('ExecutionStarted')
        AND NOT EXISTS (
            SELECT 1
            FROM Instances I
            WHERE [TaskHub] = @TaskHub AND I.[InstanceID] = E.[InstanceID])
    ORDER BY E.[InstanceID] ASC

    -- Insert new event data payloads into the Payloads table in batches.
    -- PayloadID values are provided by the caller only if a payload exists.
    INSERT INTO Payloads ([TaskHub], [InstanceID], [PayloadID], [Text], [Reason])
        SELECT @TaskHub, [InstanceID], [PayloadID], [PayloadText], [Reason]
        FROM @NewOrchestrationEvents
        WHERE [PayloadID] IS NOT NULL

    INSERT INTO Payloads ([TaskHub], [InstanceID], [PayloadID], [Text])
        SELECT @TaskHub, [InstanceID], [PayloadID], [PayloadText]
        FROM @NewTaskEvents
        WHERE [PayloadID] IS NOT NULL

    -- Insert the new events with references to their payloads, if applicable
    INSERT INTO NewEvents (
        [TaskHub],
        [InstanceID],
        [ExecutionID],
        [EventType],
        [Name],
        [RuntimeStatus],
        [VisibleTime],
        [TaskID],
        [TraceContext],
        [PayloadID]
    ) 
    SELECT 
        @TaskHub,
        [InstanceID],
        [ExecutionID],
        [EventType],
        [Name],
        [RuntimeStatus],
        [VisibleTime],
        [TaskID],
        [TraceContext],
        [PayloadID]
    FROM @NewOrchestrationEvents
    
    -- We return the list of deleted messages so that the caller can issue a 
    -- warning about missing messages
    DELETE E
    OUTPUT DELETED.InstanceID, DELETED.SequenceNumber
    FROM NewEvents E WITH (FORCESEEK(PK_NewEvents(TaskHub, InstanceID, SequenceNumber)))
        INNER JOIN @DeletedEvents D ON 
            D.InstanceID = E.InstanceID AND
            D.SequenceNumber = E.SequenceNumber AND
            E.TaskHub = @TaskHub

    -- IMPORTANT: This insert is expected to fail with a primary key constraint violation in a
    --            split-brain situation where two instances try to execute the same orchestration
    --            at the same time. The SDK will check for this exact error condition.
    INSERT INTO History (
        [TaskHub],
        [InstanceID],
        [ExecutionID],
        [SequenceNumber],
        [EventType],
        [TaskID],
        [Timestamp],
        [IsPlayed],
        [Name],
        [RuntimeStatus],
        [VisibleTime],
        [TraceContext],
        [DataPayloadID])
    SELECT
        @TaskHub,
        H.[InstanceID],
        H.[ExecutionID],
        H.[SequenceNumber],
        H.[EventType],
        H.[TaskID],
        H.[Timestamp],
        H.[IsPlayed],
        H.[Name],
        H.[RuntimeStatus],
        H.[VisibleTime],
        H.[TraceContext],
        H.[PayloadID]
    FROM @NewHistoryEvents H

    -- TaskScheduled events
    INSERT INTO NewTasks (
        [TaskHub],
        [InstanceID],
        [ExecutionID],
        [Name],
        [TaskID],
        [VisibleTime],
        [LockedBy],
        [LockExpiration],
        [PayloadID],
        [Version],
        [TraceContext],
        [Tags]
    )
    OUTPUT
        INSERTED.[SequenceNumber],
        INSERTED.[TaskID]
    SELECT 
        @TaskHub,
        [InstanceID],
        [ExecutionID],
        [Name],
        [TaskID],
        [VisibleTime],
        [LockedBy],
        [LockExpiration],
        [PayloadID],
        [Version],
        [TraceContext],
        [Tags]
    FROM @NewTaskEvents

    COMMIT TRANSACTION
END
GO


CREATE OR ALTER PROCEDURE dt._DiscardEventsAndUnlockInstance
    @InstanceID varchar(100),
    @DeletedEvents MessageIDs READONLY
AS
BEGIN
    DECLARE @taskHub varchar(50) = dt.CurrentTaskHub()

    -- We return the list of deleted messages so that the caller can issue a 
    -- warning about missing messages
    DELETE E
    OUTPUT DELETED.InstanceID, DELETED.SequenceNumber
    FROM NewEvents E WITH (FORCESEEK(PK_NewEvents(TaskHub, InstanceID, SequenceNumber)))
        INNER JOIN @DeletedEvents D ON 
            D.InstanceID = E.InstanceID AND
            D.SequenceNumber = E.SequenceNumber AND
            E.TaskHub = @taskHub

    -- Release the lock on this instance
    UPDATE Instances SET [LastUpdatedTime] = SYSUTCDATETIME(), [LockExpiration] = NULL
    WHERE [TaskHub] = @taskHub and [InstanceID] = @InstanceID
END
GO


CREATE OR ALTER PROCEDURE dt._AddOrchestrationEvents
    @NewOrchestrationEvents OrchestrationEvents READONLY 
AS
BEGIN
    BEGIN TRANSACTION

    -- *** IMPORTANT ***
    -- To prevent deadlocks, it is important to maintain consistent table access
    -- order across all stored procedures that execute within a transaction.
    -- Table order for this sproc: Payloads --> NewEvents

    DECLARE @TaskHub varchar(50) = dt.CurrentTaskHub()
    
    -- External event messages can create new instances
    -- NOTE: There is a chance this could result in deadlocks if two 
    --       instances are sending events to each other at the same time
    BEGIN TRY
        INSERT INTO Instances (
            [TaskHub],
            [InstanceID],
            [ExecutionID],
            [Name],
            [Version],
            [RuntimeStatus],
            [TraceContext])
        SELECT DISTINCT
            @TaskHub,
            E.[InstanceID],
            NEWID(),
            SUBSTRING(E.[InstanceID], 2, CHARINDEX('@', E.[InstanceID], 2) - 2),
            '',
            'Pending',
            E.[TraceContext]
        FROM @NewOrchestrationEvents E
        WHERE NOT EXISTS (
            SELECT 1
            FROM Instances I
            WHERE [TaskHub] = @TaskHub AND I.[InstanceID] = E.[InstanceID])
        GROUP BY E.[InstanceID], E.[TraceContext]
        ORDER BY E.[InstanceID] ASC
    END TRY
    BEGIN CATCH
        -- Ignore PK violations here, which can happen when multiple clients
        -- try to add messages at the same time for the same instance
        IF ERROR_NUMBER() <> 2627  -- 2627 is PK violation
        BEGIN
          ROLLBACK TRANSACTION;
          THROW;
        END
    END CATCH

    -- Insert new event data payloads into the Payloads table in batches.
    -- PayloadID values are provided by the caller only if a payload exists.
    INSERT INTO Payloads ([TaskHub], [InstanceID], [PayloadID], [Text], [Reason])
        SELECT @TaskHub, [InstanceID], [PayloadID], [PayloadText], [Reason]
        FROM @NewOrchestrationEvents
        WHERE [PayloadID] IS NOT NULL

    -- Insert the new events with references to their payloads, if applicable
    INSERT INTO NewEvents (
        [TaskHub],
        [InstanceID],
        [ExecutionID],
        [EventType],
        [Name],
        [RuntimeStatus],
        [VisibleTime],
        [TaskID],
        [TraceContext],
        [PayloadID]
    ) 
    SELECT 
        @TaskHub,
        [InstanceID],
        [ExecutionID],
        [EventType],
        [Name],
        [RuntimeStatus],
        [VisibleTime],
        [TaskID],
        [TraceContext],
        [PayloadID]
    FROM @NewOrchestrationEvents

    COMMIT TRANSACTION
END
GO

CREATE OR ALTER PROCEDURE dt.QuerySingleOrchestration
    @InstanceID varchar(100),
    @ExecutionID varchar(50) = NULL,
    @FetchInput bit = 1,
    @FetchOutput bit = 1
AS
BEGIN
    DECLARE @TaskHub varchar(50) = dt.CurrentTaskHub()

    SELECT TOP 1
        I.[InstanceID],
        I.[ExecutionID],
        I.[Name],
        I.[Version],
        I.[CreatedTime],
        I.[LastUpdatedTime],
        I.[CompletedTime],
        I.[RuntimeStatus],
        I.[ParentInstanceID],
        (SELECT TOP 1 [Text] FROM Payloads P WHERE
            P.[TaskHub] = @TaskHub AND
            P.[InstanceID] = I.[InstanceID] AND
            P.[PayloadID] = I.[CustomStatusPayloadID]) AS [CustomStatusText],
        CASE WHEN @FetchInput = 1 THEN (SELECT TOP 1 [Text] FROM Payloads P WHERE
            P.[TaskHub] = @TaskHub AND
            P.[InstanceID] = I.[InstanceID] AND
            P.[PayloadID] = I.[InputPayloadID]) ELSE NULL END AS [InputText],
        CASE WHEN @FetchOutput = 1 THEN (SELECT TOP 1 [Text] FROM Payloads P WHERE
            P.[TaskHub] = @TaskHub AND
            P.[InstanceID] = I.[InstanceID] AND
            P.[PayloadID] = I.[OutputPayloadID]) ELSE NULL END AS [OutputText],
        I.[TraceContext],
        I.[Tags]
    FROM Instances I
    WHERE
        I.[TaskHub] = @TaskHub AND
        I.[InstanceID] = @InstanceID AND
        (@ExecutionID IS NULL OR @ExecutionID = I.ExecutionID)
END
GO


CREATE OR ALTER PROCEDURE dt._QueryManyOrchestrations
    @PageSize smallint = 100,
    @PageNumber int = 0,
    @FetchInput bit = 1,
    @FetchOutput bit = 1,
    @CreatedTimeFrom datetime2 = NULL,
    @CreatedTimeTo datetime2 = NULL,
    @RuntimeStatusFilter varchar(200) = NULL,
    @InstanceIDPrefix varchar(100) = NULL,
    @ExcludeSubOrchestrations bit = 0
AS
BEGIN
    DECLARE @TaskHub varchar(50) = dt.CurrentTaskHub()

    SELECT
        I.[InstanceID],
        I.[ExecutionID],
        I.[Name],
        I.[Version],
        I.[CreatedTime],
        I.[LastUpdatedTime],
        I.[CompletedTime],
        I.[RuntimeStatus],
        I.[ParentInstanceID],
        (SELECT TOP 1 [Text] FROM Payloads P WHERE
            P.[TaskHub] = @TaskHub AND
            P.[InstanceID] = I.[InstanceID] AND
            P.[PayloadID] = I.[CustomStatusPayloadID]) AS [CustomStatusText],
        CASE WHEN @FetchInput = 1 THEN (SELECT TOP 1 [Text] FROM Payloads P WHERE
            P.[TaskHub] = @TaskHub AND
            P.[InstanceID] = I.[InstanceID] AND
            P.[PayloadID] = I.[InputPayloadID]) ELSE NULL END AS [InputText],
        CASE WHEN @FetchOutput = 1 THEN (SELECT TOP 1 [Text] FROM Payloads P WHERE
            P.[TaskHub] = @TaskHub AND
            P.[InstanceID] = I.[InstanceID] AND
            P.[PayloadID] = I.[OutputPayloadID]) ELSE NULL END AS [OutputText],
        I.[TraceContext],
        I.[Tags]
    FROM
        Instances I
    WHERE
        I.[TaskHub] = @TaskHub AND
        (@CreatedTimeFrom IS NULL OR I.[CreatedTime] >= @CreatedTimeFrom) AND
        (@CreatedTimeTo IS NULL OR I.[CreatedTime] <= @CreatedTimeTo) AND
        (@RuntimeStatusFilter IS NULL OR I.[RuntimeStatus] IN (SELECT [value] FROM string_split(@RuntimeStatusFilter, ','))) AND
        (@InstanceIDPrefix IS NULL OR I.[InstanceID] LIKE @InstanceIDPrefix + '%') AND
        (@ExcludeSubOrchestrations = 0 OR I.ParentInstanceID IS NULL)
    ORDER BY
        I.[CreatedTime] OFFSET (@PageNumber * @PageSize) ROWS FETCH NEXT @PageSize ROWS ONLY
END
GO


CREATE OR ALTER PROCEDURE dt._LockNextTask
    @LockedBy varchar(100),
    @LockExpiration datetime2
AS
BEGIN
    DECLARE @TaskHub varchar(50) = dt.CurrentTaskHub()
    DECLARE @now datetime2 = SYSUTCDATETIME()

    DECLARE @SequenceNumber bigint

    BEGIN TRANSACTION
    -- *** IMPORTANT ***
    -- To prevent deadlocks, it is important to maintain consistent table access
    -- order across all stored procedures that execute within a transaction.
    -- Table order for this sproc: NewTasks --> Payloads

    -- Update (lock) and return a single row.
    -- The PK_NewTasks hint is specified to help ensure in-order selection.
    -- TODO: Filter out tasks for instances that are in a non-running state (suspended, etc.)
    UPDATE TOP (1) NewTasks WITH (READPAST)
    SET
        @SequenceNumber = [SequenceNumber],
        [LockedBy] = @LockedBy,
	    [LockExpiration] = @LockExpiration,
        [DequeueCount] = [DequeueCount] + 1
    FROM
        NewTasks WITH (INDEX (PK_NewTasks))
    WHERE
        [TaskHub] = @TaskHub AND
	    ([LockExpiration] IS NULL OR [LockExpiration] < @now) AND
        ([VisibleTime] IS NULL OR [VisibleTime] < @now)

    SELECT TOP (1)
        [SequenceNumber],
        [InstanceID],
        [ExecutionID],
        [Name],
        'TaskScheduled' AS [EventType],
        [TaskID],
        [VisibleTime],
        [Timestamp],
        [DequeueCount],
        [Version],
        (SELECT TOP 1 [Text] FROM Payloads P WHERE
            P.[TaskHub] = @TaskHub AND
            P.[InstanceID] = N.[InstanceID] AND
            P.[PayloadID] = N.[PayloadID]) AS [PayloadText],
        DATEDIFF(SECOND, [Timestamp], @now) AS [WaitTime],
        [TraceContext],
        [Tags]
    FROM NewTasks N
    WHERE [TaskHub] = @TaskHub AND [SequenceNumber] = @SequenceNumber

    COMMIT TRANSACTION
END
GO


CREATE OR ALTER PROCEDURE dt._RenewOrchestrationLocks
    @InstanceID varchar(100),
    @LockExpiration datetime2
AS
BEGIN
    DECLARE @TaskHub varchar(50) = dt.CurrentTaskHub()

    UPDATE Instances
    SET [LockExpiration] = @LockExpiration
    WHERE [TaskHub] = @TaskHub AND [InstanceID] = @InstanceID
END
GO


CREATE OR ALTER PROCEDURE dt._RenewTaskLocks
    @RenewingTasks MessageIDs READONLY,
    @LockExpiration datetime2
AS
BEGIN
    DECLARE @TaskHub varchar(50) = dt.CurrentTaskHub()

    UPDATE N
    SET [LockExpiration] = @LockExpiration
    FROM NewTasks N INNER JOIN @RenewingTasks C ON
        C.[SequenceNumber] = N.[SequenceNumber] AND
        N.[TaskHub] = @TaskHub
END
GO


CREATE OR ALTER PROCEDURE dt._CompleteTasks
    @CompletedTasks MessageIDs READONLY,
    @Results TaskEvents READONLY
AS
BEGIN
    DECLARE @TaskHub varchar(50) = dt.CurrentTaskHub()

    BEGIN TRANSACTION

    /* Ensure the instance exists and is running before attempting to handle task results.
       We need to do this first and hold the lock to avoid race conditions and deadlocks with other operations. */
    DECLARE @existingInstanceID varchar(100)

    SELECT @existingInstanceID = R.[InstanceID]
    FROM Instances I WITH (HOLDLOCK)
        INNER JOIN @Results R ON 
            I.[TaskHub] = @TaskHub AND
            I.[InstanceID] = R.[InstanceID] AND 
            I.[ExecutionID] = R.[ExecutionID] AND
            I.[RuntimeStatus] IN ('Running', 'Suspended')
    
    -- If we find the instance, save the result to the [NewEvents] table.
    IF @existingInstanceID IS NOT NULL
    BEGIN
        -- Insert new event data payloads into the Payloads table in batches.
        -- PayloadID values are provided by the caller only if a payload exists.
        INSERT INTO Payloads ([TaskHub], [InstanceID], [PayloadID], [Text], [Reason])
            SELECT @TaskHub, [InstanceID], [PayloadID], [PayloadText], [Reason]
            FROM @Results
            WHERE [PayloadID] IS NOT NULL

        INSERT INTO NewEvents (
            [TaskHub],
            [InstanceID],
            [ExecutionID],
            [Name],
            [EventType],
            [TaskID],
            [VisibleTime],
            [PayloadID]
        ) 
        SELECT
            @TaskHub,
            R.[InstanceID],
            R.[ExecutionID],
            R.[Name],
            R.[EventType],
            R.[TaskID],
            R.[VisibleTime],
            R.[PayloadID]
        FROM @Results R
    END

    DECLARE @payloadsToDelete TABLE ([PayloadID] uniqueidentifier NULL)

    -- We return the list of deleted messages so that the caller can issue a 
    -- warning about missing messages
    DELETE N
    OUTPUT DELETED.[PayloadID] INTO @payloadsToDelete
    OUTPUT DELETED.[SequenceNumber]
    FROM NewTasks N WITH (FORCESEEK(PK_NewTasks(TaskHub, SequenceNumber)))
        INNER JOIN @CompletedTasks C ON
            C.[SequenceNumber] = N.[SequenceNumber] AND
            N.[TaskHub] = @TaskHub

    -- If we fail to delete the messages then we must abort the transaction.
    -- This can happen if the message was completed by another worker, in which
    -- case we don't want any of the side-effects to persist.
    IF @@ROWCOUNT <> (SELECT COUNT(*) FROM @CompletedTasks)
    BEGIN
        ROLLBACK TRANSACTION;
        THROW 50002, N'Failed to delete the completed task events(s). They may have been deleted by another worker, in which case the current execution is likely a duplicate. Any results or pending side-effects of this task activity execution will be discarded.', 1;
    END
    COMMIT TRANSACTION
END
GO


CREATE OR ALTER PROCEDURE dt._GetVersions
AS
BEGIN
    SELECT SemanticVersion, UpgradeTime
    FROM Versions
    ORDER BY UpgradeTime DESC
END
GO


CREATE OR ALTER PROCEDURE dt._UpdateVersion
    @SemanticVersion varchar(100)
AS
BEGIN
    -- Duplicates are ignored (per the schema definition of dt.Versions)
    INSERT INTO Versions (SemanticVersion)
    VALUES (@SemanticVersion)
END
GO


CREATE OR ALTER PROCEDURE dt._RewindInstance
    @InstanceID varchar(100),
    @Reason varchar(max) = NULL
AS
BEGIN
    BEGIN TRANSACTION

    EXEC dt._RewindInstanceRecursive @InstanceID, @Reason

    COMMIT TRANSACTION
END
GO


CREATE OR ALTER PROCEDURE dt._RewindInstanceRecursive
    @InstanceID varchar(100),
    @Reason varchar(max) = NULL
AS
BEGIN
    DECLARE @TaskHub varchar(50) = dt.CurrentTaskHub()

    -- *** IMPORTANT ***
    -- To prevent deadlocks, it is important to maintain consistent table access
    -- order across all stored procedures that execute within a transaction.
    -- Table order for this sproc: Instances --> (History --> Payloads --> History), NewEvents)  

    DECLARE @existingStatus varchar(30)
    DECLARE @executionID varchar(50)
    
    SELECT TOP 1 @existingStatus = existing.[RuntimeStatus], @executionID = existing.[ExecutionID]
    FROM Instances existing WITH (HOLDLOCK)
    WHERE [TaskHub] = @TaskHub AND [InstanceID] = @InstanceID

    -- Instance IDs can be overwritten only if the orchestration is in a terminal state
    IF @existingStatus NOT IN ('Failed')
    BEGIN
        DECLARE @msg nvarchar(4000) = FORMATMESSAGE('Cannot rewind instance with ID ''%s'' because it is not in a ''Failed'' state, but in ''%s'' state.', @InstanceID, @existingStatus);
        THROW 50001, @msg, 1;
    END
    
    DECLARE @eventsInFailure TABLE (
        [SequenceNumber] bigint NULL,
        [EventType] varchar(40) NULL,
        [TaskID] int NULL,
        [DataPayloadID] uniqueidentifier NULL)
    
    -- Save all events related to failures (ie TaskScheduled/TaskFailed and SubOrchestrationInstanceStarted/SubOrchestrationInstanceFailed couples)
    INSERT INTO @eventsInFailure
    SELECT h.[SequenceNumber], h.[EventType], h.[TaskID], h.[DataPayloadID]
    FROM History h
    WHERE h.[TaskHub] = @TaskHub
      AND h.[InstanceID] = @InstanceID
      AND (h.[EventType] IN ('TaskFailed', 'SubOrchestrationInstanceFailed') OR (h.[EventType] IN ('TaskScheduled', 'SubOrchestrationInstanceStarted') AND EXISTS (
        SELECT 1
        FROM History f
        WHERE f.[TaskHub] = @TaskHub 
          AND f.[InstanceID] = @InstanceID
          AND f.[TaskID] = h.[TaskID]
          AND f.[EventType] = CASE WHEN h.[EventType] = 'TaskScheduled' THEN 'TaskFailed' ELSE 'SubOrchestrationInstanceFailed' END)))

    -- Mark all events related to failure as rewound
    -- This first batch is for all events that have corresponding records in the Payloads table already
    UPDATE Payloads
    SET [Reason] = CONCAT('Rewound: ', ef.[EventType])
    FROM Payloads p
    JOIN @eventsInFailure ef ON p.[PayloadID] = ef.[DataPayloadID]
    WHERE [TaskHub] = @TaskHub
      AND [InstanceID] = @InstanceID
      AND [SequenceNumber] IN (SELECT [SequenceNumber] FROM @eventsInFailure WHERE [DataPayloadID] IS NOT NULL AND [EventType] IN ('TaskScheduled', 'SubOrchestrationInstanceCreated'))

    -- Next, insert new rows into the Payloads table for the rewound events that DON'T already have Payloads
    DECLARE @sequenceNumber bigint
    DECLARE @eventType varchar(40)
    DECLARE @payloadId uniqueidentifier
    DECLARE sequenceNumberCursor CURSOR LOCAL FOR
        SELECT [SequenceNumber], [EventType]
        FROM @eventsInFailure
        WHERE [DataPayloadID] IS NULL

    OPEN sequenceNumberCursor 
    FETCH NEXT FROM sequenceNumberCursor INTO @sequenceNumber, @eventType

    WHILE @@FETCH_STATUS = 0 BEGIN
        SET @payloadId = NEWID()
        INSERT INTO Payloads (
            [TaskHub],
            [InstanceID],
            [PayloadID],
            [Reason]
        )
        VALUES (@TaskHub, @InstanceID, @payloadId, CONCAT('Rewound: ', @eventType))
        FETCH NEXT FROM sequenceNumberCursor INTO @sequenceNumber, @eventType
    END
    CLOSE sequenceNumberCursor
    DEALLOCATE sequenceNumberCursor

    -- Transform all events related to failure to GenericEvents, except for SubOrchestrationInstanceStarted that can be kept
    UPDATE History
    SET [EventType] = 'GenericEvent'
    WHERE [TaskHub] = @TaskHub AND [InstanceID] = @InstanceID
      AND ([SequenceNumber] IN (SELECT [SequenceNumber]
                                FROM @eventsInFailure WHERE [EventType] <> 'SubOrchestrationInstanceStarted')
           OR [RuntimeStatus] = 'Failed')

    -- Enumerate instances of sub orchestrations related to SubOrchestrationInstanceFailed events and rewing them recursively
    DECLARE @subOrchestrationInstanceID varchar(100)
    DECLARE subOrchestrationCursor CURSOR LOCAL FOR
        SELECT i.[InstanceID]
        FROM Instances i
          JOIN History h ON i.[TaskHub] = h.[TaskHub] AND i.[InstanceID] = h.[InstanceID]
          JOIN @eventsInFailure e ON e.[TaskID] = h.[TaskID]
        WHERE i.[ParentInstanceID] = @InstanceID 
          AND h.[EventType] = 'ExecutionStarted'
          AND e.[EventType] = 'SubOrchestrationInstanceFailed'


    OPEN subOrchestrationCursor 
    FETCH NEXT FROM subOrchestrationCursor INTO @subOrchestrationInstanceID

    WHILE @@FETCH_STATUS = 0 BEGIN
        -- Call rewind recursively on the failing suborchestrations
        EXECUTE dt._RewindInstanceRecursive @subOrchestrationInstanceID, @Reason
        FETCH NEXT FROM subOrchestrationCursor INTO @subOrchestrationInstanceID
    END
    CLOSE subOrchestrationCursor
    DEALLOCATE subOrchestrationCursor

    -- Insert a line in NewEvents to ensure orchestration will start
    SET @payloadId = NEWID()
    INSERT INTO Payloads (
        [TaskHub],
        [InstanceID],
        [PayloadID],
        [Text]
    )
    VALUES (@TaskHub, @InstanceID, @payloadId, @Reason)
    INSERT INTO NewEvents (
        [TaskHub],
        [InstanceID],
        [ExecutionID],
        [EventType],
        [PayloadID]
    ) 
    VALUES(
        @TaskHub,
        @InstanceID,
        @executionID,
        'GenericEvent',
        @payloadId)

    -- Set orchestration status to Pending
    UPDATE Instances
    SET [RuntimeStatus] = 'Pending', [LastUpdatedTime] = SYSUTCDATETIME()
    WHERE [TaskHub] = @TaskHub AND [InstanceID] = @InstanceID
END
GO
GO

-- ═══════════════════════════════════════════════════════════════════════════
-- DurableTask : permissions.sql (rôle dt_runtime)
-- ═══════════════════════════════════════════════════════════════════════════
-- Copyright (c) Microsoft Corporation.
-- Licensed under the MIT License.

-- Security 
IF DATABASE_PRINCIPAL_ID('dt_runtime') IS NULL
BEGIN
    -- This is the role to which all low-privilege user accounts should be associated using
    -- the 'ALTER ROLE dt_runtime ADD MEMBER [<username>]' statement.
    CREATE ROLE dt_runtime
END

-- Each stored procedure that is granted to dt_runtime must limits access to data based 
-- on the task hub since that is. that no
-- database user can access data created by another database user.

-- Functions 
GRANT EXECUTE ON OBJECT::dt.GetScaleMetric TO dt_runtime
GRANT EXECUTE ON OBJECT::dt.GetScaleRecommendation TO dt_runtime
GRANT EXECUTE ON OBJECT::dt.CurrentTaskHub TO dt_runtime

-- Public sprocs
GRANT EXECUTE ON OBJECT::dt.CreateInstance TO dt_runtime
GRANT EXECUTE ON OBJECT::dt.GetInstanceHistory TO dt_runtime
GRANT EXECUTE ON OBJECT::dt.QuerySingleOrchestration TO dt_runtime
GRANT EXECUTE ON OBJECT::dt.RaiseEvent TO dt_runtime
GRANT EXECUTE ON OBJECT::dt.TerminateInstance TO dt_runtime
GRANT EXECUTE ON OBJECT::dt.PurgeInstanceStateByID TO dt_runtime
GRANT EXECUTE ON OBJECT::dt.PurgeInstanceStateByTime TO dt_runtime

-- Internal sprocs
GRANT EXECUTE ON OBJECT::dt._AddOrchestrationEvents TO dt_runtime
GRANT EXECUTE ON OBJECT::dt._CheckpointOrchestration TO dt_runtime
GRANT EXECUTE ON OBJECT::dt._CompleteTasks TO dt_runtime
GRANT EXECUTE ON OBJECT::dt._DiscardEventsAndUnlockInstance TO dt_runtime
GRANT EXECUTE ON OBJECT::dt._GetVersions TO dt_runtime
GRANT EXECUTE ON OBJECT::dt._LockNextOrchestration TO dt_runtime
GRANT EXECUTE ON OBJECT::dt._LockNextTask TO dt_runtime
GRANT EXECUTE ON OBJECT::dt._QueryManyOrchestrations TO dt_runtime
GRANT EXECUTE ON OBJECT::dt._RenewOrchestrationLocks TO dt_runtime
GRANT EXECUTE ON OBJECT::dt._RenewTaskLocks TO dt_runtime
GRANT EXECUTE ON OBJECT::dt._UpdateVersion TO dt_runtime
GRANT EXECUTE ON OBJECT::dt._RewindInstance TO dt_runtime
GRANT EXECUTE ON OBJECT::dt._RewindInstanceRecursive TO dt_runtime

-- Types
GRANT EXECUTE ON TYPE::dt.HistoryEvents TO dt_runtime
GRANT EXECUTE ON TYPE::dt.MessageIDs TO dt_runtime
GRANT EXECUTE ON TYPE::dt.InstanceIDs TO dt_runtime
GRANT EXECUTE ON TYPE::dt.OrchestrationEvents TO dt_runtime
GRANT EXECUTE ON TYPE::dt.TaskEvents TO dt_runtime

GO
GO

-- ═══════════════════════════════════════════════════════════════════════════
-- DurableTask : version du schéma et mode du task hub
-- ═══════════════════════════════════════════════════════════════════════════
-- Version reconnue par le fournisseur : au démarrage, il ne tente aucune mise à niveau si elle est à jour.
EXEC dt._UpdateVersion @SemanticVersion = '1.8.1+e7d35db87d50c9269af135441c823b24c083bce8';

-- Task hub déterminé par le nom d'application (Oim:TaskHubParApplication = true) plutôt que par
-- l'utilisateur SQL. Réservé à un administrateur : l'application ne peut pas le faire elle-même.
EXEC dt.SetGlobalSetting @Name = 'TaskHubMode', @Value = 0;
GO

-- ═══════════════════════════════════════════════════════════════════════════
-- OIM : schéma oim (sources/OIM.Moteur/Stockage/SchemaOim.sql)
-- ═══════════════════════════════════════════════════════════════════════════
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
GO

-- ═══════════════════════════════════════════════════════════════════════════
-- Rôle de l'application : oim_application
-- ═══════════════════════════════════════════════════════════════════════════
IF DATABASE_PRINCIPAL_ID('oim_application') IS NULL
    CREATE ROLE oim_application;

-- Moteur DurableTask (procédures stockées du fournisseur).
ALTER ROLE dt_runtime ADD MEMBER oim_application;

-- Suivi des instances (tableau de bord) : lecture directe des vues DurableTask.
GRANT SELECT ON OBJECT::dt.vInstances TO oim_application;
GRANT SELECT ON OBJECT::dt.vHistory TO oim_application;

-- Définitions de processus et brouillons de tests.
GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::oim TO oim_application;
GO

