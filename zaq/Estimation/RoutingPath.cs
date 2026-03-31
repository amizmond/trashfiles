namespace Estimation;

public static class RoutingPath
{
    public const string Base = "";

    // Planning
    public const string Features = $"{Base}/features";
    public const string FeaturesNew = $"{Features}/new";
    public const string FeaturesEdit = $"{Features}/{{Id:int}}";
    public const string FeaturesUpload = $"{Features}/upload";

    public const string CapitalProjects = $"{Base}/capital-projects";
    public const string CapitalProjectsNew = $"{CapitalProjects}/new";
    public const string CapitalProjectsEdit = $"{CapitalProjects}/{{Id:int}}";

    public const string ProjectPrograms = $"{Base}/project-programs";
    public const string ProjectProgramsNew = $"{ProjectPrograms}/new";
    public const string ProjectProgramsEdit = $"{ProjectPrograms}/{{Id:int}}";

    public const string PortfolioEpics = $"{Base}/portfolio-epics";
    public const string PortfolioEpicsNew = $"{PortfolioEpics}/new";
    public const string PortfolioEpicsEdit = $"{PortfolioEpics}/{{Id:int}}";

    public const string BusinessOutcomes = $"{Base}/business-outcomes";
    public const string BusinessOutcomesNew = $"{BusinessOutcomes}/new";
    public const string BusinessOutcomesEdit = $"{BusinessOutcomes}/{{Id:int}}";

    public const string CapitalSolutionTrainPriorities = $"{Base}/capital-solution-train-priorities";

    public const string Pis = $"{Base}/pis";
    public const string PisNew = $"{Pis}/new";
    public const string PisEdit = $"{Pis}/{{Id:int}}";

    public const string PiPrioritization = $"{Base}/pi-prioritization";

    // Resources
    public const string Teams = $"{Base}/teams";
    public const string TeamsNew = $"{Teams}/new";
    public const string TeamsEdit = $"{Teams}/{{Id:int}}";

    public const string HumanResources = $"{Base}/human-resources";
    public const string HumanResourcesNew = $"{HumanResources}/new";
    public const string HumanResourcesEdit = $"{HumanResources}/{{Id:int}}";

    public const string Skills = $"{Base}/skills";
    public const string SkillsNew = $"{Skills}/new";
    public const string SkillsEdit = $"{Skills}/{{Id:int}}";

    public const string TechnologyStacks = $"{Base}/technology-stacks";
    public const string TechnologyStacksNew = $"{TechnologyStacks}/new";
    public const string TechnologyStacksEdit = $"{TechnologyStacks}/{{Id:int}}";

    // Settings
    public const string StaticSettings = $"{Base}/static-settings";
    public const string Upload = $"{Base}/upload";
}
