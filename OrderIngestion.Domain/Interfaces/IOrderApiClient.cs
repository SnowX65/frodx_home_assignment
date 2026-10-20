using OrderIngestion.Domain.Entities;

namespace OrderIngestion.Domain.Interfaces;

public interface IOrderApiClient
{
    Task<IReadOnlyList<Order>> GetOrdersAsync(
        CancellationToken cancellationToken);
}