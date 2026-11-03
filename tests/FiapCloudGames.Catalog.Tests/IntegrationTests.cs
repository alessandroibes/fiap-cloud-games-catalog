extern alias Consumer;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FiapCloudGames.Catalog.Application;
using Microsoft.Extensions.DependencyInjection;
using FiapCloudGames.Catalog.Domain;
using FiapCloudGames.Catalog.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;
using Payment = Consumer::FiapCloudGames.Contracts.V1.PaymentProcessedEvent;

namespace FiapCloudGames.Catalog.Tests;

[Trait("Category", "Integration")]
public sealed class IntegrationTests(CatalogFixture f) : IClassFixture<CatalogFixture>
{
    private async Task<Guid> Game(decimal price = 59.90m)
    {
        using var admin = f.As(Guid.NewGuid(), "Admin");
        var response = await admin.PostAsJsonAsync("/api/games", new { name = "Jogo G3 " + Guid.NewGuid(), price });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("id").GetGuid();
    }
    private async Task<Guid> Place(HttpClient client, Guid game)
    {
        var response = await client.PostAsJsonAsync("/api/orders", new { gameId = game });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Pending", json.GetProperty("status").GetString());
        var id = json.GetProperty("orderId").GetGuid(); Assert.EndsWith(id.ToString(), response.Headers.Location!.ToString());
        return id;
    }
    private async Task<Payment> Result(Guid orderId, string status = "Approved")
    {
        await using var db = f.Db(); var order = await db.Orders.SingleAsync(x => x.Id == orderId);
        return new Payment { EventId = Guid.NewGuid(), OccurredAt = DateTimeOffset.UtcNow, OrderId = order.Id, PaymentId = Guid.NewGuid(), UserId = order.UserId, GameId = order.GameId, Price = order.Price, Currency = order.Currency, Status = status };
    }
    [Fact]
    public async Task FailureAfterGrantCommitBeforeAckSurvivesRestartWithoutDuplicate()
    {
        using var client = f.As(Guid.NewGuid()); var id = await Place(client, await Game()); var payment = await Result(id);
        f.Probe.FailOrder = id;
        await f.Publish(payment);
        await f.Probe.Committed.Task.WaitAsync(TimeSpan.FromSeconds(45));
        await f.RestartApi();
        await f.Publish(payment);
        var replay = payment with { EventId = Guid.NewGuid() };
        await f.Publish(replay); await f.WaitReceipt(replay.EventId);
        await using var db = f.Db();
        Assert.Equal(OrderStatus.Approved, (await db.Orders.FindAsync(id))!.Status);
        Assert.Equal(1, await db.Library.CountAsync(x => x.OrderId == id));
        Assert.Equal(2, await db.PaymentReceipts.CountAsync(x => x.OrderId == id));
        Assert.DoesNotContain(f.Errors, x => x.EventId == payment.EventId);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EquivalentPriceScaleAcceptsCurrentAndLegacyReceipts(bool legacy)
    {
        using var client = f.As(Guid.NewGuid()); var id = await Place(client, await Game()); var payment = await Result(id);
        await f.Publish(payment); await f.WaitReceipt(payment.EventId);
        var p = new PaymentResult(payment.EventId, payment.OccurredAt, payment.OrderId, payment.PaymentId, payment.UserId, payment.GameId, payment.Status, 59.9m, payment.Currency);
        if (legacy)
        {
            await using var db = f.Db(); var receipt = await db.PaymentReceipts.FindAsync(payment.EventId);
            receipt!.Fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(p with { Price = 59.900m }))));
            await db.SaveChangesAsync();
        }
        using var scope = f.Factory.Services.CreateScope(); var service = scope.ServiceProvider.GetRequiredService<CatalogService>();
        foreach (var price in new[] { "59.9", "59.90", "59.9000" })
            await service.ProcessPayment(p with { Price = decimal.Parse(price, CultureInfo.InvariantCulture) }, default);
        await Assert.ThrowsAsync<InvalidPaymentException>(() => service.ProcessPayment(p with { Price = 59.91m }, default));
        await Assert.ThrowsAsync<InvalidPaymentException>(() => service.ProcessPayment(p with { Status = "Rejected" }, default));
        await using var check = f.Db();
        Assert.Equal(1, await check.Library.CountAsync(x => x.OrderId == id));
        Assert.Equal(1, await check.PaymentReceipts.CountAsync(x => x.OrderId == id));
    }
    [Fact]
    public async Task CrudRequiresAdminAndDeletionHidesGame()
    {
        using var user = f.As(Guid.NewGuid()); using var admin = f.As(Guid.NewGuid(), "Admin");
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.Client.PostAsJsonAsync("/api/games", new { name = "Jogo", price = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.PostAsJsonAsync("/api/games", new { name = "Jogo", price = 1 })).StatusCode);
        var game = await Game();
        Assert.Equal(HttpStatusCode.Forbidden, (await user.PutAsJsonAsync($"/api/games/{game}", new { name = "Novo", price = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.DeleteAsync($"/api/games/{game}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"/api/games/{game}", new { name = "Atualizado", price = 12.34m })).StatusCode);
        var dto = await f.Client.GetFromJsonAsync<JsonElement>($"/api/games/{game}"); Assert.Equal(12.34m, dto.GetProperty("price").GetDecimal());
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/games/{game}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await f.Client.GetAsync($"/api/games/{game}")).StatusCode);
        var list = await f.Client.GetFromJsonAsync<JsonElement>("/api/games"); Assert.DoesNotContain(list.EnumerateArray(), x => x.GetProperty("id").GetGuid() == game);
        Assert.Equal(HttpStatusCode.NotFound, (await user.PostAsJsonAsync("/api/orders", new { gameId = game })).StatusCode);
    }
    [Theory]
    [InlineData("{\"name\":\"\",\"price\":10}")]
    [InlineData("{\"name\":\"Jogo\",\"price\":-1}")]
    [InlineData("{\"name\":\"Jogo\",\"price\":1.001}")]
    [InlineData("{\"name\":\"Jogo\"}")]
    [InlineData("{\"price\":1}")]
    public async Task InvalidGameIsRejected(string json)
    {
        using var admin = f.As(Guid.NewGuid(), "Admin");
        using var body = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync("/api/games", body)).StatusCode);
    }
    [Theory]
    [InlineData("signature")]
    [InlineData("issuer")]
    [InlineData("audience")]
    [InlineData("expired")]
    [InlineData("subject")]
    public async Task InvalidJwtCannotAccessLibrary(string invalid)
    {
        using var client = f.As(Guid.NewGuid(), invalid: invalid);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/library")).StatusCode);
    }
    [Fact]
    public async Task OrdersUseJwtIdentityAndArePrivate()
    {
        var owner = Guid.NewGuid(); using var client = f.As(owner); using var other = f.As(Guid.NewGuid(), "Admin");
        var game = await Game();
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.Client.PostAsJsonAsync("/api/orders", new { gameId = game })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/orders", new { gameId = Guid.Empty })).StatusCode);
        var response = await client.PostAsJsonAsync("/api/orders", new { gameId = game, userId = Guid.NewGuid(), price = 0, status = "Approved" });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var accepted = await response.Content.ReadFromJsonAsync<JsonElement>(); var id = accepted.GetProperty("orderId").GetGuid();
        var order = await client.GetFromJsonAsync<JsonElement>($"/api/orders/{id}");
        Assert.Equal(owner, order.GetProperty("userId").GetGuid()); Assert.Equal(59.90m, order.GetProperty("price").GetDecimal());
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/orders/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.Client.GetAsync($"/api/orders/{id}")).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<JsonElement>("/api/library")).EnumerateArray());
        await f.WaitOrder(id);
        Assert.Equal(owner, Assert.Single(f.Orders, x => x.OrderId == id).UserId);
    }
    [Fact]
    public async Task ConcurrentPurchasesCreateOnlyOnePendingOrder()
    {
        var owner = Guid.NewGuid(); using var client = f.As(owner); var game = await Game();
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => client.PostAsJsonAsync("/api/orders", new { gameId = game })));
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.Accepted);
        Assert.Equal(7, results.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        await using var db = f.Db(); var order = Assert.Single(await db.Orders.Where(x => x.UserId == owner && x.GameId == game).ToListAsync());
        await f.WaitOrder(order.Id); Assert.Single(f.Orders, x => x.OrderId == order.Id);
    }
    [Fact]
    public async Task ApprovalIsIdempotentAcrossMessageIdsEventIdsAndRestart()
    {
        var owner = Guid.NewGuid(); using var client = f.As(owner); var game = await Game(); var id = await Place(client, game); var payment = await Result(id);
        await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => f.Publish(payment)));
        await f.WaitReceipt(payment.EventId);
        var equivalent = payment with { EventId = Guid.NewGuid() }; await f.Publish(equivalent); await f.WaitReceipt(equivalent.EventId);
        await f.RestartApi(); await f.Publish(payment);
        using var restarted = f.As(owner);
        Assert.Equal(HttpStatusCode.Conflict, (await restarted.PostAsJsonAsync("/api/orders", new { gameId = game })).StatusCode);
        var library = await restarted.GetFromJsonAsync<JsonElement>("/api/library"); Assert.Single(library.EnumerateArray());
        using var other = f.As(Guid.NewGuid()); Assert.Empty((await other.GetFromJsonAsync<JsonElement>("/api/library")).EnumerateArray());
        await using var db = f.Db(); Assert.Equal(1, await db.Library.CountAsync(x => x.OrderId == id)); Assert.Equal(2, await db.PaymentReceipts.CountAsync(x => x.OrderId == id));
    }
    [Fact]
    public async Task RejectionAllowsNewOrderAndOldConflictingApprovalCannotGrant()
    {
        using var client = f.As(Guid.NewGuid()); var game = await Game(); var first = await Place(client, game); var rejected = await Result(first, "Rejected");
        await f.Publish(rejected); await f.WaitReceipt(rejected.EventId);
        Assert.Empty((await client.GetFromJsonAsync<JsonElement>("/api/library")).EnumerateArray());
        var second = await Place(client, game); Assert.NotEqual(first, second);
        var conflict = rejected with { EventId = Guid.NewGuid(), Status = "Approved" }; await f.Publish(conflict); await f.WaitError(conflict.EventId);
        var approval = await Result(second); await f.Publish(approval); await f.WaitReceipt(approval.EventId);
        await using var db = f.Db(); Assert.Equal(OrderStatus.Rejected, (await db.Orders.FindAsync(first))!.Status); Assert.Equal(second, Assert.Single(await db.Library.Where(x => x.GameId == game).ToListAsync()).OrderId);
    }
    [Fact]
    public async Task PriceSnapshotSurvivesUpdateAndApprovalAfterDeletion()
    {
        using var client = f.As(Guid.NewGuid()); using var admin = f.As(Guid.NewGuid(), "Admin"); var game = await Game(10.25m); var id = await Place(client, game);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"/api/games/{game}", new { name = "Preço alterado", price = 20.50m })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/games/{game}")).StatusCode);
        var payment = await Result(id); Assert.Equal(10.25m, payment.Price); await f.Publish(payment); await f.WaitReceipt(payment.EventId);
        var item = Assert.Single((await client.GetFromJsonAsync<JsonElement>("/api/library")).EnumerateArray());
        Assert.Equal(20.50m, item.GetProperty("price").GetDecimal()); Assert.False(item.GetProperty("isActive").GetBoolean());
        var order = await client.GetFromJsonAsync<JsonElement>($"/api/orders/{id}"); Assert.Equal(10.25m, order.GetProperty("price").GetDecimal());
    }
    [Theory]
    [InlineData("user")]
    [InlineData("game")]
    [InlineData("price")]
    [InlineData("currency")]
    [InlineData("status")]
    [InlineData("utc")]
    [InlineData("event")]
    [InlineData("payment")]
    [InlineData("order")]
    public async Task InvalidPaymentGoesToErrorWithoutGrant(string field)
    {
        using var client = f.As(Guid.NewGuid()); var id = await Place(client, await Game()); var original = await Result(id);
        var invalid = field switch
        {
            "user" => original with { UserId = Guid.NewGuid() },
            "game" => original with { GameId = Guid.NewGuid() },
            "price" => original with { Price = original.Price + 1 },
            "currency" => original with { Currency = "USD" },
            "status" => original with { Status = "approved" },
            "utc" => original with { OccurredAt = original.OccurredAt.ToOffset(TimeSpan.FromHours(-3)) },
            "event" => original with { EventId = Guid.Empty },
            "payment" => original with { PaymentId = Guid.Empty },
            _ => original with { OrderId = Guid.NewGuid() }
        };
        await f.Publish(invalid); await f.WaitError(invalid.EventId);
        await using var db = f.Db(); Assert.Equal(OrderStatus.Pending, (await db.Orders.FindAsync(id))!.Status);
        Assert.False(await db.Library.AnyAsync(x => x.OrderId == id)); Assert.False(await db.PaymentReceipts.AnyAsync(x => x.OrderId == id));
    }
    [Fact]
    public async Task EventIdCannotBeReusedWithDifferentPayload()
    {
        using var client = f.As(Guid.NewGuid()); var id = await Place(client, await Game()); var payment = await Result(id);
        await f.Publish(payment); await f.WaitReceipt(payment.EventId);
        await f.Publish(payment with { Status = "Rejected" }); await f.WaitError(payment.EventId);
        await using var db = f.Db(); Assert.Equal(OrderStatus.Approved, (await db.Orders.FindAsync(id))!.Status);
    }
    [Fact]
    public async Task TransientFailureRollsBackAndRetriesSuccessfully()
    {
        using var client = f.As(Guid.NewGuid()); var id = await Place(client, await Game()); var payment = await Result(id);
        f.Failures.Remaining[id] = 1; await f.Publish(payment); await f.WaitReceipt(payment.EventId);
        Assert.Equal(2, f.Failures.Attempts[id]); await using var db = f.Db(); Assert.Equal(1, await db.Library.CountAsync(x => x.OrderId == id));
    }
    [Fact]
    public async Task ExhaustedRetriesLeavePendingAndAllowRecoveryByReplay()
    {
        using var client = f.As(Guid.NewGuid()); var id = await Place(client, await Game()); var payment = await Result(id);
        f.Failures.Remaining[id] = 4; await f.Publish(payment); await f.WaitError(payment.EventId);
        Assert.Equal(4, f.Failures.Attempts[id]);
        await using (var db = f.Db()) { Assert.Equal(OrderStatus.Pending, (await db.Orders.FindAsync(id))!.Status); Assert.False(await db.PaymentReceipts.AnyAsync(x => x.EventId == payment.EventId)); Assert.False(await db.Library.AnyAsync(x => x.OrderId == id)); }
        f.Failures.Remaining.TryRemove(id, out _); await f.Publish(payment); await f.WaitReceipt(payment.EventId);
    }
    [Fact]
    public async Task FailedOrderCommitDoesNotPersistOrderOrPublish()
    {
        using var client = f.As(Guid.NewGuid()); var game = await Game(); f.Failures.FailNewOrderForGame = game;
        try { Assert.Equal(HttpStatusCode.InternalServerError, (await client.PostAsJsonAsync("/api/orders", new { gameId = game })).StatusCode); }
        finally { f.Failures.FailNewOrderForGame = Guid.Empty; }
        await using var db = f.Db(); Assert.False(await db.Orders.AnyAsync(x => x.GameId == game));
        Assert.False(await db.Set<MassTransit.EntityFrameworkCoreIntegration.OutboxMessage>().AnyAsync(x => x.Body.Contains(game.ToString())));
        Assert.DoesNotContain(f.Orders, x => x.GameId == game);
    }
    [Fact]
    public async Task BrokerOutagePreservesOrderAcrossApiRestart()
    {
        var owner = Guid.NewGuid(); using var client = f.As(owner); var game = await Game(); Guid id;
        await f.Docker("stop", "rabbitmq");
        try
        {
            id = await Place(client, game);
            await using (var db = f.Db()) Assert.True(await db.Set<MassTransit.EntityFrameworkCoreIntegration.OutboxMessage>().AnyAsync(x => x.Body.Contains(id.ToString())));
            await f.RestartApi(false); Assert.Equal(HttpStatusCode.OK, (await f.Client.GetAsync("/health/ready")).StatusCode);
        }
        finally { await f.Docker("start", "rabbitmq"); }
        await f.WaitOrder(id);
        await CatalogFixture.Wait(async () => { await using var db = f.Db(); return !await db.Set<MassTransit.EntityFrameworkCoreIntegration.OutboxMessage>().AnyAsync(x => x.Body.Contains(id.ToString())); });
    }
    [Fact]
    public async Task PaymentsMayStartAfterOrderPublication()
    {
        using (var warmup = f.As(Guid.NewGuid())) await f.WaitOrder(await Place(warmup, await Game()));
        await f.RecreatePaymentsQueue();
        using var client = f.As(Guid.NewGuid()); var id = await Place(client, await Game()); await f.WaitOrder(id);
        await f.VerifyLatePayments(id);
    }
    [Fact]
    public async Task ConcurrentConflictingResultsCannotReverseWinner()
    {
        using var client = f.As(Guid.NewGuid()); var id = await Place(client, await Game()); var approved = await Result(id);
        var rejected = approved with { EventId = Guid.NewGuid(), Status = "Rejected" };
        await Task.WhenAll(f.Publish(approved), f.Publish(rejected));
        await CatalogFixture.Wait(() => Task.FromResult(f.Errors.Any(x => x.EventId == approved.EventId || x.EventId == rejected.EventId)));
        await using var db = f.Db(); var order = (await db.Orders.FindAsync(id))!;
        Assert.NotEqual(OrderStatus.Pending, order.Status);
        Assert.Equal(order.Status == OrderStatus.Approved ? 1 : 0, await db.Library.CountAsync(x => x.OrderId == id));
        Assert.Equal(1, await db.PaymentReceipts.CountAsync(x => x.OrderId == id));
    }
    [Fact]
    public async Task SamePaymentCannotCompleteDifferentOrders()
    {
        using var client = f.As(Guid.NewGuid()); var first = await Place(client, await Game()); var second = await Place(client, await Game());
        var payment = await Result(first); await f.Publish(payment); await f.WaitReceipt(payment.EventId);
        var reused = (await Result(second)) with { PaymentId = payment.PaymentId }; await f.Publish(reused); await f.WaitError(reused.EventId);
        await using var db = f.Db(); Assert.Equal(OrderStatus.Pending, (await db.Orders.FindAsync(second))!.Status);
        Assert.False(await db.Library.AnyAsync(x => x.OrderId == second)); Assert.False(await db.PaymentReceipts.AnyAsync(x => x.EventId == reused.EventId));
    }
    [Fact]
    public async Task DatabaseRejectsPendingDuplicateEvenWithoutApplicationLock()
    {
        var owner = Guid.NewGuid(); using var client = f.As(owner); var gameId = await Game(); await Place(client, gameId);
        await using var db = f.Db(); var game = await db.Games.SingleAsync(x => x.Id == gameId);
        db.Orders.Add(Order.Create(owner, game, DateTimeOffset.UtcNow));
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal("UX_Orders_Pending", Assert.IsType<PostgresException>(error.InnerException).ConstraintName);
    }
    [Fact]
    public async Task StartupDoesNotApplyMigrations()
    {
        var schema = "g3_" + Guid.NewGuid().ToString("N");
        await using var connection = new NpgsqlConnection(f.Config["ConnectionStrings:Catalog"]); await connection.OpenAsync();
        var quoted = new NpgsqlCommandBuilder().QuoteIdentifier(schema);
        await using (var create = new NpgsqlCommand("CREATE SCHEMA " + quoted, connection)) await create.ExecuteNonQueryAsync();
        try
        {
            var config = new Dictionary<string, string?>(f.Config) { ["ConnectionStrings:Catalog"] = new NpgsqlConnectionStringBuilder(f.Config["ConnectionStrings:Catalog"]) { SearchPath = schema }.ConnectionString };
            await using var isolated = new CatalogFactory(config, new FailurePlan()); using var client = isolated.CreateClient();
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/health/ready")).StatusCode);
            await using var query = new NpgsqlCommand("SELECT count(*) FROM information_schema.tables WHERE table_schema = @schema", connection); query.Parameters.AddWithValue("schema", schema);
            Assert.Equal(0L, await query.ExecuteScalarAsync());
        }
        finally { await using var drop = new NpgsqlCommand("DROP SCHEMA " + quoted, connection); await drop.ExecuteNonQueryAsync(); }
    }
}
