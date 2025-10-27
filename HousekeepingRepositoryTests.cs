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
        private Mock<DbSet<HkRequestType>> _mockDbSet;
        private HousekeepingRepository<TestDbContext> _repository;

        [SetUp]
        public void SetUp()
        {
            _mockLogger = new Mock<ILogger<IAdjustmentRepository>>();
            _mockDbContextFactory = new Mock<IDbContextFactory<TestDbContext>>();
            _mockDbContext = new Mock<TestDbContext>();
            _mockDbSet = new Mock<DbSet<HkRequestType>>();

            _repository = new HousekeepingRepository<TestDbContext>(
                _mockLogger.Object,
                _mockDbContextFactory.Object);
        }

        [Test]
        public async Task GetHousekeepingRequestTypes_WithoutWorkspace_ReturnsAllRequestTypes()
        {
            // Arrange
            var requestTypes = new List<HkRequestType>
            {
                new HkRequestType { RequestTypeId = 1, RequestTypeDesc = "Type1", Workspace = "WS1" },
                new HkRequestType { RequestTypeId = 2, RequestTypeDesc = "Type2", Workspace = "WS2" }
            }.AsQueryable();

            var mockDbSet = CreateMockDbSet(requestTypes);
            _mockDbContext.Setup(c => c.HousekeepingRequestTypes).Returns(mockDbSet.Object);
            _mockDbContextFactory.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(_mockDbContext.Object);

            // Act
            var result = await _repository.GetHousekeepingRequestTypes();

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(2, result.Count);
            Assert.AreEqual(1, result[0].RequestTypeId);
            Assert.AreEqual(2, result[1].RequestTypeId);
        }

        [Test]
        public async Task GetHousekeepingRequestTypes_WithWorkspace_ReturnsFilteredRequestTypes()
        {
            // Arrange
            var requestTypes = new List<HkRequestType>
            {
                new HkRequestType { RequestTypeId = 1, RequestTypeDesc = "Type1", Workspace = "WS1" },
                new HkRequestType { RequestTypeId = 2, RequestTypeDesc = "Type2", Workspace = "WS2" }
            }.AsQueryable();

            var mockDbSet = CreateMockDbSet(requestTypes);
            _mockDbContext.Setup(c => c.HousekeepingRequestTypes).Returns(mockDbSet.Object);
            _mockDbContextFactory.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(_mockDbContext.Object);

            // Act
            var result = await _repository.GetHousekeepingRequestTypes("WS1");

            // Assert
            Assert.IsNotNull(result);
            _mockDbContextFactory.Verify(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public async Task GetHousekeepingRequestTypes_WithEmptyWorkspace_ReturnsAllRequestTypes()
        {
            // Arrange
            var requestTypes = new List<HkRequestType>
            {
                new HkRequestType { RequestTypeId = 1, RequestTypeDesc = "Type1", Workspace = "WS1" }
            }.AsQueryable();

            var mockDbSet = CreateMockDbSet(requestTypes);
            _mockDbContext.Setup(c => c.HousekeepingRequestTypes).Returns(mockDbSet.Object);
            _mockDbContextFactory.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(_mockDbContext.Object);

            // Act
            var result = await _repository.GetHousekeepingRequestTypes(string.Empty);

            // Assert
            Assert.IsNotNull(result);
            _mockDbContextFactory.Verify(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public async Task GetHousekeepingRequests_ReturnsRequestList()
        {
            // Arrange
            var expectedRequests = new List<HkRequest>
            {
                new HkRequest { RequestId = 1, LastHoldDate = DateTime.Now },
                new HkRequest { RequestId = 2, LastHoldDate = DateTime.Now.AddDays(1) }
            };

            _mockDbContext.Setup(c => c.ExecuteStoredProcedureAsync<HkRequest, GetHkRequestIdListParameter>(
                    It.IsAny<GetHkRequestIdListParameter>()))
                .ReturnsAsync(expectedRequests);

            _mockDbContextFactory.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(_mockDbContext.Object);

            // Act
            var result = await _repository.GetHousekeepingRequests(123);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(2, result.Count);
            Assert.AreEqual(1, result[0].RequestId);
            Assert.AreEqual(2, result[1].RequestId);
            _mockDbContext.Verify(c => c.ExecuteStoredProcedureAsync<HkRequest, GetHkRequestIdListParameter>(
                It.Is<GetHkRequestIdListParameter>(p => p.RequestTypeId == 123)), Times.Once);
        }

        [Test]
        public async Task GetHousekeepingRequests_WithZeroRequestTypeId_CallsStoredProcedure()
        {
            // Arrange
            var expectedRequests = new List<HkRequest>();

            _mockDbContext.Setup(c => c.ExecuteStoredProcedureAsync<HkRequest, GetHkRequestIdListParameter>(
                    It.IsAny<GetHkRequestIdListParameter>()))
                .ReturnsAsync(expectedRequests);

            _mockDbContextFactory.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(_mockDbContext.Object);

            // Act
            var result = await _repository.GetHousekeepingRequests(0);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(0, result.Count);
            _mockDbContext.Verify(c => c.ExecuteStoredProcedureAsync<HkRequest, GetHkRequestIdListParameter>(
                It.Is<GetHkRequestIdListParameter>(p => p.RequestTypeId == 0)), Times.Once);
        }

        [Test]
        public async Task UpdateRequestHousekeeping_WithDate_ExecutesStoredProcedure()
        {
            // Arrange
            var testDate = DateTime.Now;
            _mockDbContext.Setup(c => c.ExecuteStoredProcedureAsync(It.IsAny<HoldRequestHkParameter>()))
                .Returns(Task.CompletedTask);

            _mockDbContextFactory.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(_mockDbContext.Object);

            // Act
            await _repository.UpdateRequestHousekeeping(456, testDate);

            // Assert
            _mockDbContext.Verify(c => c.ExecuteStoredProcedureAsync(
                It.Is<HoldRequestHkParameter>(p => p.RequestId == 456 && p.LastHoldDate == testDate)), Times.Once);
        }

        [Test]
        public async Task UpdateRequestHousekeeping_WithNullDate_ExecutesStoredProcedure()
        {
            // Arrange
            _mockDbContext.Setup(c => c.ExecuteStoredProcedureAsync(It.IsAny<HoldRequestHkParameter>()))
                .Returns(Task.CompletedTask);

            _mockDbContextFactory.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(_mockDbContext.Object);

            // Act
            await _repository.UpdateRequestHousekeeping(789, null);

            // Assert
            _mockDbContext.Verify(c => c.ExecuteStoredProcedureAsync(
                It.Is<HoldRequestHkParameter>(p => p.RequestId == 789 && p.LastHoldDate == null)), Times.Once);
        }

        [Test]
        public void HkRequestType_Properties_CanBeSetAndRetrieved()
        {
            // Arrange & Act
            var hkRequestType = new HkRequestType
            {
                Workspace = "TestWorkspace",
                RequestTypeId = 100,
                RequestTypeDesc = "TestDescription"
            };

            // Assert
            Assert.AreEqual("TestWorkspace", hkRequestType.Workspace);
            Assert.AreEqual(100, hkRequestType.RequestTypeId);
            Assert.AreEqual("TestDescription", hkRequestType.RequestTypeDesc);
        }

        [Test]
        public void HkRequest_Properties_CanBeSetAndRetrieved()
        {
            // Arrange & Act
            var testDate = DateTime.Now;
            var hkRequest = new HkRequest
            {
                RequestId = 200,
                LastHoldDate = testDate
            };

            // Assert
            Assert.AreEqual(200, hkRequest.RequestId);
            Assert.AreEqual(testDate, hkRequest.LastHoldDate);
        }

        [Test]
        public void GetHkRequestIdListParameter_Constructor_SetsRequestTypeId()
        {
            // Act
            var parameter = new GetHkRequestIdListParameter(999);

            // Assert
            Assert.AreEqual(999, parameter.RequestTypeId);
        }

        [Test]
        public void HoldRequestHkParameter_Constructor_WithDate_SetsProperties()
        {
            // Arrange
            var testDate = DateTime.Now;

            // Act
            var parameter = new HoldRequestHkParameter(555, testDate);

            // Assert
            Assert.AreEqual(555, parameter.RequestId);
            Assert.AreEqual(testDate, parameter.LastHoldDate);
        }

        [Test]
        public void HoldRequestHkParameter_Constructor_WithNullDate_SetsRequestIdOnly()
        {
            // Act
            var parameter = new HoldRequestHkParameter(666, null);

            // Assert
            Assert.AreEqual(666, parameter.RequestId);
            Assert.IsNull(parameter.LastHoldDate);
        }

        [Test]
        public void RequestHkHold_Properties_CanBeSetAndRetrieved()
        {
            // Arrange & Act
            var testDate = DateTime.Now;
            var requestHkHold = new RequestHkHold
            {
                RequestId = 777,
                LastHoldDate = testDate
            };

            // Assert
            Assert.AreEqual(777, requestHkHold.RequestId);
            Assert.AreEqual(testDate, requestHkHold.LastHoldDate);
        }

        private Mock<DbSet<T>> CreateMockDbSet<T>(IQueryable<T> data) where T : class
        {
            var mockSet = new Mock<DbSet<T>>();
            mockSet.As<IAsyncEnumerable<T>>()
                .Setup(m => m.GetAsyncEnumerator(It.IsAny<CancellationToken>()))
                .Returns(new TestAsyncEnumerator<T>(data.GetEnumerator()));

            mockSet.As<IQueryable<T>>()
                .Setup(m => m.Provider)
                .Returns(new TestAsyncQueryProvider<T>(data.Provider));

            mockSet.As<IQueryable<T>>().Setup(m => m.Expression).Returns(data.Expression);
            mockSet.As<IQueryable<T>>().Setup(m => m.ElementType).Returns(data.ElementType);
            mockSet.As<IQueryable<T>>().Setup(m => m.GetEnumerator()).Returns(data.GetEnumerator());

            return mockSet;
        }
    }

    // Helper classes for async query support
    internal class TestAsyncEnumerator<T> : IAsyncEnumerator<T>
    {
        private readonly IEnumerator<T> _inner;

        public TestAsyncEnumerator(IEnumerator<T> inner)
        {
            _inner = inner;
        }

        public T Current => _inner.Current;

        public ValueTask<bool> MoveNextAsync()
        {
            return new ValueTask<bool>(_inner.MoveNext());
        }

        public ValueTask DisposeAsync()
        {
            _inner.Dispose();
            return new ValueTask();
        }
    }

    internal class TestAsyncQueryProvider<TEntity> : IAsyncQueryProvider
    {
        private readonly IQueryProvider _inner;

        internal TestAsyncQueryProvider(IQueryProvider inner)
        {
            _inner = inner;
        }

        public IQueryable CreateQuery(System.Linq.Expressions.Expression expression)
        {
            return new TestAsyncEnumerable<TEntity>(expression);
        }

        public IQueryable<TElement> CreateQuery<TElement>(System.Linq.Expressions.Expression expression)
        {
            return new TestAsyncEnumerable<TElement>(expression);
        }

        public object Execute(System.Linq.Expressions.Expression expression)
        {
            return _inner.Execute(expression);
        }

        public TResult Execute<TResult>(System.Linq.Expressions.Expression expression)
        {
            return _inner.Execute<TResult>(expression);
        }

        public TResult ExecuteAsync<TResult>(System.Linq.Expressions.Expression expression, CancellationToken cancellationToken = default)
        {
            var resultType = typeof(TResult).GetGenericArguments()[0];
            var executeMethod = typeof(IQueryProvider)
                .GetMethod(nameof(IQueryProvider.Execute), 1, new[] { typeof(System.Linq.Expressions.Expression) })
                .MakeGenericMethod(resultType);

            var result = executeMethod.Invoke(_inner, new[] { expression });
            return (TResult)typeof(Task).GetMethod(nameof(Task.FromResult))
                .MakeGenericMethod(resultType)
                .Invoke(null, new[] { result });
        }
    }

    internal class TestAsyncEnumerable<T> : EnumerableQuery<T>, IAsyncEnumerable<T>, IQueryable<T>
    {
        public TestAsyncEnumerable(System.Linq.Expressions.Expression expression)
            : base(expression)
        {
        }

        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            return new TestAsyncEnumerator<T>(this.AsEnumerable().GetEnumerator());
        }

        IQueryProvider IQueryable.Provider => new TestAsyncQueryProvider<T>(this);
    }

    // Mock DbContext for testing
    public class TestDbContext : BaseDbContext
    {
        public virtual DbSet<HkRequestType> HousekeepingRequestTypes { get; set; }

        public virtual Task<IList<TResult>> ExecuteStoredProcedureAsync<TResult, TParameter>(TParameter parameter)
        {
            throw new NotImplementedException();
        }

        public virtual Task ExecuteStoredProcedureAsync<TParameter>(TParameter parameter)
        {
            throw new NotImplementedException();
        }
    }

    // Mock base class
    public abstract class BaseDbContext : DbContext
    {
        protected BaseDbContext()
        {
        }

        protected BaseDbContext(DbContextOptions options) : base(options)
        {
        }
    }

    // Mock interfaces and base repository
    public interface IAdjustmentRepository { }

    public interface IHousekeepingRepository { }

    public abstract class BaseRepository<TDbContext> where TDbContext : BaseDbContext
    {
        protected readonly ILogger<IAdjustmentRepository> Logger;
        protected readonly IDbContextFactory<TDbContext> DbContextFactory;

        protected BaseRepository(ILogger<IAdjustmentRepository> logger, IDbContextFactory<TDbContext> dbContextFactory)
        {
            Logger = logger;
            DbContextFactory = dbContextFactory;
        }
    }

    // Mock attribute
    public class StoredProcNameAttribute : Attribute
    {
        public string Name { get; }
        public StoredProcNameAttribute(string name)
        {
            Name = name;
        }
    }
}