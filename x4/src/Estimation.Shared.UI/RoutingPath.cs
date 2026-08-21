namespace Estimation;

public static class RoutingPath
{
    public const string Base = "";

    // Planning
    public const string Departments = $"{Base}/departments";
    public const string DepartmentsNew = $"{Departments}/new";
    public const string DepartmentsEdit = $"{Departments}/{{Id:int}}";

    public const string Features = $"{Base}/features";
    public const string FeaturesNew = $"{Features}/new";
    public const string FeaturesEdit = $"{Features}/{{Id:int}}";
    public const string FeaturesUploadPreview = $"{Features}/upload-preview";
    public const string FeatureSnapshots = $"{Features}/snapshots";
    public const string FeatureSnapshotsDelta = $"{FeatureSnapshots}/delta";
    public const string FeatureChangeReviews = $"{FeatureSnapshots}/reviews"; // ReviewRounds
    public const string FeatureChangeReviewDetail = $"{FeatureChangeReviews}/detail"; // ReviewRounds

    public const string CapitalProjects = $"{Base}/capital-projects";
    public const string CapitalProjectsNew = $"{CapitalProjects}/new";
    public const string CapitalProjectsEdit = $"{CapitalProjects}/{{Id:int}}";

    public const string StrategicObjectives = $"{Base}/strategic-objectives";
    public const string StrategicObjectivesNew = $"{StrategicObjectives}/new";
    public const string StrategicObjectivesEdit = $"{StrategicObjectives}/{{Id:int}}";

    public const string PortfolioEpics = $"{Base}/portfolio-epics";
    public const string PortfolioEpicsNew = $"{PortfolioEpics}/new";
    public const string PortfolioEpicsEdit = $"{PortfolioEpics}/{{Id:int}}";

    public const string BusinessOutcomes = $"{Base}/business-outcomes";
    public const string BusinessOutcomesNew = $"{BusinessOutcomes}/new";
    public const string BusinessOutcomesEdit = $"{BusinessOutcomes}/{{Id:int}}";

    public const string Pis = $"{Base}/pis";
    public const string PisNew = $"{Pis}/new";
    public const string PisEdit = $"{Pis}/{{Id:int}}";

    // Resources
    public const string Teams = $"{Base}/teams";
    public const string TeamsNew = $"{Teams}/new";
    public const string TeamsEdit = $"{Teams}/{{Id:int}}";
    public const string TeamsUploadPreview = $"{Teams}/{{Id:int}}/upload-preview";
    public const string TeamsUploadBulkPreview = $"{Teams}/upload-preview";

    public const string HumanResources = $"{Base}/human-resources";
    public const string HumanResourcesNew = $"{HumanResources}/new";
    public const string HumanResourcesEdit = $"{HumanResources}/{{Id:int}}";
    public const string HumanResourcesUploadPreview = $"{HumanResources}/upload-preview";

    public const string Skills = $"{Base}/skills";
    public const string SkillsNew = $"{Skills}/new";
    public const string SkillsEdit = $"{Skills}/{{Id:int}}";
    public const string SkillsUploadPreview = $"{Skills}/upload-preview";

    public const string TechnologyStacks = $"{Base}/technology-stacks";
    public const string TechnologyStacksNew = $"{TechnologyStacks}/new";
    public const string TechnologyStacksEdit = $"{TechnologyStacks}/{{Id:int}}";

    public const string MasterSheet = $"{Base}/master-sheet";
    public const string MasterSheetUploadPreview = $"{MasterSheet}/upload-preview";

    public const string Risks = $"{Base}/risks";

    // Audit
    public const string AuditLog = $"{Base}/audit-log";

    // Settings
    public const string StaticSettings = $"{Base}/static-settings";

    // Authorization
    public const string Authorization = $"{Base}/authorization";

    // Profiles (admin)
    public const string Profiles = $"{Base}/profiles";
    public const string ProfilesNew = $"{Profiles}/new";
    public const string ProfilesEdit = $"{Profiles}/{{Id:int}}";

    // Database backup
    public const string DatabaseBackup = $"{Base}/database-backup";

    // Jira background sync (admin)
    public const string JiraSync = $"{Base}/jira-sync";

    // Holidays & Sprints
    public const string TeamHolidayPlanner = $"{Teams}/{{Id:int}}/holidays";
    public const string TeamSprints = $"{Teams}/{{Id:int}}/sprints";
    public const string TeamSprintDetail = $"{TeamSprints}/{{SprintId:int}}";
    public const string TeamCoefficients = $"{Teams}/{{Id:int}}/coefficients";
    public const string TeamCapacity = $"{Teams}/{{Id:int}}/capacity";

    // Holidays planner setup (admin)
    public const string HolidaysSetup = $"{Base}/holidays-setup";
    public const string PublicHolidaysUploadPreview = $"{HolidaysSetup}/upload-preview";

    // Self-service vacations (for users who are not authorized to the app but exist in HR)
    public const string MyVacations = $"{Base}/my-vacations";
    public const string MyTeamHolidays = $"{MyVacations}/{{TeamId:int}}";

    // Estimation poker
    public const string Poker = $"{Base}/poker";
    public const string PokerNew = $"{Poker}/new";
    public const string PokerRoom = $"{Poker}/room/{{RoomId:guid}}";

    // Team planning (team selected via dropdown instead of route)
    public const string TeamPlanning = $"{Base}/team-planning";
    public const string TeamPlanningCapacity = $"{TeamPlanning}/capacity";
    public const string TeamPlanningHolidays = $"{TeamPlanning}/holidays";
    public const string TeamPlanningCoefficients = $"{TeamPlanning}/coefficients";
    public const string TeamPlanningSprints = $"{TeamPlanning}/sprints";
    public const string TeamPlanningSprintDetail = $"{TeamPlanningSprints}/{{TeamId:int}}/{{SprintId:int}}";
}
