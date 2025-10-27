using NUnit.Framework;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

[TestFixture]
public class HousekeepingRepositoryTests
{
    private Mock<ILogger<IAdjustmentRepository>> _mockLogger;
    private DbContextOptions<BaseDbContext> _options;

    [SetUp]
    public void SetUp()
    {
        _options = new DbContextOptionsBuilder<BaseDbContext>()
            .UseInMemoryDatabase(databaseName: $"HousekeepingTestDb_{Guid.NewGuid()}")
            .Options;

        _mockLogger = new Mock<ILogger<IAdjustmentRepository>>();
    }

    [Test]
    public async Task GetHousekeepingRequestTypes_WithoutWorkspace_ReturnsAllRequestTypes()
    {
        // Arrange
        var context = new HousekeepingTestDbContextWithConfig(_options, Mock.Of<ILoggerFactory>(), true);
        SeedTestData(context);

        var factory = new Mock<IDbContextFactory<HousekeepingTestDbContext>>();
        factory.Setup(f => f.CreateDbContextAsync(default)).ReturnsAsync(context);

        var repository = new HousekeepingRepository<HousekeepingTestDbContext>(
            _mockLogger.Object,
            factory.Object);

        // Act
        var result = await repository.GetHousekeepingRequestTypes();

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Count, Is.EqualTo(3));

        context.Database.EnsureDeleted();
        context.Dispose();
    }

    [Test]
    public async Task GetHousekeepingRequestTypes_WithWorkspace_ReturnsFilteredRequestTypes()
    {
        // Arrange
        var context = new HousekeepingTestDbContextWithConfig(_options, Mock.Of<ILoggerFactory>(), true);
        SeedTestData(context);

        var factory = new Mock<IDbContextFactory<HousekeepingTestDbContext>>();
        factory.Setup(f => f.CreateDbContextAsync(default)).ReturnsAsync(context);

        var repository = new HousekeepingRepository<HousekeepingTestDbContext>(
            _mockLogger.Object,
            factory.Object);

        // Act
        var result = await repository.GetHousekeepingRequestTypes("Workspace1");

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Count, Is.EqualTo(2));
        Assert.That(result.All(r => r.Workspace == "Workspace1"), Is.True);

        context.Database.EnsureDeleted();
        context.Dispose();
    }

    [Test]
    public async Task GetHousekeepingRequestTypes_WithNonExistingWorkspace_ReturnsEmptyList()
    {
        // Arrange
        var context = new HousekeepingTestDbContextWithConfig(_options, Mock.Of<ILoggerFactory>(), true);
        SeedTestData(context);

        var factory = new Mock<IDbContextFactory<HousekeepingTestDbContext>>();
        factory.Setup(f => f.CreateDbContextAsync(default)).ReturnsAsync(context);

        var repository = new HousekeepingRepository<HousekeepingTestDbContext>(
            _mockLogger.Object,
            factory.Object);

        // Act
        var result = await repository.GetHousekeepingRequestTypes("NonExistingWorkspace");

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Count, Is.EqualTo(0));

        context.Database.EnsureDeleted();
        context.Dispose();
    }

    [Test]
    public async Task GetHousekeepingRequestTypes_WithEmptyWorkspace_ReturnsAllRequestTypes()
    {
        // Arrange
        var context = new HousekeepingTestDbContextWithConfig(_options, Mock.Of<ILoggerFactory>(), true);
        SeedTestData(context);

        var factory = new Mock<IDbContextFactory<HousekeepingTestDbContext>>();
        factory.Setup(f => f.CreateDbContextAsync(default)).ReturnsAsync(context);

        var repository = new HousekeepingRepository<HousekeepingTestDbContext>(
            _mockLogger.Object,
            factory.Object);

        // Act
        var result = await repository.GetHousekeepingRequestTypes(string.Empty);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Count, Is.EqualTo(3));

        context.Database.EnsureDeleted();
        context.Dispose();
    }

    [Test]
    public async Task GetHousekeepingRequests_ValidRequestTypeId_CallsStoredProcedure()
    {
        // Arrange
        var requestTypeId = 1;
        var expectedRequests = new List<HkRequest>
        {
            new HkRequest { RequestId = 1, LastHoldDate = DateTime.Now },
            new HkRequest { RequestId = 2, LastHoldDate = DateTime.Now.AddDays(-1) }
        };

        var spyContext = new SpyHousekeepingTestDbContext(_options, Mock.Of<ILoggerFactory>(), true);
        spyContext.SetupGetHousekeepingRequestsResult(expectedRequests);

        var factory = new TestDbContextFactory<SpyHousekeepingTestDbContext>(spyContext);

        var repository = new HousekeepingRepository<SpyHousekeepingTestDbContext>(
            _mockLogger.Object,
            factory);

        // Act
        var result = await repository.GetHousekeepingRequests(requestTypeId);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Count, Is.EqualTo(2));
        Assert.That(spyContext.GetHousekeepingRequestsCalled, Is.True);
        Assert.That(spyContext.LastGetHousekeepingRequestsParameter?.RequestTypeId, Is.EqualTo(requestTypeId));

        spyContext.Dispose();
    }

    [Test]
    public async Task UpdateRequestHousekeeping_ValidParameters_CallsStoredProcedure()
    {
        // Arrange
        var requestTypeId = 1;
        var holdDate = DateTime.Now;

        var spyContext = new SpyHousekeepingTestDbContext(_options, Mock.Of<ILoggerFactory>(), true);

        var factory = new TestDbContextFactory<SpyHousekeepingTestDbContext>(spyContext);

        var repository = new HousekeepingRepository<SpyHousekeepingTestDbContext>(
            _mockLogger.Object,
            factory);

        // Act
        await repository.UpdateRequestHousekeeping(requestTypeId, holdDate);

        // Assert
        Assert.That(spyContext.UpdateRequestHousekeepingCalled, Is.True);
        Assert.That(spyContext.LastUpdateRequestHousekeepingParameter, Is.Not.Null);
        Assert.That(spyContext.LastUpdateRequestHousekeepingParameter.RequestId, Is.EqualTo(requestTypeId));
        Assert.That(spyContext.LastUpdateRequestHousekeepingParameter.LastHoldDate, Is.EqualTo(holdDate));

        spyContext.Dispose();
    }

    [Test]
    public async Task UpdateRequestHousekeeping_NullDate_CallsStoredProcedureWithNullDate()
    {
        // Arrange
        var requestTypeId = 1;
        DateTime? holdDate = null;

        var spyContext = new SpyHousekeepingTestDbContext(_options, Mock.Of<ILoggerFactory>(), true);

        var factory = new TestDbContextFactory<SpyHousekeepingTestDbContext>(spyContext);

        var repository = new HousekeepingRepository<SpyHousekeepingTestDbContext>(
            _mockLogger.Object,
            factory);

        // Act
        await repository.UpdateRequestHousekeeping(requestTypeId, holdDate);

        // Assert
        Assert.That(spyContext.UpdateRequestHousekeepingCalled, Is.True);
        Assert.That(spyContext.LastUpdateRequestHousekeepingParameter, Is.Not.Null);
        Assert.That(spyContext.LastUpdateRequestHousekeepingParameter.RequestId, Is.EqualTo(requestTypeId));
        Assert.That(spyContext.LastUpdateRequestHousekeepingParameter.LastHoldDate, Is.Null);

        spyContext.Dispose();
    }

    private void SeedTestData(HousekeepingTestDbContext context)
    {
        var requestTypes = new List<HkRequestType>
        {
            new HkRequestType { RequestTypeId = 1, RequestTypeDesc = "Type 1", Workspace = "Workspace1" },
            new HkRequestType { RequestTypeId = 2, RequestTypeDesc = "Type 2", Workspace = "Workspace1" },
            new HkRequestType { RequestTypeId = 3, RequestTypeDesc = "Type 3", Workspace = "Workspace2" }
        };

        context.HousekeepingRequestTypes.AddRange(requestTypes);
        context.SaveChanges();
    }
}

// Simple test factory that returns the provided context
public class TestDbContextFactory<TContext> : IDbContextFactory<TContext> 
    where TContext : DbContext
{
    private readonly TContext _context;

    public TestDbContextFactory(TContext context)
    {
        _context = context;
    }

    public TContext CreateDbContext()
    {
        return _context;
    }

    public Task<TContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_context);
    }
}

// Spy context that tracks stored procedure calls
public class SpyHousekeepingTestDbContext : HousekeepingTestDbContext
{
    private IList<HkRequest> _getHousekeepingRequestsResult;

    public bool GetHousekeepingRequestsCalled { get; private set; }
    public GetHkRequestIdListParameter LastGetHousekeepingRequestsParameter { get; private set; }

    public bool UpdateRequestHousekeepingCalled { get; private set; }
    public HoldRequestHkParameter LastUpdateRequestHousekeepingParameter { get; private set; }

    public SpyHousekeepingTestDbContext(
        DbContextOptions<BaseDbContext> options,
        ILoggerFactory loggerFactory,
        bool isUnderTest = true)
        : base(options, loggerFactory, isUnderTest)
    {
        _getHousekeepingRequestsResult = new List<HkRequest>();
    }

    public void SetupGetHousekeepingRequestsResult(IList<HkRequest> result)
    {
        _getHousekeepingRequestsResult = result;
    }

    public override Task<IList<T>> ExecuteStoredProcedureAsync<T, TParam>(TParam parameter)
    {
        if (parameter is GetHkRequestIdListParameter getParam)
        {
            GetHousekeepingRequestsCalled = true;
            LastGetHousekeepingRequestsParameter = getParam;
            return Task.FromResult(_getHousekeepingRequestsResult as IList<T>);
        }

        throw new NotImplementedException();
    }

    public override Task ExecuteStoredProcedureAsync<TParam>(TParam parameter)
    {
        if (parameter is HoldRequestHkParameter holdParam)
        {
            UpdateRequestHousekeepingCalled = true;
            LastUpdateRequestHousekeepingParameter = holdParam;
            return Task.CompletedTask;
        }

        throw new NotImplementedException();
    }
}

// Test context with model configuration
public class HousekeepingTestDbContextWithConfig : HousekeepingTestDbContext
{
    public HousekeepingTestDbContextWithConfig(
        DbContextOptions<BaseDbContext> options,
        ILoggerFactory loggerFactory,
        bool isUnderTest = true)
        : base(options, loggerFactory, isUnderTest)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<HkRequestType>()
            .HasKey(e => e.RequestTypeId);
    }
}