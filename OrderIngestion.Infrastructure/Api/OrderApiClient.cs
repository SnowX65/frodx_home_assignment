using System.Net.Http.Json;
using OrderIngestion.Domain.Entities;
using OrderIngestion.Domain.Interfaces;

namespace OrderIngestion.Infrastructure.Api;

public class OrderApiClient : IOrderApiClient
{
    private readonly HttpClient _httpClient;

    public OrderApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<Order>> GetOrdersAsync(
        CancellationToken cancellationToken)
    {
        var posts = await _httpClient.GetFromJsonAsync<List<PostDto>>(
            "/posts",
            cancellationToken);

        var ingestedAt = DateTime.UtcNow;

        return posts?.Select(post => new Order
        {
            ExternalId = post.Id.ToString(),
            UserId = post.UserId,
            Title = post.Title,
            Body = post.Body,
            IngestedAt = ingestedAt
        }).ToList() ?? [];
    }
}