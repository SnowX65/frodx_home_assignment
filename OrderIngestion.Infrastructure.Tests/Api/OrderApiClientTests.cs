using System.Net;
using OrderIngestion.Infrastructure.Api;

namespace OrderIngestion.Infrastructure.Tests.Api;

public class OrderApiClientTests
{
    private const string TwoPosts = """
        [
          { "userId": 1, "id": 1, "title": "first title", "body": "first body" },
          { "userId": 2, "id": 11, "title": "second title", "body": "second body" }
        ]
        """;

    [Fact]
    public async Task GetOrdersAsync_SendsGetRequestToPosts()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, "[]");

        await CreateClient(handler).GetOrdersAsync(CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(
            new Uri("https://api.example.test/posts"),
            request.RequestUri);
    }

    [Fact]
    public async Task GetOrdersAsync_MapsEachPostToAnOrder()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, TwoPosts);

        var orders = await CreateClient(handler).GetOrdersAsync(CancellationToken.None);

        Assert.Collection(
            orders,
            first =>
            {
                Assert.Equal("1", first.ExternalId);
                Assert.Equal(1, first.UserId);
                Assert.Equal("first title", first.Title);
                Assert.Equal("first body", first.Body);
            },
            second =>
            {
                Assert.Equal("11", second.ExternalId);
                Assert.Equal(2, second.UserId);
                Assert.Equal("second title", second.Title);
                Assert.Equal("second body", second.Body);
            });
    }

    [Fact]
    public async Task GetOrdersAsync_SetsTheSameUtcIngestedAtOnAllOrders()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, TwoPosts);
        var before = DateTime.UtcNow;

        var orders = await CreateClient(handler).GetOrdersAsync(CancellationToken.None);

        var after = DateTime.UtcNow;
        var ingestedAt = Assert.Single(orders.Select(order => order.IngestedAt).Distinct());
        Assert.Equal(DateTimeKind.Utc, ingestedAt.Kind);
        Assert.InRange(ingestedAt, before, after);
    }

    [Fact]
    public async Task GetOrdersAsync_EmptyArray_ReturnsNoOrders()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, "[]");

        var orders = await CreateClient(handler).GetOrdersAsync(CancellationToken.None);

        Assert.Empty(orders);
    }

    [Fact]
    public async Task GetOrdersAsync_NullBody_ReturnsNoOrders()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, "null");

        var orders = await CreateClient(handler).GetOrdersAsync(CancellationToken.None);

        Assert.Empty(orders);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task GetOrdersAsync_ErrorStatusCode_ThrowsHttpRequestException(
        HttpStatusCode statusCode)
    {
        var handler = new StubHttpMessageHandler(statusCode, "");

        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => CreateClient(handler).GetOrdersAsync(CancellationToken.None));

        Assert.Equal(statusCode, exception.StatusCode);
    }

    [Fact]
    public async Task GetOrdersAsync_CancelledToken_ThrowsOperationCanceledException()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, TwoPosts);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateClient(handler).GetOrdersAsync(cancellation.Token));

        Assert.Empty(handler.Requests);
    }

    private static OrderApiClient CreateClient(StubHttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.example.test")
        };

        return new OrderApiClient(httpClient);
    }
}
