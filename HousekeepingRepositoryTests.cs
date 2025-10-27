using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace YourNamespace.Tests
{
    [TestFixture]
    public class HousekeepingRepositoryTests
    {
        private Mock<ILogger<IAdjustmentRepository>> _mockLogger;
        private Mock<IDbContextFactory<TestDbContext>> _mockDbContextFactory;
        private Mock<TestDbContext> _mockDbContext;
        private HousekeepingRepository<TestDbContext> _repository;

        [SetUp]
        public void SetUp()
        {
            _mockLogger = new Mock<ILogger<IAdjustmentRepository>>();
            _mockDbContextFactory = new Mock<IDbContextFactory<TestDbContext>>();
            
            // Create mock DbContext
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            _mockDbContext = new Mock<TestDbContext>(
                new DbContextOptionsBuilder<TestDbContext>().Options, 
                mockLoggerFactory.Object);

            // Setup factory to return mocked context
            _mockDbContextFactory
                .Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(_mockDbContext.Object);

            _repository = new HousekeepingRepository<TestDbContext>(
                _mockLogger.Object, 
                _mockDbContextFactory.Object);
        }

        [Test]
        public async Task GetHousekeepingRequestTypes_WithoutWorkspace_ReturnsAllRequestTypes()
        {
            // Arrange
            var data = new List<HkRequestType>
            {
                new HkRequestType { RequestTypeId = 1, Workspace = "WorkspaceA", RequestTypeDesc = "Type A" },
                new HkRequestType { RequestTypeId = 2, Workspace = "WorkspaceB", RequestTypeDesc = "Type B" },
                new HkRequestType { RequestTypeId = 3, Workspace = "WorkspaceC", RequestTypeDesc = "Type C" }
            };

            var mockDbSet = CreateMockDbSet(data);
            _mockDbContext.Setup(c => c.HousekeepingRequestTypes).Returns(mockDbSet.Object);

            // Act
            var result = await _repository.GetHousekeepingRequestTypes();

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Count, Is.EqualTo(3));
            _mockDbContextFactory.Verify(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public async Task GetHousekeepingRequestTypes_WithWorkspace_ReturnsFilteredRequestTypes()
        {
            // Arrange
            var data = new List<HkRequestType>
            {
                new HkRequestType { RequestTypeId = 1, Workspace = "WorkspaceA", RequestTypeDesc = "Type A" },
                new HkRequestType { RequestTypeId = 2, Workspace = "WorkspaceB", RequestTypeDesc = "Type B" },
                new HkRequestType { RequestTypeId = 3, Workspace = "WorkspaceA", RequestTypeDesc = "Type C" }
            };

            var mockDbSet = CreateMockDbSet(data);
            _mockDbContext.Setup(c => c.HousekeepingRequestTypes).Returns(mockDbSet.Object);

            // Act
            var result = await _repository.GetHousekeepingRequestTypes("WorkspaceA");

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Count, Is.EqualTo(2));
            Assert.That(result.All(r => r.Workspace == "WorkspaceA"), Is.True);
        }

        [Test]
        public async Task GetHousekeepingRequestTypes_WithNonExistentWorkspace_ReturnsEmptyList()
        {
            // Arrange
            var data = new List<HkRequestType>
            {
                new HkRequestType { RequestTypeId = 1, Workspace = "WorkspaceA", RequestTypeDesc = "Type A" }
            };

            var mockDbSet = CreateMockDbSet(data);
            _mockDbContext.Setup(c => c.HousekeepingRequestTypes).Returns(mockDbSet.Object);

            // Act
            var result = await _repository.GetHousekeepingRequestTypes("NonExistent");

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Count, Is.EqualTo(0));
        }

        [Test]
        public async Task GetHousekeepingRequestTypes_WithEmptyDatabase_ReturnsEmptyList()
        {
            // Arrange
            var data = new List<HkRequestType>();
            var mockDbSet = CreateMockDbSet(data);
            _mockDbContext.Setup(c => c.HousekeepingRequestTypes).Returns(mockDbSet.Object);

            // Act
            var result = await _repository.GetHousekeepingRequestTypes();

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Count, Is.EqualTo(0));
        }

        [Test]
        public async Task GetHousekeepingRequestTypes_WithNullWorkspace_ReturnsAllRequestTypes()
        {
            // Arrange
            var data = new List<HkRequestType>
            {
                new HkRequestType { RequestTypeId = 1, Workspace = "WorkspaceA", RequestTypeDesc = "Type A" },
                new HkRequestType { RequestTypeId = 2, Workspace = "WorkspaceB", RequestTypeDesc = "Type B" }
            };

            var mockDbSet = CreateMockDbSet(data);
            _mockDbContext.Setup(c => c.HousekeepingRequestTypes).Returns(mockDbSet.Object);

            // Act
            var result = await _repository.GetHousekeepingRequestTypes(null);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Count, Is.EqualTo(2));
        }

        [Test]
        public async Task GetHousekeepingRequestTypes_WithEmptyStringWorkspace_ReturnsAllRequestTypes()
        {
            // Arrange
            var data = new List<HkRequestType>
            {
                new HkRequestType { RequestTypeId = 1, Workspace = "WorkspaceA", RequestTypeDesc = "Type A" }
            };

            var mockDbSet = CreateMockDbSet(data);
            _mockDbContext.Setup(c => c.HousekeepingRequestTypes).Returns(mockDbSet.Object);

            // Act
            var result = await _repository.GetHousekeepingRequestTypes(string.Empty);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Count, Is.EqualTo(1));
        }

        [Test]
        public async Task GetHousekeepingRequests_CallsExecuteStoredProcedure()
        {
            // Arrange
            int requestTypeId = 1;
            var expectedResult = new List<HkRequest>
            {
                new HkRequest { RequestId = 1, LastHoldDate = DateTime.Now },
                new HkRequest { RequestId = 2, LastHoldDate = DateTime.Now.AddDays(-1) }
            };

            _mockDbContext
                .Setup(c => c.ExecuteStoredProcedureAsync<HkRequest, GetHkRequestIdListParameter>(
                    It.Is<GetHkRequestIdListParameter>(p => p.RequestTypeId == requestTypeId)))
                .ReturnsAsync(expectedResult);

            // Act
            var result = await _repository.GetHousekeepingRequests(requestTypeId);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Count, Is.EqualTo(2));
            _mockDbContext.Verify(c => c.ExecuteStoredProcedureAsync<HkRequest, GetHkRequestIdListParameter>(
                It.IsAny<GetHkRequestIdListParameter>()), Times.Once);
        }

        [Test]
        public async Task UpdateRequestHousekeeping_WithDate_CallsStoredProcedure()
        {
            // Arrange
            int requestTypeId = 1;
            DateTime? date = DateTime.Now;

            _mockDbContext
                .Setup(c => c.ExecuteStoredProcedureAsync(It.IsAny<HoldRequestHkParameter>()))
                .Returns(Task.CompletedTask);

            // Act
            await _repository.UpdateRequestHousekeeping(requestTypeId, date);

            // Assert
            _mockDbContext.Verify(c => c.ExecuteStoredProcedureAsync(
                It.Is<HoldRequestHkParameter>(p => p.RequestId == requestTypeId && p.LastHoldDate == date)), 
                Times.Once);
        }

        [Test]
        public async Task UpdateRequestHousekeeping_WithNullDate_CallsStoredProcedure()
        {
            // Arrange
            int requestTypeId = 1;
            DateTime? date = null;

            _mockDbContext
                .Setup(c => c.ExecuteStoredProcedureAsync(It.IsAny<HoldRequestHkParameter>()))
                .Returns(Task.CompletedTask);

            // Act
            await _repository.UpdateRequestHousekeeping(requestTypeId, date);

            // Assert
            _mockDbContext.Verify(c => c.ExecuteStoredProcedureAsync(
                It.Is<HoldRequestHkParameter>(p => p.RequestId == requestTypeId && p.LastHoldDate == null)), 
                Times.Once);
        }

        [Test]
        public async Task GetHousekeepingRequests_VerifiesCorrectParameter()
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
            _mockDbContext.Verify(c => c.ExecuteStoredProcedureAsync<HkRequest, GetHkRequestIdListParameter>(
                It.Is<GetHkRequestIdListParameter>(p => p.RequestTypeId == requestTypeId)), 
                Times.Once);
        }

        // Helper method to create a mockable DbSet
        private Mock<DbSet<T>> CreateMockDbSet<T>(List<T> data) where T : class
        {
            var queryable = data.AsQueryable();
            var mockDbSet = new Mock<DbSet<T>>();

            mockDbSet.As<IQueryable<T>>().Setup(m => m.Provider).Returns(queryable.Provider);
            mockDbSet.As<IQueryable<T>>().Setup(m => m.Expression).Returns(queryable.Expression);
            mockDbSet.As<IQueryable<T>>().Setup(m => m.ElementType).Returns(queryable.ElementType);
            mockDbSet.As<IQueryable<T>>().Setup(m => m.GetEnumerator()).Returns(() => queryable.GetEnumerator());

            return mockDbSet;
        }
    }

    // Test DbContext that can be mocked
    public class TestDbContext : BaseDbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options, ILoggerFactory loggerFactory)
            : base(options, loggerFactory)
        {
        }

        public virtual DbSet<HkRequestType> HousekeepingRequestTypes { get; set; }
        public virtual DbSet<HkRequest> HousekeepingRequests { get; set; }

        // Make these virtual so they can be mocked
        public virtual Task<IList<T>> ExecuteStoredProcedureAsync<T, TParameter>(TParameter parameter) 
            where T : class
        {
            throw new NotImplementedException();
        }

        public virtual Task ExecuteStoredProcedureAsync<TParameter>(TParameter parameter)
        {
            throw new NotImplementedException();
        }
    }
}