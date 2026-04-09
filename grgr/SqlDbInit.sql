-- ============================================================
-- Estimation Database — Full Schema Creation Script
-- Creates all tables for a new database
-- Order respects foreign key dependencies
-- ============================================================

-- ============================================================
-- 1. Skills
-- ============================================================
CREATE TABLE [dbo].[Skills]
(
    [Id]          INT           NOT NULL IDENTITY(1,1),
    [Name]        NVARCHAR(50)  NOT NULL,
    [Description] NVARCHAR(150) NULL,
    [Created]     DATETIME2     NOT NULL CONSTRAINT [DF_Skills_Created] DEFAULT GETDATE(),
    [Updated]     DATETIME2     NULL,
    CONSTRAINT [PK_Skills] PRIMARY KEY CLUSTERED ([Id] ASC)
);

-- ============================================================
-- 2. SkillLevels
-- ============================================================
CREATE TABLE [dbo].[SkillLevels]
(
    [Id]          INT           NOT NULL IDENTITY(1,1),
    [SkillId]     INT           NOT NULL,
    [Name]        NVARCHAR(50)  NOT NULL,
    [Value]       INT           NULL,
    [Description] NVARCHAR(150) NULL,
    CONSTRAINT [PK_SkillLevels] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_SkillLevels_Skills]
        FOREIGN KEY ([SkillId])
        REFERENCES [dbo].[Skills] ([Id])
        ON DELETE CASCADE
);

-- ============================================================
-- 3. Lookup Tables (referenced by HumanResources)
-- ============================================================

CREATE TABLE [dbo].[EmployeeCategories]
(
    [Id]          INT          NOT NULL IDENTITY(1,1),
    [Name]        NVARCHAR(30) NOT NULL,
    [Description] NVARCHAR(100) NULL,
    CONSTRAINT [PK_EmployeeCategories] PRIMARY KEY CLUSTERED ([Id] ASC)
);

CREATE TABLE [dbo].[EmployeeTypes]
(
    [Id]          INT          NOT NULL IDENTITY(1,1),
    [Name]        NVARCHAR(30) NOT NULL,
    [Description] NVARCHAR(100) NULL,
    CONSTRAINT [PK_EmployeeTypes] PRIMARY KEY CLUSTERED ([Id] ASC)
);

CREATE TABLE [dbo].[EmployeeVendors]
(
    [Id]          INT          NOT NULL IDENTITY(1,1),
    [Name]        NVARCHAR(30) NOT NULL,
    [Description] NVARCHAR(100) NULL,
    CONSTRAINT [PK_EmployeeVendors] PRIMARY KEY CLUSTERED ([Id] ASC)
);

CREATE TABLE [dbo].[EmployeeRoles]
(
    [Id]          INT          NOT NULL IDENTITY(1,1),
    [Name]        NVARCHAR(30) NOT NULL,
    [Description] NVARCHAR(100) NULL,
    CONSTRAINT [PK_EmployeeRoles] PRIMARY KEY CLUSTERED ([Id] ASC)
);

CREATE TABLE [dbo].[CorporateGrades]
(
    [Id]          INT          NOT NULL IDENTITY(1,1),
    [Name]        NVARCHAR(30) NOT NULL,
    [Description] NVARCHAR(100) NULL,
    CONSTRAINT [PK_CorporateGrades] PRIMARY KEY CLUSTERED ([Id] ASC)
);

CREATE TABLE [dbo].[Cities]
(
    [Id]          INT          NOT NULL IDENTITY(1,1),
    [Name]        NVARCHAR(70) NOT NULL,
    [Description] NVARCHAR(100) NULL,
    CONSTRAINT [PK_Cities] PRIMARY KEY CLUSTERED ([Id] ASC)
);

CREATE TABLE [dbo].[Countries]
(
    [Id]          INT          NOT NULL IDENTITY(1,1),
    [Name]        NVARCHAR(70) NOT NULL,
    [Description] NVARCHAR(100) NULL,
    CONSTRAINT [PK_Countries] PRIMARY KEY CLUSTERED ([Id] ASC)
);

CREATE TABLE [dbo].[TeamRoles]
(
    [Id]          INT          NOT NULL IDENTITY(1,1),
    [Name]        NVARCHAR(50) NOT NULL,
    [Description] NVARCHAR(100) NULL,
    CONSTRAINT [PK_TeamRoles] PRIMARY KEY CLUSTERED ([Id] ASC)
);

INSERT INTO [dbo].[TeamRoles] ([Name]) VALUES (N'Product Owner'), (N'Scrum Master'), (N'FT member');

-- ============================================================
-- 4. HumanResources
-- ============================================================
CREATE TABLE [dbo].[HumanResources]
(
    [Id]                 INT           NOT NULL IDENTITY(1,1),
    [IsActive]           BIT           NOT NULL CONSTRAINT [DF_HumanResources_IsActive] DEFAULT 1,
    [Cio]                NVARCHAR(50)  NULL,
    [Cio1]               NVARCHAR(50)  NULL,
    [Cio2]               NVARCHAR(50)  NULL,
    [EmployeeNumber]     NVARCHAR(30)  NULL,
    [EmployeeName]       NVARCHAR(70)  NOT NULL,
    [FullName]           NVARCHAR(100) NOT NULL,
    [LineManagerName]    NVARCHAR(100) NULL,
    [EmployeeCategoryId] INT           NULL,
    [EmployeeTypeId]     INT           NULL,
    [EmployeeRoleId]     INT           NULL,
    [EmployeeVendorId]   INT           NULL,
    [CityId]             INT           NULL,
    [CountryId]          INT           NULL,
    [CorporateGradeId]   INT           NULL,
    [TeamRoleId]         INT           NULL,
    CONSTRAINT [PK_HumanResources] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_HumanResources_EmployeeCategories]
        FOREIGN KEY ([EmployeeCategoryId])
        REFERENCES [dbo].[EmployeeCategories] ([Id])
        ON DELETE SET NULL,
    CONSTRAINT [FK_HumanResources_EmployeeTypes]
        FOREIGN KEY ([EmployeeTypeId])
        REFERENCES [dbo].[EmployeeTypes] ([Id])
        ON DELETE SET NULL,
    CONSTRAINT [FK_HumanResources_EmployeeRoles]
        FOREIGN KEY ([EmployeeRoleId])
        REFERENCES [dbo].[EmployeeRoles] ([Id])
        ON DELETE SET NULL,
    CONSTRAINT [FK_HumanResources_EmployeeVendors]
        FOREIGN KEY ([EmployeeVendorId])
        REFERENCES [dbo].[EmployeeVendors] ([Id])
        ON DELETE SET NULL,
    CONSTRAINT [FK_HumanResources_Cities]
        FOREIGN KEY ([CityId])
        REFERENCES [dbo].[Cities] ([Id])
        ON DELETE SET NULL,
    CONSTRAINT [FK_HumanResources_Countries]
        FOREIGN KEY ([CountryId])
        REFERENCES [dbo].[Countries] ([Id])
        ON DELETE SET NULL,
    CONSTRAINT [FK_HumanResources_CorporateGrades]
        FOREIGN KEY ([CorporateGradeId])
        REFERENCES [dbo].[CorporateGrades] ([Id])
        ON DELETE SET NULL,
    CONSTRAINT [FK_HumanResources_TeamRoles]
        FOREIGN KEY ([TeamRoleId])
        REFERENCES [dbo].[TeamRoles] ([Id])
        ON DELETE SET NULL
);

-- ── Performance indexes on HumanResources ──
CREATE NONCLUSTERED INDEX [IX_HumanResources_FullName]       ON [dbo].[HumanResources] ([FullName]);
CREATE NONCLUSTERED INDEX [IX_HumanResources_EmployeeName]   ON [dbo].[HumanResources] ([EmployeeName]);
CREATE NONCLUSTERED INDEX [IX_HumanResources_EmployeeNumber] ON [dbo].[HumanResources] ([EmployeeNumber]);
CREATE NONCLUSTERED INDEX [IX_HumanResources_IsActive]       ON [dbo].[HumanResources] ([IsActive]);

-- ============================================================
-- 5. HumanResourceSkills
-- ============================================================
CREATE TABLE [dbo].[HumanResourceSkills]
(
    [HumanResourceId] INT NOT NULL,
    [SkillId]         INT NOT NULL,
    [SkillLevelId]    INT NULL,
    CONSTRAINT [PK_HumanResourceSkills]
        PRIMARY KEY CLUSTERED ([HumanResourceId] ASC, [SkillId] ASC),
    CONSTRAINT [FK_HumanResourceSkills_HumanResources]
        FOREIGN KEY ([HumanResourceId])
        REFERENCES [dbo].[HumanResources] ([Id])
        ON DELETE CASCADE,
    CONSTRAINT [FK_HumanResourceSkills_Skills]
        FOREIGN KEY ([SkillId])
        REFERENCES [dbo].[Skills] ([Id])
        ON DELETE NO ACTION,
    CONSTRAINT [FK_HumanResourceSkills_SkillLevels]
        FOREIGN KEY ([SkillLevelId])
        REFERENCES [dbo].[SkillLevels] ([Id])
        ON DELETE SET NULL
);

-- ── Performance index on HumanResourceSkills ──
CREATE NONCLUSTERED INDEX [IX_HumanResourceSkills_HumanResourceId] ON [dbo].[HumanResourceSkills] ([HumanResourceId]);

-- ============================================================
-- 6. Teams
-- ============================================================
CREATE TABLE [dbo].[Teams]
(
    [Id]              INT           NOT NULL IDENTITY(1,1),
    [Name]            NVARCHAR(50)  NOT NULL,
    [FullName]        NVARCHAR(70)  NULL,
    [OptionalTeamTag] NVARCHAR(50)  NULL,
    [Description]     NVARCHAR(200) NULL,
    CONSTRAINT [PK_Teams] PRIMARY KEY CLUSTERED ([Id] ASC)
);

-- ============================================================
-- 7. TeamMembers  (Team <-> HumanResource)
-- ============================================================
CREATE TABLE [dbo].[TeamMembers]
(
    [TeamId]          INT NOT NULL,
    [HumanResourceId] INT NOT NULL,
    CONSTRAINT [PK_TeamMembers]
        PRIMARY KEY CLUSTERED ([TeamId] ASC, [HumanResourceId] ASC),
    CONSTRAINT [FK_TeamMembers_Teams]
        FOREIGN KEY ([TeamId])
        REFERENCES [dbo].[Teams] ([Id])
        ON DELETE CASCADE,
    CONSTRAINT [FK_TeamMembers_HumanResources]
        FOREIGN KEY ([HumanResourceId])
        REFERENCES [dbo].[HumanResources] ([Id])
        ON DELETE CASCADE
);

-- ── Performance index on TeamMembers ──
CREATE NONCLUSTERED INDEX [IX_TeamMembers_HumanResourceId] ON [dbo].[TeamMembers] ([HumanResourceId]);

-- ============================================================
-- 8. Pis
-- ============================================================
CREATE TABLE [dbo].[Pis]
(
    [Id]          INT           NOT NULL IDENTITY(1,1),
    [Name]        NVARCHAR(100) NOT NULL,
    [Description] NVARCHAR(500) NULL,
    [Priority]    NVARCHAR(50)  NULL,
    [Comments]    NVARCHAR(250) NULL,
    [StartDate]   DATETIME2     NULL,
    [EndDate]     DATETIME2     NULL,
    CONSTRAINT [PK_Pis] PRIMARY KEY CLUSTERED ([Id] ASC)
);

-- ============================================================
-- 9. CapitalProjects
-- ============================================================
CREATE TABLE [dbo].[CapitalProjects]
(
    [Id]          INT           NOT NULL IDENTITY(1,1),
    [JiraKey]     NVARCHAR(10)  NULL,
    [Name]        NVARCHAR(100) NOT NULL,
    [Description] NVARCHAR(500) NULL,
    CONSTRAINT [PK_CapitalProjects] PRIMARY KEY CLUSTERED ([Id] ASC)
);

-- ============================================================
-- 10. StrategicObjectives
-- ============================================================
CREATE TABLE [dbo].[StrategicObjectives]
(
    [Id]          INT           NOT NULL IDENTITY(1,1),
    [JiraId]      NVARCHAR(100) NULL,
    [Name]        NVARCHAR(100) NOT NULL,
    [Description] NVARCHAR(500) NULL,
    [Comments]    NVARCHAR(250) NULL,
    CONSTRAINT [PK_StrategicObjectives] PRIMARY KEY CLUSTERED ([Id] ASC)
);

-- ── Performance indexes on StrategicObjectives ──
CREATE NONCLUSTERED INDEX [IX_StrategicObjectives_JiraId] ON [dbo].[StrategicObjectives] ([JiraId]);
CREATE NONCLUSTERED INDEX [IX_StrategicObjectives_Name]   ON [dbo].[StrategicObjectives] ([Name]);

-- ============================================================
-- 11. CapitalProjectStrategicObjectives  (CapitalProject <-> StrategicObjective)
-- ============================================================
CREATE TABLE [dbo].[CapitalProjectStrategicObjectives]
(
    [CapitalProjectId]    INT NOT NULL,
    [StrategicObjectiveId] INT NOT NULL,
    CONSTRAINT [PK_CapitalProjectStrategicObjectives]
        PRIMARY KEY CLUSTERED ([CapitalProjectId] ASC, [StrategicObjectiveId] ASC),
    CONSTRAINT [FK_CapitalProjectStrategicObjectives_CapitalProjects]
        FOREIGN KEY ([CapitalProjectId])
        REFERENCES [dbo].[CapitalProjects] ([Id])
        ON DELETE CASCADE,
    CONSTRAINT [FK_CapitalProjectStrategicObjectives_StrategicObjectives]
        FOREIGN KEY ([StrategicObjectiveId])
        REFERENCES [dbo].[StrategicObjectives] ([Id])
        ON DELETE NO ACTION
);

-- ── Performance index on CapitalProjectStrategicObjectives ──
CREATE NONCLUSTERED INDEX [IX_CapitalProjectStrategicObjectives_StrategicObjectiveId]
    ON [dbo].[CapitalProjectStrategicObjectives] ([StrategicObjectiveId]);

-- ============================================================
-- 12. CapitalProjectTeams  (CapitalProject <-> Team)
-- ============================================================
CREATE TABLE [dbo].[CapitalProjectTeams]
(
    [CapitalProjectId] INT NOT NULL,
    [TeamId]           INT NOT NULL,
    CONSTRAINT [PK_CapitalProjectTeams]
        PRIMARY KEY CLUSTERED ([CapitalProjectId] ASC, [TeamId] ASC),
    CONSTRAINT [FK_CapitalProjectTeams_CapitalProjects]
        FOREIGN KEY ([CapitalProjectId])
        REFERENCES [dbo].[CapitalProjects] ([Id])
        ON DELETE CASCADE,
    CONSTRAINT [FK_CapitalProjectTeams_Teams]
        FOREIGN KEY ([TeamId])
        REFERENCES [dbo].[Teams] ([Id])
        ON DELETE NO ACTION
);

-- ── Performance index on CapitalProjectTeams ──
CREATE NONCLUSTERED INDEX [IX_CapitalProjectTeams_TeamId]
    ON [dbo].[CapitalProjectTeams] ([TeamId]);

-- ============================================================
-- 13. PortfolioEpics
-- ============================================================
CREATE TABLE [dbo].[PortfolioEpics]
(
    [Id]          INT           NOT NULL IDENTITY(1,1),
    [JiraId]      NVARCHAR(100) NULL,
    [Name]        NVARCHAR(100) NULL,
    [Description] NVARCHAR(500) NULL,
    [Comments]    NVARCHAR(250) NULL,
    CONSTRAINT [PK_PortfolioEpics] PRIMARY KEY CLUSTERED ([Id] ASC)
);

-- ── Performance indexes on PortfolioEpics ──
CREATE NONCLUSTERED INDEX [IX_PortfolioEpics_JiraId] ON [dbo].[PortfolioEpics] ([JiraId]);
CREATE NONCLUSTERED INDEX [IX_PortfolioEpics_Name]   ON [dbo].[PortfolioEpics] ([Name]);

-- ============================================================
-- 14. StrategicObjectivePortfolioEpics  (StrategicObjective <-> PortfolioEpic)
-- ============================================================
CREATE TABLE [dbo].[StrategicObjectivePortfolioEpics]
(
    [StrategicObjectiveId] INT NOT NULL,
    [PortfolioEpicId]      INT NOT NULL,
    CONSTRAINT [PK_StrategicObjectivePortfolioEpics]
        PRIMARY KEY CLUSTERED ([StrategicObjectiveId] ASC, [PortfolioEpicId] ASC),
    CONSTRAINT [FK_StrategicObjectivePortfolioEpics_StrategicObjectives]
        FOREIGN KEY ([StrategicObjectiveId])
        REFERENCES [dbo].[StrategicObjectives] ([Id])
        ON DELETE CASCADE,
    CONSTRAINT [FK_StrategicObjectivePortfolioEpics_PortfolioEpics]
        FOREIGN KEY ([PortfolioEpicId])
        REFERENCES [dbo].[PortfolioEpics] ([Id])
        ON DELETE NO ACTION
);

-- ── Performance index on StrategicObjectivePortfolioEpics ──
CREATE NONCLUSTERED INDEX [IX_StrategicObjectivePortfolioEpics_PortfolioEpicId]
    ON [dbo].[StrategicObjectivePortfolioEpics] ([PortfolioEpicId]);

-- ============================================================
-- 15. BusinessOutcomes
-- ============================================================
CREATE TABLE [dbo].[BusinessOutcomes]
(
    [Id]              INT           NOT NULL IDENTITY(1,1),
    [JiraId]          NVARCHAR(100) NULL,
    [Name]            NVARCHAR(200) NULL,
    [Description]     NVARCHAR(500) NULL,
    [Comments]        NVARCHAR(250) NULL,
    [Ranking]         INT           NULL,
    [ArtName]         NVARCHAR(200) NULL,
    [PortfolioEpicId] INT           NULL,
    CONSTRAINT [PK_BusinessOutcomes] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_BusinessOutcomes_PortfolioEpics]
        FOREIGN KEY ([PortfolioEpicId])
        REFERENCES [dbo].[PortfolioEpics] ([Id])
        ON DELETE SET NULL
);

-- ── Performance indexes on BusinessOutcomes ──
CREATE NONCLUSTERED INDEX [IX_BusinessOutcomes_Name]             ON [dbo].[BusinessOutcomes] ([Name]);
CREATE NONCLUSTERED INDEX [IX_BusinessOutcomes_JiraId]           ON [dbo].[BusinessOutcomes] ([JiraId]);
CREATE NONCLUSTERED INDEX [IX_BusinessOutcomes_Ranking]          ON [dbo].[BusinessOutcomes] ([Ranking]);
CREATE NONCLUSTERED INDEX [IX_BusinessOutcomes_ArtName]          ON [dbo].[BusinessOutcomes] ([ArtName]);
CREATE NONCLUSTERED INDEX [IX_BusinessOutcomes_PortfolioEpicId]  ON [dbo].[BusinessOutcomes] ([PortfolioEpicId]);

-- ============================================================
-- 16. UnfundedOptions
-- ============================================================
CREATE TABLE [dbo].[UnfundedOptions]
(
    [Id]          INT           NOT NULL IDENTITY(1,1),
    [Name]        NVARCHAR(50)  NOT NULL,
    [Description] NVARCHAR(150) NULL,
    [Order]       INT           NOT NULL CONSTRAINT [DF_UnfundedOptions_Order] DEFAULT 0,
    CONSTRAINT [PK_UnfundedOptions] PRIMARY KEY CLUSTERED ([Id] ASC)
);

-- ============================================================
-- 17. Features
-- ============================================================
CREATE TABLE [dbo].[Features]
(
    [Id]                INT            NOT NULL IDENTITY(1,1),
    [JiraId]            NVARCHAR(100)  NULL,
    [ProjectKey]        NVARCHAR(10)   NULL,
    [IssueType]         NVARCHAR(50)   NULL,
    [Summary]           NVARCHAR(255)  NOT NULL,
    [Name]              NVARCHAR(200)  NULL,
    [Description]       NVARCHAR(MAX)  NULL,
    [Labels]            NVARCHAR(2000) NULL,
    [Comments]          NVARCHAR(250)  NULL,
    [Ranking]           INT            NULL,
    [UnfundedOptionId]  INT            NULL,
    [DateExpected]      DATETIME2      NULL,
    [IsLinkedToTheJira] BIT            NULL,
    [BusinessOutcomeId] INT            NULL,
    [PiId]              INT            NULL,
    CONSTRAINT [PK_Features] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_Features_UnfundedOptions]
        FOREIGN KEY ([UnfundedOptionId])
        REFERENCES [dbo].[UnfundedOptions] ([Id])
        ON DELETE SET NULL,
    CONSTRAINT [FK_Features_BusinessOutcomes]
        FOREIGN KEY ([BusinessOutcomeId])
        REFERENCES [dbo].[BusinessOutcomes] ([Id])
        ON DELETE SET NULL,
    CONSTRAINT [FK_Features_Pis]
        FOREIGN KEY ([PiId])
        REFERENCES [dbo].[Pis] ([Id])
        ON DELETE SET NULL
);

-- ── Performance indexes on Features ──
CREATE NONCLUSTERED INDEX [IX_Features_JiraId]            ON [dbo].[Features] ([JiraId]);
CREATE NONCLUSTERED INDEX [IX_Features_ProjectKey]        ON [dbo].[Features] ([ProjectKey]);
CREATE NONCLUSTERED INDEX [IX_Features_Ranking]           ON [dbo].[Features] ([Ranking]);
CREATE NONCLUSTERED INDEX [IX_Features_Name]              ON [dbo].[Features] ([Name]);
CREATE NONCLUSTERED INDEX [IX_Features_BusinessOutcomeId] ON [dbo].[Features] ([BusinessOutcomeId]);
CREATE NONCLUSTERED INDEX [IX_Features_PiId]              ON [dbo].[Features] ([PiId]);
CREATE NONCLUSTERED INDEX [IX_Features_UnfundedOptionId]  ON [dbo].[Features] ([UnfundedOptionId]);

-- ============================================================
-- 18. FeatureTeams  (Feature <-> Team)
-- ============================================================
CREATE TABLE [dbo].[FeatureTeams]
(
    [FeatureId] INT NOT NULL,
    [TeamId]    INT NOT NULL,
    CONSTRAINT [PK_FeatureTeams]
        PRIMARY KEY CLUSTERED ([FeatureId] ASC, [TeamId] ASC),
    CONSTRAINT [FK_FeatureTeams_Features]
        FOREIGN KEY ([FeatureId])
        REFERENCES [dbo].[Features] ([Id])
        ON DELETE CASCADE,
    CONSTRAINT [FK_FeatureTeams_Teams]
        FOREIGN KEY ([TeamId])
        REFERENCES [dbo].[Teams] ([Id])
        ON DELETE NO ACTION
);

-- ============================================================
-- 19. TechnologyStacks
-- ============================================================
CREATE TABLE [dbo].[TechnologyStacks]
(
    [Id]          INT            NOT NULL IDENTITY(1,1),
    [Name]        NVARCHAR(100)  NOT NULL,
    [Description] NVARCHAR(200)  NULL,
    CONSTRAINT [PK_TechnologyStacks] PRIMARY KEY CLUSTERED ([Id] ASC)
);

-- ============================================================
-- 20. TechnologyStackSkills  (TechnologyStack <-> Skill)
-- ============================================================
CREATE TABLE [dbo].[TechnologyStackSkills]
(
    [TechnologyStackId] INT NOT NULL,
    [SkillId]           INT NOT NULL,
    CONSTRAINT [PK_TechnologyStackSkills]
        PRIMARY KEY CLUSTERED ([TechnologyStackId] ASC, [SkillId] ASC),
    CONSTRAINT [FK_TechnologyStackSkills_TechnologyStacks]
        FOREIGN KEY ([TechnologyStackId])
        REFERENCES [dbo].[TechnologyStacks] ([Id])
        ON DELETE CASCADE,
    CONSTRAINT [FK_TechnologyStackSkills_Skills]
        FOREIGN KEY ([SkillId])
        REFERENCES [dbo].[Skills] ([Id])
        ON DELETE NO ACTION
);

CREATE NONCLUSTERED INDEX [IX_TechnologyStackSkills_SkillId]
    ON [dbo].[TechnologyStackSkills] ([SkillId]);

-- ============================================================
-- 21. FeatureTechnologyStacks  (Feature -> TechnologyStack with EstimatedEffort)
-- ============================================================
CREATE TABLE [dbo].[FeatureTechnologyStacks]
(
    [Id]                  INT NOT NULL IDENTITY(1,1),
    [FeatureId]           INT NOT NULL,
    [TechnologyStackId]   INT NOT NULL,
    [EstimatedEffort]     INT NULL,
    CONSTRAINT [PK_FeatureTechnologyStacks] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_FeatureTechnologyStacks_Features]
        FOREIGN KEY ([FeatureId])
        REFERENCES [dbo].[Features] ([Id])
        ON DELETE CASCADE,
    CONSTRAINT [FK_FeatureTechnologyStacks_TechnologyStacks]
        FOREIGN KEY ([TechnologyStackId])
        REFERENCES [dbo].[TechnologyStacks] ([Id])
        ON DELETE NO ACTION
);

CREATE NONCLUSTERED INDEX [IX_FeatureTechnologyStacks_FeatureId]
    ON [dbo].[FeatureTechnologyStacks] ([FeatureId]);
CREATE NONCLUSTERED INDEX [IX_FeatureTechnologyStacks_FeatureId_TechnologyStackId]
    ON [dbo].[FeatureTechnologyStacks] ([FeatureId], [TechnologyStackId]);

-- ============================================================
-- 22. TeamTechnologyStacks  (Team <-> TechnologyStack)
-- ============================================================
CREATE TABLE [dbo].[TeamTechnologyStacks]
(
    [TeamId]            INT NOT NULL,
    [TechnologyStackId] INT NOT NULL,
    CONSTRAINT [PK_TeamTechnologyStacks]
        PRIMARY KEY CLUSTERED ([TeamId] ASC, [TechnologyStackId] ASC),
    CONSTRAINT [FK_TeamTechnologyStacks_Teams]
        FOREIGN KEY ([TeamId])
        REFERENCES [dbo].[Teams] ([Id])
        ON DELETE CASCADE,
    CONSTRAINT [FK_TeamTechnologyStacks_TechnologyStacks]
        FOREIGN KEY ([TechnologyStackId])
        REFERENCES [dbo].[TechnologyStacks] ([Id])
        ON DELETE CASCADE
);

CREATE NONCLUSTERED INDEX [IX_TeamTechnologyStacks_TechnologyStackId]
    ON [dbo].[TeamTechnologyStacks] ([TechnologyStackId]);


CREATE TABLE [dbo].[JiraTokens] (
    [Id]                INT            IDENTITY(1,1) NOT NULL,
    [UserName]          NVARCHAR(256)  NOT NULL,
    [AccessToken]       NVARCHAR(2000) NOT NULL,
    [AccessTokenSecret] NVARCHAR(2000) NOT NULL,
    [Created]           DATETIME2      NOT NULL,
    [Updated]           DATETIME2      NULL,
    CONSTRAINT [PK_JiraTokens] PRIMARY KEY CLUSTERED ([Id])
);

CREATE UNIQUE INDEX [IX_JiraTokens_UserName] ON [dbo].[JiraTokens] ([UserName]);

-- ============================================================
-- Done
-- ============================================================
PRINT 'Schema creation complete.';
