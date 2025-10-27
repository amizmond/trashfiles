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
    [Test]
    public async Task GetHousekeepingRequests_ShouldExecuteStoredProcedureAsync()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<IAdjustmentRepository>>();
        var mockDbContextFactory = new Mock<IDbContextFactory<HousekeepingTestDbContext>>();
        var mockDbContext = new Mock<HousekeepingTestDbContext>(
            Mock.Of<DbContextOptions<BaseDbContext>>(),
            Mock.Of<ILoggerFactory>(),
            true);
        
        mockDbContextFactory
            .Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockDbContext.Object);
        
        var repository = new HousekeepingRepository<HousekeepingTestDbContext>(
            mockLogger.Object,
            mockDbContextFactory.Object);
        
        int requestTypeId = 123;
        var expectedResult = new List<HkRequest>();
        
        mockDbContext
            .Setup(c => c.ExecuteStoredProcedureAsync<HkRequest, GetHkRequestIdListParameter>(
                It.IsAny<GetHkRequestIdListParameter>()))
            .ReturnsAsync(expectedResult);

        // Act
        await repository.GetHousekeepingRequests(requestTypeId);

        // Assert
        mockDbContext.Verify(
            c => c.ExecuteStoredProcedureAsync<HkRequest, GetHkRequestIdListParameter>(
                It.Is<GetHkRequestIdListParameter>(p => p.RequestTypeId == requestTypeId)),
            Times.Once);
    }

    [Test]
    public async Task UpdateRequestHousekeeping_ShouldExecuteStoredProcedureAsync()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<IAdjustmentRepository>>();
        var mockDbContextFactory = new Mock<IDbContextFactory<HousekeepingTestDbContext>>();
        var mockDbContext = new Mock<HousekeepingTestDbContext>(
            Mock.Of<DbContextOptions<BaseDbContext>>(),
            Mock.Of<ILoggerFactory>(),
            true);
        
        mockDbContextFactory
            .Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockDbContext.Object);
        
        var repository = new HousekeepingRepository<HousekeepingTestDbContext>(
            mockLogger.Object,
            mockDbContextFactory.Object);
        
        int requestTypeId = 456;
        DateTime? date = new DateTime(2025, 10, 27);
        
        mockDbContext
            .Setup(c => c.ExecuteStoredProcedureAsync(
                It.IsAny<HoldRequestHkParameter>()))
            .Returns(Task.CompletedTask);

        // Act
        await repository.UpdateRequestHousekeeping(requestTypeId, date);

        // Assert
        mockDbContext.Verify(
            c => c.ExecuteStoredProcedureAsync(
                It.Is<HoldRequestHkParameter>(p => p.RequestId == requestTypeId)),
            Times.Once);
    }
}