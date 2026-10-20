using OrderIngestion.Domain.Entities;

namespace OrderIngestion.Domain.Interfaces;

public interface IOrderRepository
{
    Task<int> AddNewOrdersAsync(
        IReadOnlyCollection<Order> orders,
        CancellationToken cancellationToken);
}
