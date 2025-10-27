using NUnit.Framework;
using Moq;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

[TestFixture]
public class HousekeepingRepositoryTests
{
    private Mock<ILogger<IAdjustmentRepository>> _mockLogger;
    private Mock<IDbContextFactory<HousekeepingTestDbContext>> _mockDbContextFactory;
    private Mock<HousekeepingTestDbContext> _mockDbContext;
    private HousekeepingRepository<HousekeepingTestDbContext> _repository;

    [SetUp]
    public void SetUp()
    {
        _mockLogger = new Mock<ILogger<IAdjustmentRepository>>();
        _mockDbContextFactory = new Mock<IDbContextFactory<HousekeepingTestDbContext>>();
        _mockDbContext = new Mock<HousekeepingTestDbContext>(
            Mock.Of<DbContextOptions<BaseDbContext>>(),
            Mock.Of<ILoggerFactory>(),
            true);
        
        _mockDbContextFactory
            .Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_mockDbContext.Object);
        
        _repository = new HousekeepingRepository<HousekeepingTestDbContext>(
            _mockLogger.Object,
            _mockDbContextFactory.Object);
    }

    [Test]
    public async Task GetHousekeepingRequests_ShouldExecuteStoredProcedureAsync()
    {
        // Arrange
        int requestTypeId = 123;
        var expectedResult = new List<HkRequest>();
        
        _mockDbContext
            .Setup(c => c.ExecuteStoredProcedureAsync<HkRequest, GetHkRequestIdListParameter>(
                It.IsAny<GetHkRequestIdListParameter>()))
            .ReturnsAsync(expectedResult);

        // Act
        await _repository.GetHousekeepingRequests(requestTypeId);

        // Assert
        _mockDbContext.Verify(
            c => c.ExecuteStoredProcedureAsync<HkRequest, GetHkRequestIdListParameter>(
                It.Is<GetHkRequestIdListParameter>(p => p.RequestTypeId == requestTypeId)),
            Times.Once);
    }

    [Test]
    public async Task UpdateRequestHousekeeping_ShouldExecuteStoredProcedureAsync()
    {
        // Arrange
        int requestTypeId = 456;
        DateTime? date = new DateTime(2025, 10, 27);
        
        _mockDbContext
            .Setup(c => c.ExecuteStoredProcedureAsync(
                It.IsAny<HoldRequestHkParameter>()))
            .Returns(Task.CompletedTask);

        // Act
        await _repository.UpdateRequestHousekeeping(requestTypeId, date);

        // Assert
        _mockDbContext.Verify(
            c => c.ExecuteStoredProcedureAsync(
                It.Is<HoldRequestHkParameter>(p => p.RequestId == requestTypeId)),
            Times.Once);
    }

    [TearDown]
    public void TearDown()
    {
        _mockDbContext?.Object?.Dispose();
    }
}