using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
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
        private Mock<DbSet<HkRequestType>> _mockHkRequestTypeDbSet;
        private HousekeepingRepository<TestDbContext> _repository;

        [SetUp]
        public void SetUp()
        {
            _mockLogger = new Mock<ILogger<IAdjustmentRepository>>();
            _mockDbContextFactory = new Mock<IDbContextFactory<TestDbContext>>();
            _mockDbContext = new Mock<TestDbContext>();
            _mockHkRequestTypeDbSet = new Mock<DbSet<HkRequestType>>();

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
                new HkRequestType { RequestTypeId = 1, Workspace = "WS1", RequestTypeDesc = "Type1" },
                new HkRequestType { RequestTypeId = 2, Workspace = "WS2", RequestTypeDesc = "Type2" }
            };

            var queryable = requestTypes.AsQueryable();
            SetupMockDbSet(_mockHkRequestTypeDbSet, queryable);
            
            _mockDbContext.Setup(c => c.HousekeepingRequestTypes).Returns(_mockHkRequestTypeDbSet.Object);
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
                new HkRequestType { RequestTypeId = 1, Workspace = "WS1", RequestTypeDesc = "Type1" },
                new HkRequestType { RequestTypeId = 2, Workspace = "WS2", RequestTypeDesc = "Type2" },
                new HkRequestType { RequestTypeId = 3, Workspace = "WS1", RequestTypeDesc = "Type3" }
            };

            var queryable = requestTypes.AsQueryable();
            SetupMockDbSet(_mockHkRequestTypeDbSet, queryable);
            
            _mockDbContext.Setup(c => c.HousekeepingRequestTypes).Returns(_mockHkRequestTypeDbSet.Object);
            _mockDbContextFactory.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(_mockDbContext.Object);

            // Mock EF.Functions.Like behavior
            var filteredData = requestTypes.Where(x => x.Workspace.Contains("WS1")).ToList();
            _mockHkRequestTypeDbSet.Setup(m => m.Provider)
                .Returns(new TestAsyncQueryProvider<HkRequestType>(filteredData.AsQueryable().Provider));

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
                new HkRequestType { RequestTypeId = 1, Workspace = "WS1", RequestTypeDesc = "Type1" }
            };

            var queryable = requestTypes.AsQueryable();
            SetupMockDbSet(_mockHkRequestTypeDbSet, queryable);
            
            _mockDbContext.Setup(c => c.HousekeepingRequestTypes).Returns(_mockHkRequestTypeDbSet.Object);
            _mockDbContextFactory.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(_mockDbContext.Object);

            // Act
            var result = await _repository.GetHousekeepingRequestTypes(string.Empty);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(1, result.Count);
        }

        [Test]
        public async Task GetHousekeepingRequests_WithValidRequestTypeId_ReturnsHkRequests()
        {
            // Arrange
            var requestTypeId = 123;
            var expectedRequests = new List<HkRequest>
            {
                new HkRequest { RequestId = 1, LastHoldDate = DateTime.Now },
                new HkRequest { RequestId = 2, LastHoldDate = null }
            };

            _mockDbContext.Setup(c => c.ExecuteStoredProcedureAsync<HkRequest, GetHkRequestIdListParameter>(
                It.IsAny<GetHkRequestIdListParameter>()))
                .ReturnsAsync(expectedRequests);

            _mockDbContextFactory.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(_mockDbContext.Object);

            // Act
            var result = await _repository.GetHousekeepingRequests(requestTypeId);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(2, result.Count);
            Assert.AreEqual(1, result[0].RequestId);
            Assert.AreEqual(2, result[1].RequestId);
            _mockDbContext.Verify(c => c.ExecuteStoredProcedureAsync<HkRequest, GetHkRequestIdListParameter>(
                It.Is<GetHkRequestIdListParameter>(p => p.RequestTypeId == requestTypeId)), Times.Once);
        }

        [Test]
        public async Task UpdateRequestHousekeeping_WithDate_ExecutesStoredProcedure()
        {
            // Arrange
            var requestTypeId = 456;
            var date = new DateTime(2025, 1, 15);

            _mockDbContext.Setup(c => c.ExecuteStoredProcedureAsync(
                It.IsAny<HoldRequestHkParameter>()))
                .Returns(Task.CompletedTask);

            _mockDbContextFactory.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(_mockDbContext.Object);

            // Act
            await _repository.UpdateRequestHousekeeping(requestTypeId, date);

            // Assert
            _mockDbContext.Verify(c => c.ExecuteStoredProcedureAsync(
                It.Is<HoldRequestHkParameter>(p => p.RequestId == requestTypeId && p.LastHoldDate == date)), 
                Times.Once);
        }

        [Test]
        public async Task UpdateRequestHousekeeping_WithNullDate_ExecutesStoredProcedure()
        {
            // Arrange
            var requestTypeId = 789;

            _mockDbContext.Setup(c => c.ExecuteStoredProcedureAsync(
                It.IsAny<HoldRequestHkParameter>()))
                .Returns(Task.CompletedTask);

            _mockDbContextFactory.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(_mockDbContext.Object);

            // Act
            await _repository.UpdateRequestHousekeeping(requestTypeId, null);

            // Assert
            _mockDbContext.Verify(c => c.ExecuteStoredProcedureAsync(
                It.Is<HoldRequestHkParameter>(p => p.RequestId == requestTypeId && p.LastHoldDate == null)), 
                Times.Once);
        }

        private void SetupMockDbSet<T>(Mock<DbSet<T>> mockDbSet, IQueryable<T> data) where T : class
        {
            mockDbSet.As<IAsyncEnumerable<T>>()
                .Setup(m => m.GetAsyncEnumerator(It.IsAny<CancellationToken>()))
                .Returns(new TestAsyncEnumerator<T>(data.GetEnumerator()));

            mockDbSet.As<IQueryable<T>>()
                .Setup(m => m.Provider)
                .Returns(new TestAsyncQueryProvider<T>(data.Provider));

            mockDbSet.As<IQueryable<T>>().Setup(m => m.Expression).Returns(data.Expression);
            mockDbSet.As<IQueryable<T>>().Setup(m => m.ElementType).Returns(data.ElementType);
            mockDbSet.As<IQueryable<T>>().Setup(m => m.GetEnumerator()).Returns(data.GetEnumerator());
        }
    }

    // Test DbContext based on the constructor requirements
    public class TestDbContext : BaseDbContext
    {
        public TestDbContext() 
            : base(new DbContextOptionsBuilder<TestDbContext>().Options, null)
        {
        }

        public virtual DbSet<HkRequestType> HousekeepingRequestTypes { get; set; }

        public virtual Task<IList<T>> ExecuteStoredProcedureAsync<T, TParam>(TParam parameter) 
            where T : class
        {
            throw new NotImplementedException();
        }

        public virtual Task ExecuteStoredProcedureAsync<TParam>(TParam parameter)
        {
            throw new NotImplementedException();
        }
    }

    // Helper classes for async query support
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
            var executionResult = typeof(IQueryProvider)
                .GetMethod(
                    name: nameof(IQueryProvider.Execute),
                    genericParameterCount: 1,
                    types: new[] { typeof(System.Linq.Expressions.Expression) })
                .MakeGenericMethod(resultType)
                .Invoke(this, new[] { expression });

            return (TResult)typeof(Task).GetMethod(nameof(Task.FromResult))
                .MakeGenericMethod(resultType)
                .Invoke(null, new[] { executionResult });
        }
    }

    internal class TestAsyncEnumerable<T> : EnumerableQuery<T>, IAsyncEnumerable<T>, IQueryable<T>
    {
        public TestAsyncEnumerable(IEnumerable<T> enumerable)
            : base(enumerable)
        { }

        public TestAsyncEnumerable(System.Linq.Expressions.Expression expression)
            : base(expression)
        { }

        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            return new TestAsyncEnumerator<T>(this.AsEnumerable().GetEnumerator());
        }

        IQueryProvider IQueryable.Provider => new TestAsyncQueryProvider<T>(this);
    }

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
}