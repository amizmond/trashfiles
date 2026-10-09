using Estimation.Core.Capacity.Models;
using Estimation.Core.Features.Models;
using Estimation.Core.PlanningIncrement.Models;
using Estimation.Core.Resources.Models;
using Estimation.Core.Tests.Infrastructure;
using Estimation.Core.Train.Models;

namespace Estimation.Core.Tests.Capacity;

internal static class ArtOrderTestData
{
    public const int PastPi = 1;
    public const int CurrentPi = 2;
    public const int NextPi = 3;
    public const int QuietPi = 4;
    public const int OwnPi = 5;

    public const int CorePayments = 1;
    public const int DataPlatform = 2;
    public const int Mobile = 3;

    public const int Argon = 1;
    public const int Iris = 2;
    public const int Zeta = 3;

    public const int Featureless = 4;
    public const int OtherArtOnly = 5;
    public const int DoneEpic = 6;
    public const int PastOnly = 7;

    public static Task SeedAsync(
        InMemoryDatabase database,
        ArtPrioritization? corePayments = ArtPrioritization.PortfolioEpic,
        ArtPrioritization? dataPlatform = ArtPrioritization.BusinessOutcome,
        ArtPrioritization? mobile = null) =>
        database.SeedAsync(db =>
        {
            var today = DateTime.UtcNow.Date;
            db.Pis.AddRange(
                new Pi { Id = PastPi, Name = "PI 26.07", EndDate = today.AddDays(-20), IsLocked = true },
                new Pi { Id = CurrentPi, Name = "PI 26.10", EndDate = today.AddDays(40) },
                new Pi { Id = NextPi, Name = "PI 27.01", EndDate = today.AddDays(130), IsLocked = true },
                new Pi { Id = QuietPi, Name = "PI 27.04", EndDate = today.AddDays(220), IsLocked = true },
                new Pi { Id = OwnPi, Name = "PI 26.04", EndDate = today.AddDays(-110), IsLocked = true });

            db.CapitalProjects.AddRange(
                new Art { Id = CorePayments, Name = "Core Payments", Prioritization = corePayments },
                new Art { Id = DataPlatform, Name = "Data Platform", Prioritization = dataPlatform },
                new Art { Id = Mobile, Name = "Mobile", Prioritization = mobile });
            db.Teams.AddRange(
                new Team { Id = Argon, Name = "Argon" },
                new Team { Id = Iris, Name = "Iris" },
                new Team { Id = Zeta, Name = "Zeta" });
            db.CapitalProjectTeams.AddRange(
                new ArtTeam { CapitalProjectId = CorePayments, TeamId = Argon },
                new ArtTeam { CapitalProjectId = CorePayments, TeamId = Iris },
                new ArtTeam { CapitalProjectId = DataPlatform, TeamId = Zeta });

            db.PortfolioEpics.AddRange(
                new PortfolioEpic { Id = 1, JiraId = "EPIC-1", Summary = "Instant payments", L6Owner = "Martin Vale", Comments = "Board ask" },
                new PortfolioEpic { Id = 2, JiraId = "EPIC-2", Summary = "Card tokenisation" },
                new PortfolioEpic { Id = 3, JiraId = "EPIC-3", Summary = "Observability" },
                new PortfolioEpic { Id = Featureless, JiraId = "EPIC-4", Summary = "Partner onboarding", Status = "In Progress" },
                new PortfolioEpic { Id = OtherArtOnly, JiraId = "EPIC-5", Summary = "Data lake" },
                new PortfolioEpic { Id = DoneEpic, JiraId = "EPIC-6", Summary = "Legacy payments shutdown", Status = "Done" },
                new PortfolioEpic { Id = PastOnly, JiraId = "EPIC-7", Summary = "Fraud scoring" },
                new PortfolioEpic { Id = 10, JiraId = "EPIC-10", Summary = "Payment instant refunds" });
            db.BusinessOutcomes.AddRange(
                new BusinessOutcome { Id = 1, JiraId = "BO-1", Summary = "Payment rails", PortfolioEpicId = 1, RagStatus = "Amber" },
                new BusinessOutcome { Id = 2, JiraId = "BO-2", Summary = "Token vault", PortfolioEpicId = 2 },
                new BusinessOutcome { Id = 3, JiraId = "BO-3", Summary = "Tracing", PortfolioEpicId = 3 },
                new BusinessOutcome { Id = 4, JiraId = "BO-4", Summary = "Orphan outcome" },
                new BusinessOutcome { Id = 5, JiraId = "BO-5", Summary = "Lake ingestion", PortfolioEpicId = OtherArtOnly },
                new BusinessOutcome { Id = 7, JiraId = "BO-7", Summary = "Scoring model", PortfolioEpicId = PastOnly },
                new BusinessOutcome { Id = 8, JiraId = "BO-8", Summary = "Unplanned outcome", PortfolioEpicId = Featureless });

            AddFeature(db, 1, outcomeId: 1, piId: CurrentPi, Argon);
            AddFeature(db, 2, outcomeId: 2, piId: CurrentPi, Argon, Iris);
            AddFeature(db, 3, outcomeId: 3, piId: NextPi, Iris);
            AddFeature(db, 4, outcomeId: null, piId: CurrentPi, Argon);
            AddFeature(db, 5, outcomeId: 4, piId: CurrentPi, Iris);
            AddFeature(db, 6, outcomeId: 5, piId: CurrentPi, Zeta);
            AddFeature(db, 7, outcomeId: 7, piId: PastPi, Argon);
            AddFeature(db, 8, outcomeId: 1, piId: null, Argon);
            AddFeature(db, 9, outcomeId: 7, piId: PastPi, Argon);

            db.TeamCapacityFeatureOrders.Add(new TeamCapacityFeatureOrder
            {
                TeamId = Argon,
                PiId = CurrentPi,
                FeatureId = 9,
                SortOrder = 1,
                IsIncluded = true,
            });
        });

    private static void AddFeature(EstimationDbContext db, int id, int? outcomeId, int? piId, params int[] teamIds)
    {
        db.Features.Add(new Feature
        {
            Id = id,
            JiraId = $"PAY-{id}",
            Name = $"Feature {id}",
            Summary = $"Feature {id}",
            Status = "Funnel",
            Ranking = id,
            BusinessOutcomeId = outcomeId,
            PiId = piId,
        });
        foreach (var teamId in teamIds)
        {
            db.FeatureTeams.Add(new FeatureTeam { FeatureId = id, TeamId = teamId, StoryPoints = 10 });
        }
    }

    public static Task SaveRowsAsync(
        InMemoryDatabase database,
        int artId,
        int? piId,
        ArtPrioritization level,
        params (int? ItemId, int SortOrder, bool IsIncluded)[] rows) =>
        database.SeedAsync(db =>
        {
            foreach (var (itemId, sortOrder, isIncluded) in rows)
            {
                db.ArtPrioritizationOrders.Add(new ArtPrioritizationOrder
                {
                    CapitalProjectId = artId,
                    PiId = piId,
                    Level = level,
                    PortfolioEpicId = level == ArtPrioritization.PortfolioEpic ? itemId : null,
                    BusinessOutcomeId = level == ArtPrioritization.BusinessOutcome ? itemId : null,
                    SortOrder = sortOrder,
                    IsIncluded = isIncluded,
                });
            }
        });
}
