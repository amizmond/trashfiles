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
    private HousekeepingTestDbContext _testContext;
    private HousekeepingRepository<HousekeepingTestDbContext> _repository;
    private DbContextOptions<BaseDbContext> _options;

    [SetUp]
    public void SetUp()
    {
        // Create in-memory database options
        _options = new DbContextOptionsBuilder<BaseDbContext>()
            .UseInMemoryDatabase(databaseName: $"HousekeepingTestDb_{Guid.NewGuid()}")
            .Options;

        var mockLoggerFactory = new Mock<ILoggerFactory>();
        _mockLogger = new Mock<ILogger<IAdjustmentRepository>>();
        
        // Create test context
        _testContext = new HousekeepingTestDbContext(_options, mockLoggerFactory.Object, true);
        
        // Mock the DbContextFactory
        _mockDbContextFactory = new Mock<IDbContextFactory<HousekeepingTestDbContext>>();
        _mockDbContextFactory
            .Setup(f => f.CreateDbContextAsync(default))
            .ReturnsAsync(_testContext);
        
        // Create repository
        _repository = new HousekeepingRepository<HousekeepingTestDbContext>(
            _mockLogger.Object,
            _mockDbContextFactory.Object);
        
        // Seed test data
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
        // Act
        var result = await _repository.GetHousekeepingRequestTypes();

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Count, Is.EqualTo(3));
    }

    [Test]
    public async Task GetHousekeepingRequestTypes_WithWorkspace_ReturnsFilteredRequestTypes()
    {
        // Act
        var result = await _repository.GetHousekeepingRequestTypes("Workspace1");

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Count, Is.EqualTo(2));
        Assert.That(result.All(r => r.Workspace == "Workspace1"), Is.True);
    }

    [Test]
    public async Task GetHousekeepingRequestTypes_WithNonExistingWorkspace_ReturnsEmptyList()
    {
        // Act
        var result = await _repository.GetHousekeepingRequestTypes("NonExistingWorkspace");

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Count, Is.EqualTo(0));
    }

    [Test]
    public async Task GetHousekeepingRequestTypes_WithEmptyWorkspace_ReturnsAllRequestTypes()
    {
        // Act
        var result = await _repository.GetHousekeepingRequestTypes(string.Empty);

        // Assert
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

        var mockContext = new Mock<HousekeepingTestDbContext>(_options, Mock.Of<ILoggerFactory>(), true);
        mockContext.Setup(c => c.ExecuteStoredProcedureAsync<HkRequest, GetHkRequestIdListParameter>(
            It.IsAny<GetHkRequestIdListParameter>()))
            .ReturnsAsync(expectedRequests);

        _mockDbContextFactory
            .Setup(f => f.CreateDbContextAsync(default))
            .ReturnsAsync(mockContext.Object);

        // Act
        var result = await _repository.GetHousekeepingRequests(requestTypeId);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Count, Is.EqualTo(2));
        mockContext.Verify(c => c.ExecuteStoredProcedureAsync<HkRequest, GetHkRequestIdListParameter>(
            It.Is<GetHkRequestIdListParameter>(p => p.RequestTypeId == requestTypeId)), 
            Times.Once);
    }

    [Test]
    public async Task UpdateRequestHousekeeping_ValidParameters_CallsStoredProcedure()
    {
        // Arrange
        var requestTypeId = 1;
        var holdDate = DateTime.Now;

        var mockContext = new Mock<HousekeepingTestDbContext>(_options, Mock.Of<ILoggerFactory>(), true);
        mockContext.Setup(c => c.ExecuteStoredProcedureAsync(It.IsAny<HoldRequestHkParameter>()))
            .Returns(Task.CompletedTask);

        _mockDbContextFactory
            .Setup(f => f.CreateDbContextAsync(default))
            .ReturnsAsync(mockContext.Object);

        // Act
        await _repository.UpdateRequestHousekeeping(requestTypeId, holdDate);

        // Assert
        mockContext.Verify(c => c.ExecuteStoredProcedureAsync(
            It.Is<HoldRequestHkParameter>(p => p.RequestId == requestTypeId && p.LastHoldDate == holdDate)), 
            Times.Once);
    }

    [Test]
    public async Task UpdateRequestHousekeeping_NullDate_CallsStoredProcedureWithNullDate()
    {
        // Arrange
        var requestTypeId = 1;
        DateTime? holdDate = null;

        var mockContext = new Mock<HousekeepingTestDbContext>(_options, Mock.Of<ILoggerFactory>(), true);
        mockContext.Setup(c => c.ExecuteStoredProcedureAsync(It.IsAny<HoldRequestHkParameter>()))
            .Returns(Task.CompletedTask);

        _mockDbContextFactory
            .Setup(f => f.CreateDbContextAsync(default))
            .ReturnsAsync(mockContext.Object);

        // Act
        await _repository.UpdateRequestHousekeeping(requestTypeId, holdDate);

        // Assert
        mockContext.Verify(c => c.ExecuteStoredProcedureAsync(
            It.Is<HoldRequestHkParameter>(p => p.RequestId == requestTypeId && p.LastHoldDate == null)), 
            Times.Once);
    }
}