using DotNetEnv;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using OrderIngestion.Domain.Interfaces;
using OrderIngestion.Infrastructure.Api;
using OrderIngestion.Infrastructure.Persistence;
using OrderIngestion.Worker;

Env.NoClobber().TraversePath().Load();

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.AddOpenTelemetry(logging =>
{
    logging.IncludeFormattedMessage = true;
    logging.IncludeScopes = true;
});

var openTelemetry = builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource(OrderIngestionWorker.ActivitySourceName)
        .AddHttpClientInstrumentation()
        .AddEntityFrameworkCoreInstrumentation(options =>
        {
            options.EnrichWithIDbCommand = (activity, command) =>
            {
                var operation = command.CommandText
                    .Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault()?
                    .ToUpperInvariant();

                if (operation is not null)
                {
                    activity.DisplayName = operation;
                    activity.SetTag("db.operation.name", operation);
                }
            };
        }))
    .WithMetrics(metrics => metrics
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation());

if (!string.IsNullOrWhiteSpace(
        builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
{
    openTelemetry.UseOtlpExporter();
}

builder.Services
    .AddOptions<OrderApiOptions>()
    .Bind(builder.Configuration.GetSection(
        OrderApiOptions.SectionName))
    .Validate(
        options => Uri.TryCreate(
            options.BaseUrl, UriKind.Absolute, out _),
        "OrderApi:BaseUrl must be an absolute URL.")
    .ValidateOnStart();

builder.Services.AddHttpClient<IOrderApiClient, OrderApiClient>(
    (serviceProvider, client) =>
    {
        var options = serviceProvider
            .GetRequiredService<IOptions<OrderApiOptions>>()
            .Value;

        client.BaseAddress = new Uri(options.BaseUrl);
    })
    .AddStandardResilienceHandler();

builder.Services
    .AddOptions<OrderIngestionOptions>()
    .Bind(builder.Configuration.GetSection(
        OrderIngestionOptions.SectionName))
    .Validate(
        options => options.PollingInterval > TimeSpan.Zero,
        "OrderIngestion:PollingInterval must be greater than zero.")
    .ValidateOnStart();

builder.Services.AddHostedService<OrderIngestionWorker>();

var connectionString = new SqliteConnectionStringBuilder(
    builder.Configuration.GetConnectionString("Orders")
    ?? throw new InvalidOperationException(
        "Connection string 'Orders' is not configured. Is appsettings.json " +
        $"in the content root ({builder.Environment.ContentRootPath})?"));

if (!Path.IsPathRooted(connectionString.DataSource))
{
    connectionString.DataSource = Path.Combine(
        builder.Environment.ContentRootPath,
        connectionString.DataSource);
}

builder.Services.AddDbContext<OrderDbContext>(options =>
    options.UseSqlite(connectionString.ToString()));

builder.Services.AddScoped<IOrderRepository, OrderRepository>();

var host = builder.Build();

using (var scope = host.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider
        .GetRequiredService<OrderDbContext>();

    await dbContext.Database.MigrateAsync();
}

await host.RunAsync();
