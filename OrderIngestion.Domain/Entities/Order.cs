namespace OrderIngestion.Domain.Entities;

public class Order
{
    public long Id { get; set; }

    public string ExternalId { get; set; } = string.Empty;

    public int UserId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    public DateTime IngestedAt { get; set; }
}
