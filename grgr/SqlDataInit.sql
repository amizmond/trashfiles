-- ============================================================
-- Test Data for PI Capacity Check feature
-- Uses TechnologyStacks with the following definitions:
--   Front-end:  Angular, C#/.Net, JavaScript, HTML, SQL
--   Streams:    C#/.Net, MongoDB, Kafka, SQL
--   BA:         R, Python, SQL
--   DB_Core:    SQL, C#/.Net
--
-- Covers:
--   - Assigned team with matching TechnologyStack (Priority 1)
--   - Matching team fallback (Priority 2)
--   - Capacity shortage (RED) and fully reserved (GREEN)
-- ============================================================

-- ============================================================
-- Reference / Lookup Tables
-- ============================================================

-- Countries
SET IDENTITY_INSERT [dbo].[Countries] ON;

INSERT INTO [dbo].[Countries] ([Id], [Name], [Description]) VALUES
(1001, 'United States',  'USA'),
(1002, 'Germany',        'DE'),
(1003, 'India',          'IN'),
(1004, 'United Kingdom', 'UK'),
(1005, 'Poland',         'PL');

SET IDENTITY_INSERT [dbo].[Countries] OFF;

-- Cities
SET IDENTITY_INSERT [dbo].[Cities] ON;

INSERT INTO [dbo].[Cities] ([Id], [Name], [Description]) VALUES
(1001, 'New York',      'NY, USA'),
(1002, 'San Francisco', 'CA, USA'),
(1003, 'Berlin',        'Germany'),
(1004, 'Bangalore',     'India'),
(1005, 'London',        'UK'),
(1006, 'Warsaw',        'Poland'),
(1007, 'Chicago',       'IL, USA');

SET IDENTITY_INSERT [dbo].[Cities] OFF;

-- Employee Categories
SET IDENTITY_INSERT [dbo].[EmployeeCategories] ON;

INSERT INTO [dbo].[EmployeeCategories] ([Id], [Name], [Description]) VALUES
(1001, 'Internal',   'Full-time internal employee'),
(1002, 'Contractor', 'External contractor'),
(1003, 'Consultant', 'External consultant');

SET IDENTITY_INSERT [dbo].[EmployeeCategories] OFF;

-- Employee Types
SET IDENTITY_INSERT [dbo].[EmployeeTypes] ON;

INSERT INTO [dbo].[EmployeeTypes] ([Id], [Name], [Description]) VALUES
(1001, 'Full-Time',  'Full-time employee'),
(1002, 'Part-Time',  'Part-time employee'),
(1003, 'Temporary',  'Temporary / fixed-term');

SET IDENTITY_INSERT [dbo].[EmployeeTypes] OFF;

-- Employee Vendors
SET IDENTITY_INSERT [dbo].[EmployeeVendors] ON;

INSERT INTO [dbo].[EmployeeVendors] ([Id], [Name], [Description]) VALUES
(1001, 'Accenture',  'Accenture consulting'),
(1002, 'Infosys',    'Infosys technologies'),
(1003, 'Capgemini',  'Capgemini engineering');

SET IDENTITY_INSERT [dbo].[EmployeeVendors] OFF;

-- Employee Roles
SET IDENTITY_INSERT [dbo].[EmployeeRoles] ON;

INSERT INTO [dbo].[EmployeeRoles] ([Id], [Name], [Description]) VALUES
(1001, 'Developer',       'Software developer'),
(1002, 'Tech Lead',       'Technical lead'),
(1003, 'Architect',       'Solution architect'),
(1004, 'QA Engineer',     'Quality assurance'),
(1005, 'DevOps Engineer', 'DevOps / SRE');

SET IDENTITY_INSERT [dbo].[EmployeeRoles] OFF;

-- Corporate Grades
SET IDENTITY_INSERT [dbo].[CorporateGrades] ON;

INSERT INTO [dbo].[CorporateGrades] ([Id], [Name], [Description]) VALUES
(1001, 'L1', 'Entry level'),
(1002, 'L2', 'Mid level'),
(1003, 'L3', 'Senior level'),
(1004, 'L4', 'Staff / Principal'),
(1005, 'L5', 'Director level');

SET IDENTITY_INSERT [dbo].[CorporateGrades] OFF;

-- Unfunded Options
SET IDENTITY_INSERT [dbo].[UnfundedOptions] ON;

INSERT INTO [dbo].[UnfundedOptions] ([Id], [Name], [Description], [Order]) VALUES
(1001, 'Funded',           'Fully funded work item',     1),
(1002, 'Partially Funded', 'Partially funded work item', 2),
(1003, 'Unfunded',         'Not yet funded',             3);

SET IDENTITY_INSERT [dbo].[UnfundedOptions] OFF;

-- ============================================================
-- Skills (10 skills)
-- ============================================================
SET IDENTITY_INSERT [dbo].[Skills] ON;

INSERT INTO [dbo].[Skills] ([Id], [Name], [Description], [Created], [Updated]) VALUES
(1001, 'C#/.Net',     'C# and .NET development',     GETDATE(), NULL),
(1002, 'JavaScript',  'JavaScript / TypeScript',      GETDATE(), NULL),
(1003, 'SQL',         'SQL Server / relational DB',   GETDATE(), NULL),
(1004, 'Python',      'Python scripting & ML',        GETDATE(), NULL),
(1005, 'DevOps',      'CI/CD and infrastructure',     GETDATE(), NULL),
(1006, 'Angular',     'Angular framework',            GETDATE(), NULL),
(1007, 'HTML',        'HTML / CSS markup',            GETDATE(), NULL),
(1008, 'MongoDB',     'MongoDB NoSQL database',       GETDATE(), NULL),
(1009, 'Kafka',       'Apache Kafka messaging',       GETDATE(), NULL),
(1010, 'R',           'R statistical programming',    GETDATE(), NULL);

SET IDENTITY_INSERT [dbo].[Skills] OFF;

-- Skill Levels: 3 levels per skill (Junior=1, Mid=2, Senior=3)
SET IDENTITY_INSERT [dbo].[SkillLevels] ON;

INSERT INTO [dbo].[SkillLevels] ([Id], [SkillId], [Name], [Value], [Description]) VALUES
-- C#/.Net (1001)
(1001, 1001, 'Junior', 1, 'Junior level'),
(1002, 1001, 'Mid',    2, 'Mid level'),
(1003, 1001, 'Senior', 3, 'Senior level'),
-- JavaScript (1002)
(1004, 1002, 'Junior', 1, 'Junior level'),
(1005, 1002, 'Mid',    2, 'Mid level'),
(1006, 1002, 'Senior', 3, 'Senior level'),
-- SQL (1003)
(1007, 1003, 'Junior', 1, 'Junior level'),
(1008, 1003, 'Mid',    2, 'Mid level'),
(1009, 1003, 'Senior', 3, 'Senior level'),
-- Python (1004)
(1010, 1004, 'Junior', 1, 'Junior level'),
(1011, 1004, 'Mid',    2, 'Mid level'),
(1012, 1004, 'Senior', 3, 'Senior level'),
-- DevOps (1005)
(1013, 1005, 'Junior', 1, 'Junior level'),
(1014, 1005, 'Mid',    2, 'Mid level'),
(1015, 1005, 'Senior', 3, 'Senior level'),
-- Angular (1006)
(1016, 1006, 'Junior', 1, 'Junior level'),
(1017, 1006, 'Mid',    2, 'Mid level'),
(1018, 1006, 'Senior', 3, 'Senior level'),
-- HTML (1007)
(1019, 1007, 'Junior', 1, 'Junior level'),
(1020, 1007, 'Mid',    2, 'Mid level'),
(1021, 1007, 'Senior', 3, 'Senior level'),
-- MongoDB (1008)
(1022, 1008, 'Junior', 1, 'Junior level'),
(1023, 1008, 'Mid',    2, 'Mid level'),
(1024, 1008, 'Senior', 3, 'Senior level'),
-- Kafka (1009)
(1025, 1009, 'Junior', 1, 'Junior level'),
(1026, 1009, 'Mid',    2, 'Mid level'),
(1027, 1009, 'Senior', 3, 'Senior level'),
-- R (1010)
(1028, 1010, 'Junior', 1, 'Junior level'),
(1029, 1010, 'Mid',    2, 'Mid level'),
(1030, 1010, 'Senior', 3, 'Senior level');

SET IDENTITY_INSERT [dbo].[SkillLevels] OFF;

-- ============================================================
-- Technology Stacks
-- ============================================================
SET IDENTITY_INSERT [dbo].[TechnologyStacks] ON;

INSERT INTO [dbo].[TechnologyStacks] ([Id], [Name], [Description]) VALUES
(1001, 'Front-end', 'Frontend development stack'),
(1002, 'Streams',   'Event streaming and backend services'),
(1003, 'BA',        'Business analytics and data science'),
(1004, 'DB_Core',   'Core database and backend');

SET IDENTITY_INSERT [dbo].[TechnologyStacks] OFF;

-- TechnologyStack <-> Skill mappings
INSERT INTO [dbo].[TechnologyStackSkills] ([TechnologyStackId], [SkillId]) VALUES
-- Front-end: Angular, C#/.Net, JavaScript, HTML, SQL
(1001, 1006),  -- Angular
(1001, 1001),  -- C#/.Net
(1001, 1002),  -- JavaScript
(1001, 1007),  -- HTML
(1001, 1003),  -- SQL
-- Streams: C#/.Net, MongoDB, Kafka, SQL
(1002, 1001),  -- C#/.Net
(1002, 1008),  -- MongoDB
(1002, 1009),  -- Kafka
(1002, 1003),  -- SQL
-- BA: R, Python, SQL
(1003, 1010),  -- R
(1003, 1004),  -- Python
(1003, 1003),  -- SQL
-- DB_Core: SQL, C#/.Net
(1004, 1003),  -- SQL
(1004, 1001);  -- C#/.Net

-- ============================================================
-- Teams
-- ============================================================
SET IDENTITY_INSERT [dbo].[Teams] ON;

INSERT INTO [dbo].[Teams] ([Id], [Name], [FullName], [OptionalTeamTag], [Description]) VALUES
(1001, 'Alpha',   'Team Alpha - Frontend',    NULL, 'Frontend / GUI team'),
(1002, 'Bravo',   'Team Bravo - Streams',     NULL, 'Streaming and backend services'),
(1003, 'Charlie', 'Team Charlie - Analytics',  NULL, 'Business analytics team'),
(1004, 'Delta',   'Team Delta - FullStack',    NULL, 'Full-stack generalists');

SET IDENTITY_INSERT [dbo].[Teams] OFF;

-- Team <-> TechnologyStack
INSERT INTO [dbo].[TeamTechnologyStacks] ([TeamId], [TechnologyStackId]) VALUES
(1001, 1001),  -- Alpha   -> Front-end
(1002, 1002),  -- Bravo   -> Streams
(1002, 1004),  -- Bravo   -> DB_Core
(1003, 1003),  -- Charlie -> BA
(1004, 1001),  -- Delta   -> Front-end
(1004, 1004);  -- Delta   -> DB_Core

-- ============================================================
-- Human Resources (12 people)
-- ============================================================
SET IDENTITY_INSERT [dbo].[HumanResources] ON;

INSERT INTO [dbo].[HumanResources] ([Id], [IsActive], [EmployeeNumber], [EmployeeName], [FullName], [LineManagerName],
    [EmployeeCategoryId], [EmployeeTypeId], [EmployeeRoleId], [EmployeeVendorId], [CorporateGradeId], [CountryId], [CityId], [TeamRoleId]) VALUES
-- Team Alpha members                                                                                                        -- TeamRole: 1=PO, 2=SM, 3=FT
(1001, 1, 'E1001', 'Alice',  'Alice Johnson',   'Mike Spencer', 1001, 1001, 1002, NULL, 1003, 1001, 1001, 1),               -- Product Owner
(1002, 1, 'E1002', 'Bob',    'Bob Williams',    'Mike Spencer', 1001, 1001, 1001, NULL, 1002, 1001, 1002, 2),               -- Scrum Master
(1003, 1, 'E1003', 'Carol',  'Carol Davis',     'Mike Spencer', 1002, 1001, 1001, 1001, 1002, 1004, 1005, 3),               -- FT member
-- Team Bravo members
(1004, 1, 'E1004', 'Dave',   'Dave Martinez',   'Sarah Chen',   1001, 1001, 1002, NULL, 1003, 1002, 1003, 2),               -- Scrum Master
(1005, 1, 'E1005', 'Eve',    'Eve Anderson',    'Sarah Chen',   1001, 1001, 1001, NULL, 1002, 1001, 1007, 3),               -- FT member
(1006, 1, 'E1006', 'Frank',  'Frank Thomas',    'Sarah Chen',   1002, 1001, 1005, 1002, 1002, 1003, 1004, 1),               -- Product Owner
-- Team Charlie members
(1007, 1, 'E1007', 'Grace',  'Grace Wilson',    'Tom Reed',     1001, 1001, 1003, NULL, 1004, 1004, 1005, 1),               -- Product Owner
(1008, 1, 'E1008', 'Hank',   'Hank Taylor',     'Tom Reed',     1001, 1002, 1001, NULL, 1001, 1005, 1006, 3),               -- FT member
-- Team Delta members
(1009, 1, 'E1009', 'Ivy',    'Ivy Moore',       'Tom Reed',     1001, 1001, 1001, NULL, 1002, 1001, 1001, 3),               -- FT member
(1010, 1, 'E1010', 'Jack',   'Jack Brown',      'Tom Reed',     1003, 1001, 1001, 1003, 1002, 1002, 1003, 1),               -- Product Owner
(1011, 1, 'E1011', 'Karen',  'Karen Lee',       'Tom Reed',     1001, 1001, 1001, NULL, 1002, 1003, 1004, 2),               -- Scrum Master
-- Inactive employee (should be excluded from capacity)
(1012, 0, 'E1012', 'Leo',    'Leo Harris',      'Tom Reed',     1001, 1003, 1001, NULL, 1001, 1005, 1006, 3);               -- FT member

SET IDENTITY_INSERT [dbo].[HumanResources] OFF;

-- Team Members
INSERT INTO [dbo].[TeamMembers] ([TeamId], [HumanResourceId]) VALUES
-- Alpha: Alice, Bob, Carol
(1001, 1001), (1001, 1002), (1001, 1003),
-- Bravo: Dave, Eve, Frank
(1002, 1004), (1002, 1005), (1002, 1006),
-- Charlie: Grace, Hank
(1003, 1007), (1003, 1008),
-- Delta: Ivy, Jack, Karen, Leo(inactive)
(1004, 1009), (1004, 1010), (1004, 1011), (1004, 1012);

-- ============================================================
-- Human Resource Skills
-- ============================================================
-- SkillLevel IDs:
--   C#/.Net(1001):  Junior=1001, Mid=1002, Senior=1003
--   JS(1002):       Junior=1004, Mid=1005, Senior=1006
--   SQL(1003):      Junior=1007, Mid=1008, Senior=1009
--   Python(1004):   Junior=1010, Mid=1011, Senior=1012
--   DevOps(1005):   Junior=1013, Mid=1014, Senior=1015
--   Angular(1006):  Junior=1016, Mid=1017, Senior=1018
--   HTML(1007):     Junior=1019, Mid=1020, Senior=1021
--   MongoDB(1008):  Junior=1022, Mid=1023, Senior=1024
--   Kafka(1009):    Junior=1025, Mid=1026, Senior=1027
--   R(1010):        Junior=1028, Mid=1029, Senior=1030

INSERT INTO [dbo].[HumanResourceSkills] ([HumanResourceId], [SkillId], [SkillLevelId]) VALUES
-- ── Team Alpha (Front-end team) ──
-- Alice: C#/.Net Senior(3), JavaScript Mid(2), Angular Mid(2), HTML Mid(2)
(1001, 1001, 1003),  -- C#/.Net Senior
(1001, 1002, 1005),  -- JavaScript Mid
(1001, 1006, 1017),  -- Angular Mid
(1001, 1007, 1020),  -- HTML Mid
-- Bob: JavaScript Senior(3), Angular Senior(3), HTML Junior(1), C#/.Net Junior(1)
(1002, 1002, 1006),  -- JavaScript Senior
(1002, 1006, 1018),  -- Angular Senior
(1002, 1007, 1019),  -- HTML Junior
(1002, 1001, 1001),  -- C#/.Net Junior
-- Carol: C#/.Net Mid(2), JavaScript Junior(1), HTML Mid(2)
(1003, 1001, 1002),  -- C#/.Net Mid
(1003, 1002, 1004),  -- JavaScript Junior
(1003, 1007, 1020),  -- HTML Mid
-- Alpha Front-end capacity = Alice(3+2+2+2) + Bob(3+3+1+1) + Carol(2+1+2) = 9+8+5 = 22

-- ── Team Bravo (Streams + DB_Core team) ──
-- Dave: C#/.Net Senior(3), SQL Mid(2), Kafka Mid(2), MongoDB Junior(1)
(1004, 1001, 1003),  -- C#/.Net Senior
(1004, 1003, 1008),  -- SQL Mid
(1004, 1009, 1026),  -- Kafka Mid
(1004, 1008, 1022),  -- MongoDB Junior
-- Eve: C#/.Net Mid(2), SQL Senior(3), MongoDB Mid(2)
(1005, 1001, 1002),  -- C#/.Net Mid
(1005, 1003, 1009),  -- SQL Senior
(1005, 1008, 1023),  -- MongoDB Mid
-- Frank: SQL Mid(2), Kafka Senior(3)
(1006, 1003, 1008),  -- SQL Mid
(1006, 1009, 1027),  -- Kafka Senior
-- Bravo Streams capacity = Dave(3+2+2+1) + Eve(2+3+2) + Frank(2+3) = 8+7+5 = 20
-- Bravo DB_Core capacity = Dave(3+2) + Eve(2+3) + Frank(2) = 5+5+2 = 12

-- ── Team Charlie (BA team) ──
-- Grace: SQL Senior(3), Python Mid(2), R Senior(3)
(1007, 1003, 1009),  -- SQL Senior
(1007, 1004, 1011),  -- Python Mid
(1007, 1010, 1030),  -- R Senior
-- Hank: Python Senior(3), SQL Junior(1), R Mid(2)
(1008, 1004, 1012),  -- Python Senior
(1008, 1003, 1007),  -- SQL Junior
(1008, 1010, 1029),  -- R Mid
-- Charlie BA capacity = Grace(3+2+3) + Hank(3+1+2) = 8+6 = 14

-- ── Team Delta (Front-end + DB_Core) ──
-- Ivy: C#/.Net Mid(2), JavaScript Mid(2), Angular Junior(1), HTML Junior(1)
(1009, 1001, 1002),  -- C#/.Net Mid
(1009, 1002, 1005),  -- JavaScript Mid
(1009, 1006, 1016),  -- Angular Junior
(1009, 1007, 1019),  -- HTML Junior
-- Jack: C#/.Net Junior(1), SQL Mid(2)
(1010, 1001, 1001),  -- C#/.Net Junior
(1010, 1003, 1008),  -- SQL Mid
-- Karen: JavaScript Senior(3), Angular Mid(2), C#/.Net Mid(2)
(1011, 1002, 1006),  -- JavaScript Senior
(1011, 1006, 1017),  -- Angular Mid
(1011, 1001, 1002),  -- C#/.Net Mid
-- Leo (INACTIVE - excluded from capacity): C#/.Net Senior(3), JS Senior(3)
(1012, 1001, 1003),
(1012, 1002, 1006);
-- Delta Front-end capacity = Ivy(2+2+1+1) + Jack(1+2) + Karen(3+2+2) = 6+3+7 = 16
-- Delta DB_Core capacity = Ivy(2) + Jack(1+2) + Karen(2) = 2+3+2 = 7

-- ============================================================
-- Portfolio Epics
-- ============================================================
SET IDENTITY_INSERT [dbo].[PortfolioEpics] ON;

INSERT INTO [dbo].[PortfolioEpics] ([Id], [JiraId], [Name], [Description]) VALUES
(1001, 'PE-1001', 'Digital Transformation',  'Enterprise-wide digital transformation initiative'),
(1002, 'PE-1002', 'Customer Experience',     'Improve customer-facing platforms'),
(1003, 'PE-1003', 'Data-Driven Decisions',   'Enable data-driven business decisions');

SET IDENTITY_INSERT [dbo].[PortfolioEpics] OFF;

-- ============================================================
-- Capital Projects
-- ============================================================
SET IDENTITY_INSERT [dbo].[CapitalProjects] ON;

INSERT INTO [dbo].[CapitalProjects] ([Id], [JiraKey], [Name], [Description]) VALUES
(1001, 'RRWA',  'CAPEX-Platform',  'Platform modernization capital project'),
(1002, 'DATAX', 'CAPEX-Analytics', 'Data analytics capital project');

SET IDENTITY_INSERT [dbo].[CapitalProjects] OFF;

-- ============================================================
-- Strategic Objectives
-- ============================================================
SET IDENTITY_INSERT [dbo].[StrategicObjectives] ON;

INSERT INTO [dbo].[StrategicObjectives] ([Id], [JiraId], [Name], [Description]) VALUES
(1001, 'PGM-1001', 'Core Platform Program', 'Program for core platform modernization'),
(1002, 'PGM-1002', 'CX Program',            'Program for customer experience improvements'),
(1003, 'PGM-1003', 'Data Program',          'Program for data and analytics initiatives');

SET IDENTITY_INSERT [dbo].[StrategicObjectives] OFF;

-- Capital Project <-> Strategic Objective
INSERT INTO [dbo].[CapitalProjectStrategicObjectives] ([CapitalProjectId], [StrategicObjectiveId]) VALUES
(1001, 1001),
(1002, 1001),
(1002, 1002),
(1002, 1003);

-- Capital Project <-> Team
INSERT INTO [dbo].[CapitalProjectTeams] ([CapitalProjectId], [TeamId]) VALUES
(1001, 1001),
(1001, 1002),
(1002, 1003);

-- Strategic Objective <-> Portfolio Epic
INSERT INTO [dbo].[StrategicObjectivePortfolioEpics] ([StrategicObjectiveId], [PortfolioEpicId]) VALUES
(1001, 1001),
(1002, 1002),
(1003, 1003);

-- ============================================================
-- Business Outcomes
-- ============================================================
SET IDENTITY_INSERT [dbo].[BusinessOutcomes] ON;

INSERT INTO [dbo].[BusinessOutcomes] ([Id], [JiraId], [Name], [Description], [PortfolioEpicId]) VALUES
(1001, 'BO-1001', 'Platform Modernization', 'Modernize core platform components', 1001),
(1002, 'BO-1002', 'Customer Portal',        'New customer self-service portal',    1002),
(1003, 'BO-1003', 'Data Analytics',          'Advanced analytics capabilities',     1003),
(1004, 'BO-1004', 'Security Hardening',      'Improve platform security posture',   1001),
(1005, 'BO-1005', 'Mobile Experience',       'Mobile-first customer experience',    1002);

SET IDENTITY_INSERT [dbo].[BusinessOutcomes] OFF;

-- ============================================================
-- Planning Increments
-- ============================================================
SET IDENTITY_INSERT [dbo].[Pis] ON;

INSERT INTO [dbo].[Pis] ([Id], [Name], [Description], [Priority], [StartDate], [EndDate]) VALUES
(1001, 'PI-2026-Q2', 'Q2 2026 Planning Increment', '1', '2026-04-01', '2026-06-30'),
(1002, 'PI-2026-Q3', 'Q3 2026 Planning Increment', '2', '2026-07-01', '2026-09-30'),
(1003, 'PI-2026-Q4', 'Q4 2026 Planning Increment', '3', '2026-10-01', '2026-12-31');

SET IDENTITY_INSERT [dbo].[Pis] OFF;

-- ============================================================
-- Features
-- ============================================================
SET IDENTITY_INSERT [dbo].[Features] ON;

INSERT INTO [dbo].[Features] ([Id], [ProjectKey], [JiraId], [Name], [Description], [Comments], [Ranking], [PiId], [BusinessOutcomeId]) VALUES
-- ── PI-2026-Q2 (PI 1001) ──
(1001, 'RRWA', 'FEAT-1001', 'Redesign Login Page',
    'Redesign the login page with Angular components', 'High priority', 1, 1001, 1001),
(1002, 'RRWA', 'FEAT-1002', 'API Gateway Refactor',
    'Refactor API gateway for streaming and DB performance', NULL, 2, 1001, 1001),
(1003, 'RRWA', 'FEAT-1003', 'Customer Dashboard',
    'Build interactive customer dashboard', 'Large front-end effort', 3, 1001, 1002),
(1004, 'RRWA', 'FEAT-1004', 'Data Export Service',
    'Export service for analytics data', NULL, 4, 1001, 1003),
(1005, 'RRWA', 'FEAT-1005', 'Email Notifications',
    'Implement email notification via streaming', 'No team assigned', 5, 1001, 1002),
(1006, 'RRWA', 'FEAT-1006', 'Admin Panel',
    'Admin panel with front-end and DB work', NULL, 6, 1001, 1001),
(1007, 'RRWA', 'FEAT-1007', 'Full Platform Rewrite',
    'Complete platform rewrite - very large effort', 'Will exceed capacity', 7, 1001, 1001),
(1008, 'RRWA', 'FEAT-1008', 'Documentation Update',
    'Update product documentation', 'No dev effort', 8, 1001, 1002),

-- ── PI-2026-Q3 (PI 1002) ──
(1009, 'RRWA', 'FEAT-1009', 'Mobile API Layer',
    'Build REST API for mobile app', NULL, 1, 1002, 1004),
(1010, 'RRWA', 'FEAT-1010', 'Auth Service Migration',
    'Migrate authentication to OAuth2/OIDC', 'Security initiative', 2, 1002, 1004),
(1011, 'RRWA', 'FEAT-1011', 'Real-Time Notifications',
    'WebSocket-based push notifications', NULL, 3, 1002, 1002),
(1012, 'RRWA', 'FEAT-1012', 'ML Recommendation Engine',
    'Product recommendation engine using ML', 'Data team lead', 4, 1002, 1003),

-- ── PI-2026-Q4 (PI 1003) ──
(1013, 'RRWA', 'FEAT-1013', 'Self-Service Portal v2',
    'Next-gen customer portal with Angular', NULL, 1, 1003, 1005),
(1014, 'RRWA', 'FEAT-1014', 'Automated Testing Framework',
    'CI/CD test automation infrastructure', NULL, 2, 1003, 1004),
(1015, 'RRWA', 'FEAT-1015', 'Data Lake Integration',
    'Integrate with enterprise data lake', 'Cross-team effort', 3, 1003, 1003),
(1016, 'RRWA', 'FEAT-1016', 'Performance Monitoring Dashboard',
    'APM dashboard for operations', NULL, 4, 1003, 1001);

SET IDENTITY_INSERT [dbo].[Features] OFF;

-- ============================================================
-- Feature <-> Team assignments
-- ============================================================
INSERT INTO [dbo].[FeatureTeams] ([FeatureId], [TeamId]) VALUES
-- PI-2026-Q2
(1001, 1001),  -- Login Page     -> Alpha
(1002, 1002),  -- API Gateway    -> Bravo
(1003, 1001),  -- Dashboard      -> Alpha
(1003, 1004),  -- Dashboard      -> Delta
(1004, 1003),  -- Data Export    -> Charlie
-- FEAT-1005: no team assigned (tests Priority 2 MatchingTeam fallback)
(1006, 1001),  -- Admin Panel    -> Alpha
(1006, 1002),  -- Admin Panel    -> Bravo
(1007, 1001),  -- Rewrite        -> Alpha
(1007, 1002),  -- Rewrite        -> Bravo
(1007, 1004),  -- Rewrite        -> Delta
-- PI-2026-Q3
(1009, 1002),  -- Mobile API     -> Bravo
(1009, 1004),  -- Mobile API     -> Delta
(1010, 1002),  -- Auth Migration -> Bravo
(1011, 1001),  -- Notifications  -> Alpha
(1011, 1004),  -- Notifications  -> Delta
(1012, 1003),  -- ML Engine      -> Charlie
-- PI-2026-Q4
(1013, 1001),  -- Portal v2      -> Alpha
(1013, 1004),  -- Portal v2      -> Delta
(1014, 1002),  -- Test Framework -> Bravo
(1015, 1003),  -- Data Lake      -> Charlie
(1016, 1001),  -- Monitoring     -> Alpha
(1016, 1002);  -- Monitoring     -> Bravo

-- ============================================================
-- Labels
-- ============================================================
SET IDENTITY_INSERT [dbo].[Labels] ON;

INSERT INTO [dbo].[Labels] ([Id], [Name]) VALUES
(1001, 'frontend'),
(1002, 'backend'),
(1003, 'security'),
(1004, 'performance'),
(1005, 'ux'),
(1006, 'data'),
(1007, 'infrastructure'),
(1008, 'mobile');

SET IDENTITY_INSERT [dbo].[Labels] OFF;

-- ============================================================
-- FeatureLabels (Feature <-> Label)
-- ============================================================
INSERT INTO [dbo].[FeatureLabels] ([FeatureId], [LabelId]) VALUES
(1001, 1001),  -- Login Page       -> frontend
(1001, 1005),  -- Login Page       -> ux
(1002, 1002),  -- API Gateway      -> backend
(1002, 1004),  -- API Gateway      -> performance
(1003, 1001),  -- Dashboard        -> frontend
(1003, 1005),  -- Dashboard        -> ux
(1004, 1006),  -- Data Export      -> data
(1005, 1002),  -- Email Notif      -> backend
(1006, 1001),  -- Admin Panel      -> frontend
(1006, 1002),  -- Admin Panel      -> backend
(1007, 1001),  -- Full Rewrite     -> frontend
(1007, 1002),  -- Full Rewrite     -> backend
(1007, 1007),  -- Full Rewrite     -> infrastructure
(1009, 1008),  -- Mobile API       -> mobile
(1009, 1002),  -- Mobile API       -> backend
(1010, 1003),  -- Auth Migration   -> security
(1012, 1006),  -- ML Engine        -> data
(1013, 1001),  -- Portal v2        -> frontend
(1013, 1008),  -- Portal v2        -> mobile
(1014, 1007),  -- Test Framework   -> infrastructure
(1015, 1006),  -- Data Lake        -> data
(1016, 1004);  -- Monitoring       -> performance

-- ============================================================
-- FeatureTechnologyStacks (Feature -> TechnologyStack with EstimatedEffort)
-- ============================================================
SET IDENTITY_INSERT [dbo].[FeatureTechnologyStacks] ON;

INSERT INTO [dbo].[FeatureTechnologyStacks] ([Id], [FeatureId], [TechnologyStackId], [EstimatedEffort]) VALUES
-- ── PI-2026-Q2 ──

-- FEAT-1001 (Rank 1): Login Page -> Front-end=10
-- Assigned: Alpha(FE=22). Reserve 10 from Alpha. Alpha FE=12. GREEN.
(1001, 1001, 1001, 10),

-- FEAT-1002 (Rank 2): API Gateway -> Streams=8, DB_Core=5
-- Assigned: Bravo(Streams=20, DB_Core=12). Reserve 8 Streams + 5 DB_Core. GREEN.
(1002, 1002, 1002, 8),
(1003, 1002, 1004, 5),

-- FEAT-1003 (Rank 3): Dashboard -> Front-end=15
-- Assigned: Alpha(FE=12), Delta(FE=16). Reserve 12 from Alpha + 3 from Delta. GREEN.
(1004, 1003, 1001, 15),

-- FEAT-1004 (Rank 4): Data Export -> BA=6, DB_Core=4
-- Assigned: Charlie(BA=14). BA=6 from Charlie. DB_Core: Charlie has no DB_Core -> Priority 2: Bravo(7), reserve 4. GREEN.
(1005, 1004, 1003, 6),
(1006, 1004, 1004, 4),

-- FEAT-1005 (Rank 5): Email Notifications -> Streams=10
-- No assigned team with Streams... wait, assigned to Bravo which HAS Streams.
-- Actually let me not assign any team to FEAT-1005 to test Priority 2 fallback.
-- Bravo Streams=12 remaining. Reserve 10. Bravo Streams=2. GREEN.
(1007, 1005, 1002, 10),

-- FEAT-1006 (Rank 6): Admin Panel -> Front-end=8, DB_Core=3
-- Assigned: Alpha(FE=0), Bravo(no FE). FE: Priority 2 -> Delta(FE=13), reserve 8. Delta FE=5.
-- DB_Core: Alpha(no DB_Core), Bravo(DB_Core=3). Reserve 3 from Bravo. Bravo DB_Core=0. GREEN.
(1008, 1006, 1001, 8),
(1009, 1006, 1004, 3),

-- FEAT-1007 (Rank 7): Full Platform Rewrite -> FE=30, Streams=25, BA=20, DB_Core=15
-- Remaining: Alpha FE=0, Bravo Streams=2 DB_Core=0, Charlie BA=8, Delta FE=5 DB_Core=7
-- FE: assigned Alpha(0)+Delta(5)=5 reserved of 30. SHORTAGE.
-- Streams: assigned Bravo(2)=2 reserved of 25. SHORTAGE.
-- BA: no assigned BA team. Matching: Charlie(8). 8 of 20. SHORTAGE.
-- DB_Core: assigned Bravo(0)+Delta(7)=7 reserved of 15. SHORTAGE.
-- Total reserved: 22 / Total required: 90. RED.
(1010, 1007, 1001, 30),
(1011, 1007, 1002, 25),
(1012, 1007, 1003, 20),
(1013, 1007, 1004, 15),

-- FEAT-1008 (Rank 8): Documentation -> no tech stacks. GREEN.

-- ── PI-2026-Q3 ──

-- FEAT-1009 (Rank 1): Mobile API -> Streams=6, DB_Core=3
(1014, 1009, 1002, 6),
(1015, 1009, 1004, 3),

-- FEAT-1010 (Rank 2): Auth Migration -> Streams=5, DB_Core=4
(1016, 1010, 1002, 5),
(1017, 1010, 1004, 4),

-- FEAT-1011 (Rank 3): Real-Time Notifications -> Front-end=8
(1018, 1011, 1001, 8),

-- FEAT-1012 (Rank 4): ML Recommendation Engine -> BA=10
(1019, 1012, 1003, 10),

-- ── PI-2026-Q4 ──

-- FEAT-1013 (Rank 1): Portal v2 -> Front-end=12
(1020, 1013, 1001, 12),

-- FEAT-1014 (Rank 2): Test Framework -> Streams=4, DB_Core=3
(1021, 1014, 1002, 4),
(1022, 1014, 1004, 3),

-- FEAT-1015 (Rank 3): Data Lake -> BA=8, DB_Core=5
(1023, 1015, 1003, 8),
(1024, 1015, 1004, 5),

-- FEAT-1016 (Rank 4): Monitoring Dashboard -> Front-end=6, Streams=4
(1025, 1016, 1001, 6),
(1026, 1016, 1002, 4);

SET IDENTITY_INSERT [dbo].[FeatureTechnologyStacks] OFF;

-- ============================================================
-- Summary: Team Capacities (per TechnologyStack)
-- ============================================================
--   Alpha:   Front-end = 22
--   Bravo:   Streams = 20,  DB_Core = 12
--   Charlie: BA = 14
--   Delta:   Front-end = 16,  DB_Core = 7
--
-- ============================================================
-- Expected PI-2026-Q2 Capacity Results (processing by Ranking)
-- ============================================================
--
-- Rank 1 - FEAT-1001 (Login Page): Front-end=10
--   Assigned Alpha(FE=22) -> reserve 10. Alpha FE=12.
--   Result: FULLY RESERVED (GREEN)
--
-- Rank 2 - FEAT-1002 (API Gateway): Streams=8, DB_Core=5
--   Assigned Bravo -> Streams: reserve 8 (Bravo Streams=12).
--                  -> DB_Core: reserve 5 (Bravo DB_Core=7).
--   Result: FULLY RESERVED (GREEN)
--
-- Rank 3 - FEAT-1003 (Dashboard): Front-end=15
--   Assigned Alpha(FE=12) -> reserve 12. Need 3 more.
--   Assigned Delta(FE=16) -> reserve 3. Delta FE=13.
--   Alpha FE=0.
--   Result: FULLY RESERVED (GREEN)
--
-- Rank 4 - FEAT-1004 (Data Export): BA=6, DB_Core=4
--   Assigned Charlie(BA=14) -> BA: reserve 6. Charlie BA=8.
--   Charlie has no DB_Core -> Priority 2: Bravo(DB_Core=7) -> reserve 4. Bravo DB_Core=3.
--   Result: FULLY RESERVED (GREEN)
--
-- Rank 5 - FEAT-1005 (Email Notifications): Streams=10
--   No assigned teams. Priority 2: Bravo(Streams=12) -> reserve 10. Bravo Streams=2.
--   Result: FULLY RESERVED (GREEN)
--
-- Rank 6 - FEAT-1006 (Admin Panel): Front-end=8, DB_Core=3
--   Assigned Alpha(FE=0) -> 0. Bravo has no FE.
--   Priority 2: Delta(FE=13) -> reserve 8. Delta FE=5.
--   DB_Core: Alpha has no DB_Core. Bravo(DB_Core=3) -> reserve 3. Bravo DB_Core=0.
--   Result: FULLY RESERVED (GREEN)
--
-- Rank 7 - FEAT-1007 (Full Platform Rewrite): FE=30, Streams=25, BA=20, DB_Core=15
--   Remaining: Alpha FE=0 | Bravo Streams=2, DB_Core=0 | Charlie BA=8 | Delta FE=5, DB_Core=7
--   FE=30:     assigned Alpha(0)+Delta(5)=5 reserved. SHORTAGE (5/30).
--   Streams=25: assigned Bravo(2)=2 reserved. SHORTAGE (2/25).
--   BA=20:     no assigned. Matching Charlie(8). SHORTAGE (8/20).
--   DB_Core=15: assigned Bravo(0)+Delta(7)=7 reserved. SHORTAGE (7/15).
--   Total: 22 reserved / 90 required.
--   Result: NOT FULLY RESERVED (RED)
--
-- Rank 8 - FEAT-1008 (Documentation): No tech stacks.
--   Result: FULLY RESERVED (GREEN)
--
-- Summary: Features 1001-1006, 1008 = GREEN | Feature 1007 = RED
-- ============================================================

PRINT 'Test data inserted successfully.';
PRINT 'PI-2026-Q2: Features 1001-1006 and 1008 = GREEN, Feature 1007 = RED';
PRINT 'PI-2026-Q3: Features 1009-1012';
PRINT 'PI-2026-Q4: Features 1013-1016';
PRINT '';
PRINT 'Hierarchy:';
PRINT '  CAPEX-Platform  -> PGM-1001 (Core Platform) -> PE-1001 (Digital Transformation) -> BO-1001, BO-1004';
PRINT '  CAPEX-Analytics -> PGM-1001 (Core Platform) -> PE-1001 (Digital Transformation) -> BO-1001, BO-1004';
PRINT '  CAPEX-Analytics -> PGM-1002 (CX Program)    -> PE-1002 (Customer Experience)    -> BO-1002, BO-1005';
PRINT '  CAPEX-Analytics -> PGM-1003 (Data Program)  -> PE-1003 (Data-Driven Decisions)  -> BO-1003';
PRINT '';
PRINT 'Technology Stacks:';
PRINT '  Front-end: Angular, C#/.Net, JavaScript, HTML, SQL';
PRINT '  Streams:   C#/.Net, MongoDB, Kafka, SQL';
PRINT '  BA:        R, Python, SQL';
PRINT '  DB_Core:   SQL, C#/.Net';
PRINT '';
PRINT 'Team Capacities:';
PRINT '  Alpha:   Front-end=22';
PRINT '  Bravo:   Streams=20, DB_Core=12';
PRINT '  Charlie: BA=14';
PRINT '  Delta:   Front-end=16, DB_Core=7';
