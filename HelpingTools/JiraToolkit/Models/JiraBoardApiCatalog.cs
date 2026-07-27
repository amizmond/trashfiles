namespace JiraToolkit.Models;

/// <summary>
/// Declarative catalog of the Jira Agile "board" REST endpoints surfaced on the Boards page.
/// Paths follow the Jira Software REST API (https://developer.atlassian.com/cloud/jira/software/rest/api-group-board/).
/// </summary>
public static class JiraBoardApiCatalog
{
    private const string Agile = "rest/agile/1.0";
    private const string Software = "rest/software/1.0";

    public static IReadOnlyList<JiraApiDefinition> All { get; } = new List<JiraApiDefinition>
    {
        new()
        {
            Id = "get-all-boards",
            Name = "Get all boards",
            Description = "Returns all boards visible to the user. Optionally filtered by name, type or project.",
            PathTemplate = $"{Agile}/board",
            Parameters = new List<JiraApiParameter>
            {
                StartAt(),
                MaxResults(),
                new()
                {
                    Name = "type",
                    Type = JiraApiParamType.Select,
                    Options = new[] { "scrum", "kanban", "simple" },
                    Description = "Filter by board type."
                },
                new()
                {
                    Name = "name",
                    Placeholder = "Team Alpha",
                    Description = "Case-insensitive substring match on the board name."
                },
                new()
                {
                    Name = "projectKeyOrId",
                    Placeholder = "PROJ",
                    Description = "Only boards whose location is this project."
                },
                new()
                {
                    Name = "accountIdLocation",
                    Description = "Only boards located on this user account (Cloud)."
                },
                new()
                {
                    Name = "projectLocation",
                    Placeholder = "PROJ",
                    Description = "Only boards located in this project."
                },
                new()
                {
                    Name = "includePrivate",
                    Type = JiraApiParamType.Boolean,
                    Description = "Include private boards the user has access to."
                },
                new()
                {
                    Name = "negateLocationFiltering",
                    Type = JiraApiParamType.Boolean,
                    Description = "Invert the location filters above (returns boards NOT in that location)."
                },
                new()
                {
                    Name = "orderBy",
                    Placeholder = "name",
                    Description = "Order of the returned boards. Supported value: name."
                },
                new()
                {
                    Name = "filterId",
                    Type = JiraApiParamType.Number,
                    Description = "Only boards backed by this saved filter."
                },
                Expand("Comma-separated list of fields to expand, e.g. admins,permissions.")
            }
        },

        new()
        {
            Id = "get-board-by-filter-id",
            Name = "Get board by filter id",
            Description = "Returns any boards that are backed by the given saved filter.",
            PathTemplate = $"{Agile}/board/filter/{{filterId}}",
            Parameters = new List<JiraApiParameter>
            {
                new()
                {
                    Name = "filterId",
                    Kind = JiraApiParamKind.Path,
                    Type = JiraApiParamType.Number,
                    Required = true,
                    Placeholder = "10000",
                    Description = "Id of the saved filter the board is based on."
                },
                StartAt(),
                MaxResults()
            }
        },

        new()
        {
            Id = "get-board",
            Name = "Get board",
            Description = "Returns a single board — id, name, type and location.",
            PathTemplate = $"{Agile}/board/{{boardId}}",
            Parameters = new List<JiraApiParameter> { BoardId() }
        },

        new()
        {
            Id = "get-backlog-issues",
            Name = "Get issues for backlog",
            Description = "Returns all issues from the board's backlog. Issues already in a sprint are excluded.",
            PathTemplate = $"{Agile}/board/{{boardId}}/backlog",
            Parameters = new List<JiraApiParameter>
            {
                BoardId(),
                StartAt(),
                MaxResults(),
                Jql(),
                ValidateQuery(),
                Fields(),
                Expand()
            }
        },

        new()
        {
            Id = "get-backlog-approximate-count",
            Name = "Get approximate issue count for backlog",
            Description = "Returns an approximate count of issues in the board's backlog — much cheaper than paging the backlog.",
            PathTemplate = $"{Software}/board/{{boardId}}/backlog/approximate-count",
            Note = "Jira Cloud only, and note the /rest/software/1.0 base path (not /rest/agile/1.0).",
            Parameters = new List<JiraApiParameter> { BoardId() }
        },

        new()
        {
            Id = "get-configuration",
            Name = "Get configuration",
            Description = "Returns the board configuration: filter, column mapping, estimation and ranking fields, sub-query.",
            PathTemplate = $"{Agile}/board/{{boardId}}/configuration",
            Parameters = new List<JiraApiParameter> { BoardId() }
        },

        new()
        {
            Id = "get-epics",
            Name = "Get epics",
            Description = "Returns all epics from the board, for the given board id.",
            PathTemplate = $"{Agile}/board/{{boardId}}/epic",
            Parameters = new List<JiraApiParameter>
            {
                BoardId(),
                StartAt(),
                MaxResults(),
                new()
                {
                    Name = "done",
                    Type = JiraApiParamType.Boolean,
                    Description = "Filter by epic completion state. Leave unset to return both."
                }
            }
        },

        new()
        {
            Id = "get-epic-issues",
            Name = "Get board issues for epic",
            Description = "Returns all issues that belong to the given epic and are visible on the board.",
            PathTemplate = $"{Agile}/board/{{boardId}}/epic/{{epicId}}/issue",
            Note = "Use epicId = none to list board issues that have no epic.",
            Parameters = new List<JiraApiParameter>
            {
                BoardId(),
                new()
                {
                    Name = "epicId",
                    Kind = JiraApiParamKind.Path,
                    Required = true,
                    Placeholder = "10015 or none",
                    Description = "Epic id, or the literal none for issues without an epic."
                },
                StartAt(),
                MaxResults(),
                Jql(),
                ValidateQuery(),
                Fields(),
                Expand()
            }
        },

        new()
        {
            Id = "get-features",
            Name = "Get features for board",
            Description = "Returns the agile features (backlog, sprints, estimation, ...) and their toggle state for the board.",
            PathTemplate = $"{Agile}/board/{{boardId}}/features",
            Parameters = new List<JiraApiParameter> { BoardId() }
        },

        new()
        {
            Id = "get-board-issues",
            Name = "Get issues for board",
            Description = "Returns all issues on the board — from the backlog and from every sprint, subject to the board filter.",
            PathTemplate = $"{Agile}/board/{{boardId}}/issue",
            Parameters = new List<JiraApiParameter>
            {
                BoardId(),
                StartAt(),
                MaxResults(),
                Jql(),
                ValidateQuery(),
                Fields(),
                Expand()
            }
        },

        new()
        {
            Id = "get-quick-filters",
            Name = "Get all quick filters",
            Description = "Returns all quick filters configured on the board, in the order they appear.",
            PathTemplate = $"{Agile}/board/{{boardId}}/quickfilter",
            Parameters = new List<JiraApiParameter>
            {
                BoardId(),
                StartAt(),
                MaxResults()
            }
        },

        new()
        {
            Id = "get-sprints",
            Name = "Get all sprints",
            Description = "Returns all sprints on the board, optionally filtered by state.",
            PathTemplate = $"{Agile}/board/{{boardId}}/sprint",
            Note = "Only Scrum boards have sprints — a Kanban board returns 400.",
            Parameters = new List<JiraApiParameter>
            {
                BoardId(),
                StartAt(),
                MaxResults(),
                new()
                {
                    Name = "state",
                    Placeholder = "active,closed",
                    Description = "Comma-separated sprint states: future, active, closed."
                }
            }
        },

        new()
        {
            Id = "get-sprint-issues",
            Name = "Get board issues for sprint",
            Description = "Returns all issues in the given sprint that are visible on the board.",
            PathTemplate = $"{Agile}/board/{{boardId}}/sprint/{{sprintId}}/issue",
            Parameters = new List<JiraApiParameter>
            {
                BoardId(),
                new()
                {
                    Name = "sprintId",
                    Kind = JiraApiParamKind.Path,
                    Type = JiraApiParamType.Number,
                    Required = true,
                    Placeholder = "42",
                    Description = "Id of the sprint (see \"Get all sprints\")."
                },
                StartAt(),
                MaxResults(),
                Jql(),
                ValidateQuery(),
                Fields(),
                Expand()
            }
        },

        new()
        {
            Id = "get-versions",
            Name = "Get all versions",
            Description = "Returns all versions from the projects the board is located in.",
            PathTemplate = $"{Agile}/board/{{boardId}}/version",
            Parameters = new List<JiraApiParameter>
            {
                BoardId(),
                StartAt(),
                MaxResults(),
                new()
                {
                    Name = "released",
                    Type = JiraApiParamType.Boolean,
                    Description = "Filter by release state. Leave unset to return both."
                }
            }
        }
    };

    private static JiraApiParameter BoardId() => new()
    {
        Name = "boardId",
        Kind = JiraApiParamKind.Path,
        Type = JiraApiParamType.Number,
        Required = true,
        Placeholder = "123",
        Description = "Id of the board (see \"Get all boards\")."
    };

    private static JiraApiParameter StartAt() => new()
    {
        Name = "startAt",
        Type = JiraApiParamType.Number,
        Default = "0",
        Description = "Zero-based index of the first item to return."
    };

    private static JiraApiParameter MaxResults() => new()
    {
        Name = "maxResults",
        Type = JiraApiParamType.Number,
        Default = "50",
        Description = "Maximum number of items per page. Jira may cap this."
    };

    private static JiraApiParameter Jql() => new()
    {
        Name = "jql",
        Placeholder = "status = \"In Progress\" ORDER BY rank",
        Description = "Extra JQL, ANDed with the board filter."
    };

    private static JiraApiParameter ValidateQuery() => new()
    {
        Name = "validateQuery",
        Type = JiraApiParamType.Boolean,
        Description = "Whether Jira validates the JQL before running it. Defaults to true when unset."
    };

    private static JiraApiParameter Fields() => new()
    {
        Name = "fields",
        Placeholder = "summary,status,assignee",
        Description = "Comma-separated fields to return. Use *all or *navigable for the full set."
    };

    private static JiraApiParameter Expand(string description = "Comma-separated list of entities to expand, e.g. changelog,renderedFields.") => new()
    {
        Name = "expand",
        Description = description
    };
}
