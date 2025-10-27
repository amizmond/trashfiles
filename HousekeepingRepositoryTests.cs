using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using MockQueryable.Moq;

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
            
            // Create mock DbContext with proper constructor parameters
            var options = new DbContextOptionsBuilder<TestDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            
            _mockDbContext = new Mock<TestDbContext>(options, mockLoggerFactory.Object);
            
            _mockDbContextFactory
                .Setup(x => x.CreateDbContextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(_mockDbContext.Object);

            _repository = new HousekeepingRepository<TestDbContext>(
                _mockLogger.Object,
                _mockDbContextFactory.Object);
        }

        [TearDown]
        public void TearDown()
        {
            _mockDbContext?.Object?.Dispose();
        }

        [Test]
        public async Task GetHousekeepingRequestTypes_WithoutWorkspace_ReturnsAllRequestTypes()
        {
            // Arrange
            var requestTypes = new List<HkRequestType>
            {
                new HkRequestType { RequestTypeId = 1, RequestTypeDesc = "Type1", Workspace = "WS1" },
                new HkRequestType { RequestTypeId = 2, RequestTypeDesc = "Type2", Workspace = "WS2" },
                new HkRequestType { RequestTypeId = 3, RequestTypeDesc = "Type3", Workspace = "WS3" }
            };

            var mockDbSet = requestTypes.AsQueryable().BuildMockDbSet();
            
            _mockDbContext
                .Setup(x => x.HousekeepingRequestTypes)
                .Returns(mockDbSet.Object);

            // Act
            var result = await _repository.GetHousekeepingRequestTypes();

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(3, result.Count);
            Assert.AreEqual("Type1", result[0].RequestTypeDesc);
            Assert.AreEqual("Type2", result[1].RequestTypeDesc);
            Assert.AreEqual("Type3", result[2].RequestTypeDesc);
        }

        [Test]
        public async Task GetHousekeepingRequestTypes_WithWorkspace_ReturnsFilteredRequestTypes()
        {
            // Arrange
            var requestTypes = new List<HkRequestType>
            {
                new HkRequestType { RequestTypeId = 1, RequestTypeDesc = "Type1", Workspace = "WS1" },
                new HkRequestType { RequestTypeId = 2, RequestTypeDesc = "Type2", Workspace = "WS2" },
                new HkRequestType { RequestTypeId = 3, RequestTypeDesc = "Type3", Workspace = "WS1" }
            };

            var mockDbSet = requestTypes.AsQueryable().BuildMockDbSet();
            
            _mockDbContext
                .Setup(x => x.HousekeepingRequestTypes)
                .Returns(mockDbSet.Object);

            // Act
            var result = await _repository.GetHousekeepingRequestTypes("WS1");

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(2, result.Count);
            Assert.IsTrue(result.All(x => x.Workspace == "WS1"));
        }

        [Test]
        public async Task GetHousekeepingRequestTypes_WithEmptyWorkspace_ReturnsAllRequestTypes()
        {
            // Arrange
            var requestTypes = new List<HkRequestType>
            {
                new HkRequestType { RequestTypeId = 1, RequestTypeDesc = "Type1", Workspace = "WS1" },
                new HkRequestType { RequestTypeId = 2, RequestTypeDesc = "Type2", Workspace = "WS2" }
            };

            var mockDbSet = requestTypes.AsQueryable().BuildMockDbSet();
            
            _mockDbContext
                .Setup(x => x.HousekeepingRequestTypes)
                .Returns(mockDbSet.Object);

            // Act
            var result = await _repository.GetHousekeepingRequestTypes(string.Empty);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(2, result.Count);
        }

        [Test]
        public async Task GetHousekeepingRequestTypes_NoData_ReturnsEmptyList()
        {
            // Arrange
            var requestTypes = new List<HkRequestType>();
            var mockDbSet = requestTypes.AsQueryable().BuildMockDbSet();
            
            _mockDbContext
                .Setup(x => x.HousekeepingRequestTypes)
                .Returns(mockDbSet.Object);

            // Act
            var result = await _repository.GetHousekeepingRequestTypes();

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(0, result.Count);
        }

        [Test]
        public async Task GetHousekeepingRequests_ValidRequestTypeId_ReturnsRequests()
        {
            // Arrange
            var requestTypeId = 123;
            var expectedRequests = new List<HkRequest>
            {
                new HkRequest { RequestId = 1, LastHoldDate = DateTime.Now },
                new HkRequest { RequestId = 2, LastHoldDate = DateTime.Now.AddDays(-1) }
            };

            _mockDbContext
                .Setup(x => x.ExecuteStoredProcedureAsync<HkRequest, GetHkRequestIdListParameter>(
                    It.Is<GetHkRequestIdListParameter>(p => p.RequestTypeId == requestTypeId)))
                .ReturnsAsync(expectedRequests);

            // Act
            var result = await _repository.GetHousekeepingRequests(requestTypeId);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(2, result.Count);
            Assert.AreEqual(1, result[0].RequestId);
            Assert.AreEqual(2, result[1].RequestId);
            
            _mockDbContext.Verify(
                x => x.ExecuteStoredProcedureAsync<HkRequest, GetHkRequestIdListParameter>(
                    It.Is<GetHkRequestIdListParameter>(p => p.RequestTypeId == requestTypeId)),
                Times.Once);
        }

        [Test]
        public async Task GetHousekeepingRequests_NoResults_ReturnsEmptyList()
        {
            // Arrange
            var requestTypeId = 999;
            var expectedRequests = new List<HkRequest>();

            _mockDbContext
                .Setup(x => x.ExecuteStoredProcedureAsync<HkRequest, GetHkRequestIdListParameter>(
                    It.IsAny<GetHkRequestIdListParameter>()))
                .ReturnsAsync(expectedRequests);

            // Act
            var result = await _repository.GetHousekeepingRequests(requestTypeId);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(0, result.Count);
        }

        [Test]
        public async Task UpdateRequestHousekeeping_WithDate_ExecutesStoredProcedure()
        {
            // Arrange
            var requestTypeId = 456;
            var date = DateTime.Now;

            _mockDbContext
                .Setup(x => x.ExecuteStoredProcedureAsync(
                    It.Is<HoldRequestHkParameter>(p => 
                        p.RequestId == requestTypeId && 
                        p.LastHoldDate == date)))
                .Returns(Task.CompletedTask);

            // Act
            await _repository.UpdateRequestHousekeeping(requestTypeId, date);

            // Assert
            _mockDbContext.Verify(
                x => x.ExecuteStoredProcedureAsync(
                    It.Is<HoldRequestHkParameter>(p => 
                        p.RequestId == requestTypeId && 
                        p.LastHoldDate == date)),
                Times.Once);
        }

        [Test]
        public async Task UpdateRequestHousekeeping_WithNullDate_ExecutesStoredProcedure()
        {
            // Arrange
            var requestTypeId = 789;
            DateTime? date = null;

            _mockDbContext
                .Setup(x => x.ExecuteStoredProcedureAsync(
                    It.Is<HoldRequestHkParameter>(p => 
                        p.RequestId == requestTypeId && 
                        p.LastHoldDate == null)))
                .Returns(Task.CompletedTask);

            // Act
            await _repository.UpdateRequestHousekeeping(requestTypeId, date);

            // Assert
            _mockDbContext.Verify(
                x => x.ExecuteStoredProcedureAsync(
                    It.Is<HoldRequestHkParameter>(p => 
                        p.RequestId == requestTypeId && 
                        p.LastHoldDate == null)),
                Times.Once);
        }

        [Test]
        public async Task GetHousekeepingRequestTypes_VerifiesDbContextFactoryCalled()
        {
            // Arrange
            var requestTypes = new List<HkRequestType>();
            var mockDbSet = requestTypes.AsQueryable().BuildMockDbSet();
            
            _mockDbContext
                .Setup(x => x.HousekeepingRequestTypes)
                .Returns(mockDbSet.Object);

            // Act
            await _repository.GetHousekeepingRequestTypes();

            // Assert
            _mockDbContextFactory.Verify(
                x => x.CreateDbContextAsync(It.IsAny<CancellationToken>()), 
                Times.Once);
        }
    }

    // Test DbContext based on the BaseDbContext constructor
    public class TestDbContext : BaseDbContext
    {
        public TestDbContext(DbContextOptions options, ILoggerFactory loggerFactory) 
            : base(options, loggerFactory)
        {
        }

        public virtual DbSet<HkRequestType> HousekeepingRequestTypes { get; set; }
        
        public virtual Task<IList<T>> ExecuteStoredProcedureAsync<T, TParameter>(TParameter parameter)
        {
            throw new NotImplementedException("This should be mocked in tests");
        }
        
        public virtual Task ExecuteStoredProcedureAsync<TParameter>(TParameter parameter)
        {
            throw new NotImplementedException("This should be mocked in tests");
        }
    }
}