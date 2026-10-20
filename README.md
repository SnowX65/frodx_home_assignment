# Order Ingestion Worker

This project is a .NET 10 background worker. At a fixed interval, it gets orders from an external API and writes the new orders to a SQLite database. It skips orders that are already in the database. Thus, you can run it again at any time without duplicate rows.

## Run the worker

You need the .NET 10 SDK.

1. Go to the root folder of the repository.
2. Install the local tools:

   ```bash
   dotnet tool restore
   ```

3. Start the worker:

   ```bash
   dotnet run --project OrderIngestion.Worker
   ```

4. To stop the worker, push `Ctrl+C`.

At startup, the worker applies the database migrations. On the first run, this step creates the `orders.db` file in the `OrderIngestion.Worker` folder. Then the worker starts the first ingestion run immediately. After each run, it waits for the polling interval and starts the next run.

Each run writes one summary line to the log:

```
Fetched 100 orders from API, inserted 100 new, skipped 0 existing.
```

## Configuration

The settings are in `OrderIngestion.Worker/appsettings.json`.

| Setting | Default | Description |
|---|---|---|
| `ConnectionStrings:Orders` | `Data Source=orders.db` | The SQLite database file. The worker resolves a relative path against the content root, not the current working directory. |
| `OrderApi:BaseUrl` | `https://jsonplaceholder.typicode.com` | The base URL of the order API. The value must be an absolute URL. |
| `OrderIngestion:PollingInterval` | `00:05:00` | The time between two runs, in the format `hh:mm:ss`. The value must be more than zero. |

In the Development environment, `appsettings.Development.json` sets the polling interval to 10 seconds.

You can override a setting without a rebuild:

- With an environment variable: `OrderIngestion__PollingInterval=00:01:00`
- With a command-line argument: `--OrderIngestion:PollingInterval=00:01:00`

The worker validates the settings at startup. If a value is not valid, the worker stops with an error message. The worker reads the polling interval again before each wait. Thus, a change to the interval in `appsettings.json` has an effect while the worker runs.

## Monitoring

The worker uses OpenTelemetry to send logs, traces, and metrics to Grafana Cloud. The metrics include HTTP client metrics and .NET runtime metrics, for example memory use and garbage collection.

Each ingestion run creates one trace. The root span of the trace has the name `order ingestion run`. Each API call of the run is a child span with its duration and status code. If the resilience handler retries the call, each attempt is a separate child span. Each database command of the run is also a child span. The `db.statement` attribute of this span contains the SQL text with parameter names, but not the parameter values. The root span has these tags:

- `orders.fetched`: the number of orders from the API
- `orders.inserted`: the number of new orders in the database
- `orders.skipped`: the number of orders that were already in the database

If a run fails, the root span gets the error status and an exception event. The log entries of a run have the trace ID of that run. Thus, Grafana can show the logs and the trace of a run together.

To send the data to Grafana Cloud:

1. Copy `OrderIngestion.Worker/.env.example` to `OrderIngestion.Worker/.env`.
2. In `.env`, replace `<base64 of instance-id:token>` with the value from the OpenTelemetry setup page of your Grafana Cloud stack. This value is the Base64 encoding of your instance ID, a colon, and your token. Do not use the token alone. If you use the token alone, Grafana Cloud rejects all data with the error `authentication error: no credentials provided`.
3. If your Grafana Cloud stack is not in the `eu-central-0` region, change `OTEL_EXPORTER_OTLP_ENDPOINT`.
4. Start the worker.

The `.env` file is in `.gitignore`. Do not commit the token.

At startup, the worker loads `.env` into the environment variables. A variable that is already set in the environment keeps its value. If `OTEL_EXPORTER_OTLP_ENDPOINT` has no value, the worker does not export telemetry, and it writes logs only to the console.

In Grafana Cloud, open **Explore** and filter on the service name `order-ingestion-worker`:

- **Loki** shows the logs. `OrderCount`, `InsertedCount`, and `SkippedCount` are separate fields.
- **Tempo** shows the traces.
- **Prometheus** shows the metrics.

## Components

The solution has three projects. The dependencies go in one direction only: `Worker` uses `Infrastructure` and `Domain`, and `Infrastructure` uses `Domain`.

### OrderIngestion.Domain

This project contains the business types. It has no dependency on other projects or external packages.

- **`Order`**: The entity that the worker stores. It has an internal `Id`, the `ExternalId`, `UserId`, `Title`, and `Body` from the API, and `IngestedAt`. `IngestedAt` is the UTC time when the worker got the order. The section [Source data](#source-data) shows how the API fields map to these properties.
- **`IOrderApiClient`**: The contract to get orders from an external source.
- **`IOrderRepository`**: The contract to store orders. `AddNewOrdersAsync` writes only the orders that are not in the database and returns the number of inserted orders.

### OrderIngestion.Infrastructure

This project contains the implementations that connect to external systems: the HTTP API and the database.

- **`OrderApiClient`**: Implements `IOrderApiClient`. It sends a `GET /posts` request to the API and converts each post to an `Order`.
- **`PostDto`**: The shape of one post in the API response.
- **`OrderApiOptions`**: Holds the `OrderApi:BaseUrl` setting.
- **`OrderDbContext`**: The Entity Framework (EF) Core context for the `Orders` table. It puts a unique index on `ExternalId`. It also marks `IngestedAt` as UTC when it reads the value, because SQLite does not store the kind of a date.
- **`OrderRepository`**: Implements `IOrderRepository`. The section [Duplicate orders](#duplicate-orders) describes how it works.
- **`Persistence/Migrations`**: The EF Core migrations that create and change the database schema.

### OrderIngestion.Worker

This project is the host. It reads the configuration, registers the services, and starts the background service.

- **`Program.cs`**: Builds the host. It binds and validates the settings, registers the HTTP client and the database context, and applies the migrations before the worker starts. It also adds the standard resilience handler to the HTTP client. The section [Errors](#errors) describes this handler. It also loads `.env` and sets up OpenTelemetry. The section [Monitoring](#monitoring) describes this setup.
- **`.env.example`**: A template for the `.env` file with the OpenTelemetry settings for Grafana Cloud.
- **`OrderIngestionWorker`**: The background service. It runs in a loop until the application stops. Each loop starts a trace, gets the orders from the API, gives them to the repository, logs the result, and waits for the polling interval.
- **`OrderIngestionOptions`**: Holds the `OrderIngestion:PollingInterval` setting.

## Design decisions

### One loop, a new scope for each run

The host starts one instance of `OrderIngestionWorker` and keeps it for the full life of the application. Each run creates a new dependency injection (DI) scope. The scope supplies a new HTTP client and a new database context, and it disposes of them at the end of the run. If the worker got these services through its constructor, it would keep them for the full life of the application.

### Errors

The HTTP client uses the standard resilience handler from `Microsoft.Extensions.Http.Resilience`. If the API call fails with a temporary error, the handler tries the call again up to three times, with a longer wait before each attempt. Temporary errors are network errors, timeouts, HTTP 408, HTTP 429, and HTTP 5xx. The handler logs each retry as a warning.

If a run still fails, the worker logs the error and tries again after the polling interval. One failed run does not stop the worker. When you stop the application, the worker exits the loop without an error entry in the log.

### Duplicate orders

The worker uses two layers to prevent duplicate orders:

1. The database has a unique index on `ExternalId`. This index is the final guarantee.
2. The repository removes duplicates from the incoming batch. Then it divides the batch into chunks of 1,000 orders. For each chunk, it does these steps:
   1. It gets the `ExternalId` values that are already in the database. The query uses `WHERE ExternalId IN (...)` and the unique index. Thus, the cost depends on the chunk size, not on the size of the table.
   2. It adds only the new orders and saves them with one call to `SaveChanges`.
   3. It clears the change tracker, so that memory use stays the same from chunk to chunk.

Another process can insert the same orders between step 1 and step 2. In that case, the unique index rejects the save and the run fails. The next run inserts the remaining orders, because each run is safe to repeat.

### Database schema

EF Core migrations control the database schema. To add a migration, run this command from the root folder:

```bash
dotnet ef migrations add <Name> \
  --project OrderIngestion.Infrastructure \
  --startup-project OrderIngestion.Worker \
  --output-dir Persistence/Migrations
```

The worker applies new migrations at the next startup.

## Source data

The API at JSONPlaceholder has no orders endpoint, and the task does not define a structure for an order. Thus, the worker uses the `/posts` endpoint as the source of orders. It stores the data that the API returns and adds no invented values:

| API field | `Order` property |
|---|---|
| `id` | `ExternalId` |
| `userId` | `UserId` |
| `title` | `Title` |
| `body` | `Body` |

The worker also sets `IngestedAt` to the time of the run, in UTC.