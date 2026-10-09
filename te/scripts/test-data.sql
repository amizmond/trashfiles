-- =====================================================================================================
-- Capital PI Tool - local test data
--
-- Loads a coherent, self-consistent test data set into a LOCAL, already-migrated estimation database so
-- every page can be tested without PROD or Jira. All plan dates are relative to the day the script runs
-- (today is always inside sprint 2 of the current PI), so the data never goes stale.
--
-- WARNING: deletes ALL planning, resource, feature, risk, user (except you), profile, audit and Jira sync data
-- (sync keys, sync history, label caches, issue links), the global message and all global filters (yours too)
-- in the current database before loading. Kept: migrations, AppPages, seeded TechnicalApprovals,
-- BackupSettings, BackupHistory, JiraSyncSettings (disabled, LastRunAt set from the test history).
-- HolidayTypes are reset to the seed.
-- Jira connections (JiraTokens) are deleted too, so the app cannot read or write your Jira for the test keys
-- (PAY-, MOB-, RISK-, DATA-, CRM-). Do not reconnect Jira on this database.
--
-- Run with Windows authentication against the local database, with the app stopped (it caches skills,
-- teams, stacks and the dashboard for a few minutes), e.g. from PowerShell:
--   & "C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\sqlcmd.exe" `
--       -S TORADORA -d db_estimation -E -C -b -I -i scripts\test-data.sql
-- In SSMS: run the whole file at once in a new query window.
-- Your Windows login (SUSER_SNAME()) is kept / made an approved admin, so you can sign in afterwards.
-- The app must have been started once on this database (creates the ArtPlanning tables); otherwise the
-- ART capacity settings are skipped.
-- =====================================================================================================
SET NOEXEC OFF;
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;

DECLARE @Guard nvarchar(400);
IF OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U') IS NULL
    SET @Guard = N'This is not an estimation database (no __EFMigrationsHistory). Connect to your local estimation database and run the script again.';
ELSE IF NOT EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId = N'20261009130717_ArtPrioritizationGeneralOrder')
    SET @Guard = N'This database is not migrated to 20261009130717_ArtPrioritizationGeneralOrder. Start the app once (it migrates on startup) and run the script again.';
ELSE IF ISNULL(CONVERT(nvarchar(48), CONNECTIONPROPERTY('client_net_address')), N'') NOT IN (N'<local machine>', N'127.0.0.1', N'::1')
    AND ISNULL(CONVERT(nvarchar(128), SERVERPROPERTY('MachineName')), N'') <> ISNULL(HOST_NAME(), N'')
    SET @Guard = N'This script wipes the database: run it on your own machine against your local SQL Server.';
ELSE IF CHARINDEX(N'\', SUSER_SNAME()) = 0
    SET @Guard = N'Run this script with Windows authentication: SUSER_SNAME() must be DOMAIN\user so your login stays an admin of the app.';
IF @Guard IS NOT NULL
BEGIN
    RAISERROR(@Guard, 16, 1);
    SET NOEXEC ON;
END;
GO

DECLARE @Section nvarchar(200) = N'00-header';
DECLARE @Dev nvarchar(256) = SUSER_SNAME();
DECLARE @NowUtc datetime2 = SYSUTCDATETIME();
DECLARE @Today date = CAST(GETDATE() AS date);
DECLARE @WeekStart date = DATEADD(day, -(DATEDIFF(day, '19000101', @Today) % 7), @Today);
DECLARE @Pi3Start date = DATEADD(day, -21, @WeekStart);
DECLARE @Pi1Start date = DATEADD(day, -168, @Pi3Start);
DECLARE @Pi2Start date = DATEADD(day, -84, @Pi3Start);
DECLARE @Pi4Start date = DATEADD(day, 84, @Pi3Start);
DECLARE @Pi1End date = DATEADD(day, 83, @Pi1Start);
DECLARE @Pi2End date = DATEADD(day, 83, @Pi2Start);
DECLARE @Pi3End date = DATEADD(day, 83, @Pi3Start);
DECLARE @Pi4End date = DATEADD(day, 83, @Pi4Start);
DECLARE @Pi1Code nvarchar(10) = RIGHT(CONVERT(nvarchar(4), YEAR(@Pi1Start)), 2) + N'.' + RIGHT(N'0' + CONVERT(nvarchar(2), MONTH(@Pi1Start)), 2);
DECLARE @Pi2Code nvarchar(10) = RIGHT(CONVERT(nvarchar(4), YEAR(@Pi2Start)), 2) + N'.' + RIGHT(N'0' + CONVERT(nvarchar(2), MONTH(@Pi2Start)), 2);
DECLARE @Pi3Code nvarchar(10) = RIGHT(CONVERT(nvarchar(4), YEAR(@Pi3Start)), 2) + N'.' + RIGHT(N'0' + CONVERT(nvarchar(2), MONTH(@Pi3Start)), 2);
DECLARE @Pi4Code nvarchar(10) = RIGHT(CONVERT(nvarchar(4), YEAR(@Pi4Start)), 2) + N'.' + RIGHT(N'0' + CONVERT(nvarchar(2), MONTH(@Pi4Start)), 2);
DECLARE @Pi1Name nvarchar(100) = N'PI ' + @Pi1Code;
DECLARE @Pi2Name nvarchar(100) = N'PI ' + @Pi2Code;
DECLARE @Pi3Name nvarchar(100) = N'PI ' + @Pi3Code;
DECLARE @Pi4Name nvarchar(100) = N'PI ' + @Pi4Code;
DECLARE @CurPiLabel nvarchar(50) = N'pi-' + @Pi3Code;
DECLARE @NextPiLabel nvarchar(50) = N'pi-' + @Pi4Code;

DROP TABLE IF EXISTS #Pi;
CREATE TABLE #Pi (PiId int NOT NULL PRIMARY KEY, Code nvarchar(10) NOT NULL, Name nvarchar(100) NOT NULL, StartDate date NOT NULL, EndDate date NOT NULL);
INSERT INTO #Pi (PiId, Code, Name, StartDate, EndDate) VALUES
    (1, @Pi1Code, @Pi1Name, @Pi1Start, @Pi1End),
    (2, @Pi2Code, @Pi2Name, @Pi2Start, @Pi2End),
    (3, @Pi3Code, @Pi3Name, @Pi3Start, @Pi3End),
    (4, @Pi4Code, @Pi4Name, @Pi4Start, @Pi4End);

DROP TABLE IF EXISTS #N;
CREATE TABLE #N (n int NOT NULL PRIMARY KEY);
INSERT INTO #N (n)
SELECT TOP (2000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) - 1
FROM sys.all_objects a CROSS JOIN sys.all_objects b;

PRINT CONCAT(N'Loading test data into ', DB_NAME(), N' on ', CONVERT(nvarchar(128), SERVERPROPERTY('ServerName')), N' for ', @Dev, N'. Current PI: ', @Pi3Name, N' (', CONVERT(nchar(10), @Pi3Start, 23), N' .. ', CONVERT(nchar(10), @Pi3End, 23), N').');

BEGIN TRY
    BEGIN TRANSACTION;

    SET @Section = N'00-header: wipe';
    DELETE dbo.IssueLinks;
    DELETE dbo.JiraLabelCaches;
    DELETE dbo.JiraSyncHistory;
    DELETE dbo.JiraSyncKeys;
    DELETE dbo.JiraTokens;
    DELETE dbo.FeatureStateApprovals;
    DELETE dbo.FeatureSnapshotItems;
    DELETE dbo.FeatureSnapshots;
    DELETE dbo.FeatureHygieneRules;
    DELETE dbo.RiskComments;
    DELETE dbo.RiskFeatures;
    DELETE dbo.RiskCapitalProjects;
    DELETE dbo.Risks;
    DELETE dbo.FeatureComments;
    DELETE dbo.TeamCapacityFeatureOrders;
    DELETE dbo.ArtPrioritizationOrders;
    DELETE dbo.FeatureTeamSprints;
    DELETE dbo.FeatureTeamStackSkills;
    DELETE dbo.FeatureTeamTechnologyStacks;
    DELETE dbo.FeatureSkills;
    DELETE dbo.FeatureTeams;
    DELETE dbo.Features;
    DELETE dbo.RequirementStatuses;
    DELETE dbo.PiObjectives;
    DELETE dbo.TechnicalApprovals WHERE Id > 3;
    DELETE dbo.Holidays;
    DELETE dbo.HolidayTypes;
    DELETE dbo.PublicHolidays;
    DELETE dbo.TeamMemberCoefficients;
    DELETE dbo.TeamMemberTechnologyStacks;
    DELETE dbo.TeamTechnologyStacks;
    DELETE dbo.TeamMembers;
    DELETE dbo.Sprints;
    DELETE dbo.CapitalProjectSprints;
    DELETE dbo.CapitalProjectTeams;
    DELETE dbo.CapitalProjectStrategicObjectives;
    DELETE dbo.StrategicObjectivePortfolioEpics;
    DELETE dbo.BusinessOutcomes;
    DELETE dbo.PortfolioEpics;
    DELETE dbo.StrategicObjectives;
    DELETE dbo.UnfundedOptions;
    DELETE dbo.ProfilePagePermissions;
    DELETE dbo.ProfileActionPermissions;
    DELETE dbo.AppUserProfiles;
    DELETE dbo.Profiles;
    DELETE dbo.UserGlobalFilters;
    DELETE dbo.GlobalMessages;
    DELETE dbo.AppUsers WHERE WindowsUserName <> @Dev;
    DELETE dbo.Teams;
    DELETE dbo.HumanResourceSkills;
    DELETE dbo.HumanResources;
    DELETE dbo.TechnologyStackSkills;
    DELETE dbo.TechnologyStacks;
    DELETE dbo.SkillLevels;
    DELETE dbo.Skills;
    DELETE dbo.TeamRoles;
    DELETE dbo.EmployeeCategories;
    DELETE dbo.EmployeeTypes;
    DELETE dbo.EmployeeVendors;
    DELETE dbo.EmployeeRoles;
    DELETE dbo.CorporateGrades;
    DELETE dbo.Cities;
    DELETE dbo.Countries;
    DELETE dbo.CapitalProjectJiraKeys;
    DELETE dbo.CapitalProjects;
    DELETE dbo.Departments;
    DELETE dbo.Pis;
    DELETE dbo.AuditLogs;
    IF OBJECT_ID(N'dbo.ArtPlanningSkillRankings', N'U') IS NOT NULL
        EXEC (N'DELETE dbo.ArtPlanningSkillRankings; DELETE dbo.ArtPlanningSkillLevelAllocations; DELETE dbo.ArtPlanningRoleCoefficients; DELETE dbo.ArtPlanningRoleSkillLimits; DELETE dbo.ArtPlanningTrainCoefficients;');


    -- 10-train: departments, lookups, PIs, ARTs, Jira keys, ART sprints, SO/PE/BO hierarchy
    SET @Section = N'10-train';
    PRINT N'Train: departments, lookups, PIs, ARTs, Jira keys, ART sprints, strategic objectives, epics, outcomes';

    DECLARE @a_nl nchar(2) = NCHAR(13) + NCHAR(10);

    -- departments
    SET IDENTITY_INSERT dbo.Departments ON;
    INSERT INTO dbo.Departments (Id, [Name], Description) VALUES
        (1, N'Retail Banking Solution Train', N'Payments, cards and mobile channels for retail customers'),
        (2, N'Risk and Finance Solution Train', N'Regulatory capital, credit risk and finance reporting'),
        (3, N'Group Technology', N'Shared platforms and data'),
        (4, N'Corporate Banking', NULL);
    SET IDENTITY_INSERT dbo.Departments OFF;

    -- lookups
    SET IDENTITY_INSERT dbo.UnfundedOptions ON;
    INSERT INTO dbo.UnfundedOptions (Id, [Name], Description, [Order]) VALUES
        (1, N'Partially Funded', N'Funding approved for part of the scope', 1),
        (2, N'Unfunded', N'No budget approved yet', 2),
        (3, N'Pending Approval', N'Business case submitted to the investment board', 3),
        (4, N'Deferred', NULL, 4);
    SET IDENTITY_INSERT dbo.UnfundedOptions OFF;

    SET IDENTITY_INSERT dbo.RequirementStatuses ON;
    INSERT INTO dbo.RequirementStatuses (Id, [Name]) VALUES
        (1, N'Draft'),
        (2, N'In Analysis'),
        (3, N'Ready for Review'),
        (4, N'Approved'),
        (5, N'Committed'),
        (6, N'On Hold');
    SET IDENTITY_INSERT dbo.RequirementStatuses OFF;

    SET IDENTITY_INSERT dbo.PiObjectives ON;
    INSERT INTO dbo.PiObjectives (Id, [Name]) VALUES
        (1, N'Launch instant payments MVP'),
        (2, N'Tokenise 80% of card traffic'),
        (3, N'Mobile onboarding under 5 minutes'),
        (4, N'Basel IV RWA parallel run'),
        (5, N'Retire legacy risk warehouse'),
        (6, N'Data lake ingestion for core systems'),
        (7, N'Reduce P1 incidents by 30%'),
        (8, N'Accessibility AA compliance');
    SET IDENTITY_INSERT dbo.PiObjectives OFF;

    -- PIs
    SET IDENTITY_INSERT dbo.Pis ON;
    INSERT INTO dbo.Pis (Id, [Name], Description, Priority, Comments, StartDate, EndDate, FeatureLabels, LabelMatchMode, IsLocked)
    SELECT p.PiId, p.Name,
        CASE p.PiId
            WHEN 1 THEN N'Foundations: instant payments MVP and Basel IV impact analysis'
            WHEN 2 THEN N'Tokenisation pilot, RWA engine build and first data lake feeds'
            WHEN 3 THEN N'Instant payments rollout, mobile onboarding redesign, RWA parallel run'
            WHEN 4 THEN N'Scale-out of tokenisation and legacy risk decommissioning'
        END,
        CASE p.PiId WHEN 2 THEN N'High' WHEN 3 THEN N'High' ELSE N'Medium' END,
        CASE p.PiId
            WHEN 1 THEN N'Locked after inspect and adapt'
            WHEN 2 THEN N'Locked; automatic snapshots taken at lock'
            WHEN 4 THEN N'Draft plan; features need both labels to join by label'
        END,
        p.StartDate, p.EndDate,
        CASE p.PiId WHEN 3 THEN @CurPiLabel WHEN 4 THEN @NextPiLabel + N',committed' END,
        CASE p.PiId WHEN 4 THEN 1 ELSE 0 END,
        CASE p.PiId WHEN 1 THEN 1 WHEN 2 THEN 1 WHEN 4 THEN 0 END
    FROM #Pi AS p;
    INSERT INTO dbo.Pis (Id, [Name], Description, Priority, Comments, StartDate, EndDate, FeatureLabels, LabelMatchMode, IsLocked)
    VALUES (5, N'Backlog', N'Unscheduled work without PI dates', NULL, NULL, NULL, NULL, NULL, 0, NULL);
    SET IDENTITY_INSERT dbo.Pis OFF;

    -- ARTs and Jira keys
    SET IDENTITY_INSERT dbo.CapitalProjects ON;
    INSERT INTO dbo.CapitalProjects (Id, [Name], Description, DepartmentId, Prioritization) VALUES
        (1, N'Core Payments', N'Payment hub, instant payments and card processing', 1, 1),
        (2, N'Mobile Banking', N'Retail mobile apps and their backend services', 1, NULL),
        (3, N'Risk Analytics', N'Regulatory capital and credit risk models', 2, NULL),
        (4, N'Risk Legacy Migration', N'Migration off the legacy risk warehouse', 2, NULL),
        (5, N'Data Platform', N'Enterprise data lake and analytics', 3, 2),
        (6, N'Shared Services', NULL, NULL, NULL);
    SET IDENTITY_INSERT dbo.CapitalProjects OFF;

    SET IDENTITY_INSERT dbo.CapitalProjectJiraKeys ON;
    INSERT INTO dbo.CapitalProjectJiraKeys (Id, CapitalProjectId, JiraKey, Components, Labels) VALUES
        (1, 1, N'PAY', NULL, NULL),
        (2, 2, N'PAY', N'Mobile,iOS,Android', NULL),
        (3, 2, N'MOB', NULL, NULL),
        (4, 3, N'RISK', NULL, N'!Legacy,!(empty)'),
        (5, 4, N'RISK', NULL, N'Legacy'),
        (6, 5, N'DATA', N'(empty)', NULL),
        (7, 5, N'PAY', NULL, N'data');
    SET IDENTITY_INSERT dbo.CapitalProjectJiraKeys OFF;

    -- ART sprints
    DROP TABLE IF EXISTS #a_ArtPi;
    CREATE TABLE #a_ArtPi (ArtId int NOT NULL, PiId int NOT NULL, PRIMARY KEY (ArtId, PiId));
    INSERT INTO #a_ArtPi (ArtId, PiId) VALUES
        (1, 2), (1, 3), (1, 4),
        (2, 2), (2, 3), (2, 4),
        (3, 2), (3, 3), (3, 4),
        (4, 3), (4, 4),
        (5, 3), (5, 4);

    DROP TABLE IF EXISTS #a_Sprint;
    CREATE TABLE #a_Sprint (Id int NOT NULL PRIMARY KEY, ArtId int NOT NULL, PiId int NOT NULL, S int NOT NULL, Code nvarchar(10) NOT NULL, StartDate date NOT NULL, EndDate date NOT NULL);
    INSERT INTO #a_Sprint (Id, ArtId, PiId, S, Code, StartDate, EndDate)
    SELECT ap.ArtId * 100 + ap.PiId * 10 + n.n, ap.ArtId, ap.PiId, n.n, p.Code,
        DATEADD(day, 14 * (n.n - 1) + CASE WHEN ap.ArtId = 4 AND ap.PiId = 4 AND n.n = 6 THEN 1 ELSE 0 END, p.StartDate),
        CASE WHEN n.n = 6 THEN p.EndDate ELSE DATEADD(day, 14 * (n.n - 1) + 13, p.StartDate) END
    FROM #a_ArtPi AS ap
    JOIN #Pi AS p ON p.PiId = ap.PiId
    JOIN #N AS n ON n.n BETWEEN 1 AND 6;

    SET IDENTITY_INSERT dbo.CapitalProjectSprints ON;
    INSERT INTO dbo.CapitalProjectSprints (Id, CapitalProjectId, PiId, [Name], StartDate, EndDate, IsIpSprint, FixVersion, UatStart, UatEnd, SignOffDate, ReleaseDate)
    SELECT x.Id, x.ArtId, x.PiId, x.SprintName, x.StartDate, x.EndDate, x.IsIp, x.FixVersion, x.UatStart, x.UatEnd,
        CASE
            WHEN x.ArtId = 1 AND x.S <= 5 THEN x.UatEnd
            WHEN x.ArtId = 2 AND x.S IN (3, 5) THEN x.EndDate
            WHEN x.ArtId = 3 AND x.S = 5 THEN x.UatEnd
        END,
        CASE
            WHEN x.ArtId = 1 AND x.S <= 5 THEN DATEADD(day, 3, x.UatEnd)
            WHEN x.ArtId = 2 AND x.S IN (3, 5) THEN DATEADD(day, 2, x.EndDate)
            WHEN x.ArtId = 3 AND x.S = 5 THEN DATEADD(day, 3, x.UatEnd)
        END
    FROM (
        SELECT s.Id, s.ArtId, s.PiId, s.S, s.StartDate, s.EndDate,
            s.Code + N'.' + CASE WHEN s.S = 6 THEN N'IP' ELSE CAST(s.S AS nvarchar(2)) END AS SprintName,
            CAST(CASE WHEN s.S = 6 THEN 1 ELSE 0 END AS bit) AS IsIp,
            CASE
                WHEN s.ArtId = 1 AND s.S <= 5 THEN N'PAY ' + s.Code + N'.' + CAST(s.S AS nvarchar(2))
                WHEN s.ArtId = 2 AND s.S = 3 THEN N'MOB ' + s.Code + N'.R1'
                WHEN s.ArtId = 2 AND s.S = 5 THEN N'MOB ' + s.Code + N'.R2'
                WHEN s.ArtId = 3 AND s.S = 5 THEN N'RISK ' + s.Code
            END AS FixVersion,
            CASE
                WHEN s.ArtId IN (1, 3) AND s.S <= 5 THEN DATEADD(day, 1, s.EndDate)
                WHEN s.ArtId = 5 AND s.S IN (2, 4) THEN DATEADD(day, 1, s.EndDate)
            END AS UatStart,
            CASE
                WHEN s.ArtId IN (1, 3) AND s.S <= 5 THEN DATEADD(day, 7, s.EndDate)
                WHEN s.ArtId = 5 AND s.S IN (2, 4) THEN DATEADD(day, 5, s.EndDate)
            END AS UatEnd
        FROM #a_Sprint AS s
    ) AS x;
    SET IDENTITY_INSERT dbo.CapitalProjectSprints OFF;

    -- strategic objectives
    SET IDENTITY_INSERT dbo.StrategicObjectives ON;
    INSERT INTO dbo.StrategicObjectives (Id, JiraId, ProjectKey, IssueType, Summary, Description, Labels, Status, JiraUpdated, TargetStart, TargetEnd) VALUES
        (1, N'STRAT-1', N'STRAT', N'Strategic Objective', N'Grow digital payments revenue',
            CONCAT(N'h3. Objective', @a_nl, N'Grow revenue from instant and card payments processed on the new payment hub.', @a_nl, @a_nl,
                N'h3. Key results', @a_nl, N'* Instant payments live for all retail customers', @a_nl, N'* Card tokenisation covering *80%* of card traffic', @a_nl, N'* Legacy payment hub switched off'),
            N'payments,growth', N'In Progress', DATEADD(hour, -26, @NowUtc), @Pi2Start, @Pi4End),
        (2, N'STRAT-2', N'STRAT', N'Strategic Objective', N'Mobile-first customer experience',
            N'Make the mobile app the primary channel for everyday banking.',
            N'mobile,customer-experience', N'In Progress', DATEADD(hour, -50, @NowUtc), @Pi2Start, @Pi4End),
        (3, N'STRAT-3', N'STRAT', N'Strategic Objective', N'Regulatory compliance',
            CONCAT(N'h3. Scope', @a_nl, N'||Regulation||Owner||', @a_nl, N'|Basel IV|Risk Analytics|', @a_nl, N'|Legacy decommissioning|Risk Legacy Migration|', @a_nl, @a_nl,
                N'_All regulatory items are mandatory and cannot be descoped._'),
            N'regulatory,basel', N'In Progress', DATEADD(hour, -74, @NowUtc), @Pi1Start, NULL),
        (4, N'STRAT-4', N'STRAT', N'Strategic Objective', N'Modern data platform',
            N'Replace departmental data marts with one governed enterprise data lake.',
            N'data-platform', N'To Do', DATEADD(day, -6, @NowUtc), NULL, NULL),
        (5, N'STRAT-5', N'STRAT', N'Strategic Objective', N'Operational resilience',
            CONCAT(N'Reduce the number and impact of production incidents.', @a_nl, @a_nl, N'See {{runbook-index}} for the current service map.'),
            NULL, N'In Progress', DATEADD(day, -9, @NowUtc), NULL, NULL),
        (6, N'RISK-9006', N'RISK', N'Strategic Objective', N'Legacy decommissioning',
            N'Retire the remaining legacy risk applications once their data is migrated.',
            N'basel', N'To Do', DATEADD(day, -14, @NowUtc), @Pi4Start, @Pi4End);
    SET IDENTITY_INSERT dbo.StrategicObjectives OFF;

    -- portfolio epics
    SET IDENTITY_INSERT dbo.PortfolioEpics ON;
    INSERT INTO dbo.PortfolioEpics (Id, JiraId, ProjectKey, IssueType, Summary, Description, Labels, Status, JiraUpdated, TargetStart, TargetEnd, Comments, L6Owner, UnfundedOptionId) VALUES
        (1, N'EPIC-11', N'EPIC', N'Portfolio Epic', N'Instant payments',
            CONCAT(N'h3. Goal', @a_nl, N'Real-time account-to-account payments, 24x7.', @a_nl, @a_nl, N'* Scheme connectivity', @a_nl, N'* Fraud screening in the payment flow'),
            N'payments,instant', N'In Progress', DATEADD(hour, -20, @NowUtc), @Pi2Start, @Pi3End, N'Board priority; funding confirmed for two PIs', N'Martin Vale', NULL),
        (2, N'EPIC-12', N'EPIC', N'Portfolio Epic', N'Card tokenisation',
            N'Replace card numbers with tokens across all channels.',
            N'cards,security', N'In Progress', DATEADD(hour, -44, @NowUtc), @Pi2Start, @Pi4End, NULL, N'Sofia Brandt', NULL),
        (3, N'EPIC-13', N'EPIC', N'Portfolio Epic', N'Mobile onboarding',
            N'New customers open an account in the app in under five minutes.',
            N'mobile,onboarding', N'Analysis', DATEADD(hour, -68, @NowUtc), @Pi3Start, @Pi4End, N'Waiting for architecture sign-off', NULL, NULL),
        (4, N'EPIC-14', N'EPIC', N'Portfolio Epic', N'RWA calculation engine',
            CONCAT(N'h3. Goal', @a_nl, N'Basel IV compliant risk-weighted asset calculation.', @a_nl, @a_nl, N'# Parallel run', @a_nl, N'# Regulator sign-off', @a_nl, N'# Switch-over'),
            N'basel,rwa', N'In Progress', DATEADD(day, -4, @NowUtc), @Pi1Start, @Pi4End, N'Regulatory deadline, cannot slip', N'Owen Hale', NULL),
        (5, N'EPIC-15', N'EPIC', N'Portfolio Epic', N'Credit risk model refresh',
            N'Recalibrate IRB models and add stress testing.',
            N'basel,models', N'Analysis', DATEADD(day, -5, @NowUtc), NULL, NULL, NULL, NULL, NULL),
        (6, N'EPIC-16', N'EPIC', N'Portfolio Epic', N'Enterprise data lake',
            N'Ingest core banking systems into the governed data lake and open it for self-service analytics.',
            N'data-lake', N'In Progress', DATEADD(day, -3, @NowUtc), @Pi3Start, NULL, N'Merge candidate with EPIC-19?', N'Priya Raman', NULL),
        (7, N'EPIC-17', N'EPIC', N'Portfolio Epic', N'Platform observability',
            N'Common logging, metrics and alerting for payment services.',
            NULL, N'Funnel', DATEADD(day, -12, @NowUtc), NULL, NULL, NULL, NULL, 3),
        (8, N'EPIC-18', N'EPIC', N'Portfolio Epic', N'Legacy risk system migration',
            N'Move risk data and reports off the legacy warehouse.',
            N'legacy,migration', N'To Do', DATEADD(day, -7, @NowUtc), @Pi3Start, @Pi4End, NULL, N'Owen Hale', NULL),
        (9, N'EPIC-19', N'EPIC', N'Portfolio Epic', N'Shared reference data',
            N'One source of truth for currencies, calendars and counterparties.',
            N'reference-data', N'Funnel', DATEADD(day, -20, @NowUtc), NULL, NULL, N'No strategic objective yet', NULL, 2),
        (10, N'PAY-9110', N'PAY', N'Portfolio Epic', N'Payment hub decommissioning',
            N'Switch off the old payment hub after all flows moved to the new platform.',
            N'decommission', N'Closed', DATEADD(day, -30, @NowUtc), @Pi1Start, @Pi2End, NULL, NULL, NULL);
    SET IDENTITY_INSERT dbo.PortfolioEpics OFF;

    INSERT INTO dbo.StrategicObjectivePortfolioEpics (StrategicObjectiveId, PortfolioEpicId) VALUES
        (1, 1), (1, 2), (1, 10),
        (2, 2), (2, 3),
        (3, 4), (3, 5), (3, 8),
        (4, 6),
        (5, 7);

    -- business outcomes
    SET IDENTITY_INSERT dbo.BusinessOutcomes ON;
    INSERT INTO dbo.BusinessOutcomes (Id, JiraId, ProjectKey, IssueType, Summary, Description, Labels, Components, Status, JiraUpdated, TargetStart, TargetEnd, StoryPoints, RagStatus, RagExplain, PortfolioEpicId) VALUES
        (1, N'PAY-9201', N'PAY', N'Business Outcome', N'Real-time payment rails',
            CONCAT(N'h3. Outcome', @a_nl, N'Customers send and receive payments in seconds, any time.', @a_nl, @a_nl,
                N'h3. Measures', @a_nl, N'* 99.9% of instant payments settled within *10 seconds*', @a_nl, N'* No scheme penalties'),
            N'payments,instant', NULL, N'In Progress', DATEADD(hour, -6, @NowUtc), @Pi2Start, @Pi3End, 120, N'Green', NULL, 1),
        (2, N'PAY-9202', N'PAY', N'Business Outcome', N'Payment fraud screening',
            N'Screen every instant payment for fraud before release.',
            N'payments,fraud', NULL, N'In Progress', DATEADD(hour, -9, @NowUtc), @Pi3Start, @Pi3End, 60, N'Amber', N'Screening vendor API delivered two sprints late; recovery plan agreed', 1),
        (3, N'PAY-9203', N'PAY', N'Business Outcome', N'Card tokenisation service',
            CONCAT(N'Token vault and tokenisation API used by card processing and the mobile wallet.', @a_nl, @a_nl,
                N'{panel:title=Dependencies}', @a_nl, N'Mobile Banking wallet SDK, card scheme certification', @a_nl, N'{panel}'),
            N'cards,security', NULL, N'In Progress', DATEADD(hour, -12, @NowUtc), @Pi2Start, @Pi4End, 150, N'Red', N'Card scheme certification failed; retest booked next PI', 2),
        (4, N'MOB-9204', N'MOB', N'Business Outcome', N'Digital onboarding journey',
            N'Account opening fully in the app, including ID verification.',
            N'mobile,onboarding', N'iOS,Android', N'In Progress', DATEADD(hour, -15, @NowUtc), @Pi3Start, @Pi4End, 80, N'Green', NULL, 3),
        (5, N'MOB-9205', N'MOB', N'Business Outcome', N'Mobile app performance',
            N'App start under two seconds on mid-range phones.',
            NULL, NULL, N'To Do', DATEADD(day, -2, @NowUtc), NULL, NULL, NULL, NULL, NULL, 3),
        (6, N'RISK-9206', N'RISK', N'Business Outcome', N'RWA engine v2',
            CONCAT(N'h3. Outcome', @a_nl, N'Basel IV RWA figures produced by the new engine in parallel with the old one.', @a_nl, @a_nl,
                N'# Credit risk standardised approach', @a_nl, N'# Output floor', @a_nl, N'# Reconciliation report'),
            N'basel,rwa', NULL, N'In Progress', DATEADD(hour, -30, @NowUtc), @Pi2Start, @Pi4End, 200, N'Amber', N'Parallel run differences above tolerance for two portfolios', 4),
        (7, N'RISK-9207', N'RISK', N'Business Outcome', N'IRB model recalibration',
            N'Recalibrate PD and LGD models with the latest default data.',
            N'basel,models', NULL, N'Analysis', DATEADD(hour, -40, @NowUtc), NULL, NULL, NULL, N'Blue', NULL, 5),
        (8, N'RISK-9208', N'RISK', N'Business Outcome', N'Legacy risk data migration',
            N'Historic risk data available in the new platform with full lineage.',
            N'Legacy,migration', NULL, N'In Progress', DATEADD(day, -3, @NowUtc), @Pi3Start, @Pi4End, 90, N'Green', NULL, 8),
        (9, N'DATA-9209', N'DATA', N'Business Outcome', N'Data lake ingestion',
            N'Core banking, cards and payments data land daily in the data lake.',
            N'data-lake,ingestion', NULL, N'Done', DATEADD(day, -8, @NowUtc), @Pi2Start, @Pi3End, 70, N'Green', NULL, 6),
        (10, N'DATA-9210', N'DATA', N'Business Outcome', N'Self-service analytics',
            N'Business users build their own reports on curated data sets.',
            N'data-lake,analytics', NULL, N'Funnel', DATEADD(day, -10, @NowUtc), NULL, NULL, NULL, N'Red', N'No funding for the BI licences; scope on hold', 6),
        (11, N'PAY-9211', N'PAY', N'Business Outcome', N'Payments observability',
            N'End-to-end tracing of every payment across services.',
            NULL, NULL, N'Funnel', DATEADD(day, -11, @NowUtc), NULL, NULL, NULL, NULL, NULL, 7),
        (12, N'MOB-9212', N'MOB', N'Business Outcome', N'Mobile accessibility uplift',
            N'Mobile apps meet WCAG 2.1 AA.',
            N'mobile,accessibility', NULL, N'To Do', DATEADD(day, -15, @NowUtc), @Pi4Start, @Pi4End, NULL, N'Amber', N'Audit found 40 issues; fixes not yet planned', NULL),
        (13, N'RISK-9213', N'RISK', N'Business Outcome', N'Stress testing framework',
            N'Run regulatory stress scenarios on demand.',
            N'basel', NULL, N'To Do', DATEADD(day, -16, @NowUtc), @Pi4Start, @Pi4End, NULL, N'Green', NULL, 5),
        (14, N'DATA-9214', N'DATA', N'Business Outcome', N'Cross-ART data quality',
            N'Data quality checks shared by all trains.',
            N'data-quality', NULL, N'Analysis', DATEADD(day, -18, @NowUtc), NULL, NULL, NULL, N'Amber', N'Ownership unclear: work items sit on keys no single ART owns', 9);
    SET IDENTITY_INSERT dbo.BusinessOutcomes OFF;

    INSERT INTO dbo.CapitalProjectStrategicObjectives (CapitalProjectId, StrategicObjectiveId) VALUES
        (1, 1), (1, 2), (2, 2), (3, 3), (4, 3), (5, 4);


    -- 20-resources: locations, lookups, skills, people, stacks, teams, team sprints, holidays, public holidays
    SET @Section = N'20-resources';
    PRINT N'Resources: locations, lookups, skills, people, technology stacks, teams, team sprints, holidays, public holidays';

    DECLARE @b_MonthStart date = DATEFROMPARTS(YEAR(@Today), MONTH(@Today), 1);
    DECLARE @b_MonthMon date = DATEADD(day, (7 - DATEDIFF(day, '19000101', @b_MonthStart) % 7) % 7, @b_MonthStart);

    -- locations
    SET IDENTITY_INSERT dbo.Countries ON;
    INSERT INTO dbo.Countries (Id, [Name], Description) VALUES
        (1, N'United Kingdom', N'UK'),
        (2, N'Czech Republic', N'CZ'),
        (3, N'India', N'IN'),
        (4, N'United States', N'US'),
        (5, N'Poland', N'PL');
    SET IDENTITY_INSERT dbo.Countries OFF;

    SET IDENTITY_INSERT dbo.Cities ON;
    INSERT INTO dbo.Cities (Id, [Name], Description, CountryId) VALUES
        (1, N'London', N'Head office', 1),
        (2, N'Manchester', NULL, 1),
        (3, N'Prague', N'Technology hub', 2),
        (4, N'Brno', NULL, 2),
        (5, N'Pune', N'Delivery centre', 3),
        (6, N'Chennai', N'Delivery centre', 3),
        (7, N'New York', NULL, 4),
        (8, N'Austin', N'Planned site', 4);
    SET IDENTITY_INSERT dbo.Cities OFF;

    -- lookups
    SET IDENTITY_INSERT dbo.CorporateGrades ON;
    INSERT INTO dbo.CorporateGrades (Id, [Name], Description) VALUES
        (1, N'L1', N'Graduate'),
        (2, N'L2', N'Associate'),
        (3, N'L3', N'Senior Associate'),
        (4, N'L4', N'Lead'),
        (5, N'L5', N'Principal'),
        (6, N'AVP', N'Assistant Vice President'),
        (7, N'VP', N'Vice President');
    SET IDENTITY_INSERT dbo.CorporateGrades OFF;

    SET IDENTITY_INSERT dbo.EmployeeCategories ON;
    INSERT INTO dbo.EmployeeCategories (Id, [Name], Description) VALUES
        (1, N'Internal', N'Permanent employee'),
        (2, N'Contractor', N'Agency contractor'),
        (3, N'Consultant', NULL);
    SET IDENTITY_INSERT dbo.EmployeeCategories OFF;

    SET IDENTITY_INSERT dbo.EmployeeTypes ON;
    INSERT INTO dbo.EmployeeTypes (Id, [Name], Description) VALUES
        (1, N'Full-Time', NULL),
        (2, N'Part-Time', N'Reduced weekly hours'),
        (3, N'Temporary', N'Fixed-term engagement');
    SET IDENTITY_INSERT dbo.EmployeeTypes OFF;

    SET IDENTITY_INSERT dbo.EmployeeVendors ON;
    INSERT INTO dbo.EmployeeVendors (Id, [Name], Description) VALUES
        (1, N'Northwind Staffing', N'Contractor agency'),
        (2, N'Bluefield Consulting', N'Consulting partner'),
        (3, N'Orion Talent', NULL);
    SET IDENTITY_INSERT dbo.EmployeeVendors OFF;

    SET IDENTITY_INSERT dbo.EmployeeRoles ON;
    INSERT INTO dbo.EmployeeRoles (Id, [Name], Description) VALUES
        (1, N'Developer', NULL),
        (2, N'Senior Developer', NULL),
        (3, N'Tech Lead', N'Technical lead of a delivery team'),
        (4, N'Architect', NULL),
        (5, N'QA Engineer', NULL),
        (6, N'DevOps Engineer', NULL),
        (7, N'Business Analyst', NULL),
        (8, N'Delivery Manager', N'Scrum master or delivery lead');
    SET IDENTITY_INSERT dbo.EmployeeRoles OFF;

    SET IDENTITY_INSERT dbo.TeamRoles ON;
    INSERT INTO dbo.TeamRoles (Id, [Name], Description, CanEditTeam) VALUES
        (1, N'Scrum Master', N'Facilitates the team; may edit team planning', 1),
        (2, N'Product Owner', N'Owns the team backlog', NULL),
        (3, N'Tech Lead', N'Technical lead; may edit team planning', 1),
        (4, N'Developer', NULL, 0),
        (5, N'QA Engineer', NULL, NULL),
        (6, N'Business Analyst', N'Not used by any member yet', NULL);
    SET IDENTITY_INSERT dbo.TeamRoles OFF;

    -- skills and levels
    SET IDENTITY_INSERT dbo.Skills ON;
    INSERT INTO dbo.Skills (Id, [Name], Description, Created, Updated)
    SELECT v.Id, v.SkillName, v.Descr, DATEADD(day, -420 + 7 * v.Id, @NowUtc), CASE WHEN v.UpdDays IS NOT NULL THEN DATEADD(day, -v.UpdDays, @NowUtc) END
    FROM (VALUES
        (1, N'C#', N'C# and .NET development', NULL),
        (2, N'SQL', N'T-SQL and relational database design', 40),
        (3, N'Angular', N'Angular front-end development', NULL),
        (4, N'React', N'React front-end development', NULL),
        (5, N'Java', N'Java and JVM services', NULL),
        (6, N'Python', N'Python for data and analytics', 12),
        (7, N'Testing', N'Manual and exploratory testing', NULL),
        (8, N'Test Automation', N'Automated test suites and frameworks', NULL),
        (9, N'DevOps', N'CI/CD pipelines and infrastructure as code', NULL),
        (10, N'Business Analysis', N'Requirements, user stories and process modelling', NULL),
        (11, N'Risk Modelling', N'Credit and market risk models', 75),
        (12, N'Data Engineering', N'ETL pipelines and data modelling', NULL),
        (13, N'iOS Development', N'Swift and native iOS apps', NULL),
        (14, N'Android Development', N'Kotlin and native Android apps', NULL),
        (15, N'AutoSys', N'AutoSys batch job scheduling', 5),
        (16, N'COBOL', N'Mainframe COBOL programs', NULL)
    ) AS v (Id, SkillName, Descr, UpdDays);
    SET IDENTITY_INSERT dbo.Skills OFF;

    SET IDENTITY_INSERT dbo.SkillLevels ON;
    INSERT INTO dbo.SkillLevels (Id, [Name], [Value], Description, SkillId)
    SELECT s.Id * 10 + n.n,
        CASE n.n WHEN 1 THEN N'Beginner' WHEN 2 THEN N'Intermediate' WHEN 3 THEN N'Advanced' ELSE N'Expert' END,
        n.n,
        CASE n.n
            WHEN 1 THEN N'Basic knowledge, needs guidance'
            WHEN 2 THEN N'Can work independently on standard tasks'
            WHEN 3 THEN N'Deep expertise, can mentor others'
            ELSE N'Subject matter expert, leads design decisions'
        END,
        s.Id
    FROM dbo.Skills AS s
    JOIN #N AS n ON n.n BETWEEN 1 AND 4
    WHERE s.Id <> 15 AND NOT (s.Id = 11 AND n.n = 4);
    INSERT INTO dbo.SkillLevels (Id, [Name], [Value], Description, SkillId)
    VALUES (69, N'Trainee', NULL, N'In training, not yet productive', 6);
    SET IDENTITY_INSERT dbo.SkillLevels OFF;

    -- people
    SET IDENTITY_INSERT dbo.HumanResources ON;
    INSERT INTO dbo.HumanResources (Id, IsActive, Cio, Cio1, Cio2, EmployeeNumber, EmployeeName, FullName, LineManagerName,
        EmployeeCategoryId, EmployeeTypeId, EmployeeRoleId, EmployeeVendorId, CityId, CorporateGradeId, AnnualLeaveDays)
    SELECT v.Id, v.Act,
        CASE WHEN v.Org IS NOT NULL THEN N'Helen Strand' END,
        CASE v.Org WHEN N'P' THEN N'Mark Ellison' WHEN N'M' THEN N'Mark Ellison' WHEN N'K' THEN N'Ruth Calder' WHEN N'T' THEN N'Peter Lowe' END,
        CASE v.Org WHEN N'P' THEN N'Anna Berg' WHEN N'M' THEN N'Daniel Moss' WHEN N'K' THEN N'Simon Tate' WHEN N'T' THEN N'Lena Fox' END,
        CASE WHEN v.Id IN (61, 62) THEN NULL ELSE N'G' + RIGHT(N'00000000' + CAST(1000000 + v.Id AS nvarchar(10)), 8) END,
        v.EmpName, v.FullName, v.LineManager, v.Cat, v.Typ, v.Rol, v.Ven, v.CityId, v.Grade, v.Leave
    FROM (VALUES
        (1, N'Alice Morgan', N'A. Morgan', 1, 1, N'P', N'Gordon Pike', 1, 1, 8, NULL, 6, 28),
        (2, N'Ben Carter', N'B. Carter', 1, 1, N'P', N'Gordon Pike', 1, 1, 7, NULL, 6, 25),
        (3, N'Chloe Novak', N'C. Novak', 1, 3, N'P', N'Alice Morgan', 1, 1, 3, NULL, 5, 25),
        (4, N'Daniel Price', N'D. Price', 1, 2, N'P', N'Gordon Pike', 1, 1, 8, NULL, 5, 28),
        (5, N'Eva Horak', N'E. Horak', 1, 3, N'K', N'Helena Varga', 1, 1, 8, NULL, 5, 25),
        (6, N'Farid Khan', N'F. Khan', 1, 5, N'M', N'Monica Hart', 1, 1, 7, NULL, 6, 20),
        (7, N'Grace Liu', N'G. Liu', 1, 7, N'T', N'Lena Fox', 1, 1, 3, NULL, 5, 20),
        (8, N'Hugo Marek', N'H. Marek', 1, 4, N'K', N'Eva Horak', 2, 1, 2, 1, 3, 20),
        (9, N'Ian Fletcher', N'I. Fletcher', 1, 1, N'P', N'Chloe Novak', 1, 1, 1, NULL, 1, 25),
        (10, N'Julia Brooks', N'J. Brooks', 1, 1, N'P', N'Chloe Novak', 1, 1, 6, NULL, 3, 25),
        (11, N'Kamal Mehta', N'K. Mehta', 1, 5, N'P', N'Alice Morgan', 2, 1, 5, 3, 2, 20),
        (12, N'Laura Svoboda', N'L. Svoboda', 1, 3, N'P', N'Alice Morgan', 1, 2, 5, NULL, 2, 20),
        (13, N'Martin Doyle', N'M. Doyle', 1, 2, N'P', N'Daniel Price', 1, 1, 7, NULL, 5, 25),
        (14, N'Nina Petrova', N'N. Petrova', 1, 3, N'P', N'Daniel Price', 1, 1, 3, NULL, 4, 25),
        (15, N'Oscar Lindqvist', N'O. Lindqvist', 1, 2, N'P', N'Nina Petrova', 3, 1, 4, 2, NULL, NULL),
        (16, N'Priya Nair', N'P. Nair', 1, 5, N'P', N'Nina Petrova', 2, 1, 5, 1, 2, 25),
        (17, N'Quentin Hayes', N'Q. Hayes', 1, 7, N'T', N'Elena Marin', 2, 3, 1, 3, 1, 20),
        (18, N'Rosa Dvorak', N'R. Dvorak', 1, 3, N'P', N'Gordon Pike', 1, 1, 8, NULL, 4, 25),
        (19, N'Samuel Okafor', N'S. Okafor', 1, 1, N'P', N'Gordon Pike', 1, 1, 7, NULL, 5, 28),
        (20, N'Tereza Kral', N'T. Kral', 1, 3, N'P', N'Rosa Dvorak', 1, 1, 3, NULL, 4, 25),
        (21, N'Umar Siddiqui', N'U. Siddiqui', 1, 6, N'P', N'Tereza Kral', 2, 1, 5, 1, 2, 20),
        (22, N'Vera Kowalczyk', N'V. Kowalczyk', 1, 1, N'M', N'Monica Hart', 1, 1, 8, NULL, 4, 25),
        (23, N'William Shaw', N'W. Shaw', 1, 2, N'M', N'Monica Hart', 1, 1, 8, NULL, 4, 28),
        (24, N'Xavier Dumont', N'X. Dumont', 1, 1, N'M', N'Farid Khan', 3, 1, 4, 2, 6, 20),
        (25, N'Yara Haddad', N'Y. Haddad', 1, 6, N'M', N'Xavier Dumont', 1, 1, 2, NULL, 3, 25),
        (26, N'Zoe Whitfield', N'Z. Whitfield', 1, 2, N'M', N'Xavier Dumont', 1, 2, 5, NULL, 3, 20),
        (27, N'Adam Reyes', N'A. Reyes', 1, 7, N'M', N'Monica Hart', 1, 1, 8, NULL, 4, 20),
        (28, N'Beatrice Lang', N'B. Lang', 1, 1, N'M', N'Monica Hart', 1, 1, 7, NULL, 5, 28),
        (29, N'Carlos Mendes', N'C. Mendes', 1, 7, N'M', N'Adam Reyes', 2, 3, 1, 3, 2, 20),
        (30, N'Diana Frost', N'D. Frost', 0, 7, N'M', N'Adam Reyes', 1, 1, 1, NULL, 2, 20),
        (31, N'Ethan Cole', N'E. Cole', 1, 4, N'K', N'Helena Varga', 1, 1, 7, NULL, 5, 25),
        (32, N'Fiona Marsh', N'F. Marsh', 1, 1, N'K', N'Eva Horak', 1, 1, 3, NULL, 5, 28),
        (33, N'George Benes', N'G. Benes', 1, 3, N'K', N'Fiona Marsh', 1, 1, 2, NULL, 3, 25),
        (34, N'Hana Prochazka', N'H. Prochazka', 1, 4, N'K', N'Fiona Marsh', 1, NULL, 5, NULL, 2, 25),
        (35, N'Isaac Bennett', N'I. Bennett', 1, 6, N'K', N'Helena Varga', 1, 1, 8, NULL, 4, 20),
        (36, N'Jana Vesely', N'J. Vesely', 1, 3, N'K', N'Isaac Bennett', 1, 1, 3, NULL, 5, 25),
        (37, N'Karthik Iyer', N'K. Iyer', 1, 6, N'K', N'Jana Vesely', 2, 1, 2, 1, 3, 25),
        (38, N'Leila Rahman', N'L. Rahman', 1, 5, N'K', N'Jana Vesely', 1, 1, 5, NULL, 2, 20),
        (39, N'Mateo Alvarez', N'M. Alvarez', 1, NULL, N'K', NULL, 3, 3, NULL, 2, NULL, NULL),
        (40, N'Nora Kubik', N'N. Kubik', 1, 4, N'K', N'Helena Varga', 1, 1, 8, NULL, 4, 25),
        (41, N'Owen Pritchard', N'O. Pritchard', 1, 2, N'K', N'Helena Varga', 1, 1, 7, NULL, 5, 28),
        (42, N'Petra Sykora', N'P. Sykora', 1, 3, N'K', N'Nora Kubik', 1, 1, 3, NULL, 4, 25),
        (43, N'Radek Moravec', N'R. Moravec', 1, 4, N'K', N'Petra Sykora', 2, 1, 1, 3, 2, 20),
        (44, N'Sophie Grant', N'S. Grant', 1, 7, N'T', N'Lena Fox', 1, 1, 8, NULL, 4, 20),
        (45, N'Tomas Urban', N'T. Urban', 1, 3, N'T', N'Lena Fox', 1, 1, 7, NULL, 5, 25),
        (46, N'Ursula Kent', N'U. Kent', 0, 7, N'T', N'Grace Liu', 2, 1, 1, 1, 2, 20),
        (47, N'Victor Huang', N'V. Huang', 1, 6, N'T', N'Grace Liu', 2, 1, 5, 3, 2, 20),
        (48, N'Wendy Harper', N'W. Harper', 1, 7, N'T', N'Lena Fox', 1, 1, 8, NULL, 4, 20),
        (49, N'Yusuf Demir', N'Y. Demir', 1, 4, N'K', N'Helena Varga', 1, 1, 7, NULL, 5, 25),
        (50, N'Zdenek Pospisil', N'Z. Pospisil', 1, 3, N'T', N'Wendy Harper', 1, 1, 2, NULL, 3, 25),
        (51, N'Amelia Wood', N'A. Wood', 1, 2, N'T', N'Ravi Kapoor', 1, 1, 8, NULL, 4, 28),
        (52, N'Bruno Rossi', N'B. Rossi', 1, 5, N'T', N'Ravi Kapoor', 1, 1, 7, NULL, 5, 20),
        (53, N'Clara Jensen', N'C. Jensen', 1, NULL, N'T', N'Amelia Wood', 3, 1, 6, 2, 4, NULL),
        (54, N'Deepak Rao', N'D. Rao', 1, 5, N'T', N'Amelia Wood', 1, 1, 5, NULL, 2, 20),
        (55, N'Elena Marin', N'E. Marin', 1, 1, N'T', N'Ravi Kapoor', 1, 1, 8, NULL, 5, 25),
        (56, N'Felix Novotny', N'F. Novotny', 1, 7, N'T', N'Ravi Kapoor', 1, 1, 7, NULL, 4, 25),
        (57, N'Gemma Porter', N'G. Porter', 1, 2, NULL, NULL, NULL, 1, 4, NULL, 6, 28),
        (58, N'Harvey Quinn', N'H. Quinn', 0, 1, N'P', N'Gordon Pike', 1, 1, 2, NULL, 3, 25),
        (59, N'Irene Walsh', N'I. Walsh', 0, 6, NULL, NULL, 2, 3, 1, 1, 1, NULL),
        (60, N'Jakub Zeman', N'J. Zeman', 1, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL),
        (61, N'Kavya Menon', N'K. Menon', 1, 5, N'P', N'Rosa Dvorak', 2, 1, 1, 1, 1, 20),
        (62, N'Liam Foster', N'L. Foster', 0, 7, NULL, NULL, 3, 3, 7, 2, NULL, NULL)
    ) AS v (Id, FullName, EmpName, Act, CityId, Org, LineManager, Cat, Typ, Rol, Ven, Grade, Leave);
    SET IDENTITY_INSERT dbo.HumanResources OFF;

    INSERT INTO dbo.HumanResourceSkills (HumanResourceId, SkillId, SkillLevelId)
    SELECT v.HrId, v.SkillId, CASE WHEN v.Lvl IS NOT NULL THEN v.SkillId * 10 + v.Lvl END
    FROM (VALUES
        (1, 10, 3), (1, 7, 2),
        (2, 10, 4), (2, 2, NULL),
        (3, 1, 4), (3, 2, 4), (3, 12, 3), (3, 3, 2),
        (4, 10, 2), (4, 5, NULL),
        (5, 11, 2), (5, 10, 3),
        (6, 10, 4), (6, 13, 1),
        (7, 12, 4), (7, 6, 4), (7, 2, 3),
        (8, 6, 4), (8, 12, 3), (8, 2, 3),
        (9, 1, 1), (9, 2, 1),
        (10, 9, 4), (10, 2, 3), (10, 6, 2), (10, 1, NULL),
        (11, 2, 3), (11, 9, 2),
        (12, 7, 3), (12, 8, 2), (12, 1, 2),
        (13, 10, 3), (13, 2, 1),
        (14, 5, 4), (14, 1, 3), (14, 2, 3), (14, 15, NULL),
        (15, 1, 3), (15, 2, 2), (15, 4, 2),
        (16, 1, 4), (16, 7, 2), (16, 8, NULL),
        (17, 9, 3), (17, 4, 4), (17, 1, 2),
        (18, 7, NULL), (18, 10, 3),
        (19, 10, 4), (19, 3, 1),
        (20, 5, 4), (20, 3, 3), (20, 2, 2),
        (21, 7, 4), (21, 8, 3), (21, 3, 2),
        (22, 10, 2), (22, 7, NULL),
        (23, 10, 3), (23, 14, 2),
        (24, 1, 4), (24, 5, 3), (24, 13, 3), (24, 14, 2),
        (25, 13, 4), (25, 14, 3), (25, 4, 2),
        (26, 7, 3), (26, 8, 3), (26, 14, 2),
        (27, 7, NULL), (27, 10, 2),
        (28, 10, 3), (28, 13, NULL),
        (29, 3, 4), (29, 4, 3), (29, 14, 2),
        (30, 13, 2),
        (31, 11, 3), (31, 10, 4),
        (32, 6, 4), (32, 11, 3), (32, 2, 4),
        (33, 6, 2), (33, 2, 3), (33, 15, NULL),
        (34, 7, 3), (34, 6, 1), (34, 8, 2),
        (35, 11, 1), (35, 7, NULL),
        (36, 6, 3), (36, 11, 3), (36, 9, 2),
        (37, 6, 3), (37, 2, 2), (37, 15, NULL),
        (38, 7, 2), (38, 8, 4),
        (39, 6, 9), (39, 11, 1),
        (40, 10, 2), (40, 11, NULL),
        (41, 10, 4), (41, 11, 2),
        (42, 5, 4), (42, 11, 3), (42, 2, 3),
        (43, 5, 3), (43, 2, 2), (43, 15, NULL),
        (44, 10, 2), (44, 12, NULL),
        (45, 10, 3), (45, 2, 2),
        (46, 6, 2), (46, 12, NULL),
        (47, 6, 3), (47, 7, 2), (47, 8, 1),
        (48, 10, 3), (48, 9, 1),
        (49, 11, 2), (49, 10, 3), (49, 6, 1),
        (50, 9, 3), (50, 6, 3), (50, 12, 2),
        (51, 9, 2), (51, 7, NULL),
        (53, 9, 4), (53, 15, NULL), (53, 2, 2),
        (54, 7, 3), (54, 8, 3), (54, 9, 1),
        (55, 9, 2), (55, 2, NULL),
        (56, 10, 2), (56, 2, 3),
        (57, 1, 4), (57, 5, 4), (57, 2, 3),
        (58, 1, 3),
        (59, 7, 2),
        (60, 4, 2), (60, 3, NULL),
        (61, 5, 2), (61, 2, 2), (61, 3, NULL),
        (62, 10, 3)
    ) AS v (HrId, SkillId, Lvl);

    -- technology stacks
    SET IDENTITY_INSERT dbo.TechnologyStacks ON;
    INSERT INTO dbo.TechnologyStacks (Id, [Name], Description, CapitalProjectId) VALUES
        (1, N'SQL Server Database', N'Shared SQL Server estate and database development', NULL),
        (2, N'Test and Release', N'Test environments, regression and release management', NULL),
        (3, N'GUI / Angular', N'Shared Angular component library and front ends', NULL),
        (4, N'CI/CD Pipeline', N'Build and deployment pipelines', NULL),
        (5, N'Payments Engine', N'Payment hub processing engine', 1),
        (6, N'Card Gateway', N'Card authorisation and tokenisation gateway', 1),
        (7, N'Mobile Apps', N'Native iOS and Android apps', 2),
        (8, N'Mobile Backend', N'APIs serving the mobile apps', 2),
        (9, N'Risk Engine', N'RWA and credit risk calculation engine', 3),
        (10, N'Risk Engine', N'Legacy risk engine being migrated', 4),
        (11, N'Data Lake', N'Enterprise data lake ingestion and storage', 5),
        (12, N'Regulatory Reporting', N'Regulatory returns, not staffed yet', 3),
        (13, N'Legacy Mainframe', N'Mainframe batch with no active team', NULL);
    SET IDENTITY_INSERT dbo.TechnologyStacks OFF;

    INSERT INTO dbo.TechnologyStackSkills (TechnologyStackId, SkillId, Percentage) VALUES
        (1, 2, 0.70), (1, 12, 0.30),
        (2, 7, 0.50), (2, 8, 0.30), (2, 9, 0.20),
        (3, 3, 0.80), (3, 7, 0.20),
        (4, 9, 1.00),
        (5, 1, 0.60), (5, 2, 0.40),
        (6, 5, 0.70), (6, 2, 0.30),
        (7, 13, 0.50), (7, 14, 0.50),
        (8, 1, 1.00), (8, 5, 1.00),
        (9, 6, 0.50), (9, 11, 0.40), (9, 2, 0.10),
        (10, 5, 0.60), (10, 11, 0.40), (10, 10, 0.00),
        (11, 6, 0.40), (11, 12, 0.60),
        (13, 16, 1.00);

    -- teams
    SET IDENTITY_INSERT dbo.Teams ON;
    INSERT INTO dbo.Teams (Id, [Name], FullName, OptionalTeamTag, Description, IsArtSharedTeam, JiraName) VALUES
        (1, N'Argon', N'Team Argon', N'Payments', N'Instant payments and payment hub services', 0, N'ARG'),
        (2, N'Boron', N'Team Boron', NULL, N'Card processing and tokenisation', 0, NULL),
        (3, N'Carbon', N'Team Carbon', NULL, N'Card gateway and merchant portal UI', 0, N'CRB'),
        (4, N'Delta', N'Team Delta', N'Mobile', N'Mobile apps and mobile backend', 0, NULL),
        (5, N'Echo', N'Team Echo', NULL, N'Mobile web and onboarding UI', 0, N'ECHO'),
        (6, N'Fermi', N'Team Fermi', N'Basel', N'RWA engine and capital calculation', 0, NULL),
        (7, N'Gauss', N'Team Gauss', NULL, N'Credit risk models and model testing', 0, N'GSS'),
        (8, N'Hubble', N'Team Hubble', NULL, N'Legacy risk data migration', 1, NULL),
        (9, N'Iris', N'Team Iris', NULL, N'Data lake ingestion pipelines', 0, NULL),
        (10, N'Juno', N'Team Juno', N'Shared', N'Risk data feeds for Risk Analytics and Data Platform', 1, NULL),
        (11, N'Kepler', N'Team Kepler', NULL, N'Shared CI/CD and test environments', 0, NULL),
        (12, N'Lambda', N'Team Lambda', N'Platform', N'Platform engineering outside any ART', 0, NULL),
        (13, N'Mercury', NULL, NULL, N'New team, members not assigned yet', 0, NULL);
    SET IDENTITY_INSERT dbo.Teams OFF;

    INSERT INTO dbo.CapitalProjectTeams (CapitalProjectId, TeamId) VALUES
        (1, 1), (1, 2), (1, 3),
        (2, 4), (2, 5),
        (3, 6), (3, 7), (3, 10),
        (4, 8),
        (5, 9), (5, 10),
        (6, 11), (6, 13);

    INSERT INTO dbo.TeamMembers (TeamId, HumanResourceId, TeamRoleId) VALUES
        (1, 1, 1), (1, 2, 2), (1, 3, 3), (1, 9, 4), (1, 10, 4), (1, 11, 5), (1, 12, NULL),
        (2, 4, 1), (2, 13, 2), (2, 14, 3), (2, 15, 4), (2, 16, 5),
        (3, 18, 1), (3, 19, 2), (3, 20, 3), (3, 21, 5), (3, 61, 4),
        (4, 6, 2), (4, 22, 1), (4, 23, 1), (4, 24, 3), (4, 25, 4), (4, 26, 5),
        (5, 27, 1), (5, 28, 2), (5, 29, 4), (5, 30, 4), (5, 26, 5),
        (6, 5, 1), (6, 8, 4), (6, 31, 2), (6, 32, 3), (6, 33, 4), (6, 34, 5),
        (7, 35, 1), (7, 36, 3), (7, 37, 4), (7, 38, 5), (7, 39, 4),
        (8, 40, 1), (8, 41, 2), (8, 42, 3), (8, 43, 4),
        (9, 7, 3), (9, 44, 1), (9, 45, 2), (9, 46, 4), (9, 47, 5),
        (10, 8, 4), (10, 48, 1), (10, 49, 2), (10, 50, 4),
        (11, 51, 1), (11, 52, 2), (11, 53, 4), (11, 54, 5),
        (12, 55, 1), (12, 56, 2), (12, 17, 4), (12, 10, 4);

    INSERT INTO dbo.TeamTechnologyStacks (TeamId, TechnologyStackId) VALUES
        (1, 5), (1, 1), (1, 2),
        (2, 5), (2, 6),
        (3, 6), (3, 3), (3, 2),
        (4, 7), (4, 8),
        (5, 7), (5, 3),
        (6, 9), (6, 1),
        (7, 9), (7, 2),
        (8, 10), (8, 1),
        (9, 11), (9, 1), (9, 4),
        (10, 11), (10, 9), (10, 4),
        (11, 4), (11, 2),
        (12, 4), (12, 1);

    INSERT INTO dbo.TeamMemberTechnologyStacks (TeamId, HumanResourceId, TechnologyStackId) VALUES
        (1, 2, 5), (1, 3, 5), (1, 3, 1), (1, 9, 5), (1, 10, 1), (1, 11, 2), (1, 12, 5), (1, 12, 2),
        (2, 13, 6), (2, 14, 5), (2, 14, 6), (2, 15, 5), (2, 16, 5),
        (3, 19, 3), (3, 20, 6), (3, 20, 3), (3, 21, 2), (3, 61, 6),
        (4, 6, 7), (4, 23, 7), (4, 24, 7), (4, 24, 8), (4, 25, 7), (4, 26, 7),
        (5, 28, 7), (5, 29, 3), (5, 29, 7), (5, 30, 7), (5, 26, 3),
        (6, 8, 9), (6, 8, 1), (6, 31, 9), (6, 32, 9), (6, 33, 1), (6, 34, 9),
        (7, 36, 9), (7, 36, 2), (7, 37, 9), (7, 38, 2), (7, 39, 9),
        (8, 41, 10), (8, 42, 10), (8, 42, 1), (8, 43, 1),
        (9, 7, 11), (9, 7, 1), (9, 45, 1), (9, 46, 11), (9, 47, 11), (9, 47, 4),
        (10, 8, 11), (10, 49, 9), (10, 50, 4), (10, 50, 11),
        (11, 52, 4), (11, 53, 4), (11, 53, 2), (11, 54, 2),
        (12, 56, 1), (12, 17, 4), (12, 10, 4), (12, 10, 1);

    -- coefficients
    SET IDENTITY_INSERT dbo.TeamMemberCoefficients ON;
    INSERT INTO dbo.TeamMemberCoefficients (Id, TeamId, HumanResourceId, PiId, [Value], Comment) VALUES
        (1, 1, 1, NULL, 0.70, NULL),
        (2, 1, 2, NULL, 0.50, NULL),
        (3, 1, 3, NULL, 1.00, NULL),
        (4, 1, 9, NULL, 1.00, NULL),
        (5, 1, 10, NULL, 0.50, N'Also in Lambda'),
        (6, 1, 11, NULL, 1.00, NULL),
        (7, 1, 12, NULL, 0.50, N'Part-time'),
        (8, 2, 4, NULL, 0.70, NULL),
        (9, 2, 13, NULL, 0.50, NULL),
        (10, 2, 14, NULL, 1.00, NULL),
        (11, 2, 15, NULL, 0.00, N'Seconded to another programme'),
        (12, 2, 16, NULL, 1.00, NULL),
        (13, 4, 6, NULL, 0.50, NULL),
        (14, 4, 22, NULL, 0.70, NULL),
        (15, 4, 23, NULL, 0.70, NULL),
        (16, 4, 24, NULL, 1.00, NULL),
        (17, 4, 25, NULL, 1.00, NULL),
        (18, 4, 26, NULL, 0.50, N'Also in Echo'),
        (19, 6, 5, NULL, 0.70, NULL),
        (20, 6, 8, NULL, 0.60, N'Split with Juno'),
        (21, 6, 31, NULL, 0.50, NULL),
        (22, 6, 32, NULL, 1.00, NULL),
        (23, 6, 33, NULL, 1.00, NULL),
        (24, 6, 34, NULL, 1.00, NULL),
        (25, 10, 8, NULL, 0.40, N'Split with Fermi'),
        (26, 10, 48, NULL, 0.70, NULL),
        (27, 10, 49, NULL, 0.50, NULL),
        (28, 10, 50, NULL, 1.00, NULL),
        (29, 1, 1, 3, 0.60, N'Also coaching Boron this PI'),
        (30, 1, 3, 3, 0.80, N'Architecture board this PI'),
        (31, 1, 9, 3, 0.90, NULL),
        (32, 1, 12, 3, 0.40, N'Reduced hours this PI');
    SET IDENTITY_INSERT dbo.TeamMemberCoefficients OFF;

    -- team sprints
    SET IDENTITY_INSERT dbo.Sprints ON;
    INSERT INTO dbo.Sprints (Id, TeamId, PiId, SourceArtSprintId, [Name], StartDate, EndDate, UatStart, UatEnd, FixVersion, SignOffDate, ReleaseDate, ColorHex, IsIpSprint, Comment)
    SELECT t.Id * 1000 + s.Id, t.Id, s.PiId, s.Id, ISNULL(t.JiraName, t.[Name]) + N' ' + s.[Name],
        s.StartDate, s.EndDate, s.UatStart, s.UatEnd, s.FixVersion, s.SignOffDate, s.ReleaseDate, NULL, s.IsIpSprint, NULL
    FROM dbo.CapitalProjectSprints AS s
    JOIN dbo.CapitalProjectTeams AS cpt ON cpt.CapitalProjectId = s.CapitalProjectId
    JOIN dbo.Teams AS t ON t.Id = cpt.TeamId
    WHERE t.IsArtSharedTeam = 0;

    INSERT INTO dbo.Sprints (Id, TeamId, PiId, SourceArtSprintId, [Name], StartDate, EndDate, UatStart, UatEnd, FixVersion, SignOffDate, ReleaseDate, ColorHex, IsIpSprint, Comment)
    SELECT m.TeamId * 1000 + 900 + (m.PiId - 3) * 6 + n.n, m.TeamId, m.PiId, NULL,
        t.[Name] + N' Sprint ' + p.Code + N'.' + CASE WHEN n.n = 6 THEN N'IP' ELSE CAST(n.n AS nvarchar(2)) END,
        DATEADD(day, 14 * (n.n - 1), p.StartDate),
        CASE WHEN n.n = 6 THEN p.EndDate ELSE DATEADD(day, 14 * (n.n - 1) + 13, p.StartDate) END,
        NULL, NULL, NULL, NULL, NULL, NULL,
        CASE WHEN n.n = 6 THEN CAST(1 AS bit) WHEN m.TeamId = 12 THEN NULL ELSE CAST(0 AS bit) END,
        NULL
    FROM (VALUES (8, 3, 6), (8, 4, 6), (10, 3, 6), (10, 4, 6), (12, 3, 6), (12, 4, 3)) AS m (TeamId, PiId, LastS)
    JOIN #Pi AS p ON p.PiId = m.PiId
    JOIN dbo.Teams AS t ON t.Id = m.TeamId
    JOIN #N AS n ON n.n BETWEEN 1 AND m.LastS;

    INSERT INTO dbo.Sprints (Id, TeamId, PiId, SourceArtSprintId, [Name], StartDate, EndDate, UatStart, UatEnd, FixVersion, SignOffDate, ReleaseDate, ColorHex, IsIpSprint, Comment)
    VALUES (12910, 12, NULL, NULL, N'Lambda Hardening Sprint', DATEADD(day, 42, @Pi4Start), DATEADD(day, 55, @Pi4Start), NULL, NULL, NULL, NULL, NULL, N'#90A4AE', NULL, N'Not tied to a PI yet');
    SET IDENTITY_INSERT dbo.Sprints OFF;

    UPDATE s SET ColorHex = v.ColorHex, Comment = v.Comment
    FROM dbo.Sprints AS s
    JOIN (VALUES
        (1132, N'#FFB74D', N'Team offsite on Wednesday'),
        (1136, NULL, N'Hackathon and PI planning'),
        (2133, N'#81C784', NULL),
        (4233, NULL, N'App store release freeze in the second week'),
        (6331, N'#64B5F6', NULL),
        (9532, N'#BA68C8', N'Data lake upgrade at the weekend'),
        (8901, N'#4FC3F7', NULL),
        (8902, N'#FFB74D', NULL),
        (8903, N'#4FC3F7', NULL),
        (8904, N'#FFB74D', N'Cut-over rehearsal'),
        (10901, NULL, N'Capacity split between Risk Analytics and Data Platform'),
        (10906, N'#AED581', NULL),
        (12906, N'#90A4AE', N'Platform upgrade window')
    ) AS v (Id, ColorHex, Comment) ON v.Id = s.Id;

    -- holidays
    SET IDENTITY_INSERT dbo.HolidayTypes ON;
    INSERT INTO dbo.HolidayTypes (Id, [Name], Description, ColorHex, IsActive, SortOrder) VALUES
        (1, N'Annual Leave', N'Vacation / paid time off', N'#1976D2', 1, 10),
        (2, N'Sick Leave', N'Illness', N'#D32F2F', 1, 20),
        (3, N'Unpaid Leave', N'Unpaid time off', N'#616161', 1, 30),
        (4, N'Maternity Leave', N'Maternity / parental leave', N'#5D4037', 1, 40),
        (5, N'Training', N'Off-site / course', N'#388E3C', 1, 50),
        (6, N'Other', N'Other absence', N'#7B1FA2', 1, 100),
        (7, N'Conference', N'External conference or summit', N'#0097A7', 1, 60),
        (8, N'Legacy Leave', N'Imported from the old leave tracker', N'#9E9E9E', 0, 200);
    SET IDENTITY_INSERT dbo.HolidayTypes OFF;

    SET IDENTITY_INSERT dbo.Holidays ON;
    INSERT INTO dbo.Holidays (Id, HumanResourceId, HolidayTypeId, StartDate, EndDate, HalfDay, Comment)
    SELECT v.Id, v.HrId, v.TypeId, DATEADD(day, v.FromDay, b.Base), DATEADD(day, v.ToDay, b.Base), v.HalfDay, v.Comment
    FROM (VALUES
        (1, 1, 1, N'P2', 28, 39, 0, N'Summer holiday'),
        (2, 1, 1, N'W', 8, 8, 1, NULL),
        (3, 2, 5, N'P3', 30, 31, 0, N'Product owner certification'),
        (4, 3, 7, N'P3', 37, 38, 0, N'Architecture summit'),
        (5, 3, 1, N'P4', 3, 3, 2, NULL),
        (6, 9, 2, N'P3', 8, 9, 0, NULL),
        (7, 10, 1, N'W', 4, 11, 0, N'Family wedding'),
        (8, 11, 1, N'M', 14, 18, 0, NULL),
        (9, 12, 6, N'P3', 3, 3, 0, N'Moving house'),
        (10, 12, 3, N'P4', 28, 32, 0, NULL),
        (11, 4, 8, N'P2', 30, 32, 0, N'Imported from the old leave tracker'),
        (12, 4, 1, N'P3', 42, 46, 0, NULL),
        (13, 4, 1, N'P3', 15, 15, 1, NULL),
        (14, 13, 1, N'M', 2, 2, 2, NULL),
        (15, 13, 1, N'P4', 35, 43, 0, NULL),
        (16, 14, 1, N'P2', 70, 76, 0, NULL),
        (17, 14, 5, N'P3', 50, 51, 0, N'Cloud certification course'),
        (18, 16, 1, N'P3', 56, 60, 0, N'Festival week with family'),
        (19, 18, 1, N'M', 7, 11, 0, NULL),
        (20, 19, 2, N'W', 1, 1, 0, NULL),
        (21, 20, 1, N'P4', 0, 4, 0, NULL),
        (22, 20, 1, N'P3', 44, 44, 1, NULL),
        (23, 21, 5, N'P4', 15, 17, 0, N'Test automation training'),
        (24, 61, 1, N'P3', 63, 69, 0, NULL),
        (25, 6, 1, N'P3', 70, 81, 0, NULL),
        (26, 6, 1, N'P2', 30, 30, 2, NULL),
        (27, 22, 2, N'M', 3, 4, 0, NULL),
        (28, 23, 1, N'P4', 49, 60, 0, NULL),
        (29, 24, 7, N'P3', 16, 17, 0, N'Mobile developer conference'),
        (30, 25, 4, N'P3', 49, 137, 0, NULL),
        (31, 26, 1, N'W', 7, 11, 0, NULL),
        (32, 26, 1, N'P4', 10, 10, 1, NULL),
        (33, 27, 1, N'P2', 0, 11, 0, NULL),
        (34, 27, 2, N'P3', 36, 36, 0, NULL),
        (35, 28, 1, N'M', 15, 15, 1, NULL),
        (36, 28, 1, N'P4', 28, 39, 0, NULL),
        (37, 29, 5, N'P3', 57, 59, 0, NULL),
        (38, 30, 1, N'P3', 21, 25, 0, NULL),
        (39, 5, 1, N'P3', 35, 39, 0, NULL),
        (40, 5, 1, N'P4', 24, 24, 2, NULL),
        (41, 8, 1, N'W', 0, 2, 0, NULL),
        (42, 8, 1, N'P4', 14, 22, 0, N'Skiing trip'),
        (43, 31, 1, N'M', 0, 4, 0, NULL),
        (44, 32, 2, N'P3', 1, 3, 0, NULL),
        (45, 34, 1, N'W', 2, 2, 2, NULL),
        (46, 35, 1, N'P4', 7, 18, 0, NULL),
        (47, 37, 1, N'P3', 45, 53, 0, NULL),
        (48, 38, 1, N'M', 1, 1, 2, NULL),
        (49, 38, 2, N'M', 8, 8, 0, NULL),
        (50, 39, 1, N'P3', 28, 32, 0, NULL),
        (51, 41, 7, N'M', 9, 10, 0, NULL),
        (52, 43, 6, N'P4', 29, 29, 0, N'Jury service'),
        (53, 7, 1, N'P3', 77, 83, 0, NULL),
        (54, 7, 1, N'P2', 22, 22, 1, NULL),
        (55, 46, 1, N'P2', 42, 46, 0, NULL),
        (56, 47, 1, N'M', 7, 13, 0, NULL),
        (57, 49, 1, N'P4', 44, 44, 1, NULL),
        (58, 50, 5, N'P3', 43, 44, 0, NULL),
        (59, 53, 2, N'P4', 2, 2, 0, NULL),
        (60, 55, 1, N'P3', 38, 46, 0, NULL)
    ) AS v (Id, HrId, TypeId, Anchor, FromDay, ToDay, HalfDay, Comment)
    CROSS APPLY (SELECT CASE v.Anchor
        WHEN N'P1' THEN @Pi1Start WHEN N'P2' THEN @Pi2Start WHEN N'P3' THEN @Pi3Start WHEN N'P4' THEN @Pi4Start
        WHEN N'W' THEN @WeekStart ELSE @b_MonthMon END AS Base) AS b;
    SET IDENTITY_INSERT dbo.Holidays OFF;

    -- public holidays
    DROP TABLE IF EXISTS #b_Ph;
    CREATE TABLE #b_Ph (CountryId int NULL, CityId int NULL, D date NOT NULL, [Name] nvarchar(100) NOT NULL, Description nvarchar(200) NULL);

    INSERT INTO #b_Ph (CountryId, CityId, D, [Name], Description)
    SELECT f.CountryId, NULL, DATEFROMPARTS(y.Y, f.Mo, f.Dy), f.HolidayName, N'National holiday'
    FROM (VALUES
        (1, 1, 1, N'New Year''s Day'), (1, 12, 25, N'Christmas Day'), (1, 12, 26, N'Boxing Day'),
        (2, 1, 1, N'New Year''s Day'), (2, 5, 1, N'Labour Day'), (2, 5, 8, N'Liberation Day'),
        (2, 7, 5, N'Saints Cyril and Methodius Day'), (2, 7, 6, N'Jan Hus Day'), (2, 9, 28, N'Czech Statehood Day'),
        (2, 10, 28, N'Independent Czechoslovak State Day'), (2, 11, 17, N'Struggle for Freedom and Democracy Day'),
        (2, 12, 24, N'Christmas Eve'), (2, 12, 25, N'Christmas Day'), (2, 12, 26, N'St. Stephen''s Day'),
        (3, 1, 26, N'Republic Day'), (3, 8, 15, N'Independence Day'), (3, 10, 2, N'Gandhi Jayanti'), (3, 12, 25, N'Christmas Day'),
        (4, 1, 1, N'New Year''s Day'), (4, 6, 19, N'Juneteenth'), (4, 7, 4, N'Independence Day'), (4, 11, 11, N'Veterans Day'), (4, 12, 25, N'Christmas Day'),
        (5, 1, 1, N'New Year''s Day'), (5, 1, 6, N'Epiphany'), (5, 5, 1, N'Labour Day'), (5, 5, 3, N'Constitution Day'),
        (5, 8, 15, N'Assumption Day'), (5, 11, 1, N'All Saints'' Day'), (5, 11, 11, N'Independence Day'),
        (5, 12, 25, N'Christmas Day'), (5, 12, 26, N'Second Day of Christmas')
    ) AS f (CountryId, Mo, Dy, HolidayName)
    CROSS JOIN (VALUES (YEAR(@Today) - 1), (YEAR(@Today)), (YEAR(@Today) + 1)) AS y (Y);

    INSERT INTO #b_Ph (CountryId, CityId, D, [Name], Description)
    SELECT c.CountryId, NULL, x.D, N'Company Wellbeing Day', N'Company-wide day off'
    FROM (VALUES (1), (2), (3), (4), (5)) AS c (CountryId)
    CROSS JOIN (VALUES (DATEADD(day, 24, @Pi3Start)), (DATEADD(day, 31, @Pi4Start))) AS a (Anchor)
    CROSS APPLY (
        SELECT TOP (1) DATEADD(day, n.n, a.Anchor) AS D
        FROM #N AS n
        WHERE n.n < 14
            AND DATEDIFF(day, '19000101', DATEADD(day, n.n, a.Anchor)) % 7 < 5
            AND NOT EXISTS (SELECT 1 FROM #b_Ph AS p WHERE p.CountryId = c.CountryId AND p.CityId IS NULL AND p.D = DATEADD(day, n.n, a.Anchor))
        ORDER BY n.n
    ) AS x;

    INSERT INTO #b_Ph (CountryId, CityId, D, [Name], Description)
    SELECT NULL, m.CityId, p.D, p.[Name], p.Description
    FROM #b_Ph AS p
    JOIN (VALUES (3, 5), (1, 1)) AS m (CountryId, CityId) ON m.CountryId = p.CountryId
    WHERE p.CityId IS NULL;

    INSERT INTO #b_Ph (CountryId, CityId, D, [Name], Description)
    SELECT NULL, l.CityId, x.D, l.HolidayName, l.Descr
    FROM (VALUES
        (5, DATEADD(day, 45, @Pi3Start), N'Pune Site Foundation Day', N'Local holiday for the Pune office'),
        (5, DATEADD(day, 38, @Pi4Start), N'Pune Harvest Festival', N'Local holiday for the Pune office'),
        (4, DATEADD(day, 52, @Pi3Start), N'Brno City Day', N'Only Brno row: replaces the national holidays for Brno'),
        (1, DATEADD(day, 45, @Pi4Start), N'London Borough Day', N'Local holiday for the London office')
    ) AS l (CityId, Anchor, HolidayName, Descr)
    CROSS APPLY (
        SELECT TOP (1) DATEADD(day, n.n, l.Anchor) AS D
        FROM #N AS n
        WHERE n.n < 14
            AND DATEDIFF(day, '19000101', DATEADD(day, n.n, l.Anchor)) % 7 < 5
            AND NOT EXISTS (SELECT 1 FROM #b_Ph AS p WHERE p.CityId = l.CityId AND p.D = DATEADD(day, n.n, l.Anchor))
        ORDER BY n.n
    ) AS x;

    SET IDENTITY_INSERT dbo.PublicHolidays ON;
    INSERT INTO dbo.PublicHolidays (Id, CountryId, CityId, [Date], [Name], Description)
    SELECT ROW_NUMBER() OVER (ORDER BY CASE WHEN p.CityId IS NULL THEN 0 ELSE 1 END, p.CountryId, p.CityId, p.D),
        p.CountryId, p.CityId, p.D, p.[Name], p.Description
    FROM #b_Ph AS p;
    SET IDENTITY_INSERT dbo.PublicHolidays OFF;


    -- 30-features: features, team estimates, sprint plans, capacity board orders, comments
    SET @Section = N'30-features';
    PRINT N'Features: catalog, teams, stacks, skill splits, sprint plans, capacity board orders, comments';

    DECLARE @c_nl nchar(2) = NCHAR(13) + NCHAR(10);
    DECLARE @c_rows int;

    -- catalog
    DROP TABLE IF EXISTS #c_F;
    CREATE TABLE #c_F (Id int NOT NULL PRIMARY KEY, Pk nvarchar(10) NULL, IssueType nvarchar(60) NULL, Summary nvarchar(255) NOT NULL,
        FName nvarchar(255) NULL, Labels nvarchar(400) NULL, Components nvarchar(400) NULL, St nvarchar(50) NULL, Rag nvarchar(255) NULL,
        Ranking int NULL, Sp int NULL, ReqId int NULL, TaId int NULL, BoId int NULL, PiId int NULL, PiObjId int NULL, Fix char(1) NOT NULL);
    INSERT INTO #c_F (Id, Pk, IssueType, Summary, FName, Labels, Components, St, Rag, Ranking, Sp, ReqId, TaId, BoId, PiId, PiObjId, Fix) VALUES
        (101, N'PAY', N'Feature', N'Instant payment initiation API', N'Instant payment initiation', @CurPiLabel + N',instant-payments', N'Payments API', N'In Progress', N'Green', 10, -1, 5, 1, 1, 3, 1, 'A'),
        (102, N'PAY', N'Feature', N'Instant payment status notifications', N'Payment status notifications', N'instant-payments', N'Payments API', N'To Do', N'Green', 20, -1, 4, 3, 1, 3, 1, 'A'),
        (103, N'PAY', N'Feature', N'Card scheme tokenisation certification', N'Scheme token certification', N'tokenisation,scheme', N'Card Gateway', N'In Development', N'Amber', 30, -1, 5, 1, 3, 3, 2, 'X'),
        (104, N'PAY', N'Feature', N'Merchant portal token management screens', N'Token management UI', N'tokenisation,merchant-portal', N'Merchant Portal', N'In Progress', N'Red', 40, -1, 4, 1, 3, 3, 2, 'A'),
        (105, N'PAY', N'Feature', N'Token vault key rotation', N'Vault key rotation', N'tokenisation,security', N'Card Gateway', N'In Progress', N'Green', 50, -1, 5, 1, 3, 3, 2, 'A'),
        (106, N'PAY', N'Feature', N'Payee name confirmation check', N'Confirmation of payee', N'instant-payments,fraud', N'Payments API', N'In Development', N'Green', 60, -1, 4, 3, 2, 3, NULL, 'N'),
        (107, N'PAY', N'Feature', N'Card BIN range table refresh', N'BIN range refresh', N'cards', NULL, N'Analysis', NULL, 70, -1, 2, 2, 3, 3, NULL, 'A'),
        (108, N'PAY', N'Feature', N'Instant payment limits per customer segment', N'Segment payment limits', N'instant-payments', N'Payments API,Backend', N'To Do', N'Amber', 80, 20, 3, 2, 1, 3, 1, 'N'),
        (109, N'PAY', N'Feature', N'Fraud score in the instant payment flow', N'Inline fraud scoring', N'fraud,instant-payments', N'Backend', N'In Progress', N'Amber', 90, -1, 5, 1, 2, 3, 1, 'A'),
        (110, N'PAY', N'Feature', N'Fraud case management screens', N'Fraud case UI', N'fraud', N'Back Office', N'To Do', N'Green', 100, -1, 4, 1, 2, 3, NULL, 'A'),
        (111, N'PAY', N'Feature', N'Payment hub event streaming', N'Payment event stream', N'observability', N'Backend', N'In Progress', N'Green', 110, -1, 4, 3, 11, 3, 7, 'A'),
        (112, N'PAY', N'Feature', N'Real-time reconciliation reports', N'Real-time reconciliation', N'instant-payments,reporting', N'Reporting', N'To Do', N'Green', 120, -1, 3, 2, 1, 3, 1, 'A'),
        (113, N'PAY', N'Feature', N'Scheme connectivity failover', N'Scheme failover', N'instant-payments,resilience', N'Backend', N'Blocked', N'Red', 5, -1, 5, 1, 1, 3, 1, 'A'),
        (114, N'PAY', N'Feature', N'Fraud rules self-service editor', N'Fraud rules editor', N'fraud', N'Back Office', N'To Do', NULL, 140, -1, 2, 2, 2, 3, NULL, 'A'),
        (115, N'PAY', N'Feature', N'Payment repair queue', N'Payment repair queue', NULL, NULL, N'Backlog', NULL, 150, 5, 1, 3, 1, 3, NULL, 'A'),
        (116, N'PAY', N'Feature', N'Payment status webhook for corporates', N'Corporate status webhook', N'instant-payments', N'Payments API', N'In Review', N'Green', 160, -1, 4, 1, 1, 3, 1, 'A'),
        (117, N'PAY', N'Feature', N'Instant payment receipts as PDF', N'Payment receipts', N'instant-payments', N'Payments API', N'Done', N'Green', 170, -1, 5, 3, 1, 3, 1, 'A'),
        (118, N'PAY', N'Feature', N'Request to pay messages', N'Request to pay', N'instant-payments,mvp', N'Payments API', N'In Progress', N'Green', 180, 21, 5, 1, 1, 3, 1, 'A'),
        (119, N'PAY', N'Feature', N'Token lifecycle events from the schemes', N'Token lifecycle events', N'tokenisation', N'Card Gateway', N'Closed', N'Green', 190, -1, 5, 1, 3, 3, 2, 'A'),
        (120, N'PAY', N'Feature', N'Bulk instant payments upload', N'Bulk instant payments', @CurPiLabel + N',instant-payments', N'Payments API', N'In Progress', N'Green', 200, -1, 5, 1, 1, 3, 1, 'A'),
        (121, N'PAY', N'Feature', N'Legacy card token import', N'Legacy token import', N'tokenisation,migration', N'Card Gateway', N'Rejected', NULL, 210, -1, 1, 3, 3, 3, NULL, 'A'),
        (122, N'PAY', N'Feature', N'Card-on-file token migration', N'Card-on-file migration', N'tokenisation', N'Card Gateway', N'Analysis', N'Amber', 220, -1, 2, 2, 3, 3, 2, 'A'),
        (123, N'PAY', N'Feature', N'Payment hub capacity scaling', N'Hub autoscaling', N'resilience', N'Backend', N'In Development', N'Green', 230, -1, 5, 1, 11, 3, 7, 'A'),
        (124, N'PAY', N'Feature', N'Payment hub configuration service', N'Hub config service', N'resilience', N'Backend', N'To Do', N'Green', 240, -1, 4, 3, 11, 3, 7, 'A'),
        (125, N'PAY', N'Feature', N'Card controls in the merchant portal', N'Card controls UI', N'cards,merchant-portal', N'Merchant Portal', N'In Review', N'Green', 250, -1, 4, 1, 3, 3, 2, 'A'),
        (126, N'PAY', N'Feature', N'Token usage dashboard', N'Token usage dashboard', N'tokenisation,reporting', N'Reporting', N'Analysis', NULL, 260, -1, 2, NULL, 3, 3, NULL, 'A'),
        (127, N'PAY', N'Feature', N'Fraud alerts to customers', N'Customer fraud alerts', N'fraud', N'Notifications', N'To Do', N'Green', 270, -1, 4, 1, 2, 3, NULL, 'A'),
        (128, N'PAY', N'Feature', N'Fraud model monitoring', N'Fraud model monitoring', N'fraud,observability', N'Backend', N'Backlog', NULL, 280, -1, 1, 3, 2, 3, NULL, 'A'),
        (129, N'PAY', N'Feature', N'Card dispute intake', N'Dispute intake', N'cards', N'Back Office', N'Funnel', NULL, NULL, NULL, NULL, 3, 3, 3, NULL, 'A'),
        (130, N'PAY', N'Feature', N'Instant payments go-live readiness', N'Go-live readiness', @CurPiLabel + N',instant-payments,mvp', N'Payments API', N'To Do', N'Green', 300, -1, 5, 1, 1, 3, 1, 'A'),
        (131, N'PAY', N'Feature', N'Payment hub audit trail', N'Hub audit trail', N'resilience', N'Backend', N'Ready for UAT', N'Green', 310, -1, 5, 3, 11, 3, 7, 'A'),
        (132, N'PAY', N'Feature', N'Audit trail search screen', N'Audit trail search', N'resilience', N'Back Office', N'In Progress', N'Green', 320, -1, 4, 1, 11, 3, 7, 'A'),
        (133, N'PAY', N'Feature', N'Duplicate payment detection', N'Duplicate detection', N'fraud', N'Backend', N'In Progress', N'Amber', 330, -1, 4, 1, 2, 3, NULL, 'A'),
        (134, N'PAY', N'Feature', N'Scheme reporting extract', N'Scheme reporting', N'reporting', N'Reporting', N'In Progress', N'Green', 340, -1, 4, 3, 1, 3, NULL, 'A'),
        (135, N'PAY', N'Feature', N'Scheme reporting data feed', N'Scheme data feed', N'reporting', N'Backend', N'In UAT', N'Green', 350, -1, 5, 1, 1, 3, NULL, 'A'),
        (136, N'PAY', N'Feature', N'Payment hub monitoring dashboards', N'Hub monitoring', N'observability', N'Backend', N'In Progress', N'Green', 360, -1, 4, 1, 11, 3, 7, 'A'),
        (140, N'PAY', N'Feature', N'Instant payment cut-off calendar', N'Cut-off calendar', N'instant-payments', N'Payments API', N'In Development', N'Green', 1400, -1, 4, 1, 1, 3, 1, 'A'),
        (141, N'PAY', N'Feature', N'Request to pay for e-commerce', N'E-commerce request to pay', @NextPiLabel + N',instant-payments', N'Payments API', N'To Do', NULL, 410, -1, 3, 2, 1, 4, 1, 'A'),
        (142, N'PAY', N'Feature', N'Token vault high availability', N'Vault high availability', N'tokenisation,resilience', N'Card Gateway', N'In Progress', N'Amber', 420, -1, 5, 1, 3, 2, 2, 'A'),
        (143, N'PAY', N'Feature', N'Instant payments for joint accounts', N'Joint account payments', @CurPiLabel + N',instant-payments', N'Payments API', N'In Progress', N'Green', 430, -1, 4, 1, 1, NULL, 1, 'A'),
        (144, N'PAY', N'Feature', N'Tokenised card push provisioning', N'Push provisioning', @CurPiLabel + N',tokenisation', N'Card Gateway', N'In Review', N'Amber', 440, -1, 5, 1, 3, 2, 2, 'A'),
        (145, N'PAY', N'Feature', N'Payment hub API rate limiting', N'API rate limiting', N'resilience', N'Payments API', N'In Progress', N'Green', 450, -1, 4, 1, 11, 3, 7, 'A'),
        (146, N'PAY', N'Story', N'Payment hub log retention clean-up', NULL, NULL, NULL, N'To Do', NULL, 460, -1, NULL, 2, NULL, 3, NULL, 'N'),
        (147, N'PAY', N'Feature', N'Card tokenisation for recurring payments', N'Recurring payment tokens', @NextPiLabel + N',tokenisation', N'Card Gateway', N'To Do', N'Green', 470, -1, 3, 1, 3, 4, 2, 'A'),
        (148, N'PAY', N'Feature', N'Payment hub cost reporting', N'Hub cost reporting', N'observability', N'Reporting', N'Analysis', NULL, 480, -1, 2, 3, 11, 4, 7, 'A'),
        (149, N'PAY', N'Feature', N'Fraud screening for request to pay', N'Request to pay screening', N'fraud', N'Backend', N'To Do', NULL, 490, -1, 2, 2, 2, 4, NULL, 'A'),
        (150, N'PAY', N'Feature', N'Card gateway TLS certificate automation', N'Certificate automation', N'cards,security', N'Card Gateway', N'In Progress', N'Green', 500, -1, 4, 3, 3, 3, NULL, 'A'),
        (151, N'PAY', N'Feature', N'Payment hub disaster recovery test', N'DR test', N'resilience', NULL, N'Analysis', NULL, 510, 8, 1, 3, NULL, 3, 7, 'N'),
        (152, N'PAY', N'Feature', N'Instant payments for business accounts', N'Business account payments', @NextPiLabel + N',committed,instant-payments', N'Payments API', N'To Do', N'Green', 520, -1, 5, 1, 1, NULL, 1, 'A'),
        (153, N'PAY', N'Task', N'Archive legacy payment hub logs', NULL, @NextPiLabel, NULL, N'Backlog', NULL, 530, -1, 1, 3, 11, NULL, NULL, 'A'),
        (154, N'PAY', N'Feature', N'Instant payments pilot', N'Instant payments pilot', N'instant-payments,mvp', N'Payments API', N'Done', N'Blue - Delivered', 540, -1, 5, 1, 1, 2, 1, 'A'),
        (155, N'PAY', N'Feature', N'Token vault MVP', N'Token vault MVP', N'tokenisation,mvp', N'Card Gateway', N'Accepted for Release', N'Green', 550, -1, 5, 1, 3, 2, 2, 'A'),
        (156, N'PAY', N'Feature', N'Merchant portal login redesign', N'Portal login redesign', N'merchant-portal', N'Merchant Portal', N'Closed', N'Green', 560, -1, 5, 3, 3, 2, NULL, 'A'),
        (157, N'PAY', N'Feature', N'Payment hub discovery', N'Hub discovery', N'discovery', NULL, N'Done', N'Green', 570, -1, 5, 3, 1, 1, NULL, 'N'),
        (158, N'PAY', N'Feature', N'Cheque imaging replacement', N'Cheque imaging', NULL, NULL, N'Cancelled', NULL, 580, 13, 1, 3, NULL, 1, NULL, 'N'),
        (159, N'PAY', N'Feature', N'Cross-border instant payments', N'Cross-border instant', N'instant-payments,cross-border', NULL, N'Funnel', NULL, NULL, NULL, NULL, 3, NULL, 5, NULL, 'N'),
        (160, N'PAY', N'Feature', N'Payment hub user access review', N'Access review', N'security', N'Back Office', N'Backlog', NULL, 600, -1, 2, 3, 11, 5, NULL, 'A'),
        (161, N'PAY', N'Feature', N'Scheduled instant payments', N'Scheduled instant payments', @NextPiLabel + N',instant-payments', N'Payments API', N'To Do', N'Green', 1610, -1, 4, 1, 1, 4, 1, 'A'),
        (162, N'PAY', N'Feature', N'Payment hub archive search', N'Archive search', N'resilience', N'Back Office', N'Backlog', NULL, 1620, -1, 1, 3, 11, 4, 7, 'A'),
        (163, N'PAY', N'Feature', N'Instant payment limits API for partners', N'Partner limits API', N'instant-payments,partners', N'Payments API', N'Funnel', NULL, 1630, -1, 2, 2, 1, 4, NULL, 'A'),
        (201, N'MOB', N'Feature', N'Mobile onboarding ID verification', N'ID verification in app', @CurPiLabel + N',onboarding', N'iOS,Android', N'In Progress', N'Green', 610, -1, 5, 1, 4, 3, 3, 'A'),
        (202, N'MOB', N'Feature', N'Biometric login refresh', N'Biometric login', N'security', N'iOS,Android', N'In Development', N'Green', 620, -1, 4, 1, 5, 3, 7, 'A'),
        (203, N'PAY', N'Feature', N'Wallet card tokenisation in the app', N'In-app card tokens', N'tokenisation,wallet', N'iOS,Payments API', N'In Progress', N'Amber', 630, -1, 5, 2, 3, 3, 2, 'A'),
        (204, N'PAY', N'Feature', N'Android tap to pay', N'Tap to pay', N'wallet', N'Android', N'Ready for UAT', N'Green', 640, -1, 5, 1, 3, 3, 2, 'A'),
        (205, N'MOB', N'Feature', N'Onboarding web journey', N'Onboarding web journey', N'onboarding', N'Web', N'In Review', N'Green', 650, -1, 4, 3, 4, 3, 3, 'A'),
        (206, N'MOB', N'Feature', N'Screen reader support for payments', N'Screen reader payments', N'accessibility', N'iOS,Android', N'In UAT', N'Red', 660, -1, 4, 1, 12, 3, 8, 'A'),
        (207, N'MOB', N'Feature', N'App start-up performance', N'App start-up time', N'performance', N'iOS,Android', N'To Do', NULL, 670, -1, 3, 2, 5, 3, NULL, 'A'),
        (208, N'PAY', N'Feature', N'Mobile payment limits screen', N'Mobile payment limits', N'payments', N'Mobile', N'Analysis', NULL, 680, -1, 2, 3, 3, 3, NULL, 'A'),
        (209, N'MOB', N'Feature', N'Accessibility colour contrast fixes', N'Colour contrast fixes', N'accessibility', N'Web', N'Blocked', N'Amber', 690, -1, 4, 3, 12, 3, 8, 'A'),
        (210, N'MOB', N'Feature', N'In-app notification centre', N'Notification centre', N'notifications', N'iOS,Android', N'Done', N'Green', 700, -1, 5, 1, 5, 3, NULL, 'A'),
        (211, N'MOB', N'Feature', N'Video call onboarding support', N'Video onboarding', @NextPiLabel + N',onboarding', N'iOS,Android', N'To Do', N'Green', 710, -1, 3, 2, 4, 4, 3, 'A'),
        (212, N'MOB', N'Feature', N'Dynamic font sizes', N'Dynamic font sizes', N'accessibility', N'Web', N'Backlog', NULL, 720, -1, 1, 3, 12, 4, 8, 'A'),
        (213, N'PAY', N'Feature', N'Wallet support for wearables', N'Wearable wallet', N'wallet', N'iOS,Android', N'Funnel', NULL, 730, -1, 1, 3, 3, 4, NULL, 'A'),
        (214, N'MOB', N'Feature', N'Mobile onboarding MVP', N'Onboarding MVP', N'onboarding,mvp', N'iOS,Android', N'Done', N'Green', 740, -1, 5, 1, 4, 2, 3, 'A'),
        (215, N'MOB', N'Feature', N'App analytics consent banner', N'Analytics consent', N'privacy', N'Web', N'Closed', N'Green', 750, -1, 5, 3, 5, 2, NULL, 'A'),
        (216, N'MOB', N'Feature', N'Saved payees in the app', N'Saved payees', @CurPiLabel + N',payments', N'iOS,Android', N'To Do', N'Green', 760, -1, 4, 1, 5, NULL, NULL, 'A'),
        (217, N'MOB', N'Feature', N'Mobile cheque deposit', N'Cheque deposit', NULL, N'iOS,Android', N'Funnel', NULL, NULL, NULL, NULL, 3, NULL, 5, NULL, 'N'),
        (218, N'MOB', N'Task', N'Upgrade mobile build agents', NULL, NULL, NULL, NULL, NULL, 780, -1, NULL, 3, 5, 3, NULL, 'A'),
        (219, N'MOB', N'Feature', N'Open banking income check in onboarding', N'Open banking income check', @NextPiLabel + N',onboarding', N'iOS,Android', N'To Do', N'Green', 712, -1, 4, 1, 4, 4, 3, 'A'),
        (220, N'PAY', N'Feature', N'Card freeze and unfreeze in the app', N'Card freeze in app', N'cards,wallet', N'iOS,Android', N'To Do', N'Amber', 714, -1, 3, 2, 3, 4, 2, 'A'),
        (221, N'MOB', N'Feature', N'App home screen redesign', N'Home screen redesign', N'performance', N'iOS,Android', N'Analysis', NULL, 716, -1, 2, 3, 5, 4, NULL, 'A'),
        (222, N'MOB', N'Feature', N'Offline balance view', N'Offline balances', N'performance', N'iOS,Android', N'Backlog', NULL, 732, -1, 1, 3, 5, 4, NULL, 'A'),
        (223, N'MOB', N'Feature', N'Voice control for payments', N'Voice payments', N'accessibility', N'iOS', N'Funnel', NULL, 734, -1, 1, 3, 12, 4, 8, 'A'),
        (301, N'RISK', N'Feature', N'RWA standardised approach calculator', N'SA calculator', @CurPiLabel + N',basel,rwa', NULL, N'In Progress', N'Green', 810, -1, 5, 1, 6, 3, 4, 'A'),
        (302, N'RISK', N'Feature', N'Output floor calculation', N'Output floor', N'basel,rwa', NULL, N'In Development', N'Green', 820, -1, 5, 1, 6, 3, 4, 'A'),
        (303, N'RISK', N'Feature', N'PD model recalibration pipeline', N'PD recalibration', N'models,irb', NULL, N'In Progress', N'Green', 830, -1, 5, 1, 7, 3, 4, 'A'),
        (304, N'RISK', N'Feature', N'RWA reconciliation report', N'RWA reconciliation', N'basel,reporting', NULL, N'In Progress', N'Amber', 840, -1, 4, 1, 6, 3, 4, 'A'),
        (305, N'RISK', N'Feature', N'LGD downturn adjustment', N'LGD downturn', N'models', NULL, N'Analysis', N'Green', 850, -1, 2, 3, 7, 3, NULL, 'A'),
        (306, N'RISK', N'Feature', N'Counterparty credit risk SA-CCR', N'SA-CCR', N'basel,ccr', NULL, N'In Progress', N'Red', 860, 34, 3, 2, 6, 3, 4, 'A'),
        (307, N'RISK', N'Feature', N'Risk data feed to the data lake', N'Risk data feed', N'basel,risk-feed', NULL, N'In Review', N'Green', 870, -1, 4, 1, 6, 3, NULL, 'A'),
        (308, N'RISK', N'Feature', N'Model inventory register', N'Model inventory', N'models', NULL, N'Done', N'Green', 880, -1, 5, 3, 7, 3, NULL, 'A'),
        (309, N'RISK', N'Feature', N'Exposure data quality checks', N'Exposure DQ checks', N'basel,data-quality', NULL, N'Closed', N'Green', 890, -1, 5, 1, 6, 3, 4, 'A'),
        (310, N'RISK', N'Feature', N'Stress scenario library', N'Scenario library', N'models,stress-testing', NULL, N'To Do', NULL, 900, -1, 3, 1, 7, 3, NULL, 'N'),
        (311, N'RISK', N'Feature', N'Collateral haircut tables', N'Collateral haircuts', N'basel', NULL, N'Backlog', NULL, 910, -1, 1, 3, NULL, 3, NULL, 'A'),
        (312, N'RISK', N'Feature', N'Credit conversion factors update', N'CCF update', N'basel,' + @CurPiLabel, NULL, N'In Progress', N'Green', 920, -1, 4, 1, 6, NULL, 4, 'A'),
        (313, N'RISK', N'Feature', N'RWA engine performance tuning', N'RWA performance', N'rwa,performance', NULL, N'To Do', N'Green', 930, -1, 3, 3, 6, 4, 4, 'A'),
        (314, N'RISK', N'Feature', N'IRB parallel run', N'IRB parallel run', N'irb,' + @NextPiLabel, NULL, N'Analysis', N'Amber', 940, -1, 2, 2, 7, 4, 4, 'A'),
        (315, N'RISK', N'Feature', N'Climate risk scenarios', N'Climate scenarios', N'stress-testing,climate', NULL, N'Funnel', NULL, 950, -1, 1, 3, 7, 4, NULL, 'A'),
        (316, N'RISK', N'Feature', N'RWA engine foundation', N'RWA engine foundation', N'basel,rwa', NULL, N'Accepted for Release', N'Green', 960, -1, 5, 1, 6, 2, 4, 'A'),
        (317, N'RISK', N'Feature', N'Default data mart', N'Default data mart', N'models', NULL, N'Done', N'Green', 970, -1, 5, 3, 7, 2, NULL, 'A'),
        (318, N'RISK', N'Feature', N'Basel IV impact study', N'Basel IV impact study', N'basel', NULL, N'Closed', N'Green', 980, 13, 5, 3, 6, 1, 4, 'N'),
        (401, N'RISK', N'Feature', N'Legacy exposure history migration', N'Exposure history migration', N'Legacy,migration', NULL, N'In Progress', N'Green', 1010, -1, 5, 1, 8, 3, 5, 'A'),
        (402, N'RISK', N'Feature', N'Legacy report catalogue mapping', N'Report catalogue mapping', N'Legacy', NULL, N'In Development', N'Green', 1020, -1, 4, 3, 8, 3, 5, 'A'),
        (403, N'RISK', N'Feature', N'Legacy batch job decommissioning', N'Batch decommissioning', N'Legacy,decommission', NULL, N'To Do', NULL, 1030, -1, 3, 2, 8, 3, 5, 'A'),
        (404, N'RISK', N'Feature', N'Legacy data reconciliation', N'Data reconciliation', N'Legacy,data-quality', NULL, N'Blocked', N'Red', 1040, -1, 4, 1, 8, 3, 5, 'A'),
        (405, N'RISK', N'Feature', N'Legacy risk warehouse read-only mode', N'Warehouse read-only', N'Legacy,' + @NextPiLabel, NULL, N'Analysis', NULL, 1050, -1, 2, 2, 8, 4, 5, 'A'),
        (406, N'RISK', N'Feature', N'Legacy user access removal', N'Access removal', N'Legacy,security', NULL, N'To Do', NULL, 1060, -1, 1, 3, 8, 4, 5, 'A'),
        (407, N'RISK', N'Feature', N'Legacy data inventory', N'Data inventory', N'Legacy,' + @CurPiLabel, NULL, N'Done', N'Green', 1070, -1, 5, 3, 8, 3, 5, 'A'),
        (408, N'RISK', N'Feature', N'Legacy archive retention policy', N'Archive retention', N'Legacy,decommission', NULL, N'Funnel', NULL, NULL, NULL, NULL, 3, 8, 5, NULL, 'A'),
        (409, N'RISK', N'Feature', N'Legacy risk batch move to the second data centre', N'Legacy batch move', N'Legacy,migration', NULL, N'In Progress', N'Green', 1080, -1, 4, 1, 8, 2, 5, 'A'),
        (501, N'DATA', N'Feature', N'Core banking daily ingestion', N'Core banking ingestion', N'ingestion', NULL, N'In Progress', N'Green', 1110, -1, 5, 1, 9, 3, 6, 'A'),
        (502, N'DATA', N'Feature', N'Cards data ingestion', N'Cards ingestion', N'ingestion', NULL, N'In Development', N'Green', 1120, -1, 4, 1, 9, 3, 6, 'A'),
        (503, N'PAY', N'Feature', N'Payment events into the data lake', N'Payment events ingestion', N'data,ingestion', NULL, N'In Review', N'Amber', 1130, -1, 4, 1, 9, 3, 6, 'A'),
        (504, N'PAY', N'Feature', N'Payments reporting data mart', N'Payments data mart', N'data,reporting', N'Backend', N'Ready for UAT', N'Green', 1140, -1, 5, 3, 10, 3, NULL, 'A'),
        (505, N'DATA', N'Feature', N'Data lake landing zone', N'Landing zone', N'platform', NULL, N'Done', N'Green', 1150, -1, 5, 1, 9, 3, 6, 'A'),
        (506, N'DATA', N'Feature', N'Self-service BI workspace', N'BI workspace', N'analytics', NULL, N'In Progress', N'Red', 1160, -1, 3, 2, 10, 3, NULL, 'A'),
        (507, N'DATA', N'Feature', N'Data catalogue rollout', N'Data catalogue', N'governance', NULL, N'To Do', N'Green', 1170, -1, 3, 3, 10, 4, NULL, 'A'),
        (508, N'PAY', N'Feature', N'Payments data quality scorecard', N'Payments DQ scorecard', N'data,' + @NextPiLabel + N',committed', NULL, N'Analysis', NULL, 1180, -1, 2, 3, 10, NULL, NULL, 'A'),
        (509, N'DATA', N'Feature', N'Data lake cost monitoring', N'Cost monitoring', N'platform,' + @NextPiLabel, NULL, N'Backlog', NULL, 1190, -1, 1, 3, 9, 4, NULL, 'A'),
        (510, N'DATA', N'Feature', N'Data lake proof of concept', N'Data lake PoC', N'platform', NULL, N'Closed', N'Green', 1200, -1, 5, 3, 9, 2, 6, 'N'),
        (511, N'DATA', N'Feature', N'Real-time analytics streaming', N'Streaming analytics', NULL, NULL, N'Funnel', NULL, NULL, NULL, NULL, 3, 10, NULL, NULL, 'N'),
        (512, N'DATA', N'Story', N'Partition large fact tables', NULL, N'platform', NULL, NULL, NULL, 1220, -1, NULL, 3, 9, 3, NULL, 'A'),
        (901, NULL, N'Feature', N'Branch printer replacement', N'Branch printer replacement', NULL, NULL, N'Backlog', NULL, NULL, -1, 1, 3, NULL, 3, NULL, 'A'),
        (902, N'PAY', N'Feature', N'Payment hub runbook refresh', N'Runbook refresh', NULL, NULL, N'Backlog', NULL, 1302, -1, 1, 3, 11, 3, NULL, 'A'),
        (903, N'CRM', N'Feature', N'CRM contact synchronisation', N'CRM contact sync', N'crm', NULL, N'To Do', NULL, 1303, 8, 2, 3, NULL, 3, NULL, 'A'),
        (904, N'PAY', N'Feature', N'Wallet spending insights', N'Spending insights', N'data,insights', N'iOS', N'Analysis', NULL, 1304, 8, 2, 2, 14, 3, NULL, 'A'),
        (905, N'RISK', N'Feature', N'Data quality rules for risk feeds', N'Risk feed DQ rules', NULL, NULL, N'In Progress', N'Amber', 1305, 5, 3, 3, 14, 3, NULL, 'A'),
        (906, N'DATA', N'Feature', N'ETL quality dashboards', N'ETL quality dashboards', N'data-quality', N'ETL', NULL, NULL, 1306, -1, 1, 3, 14, 4, NULL, 'A');

    DROP TABLE IF EXISTS #c_E;
    CREATE TABLE #c_E (Id int NOT NULL PRIMARY KEY, RagExplain nvarchar(255) NULL, Deps nvarchar(255) NULL, Ext bit NOT NULL, Conf int NULL, UnfId int NULL, DExp int NULL, Nav nvarchar(255) NULL);
    INSERT INTO #c_E (Id, RagExplain, Deps, Ext, Conf, UnfId, DExp, Nav) VALUES
        (101, NULL, NULL, 0, 90, NULL, 28, N'NAV-20481'),
        (103, N'Certification slot moved by the scheme to sprint 4', N'Visa and Mastercard certification slots', 1, 75, NULL, 63, N'NAV-20502'),
        (104, N'Depends on scheme certification that slipped to sprint 4', NULL, 0, NULL, NULL, NULL, NULL),
        (108, N'Limits policy not yet signed off by retail risk', NULL, 0, NULL, NULL, NULL, NULL),
        (109, N'Vendor model accuracy below target in the pilot', N'Fraud scoring vendor API v3', 1, 75, NULL, NULL, NULL),
        (113, N'Scheme test environment unavailable for three weeks', N'Scheme test environment', 1, NULL, NULL, 21, N'NAV-20517'),
        (118, NULL, N'Billers onboarding to the request to pay scheme', 0, NULL, NULL, NULL, NULL),
        (120, NULL, NULL, 0, 90, NULL, 35, NULL),
        (122, N'Merchant opt-in lower than planned', NULL, 0, NULL, NULL, NULL, NULL),
        (123, NULL, NULL, 0, 90, NULL, NULL, NULL),
        (130, NULL, N'Scheme go-live approval', 1, NULL, NULL, 49, NULL),
        (133, N'False positive rate still above 2 percent', NULL, 0, NULL, NULL, NULL, NULL),
        (141, NULL, NULL, 0, 40, NULL, 105, NULL),
        (142, N'Second data centre build is late', NULL, 0, NULL, NULL, NULL, NULL),
        (144, N'Carried over from the previous PI', N'Wallet provider onboarding', 1, NULL, NULL, NULL, NULL),
        (147, NULL, NULL, 0, 40, 1, 140, NULL),
        (154, NULL, NULL, 0, NULL, NULL, -35, NULL),
        (158, NULL, NULL, 0, NULL, 4, NULL, NULL),
        (159, NULL, NULL, 0, NULL, 2, NULL, NULL),
        (201, NULL, NULL, 0, 75, NULL, 42, N'NAV-31007'),
        (203, N'Apple entitlement review pending', N'Apple Pay entitlement approval', 1, NULL, NULL, 56, NULL),
        (206, N'Accessibility retest failed on Android', NULL, 0, NULL, NULL, NULL, NULL),
        (207, NULL, NULL, 0, 40, 3, NULL, NULL),
        (209, N'Design system update not released yet', N'Design system release 4.2', 0, NULL, NULL, NULL, NULL),
        (211, NULL, NULL, 0, 75, NULL, NULL, NULL),
        (219, NULL, N'Open banking aggregator contract', 1, 75, NULL, 126, NULL),
        (220, N'Card controls API from Core Payments not planned yet', N'Card controls API (PAY-125)', 0, NULL, NULL, NULL, NULL),
        (223, NULL, NULL, 0, NULL, 3, NULL, NULL),
        (301, NULL, NULL, 0, 90, NULL, NULL, N'NAV-40112'),
        (302, NULL, NULL, 0, 75, NULL, 77, NULL),
        (304, N'Differences above tolerance for two portfolios', NULL, 0, NULL, NULL, NULL, NULL),
        (306, N'Trade data feed incomplete for derivatives', N'Trade repository extract', 1, NULL, NULL, 84, N'NAV-40140'),
        (314, N'Model validation team capacity is limited', NULL, 0, 40, NULL, NULL, NULL),
        (315, NULL, NULL, 0, NULL, 4, NULL, NULL),
        (401, NULL, NULL, 0, NULL, NULL, 42, N'NAV-50021'),
        (404, N'Source extracts missing for 2019 to 2021', N'Archive restore by the infrastructure team', 0, NULL, NULL, NULL, NULL),
        (501, NULL, NULL, 0, 90, NULL, 28, N'NAV-60009'),
        (503, N'Payment hub event schema still changing', N'Payment hub event stream (PAY-111)', 0, NULL, NULL, NULL, NULL),
        (506, N'BI licences not funded', NULL, 0, NULL, 2, NULL, NULL),
        (507, NULL, NULL, 0, NULL, NULL, 112, NULL),
        (511, NULL, NULL, 0, NULL, 3, NULL, NULL),
        (904, NULL, NULL, 0, NULL, NULL, 70, NULL),
        (905, N'Owner not agreed between trains', NULL, 0, NULL, NULL, NULL, NULL);

    DROP TABLE IF EXISTS #c_X;
    CREATE TABLE #c_X (Id int NOT NULL PRIMARY KEY, Descr nvarchar(max) NULL, Ac nvarchar(max) NULL);
    INSERT INTO #c_X (Id, Descr, Ac) VALUES
        (101, N'As a retail customer, I want to send an instant payment from my account so that the payee receives the money within seconds.\n\nh3. Scenario\n*Given* a customer with sufficient balance\n*When* they submit an instant payment to a reachable bank\n*Then* the payment is settled and confirmed within 10 seconds',
            N'h2. Acceptance criteria\n# Payment initiation API accepts single instant payments\n# Settlement confirmation is returned within 10 seconds\n# Rejected payments return a scheme reason code'),
        (102, N'As a payer, I want status notifications for my instant payments so that I know the outcome without refreshing.\n\nh3. Scenario\n*Given* an instant payment in progress\n*When* the scheme confirms or rejects it\n*Then* a status notification is published to the channel',
            N'h2. Acceptance criteria\n* Status events published for accepted, rejected and timed out payments\n* Events contain the end-to-end reference'),
        (103, N'h3. Context\nBoth card schemes require token service provider certification before tokens go live.\n\nh3. Scope\n||Scheme||Test cases||Slot||\n|Visa|142|sprint 4|\n|Mastercard|118|sprint 4|\n\n{panel:title=Out of scope}\nDomestic scheme tokens follow in a later PI.\n{panel}',
            N'h2. Acceptance criteria\n# All mandatory scheme test cases pass\n# Certification letters received from both schemes\n# Production keys exchanged and stored in the HSM'),
        (104, N'h3. Context\nMerchants manage card tokens in the merchant portal.\n\nh3. Scope\n* List and search tokens\n* Suspend and resume a token\n* Export token report',
            N'h2. Acceptance criteria\n* Merchants can suspend and resume tokens from the portal\n* All actions are written to the audit log'),
        (105, N'Rotate token vault encryption keys every 90 days without downtime.',
            N'h2. Acceptance criteria\n# Key rotation runs online with zero failed transactions\n# Old keys retired after re-encryption completes'),
        (106, N'As a payer, I want the payee name checked before I send an instant payment so that I do not pay the wrong account.\n\nh3. Scenario\n*Given* a new payee with an account number and a name\n*When* the payer confirms the payment details\n*Then* the receiving bank returns an exact, close or no match result',
            N'h2. Acceptance criteria\n* Payee name check completes in under 2 seconds\n* Close match shows the suggested name to the customer'),
        (107, N'Refresh the card BIN range table daily from the scheme files.',
            N'h2. Acceptance criteria\n* BIN table refreshed daily from both scheme files\n* Failed loads raise an operations alert'),
        (108, N'h3. Context\nDifferent instant payment limits per customer segment, approved by retail risk.\n\nh3. Segments\n* Personal\n* Premier\n* Small business',
            N'h2. Acceptance criteria\n# Limits configurable per segment without a release\n# Payments above the limit are rejected with a clear message'),
        (109, N'h3. Context\nCall the fraud scoring service inline before releasing an instant payment.\n\n_Latency budget: 300 ms at the 99th percentile._',
            N'h2. Acceptance criteria\n# Every instant payment receives a fraud score before release\n# High scores hold the payment for review\n# Scoring timeouts fall back to the rules engine'),
        (110, N'Back office screens for fraud analysts to work on held payments.',
            N'h2. Acceptance criteria\n* Analysts can release or reject held payments\n* Decisions are recorded with analyst and reason'),
        (111, N'h3. Context\nPublish payment hub events to the enterprise event stream for monitoring and the data lake.\n\nh3. Scope\n* Payment lifecycle events\n* Schema registry entries',
            N'h2. Acceptance criteria\n* All payment lifecycle events are published within one second\n* Schemas registered and versioned'),
        (112, N'As a payments operations analyst, I want intraday reconciliation against scheme settlement reports so that breaks are found the same day.\n\nh3. Scenario\n*Given* instant payments settled by the scheme\n*When* the settlement report arrives\n*Then* every unmatched payment is listed as a break',
            N'h2. Acceptance criteria\n# Reconciliation runs every 15 minutes during business hours\n# Breaks are listed with amount and scheme reference'),
        (113, N'h3. Context\nAutomatic failover to the secondary scheme connection.\n\nh3. Risks\n* Scheme test environment availability\n* Certificate renewal on the secondary link',
            N'h2. Acceptance criteria\n# Failover completes within 60 seconds\n# No duplicate payments after failover\n# Operations dashboard shows the active link'),
        (114, N'Let fraud analysts change screening rules without a release.',
            N'h2. Acceptance criteria\n* Rule changes need four-eyes approval\n* Every rule version is kept for audit'),
        (115, N'Queue for operations staff to repair payments that failed validation.',
            N'h2. Acceptance criteria\n* Repaired payments are resubmitted with the original reference\n* Repair actions require a reason code'),
        (116, N'Corporate customers receive payment status updates through a registered webhook.',
            N'h2. Acceptance criteria'),
        (117, N'Customers can download a PDF receipt for any instant payment.',
            N'h2. Acceptance criteria\n* Receipt available for 13 months\n* Receipt shows the end-to-end reference and timestamp'),
        (118, N'As a biller, I want to send a request to pay to a customer so that bills are paid without card details.\n\nh3. Scenario\n*Given* a customer registered for request to pay\n*When* the biller sends a request\n*Then* the customer can accept, decline or let it expire',
            N'h2. Acceptance criteria\n# Billers can create a request to pay through the API\n# Customers can accept or decline the request\n# Expired requests are closed automatically'),
        (119, N'Consume token lifecycle events (suspend, resume, delete) from both schemes.',
            N'h2. Acceptance criteria\n* Token status updated within one minute of the scheme event\n* Unknown tokens are logged and ignored'),
        (120, N'As a small business, I want to upload a file of instant payments so that I can pay many suppliers at once.\n\nh3. Scenario\n*Given* a valid payment file with up to 500 payments\n*When* the file is uploaded and approved\n*Then* each payment is sent as an individual instant payment',
            N'h2. Acceptance criteria\n# Files with up to 500 payments are accepted\n# Invalid lines are reported before approval\n# Each payment has its own status'),
        (121, N'Import card tokens from the retired legacy token store.',
            N'h2. Acceptance criteria\n* All active legacy tokens imported\n* Import report reconciles token counts'),
        (122, N'Migrate merchant card-on-file data to scheme tokens.',
            N'h2. Acceptance criteria\n* Opted-in merchants receive tokens for stored cards\n* Raw card numbers removed after migration'),
        (123, N'As an operations engineer, I want the payment hub to scale automatically so that peak traffic does not slow payments down.\n\nh3. Scenario\n*Given* traffic above 70 percent of current capacity\n*When* the load lasts for five minutes\n*Then* a new hub instance is started and added to the pool',
            N'h2. Acceptance criteria\n# Hub scales out under sustained load within five minutes\n# Hub scales in after one hour of low load\n# No payment is lost during scaling'),
        (124, N'Central configuration service for payment hub parameters.',
            N'h2. Acceptance criteria\n* Configuration changes apply without restart\n* Changes are versioned and can be rolled back'),
        (125, N'Merchants set card controls (country, channel, amount) in the merchant portal.',
            N'h2. Acceptance criteria\n* Controls apply to new authorisations within one minute\n* Merchants see the active controls per card'),
        (126, N'Dashboard of token usage per merchant and channel.',
            N'h2. Acceptance criteria\n* Daily token usage per merchant shown\n* Data refreshed every night'),
        (127, N'As a customer, I want an alert for suspicious card or payment activity so that I can react before money is lost.\n\nh3. Scenario\n*Given* a payment held by fraud screening\n*When* the hold is created\n*Then* the customer receives a push and SMS alert',
            N'h2. Acceptance criteria\n# Alerts are sent within 30 seconds of a hold\n# Customers can confirm or deny the payment from the alert'),
        (128, N'Monitor fraud model drift and alert the model owners.',
            N'h2. Acceptance criteria\n* Works in production'),
        (129, N'Digital intake form for card disputes, replacing the paper form.',
            N'h2. Acceptance criteria\n* Customers can raise a dispute online\n* Dispute reference returned immediately'),
        (130, N'As the release train engineer, I want a go-live readiness checklist for instant payments so that the launch decision is based on evidence.\n\nh3. Scenario\n*Given* all instant payment features in UAT\n*When* the readiness review is held\n*Then* every checklist item has an owner and a status',
            N'h2. Acceptance criteria\n# Checklist covers operations, support and scheme sign-off\n# Readiness review minutes stored with the feature'),
        (131, N'As a compliance officer, I want an immutable audit trail of every payment hub state change so that investigations have complete evidence.\n\nh3. Scenario\n*Given* a payment moving through the hub\n*When* its state changes\n*Then* an audit record with time, user and old and new state is written',
            N'h2. Acceptance criteria\n* Every state change is written to the audit store\n* Audit records cannot be changed or deleted'),
        (132, N'As an auditor, I want to search the payment hub audit trail so that I can answer regulator questions quickly.\n\nh3. Scenario\n*Given* an end-to-end payment reference\n*When* the auditor searches for it\n*Then* all audit records of that payment are listed in time order',
            N'h2. Acceptance criteria\n# Search by reference, account and date range\n# Results exportable to CSV'),
        (133, N'Detect duplicate instant payments submitted within a short time window.',
            N'h2. Acceptance criteria\n* Duplicates within 60 seconds are flagged to the customer\n* Customer can confirm a genuine repeat payment'),
        (134, N'Monthly scheme reporting extract for instant payments volumes and values.',
            N'h2. Acceptance criteria\n* Extract matches scheme format version 3\n* Totals reconcile with the general ledger'),
        (135, N'Daily data feed of scheme transactions to the reporting platform.',
            N'h2. Acceptance criteria\n* Feed delivered by 06:00 every business day\n* Row counts reconciled with the payment hub'),
        (136, N'h3. Context\nDashboards for payment hub throughput, latency and error rates.\n\nh3. Scope\n* Service level dashboards\n* Alert thresholds agreed with operations',
            N'h2. Acceptance criteria\n# Dashboards show throughput, latency and errors per minute\n# Alerts fire within two minutes of a threshold breach'),
        (140, N'As a customer, I want to see the cut-off times for payments to banks that are not yet instant so that I know when the money arrives.\n\nh3. Scenario\n*Given* a payee bank without instant payments\n*When* the customer enters the payment\n*Then* the expected arrival date is shown',
            N'h2. Acceptance criteria\n# Cut-off calendar maintained by operations\n# Expected arrival date shown before confirmation'),
        (141, N'Request to pay for online merchants at checkout.',
            N'h2. Acceptance criteria\n* Merchants can send a request to pay from checkout\n* Customer approval returns the merchant to the shop'),
        (142, N'Run the token vault active-active across both data centres.',
            N'h2. Acceptance criteria\n# Loss of one data centre causes no token service outage\n# Replication lag under one second'),
        (143, N'As a joint account holder, I want to send instant payments from the joint account so that either holder can pay bills.\n\nh3. Scenario\n*Given* a joint account with two holders\n*When* either holder sends an instant payment\n*Then* the payment follows the account mandate',
            N'h2. Acceptance criteria\n# Both holders can initiate instant payments\n# Mandate rules apply to payments above the limit'),
        (144, N'Push card tokens directly into mobile wallets from the card gateway.',
            N'h2. Acceptance criteria\n* Tokens provisioned to the wallet in under ten seconds\n* Provisioning failures are reported to the customer'),
        (145, N'h3. Context\nProtect the payment hub APIs from overload with rate limits per client.\n\nh3. Scope\n* Limits per API client\n* Burst allowance',
            N'h2. Acceptance criteria\n# Clients above their limit receive HTTP 429\n# Limits configurable per client without a release'),
        (146, NULL, NULL),
        (147, N'Use scheme tokens for recurring card payments and subscriptions.',
            N'h2. Acceptance criteria\n* Recurring payments use network tokens\n* Card updates are applied without customer action'),
        (148, N'Report payment hub infrastructure cost per payment type.',
            N'h2. Acceptance criteria\n* Monthly cost report per payment type\n* Report shared with finance'),
        (149, N'Screen request to pay messages for fraud before they reach the customer.',
            N'h2. Acceptance criteria\n* Suspicious requests are blocked before delivery\n* Billers are notified of blocked requests'),
        (150, N'Automate renewal of TLS certificates on the card gateway.',
            N'h2. Acceptance criteria\n* Certificates renewed 30 days before expiry\n* Renewal failures raise a P2 incident'),
        (151, N'Annual disaster recovery test of the payment hub.', NULL),
        (152, N'Extend instant payments to business current accounts.',
            N'h2. Acceptance criteria\n* Business accounts can send and receive instant payments\n* Dual authorisation supported for business users'),
        (153, N'Archive five years of legacy payment hub logs to cold storage.', NULL),
        (154, N'Pilot of instant payments with staff accounts before general availability.',
            N'h2. Acceptance criteria\n* 500 staff accounts sent instant payments in the pilot\n* Pilot report approved by the steering group'),
        (155, N'First release of the token vault with one card scheme.',
            N'h2. Acceptance criteria\n* Token vault live for one card scheme\n* Penetration test findings closed'),
        (156, N'Redesign of the merchant portal login with multi-factor authentication.',
            N'h2. Acceptance criteria\n* Multi-factor login for all merchant users\n* Old login page removed'),
        (157, N'Discovery for the new payment hub: options, costs and roadmap.',
            N'h2. Acceptance criteria\n* Options paper approved by the architecture board'),
        (158, N'Replace the cheque imaging system in branches.', NULL),
        (159, N'Idea: instant payments to selected foreign banks.', NULL),
        (160, N'Quarterly access review for payment hub operator roles.',
            N'h2. Acceptance criteria\n* Review evidence stored for audit\n* Leavers removed within one day'),
        (161, N'As a customer, I want to schedule an instant payment for a future date so that bills are paid exactly on the due date.\n\nh3. Scenario\n*Given* a scheduled instant payment for tomorrow\n*When* the execution date is reached\n*Then* the payment is sent as an instant payment at 06:00',
            N'h2. Acceptance criteria\n# Payments can be scheduled up to one year ahead\n# Scheduled payments can be changed or cancelled until the day before'),
        (162, N'Search archived payments older than 13 months for operations and audit.',
            N'h2. Acceptance criteria\n* Search by reference, account and date range\n* Results returned within ten seconds'),
        (163, N'Expose instant payment limits to partner banks through an API.', NULL),
        (201, N'As a new customer, I want to verify my identity in the app so that I can open an account without visiting a branch.\n\nh3. Scenario\n*Given* a new customer with a valid passport or ID card\n*When* they scan the document and take a selfie\n*Then* the identity check result is shown within two minutes\n\nh3. Notes\n* Vendor SDK for document scan\n* Manual review queue for unclear results',
            N'h2. Acceptance criteria\n# Document scan works on iOS and Android\n# Liveness check passes for genuine users\n# Unclear results go to manual review'),
        (202, N'Refresh biometric login to the latest platform APIs.',
            N'h2. Acceptance criteria\n* Face and fingerprint login on supported devices\n* Fallback to passcode after three failures'),
        (203, N'h3. Context\nAdd cards to the phone wallet using the card tokenisation service.\n\nh3. Dependencies\n* Card token service from Core Payments\n* Apple Pay entitlement',
            N'h2. Acceptance criteria\n# Customers add a card to the wallet from the app\n# Token provisioning uses the shared token service'),
        (204, N'Android tap to pay using host card emulation.',
            N'h2. Acceptance criteria\n* Contactless payments work on supported Android phones\n* Payments above the limit ask for device unlock'),
        (205, N'Web version of the onboarding journey for customers without the app.',
            N'h2. Acceptance criteria\n* Web onboarding reaches the same checks as the app\n* Journey can be resumed in the app'),
        (206, N'Screen reader support for all payment screens.',
            N'h2. Acceptance criteria\n* VoiceOver and TalkBack read every control on payment screens\n* Focus order follows the visual order'),
        (207, N'h3. Context\nApp start takes more than four seconds on mid-range phones.\n\nh3. Target\nUnder two seconds at the 90th percentile.',
            N'h2. Acceptance criteria\n* Cold start under two seconds on reference devices\n* Start-up time tracked in production monitoring'),
        (208, N'Customers view and change their payment limits in the app.',
            N'h2. Acceptance criteria\n* Limits shown per payment type\n* Limit increases need strong authentication'),
        (209, N'Fix colour contrast issues found by the accessibility audit.',
            N'h2. Acceptance criteria\n* All screens meet the 4.5 to 1 contrast ratio\n* Audit retest passed'),
        (210, N'In-app notification centre for messages and alerts.',
            N'h2. Acceptance criteria\n* Notifications kept for 90 days\n* Unread count shown on the home screen'),
        (211, N'Video call with an agent as fallback during onboarding.',
            N'h2. Acceptance criteria\n* Customers can start a video call from a failed check\n* Agents complete the identity check during the call'),
        (212, N'Support dynamic font sizes on all screens.',
            N'h2. Acceptance criteria\n* Layouts work at 200 percent font size\n* No text is cut off'),
        (213, N'Wallet payments from smart watches.',
            N'h2. Acceptance criteria\n* Watch payments use the same tokens as the phone'),
        (214, N'First release of in-app onboarding for personal accounts.',
            N'h2. Acceptance criteria\n* Personal accounts can be opened in the app\n* Conversion tracked per step'),
        (215, N'Consent banner for app analytics, as required by privacy rules.',
            N'h2. Acceptance criteria\n* Analytics only start after consent\n* Consent can be changed in settings'),
        (216, N'Customers save payees in the app for repeat payments.',
            N'h2. Acceptance criteria\n* Up to 100 saved payees per customer\n* Payee name check applies to new payees'),
        (217, NULL, NULL),
        (218, N'Upgrade the mobile CI build agents to the latest Xcode and Android SDK.', NULL),
        (219, N'Check income during onboarding with open banking data instead of uploaded payslips.',
            N'h2. Acceptance criteria\n* Customers can share account data from another bank in the app\n* Income check result returned within one minute'),
        (220, N'Customers freeze and unfreeze their cards in the app.',
            N'h2. Acceptance criteria\n* Frozen cards decline new authorisations within one minute\n* Recurring payments can stay allowed on a frozen card'),
        (221, N'Redesign the app home screen around the most used actions.',
            N'h2. Acceptance criteria\n* Home screen loads in under one second\n* Most used actions reachable with one tap'),
        (222, N'Show the last known balances when the phone is offline.',
            N'h2. Acceptance criteria\n* Last known balances shown with their timestamp'),
        (223, N'Voice control for the main payment journeys.', NULL),
        (301, N'h3. Context\nCalculator for credit risk RWA under the Basel IV standardised approach.\n\nh3. Scope\n* Exposure classes for retail and corporate\n* Risk weight tables from the regulation',
            N'h2. Acceptance criteria\n# RWA calculated for all retail and corporate exposure classes\n# Results reconcile with the reference spreadsheet'),
        (302, N'As a capital manager, I want the output floor applied to modelled RWA so that capital figures follow Basel IV.\n\nh3. Scenario\n*Given* modelled and standardised RWA for the bank\n*When* the monthly capital run completes\n*Then* the floored RWA and the floor add-on are reported',
            N'h2. Acceptance criteria\n# Output floor applied with the transitional percentage\n# Floor add-on reported per portfolio'),
        (303, N'As a model owner, I want an automated PD recalibration pipeline so that annual recalibration takes days instead of weeks.\n\nh3. Scenario\n*Given* the latest default data in the data mart\n*When* the recalibration pipeline runs\n*Then* new PD parameters and the validation pack are produced',
            N'h2. Acceptance criteria\n# Pipeline runs end to end without manual steps\n# Validation pack generated for model validation'),
        (304, N'As a finance controller, I want an RWA reconciliation between the old and new engine so that the parallel run can be signed off.\n\nh3. Scenario\n*Given* RWA results from both engines for the same date\n*When* the reconciliation runs\n*Then* differences above tolerance are listed per portfolio',
            N'h2. Acceptance criteria\n# Differences shown per portfolio and exposure class\n# Tolerance configurable by finance'),
        (305, N'Downturn adjustment for LGD estimates as required by the regulator.',
            N'h2. Acceptance criteria'),
        (306, N'h3. Context\nImplement SA-CCR for derivative counterparty credit risk.\n\nh3. Scope\n||Product||Netting sets||\n|Interest rate swaps|412|\n|FX forwards|1280|\n|Credit default swaps|37|\n\n{panel:title=Open questions}\nTreatment of legacy trades without collateral agreements.\n{panel}',
            N'h2. Acceptance criteria\n# Exposure at default calculated for all netting sets\n# Results reconcile with the regulatory template\n# Run time under two hours'),
        (307, N'Daily feed of RWA results into the data lake for reporting.',
            N'h2. Acceptance criteria\n* Feed delivered after each capital run\n* Feed schema registered in the data catalogue'),
        (308, N'Register of all risk models with owners and validation dates.',
            N'h2. Acceptance criteria\n* All production models registered\n* Validation due dates tracked'),
        (309, N'Data quality checks on exposure data before each capital run.',
            N'h2. Acceptance criteria\n* Checks block the run on critical failures\n* Results stored per run'),
        (310, N'Library of regulatory and internal stress scenarios.',
            N'h2. Acceptance criteria\n* Works in production'),
        (311, NULL, NULL),
        (312, N'As a risk analyst, I want updated credit conversion factors so that off-balance sheet exposures follow the new rules.\n\nh3. Scenario\n*Given* undrawn commitments in the exposure data\n*When* the capital run applies the new factors\n*Then* exposure at default uses the regulatory conversion factors',
            N'h2. Acceptance criteria\n# New conversion factors applied to all commitments\n# Impact report produced for finance'),
        (313, N'Tune the RWA engine to finish the monthly run within four hours.',
            N'h2. Acceptance criteria\n* Monthly run under four hours on production data\n* No change in results'),
        (314, N'Parallel run of recalibrated IRB models against the current models.',
            N'h2. Acceptance criteria\n* Three monthly parallel runs completed\n* Differences explained to the model committee'),
        (315, N'Climate risk scenarios for the regulatory exercise.', NULL),
        (316, N'Foundation of the new RWA engine: data model, calculation framework and batch runner.',
            N'h2. Acceptance criteria\n* Calculation framework processes a full portfolio\n* Batch runner scheduled in production'),
        (317, N'Data mart of historic defaults for model development.',
            N'h2. Acceptance criteria\n* Ten years of default history loaded\n* Data lineage documented'),
        (318, N'Quantitative impact study of Basel IV on capital.',
            N'h2. Acceptance criteria\n* Impact study submitted to the regulator'),
        (401, N'Migrate ten years of exposure history from the legacy risk warehouse.',
            N'h2. Acceptance criteria\n# All exposure history migrated with lineage\n# Row counts and totals reconcile per month'),
        (402, N'Map every legacy report to its replacement or retirement decision.',
            N'h2. Acceptance criteria\n* Every legacy report has an owner and a decision\n* Decisions approved by the business'),
        (403, N'Switch off legacy batch jobs once their outputs are replaced.',
            N'h2. Acceptance criteria\n* Retired jobs removed from the scheduler\n* No downstream consumer reports missing data'),
        (404, N'Reconcile migrated data against the legacy warehouse.',
            N'h2. Acceptance criteria\n* Reconciliation passes for all migrated years\n* Exceptions signed off by data owners'),
        (405, N'Put the legacy risk warehouse in read-only mode before decommissioning.',
            N'h2. Acceptance criteria\n* No writes possible to the legacy warehouse\n* Read access kept for audit'),
        (406, N'Remove user access to the legacy risk applications.',
            N'h2. Acceptance criteria\n* All user accounts removed\n* Access removal evidence stored'),
        (407, N'Inventory of all data sets in the legacy risk warehouse.',
            N'h2. Acceptance criteria\n* Inventory lists owner, size and retention for each data set'),
        (408, N'Define retention for the legacy archive after decommissioning.', NULL),
        (409, N'Move the legacy risk batch jobs to the second data centre so that the shared link is free for token vault replication.',
            N'h2. Acceptance criteria\n* Legacy batch jobs run from the second data centre\n* Shared data centre link released for token vault replication'),
        (501, N'As a data consumer, I want core banking data ingested daily so that reports use complete data.\n\nh3. Scenario\n*Given* the nightly core banking extract\n*When* the ingestion pipeline runs\n*Then* the curated tables are available by 07:00',
            N'h2. Acceptance criteria\n# Daily ingestion of accounts, balances and transactions\n# Data quality checks pass before publishing'),
        (502, N'Ingest card authorisations and settlements into the data lake.',
            N'h2. Acceptance criteria\n* Card data available the next morning\n* Card numbers masked at ingestion'),
        (503, N'Stream payment hub events into the data lake.',
            N'h2. Acceptance criteria\n* Events available in the lake within five minutes\n* Schema changes handled without data loss'),
        (504, N'Data mart for payments reporting built on the data lake.',
            N'h2. Acceptance criteria\n* Daily payments volumes and values per channel\n* Data mart documented in the catalogue'),
        (505, N'Landing zone for raw files with retention and access control.',
            N'h2. Acceptance criteria\n* Raw files kept for 30 days\n* Access restricted to pipeline accounts'),
        (506, N'Workspace where business users build their own reports.',
            N'h2. Acceptance criteria\n* Business users can build reports on curated data\n* Workspace usage monitored'),
        (507, N'Roll out the data catalogue to all data domains.',
            N'h2. Acceptance criteria\n* All curated tables documented in the catalogue\n* Owners assigned per data domain'),
        (508, N'Scorecard of payments data quality per source system.',
            N'h2. Acceptance criteria\n* Scorecard refreshed daily\n* Thresholds agreed with data owners'),
        (509, N'Monitor data lake storage and compute cost per domain.',
            N'h2. Acceptance criteria\n* Monthly cost per domain reported'),
        (510, N'Proof of concept of the data lake platform.',
            N'h2. Acceptance criteria\n* Platform choice approved'),
        (511, N'Idea: real-time analytics on streaming data.', NULL),
        (512, N'Partition the largest fact tables by month to speed up queries.',
            N'h2. Acceptance criteria\n* Query time on the largest tables halved'),
        (901, N'Replace printers in all branches. Tracked here until a Jira project exists.', NULL),
        (902, N'Refresh the payment hub operations runbook. Local item, not yet in Jira.',
            N'h2. Acceptance criteria\n* Runbook reviewed by operations'),
        (903, N'Synchronise customer contacts between the CRM and core banking.',
            N'h2. Acceptance criteria\n* Contact changes synchronised within one hour'),
        (904, N'Spending insights in the wallet, based on data lake analytics.',
            N'h2. Acceptance criteria\n* Monthly spending by category shown in the app'),
        (905, N'Shared data quality rules for risk data feeds.', NULL),
        (906, N'Dashboards of ETL job quality across all trains.', NULL);

    SET IDENTITY_INSERT dbo.Features ON;
    INSERT INTO dbo.Features (Id, JiraId, ProjectKey, IssueType, Summary, [Name], Description, AcceptanceCriteria, NavigatorId, Labels, Components, FixVersions,
        [Status], JiraUpdated, TargetStart, TargetEnd, StoryPoints, RagStatus, RagExplain, Ranking, Dependencies, ExternalDependencies,
        RequirementStatusId, TechnicalApprovalId, ConfidencePercentage, UnfundedOptionId, DateExpected, IsLinkedToTheJira, BusinessOutcomeId,
        PiId, PiObjectiveId, ModifiedBy, ModifiedAt)
    SELECT f.Id,
        CASE WHEN f.Id IN (901, 902) THEN NULL ELSE f.Pk + N'-' + CAST(f.Id AS nvarchar(10)) END,
        f.Pk, f.IssueType, f.Summary, f.FName,
        REPLACE(x.Descr, N'\n', @c_nl), REPLACE(x.Ac, N'\n', @c_nl), e.Nav,
        f.Labels, f.Components, NULL, f.St,
        CASE WHEN f.Id IN (901, 902) THEN NULL
            WHEN f.Id = 140 THEN DATEADD(minute, -1835, @NowUtc)
            WHEN f.Id = 144 THEN DATEADD(hour, 12, CAST(DATEADD(day, 9, @Pi3Start) AS datetime2))
            ELSE DATEADD(minute, -(30 + (f.Id * 211) % 15000 + CASE f.PiId WHEN 1 THEN 150 * 1440 WHEN 2 THEN 50 * 1440 ELSE 0 END), @NowUtc) END,
        NULL, NULL,
        CASE WHEN f.Sp = -1 THEN NULL ELSE f.Sp END,
        f.Rag, e.RagExplain, f.Ranking, e.Deps, ISNULL(e.Ext, 0),
        f.ReqId, f.TaId, e.Conf, e.UnfId,
        CASE WHEN e.DExp IS NOT NULL THEN DATEADD(day, e.DExp, @Pi3Start) END,
        CASE WHEN f.Id IN (901, 902) THEN NULL ELSE CAST(1 AS bit) END,
        f.BoId, f.PiId, f.PiObjId,
        CASE WHEN f.Id = 101 THEN N'CORP\cnovak'
            WHEN f.Id BETWEEN 101 AND 199 OR f.Id = 902 THEN CHOOSE(f.Id % 4 + 1, @Dev, N'CORP\amorgan', N'CORP\cnovak', N'CORP\pmo.office')
            ELSE CHOOSE(f.Id % 3 + 1, @Dev, N'CORP\cnovak', N'CORP\pmo.office') END,
        CASE WHEN f.Id = 101 THEN DATEADD(second, -940, DATEADD(hour, -26, @NowUtc))
            WHEN f.Id = 140 THEN DATEADD(minute, -1530, @NowUtc)
            WHEN f.Id = 144 THEN DATEADD(minute, 761, CAST(DATEADD(day, 9, @Pi3Start) AS datetime2))
            ELSE DATEADD(minute, -(60 + (f.Id * 397) % 20000 + CASE f.PiId WHEN 1 THEN 150 * 1440 WHEN 2 THEN 50 * 1440 ELSE 0 END), @NowUtc) END
    FROM #c_F AS f
    LEFT JOIN #c_E AS e ON e.Id = f.Id
    LEFT JOIN #c_X AS x ON x.Id = f.Id;
    SET IDENTITY_INSERT dbo.Features OFF;

    -- teams and stack estimates
    DROP TABLE IF EXISTS #c_T;
    CREATE TABLE #c_T (FeatureId int NOT NULL, TeamId int NOT NULL, IsPrimary bit NULL, ManualSp int NULL, PRIMARY KEY (FeatureId, TeamId));
    INSERT INTO #c_T (FeatureId, TeamId, IsPrimary, ManualSp) VALUES
        (101, 1, NULL, NULL), (102, 2, NULL, NULL), (103, 1, NULL, NULL), (104, 3, NULL, NULL), (105, 2, NULL, NULL),
        (106, 1, NULL, NULL), (107, 3, NULL, NULL), (108, 1, NULL, NULL), (109, 1, 1, NULL), (109, 2, NULL, 5),
        (110, 3, NULL, NULL), (111, 2, NULL, NULL), (112, 1, NULL, NULL), (113, 1, NULL, NULL), (114, 3, NULL, NULL),
        (115, 1, NULL, NULL), (116, 2, NULL, NULL), (117, 2, NULL, NULL), (118, 1, NULL, NULL), (119, 3, NULL, NULL),
        (120, 1, NULL, NULL), (121, 2, NULL, NULL), (122, 3, NULL, NULL), (123, 1, NULL, NULL), (124, 2, NULL, NULL),
        (125, 3, NULL, NULL), (126, 3, NULL, NULL), (127, 1, NULL, NULL), (128, 2, NULL, NULL), (129, 3, NULL, NULL),
        (130, 1, NULL, NULL), (131, 1, NULL, NULL), (132, 1, NULL, NULL), (133, 2, NULL, NULL), (134, 3, NULL, NULL),
        (135, 2, NULL, NULL), (136, 1, NULL, NULL),
        (140, 1, NULL, NULL), (141, 2, NULL, NULL), (142, 3, NULL, NULL), (143, 1, NULL, NULL), (144, 3, NULL, NULL),
        (145, 1, 1, NULL), (145, 2, NULL, NULL), (146, 1, NULL, NULL), (147, 3, 1, NULL), (147, 1, NULL, NULL), (147, 2, NULL, NULL),
        (148, 1, NULL, NULL), (149, 2, 1, NULL), (149, 1, NULL, NULL), (150, 2, NULL, NULL), (150, 3, NULL, NULL),
        (152, 1, NULL, NULL), (153, 2, NULL, 3), (154, 1, NULL, NULL), (155, 2, NULL, NULL), (156, 3, NULL, NULL),
        (157, 1, NULL, 5), (160, 1, NULL, NULL), (161, 1, NULL, NULL), (162, 1, NULL, NULL), (163, 1, NULL, NULL),
        (201, 4, 1, NULL), (201, 5, NULL, NULL), (202, 4, NULL, NULL), (203, 4, NULL, NULL), (204, 4, NULL, NULL),
        (205, 5, NULL, NULL), (206, 5, 1, NULL), (206, 4, NULL, NULL), (207, 4, NULL, NULL), (208, 4, 1, NULL), (208, 5, NULL, NULL),
        (209, 5, NULL, NULL), (210, 4, NULL, NULL), (211, 4, NULL, NULL), (212, 5, NULL, NULL), (213, 4, NULL, NULL),
        (214, 4, NULL, NULL), (215, 5, NULL, NULL), (216, 5, NULL, NULL), (218, 5, NULL, NULL),
        (219, 4, NULL, NULL), (220, 4, NULL, NULL), (221, 4, NULL, NULL), (222, 4, NULL, NULL), (223, 4, NULL, NULL),
        (301, 6, NULL, NULL), (302, 6, 1, NULL), (302, 7, NULL, NULL), (303, 7, NULL, NULL), (304, 6, NULL, NULL), (304, 7, NULL, NULL),
        (305, 7, NULL, NULL), (306, 6, NULL, NULL), (307, 6, 1, NULL), (307, 10, NULL, NULL), (308, 7, NULL, NULL),
        (309, 6, NULL, NULL), (310, 7, 1, NULL), (310, 10, NULL, NULL), (311, 6, NULL, NULL), (312, 7, NULL, NULL),
        (313, 6, NULL, NULL), (314, 7, 1, NULL), (314, 6, NULL, NULL), (314, 10, NULL, NULL), (315, 6, NULL, 8),
        (316, 6, NULL, NULL), (317, 7, NULL, NULL),
        (401, 8, NULL, NULL), (402, 8, NULL, NULL), (403, 8, 1, NULL), (403, 11, NULL, NULL), (404, 8, NULL, NULL),
        (405, 8, NULL, NULL), (406, 8, NULL, NULL), (407, 8, NULL, NULL), (409, 6, NULL, NULL),
        (501, 9, NULL, NULL), (502, 9, 1, NULL), (502, 10, NULL, NULL), (503, 9, NULL, NULL), (504, 9, NULL, NULL),
        (505, 10, NULL, NULL), (506, 9, 1, NULL), (506, 12, NULL, NULL), (507, 9, NULL, NULL), (508, 9, 1, NULL), (508, 10, NULL, NULL),
        (509, 10, 1, NULL), (509, 12, NULL, NULL), (510, 9, NULL, 13), (512, 9, NULL, NULL),
        (901, 12, NULL, NULL), (902, 3, NULL, 5), (905, 7, NULL, 3), (906, 9, NULL, NULL);

    DROP TABLE IF EXISTS #c_S;
    CREATE TABLE #c_S (FeatureId int NOT NULL, TeamId int NOT NULL, StackId int NOT NULL, Sp int NULL, PRIMARY KEY (FeatureId, TeamId, StackId));
    INSERT INTO #c_S (FeatureId, TeamId, StackId, Sp) VALUES
        (101, 1, 5, 5), (101, 1, 1, 3), (102, 2, 5, 8), (103, 1, 5, 13), (104, 3, 3, 8), (104, 3, 2, 3),
        (105, 2, 6, 8), (105, 2, 5, 5), (106, 1, 5, 8), (106, 1, 2, 3), (107, 3, 6, 8),
        (108, 1, 5, 13), (108, 1, 1, 5), (108, 1, 2, 3), (109, 1, 5, 8), (109, 1, 1, 5),
        (110, 3, 3, 13), (110, 3, 2, 5), (111, 2, 5, 8), (112, 1, 5, 13), (112, 1, 1, 8), (113, 1, 5, 8),
        (114, 3, 3, 8), (114, 3, 2, 5), (116, 2, 5, 5), (117, 2, 5, 8), (118, 1, 5, 13), (118, 1, 2, 5),
        (119, 3, 6, 5), (120, 1, 5, 8), (120, 1, 1, 5), (121, 2, 6, 5), (122, 3, 6, 8), (122, 3, 2, NULL),
        (123, 1, 5, 13), (123, 1, 1, 5), (123, 1, 2, 3), (124, 2, 5, 8), (124, 2, 6, 5), (125, 3, 6, 5), (125, 3, 2, 3),
        (126, 3, 3, 5), (127, 1, 5, 8), (127, 1, 1, 3), (128, 2, 6, 13), (129, 3, 6, 8), (129, 3, 3, 8),
        (130, 1, 5, 13), (130, 1, 1, 5), (131, 1, 5, 5), (132, 1, 5, 8), (132, 1, 2, 5), (133, 2, 5, 5),
        (134, 3, 3, 8), (134, 3, 2, 5), (135, 2, 6, 8), (136, 1, 5, 13), (136, 1, 1, 5), (136, 1, 2, 3),
        (140, 1, 5, 8), (140, 1, 2, 3), (141, 2, 5, 8), (141, 2, 6, 5), (142, 3, 6, 13), (143, 1, 5, 8),
        (144, 3, 6, 8), (144, 3, 3, 5), (145, 1, 5, 5), (145, 1, 1, 3), (145, 2, 6, 8),
        (146, 1, 5, 8), (146, 1, 1, 3), (146, 1, 2, 5), (147, 3, 6, 13), (147, 3, 3, 5), (147, 1, 5, 5), (147, 2, 6, 8),
        (148, 1, 5, 5), (148, 1, 1, 3), (149, 2, 5, 8), (149, 1, 5, 5), (149, 1, 1, 3),
        (150, 2, 6, 5), (150, 3, 6, 5), (150, 3, 2, 3), (152, 1, 5, 8), (152, 1, 1, 5),
        (154, 1, 5, 13), (154, 1, 1, 8), (155, 2, 6, 8), (155, 2, 5, 5), (156, 3, 3, 8), (160, 1, 5, 5),
        (161, 1, 5, 13), (161, 1, 1, 5), (161, 1, 2, 3), (162, 1, 5, 8), (162, 1, 2, 5), (163, 1, 5, 13), (163, 1, 1, 5),
        (201, 4, 7, 21), (201, 4, 8, 13), (201, 5, 7, 5), (202, 4, 7, 21), (203, 4, 7, 21), (203, 4, 8, 8), (204, 4, 7, 13),
        (205, 5, 3, 13), (205, 5, 7, 5), (206, 5, 3, 8), (206, 4, 7, 8), (207, 4, 7, 21), (207, 4, 8, 13),
        (208, 4, 7, 21), (208, 4, 8, 5), (208, 5, 7, 8), (209, 5, 3, 13), (210, 4, 7, 8), (211, 4, 7, 13), (211, 4, 8, 8),
        (212, 5, 3, 8), (212, 5, 7, 8), (213, 4, 7, 13), (214, 4, 7, 13), (214, 4, 8, 8), (215, 5, 3, 5), (216, 5, 7, 8), (218, 5, 3, 5),
        (219, 4, 7, 21), (219, 4, 8, 13), (220, 4, 7, 21), (220, 4, 8, 8), (221, 4, 7, 13), (221, 4, 8, 8), (222, 4, 7, 21), (222, 4, 8, 5),
        (223, 4, 7, 13), (223, 4, 8, 13),
        (301, 6, 9, 8), (301, 6, 1, 5), (302, 6, 9, 8), (302, 6, 1, 5), (302, 7, 9, 5), (303, 7, 9, 13), (303, 7, 2, 5),
        (304, 6, 9, 8), (304, 7, 2, 5), (305, 7, 9, 8), (306, 6, 9, 21), (306, 6, 1, 8), (307, 6, 9, 5), (307, 10, 9, 8),
        (308, 7, 2, 5), (309, 6, 1, 8), (310, 7, 9, 8), (310, 10, 9, 5), (311, 6, 9, 5), (312, 7, 9, 8), (313, 6, 9, 13),
        (314, 7, 9, 8), (314, 7, 2, 5), (314, 6, 9, 5), (314, 10, 9, 5), (316, 6, 9, 13), (316, 6, 1, 8), (317, 7, 2, 3), (317, 7, 9, 5),
        (401, 8, 10, 13), (401, 8, 1, 5), (402, 8, 10, 8), (403, 8, 1, 8), (403, 11, 4, 5), (404, 8, 10, 13),
        (405, 8, 10, 13), (405, 8, 1, 8), (406, 8, 10, 8), (407, 8, 1, 5), (409, 6, 1, 5),
        (501, 9, 11, 13), (501, 9, 1, 5), (502, 9, 11, 8), (502, 10, 11, 5), (503, 9, 11, 13), (503, 9, 4, 3), (504, 9, 1, 8),
        (505, 10, 11, 8), (505, 10, 4, 3), (506, 9, 11, 8), (506, 12, 1, 8), (507, 9, 11, 13), (508, 9, 11, 8), (508, 10, 11, 5),
        (509, 10, 4, 5), (509, 12, 4, 5), (512, 9, 1, 5), (901, 12, 4, 5), (906, 9, 11, 5);

    IF EXISTS (SELECT 1 FROM #c_S AS s WHERE NOT EXISTS (SELECT 1 FROM dbo.TeamTechnologyStacks AS x WHERE x.TeamId = s.TeamId AND x.TechnologyStackId = s.StackId))
        THROW 50030, N'30-features: a feature team stack is not a stack of that team', 1;

    INSERT INTO dbo.FeatureTeams (FeatureId, TeamId, StoryPoints, IsPrimary)
    SELECT t.FeatureId, t.TeamId,
        CASE WHEN EXISTS (SELECT 1 FROM #c_S AS s WHERE s.FeatureId = t.FeatureId AND s.TeamId = t.TeamId)
            THEN (SELECT SUM(ISNULL(s.Sp, 0)) FROM #c_S AS s WHERE s.FeatureId = t.FeatureId AND s.TeamId = t.TeamId)
            ELSE t.ManualSp END,
        t.IsPrimary
    FROM #c_T AS t;

    INSERT INTO dbo.FeatureTeamTechnologyStacks (FeatureId, TeamId, TechnologyStackId, StoryPoints)
    SELECT s.FeatureId, s.TeamId, s.StackId, s.Sp
    FROM #c_S AS s;

    -- skill split of stack points
    WITH c_w AS (
        SELECT s.FeatureId, s.TeamId, s.TechnologyStackId, tss.SkillId, ISNULL(s.StoryPoints, 0) AS Sp,
            CAST(CASE WHEN tss.Percentage > 0 THEN tss.Percentage ELSE 0 END AS decimal(9, 4)) AS W
        FROM dbo.FeatureTeamTechnologyStacks AS s
        JOIN dbo.TechnologyStackSkills AS tss ON tss.TechnologyStackId = s.TechnologyStackId
    ), c_n AS (
        SELECT w.*,
            SUM(w.W) OVER (PARTITION BY w.FeatureId, w.TeamId, w.TechnologyStackId) AS SumW,
            COUNT(*) OVER (PARTITION BY w.FeatureId, w.TeamId, w.TechnologyStackId) AS Cnt
        FROM c_w AS w
    ), c_e AS (
        SELECT n.*,
            CAST(CASE WHEN n.SumW > 0 THEN n.W ELSE 1 END AS decimal(9, 4)) AS Ew,
            CAST(CASE WHEN n.SumW > 0 THEN n.SumW ELSE n.Cnt END AS decimal(9, 4)) AS Es
        FROM c_n AS n
    ), c_f AS (
        SELECT e.*, CAST(e.Sp * e.Ew / e.Es AS decimal(18, 8)) AS Tg, FLOOR(CAST(e.Sp * e.Ew / e.Es AS decimal(18, 8))) AS Fl
        FROM c_e AS e
    ), c_r AS (
        SELECT f.*,
            f.Sp - SUM(f.Fl) OVER (PARTITION BY f.FeatureId, f.TeamId, f.TechnologyStackId) AS Lo,
            ROW_NUMBER() OVER (PARTITION BY f.FeatureId, f.TeamId, f.TechnologyStackId
                ORDER BY CASE WHEN f.Ew > 0 THEN 0 ELSE 1 END, f.Tg - f.Fl DESC, f.Ew DESC, f.SkillId) AS Rk
        FROM c_f AS f
    )
    INSERT INTO dbo.FeatureTeamStackSkills (FeatureId, TeamId, TechnologyStackId, SkillId, [Value])
    SELECT r.FeatureId, r.TeamId, r.TechnologyStackId, r.SkillId,
        CAST(r.Fl + CASE WHEN r.Sp > 0 AND r.Ew > 0 AND r.Rk <= r.Lo THEN 1 ELSE 0 END AS decimal(9, 2))
    FROM c_r AS r;

    INSERT INTO dbo.FeatureSkills (FeatureId, SkillId, [Value]) VALUES
        (101, 1, 5.00), (101, 2, 3.00),
        (203, 13, 8.00), (203, 14, 8.00), (203, 10, 3.00),
        (302, 6, 10.00), (302, 11, 8.00), (302, 2, 0.00), (302, 7, 0.00),
        (306, 6, 15.00), (306, 11, 12.00),
        (501, 12, 10.00), (501, 6, 5.00), (501, 15, 2.00);

    -- sprint plans
    DROP TABLE IF EXISTS #c_P;
    CREATE TABLE #c_P (FeatureId int NOT NULL, TeamId int NOT NULL, PiId int NOT NULL, SFrom int NOT NULL, STo int NOT NULL);
    INSERT INTO #c_P (FeatureId, TeamId, PiId, SFrom, STo) VALUES
        (101, 1, 3, 1, 1), (102, 2, 3, 3, 3), (103, 1, 3, 4, 4), (104, 3, 3, 2, 2), (105, 2, 3, 2, 2), (106, 1, 3, 2, 2),
        (108, 1, 3, 4, 4), (109, 1, 3, 2, 2), (110, 3, 3, 5, 5), (111, 2, 3, 1, 1), (112, 1, 3, 3, 3), (113, 1, 3, 1, 1),
        (114, 3, 3, 3, 3), (116, 2, 3, 1, 1), (117, 2, 3, 2, 2), (118, 1, 3, 4, 4), (119, 3, 3, 3, 3), (120, 1, 3, 2, 2),
        (121, 2, 3, 2, 2), (122, 3, 3, 4, 4), (123, 1, 3, 2, 2), (124, 2, 3, 3, 3), (125, 3, 3, 1, 1), (126, 3, 3, 2, 2),
        (127, 1, 3, 3, 3), (128, 2, 3, 4, 4), (129, 3, 3, 3, 3), (130, 1, 3, 3, 3), (131, 1, 3, 1, 1), (132, 1, 3, 3, 3),
        (133, 2, 3, 2, 2), (134, 3, 3, 3, 3), (135, 2, 3, 1, 1), (136, 1, 3, 2, 2),
        (140, 1, 3, 3, 4), (141, 2, 4, 1, 2), (142, 3, 2, 4, 5), (143, 1, 3, 4, 5), (144, 3, 2, 5, 5), (144, 3, 3, 1, 1),
        (145, 1, 3, 3, 3), (145, 2, 3, 3, 4), (147, 3, 4, 2, 4), (147, 1, 4, 3, 3), (147, 2, 4, 2, 3), (148, 1, 4, 2, 3),
        (149, 2, 4, 3, 4), (149, 1, 4, 3, 3), (150, 2, 3, 4, 4), (150, 3, 3, 4, 5), (152, 1, 4, 1, 2),
        (154, 1, 2, 2, 3), (155, 2, 2, 4, 5), (156, 3, 2, 2, 2), (161, 1, 4, 3, 5),
        (201, 4, 3, 1, 3), (201, 5, 3, 3, 3), (202, 4, 3, 2, 3), (203, 4, 3, 2, 4), (204, 4, 3, 1, 2), (205, 5, 3, 2, 3),
        (206, 5, 3, 1, 2), (206, 4, 3, 2, 2), (207, 4, 3, 3, 5), (208, 4, 3, 4, 5), (208, 5, 3, 5, 5), (209, 5, 3, 4, 4),
        (210, 4, 3, 1, 1), (211, 4, 4, 1, 2), (212, 5, 4, 2, 2), (214, 4, 2, 4, 5), (215, 5, 2, 3, 3), (216, 5, 3, 5, 5),
        (218, 5, 3, 1, 1), (219, 4, 4, 2, 4), (220, 4, 4, 3, 5), (221, 4, 4, 4, 5), (222, 4, 4, 2, 3), (223, 4, 4, 5, 5),
        (301, 6, 3, 1, 1), (302, 6, 3, 4, 5), (302, 7, 3, 5, 5), (303, 7, 3, 3, 5), (304, 6, 3, 4, 5), (304, 7, 3, 5, 5),
        (305, 7, 3, 5, 5), (306, 6, 3, 3, 5), (307, 6, 3, 2, 4), (307, 10, 3, 2, 4), (308, 7, 3, 1, 1), (309, 6, 3, 1, 2),
        (310, 7, 3, 4, 5), (310, 10, 3, 4, 5), (312, 7, 3, 4, 5), (313, 6, 4, 1, 2), (314, 7, 4, 2, 3), (314, 6, 4, 3, 3),
        (314, 10, 4, 2, 3), (316, 6, 2, 3, 4), (317, 7, 2, 5, 5),
        (401, 8, 3, 1, 3), (402, 8, 3, 3, 4), (403, 8, 3, 4, 5), (404, 8, 3, 2, 2), (405, 8, 4, 1, 3), (406, 8, 4, 3, 4),
        (407, 8, 3, 1, 1), (409, 6, 2, 5, 5),
        (501, 9, 3, 1, 2), (502, 9, 3, 2, 3), (502, 10, 3, 3, 3), (503, 9, 3, 3, 4), (504, 9, 3, 2, 2), (505, 10, 3, 1, 2),
        (506, 9, 3, 2, 3), (506, 12, 3, 2, 3), (507, 9, 4, 1, 2), (508, 9, 4, 2, 3), (508, 10, 4, 2, 3), (509, 10, 4, 1, 1),
        (509, 12, 4, 1, 1), (512, 9, 3, 5, 5),
        (901, 12, 3, 4, 4), (905, 7, 3, 3, 3);

    INSERT INTO dbo.FeatureTeamSprints (FeatureId, TeamId, SprintId)
    SELECT p.FeatureId, p.TeamId, s.Id
    FROM #c_P AS p
    JOIN (
        SELECT sp.Id, sp.TeamId, sp.PiId, ROW_NUMBER() OVER (PARTITION BY sp.TeamId, sp.PiId ORDER BY sp.StartDate) AS S
        FROM dbo.Sprints AS sp
        WHERE sp.PiId IS NOT NULL
    ) AS s ON s.TeamId = p.TeamId AND s.PiId = p.PiId AND s.S BETWEEN p.SFrom AND p.STo;
    SET @c_rows = @@ROWCOUNT;
    IF @c_rows <> (SELECT SUM(STo - SFrom + 1) FROM #c_P)
        THROW 50031, N'30-features: a planned team sprint does not exist', 1;

    -- derived feature values
    UPDATE f SET StoryPoints = t.TeamSp
    FROM dbo.Features AS f
    JOIN #c_F AS c ON c.Id = f.Id AND c.Sp = -1
    JOIN (SELECT FeatureId, SUM(ISNULL(StoryPoints, 0)) AS TeamSp FROM dbo.FeatureTeams GROUP BY FeatureId) AS t ON t.FeatureId = f.Id;

    UPDATE f SET TargetStart = x.S, TargetEnd = x.E
    FROM dbo.Features AS f
    JOIN (
        SELECT fts.FeatureId, MIN(s.StartDate) AS S, MAX(ISNULL(s.UatEnd, s.EndDate)) AS E
        FROM dbo.FeatureTeamSprints AS fts
        JOIN dbo.Sprints AS s ON s.Id = fts.SprintId
        GROUP BY fts.FeatureId
    ) AS x ON x.FeatureId = f.Id;

    UPDATE f SET TargetStart = p.StartDate, TargetEnd = p.EndDate
    FROM dbo.Features AS f
    JOIN #Pi AS p ON p.PiId = f.PiId
    WHERE f.Id IN (157, 318, 510);

    UPDATE f SET FixVersions = CASE c.Fix WHEN 'N' THEN NULL WHEN 'X' THEN CONCAT_WS(N',', v.Fx, N'Scheme Certification') ELSE v.Fx END
    FROM dbo.Features AS f
    JOIN #c_F AS c ON c.Id = f.Id
    OUTER APPLY (
        SELECT STRING_AGG(cps.FixVersion, N',') WITHIN GROUP (ORDER BY cps.StartDate) AS Fx
        FROM dbo.CapitalProjectSprints AS cps
        WHERE cps.CapitalProjectId = CASE WHEN f.Id BETWEEN 101 AND 599 THEN f.Id / 100 WHEN f.Id = 902 THEN 1 END
            AND cps.FixVersion IS NOT NULL
            AND cps.StartDate >= f.TargetStart
            AND ISNULL(cps.UatEnd, cps.EndDate) <= f.TargetEnd
    ) AS v;

    -- capacity board orders
    SET IDENTITY_INSERT dbo.TeamCapacityFeatureOrders ON;
    INSERT INTO dbo.TeamCapacityFeatureOrders (Id, TeamId, PiId, FeatureId, SortOrder, IsIncluded, IsManuallyAdded, AddedFeaturePiId) VALUES
        (1, 1, 3, 113, 1, 1, 0, NULL), (2, 1, 3, 101, 2, 1, 0, NULL), (3, 1, 3, 131, 3, 1, 0, NULL), (4, 1, 3, 106, 4, 1, 0, NULL),
        (5, 1, 3, 120, 5, 1, 0, NULL), (6, 1, 3, 123, 6, 1, 0, NULL), (7, 1, 3, 136, 7, 1, 0, NULL), (8, 1, 3, 109, 8, 1, 0, NULL),
        (9, 1, 3, 112, 9, 1, 0, NULL), (10, 1, 3, 132, 10, 1, 0, NULL), (11, 1, 3, 127, 11, 1, 0, NULL), (12, 1, 3, 130, 12, 1, 0, NULL),
        (13, 1, 3, 103, 13, 1, 0, NULL), (14, 1, 3, 118, 14, 1, 0, NULL), (15, 1, 3, 108, 15, 1, 0, NULL), (16, 1, 3, 143, 16, 1, 0, NULL),
        (17, 1, 3, 145, 17, 1, 0, NULL), (18, 1, 3, 160, 18, 1, 1, 5), (19, 1, 3, 148, 19, 1, 1, 5), (20, 1, 3, 115, 20, 0, 0, NULL),
        (21, 1, 3, 146, 21, 0, 0, NULL),
        (22, 2, 3, 116, 1, 1, 0, NULL), (23, 2, 3, 135, 2, 1, 0, NULL), (24, 2, 3, 105, 3, 1, 0, NULL), (25, 2, 3, 102, 4, 1, 0, NULL),
        (26, 2, 3, 124, 5, 1, 0, NULL), (27, 2, 3, 128, 6, 1, 0, NULL), (28, 2, 3, 133, 7, 1, 0, NULL), (29, 2, 3, 109, 8, 1, 0, NULL),
        (30, 2, 3, 150, 9, 1, 0, NULL), (31, 2, 3, 117, 10, 1, 0, NULL), (32, 2, 3, 121, 11, 1, 0, NULL), (33, 2, 3, 111, 12, 0, 0, NULL),
        (34, 2, 3, 145, 13, 0, 0, NULL),
        (35, 1, 4, 147, 1, 1, 0, NULL), (36, 1, 4, 152, 2, 1, 0, NULL), (37, 1, 4, 149, 3, 1, 0, NULL), (38, 1, 4, 148, 4, 1, 0, NULL),
        (39, 1, 4, 161, 5, 1, 0, NULL), (40, 1, 4, 162, 6, 0, 0, NULL), (41, 1, 4, 163, 7, 0, 0, NULL);
    SET IDENTITY_INSERT dbo.TeamCapacityFeatureOrders OFF;

    -- ART prioritization: Core Payments has a general Portfolio Epic order (EPIC-19 has no Core Payments features,
    -- EPIC-17 is below the line, the "No Portfolio Epic" bucket stays new) that the current PI follows, and its own
    -- order for the next PI (EPIC-17 ahead of EPIC-12, which is below the line). Data Platform has a general
    -- Business Outcome order with DATA-9214 below the line; RISK-9207 stays new.
    INSERT INTO dbo.ArtPrioritizationOrders (CapitalProjectId, PiId, Level, PortfolioEpicId, BusinessOutcomeId, SortOrder, IsIncluded) VALUES
        (1, NULL, 1, 1, NULL, 1, 1), (1, NULL, 1, 9, NULL, 2, 1), (1, NULL, 1, 2, NULL, 3, 1), (1, NULL, 1, 7, NULL, 4, 0),
        (1, 4, 1, 1, NULL, 1, 1), (1, 4, 1, 7, NULL, 2, 1), (1, 4, 1, 2, NULL, 3, 0),
        (5, NULL, 2, NULL, 10, 1, 1), (5, NULL, 2, NULL, 9, 2, 1), (5, NULL, 2, NULL, 6, 3, 1), (5, NULL, 2, NULL, 14, 4, 0);

    -- comments
    SET IDENTITY_INSERT dbo.FeatureComments ON;
    INSERT INTO dbo.FeatureComments (Id, FeatureId, [Text], Author, CreatedAt, IsDone, DoneBy, DoneAt)
    SELECT v.Id, v.FeatureId, REPLACE(v.Txt, N'\n', @c_nl), v.Author, DATEADD(minute, -v.Ago, @NowUtc), v.IsDone, v.DoneBy,
        CASE WHEN v.IsDone = 1 THEN DATEADD(minute, -v.DoneAgo, @NowUtc) END
    FROM (VALUES
        (1, 101, N'CORP\cnovak', 40000, 0, NULL, NULL, N'Started the API skeleton. Contract agreed with the channels team.'),
        (2, 101, @Dev, 30000, 0, NULL, NULL, N'Scheme simulator is flaky in SIT.\nRaised a ticket with the environments team.'),
        (3, 101, N'CORP\amorgan', 20000, 1, @Dev, 15000, N'Can we demo this in the system demo on Thursday?'),
        (4, 101, N'CORP\contractor.x', 5000, 0, NULL, NULL, N'Load test results are on the team wiki page.\n95th percentile: 4.2 s, target is 10 s.'),
        (5, 103, N'CORP\bcarter', 8200, 1, N'CORP\bcarter', 7900, N'Certification slot confirmed for sprint 4.'),
        (6, 103, N'CORP\pmo.office', 35000, 0, NULL, NULL, N'Please confirm the HSM key ceremony date.'),
        (7, 103, @Dev, 9000, 0, NULL, NULL, N'Key ceremony booked. Two custodians confirmed.'),
        (8, 108, N'CORP\amorgan', 26000, 0, NULL, NULL, N'Retail risk wants a separate limit for new customers in their first 30 days.'),
        (9, 108, N'CORP\cnovak', 12000, 0, NULL, NULL, N'Jira estimate (20) is still lower than the team estimate (21).'),
        (10, 113, @Dev, 55000, 0, NULL, NULL, N'Blocked: scheme test environment is down until further notice.'),
        (11, 113, N'CORP\cnovak', 43000, 1, N'CORP\amorgan', 40000, N'Escalated to the scheme account manager.'),
        (12, 113, N'CORP\amorgan', 30000, 0, NULL, NULL, N'Workaround: use the recorded scheme responses for failover tests.\nNot accepted by QA for sign-off.'),
        (13, 113, N'CORP\contractor.x', 14000, 0, NULL, NULL, N'Secondary link certificate expires next month - renewal requested.'),
        (14, 113, @Dev, 2000, 0, NULL, NULL, N'Still blocked. Raised in the ART sync as a top risk.'),
        (15, 118, N'CORP\bcarter', 22000, 0, NULL, NULL, N'Two billers are ready for the pilot.'),
        (16, 118, N'CORP\cnovak', 7000, 0, NULL, NULL, N'Team estimate went down to 18 after splitting out the expiry handling.\nJira still shows 21.'),
        (17, 120, N'CORP\amorgan', 38000, 1, N'CORP\amorgan', 36000, N'File format agreed with the corporate channel.'),
        (18, 120, N'CORP\pmo.office', 25000, 1, @Dev, 24000, N'Please link the approval workflow story.'),
        (19, 123, N'CORP\cnovak', 18000, 0, NULL, NULL, N'Autoscaling tested in pre-production with three times the normal load.'),
        (20, 123, N'CORP\pmo.office', 6000, 1, N'CORP\cnovak', 4000, N'Can Risk Analytics reuse the scaling policy?'),
        (21, 130, @Dev, 16000, 0, NULL, NULL, N'Readiness checklist draft shared with operations.'),
        (22, 130, N'CORP\pmo.office', 3000, 0, NULL, NULL, N'Go-live decision meeting moved to sprint 4 planning.'),
        (23, 141, N'CORP\bcarter', 11000, 0, NULL, NULL, N'Merchant API design review next week.'),
        (24, 141, @Dev, 1500, 0, NULL, NULL, N'Depends on PAY-118 request to pay messages.'),
        (25, 201, N'CORP\bcarter', 52000, 1, N'CORP\bcarter', 50000, N'ID vendor contract signed.'),
        (26, 201, @Dev, 21000, 0, NULL, NULL, N'Android liveness check fails on two low-end devices.\nVendor fix expected next sprint.'),
        (27, 201, N'CORP\pmo.office', 8000, 0, NULL, NULL, N'Please update the RAID log with the vendor dependency.'),
        (28, 203, N'CORP\contractor.x', 33000, 0, NULL, NULL, N'Apple entitlement request submitted.'),
        (29, 203, N'CORP\bcarter', 10000, 0, NULL, NULL, N'Shared token service from Core Payments is late (PAY-103).'),
        (30, 302, N'CORP\ehorak', 28000, 0, NULL, NULL, N'Transitional floor percentage confirmed with finance.'),
        (31, 302, @Dev, 13000, 1, N'CORP\ehorak', 12000, N'Need the latest standardised RWA run for testing.'),
        (32, 306, N'CORP\ehorak', 46000, 0, NULL, NULL, N'Trade repository extract misses CDS trades.'),
        (33, 306, @Dev, 24000, 0, NULL, NULL, N'Story points: team says 29, Jira says 34.\nTo be aligned at the next refinement.'),
        (34, 306, N'CORP\pmo.office', 4500, 1, N'CORP\ehorak', 3000, N'Is this still on track for the regulatory deadline?'),
        (35, 401, N'CORP\pmo.office', 34000, 0, NULL, NULL, N'Migration window agreed with the warehouse support team.'),
        (36, 401, N'CORP\cnovak', 9500, 1, N'CORP\cnovak', 9000, N'First three years migrated and reconciled.'),
        (37, 503, @Dev, 19000, 0, NULL, NULL, N'Event schema v2 published by Core Payments.'),
        (38, 503, N'CORP\contractor.x', 5500, 0, NULL, NULL, N'Lake ingestion for v2 events needs a new topic.'),
        (39, 904, N'CORP\pmo.office', 27000, 0, NULL, NULL, N'Which ART owns this? Key PAY with label data and component iOS matches two trains.'),
        (40, 147, N'CORP\bcarter', 2500, 0, NULL, NULL, N'Funding only covers the card gateway part for now.')
    ) AS v (Id, FeatureId, Author, Ago, IsDone, DoneBy, DoneAgo, Txt);
    SET IDENTITY_INSERT dbo.FeatureComments OFF;


    -- 40-jira: issue links, label caches, sync keys, sync history, sync settings
    SET @Section = N'40-jira';
    PRINT N'Jira: issue links, label caches, sync keys, sync history, sync settings';

    DECLARE @d_nl nchar(2) = NCHAR(13) + NCHAR(10);
    DECLARE @d_b nvarchar(4) = N'  - ';
    DECLARE @d_dash nvarchar(3) = N' ' + NCHAR(8212) + N' ';
    DECLARE @d_tz int = DATEPART(tzoffset, SYSDATETIMEOFFSET());
    DECLARE @d_jtz nvarchar(100) = (SELECT TOP (1) z.[name] FROM dbo.JiraSyncSettings AS s JOIN sys.time_zone_info AS z ON z.[name] = s.JiraTimeZoneId ORDER BY s.Id);
    DECLARE @d_floor datetime2 = DATEFROMPARTS(YEAR(@Today) - 1, 1, 1);
    DECLARE @d_h1 datetime2 = DATEADD(second, -274325, @NowUtc);
    DECLARE @d_h2 datetime2 = DATEADD(second, -189100, @NowUtc);
    DECLARE @d_h3 datetime2 = DATEADD(second, -100683, @NowUtc);
    DECLARE @d_h4 datetime2 = DATEADD(second, -11040, @NowUtc);
    DECLARE @d_since2 nchar(16) = CONVERT(nchar(16), CASE WHEN @d_jtz IS NULL THEN DATEADD(minute, @d_tz, DATEADD(second, 1, @d_h2))
        ELSE CONVERT(datetime2, (DATEADD(second, 1, @d_h2) AT TIME ZONE N'UTC') AT TIME ZONE @d_jtz) END, 120);
    DECLARE @d_since3 nchar(16) = CONVERT(nchar(16), CASE WHEN @d_jtz IS NULL THEN DATEADD(minute, @d_tz, DATEADD(second, 1, @d_h3))
        ELSE CONVERT(datetime2, (DATEADD(second, 1, @d_h3) AT TIME ZONE N'UTC') AT TIME ZONE @d_jtz) END, 120);
    DECLARE @d_q nvarchar(40) = N' ORDER BY created ASC, key ASC';

    -- issue links
    SET IDENTITY_INSERT dbo.IssueLinks ON;
    INSERT INTO dbo.IssueLinks (Id, JiraLinkId, TypeName, OutwardLabel, InwardLabel, FromKey, ToKey)
    SELECT l.Id, CAST(900000 + l.Id AS nvarchar(20)), t.TypeName, t.OutwardLabel, t.InwardLabel, l.FromKey, l.ToKey
    FROM (VALUES
        (1, N'B', N'PAY-101', N'PAY-102'),
        (2, N'B', N'PAY-103', N'PAY-104'),
        (3, N'B', N'PAY-105', N'PAY-106'),
        (4, N'B', N'PAY-107', N'PAY-108'),
        (5, N'B', N'PAY-109', N'PAY-110'),
        (6, N'B', N'PAY-111', N'PAY-112'),
        (7, N'B', N'PAY-113', N'PAY-114'),
        (8, N'B', N'PAY-116', N'PAY-115'),
        (9, N'B', N'PAY-118', N'PAY-117'),
        (10, N'B', N'PAY-119', N'PAY-120'),
        (11, N'B', N'PAY-121', N'PAY-122'),
        (12, N'B', N'PAY-123', N'PAY-124'),
        (13, N'B', N'PAY-124', N'PAY-123'),
        (14, N'R', N'PAY-125', N'PAY-126'),
        (15, N'B', N'RISK-301', N'PAY-127'),
        (16, N'B', N'PAY-141', N'PAY-128'),
        (17, N'B', N'PAY-142', N'PAY-129'),
        (18, N'R', N'PAY-9201', N'PAY-130'),
        (19, N'B', N'EPIC-11', N'PAY-130'),
        (20, N'B', N'EXT-501', N'PAY-130'),
        (21, N'B', N'PAY-131', N'PAY-132'),
        (22, N'B', N'PAY-133', N'PAY-133'),
        (23, N'D', N'PAY-134', N'PAY-135'),
        (24, N'R', N'PAY-136', N'PAY-101'),
        (25, N'R', N'PAY-136', N'PAY-102'),
        (26, N'R', N'PAY-136', N'PAY-103'),
        (27, N'R', N'PAY-136', N'PAY-104'),
        (28, N'R', N'PAY-136', N'PAY-105'),
        (29, N'R', N'PAY-136', N'PAY-106'),
        (30, N'R', N'PAY-136', N'PAY-107'),
        (31, N'R', N'PAY-136', N'PAY-108'),
        (32, N'R', N'PAY-136', N'PAY-109'),
        (33, N'N', N'PAY-136', N'PAY-110'),
        (34, N'U', N'PAY-136', N'PAY-111'),
        (35, N'B', N'PAY-204', N'MOB-207'),
        (36, N'B', N'MOB-209', N'PAY-208'),
        (37, N'R', N'MOB-202', N'MOB-205'),
        (38, N'B', N'PAY-104', N'PAY-203'),
        (39, N'B', N'MOB-206', N'MOB-212'),
        (40, N'R', N'MOB-9204', N'MOB-201'),
        (41, N'B', N'RISK-303', N'RISK-306'),
        (42, N'B', N'RISK-309', N'RISK-302'),
        (43, N'B', N'RISK-301', N'RISK-307'),
        (44, N'D', N'RISK-305', N'RISK-303'),
        (45, N'B', N'RISK-404', N'RISK-306'),
        (46, N'B', N'RISK-402', N'RISK-9006'),
        (47, N'B', N'DATA-501', N'PAY-503'),
        (48, N'B', N'DATA-505', N'DATA-506'),
        (49, N'B', N'DATA-502', N'RISK-302'),
        (50, N'B', N'PAY-160', N'PAY-140'),
        (51, N'B', N'RISK-409', N'PAY-142')
    ) AS l (Id, T, FromKey, ToKey)
    JOIN (VALUES
        (N'B', N'Blocks', N'blocks', N'is blocked by'),
        (N'R', N'Relates', N'relates to', N'relates to'),
        (N'D', N'Dependency', N'depends on', N'is depended on by'),
        (N'N', N'Relates', NULL, NULL),
        (N'U', N'', N'relates to', N'relates to')
    ) AS t (T, TypeName, OutwardLabel, InwardLabel) ON t.T = l.T;
    SET IDENTITY_INSERT dbo.IssueLinks OFF;

    -- label caches
    DROP TABLE IF EXISTS #d_Label;
    CREATE TABLE #d_Label (CacheKey nvarchar(10) NOT NULL, Label nvarchar(255) NOT NULL);
    INSERT INTO #d_Label (CacheKey, Label)
    SELECT src.K, x.Label
    FROM (
        SELECT COALESCE(NULLIF(ProjectKey, N''), LEFT(JiraId, NULLIF(CHARINDEX(N'-', JiraId), 0) - 1)) AS K, Labels FROM dbo.Features
        UNION ALL
        SELECT COALESCE(NULLIF(ProjectKey, N''), LEFT(JiraId, NULLIF(CHARINDEX(N'-', JiraId), 0) - 1)), Labels FROM dbo.BusinessOutcomes
        UNION ALL
        SELECT COALESCE(NULLIF(ProjectKey, N''), LEFT(JiraId, NULLIF(CHARINDEX(N'-', JiraId), 0) - 1)), Labels FROM dbo.PortfolioEpics
        UNION ALL
        SELECT COALESCE(NULLIF(ProjectKey, N''), LEFT(JiraId, NULLIF(CHARINDEX(N'-', JiraId), 0) - 1)), Labels FROM dbo.StrategicObjectives
        UNION ALL
        SELECT k.K, p.FeatureLabels FROM dbo.Pis AS p CROSS JOIN (VALUES (N'PAY'), (N'MOB'), (N'RISK'), (N'CRM')) AS k (K)
        UNION ALL
        SELECT JiraKey, Labels FROM dbo.CapitalProjectJiraKeys
        UNION ALL
        SELECT e.K, e.Labels FROM (VALUES
            (N'PAY', N'sepa,pci-dss,regulatory,tech-debt'),
            (N'MOB', N'app-store,push,ux,tech-debt'),
            (N'RISK', N'ifrs9,model-validation,regulatory,tech-debt'),
            (N'CRM', N'salesforce,lead-scoring,campaigns,tech-debt')
        ) AS e (K, Labels)
    ) AS src
    CROSS APPLY STRING_SPLIT(src.Labels, N',') AS s
    CROSS APPLY (SELECT LTRIM(RTRIM(CASE WHEN LEFT(LTRIM(s.value), 1) = N'!' THEN STUFF(LTRIM(s.value), 1, 1, N'') ELSE s.value END)) AS Label) AS x
    WHERE src.K IN (N'PAY', N'MOB', N'RISK', N'CRM')
        AND x.Label <> N''
        AND x.Label <> N'(empty)';

    SET IDENTITY_INSERT dbo.JiraLabelCaches ON;
    INSERT INTO dbo.JiraLabelCaches (Id, CacheKey, LabelsJson, UpdatedAt, LastAttemptAt, LastError)
    SELECT
        CASE g.CacheKey WHEN N'PAY' THEN 1 WHEN N'MOB' THEN 2 WHEN N'RISK' THEN 3 ELSE 4 END,
        g.CacheKey,
        N'[' + STRING_AGG(CAST(N'"' + STRING_ESCAPE(g.Label, 'json') + N'"' AS nvarchar(max)), N',') WITHIN GROUP (ORDER BY g.Label) + N']',
        CASE g.CacheKey
            WHEN N'PAY' THEN DATEADD(second, -12010, @NowUtc)
            WHEN N'MOB' THEN DATEADD(second, -11950, @NowUtc)
            WHEN N'RISK' THEN DATEADD(hour, -48, @NowUtc)
            ELSE DATEADD(second, -97230, @NowUtc)
        END,
        CASE g.CacheKey
            WHEN N'PAY' THEN DATEADD(second, -12010, @NowUtc)
            WHEN N'MOB' THEN DATEADD(second, -11950, @NowUtc)
            WHEN N'RISK' THEN DATEADD(hour, -48, @NowUtc)
            ELSE DATEADD(second, -11890, @NowUtc)
        END,
        CASE WHEN g.CacheKey = N'CRM' THEN N'Jira is not responding.' END
    FROM (SELECT CacheKey, MIN(Label) AS Label FROM #d_Label GROUP BY CacheKey, Label) AS g
    GROUP BY g.CacheKey;
    SET IDENTITY_INSERT dbo.JiraLabelCaches OFF;

    -- sync keys
    SET IDENTITY_INSERT dbo.JiraSyncKeys ON;
    INSERT INTO dbo.JiraSyncKeys (Id, JiraKey, CreateNotExisted, UpdateExisted, IssueTypesCsv, LabelsCsv, StatusesCsv, ExcludeCreateStatusesCsv, DateFilterMode, SinceFloorUtc, LastSyncedWatermarkUtc) VALUES
        (1, N'PAY', 1, 1, N'Feature,Business Outcome,Portfolio Epic,Strategic Objective', NULL, NULL, N'Closed,Rejected', 0, @d_floor, DATEADD(second, 1, @d_h4)),
        (2, N'RISK', 0, 1, N'Feature', N'basel', NULL, NULL, 1, NULL, NULL),
        (3, N'MOB', 0, 0, N'Feature,Business Outcome', NULL, NULL, NULL, 0, NULL, DATEADD(second, -781200, @NowUtc)),
        (4, N'CRM', 0, 1, N'Feature', NULL, N'To Do,In Progress', NULL, 0, NULL, DATEADD(second, 1, @d_h4));
    SET IDENTITY_INSERT dbo.JiraSyncKeys OFF;

    -- sync history
    DECLARE @d_created2 nvarchar(200) = N'PAY-143, PAY-152, PAY-503';
    DECLARE @d_payUpd2 nvarchar(max), @d_payUpd2Count int, @d_riskUpd2 nvarchar(max), @d_riskUpd2Count int;

    SELECT @d_payUpd2 = STRING_AGG(CAST(u.JiraId AS nvarchar(max)), N', ') WITHIN GROUP (ORDER BY u.Pass, u.Num, u.JiraId),
        @d_payUpd2Count = COUNT(*)
    FROM (
        SELECT 2 AS Pass, JiraId, TRY_CAST(SUBSTRING(JiraId, CHARINDEX(N'-', JiraId) + 1, 20) AS int) AS Num FROM dbo.PortfolioEpics WHERE ProjectKey = N'PAY'
        UNION ALL
        SELECT 3, JiraId, TRY_CAST(SUBSTRING(JiraId, CHARINDEX(N'-', JiraId) + 1, 20) AS int) FROM dbo.BusinessOutcomes WHERE ProjectKey = N'PAY'
        UNION ALL
        SELECT 4, JiraId, TRY_CAST(SUBSTRING(JiraId, CHARINDEX(N'-', JiraId) + 1, 20) AS int) FROM dbo.Features
        WHERE ProjectKey = N'PAY' AND JiraId IS NOT NULL AND JiraId NOT IN (N'PAY-143', N'PAY-152', N'PAY-503', N'PAY-140', N'PAY-213', N'PAY-508')
    ) AS u;

    SELECT @d_riskUpd2 = STRING_AGG(CAST(f.JiraId AS nvarchar(max)), N', ') WITHIN GROUP (ORDER BY f.Id),
        @d_riskUpd2Count = COUNT(*)
    FROM dbo.Features AS f
    WHERE f.ProjectKey = N'RISK' AND EXISTS (SELECT 1 FROM STRING_SPLIT(f.Labels, N',') AS s WHERE s.value = N'basel');

    SET IDENTITY_INSERT dbo.JiraSyncHistory ON;
    INSERT INTO dbo.JiraSyncHistory (Id, StartedAt, FinishedAt, [Status], TriggeredBy, ProjectsProcessed, IssuesFetched, Created, Updated, Skipped, Failed, [Message]) VALUES
        (1, @d_h1, DATEADD(millisecond, 640, @d_h1), N'Failed', @Dev, 0, 0, 0, 0, 0, 0,
            N'Jira service account ''__jira-sync-service__'' is not connected. Connect it on the Jira Sync admin page.'),
        (2, @d_h2, DATEADD(second, 192, @d_h2), N'Success', @Dev, 2, @d_payUpd2Count + 5 + @d_riskUpd2Count, 3, @d_payUpd2Count + @d_riskUpd2Count, 0, 0,
            CONCAT(N'2 key(s) processed, ', @d_payUpd2Count + 5 + @d_riskUpd2Count, N' fetched, 3 created, ', @d_payUpd2Count + @d_riskUpd2Count, N' updated, 0 issue(s) skipped, 0 key(s) failed.',
                @d_nl, @d_nl, N'Keys (2):',
                @d_nl, @d_b, N'PAY: fetched ', @d_payUpd2Count + 5, N', created 3, updated ', @d_payUpd2Count, N', 2 not created (status); new Features by ART: Data Platform 1, Core Payments 2',
                @d_nl, @d_b, N'RISK: fetched ', @d_riskUpd2Count, N', created 0, updated ', @d_riskUpd2Count,
                @d_nl, @d_nl, N'Jira requests (5):',
                @d_nl, @d_b, N'PAY: project = "PAY" AND issuetype in ("Strategic Objective") AND updated >= "', CONVERT(nchar(16), @d_floor, 120), N'"', @d_q,
                @d_nl, @d_b, N'PAY: project = "PAY" AND issuetype in ("Portfolio Epic") AND updated >= "', CONVERT(nchar(16), @d_floor, 120), N'"', @d_q,
                @d_nl, @d_b, N'PAY: project = "PAY" AND issuetype in ("Business Outcome") AND updated >= "', CONVERT(nchar(16), @d_floor, 120), N'"', @d_q,
                @d_nl, @d_b, N'PAY: project = "PAY" AND issuetype in ("Feature") AND updated >= "', CONVERT(nchar(16), @d_floor, 120), N'"', @d_q,
                @d_nl, @d_b, N'RISK: project = "RISK" AND issuetype in ("Feature") AND labels in ("basel")', @d_q,
                @d_nl, @d_nl, N'Created (3): ', @d_created2,
                @d_nl, @d_nl, N'Updated (', @d_payUpd2Count + @d_riskUpd2Count, N'): ', @d_payUpd2, N', ', @d_riskUpd2)),
        (3, @d_h3, DATEADD(second, 74, @d_h3), N'Success', N'Scheduler', 1, 7, 2, 5, 0, 1,
            CONCAT(N'1 key(s) processed, 7 fetched, 2 created, 5 updated, 0 issue(s) skipped, 1 key(s) failed.',
                @d_nl, @d_nl, N'Keys (3):',
                @d_nl, @d_b, N'CRM: skipped', @d_dash, N'no issue types selected',
                @d_nl, @d_b, N'PAY: fetched 7, created 2, updated 5; new Features by ART: Data Platform 1, Core Payments 1',
                @d_nl, @d_b, N'RISK: FAILED', @d_dash, N'Jira GET failed (ServiceUnavailable): Jira is down for maintenance.',
                @d_nl, @d_nl, N'Jira requests (5):',
                @d_nl, @d_b, N'PAY: project = "PAY" AND issuetype in ("Strategic Objective") AND updated >= "', @d_since2, N'"', @d_q,
                @d_nl, @d_b, N'PAY: project = "PAY" AND issuetype in ("Portfolio Epic") AND updated >= "', @d_since2, N'"', @d_q,
                @d_nl, @d_b, N'PAY: project = "PAY" AND issuetype in ("Business Outcome") AND updated >= "', @d_since2, N'"', @d_q,
                @d_nl, @d_b, N'PAY: project = "PAY" AND issuetype in ("Feature") AND updated >= "', @d_since2, N'"', @d_q,
                @d_nl, @d_b, N'RISK: project = "RISK" AND issuetype in ("Feature") AND labels in ("basel") AND updated >= "', @d_since2, N'"', @d_q,
                @d_nl, @d_nl, N'Created (2): PAY-140, PAY-508',
                @d_nl, @d_nl, N'Updated (5): PAY-9202, PAY-104, PAY-109, PAY-121, PAY-145')),
        (4, @d_h4, DATEADD(second, 107, @d_h4), N'Success', N'Scheduler', 3, 13, 1, 11, 0, 0,
            CONCAT(N'3 key(s) processed, 13 fetched, 1 created, 11 updated, 0 issue(s) skipped, 0 key(s) failed.',
                @d_nl, @d_nl, N'Keys (3):',
                @d_nl, @d_b, N'CRM: fetched 1, created 0, updated 1',
                @d_nl, @d_b, N'PAY: fetched 8, created 1, updated 6, 1 not created (status); new Features by ART: Mobile Banking 1',
                @d_nl, @d_b, N'RISK: fetched 4, created 0, updated 4',
                @d_nl, @d_nl, N'Jira requests (6):',
                @d_nl, @d_b, N'PAY: project = "PAY" AND issuetype in ("Strategic Objective") AND updated >= "', @d_since3, N'"', @d_q,
                @d_nl, @d_b, N'PAY: project = "PAY" AND issuetype in ("Portfolio Epic") AND updated >= "', @d_since3, N'"', @d_q,
                @d_nl, @d_b, N'PAY: project = "PAY" AND issuetype in ("Business Outcome") AND updated >= "', @d_since3, N'"', @d_q,
                @d_nl, @d_b, N'CRM: project = "CRM" AND issuetype in ("Feature") AND status in ("To Do", "In Progress")', @d_q,
                @d_nl, @d_b, N'PAY: project = "PAY" AND issuetype in ("Feature") AND updated >= "', @d_since3, N'"', @d_q,
                @d_nl, @d_b, N'RISK: project = "RISK" AND issuetype in ("Feature") AND labels in ("basel") AND updated >= "', @d_since2, N'"', @d_q,
                @d_nl, @d_nl, N'Created (1): PAY-213',
                @d_nl, @d_nl, N'Updated (11): CRM-903, PAY-9201, PAY-101, PAY-113, PAY-118, PAY-130, PAY-203, RISK-301, RISK-302, RISK-304, RISK-306'));
    SET IDENTITY_INSERT dbo.JiraSyncHistory OFF;

    -- sync settings
    IF EXISTS (SELECT 1 FROM dbo.JiraSyncSettings)
        UPDATE dbo.JiraSyncSettings
        SET Enabled = 0, NextRunAt = NULL, LastRunAt = (SELECT MAX(FinishedAt) FROM dbo.JiraSyncHistory);
    ELSE
        INSERT INTO dbo.JiraSyncSettings (Enabled, CycleCooldownMinutes, ServiceAccountUserName, JiraTimeZoneId, LabelsSyncEnabled, LastRunAt, NextRunAt)
        VALUES (0, 5, N'__jira-sync-service__', NULL, 1, (SELECT MAX(FinishedAt) FROM dbo.JiraSyncHistory), NULL);


    -- 50-governance: hygiene rules, feature snapshots and items, hash approvals, risks
    SET @Section = N'50-governance';
    PRINT N'Governance: hygiene rules, feature snapshots, hash approvals, risks';

    DECLARE @e_nl nchar(2) = NCHAR(13) + NCHAR(10);
    DECLARE @e_P0 nvarchar(200) = N'{"words":[],"mode":"And","minOtherWords":1,"caseSensitive":false,';
    DECLARE @e_PDef nvarchar(400) = @e_P0 + N'"values":[]}';
    DECLARE @e_PGwt nvarchar(400) = N'{"words":["Given","When","Then"],"mode":"Or","minOtherWords":1,"caseSensitive":false,"values":[]}';
    DECLARE @e_PAcWords nvarchar(400) = N'{"words":["Acceptance criteria"],"mode":"And","minOtherWords":5,"caseSensitive":false,"values":[]}';
    DECLARE @e_PSp21 nvarchar(400) = @e_P0 + N'"number":21,"values":[]}';
    DECLARE @e_PToday nvarchar(400) = @e_P0 + N'"today":true,"values":[]}';
    DECLARE @e_PDate nvarchar(400) = @e_P0 + N'"date":"' + CONVERT(nchar(10), DATEADD(day, 55, @Pi3Start), 23) + N'","values":[]}';
    DECLARE @e_PTa nvarchar(400) = @e_P0 + N'"values":["Approved","Not applicable"]}';
    DECLARE @e_PRed nvarchar(400) = @e_P0 + N'"values":["Red","(empty)"]}';
    DECLARE @e_PNotRefined nvarchar(400) = @e_P0 + N'"values":["Funnel","Backlog"]}';
    DECLARE @e_PApproved nvarchar(400) = @e_P0 + N'"values":["Approved"]}';
    DECLARE @e_POpen nvarchar(400) = @e_P0 + N'"values":["Done","Closed"]}';
    DECLARE @e_LOr nvarchar(4000) = N'{"thenLogic":"Any","moreThen":[{"field":"AcceptanceCriteria","check":"NotEmpty","parameters":' + @e_PDef + N',"kind":"Text"}],"whenLogic":"All","when":[]}';
    DECLARE @e_LDescGuard nvarchar(4000) = N'{"thenLogic":"All","moreThen":[],"whenLogic":"All","when":[{"field":"Description","check":"NotEmpty","parameters":' + @e_PDef + N',"kind":"Text"}]}';
    DECLARE @e_LEndRag nvarchar(4000) = N'{"thenLogic":"All","moreThen":[{"field":"RagStatus","check":"InValues","parameters":' + @e_P0 + N'"values":["Green","Amber"]},"kind":"Choice"}],"whenLogic":"All","when":[{"field":"Status","check":"NotInValues","parameters":' + @e_POpen + N',"kind":"Choice"},{"field":"TargetEnd","check":"NotEmpty","parameters":' + @e_PDef + N',"kind":"Date"}]}';
    DECLARE @e_LTaGuard nvarchar(4000) = N'{"thenLogic":"All","moreThen":[],"whenLogic":"Any","when":[{"field":"RequirementStatus","check":"InValues","parameters":' + @e_P0 + N'"values":["Ready for Review"]},"kind":"Choice"},{"field":"StoryPoints","check":"NotLessThan","parameters":' + @e_P0 + N'"number":13,"values":[]},"kind":"Number"}]}';
    DECLARE @e_LGwtGuard nvarchar(4000) = N'{"thenLogic":"All","moreThen":[],"whenLogic":"All","when":[{"field":"RequirementStatus","check":"InValues","parameters":' + @e_P0 + N'"values":["Ready for Review","Approved"]},"kind":"Choice"}]}';
    DECLARE @e_LOpenOnly nvarchar(4000) =N'{"thenLogic":"All","moreThen":[],"whenLogic":"All","when":[{"field":"Status","check":"NotInValues","parameters":' + @e_POpen + N',"kind":"Choice"}]}';
    DECLARE @e_Pi3Start2 datetime2 = CAST(@Pi3Start AS datetime2);
    DECLARE @e_Pi2Lock datetime2 = DATEADD(hour, 6, CAST(DATEADD(day, 84, @Pi2Start) AS datetime2));

    -- hygiene rules
    SET IDENTITY_INSERT dbo.FeatureHygieneRules ON;
    INSERT INTO dbo.FeatureHygieneRules (Id, CapitalProjectId, PiId, Field, [Check], ParametersJson, LogicJson, Message, SortOrder, IsEnabled, CreatedBy, CreatedAt, ModifiedBy, ModifiedAt) VALUES
        (1, 1, NULL, N'AcceptanceCriteria', N'NotEmpty', @e_PDef, NULL, N'Acceptance criteria are missing: write them in Jira before the feature is planned.', 0, 1, N'CORP\amorgan', DATEADD(day, -40, @NowUtc), N'CORP\amorgan', DATEADD(day, -12, @NowUtc)),
        (2, 1, NULL, N'Description', N'ContainsWords', @e_PGwt, @e_LGwtGuard, NULL, 1, 1, N'CORP\amorgan', DATEADD(day, -40, @NowUtc), NULL, NULL),
        (3, 1, NULL, N'AcceptanceCriteria', N'NotOnlyWords', @e_PAcWords, NULL, NULL, 2, 1, N'CORP\amorgan', DATEADD(day, -40, @NowUtc), NULL, NULL),
        (4, 1, NULL, N'StoryPoints', N'NotGreaterThan', @e_PSp21, NULL, NULL, 3, 1, N'CORP\amorgan', DATEADD(day, -40, @NowUtc), NULL, NULL),
        (5, 1, NULL, N'TargetEnd', N'NotLessThan', @e_PToday, @e_LEndRag, NULL, 4, 1, @Dev, DATEADD(day, -12, @NowUtc), NULL, NULL),
        (6, 1, NULL, N'TechnicalApproval', N'InValues', @e_PTa, NULL, NULL, 5, 1, N'CORP\amorgan', DATEADD(day, -40, @NowUtc), NULL, NULL),
        (7, 1, NULL, N'Status', N'NotInValues', @e_PNotRefined, NULL, N'Features planned in the PI must be refined beyond Funnel and Backlog.', 6, 1, N'CORP\pmo.office', DATEADD(day, -30, @NowUtc), NULL, NULL),
        (8, 1, NULL, N'IsLinkedToJira', N'IsTrue', @e_PDef, NULL, NULL, 7, 1, N'CORP\amorgan', DATEADD(day, -40, @NowUtc), NULL, NULL),
        (9, 1, NULL, N'FixVersions', N'NotEmpty', @e_PDef, NULL, NULL, 8, 1, N'CORP\pmo.office', DATEADD(day, -30, @NowUtc), NULL, NULL),
        (10, 1, NULL, N'Description', N'NotEmpty', @e_PDef, @e_LOr, NULL, 9, 1, @Dev, DATEADD(day, -12, @NowUtc), NULL, NULL),
        (11, 1, NULL, N'ConfidencePercentage', N'NotEmpty', @e_PDef, NULL, N'Give a delivery confidence before PI planning.', 10, 0, N'CORP\pmo.office', DATEADD(day, -30, @NowUtc), @Dev, DATEADD(day, -12, @NowUtc)),
        (12, 1, NULL, N'Teams', N'PrimaryTeamMarked', @e_PDef, NULL, NULL, 11, 1, N'CORP\amorgan', DATEADD(day, -40, @NowUtc), NULL, NULL),
        (13, 1, NULL, N'StoryPoints', N'StoryPointsMatchTeams', @e_PDef, NULL, NULL, 12, 1, N'CORP\amorgan', DATEADD(day, -40, @NowUtc), NULL, NULL),
        (14, 3, NULL, N'AcceptanceCriteria', N'NotEmpty', @e_PDef, @e_LDescGuard, NULL, 0, 1, N'CORP\ehorak', DATEADD(day, -21, @NowUtc), NULL, NULL),
        (15, 3, NULL, N'Description', N'ContainsWords', @e_PGwt, NULL, NULL, 1, 1, N'CORP\ehorak', DATEADD(day, -21, @NowUtc), NULL, NULL),
        (16, 3, NULL, N'AcceptanceCriteria', N'NotOnlyWords', @e_PAcWords, NULL, NULL, 2, 1, N'CORP\ehorak', DATEADD(day, -21, @NowUtc), NULL, NULL),
        (17, 3, NULL, N'StoryPoints', N'NotGreaterThan', @e_PSp21, NULL, NULL, 3, 1, N'CORP\ehorak', DATEADD(day, -21, @NowUtc), NULL, NULL),
        (18, 3, NULL, N'TargetEnd', N'NotLessThan', @e_PToday, @e_LOpenOnly, NULL, 4, 1, N'CORP\ehorak', DATEADD(day, -21, @NowUtc), N'CORP\ehorak', DATEADD(day, -9, @NowUtc)),
        (19, 3, NULL, N'TargetStart', N'NotGreaterThan', @e_PDate, NULL, NULL, 5, 1, @Dev, DATEADD(day, -9, @NowUtc), NULL, NULL),
        (20, 3, NULL, N'TechnicalApproval', N'InValues', @e_PApproved, @e_LTaGuard, NULL, 6, 1, N'CORP\ehorak', DATEADD(day, -21, @NowUtc), @Dev, DATEADD(day, -5, @NowUtc)),
        (21, 3, NULL, N'RagStatus', N'NotInValues', @e_PRed, NULL, NULL, 7, 1, N'CORP\ehorak', DATEADD(day, -21, @NowUtc), NULL, NULL),
        (22, 3, NULL, N'BusinessOutcome', N'NotEmpty', @e_PDef, NULL, N'Link the feature to a business outcome in Jira.', 8, 1, @Dev, DATEADD(day, -9, @NowUtc), NULL, NULL),
        (23, 3, NULL, N'Teams', N'PrimaryTeamMarked', @e_PDef, NULL, NULL, 9, 1, N'CORP\ehorak', DATEADD(day, -21, @NowUtc), NULL, NULL),
        (24, 3, NULL, N'StoryPoints', N'StoryPointsMatchTeams', @e_PDef, NULL, NULL, 10, 1, N'CORP\ehorak', DATEADD(day, -21, @NowUtc), NULL, NULL);
    SET IDENTITY_INSERT dbo.FeatureHygieneRules OFF;

    -- snapshots
    DROP TABLE IF EXISTS #e_Live;
    CREATE TABLE #e_Live (FeatureId int NOT NULL PRIMARY KEY, ArtId int NULL, PiId int NULL, JiraId nvarchar(100) NULL, PiName nvarchar(100) NULL,
        Labels nvarchar(4000) NULL, FixVersions nvarchar(4000) NULL, BoJiraId nvarchar(100) NULL, BoName nvarchar(255) NULL, TargetStart datetime2 NULL,
        TargetEnd datetime2 NULL, StoryPoints int NULL, Teams nvarchar(2000) NULL, RequirementStatus nvarchar(30) NULL, TechnicalApproval nvarchar(30) NULL,
        FundingStatus nvarchar(50) NULL, Summary nvarchar(255) NULL, [Name] nvarchar(255) NULL, AcceptanceCriteria nvarchar(max) NULL,
        PiObjective nvarchar(255) NULL, RagExplain nvarchar(255) NULL);
    INSERT INTO #e_Live (FeatureId, ArtId, PiId, JiraId, PiName, Labels, FixVersions, BoJiraId, BoName, TargetStart, TargetEnd, StoryPoints, Teams,
        RequirementStatus, TechnicalApproval, FundingStatus, Summary, [Name], AcceptanceCriteria, PiObjective, RagExplain)
    SELECT f.Id,
        CASE WHEN f.Id BETWEEN 101 AND 199 OR f.Id = 902 THEN 1 WHEN f.Id BETWEEN 201 AND 299 THEN 2 WHEN f.Id BETWEEN 301 AND 399 THEN 3
             WHEN f.Id BETWEEN 401 AND 499 THEN 4 WHEN f.Id BETWEEN 501 AND 599 THEN 5 END,
        f.PiId, f.JiraId, p.[Name], f.Labels, ISNULL(f.FixVersions, N''), bo.JiraId, bo.Summary, f.TargetStart, f.TargetEnd, f.StoryPoints, tm.Teams,
        rs.[Name], ta.[Name], uo.[Name], f.Summary, f.[Name], f.AcceptanceCriteria, po.[Name], f.RagExplain
    FROM dbo.Features AS f
    LEFT JOIN dbo.Pis AS p ON p.Id = f.PiId
    LEFT JOIN dbo.BusinessOutcomes AS bo ON bo.Id = f.BusinessOutcomeId
    LEFT JOIN dbo.RequirementStatuses AS rs ON rs.Id = f.RequirementStatusId
    LEFT JOIN dbo.TechnicalApprovals AS ta ON ta.Id = f.TechnicalApprovalId
    LEFT JOIN dbo.UnfundedOptions AS uo ON uo.Id = f.UnfundedOptionId
    LEFT JOIN dbo.PiObjectives AS po ON po.Id = f.PiObjectiveId
    OUTER APPLY (
        SELECT STRING_AGG(x.TeamName, N', ') WITHIN GROUP (ORDER BY UPPER(x.TeamName) COLLATE Latin1_General_BIN2) AS Teams
        FROM (SELECT DISTINCT TRIM(t.[Name]) AS TeamName FROM dbo.FeatureTeams AS ft JOIN dbo.Teams AS t ON t.Id = ft.TeamId
              WHERE ft.FeatureId = f.Id AND TRIM(t.[Name]) <> N'') AS x
    ) AS tm;

    DROP TABLE IF EXISTS #e_Snap;
    CREATE TABLE #e_Snap (Id int NOT NULL PRIMARY KEY, ArtId int NULL, CapitalProjectId int NULL, ArtName nvarchar(100) NULL, ArtJiraKeys nvarchar(200) NULL,
        PiId int NOT NULL, IsAutomatic bit NOT NULL, [Name] nvarchar(200) NULL, CreatedAt datetime2 NOT NULL, CreatedBy nvarchar(256) NULL);
    INSERT INTO #e_Snap (Id, ArtId, CapitalProjectId, ArtName, ArtJiraKeys, PiId, IsAutomatic, [Name], CreatedAt, CreatedBy) VALUES
        (1, 1, 1, NULL, N'PAY', 3, 0, N'PI kick-off baseline', DATEADD(hour, 9, DATEADD(day, 2, @e_Pi3Start2)), N'CORP\pmo.office'),
        (2, 1, 1, NULL, N'PAY', 3, 0, N'Mid-PI review', DATEADD(day, -1, @NowUtc), N'CORP\amorgan'),
        (3, 1, 1, NULL, N'PAY', 3, 0, NULL, DATEADD(minute, -125, DATEADD(day, -7, @NowUtc)), @Dev),
        (4, 1, 1, NULL, N'PAY', 2, 1, NULL, @e_Pi2Lock, N'CORP\pmo.office'),
        (5, 3, 3, NULL, N'RISK', 2, 1, NULL, @e_Pi2Lock, N'CORP\pmo.office'),
        (8, 2, 2, NULL, N'PAY,MOB', 2, 1, NULL, @e_Pi2Lock, N'CORP\pmo.office'),
        (9, 5, 5, NULL, N'DATA,PAY', 2, 1, NULL, @e_Pi2Lock, N'CORP\pmo.office'),
        (10, 4, 4, NULL, N'RISK', 2, 1, NULL, @e_Pi2Lock, N'CORP\pmo.office'),
        (6, 3, 3, NULL, N'RISK', 3, 0, N'Sprint 1 review', DATEADD(minute, 870, DATEADD(day, 15, @e_Pi3Start2)), N'CORP\ehorak'),
        (7, NULL, 99, N'Retired Payments ART', N'OLDPAY', 2, 0, N'Payments hub cut-over', DATEADD(minute, 600, DATEADD(day, 30, CAST(@Pi2Start AS datetime2))), N'CORP\former.lead');
    UPDATE s SET ArtName = cp.[Name] FROM #e_Snap AS s JOIN dbo.CapitalProjects AS cp ON cp.Id = s.ArtId;

    DROP TABLE IF EXISTS #e_Item;
    CREATE TABLE #e_Item (SnapshotId int NOT NULL, FeatureId int NOT NULL, JiraId nvarchar(100) NULL, ArtName nvarchar(100) NULL, PiName nvarchar(100) NULL,
        Labels nvarchar(4000) NULL, FixVersions nvarchar(4000) NULL, BoJiraId nvarchar(100) NULL, BoName nvarchar(255) NULL, TargetStart datetime2 NULL,
        TargetEnd datetime2 NULL, StoryPoints int NULL, Teams nvarchar(2000) NULL, RequirementStatus nvarchar(30) NULL, TechnicalApproval nvarchar(30) NULL,
        FundingStatus nvarchar(50) NULL, Summary nvarchar(255) NULL, [Name] nvarchar(255) NULL, AcceptanceCriteria nvarchar(max) NULL,
        PiObjective nvarchar(255) NULL, RagExplain nvarchar(255) NULL, PRIMARY KEY (SnapshotId, FeatureId));
    INSERT INTO #e_Item (SnapshotId, FeatureId, JiraId, ArtName, PiName, Labels, FixVersions, BoJiraId, BoName, TargetStart, TargetEnd, StoryPoints, Teams,
        RequirementStatus, TechnicalApproval, FundingStatus, Summary, [Name], AcceptanceCriteria, PiObjective, RagExplain)
    SELECT s.Id, l.FeatureId, l.JiraId, s.ArtName, l.PiName, l.Labels, l.FixVersions, l.BoJiraId, l.BoName, l.TargetStart, l.TargetEnd, l.StoryPoints, l.Teams,
        l.RequirementStatus, l.TechnicalApproval, l.FundingStatus, l.Summary, l.[Name], l.AcceptanceCriteria, l.PiObjective, l.RagExplain
    FROM #e_Snap AS s
    JOIN #e_Live AS l ON l.ArtId = s.ArtId
    JOIN dbo.Pis AS p ON p.Id = s.PiId
    WHERE l.PiId = s.PiId
       OR (p.LabelMatchMode = 0 AND EXISTS (
            SELECT 1 FROM STRING_SPLIT(p.FeatureLabels, N',') AS r
            WHERE TRIM(r.value) <> N'' AND EXISTS (SELECT 1 FROM STRING_SPLIT(l.Labels, N',') AS x WHERE TRIM(x.value) = TRIM(r.value))))
       OR (p.LabelMatchMode = 1 AND EXISTS (SELECT 1 FROM STRING_SPLIT(p.FeatureLabels, N',') AS r WHERE TRIM(r.value) <> N'') AND NOT EXISTS (
            SELECT 1 FROM STRING_SPLIT(p.FeatureLabels, N',') AS r
            WHERE TRIM(r.value) <> N'' AND NOT EXISTS (SELECT 1 FROM STRING_SPLIT(l.Labels, N',') AS x WHERE TRIM(x.value) = TRIM(r.value))));

    -- baseline (1)
    DELETE FROM #e_Item WHERE SnapshotId = 1 AND FeatureId IN (140, 144, 151);
    INSERT INTO #e_Item (SnapshotId, FeatureId, JiraId, ArtName, PiName, Labels, FixVersions, BoJiraId, BoName, TargetStart, TargetEnd, StoryPoints, Teams,
        RequirementStatus, TechnicalApproval, FundingStatus, Summary, [Name], AcceptanceCriteria, PiObjective, RagExplain)
    SELECT 1, l.FeatureId, l.JiraId, N'Core Payments', @Pi3Name, l.Labels, N'PAY ' + @Pi3Code + N'.5', l.BoJiraId, l.BoName,
        DATEADD(day, 56, @Pi3Start), DATEADD(day, 76, @Pi3Start), l.StoryPoints, l.Teams, l.RequirementStatus, l.TechnicalApproval, l.FundingStatus,
        l.Summary, l.[Name], l.AcceptanceCriteria, l.PiObjective, l.RagExplain
    FROM #e_Live AS l WHERE l.FeatureId = 148;
    INSERT INTO #e_Item (SnapshotId, FeatureId, JiraId, ArtName, PiName, Labels, FixVersions, BoJiraId, BoName, TargetStart, TargetEnd, StoryPoints, Teams,
        RequirementStatus, TechnicalApproval, FundingStatus, Summary, [Name], AcceptanceCriteria, PiObjective, RagExplain)
    VALUES (1, 137, N'PAY-137', N'Core Payments', @Pi3Name, N'legacy-hub', N'PAY ' + @Pi3Code + N'.2', N'PAY-9201', N'Real-time payment rails',
        DATEADD(day, 14, @Pi3Start), DATEADD(day, 34, @Pi3Start), 8, N'Boron', N'Approved', N'Not applicable', NULL,
        N'Legacy SWIFT gateway retirement', N'SWIFT gateway retirement',
        CONCAT(N'h2. Acceptance criteria', @e_nl, N'* All SWIFT flows moved to the new hub', @e_nl, N'* Gateway servers switched off'), NULL, NULL);
    UPDATE #e_Item SET StoryPoints = 5 WHERE SnapshotId = 1 AND FeatureId = 101;
    UPDATE #e_Item SET RequirementStatus = N'Ready for Review' WHERE SnapshotId = 1 AND FeatureId = 102;
    UPDATE #e_Item SET TargetStart = DATEADD(day, 14, @Pi3Start), TargetEnd = DATEADD(day, 34, @Pi3Start), FixVersions = N'PAY ' + @Pi3Code + N'.2', RagExplain = NULL
    WHERE SnapshotId = 1 AND FeatureId = 103;
    UPDATE #e_Item SET RagExplain = N'Waiting for the scheme certification slot' WHERE SnapshotId = 1 AND FeatureId = 104;
    UPDATE #e_Item SET FundingStatus = N'Pending Approval' WHERE SnapshotId = 1 AND FeatureId = 105;
    UPDATE #e_Item SET Summary = N'Confirmation of payee name check' WHERE SnapshotId = 1 AND FeatureId = 106;
    UPDATE #e_Item SET Teams = N'Argon', StoryPoints = 13 WHERE SnapshotId = 1 AND FeatureId = 109;
    UPDATE #e_Item SET [Name] = N'Fraud case management' WHERE SnapshotId = 1 AND FeatureId = 110;
    UPDATE #e_Item SET PiObjective = NULL WHERE SnapshotId = 1 AND FeatureId = 111;
    UPDATE #e_Item SET TechnicalApproval = N'Approved' WHERE SnapshotId = 1 AND FeatureId = 112;
    UPDATE #e_Item SET Labels = N'instant-payments' WHERE SnapshotId = 1 AND FeatureId = 113;
    UPDATE #e_Item SET AcceptanceCriteria = CONCAT(N'h2. Acceptance criteria', @e_nl, N'# Billers can create a request to pay through the API', @e_nl,
        N'# Customers can accept the request', @e_nl, N'# Requests expire after 30 days') WHERE SnapshotId = 1 AND FeatureId = 118;
    UPDATE #e_Item SET Labels = N'Instant-Payments,' + UPPER(@CurPiLabel) WHERE SnapshotId = 1 AND FeatureId = 120;
    UPDATE #e_Item SET FixVersions = N'PAY ' + @Pi3Code + N'.1' WHERE SnapshotId = 1 AND FeatureId = 123;
    UPDATE #e_Item SET FixVersions = N'' WHERE SnapshotId = 1 AND FeatureId = 127;
    UPDATE #e_Item SET Labels = N'mvp,instant-payments, ' + @CurPiLabel + N',MVP' WHERE SnapshotId = 1 AND FeatureId = 130;
    UPDATE #e_Item SET BoJiraId = N'PAY-9201', BoName = N'Real-time payment rails' WHERE SnapshotId = 1 AND FeatureId = 133;
    UPDATE #e_Item SET PiName = @Pi3Name WHERE SnapshotId = 1 AND FeatureId = 143;
    UPDATE #e_Item SET Teams = N'boron, Argon', Labels = N'Resilience' WHERE SnapshotId = 1 AND FeatureId = 145;
    UPDATE #e_Item SET StoryPoints = 3 WHERE SnapshotId = 1 AND FeatureId = 902;

    -- unnamed (3)
    DELETE FROM #e_Item WHERE SnapshotId = 3 AND FeatureId = 140;
    UPDATE #e_Item SET StoryPoints = 5 WHERE SnapshotId = 3 AND FeatureId = 101;
    UPDATE #e_Item SET Teams = N'Argon', StoryPoints = 13 WHERE SnapshotId = 3 AND FeatureId = 109;
    UPDATE #e_Item SET TargetStart = DATEADD(day, 28, @Pi3Start), TargetEnd = DATEADD(day, 48, @Pi3Start), FixVersions = N'PAY ' + @Pi3Code + N'.3,Scheme Certification',
        RagExplain = N'Certification slot moved by the scheme to sprint 3' WHERE SnapshotId = 3 AND FeatureId = 103;

    -- PI lock (4)
    UPDATE #e_Item SET Labels = N'tokenisation', TargetEnd = DATEADD(day, 76, @Pi2Start), FixVersions = N'PAY ' + @Pi2Code + N'.5' WHERE SnapshotId = 4 AND FeatureId = 144;

    -- Risk Analytics (6)
    DELETE FROM #e_Item WHERE SnapshotId = 6 AND FeatureId = 312;
    INSERT INTO #e_Item (SnapshotId, FeatureId, JiraId, ArtName, PiName, Labels, FixVersions, BoJiraId, BoName, TargetStart, TargetEnd, StoryPoints, Teams,
        RequirementStatus, TechnicalApproval, FundingStatus, Summary, [Name], AcceptanceCriteria, PiObjective, RagExplain)
    SELECT 6, l.FeatureId, l.JiraId, N'Risk Analytics', @Pi3Name, l.Labels, l.FixVersions, l.BoJiraId, l.BoName,
        DATEADD(day, 56, @Pi3Start), DATEADD(day, 76, @Pi3Start), l.StoryPoints, l.Teams, l.RequirementStatus, l.TechnicalApproval, l.FundingStatus,
        l.Summary, l.[Name], l.AcceptanceCriteria, l.PiObjective, l.RagExplain
    FROM #e_Live AS l WHERE l.FeatureId = 313;
    UPDATE #e_Item SET StoryPoints = 21 WHERE SnapshotId = 6 AND FeatureId = 306;
    UPDATE #e_Item SET [Name] = N'Stress scenarios' WHERE SnapshotId = 6 AND FeatureId = 310;

    -- legacy (7)
    INSERT INTO #e_Item (SnapshotId, FeatureId, JiraId, ArtName, PiName, Labels, FixVersions, BoJiraId, BoName, TargetStart, TargetEnd, StoryPoints, Teams,
        RequirementStatus, TechnicalApproval, FundingStatus, Summary, [Name], AcceptanceCriteria, PiObjective, RagExplain) VALUES
        (7, 9701, N'OLDPAY-17', N'Retired Payments ART', @Pi2Name, N'legacy-hub,cut-over', NULL, NULL, NULL, DATEADD(day, 14, @Pi2Start), DATEADD(day, 41, @Pi2Start),
            13, N'Argon, Carbon', N'Committed', N'Approved', NULL, N'Legacy SWIFT gateway cut-over', N'SWIFT cut-over', NULL, N'Launch instant payments MVP', NULL),
        (7, 9702, N'OLDPAY-18', N'Retired Payments ART', @Pi2Name, N'legacy-hub', NULL, NULL, NULL, DATEADD(day, 28, @Pi2Start), DATEADD(day, 55, @Pi2Start),
            8, N'Boron', N'Approved', N'Not applicable', N'Deferred', N'Batch payment file decommissioning', NULL, NULL, NULL, N'Waiting for the last corporate client to move');

    SET IDENTITY_INSERT dbo.FeatureSnapshots ON;
    INSERT INTO dbo.FeatureSnapshots (Id, CapitalProjectId, ArtName, ArtJiraKeys, PiName, IncludedPiLabelMatches, CreatedAt, CreatedBy, FeatureCount, IsAutomatic, [Name])
    SELECT s.Id, s.CapitalProjectId, s.ArtName, s.ArtJiraKeys, p.[Name], 1, s.CreatedAt, s.CreatedBy,
        (SELECT COUNT(*) FROM #e_Item AS i WHERE i.SnapshotId = s.Id), s.IsAutomatic, s.[Name]
    FROM #e_Snap AS s JOIN dbo.Pis AS p ON p.Id = s.PiId;
    SET IDENTITY_INSERT dbo.FeatureSnapshots OFF;

    SET IDENTITY_INSERT dbo.FeatureSnapshotItems ON;
    INSERT INTO dbo.FeatureSnapshotItems (Id, FeatureSnapshotId, FeatureId, JiraId, ArtName, PiName, Labels, FixVersions, BusinessOutcomeJiraId, BusinessOutcomeName,
        TargetStart, TargetEnd, StoryPoints, Teams, RequirementStatus, TechnicalApproval, FundingStatus, Summary, [Name], AcceptanceCriteria, PiObjective, RagExplain)
    SELECT i.SnapshotId * 100 + ROW_NUMBER() OVER (PARTITION BY i.SnapshotId ORDER BY i.JiraId, i.FeatureId), i.SnapshotId, i.FeatureId, i.JiraId, i.ArtName, i.PiName,
        i.Labels, i.FixVersions, i.BoJiraId, i.BoName, i.TargetStart, i.TargetEnd, i.StoryPoints, i.Teams, i.RequirementStatus, i.TechnicalApproval, i.FundingStatus,
        i.Summary, i.[Name], i.AcceptanceCriteria, i.PiObjective, i.RagExplain
    FROM #e_Item AS i;
    SET IDENTITY_INSERT dbo.FeatureSnapshotItems OFF;

    -- hash approvals
    DROP TABLE IF EXISTS #e_State;
    CREATE TABLE #e_State (Ap int NOT NULL PRIMARY KEY, FeatureId int NOT NULL, JiraId nvarchar(100) NULL, ArtName nvarchar(100) NULL, PiName nvarchar(100) NULL,
        Labels nvarchar(4000) NULL, BoJiraId nvarchar(100) NULL, BoName nvarchar(255) NULL, TargetStart datetime2 NULL, TargetEnd datetime2 NULL,
        StoryPoints int NULL, Teams nvarchar(2000) NULL, RequirementStatus nvarchar(30) NULL, TechnicalApproval nvarchar(30) NULL, FundingStatus nvarchar(50) NULL,
        Summary nvarchar(255) NULL, [Name] nvarchar(255) NULL, AcceptanceCriteria nvarchar(max) NULL, PiObjective nvarchar(255) NULL, RagExplain nvarchar(255) NULL);
    INSERT INTO #e_State (Ap, FeatureId, JiraId, ArtName, PiName, Labels, BoJiraId, BoName, TargetStart, TargetEnd, StoryPoints, Teams,
        RequirementStatus, TechnicalApproval, FundingStatus, Summary, [Name], AcceptanceCriteria, PiObjective, RagExplain)
    SELECT a.Ap, i.FeatureId, i.JiraId, i.ArtName, i.PiName, i.Labels, i.BoJiraId, i.BoName, i.TargetStart, i.TargetEnd, i.StoryPoints, i.Teams,
        i.RequirementStatus, i.TechnicalApproval, i.FundingStatus, i.Summary, i.[Name], i.AcceptanceCriteria, i.PiObjective, i.RagExplain
    FROM (VALUES (2, 2, 101), (3, 2, 902), (4, 2, 140), (5, 3, 103), (6, 2, 109)) AS a (Ap, SnapshotId, FeatureId)
    JOIN #e_Item AS i ON i.SnapshotId = a.SnapshotId AND i.FeatureId = a.FeatureId;
    INSERT INTO #e_State (Ap, FeatureId, JiraId, ArtName, PiName, Labels, BoJiraId, BoName, TargetStart, TargetEnd, StoryPoints, Teams,
        RequirementStatus, TechnicalApproval, FundingStatus, Summary, [Name], AcceptanceCriteria, PiObjective, RagExplain)
    SELECT 7, l.FeatureId, l.JiraId, N'Risk Analytics', l.PiName, l.Labels, l.BoJiraId, l.BoName, l.TargetStart, l.TargetEnd, l.StoryPoints, l.Teams,
        l.RequirementStatus, l.TechnicalApproval, l.FundingStatus, l.Summary, l.[Name], l.AcceptanceCriteria, l.PiObjective, l.RagExplain
    FROM #e_Live AS l WHERE l.FeatureId = 306;

    DROP TABLE IF EXISTS #e_Canon;
    CREATE TABLE #e_Canon (Ap int NOT NULL, Ord int NOT NULL, F nvarchar(50) NOT NULL, V nvarchar(max) NULL, PRIMARY KEY (Ap, Ord));
    INSERT INTO #e_Canon (Ap, Ord, F, V)
    SELECT s.Ap, v.Ord, v.F, v.V
    FROM #e_State AS s
    OUTER APPLY (SELECT STRING_AGG(x.U, N',') WITHIN GROUP (ORDER BY x.U COLLATE Latin1_General_BIN2) AS CanonLabels
        FROM (SELECT DISTINCT UPPER(TRIM(t.value)) AS U FROM STRING_SPLIT(s.Labels, N',') AS t WHERE TRIM(t.value) <> N'') AS x) AS lb
    OUTER APPLY (SELECT STRING_AGG(x.U, N',') WITHIN GROUP (ORDER BY x.U COLLATE Latin1_General_BIN2) AS CanonTeams
        FROM (SELECT DISTINCT UPPER(TRIM(t.value)) AS U FROM STRING_SPLIT(s.Teams, N',') AS t WHERE TRIM(t.value) <> N'') AS x) AS tm
    CROSS APPLY (SELECT NULLIF(TRIM(s.BoJiraId), N'') AS BoJ, NULLIF(TRIM(s.BoName), N'') AS BoN) AS b
    CROSS APPLY (VALUES
        (1, N'ART', NULLIF(TRIM(s.ArtName), N'')),
        (2, N'PI', NULLIF(TRIM(s.PiName), N'')),
        (3, N'Labels', lb.CanonLabels),
        (4, N'Business outcome', CASE WHEN b.BoN IS NULL THEN b.BoJ WHEN b.BoJ IS NULL THEN b.BoN ELSE b.BoJ + N' ' + NCHAR(8212) + N' ' + b.BoN END),
        (5, N'Target start', CONVERT(nvarchar(10), s.TargetStart, 23)),
        (6, N'Target end', CONVERT(nvarchar(10), s.TargetEnd, 23)),
        (7, N'Story points', CONVERT(nvarchar(12), s.StoryPoints)),
        (8, N'Teams', tm.CanonTeams),
        (9, N'Requirement status', NULLIF(TRIM(s.RequirementStatus), N'')),
        (10, N'Design Approval', NULLIF(TRIM(s.TechnicalApproval), N'')),
        (11, N'Funding', NULLIF(TRIM(s.FundingStatus), N'')),
        (12, N'Summary', NULLIF(TRIM(s.Summary), N'')),
        (13, N'Name', NULLIF(TRIM(s.[Name]), N'')),
        (14, N'Acceptance criteria', NULLIF(TRIM(s.AcceptanceCriteria), N'')),
        (15, N'PI objective', NULLIF(TRIM(s.PiObjective), N'')),
        (16, N'Rag explain', NULLIF(TRIM(s.RagExplain), N''))
    ) AS v (Ord, F, V);

    DROP TABLE IF EXISTS #e_Hash;
    CREATE TABLE #e_Hash (Ap int NOT NULL PRIMARY KEY, StateJson nvarchar(max) NOT NULL, StateHash nvarchar(80) NULL);
    INSERT INTO #e_Hash (Ap, StateJson)
    SELECT c.Ap, N'[' + STRING_AGG(CAST(N'{"f":"' + c.F + N'","v":' +
        CASE WHEN c.V IS NULL THEN N'null' ELSE N'"' +
            REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                c.V COLLATE Latin1_General_BIN2,
                N'\', N'\\'), N'"', N'\' + N'u0022'), N'''', N'\' + N'u0027'), N'&', N'\' + N'u0026'), N'<', N'\' + N'u003C'), N'>', N'\' + N'u003E'),
                N'+', N'\' + N'u002B'), N'`', N'\' + N'u0060'), NCHAR(13), N'\r'), NCHAR(10), N'\n'), NCHAR(9), N'\t'), NCHAR(8212), N'\' + N'u2014')
            + N'"' END + N'}' AS nvarchar(max)), N',') WITHIN GROUP (ORDER BY c.Ord) + N']'
    FROM #e_Canon AS c
    GROUP BY c.Ap;
    UPDATE #e_Hash SET StateHash = N'v1:' + LOWER(CONVERT(varchar(64), HASHBYTES('SHA2_256', CONVERT(varchar(max), StateJson)), 2));

    SET IDENTITY_INSERT dbo.FeatureStateApprovals ON;
    INSERT INTO dbo.FeatureStateApprovals (Id, ArtName, PiName, FeatureKey, JiraId, FeatureName, StateHash, StateJson, ChangesJson, BaselineSnapshotId, Comment,
        ApprovedBy, ApprovedAt, WithdrawnBy, WithdrawnAt)
    VALUES (1, N'Core Payments', @Pi3Name, N'PAY-148', N'PAY-148', N'Hub cost reporting', N'v1:REMOVED', NULL, NULL, 1,
        N'Moved to the next PI at the PO sync', N'CORP\amorgan', DATEADD(hour, -50, @NowUtc), NULL, NULL);
    INSERT INTO dbo.FeatureStateApprovals (Id, ArtName, PiName, FeatureKey, JiraId, FeatureName, StateHash, StateJson, ChangesJson, BaselineSnapshotId, Comment,
        ApprovedBy, ApprovedAt, WithdrawnBy, WithdrawnAt)
    SELECT a.Ap, s.ArtName, @Pi3Name, ISNULL(UPPER(NULLIF(TRIM(s.JiraId), N'')), N'#' + CONVERT(nvarchar(12), s.FeatureId)), NULLIF(TRIM(s.JiraId), N''), s.[Name],
        h.StateHash, h.StateJson, a.ChangesJson, a.BaselineId, a.Comment, a.ApprovedBy, a.ApprovedAt, a.WithdrawnBy, a.WithdrawnAt
    FROM (VALUES
        (2, N'[{"Field":"Story points","OldValue":"5","NewValue":"8"}]', 1, N'Scope grew after the scheme review; agreed with the PO',
            N'CORP\amorgan', DATEADD(hour, -20, @NowUtc), NULL, NULL),
        (3, N'[{"Field":"Story points","OldValue":"3","NewValue":"5"}]', 1, N'Local item re-estimated with operations',
            @Dev, DATEADD(hour, -18, @NowUtc), NULL, NULL),
        (4, NULL, 1, N'New cut-off rules accepted into the PI by the business owner',
            N'CORP\pmo.office', DATEADD(hour, -10, @NowUtc), NULL, NULL),
        (5, CONCAT(N'[{"Field":"Target start","OldValue":"', CONVERT(nchar(10), DATEADD(day, 14, @Pi3Start), 23), N'","NewValue":"', CONVERT(nchar(10), DATEADD(day, 28, @Pi3Start), 23),
            N'"},{"Field":"Target end","OldValue":"', CONVERT(nchar(10), DATEADD(day, 34, @Pi3Start), 23), N'","NewValue":"', CONVERT(nchar(10), DATEADD(day, 48, @Pi3Start), 23),
            N'"},{"Field":"Rag explain","OldValue":null,"NewValue":"Certification slot moved by the scheme to sprint 3"}]'), 1, N'Certification moved to sprint 3',
            N'CORP\cnovak', DATEADD(hour, -146, @NowUtc), NULL, NULL),
        (6, N'[{"Field":"Story points","OldValue":"13","NewValue":"18"},{"Field":"Teams","OldValue":"Argon","NewValue":"Argon, Boron"}]', 1, N'Boron joins for the fraud scoring work',
            N'CORP\amorgan', DATEADD(hour, -75, @NowUtc), @Dev, DATEADD(hour, -49, @NowUtc)),
        (7, N'[{"Field":"Story points","OldValue":"21","NewValue":"34"}]', 6, N'SA-CCR scope now covers FX forwards',
            N'CORP\ehorak', DATEADD(hour, -30, @NowUtc), NULL, NULL)
    ) AS a (Ap, ChangesJson, BaselineId, Comment, ApprovedBy, ApprovedAt, WithdrawnBy, WithdrawnAt)
    JOIN #e_State AS s ON s.Ap = a.Ap
    JOIN #e_Hash AS h ON h.Ap = a.Ap;
    SET IDENTITY_INSERT dbo.FeatureStateApprovals OFF;

    -- risks
    SET IDENTITY_INSERT dbo.Risks ON;
    INSERT INTO dbo.Risks (Id, PiId, Category, Severity, [Status], Summary, Owner, DateRaised, DueBy, CreatedBy, DateUpdated, UpdatedBy) VALUES
        (1, 4, 0, 2, 0, N'Card scheme recertification may not finish before the recurring payment tokens go live', N'Sofia Brandt',
            DATEADD(day, -3, @Today), DATEADD(day, 14, @Pi4Start), N'CORP\amorgan', DATEADD(hour, -26, @NowUtc), N'CORP\pmo.office'),
        (2, 4, 1, 1, 0, N'Only one model validator is available for the IRB parallel run', N'Owen Hale',
            DATEADD(day, -10, @Today), NULL, N'CORP\ehorak', DATEADD(hour, -50, @NowUtc), N'CORP\ehorak'),
        (3, 4, 0, 0, 0, N'Shared performance test environment not yet booked for the next PI', NULL,
            DATEADD(day, -1, @Today), @Pi4End, N'CORP\contractor.x', NULL, NULL),
        (4, 4, 1, 2, 1, N'Data lake storage quota exhausted; new self-service data sets cannot be loaded', N'Priya Raman',
            DATEADD(day, -12, @Today), DATEADD(day, -2, @Today), N'CORP\pmo.office', DATEADD(hour, -6, @NowUtc), @Dev),
        (5, 4, 0, 1, 2, N'Legacy warehouse licence renewal could block the migration window', N'Owen Hale',
            DATEADD(day, -20, @Today), DATEADD(day, 7, @Pi4Start), @Dev, DATEADD(hour, -100, @NowUtc), N'CORP\pmo.office'),
        (6, 3, 0, 2, 0, N'Scheme test environment outage blocks failover and settlement testing', N'Alice Morgan',
            DATEADD(day, -18, @Today), DATEADD(day, -5, @Today), N'CORP\amorgan', DATEADD(hour, -4, @NowUtc), N'CORP\amorgan'),
        (7, 3, 1, 2, 0, N'Card scheme certification failed; tokenisation go-live is at risk', N'Sofia Brandt',
            DATEADD(day, -15, @Today), DATEADD(day, 42, @Pi3Start), N'CORP\cnovak', DATEADD(hour, -30, @NowUtc), N'CORP\cnovak'),
        (8, 3, 0, 0, 1, N'Derivative trade feed may stay incomplete for SA-CCR', N'Eva Horak',
            DATEADD(day, -21, @Today), DATEADD(day, 49, @Pi3Start), N'CORP\ehorak', DATEADD(hour, -72, @NowUtc), N'CORP\ehorak'),
        (9, 3, 1, 0, 2, N'Android accessibility retest failed on two screens', N'Farid Khan',
            DATEADD(day, -16, @Today), DATEADD(day, -6, @Today), N'CORP\cnovak', DATEADD(hour, -140, @NowUtc), N'CORP\pmo.office'),
        (10, 3, 0, 1, 1, N'Shared CI/CD pipeline upgrade may freeze deployments for a week', N'Grace Liu',
            DATEADD(day, -9, @Today), NULL, @Dev, DATEADD(hour, -20, @NowUtc), @Dev),
        (11, 3, 1, 1, 2, N'Legacy risk extract jobs fail after the warehouse patch', N'Daniel Price',
            DATEADD(day, -23, @Today), DATEADD(day, -10, @Today), N'CORP\ehorak', DATEADD(hour, -170, @NowUtc), N'CORP\ehorak'),
        (12, 3, 0, 2, 2, N'BI licence funding not approved for self-service analytics', N'Priya Raman',
            DATEADD(day, -24, @Today), DATEADD(day, 20, @Pi3Start), N'CORP\pmo.office', DATEADD(hour, -120, @NowUtc), N'CORP\pmo.office'),
        (13, 2, 1, 1, 2, N'Second data centre for the token vault delivered late', N'Martin Vale',
            DATEADD(day, 30, @Pi2Start), @Pi2End, N'CORP\cnovak', DATEADD(minute, 115680, CAST(@Pi2Start AS datetime2)), N'CORP\cnovak'),
        (14, 2, 0, 0, 1, N'Key staff holidays during the PI may slow the token vault MVP', N'Ben Carter',
            DATEADD(day, 9, @Pi2Start), DATEADD(day, 56, @Pi2Start), N'CORP\amorgan', DATEADD(minute, 101345, CAST(@Pi2Start AS datetime2)), @Dev);
    SET IDENTITY_INSERT dbo.Risks OFF;

    INSERT INTO dbo.RiskCapitalProjects (RiskId, CapitalProjectId) VALUES
        (1, 1), (1, 2),
        (2, 3),
        (4, 5), (4, 6),
        (5, 4),
        (6, 1),
        (7, 1), (7, 2),
        (8, 3),
        (9, 2),
        (10, 1), (10, 3), (10, 5), (10, 6),
        (11, 4),
        (12, 5),
        (13, 1);

    INSERT INTO dbo.RiskFeatures (RiskId, FeatureId) VALUES
        (1, 147), (1, 141), (1, 142),
        (2, 314),
        (4, 507),
        (5, 405),
        (6, 113), (6, 101), (6, 135),
        (7, 104), (7, 103),
        (8, 306),
        (9, 206), (9, 201),
        (11, 404),
        (12, 506),
        (13, 142), (13, 155);

    SET IDENTITY_INSERT dbo.RiskComments ON;
    INSERT INTO dbo.RiskComments (Id, RiskId, [Text], Author, CreatedAt, IsDone, DoneBy, DoneAt) VALUES
        (1, 1, N'Scheme confirmed the recertification slot for the second week of the PI.', N'CORP\amorgan', DATEADD(hour, -60, @NowUtc), 0, NULL, NULL),
        (2, 1, N'Ask the scheme for a backup slot in case the first run fails.', @Dev, DATEADD(hour, -40, @NowUtc), 1, N'CORP\amorgan', DATEADD(hour, -28, @NowUtc)),
        (3, 1, N'Backup slot requested; waiting for the scheme to answer.', N'CORP\pmo.office', DATEADD(hour, -26, @NowUtc), 0, NULL, NULL),
        (4, 2, N'Model committee agreed to borrow a validator from the market risk team.', N'CORP\ehorak', DATEADD(hour, -200, @NowUtc), 0, NULL, NULL),
        (5, 2, N'Borrowed validator starts two weeks after the PI begins.', N'CORP\contractor.x', DATEADD(hour, -50, @NowUtc), 0, NULL, NULL),
        (6, 4, N'Storage quota raised temporarily by 20 TB.', N'CORP\pmo.office', DATEADD(hour, -240, @NowUtc), 1, @Dev, DATEADD(hour, -100, @NowUtc)),
        (7, 4, N'Archive job for raw zone files ready; close the issue once it has run twice.', @Dev, DATEADD(hour, -6, @NowUtc), 0, NULL, NULL),
        (8, 5, N'Licence renewed for twelve months; risk closed.', N'CORP\pmo.office', DATEADD(hour, -100, @NowUtc), 0, NULL, NULL),
        (9, 6, N'Provider says the environment is back next Monday.', N'CORP\amorgan', DATEADD(hour, -400, @NowUtc), 1, N'CORP\amorgan', DATEADD(hour, -300, @NowUtc)),
        (10, 6, N'Update after the scheme call: the provider confirmed the test environment returns on Monday, but only four hours a day until the hardware swap is done. We will run the failover suite in two halves and keep the manual checklist as fallback. If the window slips again we escalate to the steering group.', N'CORP\cnovak', DATEADD(hour, -170, @NowUtc), 0, NULL, NULL),
        (11, 6, N'Failover suite part one passed; part two still blocked.', @Dev, DATEADD(hour, -30, @NowUtc), 0, NULL, NULL),
        (12, 6, N'Escalated to the steering group.', N'CORP\pmo.office', DATEADD(hour, -4, @NowUtc), 0, NULL, NULL),
        (13, 7, N'Retest booked with the scheme; certification letters expected after it.', N'CORP\cnovak', DATEADD(hour, -300, @NowUtc), 0, NULL, NULL),
        (14, 7, N'Mobile wallet launch moved behind the retest.', N'CORP\amorgan', DATEADD(hour, -30, @NowUtc), 0, NULL, NULL),
        (15, 8, N'Trade data owners found the missing FX forward records.', N'CORP\ehorak', DATEADD(hour, -72, @NowUtc), 0, NULL, NULL),
        (16, 8, N'Propose to close at the next risk review.', @Dev, DATEADD(hour, -48, @NowUtc), 1, N'CORP\ehorak', DATEADD(hour, -47, @NowUtc)),
        (17, 9, N'Retest passed on all screens.', N'CORP\cnovak', DATEADD(hour, -150, @NowUtc), 0, NULL, NULL),
        (18, 9, N'Closing; fixes are in the next app release.', N'CORP\pmo.office', DATEADD(hour, -140, @NowUtc), 1, N'CORP\pmo.office', DATEADD(hour, -139, @NowUtc)),
        (19, 10, N'Upgrade window agreed for the IP sprint, so no PI work is frozen.', @Dev, DATEADD(hour, -20, @NowUtc), 0, NULL, NULL),
        (20, 11, N'Patch rolled back; extracts run again.', N'CORP\ehorak', DATEADD(hour, -400, @NowUtc), 0, NULL, NULL),
        (21, 11, N'Root cause documented in the problem record.', N'CORP\contractor.x', DATEADD(hour, -170, @NowUtc), 1, N'CORP\ehorak', DATEADD(hour, -168, @NowUtc)),
        (22, 12, N'Investment board rejected the BI licences for this year.', N'CORP\pmo.office', DATEADD(hour, -500, @NowUtc), 0, NULL, NULL),
        (23, 12, N'Scope of self-service analytics put on hold.', N'CORP\amorgan', DATEADD(hour, -120, @NowUtc), 0, NULL, NULL),
        (24, 13, N'Second data centre handed over; replication tests start next sprint.', N'CORP\cnovak', DATEADD(minute, 86970, CAST(@Pi2Start AS datetime2)), 0, NULL, NULL),
        (25, 13, N'Replication lag within target; closing.', N'CORP\cnovak', DATEADD(minute, 115620, CAST(@Pi2Start AS datetime2)), 1, N'CORP\cnovak', DATEADD(minute, 115680, CAST(@Pi2Start AS datetime2))),
        (26, 14, N'Holiday cover agreed with the Boron team.', N'CORP\amorgan', DATEADD(minute, 29410, CAST(@Pi2Start AS datetime2)), 0, NULL, NULL),
        (27, 14, N'Can be closed after the PI review.', @Dev, DATEADD(minute, 101345, CAST(@Pi2Start AS datetime2)), 0, NULL, NULL);
    SET IDENTITY_INSERT dbo.RiskComments OFF;


    -- 60-admin: ART capacity page, users, profiles and permissions, global filter, global message, backup, audit log
    SET @Section = N'60-admin';
    PRINT N'Admin: ART capacity page, users, profiles, permissions, global filter, global message, backup history, audit log';

    DECLARE @f_DevJson nvarchar(600) = REPLACE(@Dev, N'\', N'\\');
    DECLARE @f_T1 datetime2 = DATEADD(second, -2243, DATEADD(day, -26, @NowUtc));
    DECLARE @f_T2 datetime2 = DATEADD(second, -5120, DATEADD(day, -18, @NowUtc));
    DECLARE @f_T3 datetime2 = DATEADD(second, -1310, DATEADD(day, -9, @NowUtc));
    SET @f_T3 = DATEADD(nanosecond, CASE WHEN DATEPART(nanosecond, @f_T3) % 1000 = 0 THEN 100 ELSE 0 END, @f_T3);
    DECLARE @f_T4 datetime2 = DATEADD(millisecond, 41, @f_T3);
    DECLARE @f_T5 datetime2 = DATEADD(second, -7600, DATEADD(day, -5, @NowUtc));
    DECLARE @f_T6 datetime2 = DATEADD(second, -940, DATEADD(hour, -26, @NowUtc));
    DECLARE @f_RiskUpdated datetime2 = DATEADD(second, -3170, DATEADD(day, -40, @NowUtc));
    SET @f_RiskUpdated = DATEADD(nanosecond, CASE WHEN DATEPART(nanosecond, @f_RiskUpdated) % 1000 = 0 THEN 100 ELSE 0 END, @f_RiskUpdated);
    DECLARE @f_B1 uniqueidentifier = NEWID();
    DECLARE @f_B2 uniqueidentifier = NEWID();
    DECLARE @f_B3 uniqueidentifier = NEWID();
    DECLARE @f_B4 uniqueidentifier = NEWID();
    DECLARE @f_B5 uniqueidentifier = NEWID();
    DECLARE @f_B6 uniqueidentifier = NEWID();

    -- ART capacity page
    IF NOT EXISTS (SELECT 1 FROM dbo.AppPages WHERE Id = 28)
    BEGIN
        SET IDENTITY_INSERT dbo.AppPages ON;
        INSERT INTO dbo.AppPages (Id, [Key], DisplayName, [Group], IsAdminOnly, SortOrder, ScopeMode)
        VALUES (28, N'ArtCapacity', N'ART capacity', NULL, 0, 102, 2);
        SET IDENTITY_INSERT dbo.AppPages OFF;
    END;

    -- users
    DECLARE @f_DevName nvarchar(256), @f_DevSam nvarchar(256), @f_DevEmpId nvarchar(50), @f_DevEmail nvarchar(256), @f_DevCreated datetime2;
    IF EXISTS (SELECT 1 FROM dbo.AppUsers WHERE WindowsUserName = @Dev AND Id BETWEEN 101 AND 109)
    BEGIN
        SELECT @f_DevName = DisplayName, @f_DevSam = SamAccountName, @f_DevEmpId = EmployeeId, @f_DevEmail = EmailAddress, @f_DevCreated = CreatedAt
        FROM dbo.AppUsers WHERE WindowsUserName = @Dev;
        DELETE dbo.AppUsers WHERE WindowsUserName = @Dev;
    END;

    SET IDENTITY_INSERT dbo.AppUsers ON;
    INSERT INTO dbo.AppUsers (Id, WindowsUserName, DisplayName, SamAccountName, EmployeeId, EmailAddress, IsAdmin, IsApproved, IsAccessRequested,
        RequestedAt, RequestedDepartmentId, RequestedCapitalProjectId, RequestComment, ApprovedAt, ApprovedBy, CreatedAt) VALUES
        (101, N'CORP\amorgan', N'Alice Morgan', N'amorgan', N'E01000001', N'a.morgan@example.com', 0, 1, 0,
            DATEADD(minute, -107950, @NowUtc), 1, 1, N'Scrum Master of Argon, I plan the team capacity and holidays',
            DATEADD(minute, -106520, @NowUtc), @Dev, DATEADD(minute, -107950, @NowUtc)),
        (102, N'CORP\bcarter', N'Ben Carter', N'bc01000002', NULL, N'b.carter@example.com', 0, 1, 0,
            NULL, NULL, NULL, NULL,
            DATEADD(minute, -100710, @NowUtc), @Dev, DATEADD(minute, -100710, @NowUtc)),
        (103, N'CORP\cnovak', N'Chloe Novak', N'cnovak', N'E01000003', N'c.novak@example.com', 0, 1, 0,
            DATEADD(minute, -86530, @NowUtc), NULL, NULL, NULL,
            DATEADD(minute, -85020, @NowUtc), N'CORP\former.lead', DATEADD(minute, -86530, @NowUtc)),
        (104, N'CORP\dprice', N'Daniel Price', N'dprice', N'E01000004', N'd.price@example.com', 0, 1, 0,
            NULL, NULL, NULL, NULL,
            DATEADD(minute, -64610, @NowUtc), @Dev, DATEADD(minute, -64610, @NowUtc)),
        (105, N'CORP\ehorak', N'Eva Horak', N'ehorak', N'E01000005', N'e.horak@example.com', 0, 1, 0,
            DATEADD(minute, -57840, @NowUtc), 2, 3, N'Fermi Scrum Master, need to see Risk Analytics plans and the risk register',
            DATEADD(minute, -54930, @NowUtc), @Dev, DATEADD(minute, -57840, @NowUtc)),
        (106, N'CORP\fkhan', N'Farid Khan', N'fkhan', N'E01000006', N'f.khan@example.com', 0, 0, 1,
            DATEADD(minute, -2890, @NowUtc), 1, 2, N'Product Owner of Delta, need to edit Mobile Banking features',
            NULL, NULL, DATEADD(minute, -2890, @NowUtc)),
        (107, N'CORP\gliu', NULL, NULL, NULL, NULL, 0, 0, 1,
            DATEADD(minute, -312, @NowUtc), NULL, NULL, NULL,
            NULL, NULL, DATEADD(minute, -312, @NowUtc)),
        (108, N'CORP\former.lead', N'Former Lead', N'former.lead', NULL, N'former.lead@example.com', 0, 0, 0,
            NULL, NULL, NULL, NULL,
            DATEADD(minute, -259300, @NowUtc), @Dev, DATEADD(minute, -259300, @NowUtc)),
        (109, N'CORP\pmo.office', N'PMO Office', N'pmo.office', NULL, N'pmo.office@example.com', 0, 1, 0,
            NULL, NULL, NULL, NULL,
            DATEADD(minute, -43170, @NowUtc), @Dev, DATEADD(minute, -43170, @NowUtc));
    SET IDENTITY_INSERT dbo.AppUsers OFF;

    IF EXISTS (SELECT 1 FROM dbo.AppUsers WHERE WindowsUserName = @Dev)
        UPDATE dbo.AppUsers
        SET IsAdmin = 1, IsApproved = 1, IsAccessRequested = 0, DisplayName = ISNULL(DisplayName, N'Local Developer')
        WHERE WindowsUserName = @Dev;
    ELSE
        INSERT INTO dbo.AppUsers (WindowsUserName, DisplayName, SamAccountName, EmployeeId, EmailAddress, IsAdmin, IsApproved, IsAccessRequested, ApprovedAt, ApprovedBy, CreatedAt)
        VALUES (@Dev, ISNULL(@f_DevName, N'Local Developer'), ISNULL(@f_DevSam, SUBSTRING(@Dev, CHARINDEX(N'\', @Dev) + 1, 256)), @f_DevEmpId, @f_DevEmail,
            1, 1, 0, @NowUtc, N'System', ISNULL(@f_DevCreated, @NowUtc));

    -- profiles
    SET IDENTITY_INSERT dbo.Profiles ON;
    INSERT INTO dbo.Profiles (Id, [Name], Description, CreatedAt, CreatedBy, ModifiedAt, ModifiedBy, RestrictTeamEditByRole) VALUES
        (1, N'Viewer (all ARTs)', N'Read-only access to every planning and resource page for all ARTs',
            DATEADD(minute, -172830, @NowUtc), N'CORP\former.lead', DATEADD(minute, -21540, @NowUtc), @Dev, 1),
        (2, N'Core Payments Editor', N'Edits Core Payments planning, teams and resources and the risk register; reads Mobile Banking',
            DATEADD(minute, -143950, @NowUtc), @Dev, DATEADD(minute, -17360, @NowUtc), @Dev, 1),
        (3, N'PI Planner', N'Maintains PIs, features, team planning, risks, feature snapshots and hygiene rules across all ARTs',
            DATEADD(minute, -129480, @NowUtc), @Dev, NULL, NULL, 0),
        (4, N'Override test', NULL,
            DATEADD(minute, -86290, @NowUtc), @Dev, DATEADD(minute, -10110, @NowUtc), @Dev, 1),
        (5, N'Risk Analytics Governance', N'Reads Risk Analytics; edits the risk register; keeps the Risk Analytics snapshots and hygiene rules',
            DATEADD(minute, -71950, @NowUtc), @Dev, NULL, NULL, 1),
        (6, N'Unused profile', N'Not assigned to anyone', @f_T3, @Dev, NULL, NULL, 1),
        (7, N'Team planning (no role restriction)', N'Edits capacity, holidays, coefficients and sprints of every Core Payments team, whatever the team role',
            DATEADD(minute, -9870, @NowUtc), @Dev, NULL, NULL, 0);
    SET IDENTITY_INSERT dbo.Profiles OFF;

    DROP TABLE IF EXISTS #f_Page;
    CREATE TABLE #f_Page (PageId int NOT NULL PRIMARY KEY, IsTrain bit NOT NULL, SortOrder int NOT NULL);
    INSERT INTO #f_Page (PageId, IsTrain, SortOrder)
    SELECT Id, CASE WHEN ScopeMode = 2 THEN 1 ELSE 0 END, SortOrder
    FROM dbo.AppPages
    WHERE IsAdminOnly = 0 AND ScopeMode IN (1, 2) AND Id IN (2, 5, 6, 7, 8, 9, 10, 11, 12, 13, 19, 20, 21, 22, 26, 27, 28, 29, 31);

    DROP TABLE IF EXISTS #f_Perm;
    CREATE TABLE #f_Perm (ProfileId int NOT NULL, AppPageId int NOT NULL, CapitalProjectId int NULL, AccessLevel int NOT NULL);
    INSERT INTO #f_Perm (ProfileId, AppPageId, CapitalProjectId, AccessLevel)
    SELECT 1, PageId, NULL, 1 FROM #f_Page
    UNION ALL SELECT 2, PageId, 1, 2 FROM #f_Page WHERE IsTrain = 1
    UNION ALL SELECT 2, PageId, 2, 1 FROM #f_Page WHERE IsTrain = 1
    UNION ALL SELECT 2, PageId, NULL, CASE WHEN PageId = 26 THEN 2 ELSE 1 END FROM #f_Page WHERE PageId IN (2, 12, 13, 26)
    UNION ALL SELECT 3, PageId, NULL, 2 FROM #f_Page WHERE PageId IN (2, 9, 19, 20, 21, 22, 26, 27, 29)
    UNION ALL SELECT 4, PageId, 1, 2 FROM #f_Page WHERE PageId = 9
    UNION ALL SELECT 4, PageId, 3, 2 FROM #f_Page WHERE PageId = 9
    UNION ALL SELECT 4, PageId, NULL, 0 FROM #f_Page WHERE PageId = 9
    UNION ALL SELECT 5, PageId, 3, CASE WHEN PageId IN (27, 29) THEN 2 ELSE 1 END FROM #f_Page WHERE IsTrain = 1
    UNION ALL SELECT 5, PageId, NULL, 2 FROM #f_Page WHERE PageId = 26
    UNION ALL SELECT 6, PageId, NULL, 1 FROM #f_Page WHERE PageId IN (12, 13)
    UNION ALL SELECT 7, PageId, 1, 2 FROM #f_Page WHERE PageId IN (19, 20, 21, 22);

    SET IDENTITY_INSERT dbo.ProfilePagePermissions ON;
    INSERT INTO dbo.ProfilePagePermissions (Id, ProfileId, AppPageId, CapitalProjectId, AccessLevel)
    SELECT ROW_NUMBER() OVER (ORDER BY x.ProfileId, p.SortOrder, x.AppPageId, ISNULL(x.CapitalProjectId, 0)),
        x.ProfileId, x.AppPageId, x.CapitalProjectId, x.AccessLevel
    FROM #f_Perm AS x
    JOIN #f_Page AS p ON p.PageId = x.AppPageId;
    SET IDENTITY_INSERT dbo.ProfilePagePermissions OFF;

    SET IDENTITY_INSERT dbo.ProfileActionPermissions ON;
    INSERT INTO dbo.ProfileActionPermissions (Id, ProfileId, ActionKey, IsAvailable) VALUES
        (1, 2, N'MapToPi', 1),
        (2, 2, N'EditLockedPi', 1),
        (3, 3, N'JiraSync', 1),
        (4, 3, N'JiraSyncFiltered', 1),
        (5, 5, N'MapToPi', 0);
    SET IDENTITY_INSERT dbo.ProfileActionPermissions OFF;

    INSERT INTO dbo.AppUserProfiles (AppUserId, ProfileId) VALUES
        (101, 2), (102, 1), (102, 7), (103, 2), (103, 3), (104, 4), (105, 5), (109, 1), (109, 3);

    -- global filters and message
    SET IDENTITY_INSERT dbo.UserGlobalFilters ON;
    INSERT INTO dbo.UserGlobalFilters (Id, WindowsUserName, DepartmentId, CapitalProjectIds, TeamIds, PiIds, UpdatedAt) VALUES
        (1, N'CORP\amorgan', 1, N'1', NULL, N'3', DATEADD(minute, -14420, @NowUtc)),
        (2, N'CORP\pmo.office', NULL, N'1,9999', NULL, NULL, DATEADD(minute, -28870, @NowUtc));
    SET IDENTITY_INSERT dbo.UserGlobalFilters OFF;

    SET IDENTITY_INSERT dbo.GlobalMessages ON;
    INSERT INTO dbo.GlobalMessages (Id, IsEnabled, MessageType, AllowDismiss, [Text], UpdatedAt, UpdatedBy)
    VALUES (1, 1, 1, 1, N'Local TEST DATA loaded by scripts/test-data.sql - not production data.', @NowUtc, @Dev);
    SET IDENTITY_INSERT dbo.GlobalMessages OFF;

    -- backup history and settings
    DECLARE @f_BkDay datetime2 = CAST(CAST(@NowUtc AS date) AS datetime2);
    DECLARE @f_BkFolder nvarchar(500) = ISNULL((SELECT TOP (1) NULLIF(RTRIM(BackupFolderPath), N'') FROM dbo.BackupSettings ORDER BY Id), N'D:\SqlBackups\Estimation');
    SET @f_BkFolder = CASE WHEN RIGHT(@f_BkFolder, 1) = N'\' THEN LEFT(@f_BkFolder, LEN(@f_BkFolder) - 1) ELSE @f_BkFolder END;
    IF NOT EXISTS (SELECT 1 FROM dbo.BackupHistory)
    BEGIN
        DROP TABLE IF EXISTS #f_Bk;
        CREATE TABLE #f_Bk (Seq int NOT NULL PRIMARY KEY, StartedAt datetime2 NOT NULL, Ms int NOT NULL, [Status] nvarchar(20) NOT NULL, Size bigint NULL, Msg nvarchar(2000) NULL, TriggeredBy nvarchar(100) NOT NULL);
        INSERT INTO #f_Bk (Seq, StartedAt, Ms, [Status], Size, Msg, TriggeredBy) VALUES
            (1, DATEADD(second, 3602, DATEADD(day, -27, @f_BkDay)), 11960, N'Success', 1835008, N'Completed in 11.8s', N'Scheduler'),
            (2, DATEADD(second, 3602, DATEADD(day, -24, @f_BkDay)), 12470, N'Success', 1871872, N'Completed in 12.3s', N'Scheduler'),
            (3, DATEADD(second, 33277, DATEADD(day, -20, @f_BkDay)), 410, N'Failed', NULL, NULL, @Dev),
            (4, DATEADD(second, 33890, DATEADD(day, -20, @f_BkDay)), 25280, N'Success', 1908736, N'Completed and verified in 25.1s', @Dev),
            (5, DATEADD(second, 3602, DATEADD(day, -13, @f_BkDay)), 13590, N'Success', 1966080, N'Completed in 13.4s', N'Scheduler'),
            (6, DATEADD(second, 3601, DATEADD(day, -9, @f_BkDay)), 4020, N'Cancelled', NULL,
                N'Cancelled before the backup finished, normally because the application was shutting down. Check the backup folder on the SQL Server to see whether the file completed.', N'Scheduler'),
            (7, DATEADD(second, 3602, DATEADD(day, -6, @f_BkDay)), 13080, N'Success', 2031616, N'Completed in 12.9s', N'Scheduler'),
            (8, DATEADD(second, 52411, DATEADD(day, -2, @f_BkDay)), 14290, N'Success', 2097152, N'Completed in 14.1s', @Dev);

        INSERT INTO dbo.BackupHistory (StartedAt, FinishedAt, [Status], FilePath, FileSizeBytes, Message, TriggeredBy)
        SELECT b.StartedAt, DATEADD(millisecond, b.Ms, b.StartedAt), b.[Status],
            CASE WHEN b.[Status] = N'Success' THEN LEFT(CONCAT(@f_BkFolder, N'\', DB_NAME(), N'_', CONVERT(nchar(8), b.StartedAt, 112), N'_', REPLACE(CONVERT(nchar(8), b.StartedAt, 108), N':', N''), N'Z.bak'), 500) END,
            b.Size,
            CASE WHEN b.[Status] = N'Failed'
                THEN CONCAT(N'Cannot open backup device ''X:\Backups\Estimation\', DB_NAME(), N'_', CONVERT(nchar(8), b.StartedAt, 112), N'_', REPLACE(CONVERT(nchar(8), b.StartedAt, 108), N':', N''), N'Z.bak''. Operating system error 3(The system cannot find the path specified.).')
                ELSE b.Msg END,
            b.TriggeredBy
        FROM #f_Bk AS b
        ORDER BY b.StartedAt;

        UPDATE dbo.BackupSettings SET LastBackupAt = (SELECT MAX(FinishedAt) FROM dbo.BackupHistory WHERE [Status] = N'Success')
        WHERE LastBackupAt IS NULL;
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.BackupSettings)
        INSERT INTO dbo.BackupSettings (Enabled, BackupFolderPath, ScheduleType, IntervalHours, DailyTime, WeeklyDay, RetentionCount, RetentionDays, RetentionMaxTotalGb, VerifyAfterBackup, LastBackupAt, NextBackupAt)
        VALUES (0, N'', 0, 24, '02:00', 0, 7, 0, 0, 0, (SELECT MAX(FinishedAt) FROM dbo.BackupHistory WHERE [Status] = N'Success'), NULL);

    -- audit log
    DECLARE @f_FeatName nvarchar(300) = (SELECT LEFT(COALESCE(NULLIF(LTRIM(RTRIM([Name])), N''), Summary), 300) FROM dbo.Features WHERE Id = 101);
    DECLARE @f_RiskSummary nvarchar(300) = N'Duplicate of the screening vendor API delay risk';

    INSERT INTO dbo.AuditLogs (EntityName, EntityId, [Action], PropertyName, EntityDisplayName, BatchId, OldValue, NewValue, [Timestamp], UserName)
    SELECT a.EntityName, a.EntityId, a.[Action], a.PropertyName, a.EntityDisplayName, a.BatchId, a.OldValue, a.NewValue, a.Ts, a.UserName
    FROM (VALUES
        (1, N'JiraSyncSettings', N'-2147482647', N'Create', NULL, NULL, @f_B1, NULL,
            N'{"Id":-2147482647,"CycleCooldownMinutes":5,"Enabled":false,"JiraTimeZoneId":null,"LabelsSyncEnabled":true,"LastRunAt":null,"NextRunAt":null,"ServiceAccountUserName":"__jira-sync-service__"}',
            @f_T1, N'System'),
        (2, N'Art', N'1', N'Update', N'DepartmentId', N'Core Payments', @f_B2, NULL, N'1', @f_T2, N'CORP\amorgan'),
        (3, N'Art', N'1', N'Update', N'Description', N'Core Payments', @f_B2, N'Payment hub and instant payments', N'Payment hub, instant payments and card processing', @f_T2, N'CORP\amorgan'),
        (4, N'Profile', N'-2147482647', N'Create', NULL, N'Unused profile', @f_B3, NULL,
            CONCAT(N'{"Id":-2147482647,"CreatedAt":"', CONVERT(nvarchar(33), @f_T3, 126), N'Z","CreatedBy":"', @f_DevJson,
                N'","Description":"Not assigned to anyone","ModifiedAt":null,"ModifiedBy":null,"Name":"Unused profile","RestrictTeamEditByRole":true}'),
            @f_T3, @Dev),
        (5, N'ProfilePagePermission', N'-2147482647', N'Create', NULL, NULL, @f_B4, NULL,
            N'{"Id":-2147482647,"AccessLevel":1,"AppPageId":12,"CapitalProjectId":null,"ProfileId":6}', @f_T4, @Dev),
        (6, N'ProfilePagePermission', N'-2147482646', N'Create', NULL, NULL, @f_B4, NULL,
            N'{"Id":-2147482646,"AccessLevel":1,"AppPageId":13,"CapitalProjectId":null,"ProfileId":6}', @f_T4, @Dev),
        (7, N'Risk', N'99', N'Delete', NULL, @f_RiskSummary, @f_B5,
            CONCAT(N'{"Id":99,"Category":0,"CreatedBy":"CORP\\ehorak","DateRaised":"', CONVERT(nchar(10), DATEADD(day, 9, @Pi2Start), 23),
                N'T00:00:00","DateUpdated":"', CONVERT(nvarchar(33), @f_RiskUpdated, 126),
                N'","DueBy":null,"Owner":"Eva Horak","PiId":2,"Severity":1,"Status":2,"Summary":"', @f_RiskSummary, N'","UpdatedBy":"CORP\\ehorak"}'),
            NULL, @f_T5, @Dev),
        (8, N'Feature', N'101', N'Update', N'ModifiedAt', @f_FeatName, @f_B6, FORMAT(DATEADD(second, -412870, @f_T6), N'dd-MMM-yy HH:mm:ss', N'en-US'),
            FORMAT(@f_T6, N'dd-MMM-yy HH:mm:ss', N'en-US'), @f_T6, N'CORP\cnovak'),
        (9, N'Feature', N'101', N'Update', N'Status', @f_FeatName, @f_B6, N'To Do', N'In Progress', @f_T6, N'CORP\cnovak'),
        (10, N'Feature', N'101', N'Update', N'StoryPoints', @f_FeatName, @f_B6, N'5', N'8', @f_T6, N'CORP\cnovak')
    ) AS a (Seq, EntityName, EntityId, [Action], PropertyName, EntityDisplayName, BatchId, OldValue, NewValue, Ts, UserName)
    ORDER BY a.Seq;


    -- 70-artplanning: ART capacity settings (skill rankings, level allocations, role coefficients, role skill limits, train coefficients)
    SET @Section = N'70-artplanning';
    PRINT N'ART planning: ART capacity settings for Core Payments (Default, PI 2, PI 3), Risk Analytics and Data Platform (Default)';

    IF OBJECT_ID(N'dbo.ArtPlanningSkillRankings', N'U') IS NOT NULL
    BEGIN
        -- skill rankings
        EXEC (N'
        SET IDENTITY_INSERT dbo.ArtPlanningSkillRankings ON;
        INSERT INTO dbo.ArtPlanningSkillRankings (Id, CapitalProjectId, PiId, SkillId, Ranking) VALUES
            (1001, 1, NULL, 1, 1), (1002, 1, NULL, 5, 2), (1003, 1, NULL, 10, 3), (1004, 1, NULL, 7, 4),
            (1005, 1, NULL, 8, 5), (1006, 1, NULL, 9, 6), (1007, 1, NULL, 12, 7), (1008, 1, NULL, 3, 8),
            (1009, 1, NULL, 4, 9), (1010, 1, NULL, 6, 10), (1011, 1, NULL, 15, 11),
            (1201, 1, 2, 1, 1), (1202, 1, 2, 2, 2), (1203, 1, 2, 5, 3), (1204, 1, 2, 10, 4),
            (1205, 1, 2, 7, 5), (1206, 1, 2, 8, 6), (1207, 1, 2, 9, 7), (1208, 1, 2, 12, 8),
            (1209, 1, 2, 3, 9), (1210, 1, 2, 4, 10), (1211, 1, 2, 6, 11), (1212, 1, 2, 15, 12),
            (3001, 3, NULL, 11, 1), (3002, 3, NULL, 6, 2), (3003, 3, NULL, 2, 3), (3004, 3, NULL, 10, 4),
            (3005, 3, NULL, 7, 5), (3006, 3, NULL, 8, 6), (3007, 3, NULL, 9, 7), (3008, 3, NULL, 12, 8),
            (3009, 3, NULL, 15, 9),
            (5001, 5, NULL, 12, 1), (5002, 5, NULL, 6, 2), (5003, 5, NULL, 2, 3), (5004, 5, NULL, 9, 4),
            (5005, 5, NULL, 7, 5), (5006, 5, NULL, 8, 6), (5007, 5, NULL, 10, 7), (5008, 5, NULL, 11, 8);
        SET IDENTITY_INSERT dbo.ArtPlanningSkillRankings OFF;');

        -- skill level allocations
        EXEC (N'
        SET IDENTITY_INSERT dbo.ArtPlanningSkillLevelAllocations ON;
        INSERT INTO dbo.ArtPlanningSkillLevelAllocations (Id, CapitalProjectId, PiId, SkillLevelValue, Ratio, UsageOrder) VALUES
            (1001, 1, NULL, 4, 1.0000, 1), (1002, 1, NULL, 3, 0.8500, 2), (1003, 1, NULL, 2, 0.6000, 3),
            (1201, 1, 2, 4, 1.0000, 1), (1202, 1, 2, 3, 0.8000, 2), (1203, 1, 2, 2, 0.5000, 3),
            (1301, 1, 3, 4, 1.0000, 1), (1302, 1, 3, 3, 0.8000, 2), (1303, 1, 3, 2, 0.5000, 3), (1304, 1, 3, 1, 0.2500, 4),
            (3001, 3, NULL, 4, 1.0000, 1), (3002, 3, NULL, 3, 0.8000, 2), (3003, 3, NULL, 2, 0.6000, 3), (3004, 3, NULL, 1, 0.0000, 4),
            (5001, 5, NULL, 4, 1.0000, 1), (5002, 5, NULL, 3, 0.8000, 2), (5003, 5, NULL, 2, 0.6000, 3), (5004, 5, NULL, 1, 0.3000, 4);
        SET IDENTITY_INSERT dbo.ArtPlanningSkillLevelAllocations OFF;');

        -- role coefficients
        EXEC (N'
        SET IDENTITY_INSERT dbo.ArtPlanningRoleCoefficients ON;
        INSERT INTO dbo.ArtPlanningRoleCoefficients (Id, CapitalProjectId, PiId, TeamRoleId, Coefficient) VALUES
            (1001, 1, NULL, 1, 0.5000), (1002, 1, NULL, 2, 0.3000), (1003, 1, NULL, 3, 0.8000), (1004, 1, NULL, 4, 1.0000),
            (1201, 1, 2, 1, 0.4000), (1202, 1, 2, 2, 0.2500), (1203, 1, 2, 3, 0.7500), (1204, 1, 2, 4, 1.0000), (1205, 1, 2, 5, 0.9000),
            (3001, 3, NULL, 1, 0.5000), (3002, 3, NULL, 2, 0.4000), (3003, 3, NULL, 3, 0.7500),
            (5001, 5, NULL, 1, 0.6000), (5002, 5, NULL, 2, 0.4000), (5003, 5, NULL, 3, 0.9000);
        SET IDENTITY_INSERT dbo.ArtPlanningRoleCoefficients OFF;');

        -- role skill limits
        EXEC (N'
        SET IDENTITY_INSERT dbo.ArtPlanningRoleSkillLimits ON;
        INSERT INTO dbo.ArtPlanningRoleSkillLimits (Id, CapitalProjectId, PiId, TeamRoleId, SkillId) VALUES
            (1001, 1, NULL, 5, 7), (1002, 1, NULL, 5, 8),
            (1201, 1, 2, 5, 7);
        SET IDENTITY_INSERT dbo.ArtPlanningRoleSkillLimits OFF;');

        -- train coefficients
        EXEC (N'
        SET IDENTITY_INSERT dbo.ArtPlanningTrainCoefficients ON;
        INSERT INTO dbo.ArtPlanningTrainCoefficients (Id, CapitalProjectId, PiId, Coefficient) VALUES
            (1001, 1, NULL, 1.0000), (1201, 1, 2, 0.9500), (1301, 1, 3, 0.9000),
            (3001, 3, NULL, 0.8500);
        SET IDENTITY_INSERT dbo.ArtPlanningTrainCoefficients OFF;');
    END
    ELSE
        PRINT N'ArtPlanning tables not found (start the app once to create them): ART capacity settings skipped.';


    SET @Section = N'99-footer';
    COMMIT TRANSACTION;

    PRINT N'Test data loaded.';
    SELECT t.TableName, t.[Rows]
    FROM (VALUES
        (N'Pis', (SELECT COUNT(*) FROM dbo.Pis)),
        (N'CapitalProjects', (SELECT COUNT(*) FROM dbo.CapitalProjects)),
        (N'CapitalProjectSprints', (SELECT COUNT(*) FROM dbo.CapitalProjectSprints)),
        (N'StrategicObjectives', (SELECT COUNT(*) FROM dbo.StrategicObjectives)),
        (N'PortfolioEpics', (SELECT COUNT(*) FROM dbo.PortfolioEpics)),
        (N'BusinessOutcomes', (SELECT COUNT(*) FROM dbo.BusinessOutcomes)),
        (N'Features', (SELECT COUNT(*) FROM dbo.Features)),
        (N'ArtPrioritizationOrders', (SELECT COUNT(*) FROM dbo.ArtPrioritizationOrders)),
        (N'Teams', (SELECT COUNT(*) FROM dbo.Teams)),
        (N'HumanResources', (SELECT COUNT(*) FROM dbo.HumanResources)),
        (N'Sprints', (SELECT COUNT(*) FROM dbo.Sprints)),
        (N'Holidays', (SELECT COUNT(*) FROM dbo.Holidays)),
        (N'PublicHolidays', (SELECT COUNT(*) FROM dbo.PublicHolidays)),
        (N'IssueLinks', (SELECT COUNT(*) FROM dbo.IssueLinks)),
        (N'FeatureSnapshots', (SELECT COUNT(*) FROM dbo.FeatureSnapshots)),
        (N'FeatureHygieneRules', (SELECT COUNT(*) FROM dbo.FeatureHygieneRules)),
        (N'Risks', (SELECT COUNT(*) FROM dbo.Risks)),
        (N'AppUsers', (SELECT COUNT(*) FROM dbo.AppUsers)),
        (N'Profiles', (SELECT COUNT(*) FROM dbo.Profiles))
    ) AS t (TableName, [Rows]);
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;
    PRINT CONCAT(N'Test data load FAILED in section ', @Section, N'. Nothing was changed.');
    THROW;
END CATCH;
