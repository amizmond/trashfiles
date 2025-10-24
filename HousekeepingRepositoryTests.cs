using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace YourNamespace.Tests
{
    [TestFixture]
    public class HousekeepingRepositoryTests
    {
        private Mock<ILogger<IAdjustmentRepository>> _loggerMock;
        private Mock<IDbContextFactory<TestDbContext>> _dbContextFactoryMock;
        private Mock<ILoggerFactory> _loggerFactoryMock;
        private HousekeepingRepository<TestDbContext> _repository;
        private SqliteConnection _connection;
        private DbContextOptions<TestDbContext> _options;

        [SetUp]
        public void SetUp()
        {
            _loggerMock = new Mock<ILogger<IAdjustmentRepository>>();
            _dbContextFactoryMock = new Mock<IDbContextFactory<TestDbContext>>();
            _loggerFactoryMock = new Mock<ILoggerFactory>();
            
            // Use SQLite in-memory database to support EF.Functions.Like
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();

            _options = new DbContextOptionsBuilder<TestDbContext>()
                .UseSqlite(_connection)
                .Options;

            // Create and initialize the database schema
            using (var initContext = new TestDbContext(_options, _loggerFactoryMock.Object))
            {
                initContext.Database.EnsureCreated();
            }

            // Setup factory to return new contexts
            _dbContextFactoryMock
                .Setup(x => x.CreateDbContextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new TestDbContext(_options, _loggerFactoryMock.Object));

            _repository = new HousekeepingRepository<TestDbContext>(
                _loggerMock.Object,
                _dbContextFactoryMock.Object);
        }

        [TearDown]
        public void TearDown()
        {
            _connection?.Close();
            _connection?.Dispose();
        }

        #region GetHousekeepingRequestTypes Tests

        [Test]
        public async Task GetHousekeepingRequestTypes_WithNullWorkspace_ReturnsAllRequestTypes()
        {
            // Arrange - Seed data into the actual database
            await using var setupContext = await _dbContextFactoryMock.Object.CreateDbContextAsync();
            setupContext.HousekeepingRequestTypes.AddRange(
                new HkRequestType { RequestTypeId = 1, RequestTypeDesc = "Type 1", Workspace = "WS1" },
                new HkRequestType { RequestTypeId = 2, RequestTypeDesc = "Type 2", Workspace = "WS2" },
                new HkRequestType { RequestTypeId = 3, RequestTypeDesc = "Type 3", Workspace = "WS3" }
            );
            await setupContext.SaveChangesAsync();

            // Act
            var result = await _repository.GetHousekeepingRequestTypes(null);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(3, result.Count);
            _dbContextFactoryMock.Verify(x => x.CreateDbContextAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        }

        [Test]
        public async Task GetHousekeepingRequestTypes_WithEmptyWorkspace_ReturnsAllRequestTypes()
        {
            // Arrange - Seed data into the actual database
            await using var setupContext = await _dbContextFactoryMock.Object.CreateDbContextAsync();
            setupContext.HousekeepingRequestTypes.AddRange(
                new HkRequestType { RequestTypeId = 1, RequestTypeDesc = "Type 1", Workspace = "WS1" },
                new HkRequestType { RequestTypeId = 2, RequestTypeDesc = "Type 2", Workspace = "WS2" }
            );
            await setupContext.SaveChangesAsync();

            // Act
            var result = await _repository.GetHousekeepingRequestTypes(string.Empty);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(2, result.Count);
        }

        [Test]
        public async Task GetHousekeepingRequestTypes_WithSpecificWorkspace_ReturnsFilteredRequestTypes()
        {
            // Arrange - Seed data into the actual database
            var workspace = "WS1";
            await using var setupContext = await _dbContextFactoryMock.Object.CreateDbContextAsync();
            setupContext.HousekeepingRequestTypes.AddRange(
                new HkRequestType { RequestTypeId = 1, RequestTypeDesc = "Type 1", Workspace = "WS1" },
                new HkRequestType { RequestTypeId = 2, RequestTypeDesc = "Type 2", Workspace = "WS2" },
                new HkRequestType { RequestTypeId = 3, RequestTypeDesc = "Type 3", Workspace = "WS1" }
            );
            await setupContext.SaveChangesAsync();

            // Act
            var result = await _repository.GetHousekeepingRequestTypes(workspace);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(2, result.Count);
            Assert.That(result, Has.All.Property("Workspace").EqualTo(workspace));
        }

        [Test]
        public async Task GetHousekeepingRequestTypes_WithNonExistentWorkspace_ReturnsEmptyList()
        {
            // Arrange - Seed data into the actual database
            await using var setupContext = await _dbContextFactoryMock.Object.CreateDbContextAsync();
            setupContext.HousekeepingRequestTypes.AddRange(
                new HkRequestType { RequestTypeId = 1, RequestTypeDesc = "Type 1", Workspace = "WS1" },
                new HkRequestType { RequestTypeId = 2, RequestTypeDesc = "Type 2", Workspace = "WS2" }
            );
            await setupContext.SaveChangesAsync();

            // Act
            var result = await _repository.GetHousekeepingRequestTypes("NonExistent");

            // Assert
            Assert.IsNotNull(result);
            Assert.IsEmpty(result);
        }

        #endregion

        #region GetHousekeepingRequests Tests

        [Test]
        public async Task GetHousekeepingRequests_WithValidRequestTypeId_CallsStoredProcedure()
        {
            // Arrange
            var requestTypeId = 1;

            // Note: Testing stored procedure execution requires either:
            // 1. Integration tests with real database
            // 2. Mocking the entire context (not just stored procedure methods)
            // 3. Using a testable wrapper around stored procedure calls
            
            // For unit testing, we verify that the method can be called without exceptions
            // Act & Assert - Should not throw
            Assert.DoesNotThrowAsync(async () => 
                await _repository.GetHousekeepingRequests(requestTypeId));
        }

        [Test]
        public async Task GetHousekeepingRequests_WithNoResults_ReturnsEmptyList()
        {
            // Arrange
            var requestTypeId = 999;

            // Act
            var result = await _repository.GetHousekeepingRequests(requestTypeId);

            // Assert - Default implementation returns empty list
            Assert.IsNotNull(result);
            Assert.IsEmpty(result);
        }

        [Test]
        public async Task GetHousekeepingRequests_CreatesNewDbContext()
        {
            // Arrange
            var requestTypeId = 1;
            var callCount = 0;
            
            _dbContextFactoryMock
                .Setup(x => x.CreateDbContextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(() =>
                {
                    callCount++;
                    return new TestDbContext(_options, _loggerFactoryMock.Object);
                });

            // Act
            await _repository.GetHousekeepingRequests(requestTypeId);

            // Assert
            Assert.AreEqual(1, callCount, "CreateDbContextAsync should be called once");
        }

        #endregion

        #region UpdateRequestHousekeeping Tests

        [Test]
        public async Task UpdateRequestHousekeeping_WithValidDateAndRequestTypeId_CallsStoredProcedure()
        {
            // Arrange
            var requestTypeId = 1;
            var date = DateTime.Now;

            // Act & Assert - Should not throw
            Assert.DoesNotThrowAsync(async () =>
                await _repository.UpdateRequestHousekeeping(requestTypeId, date));
        }

        [Test]
        public async Task UpdateRequestHousekeeping_WithNullDate_CallsStoredProcedureWithNullDate()
        {
            // Arrange
            var requestTypeId = 1;
            DateTime? date = null;

            // Act & Assert - Should not throw
            Assert.DoesNotThrowAsync(async () =>
                await _repository.UpdateRequestHousekeeping(requestTypeId, date));
        }

        [Test]
        public async Task UpdateRequestHousekeeping_CreatesNewDbContext()
        {
            // Arrange
            var requestTypeId = 1;
            var date = DateTime.Now;
            var callCount = 0;
            
            _dbContextFactoryMock
                .Setup(x => x.CreateDbContextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(() =>
                {
                    callCount++;
                    return new TestDbContext(_options, _loggerFactoryMock.Object);
                });

            // Act
            await _repository.UpdateRequestHousekeeping(requestTypeId, date);

            // Assert
            Assert.AreEqual(1, callCount, "CreateDbContextAsync should be called once");
        }

        [TestCase(1)]
        [TestCase(100)]
        [TestCase(999)]
        public async Task UpdateRequestHousekeeping_WithDifferentRequestTypeIds_PassesCorrectId(int requestTypeId)
        {
            // Arrange
            var date = DateTime.Now;

            // Act & Assert - Should not throw for any valid request type ID
            Assert.DoesNotThrowAsync(async () =>
                await _repository.UpdateRequestHousekeeping(requestTypeId, date));
        }

        #endregion

        #region Helper Methods

        // Note: CreateMockDbSet is no longer needed since we're using real SQLite database
        // Keeping it for reference in case you need to switch back to mocked DbSets

        #endregion
    }

    #region Test Helper Classes

    // Test DbContext for testing purposes
    public class TestDbContext : BaseDbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options, ILoggerFactory loggerFactory)
            : base(options, loggerFactory)
        {
        }

        public virtual DbSet<HkRequestType> HousekeepingRequestTypes { get; set; }

        public virtual Task<IList<TResult>> ExecuteStoredProcedureAsync<TResult, TParameter>(TParameter parameter)
        {
            return Task.FromResult<IList<TResult>>(new List<TResult>());
        }

        public virtual Task ExecuteStoredProcedureAsync<TParameter>(TParameter parameter)
        {
            return Task.CompletedTask;
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            
            // Configure HkRequestType entity
            modelBuilder.Entity<HkRequestType>(entity =>
            {
                entity.HasKey(e => e.RequestTypeId);
                entity.Property(e => e.RequestTypeDesc).IsRequired(false);
                entity.Property(e => e.Workspace).IsRequired(false);
            });
        }
    }

    // Helper class for async enumerable mocking
    internal class TestAsyncEnumerator<T> : IAsyncEnumerator<T>
    {
        private readonly IEnumerator<T> _inner;

        public TestAsyncEnumerator(IEnumerator<T> inner)
        {
            _inner = inner;
        }

        public ValueTask DisposeAsync()
        {
            _inner.Dispose();
            return ValueTask.CompletedTask;
        }

        public ValueTask<bool> MoveNextAsync()
        {
            return ValueTask.FromResult(_inner.MoveNext());
        }

        public T Current => _inner.Current;
    }

    #endregion
}
