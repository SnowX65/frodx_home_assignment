using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OrderIngestion.Domain.Entities;
using OrderIngestion.Infrastructure.Persistence;

namespace OrderIngestion.Infrastructure.Tests.Persistence;

public class OrderRepositoryTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<OrderDbContext> _options;

    public OrderRepositoryTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<OrderDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var dbContext = new OrderDbContext(_options);
        dbContext.Database.Migrate();
    }

    public void Dispose()
    {
        _connection.Dispose();
    }

    [Fact]
    public async Task AddNewOrdersAsync_EmptyTable_InsertsAllOrders()
    {
        var orders = new[] { CreateOrder("1"), CreateOrder("2"), CreateOrder("3") };

        var insertedCount = await AddNewOrdersAsync(orders);

        Assert.Equal(3, insertedCount);
        Assert.Equal(["1", "2", "3"], await GetStoredExternalIdsAsync());
    }

    [Fact]
    public async Task AddNewOrdersAsync_SomeOrdersAlreadyStored_InsertsOnlyNewOrders()
    {
        await AddNewOrdersAsync([CreateOrder("1"), CreateOrder("2")]);

        var insertedCount = await AddNewOrdersAsync(
            [CreateOrder("1"), CreateOrder("2"), CreateOrder("3")]);

        Assert.Equal(1, insertedCount);
        Assert.Equal(["1", "2", "3"], await GetStoredExternalIdsAsync());
    }

    [Fact]
    public async Task AddNewOrdersAsync_AllOrdersAlreadyStored_InsertsNothing()
    {
        await AddNewOrdersAsync([CreateOrder("1"), CreateOrder("2")]);

        var insertedCount = await AddNewOrdersAsync(
            [CreateOrder("1"), CreateOrder("2")]);

        Assert.Equal(0, insertedCount);
        Assert.Equal(2, await CountStoredOrdersAsync());
    }

    [Fact]
    public async Task AddNewOrdersAsync_DuplicateExternalIdInBatch_InsertsItOnce()
    {
        var insertedCount = await AddNewOrdersAsync(
            [CreateOrder("1", title: "first"), CreateOrder("1", title: "second")]);

        Assert.Equal(1, insertedCount);

        await using var dbContext = new OrderDbContext(_options);
        var stored = await dbContext.Orders.SingleAsync();
        Assert.Equal("first", stored.Title);
    }

    [Fact]
    public async Task AddNewOrdersAsync_ExistingOrderWithChangedData_DoesNotUpdateIt()
    {
        await AddNewOrdersAsync([CreateOrder("1", title: "original")]);

        var insertedCount = await AddNewOrdersAsync(
            [CreateOrder("1", title: "changed")]);

        Assert.Equal(0, insertedCount);

        await using var dbContext = new OrderDbContext(_options);
        var stored = await dbContext.Orders.SingleAsync();
        Assert.Equal("original", stored.Title);
    }

    [Fact]
    public async Task AddNewOrdersAsync_MoreOrdersThanOneChunk_InsertsAllOrders()
    {
        var orders = Enumerable.Range(1, 2500)
            .Select(i => CreateOrder(i.ToString()))
            .ToList();

        var insertedCount = await AddNewOrdersAsync(orders);

        Assert.Equal(2500, insertedCount);
        Assert.Equal(2500, await CountStoredOrdersAsync());
    }

    [Fact]
    public async Task AddNewOrdersAsync_NewOrdersInLaterChunk_InsertsOnlyNewOrders()
    {
        await AddNewOrdersAsync(Enumerable.Range(1, 1500)
            .Select(i => CreateOrder(i.ToString()))
            .ToList());

        var insertedCount = await AddNewOrdersAsync(Enumerable.Range(1, 2200)
            .Select(i => CreateOrder(i.ToString()))
            .ToList());

        Assert.Equal(700, insertedCount);
        Assert.Equal(2200, await CountStoredOrdersAsync());
    }

    [Fact]
    public async Task AddNewOrdersAsync_EmptyBatch_InsertsNothing()
    {
        var insertedCount = await AddNewOrdersAsync([]);

        Assert.Equal(0, insertedCount);
        Assert.Equal(0, await CountStoredOrdersAsync());
    }

    [Fact]
    public async Task AddNewOrdersAsync_StoresAllFields()
    {
        var ingestedAt = new DateTime(2026, 10, 20, 12, 30, 0, DateTimeKind.Utc);

        await AddNewOrdersAsync([new Order
        {
            ExternalId = "42",
            UserId = 7,
            Title = "a title",
            Body = "a body",
            IngestedAt = ingestedAt
        }]);

        await using var dbContext = new OrderDbContext(_options);
        var stored = await dbContext.Orders.SingleAsync();
        Assert.Equal("42", stored.ExternalId);
        Assert.Equal(7, stored.UserId);
        Assert.Equal("a title", stored.Title);
        Assert.Equal("a body", stored.Body);
        Assert.Equal(ingestedAt, stored.IngestedAt);
        Assert.Equal(DateTimeKind.Utc, stored.IngestedAt.Kind);
    }

    [Fact]
    public async Task UniqueIndex_RejectsSecondOrderWithSameExternalId()
    {
        await using var dbContext = new OrderDbContext(_options);
        dbContext.Orders.Add(CreateOrder("1"));
        await dbContext.SaveChangesAsync();

        dbContext.Orders.Add(CreateOrder("1"));

        await Assert.ThrowsAsync<DbUpdateException>(
            () => dbContext.SaveChangesAsync());
    }

    private async Task<int> AddNewOrdersAsync(IReadOnlyCollection<Order> orders)
    {
        await using var dbContext = new OrderDbContext(_options);
        var repository = new OrderRepository(dbContext);

        return await repository.AddNewOrdersAsync(
            orders,
            CancellationToken.None);
    }

    private async Task<List<string>> GetStoredExternalIdsAsync()
    {
        await using var dbContext = new OrderDbContext(_options);

        return await dbContext.Orders
            .OrderBy(order => order.Id)
            .Select(order => order.ExternalId)
            .ToListAsync();
    }

    private async Task<int> CountStoredOrdersAsync()
    {
        await using var dbContext = new OrderDbContext(_options);

        return await dbContext.Orders.CountAsync();
    }

    private static Order CreateOrder(string externalId, string title = "title")
    {
        return new Order
        {
            ExternalId = externalId,
            UserId = 1,
            Title = title,
            Body = "body",
            IngestedAt = DateTime.UtcNow
        };
    }
}
