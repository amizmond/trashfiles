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
    private Mock<IDbContextFactory<HousekeepingTestDbContext>> _mockDbContextFactory;
    private HousekeepingTestDbContextWithConfig _testContext;
    private HousekeepingRepository<HousekeepingTestDbContext> _repository;
    private DbContextOptions<BaseDbContext> _options;

    [SetUp]
    public void SetUp()
    {
        _options = new DbContextOptionsBuilder<BaseDbContext>()
            .UseInMemoryDatabase(databaseName: $"HousekeepingTestDb_{Guid.NewGuid()}")
            .Options;

        var mockLoggerFactory = new Mock<ILoggerFactory>();
        _mockLogger = new Mock<ILogger<IAdjustmentRepository>>();
        
        _testContext = new HousekeepingTestDbContextWithConfig(_options, mockLoggerFactory.Object, true);
        
        _mockDbContextFactory = new Mock<IDbContextFactory<HousekeepingTestDbContext>>();
        _mockDbContextFactory
            .Setup(f => f.CreateDbContextAsync(default))
            .ReturnsAsync(_testContext);
        
        _repository = new HousekeepingRepository<HousekeepingTestDbContext>(
            _mockLogger.Object,
            _mockDbContextFactory.Object);
        
        SeedTestData();
    }

    [TearDown]
    public void TearDown()
    {
        _testContext?.Database.EnsureDeleted();
        _testContext?.Dispose();
    }

    private void SeedTestData()
    {
        var requestTypes = new List<HkRequestType>
        {
            new HkRequestType { RequestTypeId = 1, RequestTypeDesc = "Type 1", Workspace = "Workspace1" },
            new HkRequestType { RequestTypeId = 2, RequestTypeDesc = "Type 2", Workspace = "Workspace1" },
            new HkRequestType { RequestTypeId = 3, RequestTypeDesc = "Type 3", Workspace = "Workspace2" }
        };

        _testContext.HousekeepingRequestTypes.AddRange(requestTypes);
        _testContext.SaveChanges();
    }

    [Test]
    public async Task GetHousekeepingRequestTypes_WithoutWorkspace_ReturnsAllRequestTypes()
    {
        var result = await _repository.GetHousekeepingRequestTypes();

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Count, Is.EqualTo(3));
    }

    [Test]
    public async Task GetHousekeepingRequestTypes_WithWorkspace_ReturnsFilteredRequestTypes()
    {
        var result = await _repository.GetHousekeepingRequestTypes("Workspace1");

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Count, Is.EqualTo(2));
        Assert.That(result.All(r => r.Workspace == "Workspace1"), Is.True);
    }

    [Test]
    public async Task GetHousekeepingRequestTypes_WithNonExistingWorkspace_ReturnsEmptyList()
    {
        var result = await _repository.GetHousekeepingRequestTypes("NonExistingWorkspace");

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Count, Is.EqualTo(0));
    }

    [Test]
    public async Task GetHousekeepingRequestTypes_WithEmptyWorkspace_ReturnsAllRequestTypes()
    {
        var result = await _repository.GetHousekeepingRequestTypes(string.Empty);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Count, Is.EqualTo(3));
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

        var spyFactory = new Mock<IDbContextFactory<SpyHousekeepingTestDbContext>>();
        spyFactory
            .Setup(f => f.CreateDbContextAsync(default))
            .ReturnsAsync(spyContext);

        var spyRepository = new HousekeepingRepository<SpyHousekeepingTestDbContext>(
            _mockLogger.Object,
            spyFactory.Object);

        // Act
        var result = await spyRepository.GetHousekeepingRequests(requestTypeId);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Count, Is.EqualTo(2));
        Assert.That(spyContext.GetHousekeepingRequestsCalled, Is.True);
        Assert.That(spyContext.LastGetHousekeepingRequestsParameter?.RequestTypeId, Is.EqualTo(requestTypeId));
    }

    [Test]
    public async Task UpdateRequestHousekeeping_ValidParameters_CallsStoredProcedure()
    {
        // Arrange
        var requestTypeId = 1;
        var holdDate = DateTime.Now;

        var spyContext = new SpyHousekeepingTestDbContext(_options, Mock.Of<ILoggerFactory>(), true);

        var spyFactory = new Mock<IDbContextFactory<SpyHousekeepingTestDbContext>>();
        spyFactory
            .Setup(f => f.CreateDbContextAsync(default))
            .ReturnsAsync(spyContext);

        var spyRepository = new HousekeepingRepository<SpyHousekeepingTestDbContext>(
            _mockLogger.Object,
            spyFactory.Object);

        // Act
        await spyRepository.UpdateRequestHousekeeping(requestTypeId, holdDate);

        // Assert
        Assert.That(spyContext.UpdateRequestHousekeepingCalled, Is.True);
        Assert.That(spyContext.LastUpdateRequestHousekeepingParameter, Is.Not.Null);
        Assert.That(spyContext.LastUpdateRequestHousekeepingParameter.RequestId, Is.EqualTo(requestTypeId));
        Assert.That(spyContext.LastUpdateRequestHousekeepingParameter.LastHoldDate, Is.EqualTo(holdDate));
    }

    [Test]
    public async Task UpdateRequestHousekeeping_NullDate_CallsStoredProcedureWithNullDate()
    {
        // Arrange
        var requestTypeId = 1;
        DateTime? holdDate = null;

        var spyContext = new SpyHousekeepingTestDbContext(_options, Mock.Of<ILoggerFactory>(), true);

        var spyFactory = new Mock<IDbContextFactory<SpyHousekeepingTestDbContext>>();
        spyFactory
            .Setup(f => f.CreateDbContextAsync(default))
            .ReturnsAsync(spyContext);

        var spyRepository = new HousekeepingRepository<SpyHousekeepingTestDbContext>(
            _mockLogger.Object,
            spyFactory.Object);

        // Act
        await spyRepository.UpdateRequestHousekeeping(requestTypeId, holdDate);

        // Assert
        Assert.That(spyContext.UpdateRequestHousekeepingCalled, Is.True);
        Assert.That(spyContext.LastUpdateRequestHousekeepingParameter, Is.Not.Null);
        Assert.That(spyContext.LastUpdateRequestHousekeepingParameter.RequestId, Is.EqualTo(requestTypeId));
        Assert.That(spyContext.LastUpdateRequestHousekeepingParameter.LastHoldDate, Is.Null);
    }
}

// Spy context that tracks stored procedure calls
public class SpyHousekeepingTestDbContext : HousekeepingTestDbContextWithConfig
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

        return base.ExecuteStoredProcedureAsync<T, TParam>(parameter);
    }

    public override Task ExecuteStoredProcedureAsync<TParam>(TParam parameter)
    {
        if (parameter is HoldRequestHkParameter holdParam)
        {
            UpdateRequestHousekeepingCalled = true;
            LastUpdateRequestHousekeepingParameter = holdParam;
            return Task.CompletedTask;
        }

        return base.ExecuteStoredProcedureAsync(parameter);
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