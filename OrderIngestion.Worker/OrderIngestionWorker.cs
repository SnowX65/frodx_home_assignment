using System.Diagnostics;
using Microsoft.Extensions.Options;
using OrderIngestion.Domain.Interfaces;

namespace OrderIngestion.Worker;

public class OrderIngestionWorker : BackgroundService
{
    public const string ActivitySourceName = "OrderIngestion.Worker";

    private static readonly ActivitySource ActivitySource =
        new(ActivitySourceName);

    private readonly ILogger<OrderIngestionWorker> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<OrderIngestionOptions> _options;

    public OrderIngestionWorker(
        ILogger<OrderIngestionWorker> logger,
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<OrderIngestionOptions> options)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _options = options;
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        _logger.LogInformation("Order ingestion worker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            using (var activity = ActivitySource.StartActivity(
                       "order ingestion run"))
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();

                    var orderApiClient = scope.ServiceProvider
                        .GetRequiredService<IOrderApiClient>();
                    var orderRepository = scope.ServiceProvider
                        .GetRequiredService<IOrderRepository>();

                    _logger.LogInformation("Fetching orders from API.");

                    var orders = await orderApiClient.GetOrdersAsync(
                        stoppingToken);

                    var insertedCount = await orderRepository.AddNewOrdersAsync(
                        orders,
                        stoppingToken);

                    activity?.SetTag("orders.fetched", orders.Count);
                    activity?.SetTag("orders.inserted", insertedCount);
                    activity?.SetTag("orders.skipped", orders.Count - insertedCount);

                    _logger.LogInformation(
                        "Fetched {OrderCount} orders from API, inserted {InsertedCount} new, skipped {SkippedCount} existing.",
                        orders.Count,
                        insertedCount,
                        orders.Count - insertedCount);
                }
                catch (OperationCanceledException)
                    when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                    activity?.AddException(ex);

                    _logger.LogError(
                        ex,
                        "Order ingestion run failed.");
                }
            }

            await Task.Delay(
                _options.CurrentValue.PollingInterval,
                stoppingToken);
        }
    }
}
