using Microsoft.EntityFrameworkCore;
using OrderIngestion.Domain.Entities;
using OrderIngestion.Domain.Interfaces;

namespace OrderIngestion.Infrastructure.Persistence;

public class OrderRepository : IOrderRepository
{
    private const int ChunkSize = 1000;

    private readonly OrderDbContext _dbContext;

    public OrderRepository(OrderDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<int> AddNewOrdersAsync(
        IReadOnlyCollection<Order> orders,
        CancellationToken cancellationToken)
    {
        var uniqueOrders = orders
            .DistinctBy(order => order.ExternalId)
            .ToList();

        var insertedCount = 0;

        foreach (var chunk in uniqueOrders.Chunk(ChunkSize))
        {
            insertedCount += await AddChunkAsync(chunk, cancellationToken);
        }

        return insertedCount;
    }

    private async Task<int> AddChunkAsync(
        Order[] chunk,
        CancellationToken cancellationToken)
    {
        var externalIds = chunk
            .Select(order => order.ExternalId)
            .ToList();

        var existingIds = await _dbContext.Orders
            .AsNoTracking()
            .Where(order => externalIds.Contains(order.ExternalId))
            .Select(order => order.ExternalId)
            .ToHashSetAsync(cancellationToken);

        var newOrders = chunk
            .Where(order => !existingIds.Contains(order.ExternalId))
            .ToList();

        if (newOrders.Count == 0)
        {
            return 0;
        }

        _dbContext.Orders.AddRange(newOrders);

        await _dbContext.SaveChangesAsync(cancellationToken);

        _dbContext.ChangeTracker.Clear();

        return newOrders.Count;
    }
}
