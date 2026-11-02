extern alias Consumer;
using System.Text.Json;
using FiapCloudGames.Contracts.V1;
using MassTransit;
using Xunit;
using RemoteOrder = Consumer::FiapCloudGames.Contracts.V1.OrderPlacedEvent;
using RemotePayment = Consumer::FiapCloudGames.Contracts.V1.PaymentProcessedEvent;

namespace FiapCloudGames.Catalog.Tests;

public sealed class ContractTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    [Fact]
    public void OrderMatchesCanonicalSchemaAndIndependentConsumer()
    {
        var evt = new OrderPlacedEvent { EventId = Guid.NewGuid(), OccurredAt = DateTimeOffset.UtcNow, OrderId = Guid.NewGuid(), UserId = Guid.NewGuid(), GameId = Guid.NewGuid(), Price = 59.90m, Currency = "BRL" };
        var text = JsonSerializer.Serialize(evt, Json);
        var remote = JsonSerializer.Deserialize<RemoteOrder>(text, Json)!;
        Assert.Equal(evt.OrderId, remote.OrderId); Assert.Equal(evt.Price, remote.Price);
        Assert.Equal("urn:message:FiapCloudGames.Contracts.V1:OrderPlacedEvent", MessageUrn.ForTypeString<OrderPlacedEvent>());
        Assert.Equal(MessageUrn.ForTypeString<OrderPlacedEvent>(), MessageUrn.ForTypeString<RemoteOrder>());
        AssertSchema(text, "order-placed-v1.md", ["eventId", "occurredAt", "orderId", "userId", "gameId", "price", "currency"]);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<OrderPlacedEvent>("{}", Json));
    }
    [Theory]
    [InlineData("Approved")]
    [InlineData("Rejected")]
    public void PaymentMatchesCanonicalSchemaAndIndependentProducer(string status)
    {
        var remote = new RemotePayment { EventId = Guid.NewGuid(), OccurredAt = DateTimeOffset.UtcNow, OrderId = Guid.NewGuid(), PaymentId = Guid.NewGuid(), UserId = Guid.NewGuid(), GameId = Guid.NewGuid(), Status = status, Price = 42.12m, Currency = "BRL" };
        var text = JsonSerializer.Serialize(remote, Json);
        var local = JsonSerializer.Deserialize<PaymentProcessedEvent>(text, Json)!;
        Assert.Equal(remote.PaymentId, local.PaymentId); Assert.Equal(status, local.Status); Assert.Equal(remote.Price, local.Price);
        Assert.Equal(MessageUrn.ForTypeString<RemotePayment>(), MessageUrn.ForTypeString<PaymentProcessedEvent>());
        AssertSchema(text, "payment-processed-v1.md", ["eventId", "occurredAt", "orderId", "paymentId", "userId", "gameId", "status", "price", "currency"]);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<PaymentProcessedEvent>("{}", Json));
    }
    private static void AssertSchema(string text, string doc, string[] fields)
    {
        using var json = JsonDocument.Parse(text);
        Assert.Equal(fields.Order(), json.RootElement.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal(JsonValueKind.Number, json.RootElement.GetProperty("price").ValueKind);
        Assert.Equal(TimeSpan.Zero, json.RootElement.GetProperty("occurredAt").GetDateTimeOffset().Offset);
        var canonical = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, doc));
        foreach (var field in fields) Assert.Contains($"`{char.ToUpperInvariant(field[0])}{field[1..]}`", canonical);
    }
}
