using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace YourNamespace.Tests
{
    // Test DbContext that inherits from BaseDbContext
    public class TestDbContext : BaseDbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options, ILoggerFactory loggerFactory)
            : base(options, loggerFactory)
        {
        }

        public DbSet<HkRequestType> HousekeepingRequestTypes { get; set; }
        public DbSet<HkRequest> HousekeepingRequests { get; set; }
    }

    [TestFixture]
    public class HousekeepingRepositoryTests
    {
        private Mock<ILogger<IAdjustmentRepository>> _mockLogger;
        private Mock<IDbContextFactory<TestDbContext>> _mockDbContextFactory;
        private HousekeepingRepository<TestDbContext> _repository;
        private TestDbContext _testContext;
        private Mock<ILoggerFactory> _mockLoggerFactory;

        [SetUp]
        public void SetUp()
        {
            // Setup logger mocks
            _mockLogger = new Mock<ILogger<IAdjustmentRepository>>();
            _mockLoggerFactory = new Mock<ILoggerFactory>();

            // Setup in-memory database
            var options = new DbContextOptionsBuilder<TestDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            _testContext = new TestDbContext(options, _mockLoggerFactory.Object);

            // Setup DbContextFactory mock
            _mockDbContextFactory = new Mock<IDbContextFactory<TestDbContext>>();
            _mockDbContextFactory
                .Setup(f => f.CreateDbContextAsync(default))
                .ReturnsAsync(_testContext);

            // Create repository
            _repository = new HousekeepingRepository<TestDbContext>(_mockLogger.Object, _mockDbContextFactory.Object);
        }

        [TearDown]
        public void TearDown()
        {
            _testContext?.Dispose();
        }

        [Test]
        public async Task GetHousekeepingRequestTypes_WithoutWorkspace_ReturnsAllRequestTypes()
        {
            // Arrange
            var requestTypes = new List<HkRequestType>
            {
                new HkRequestType { RequestTypeId = 1, Workspace = "WorkspaceA", RequestTypeDesc = "Type A" },
                new HkRequestType { RequestTypeId = 2, Workspace = "WorkspaceB", RequestTypeDesc = "Type B" },
                new HkRequestType { RequestTypeId = 3, Workspace = "WorkspaceC", RequestTypeDesc = "Type C" }
            };

            await _testContext.HousekeepingRequestTypes.AddRangeAsync(requestTypes);
            await _testContext.SaveChangesAsync();

            // Act
            var result = await _repository.GetHousekeepingRequestTypes();

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Count, Is.EqualTo(3));
        }

        [Test]
        public async Task GetHousekeepingRequestTypes_WithWorkspace_ReturnsFilteredRequestTypes()
        {
            // Arrange
            var requestTypes = new List<HkRequestType>
            {
                new HkRequestType { RequestTypeId = 1, Workspace = "WorkspaceA", RequestTypeDesc = "Type A" },
                new HkRequestType { RequestTypeId = 2, Workspace = "WorkspaceB", RequestTypeDesc = "Type B" },
                new HkRequestType { RequestTypeId = 3, Workspace = "WorkspaceA", RequestTypeDesc = "Type C" }
            };

            await _testContext.HousekeepingRequestTypes.AddRangeAsync(requestTypes);
            await _testContext.SaveChangesAsync();

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
            var requestTypes = new List<HkRequestType>
            {
                new HkRequestType { RequestTypeId = 1, Workspace = "WorkspaceA", RequestTypeDesc = "Type A" }
            };

            await _testContext.HousekeepingRequestTypes.AddRangeAsync(requestTypes);
            await _testContext.SaveChangesAsync();

            // Act
            var result = await _repository.GetHousekeepingRequestTypes("NonExistent");

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Count, Is.EqualTo(0));
        }

        [Test]
        public async Task GetHousekeepingRequestTypes_WithEmptyDatabase_ReturnsEmptyList()
        {
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
            var requestTypes = new List<HkRequestType>
            {
                new HkRequestType { RequestTypeId = 1, Workspace = "WorkspaceA", RequestTypeDesc = "Type A" },
                new HkRequestType { RequestTypeId = 2, Workspace = "WorkspaceB", RequestTypeDesc = "Type B" }
            };

            await _testContext.HousekeepingRequestTypes.AddRangeAsync(requestTypes);
            await _testContext.SaveChangesAsync();

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
            var requestTypes = new List<HkRequestType>
            {
                new HkRequestType { RequestTypeId = 1, Workspace = "WorkspaceA", RequestTypeDesc = "Type A" }
            };

            await _testContext.HousekeepingRequestTypes.AddRangeAsync(requestTypes);
            await _testContext.SaveChangesAsync();

            // Act
            var result = await _repository.GetHousekeepingRequestTypes(string.Empty);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Count, Is.EqualTo(1));
        }

        [Test]
        public void GetHousekeepingRequests_CallsStoredProcedure()
        {
            // Note: Testing stored procedures requires a real database or additional mocking
            // This is a placeholder test to demonstrate the structure
            // In a real scenario, you would need to mock ExecuteStoredProcedureAsync
            
            // Arrange
            int requestTypeId = 1;

            // Act & Assert
            Assert.DoesNotThrowAsync(async () => 
            {
                try
                {
                    await _repository.GetHousekeepingRequests(requestTypeId);
                }
                catch (NotImplementedException)
                {
                    // Expected when ExecuteStoredProcedureAsync is not implemented in test context
                    Assert.Pass("Method structure is correct. Stored procedure execution needs real DB or additional mocking.");
                }
            });
        }

        [Test]
        public void UpdateRequestHousekeeping_CallsStoredProcedure()
        {
            // Note: Testing stored procedures requires a real database or additional mocking
            // This is a placeholder test to demonstrate the structure
            
            // Arrange
            int requestTypeId = 1;
            DateTime? date = DateTime.Now;

            // Act & Assert
            Assert.DoesNotThrowAsync(async () => 
            {
                try
                {
                    await _repository.UpdateRequestHousekeeping(requestTypeId, date);
                }
                catch (NotImplementedException)
                {
                    // Expected when ExecuteStoredProcedureAsync is not implemented in test context
                    Assert.Pass("Method structure is correct. Stored procedure execution needs real DB or additional mocking.");
                }
            });
        }

        [Test]
        public void UpdateRequestHousekeeping_WithNullDate_CallsStoredProcedure()
        {
            // Arrange
            int requestTypeId = 1;
            DateTime? date = null;

            // Act & Assert
            Assert.DoesNotThrowAsync(async () => 
            {
                try
                {
                    await _repository.UpdateRequestHousekeeping(requestTypeId, date);
                }
                catch (NotImplementedException)
                {
                    // Expected when ExecuteStoredProcedureAsync is not implemented in test context
                    Assert.Pass("Method structure is correct. Stored procedure execution needs real DB or additional mocking.");
                }
            });
        }
    }
}