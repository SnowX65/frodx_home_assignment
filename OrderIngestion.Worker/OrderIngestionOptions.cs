namespace OrderIngestion.Worker;

public class OrderIngestionOptions
{
    public const string SectionName = "OrderIngestion";

    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromMinutes(5);
}
