extern alias Consumer;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Data.Common;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using FiapCloudGames.Catalog.Domain;
using FiapCloudGames.Catalog.Infrastructure;
using MassTransit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Xunit;
using RemoteOrder = Consumer::FiapCloudGames.Contracts.V1.OrderPlacedEvent;
using RemotePayment = Consumer::FiapCloudGames.Contracts.V1.PaymentProcessedEvent;

[assembly: CollectionBehavior(DisableTestParallelization = true)]
namespace FiapCloudGames.Catalog.Tests;

public sealed class FailurePlan : SaveChangesInterceptor
{
    public ConcurrentDictionary<Guid, int> Remaining { get; } = new();
    public ConcurrentDictionary<Guid, int> Attempts { get; } = new();
    public Guid FailNewOrderForGame { get; set; }
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken ct = default)
    {
        var context = data.Context!;
        if (FailNewOrderForGame != Guid.Empty && context.ChangeTracker.Entries<Order>().Any(e => e.State == EntityState.Added && e.Entity.GameId == FailNewOrderForGame))
            throw new IOException("Falha controlada antes do commit do pedido.");
        foreach (var receipt in context.ChangeTracker.Entries<PaymentReceipt>().Where(e => e.State == EntityState.Added))
        {
            var id = receipt.Entity.OrderId;
            if (!Remaining.ContainsKey(id)) continue;
            Attempts.AddOrUpdate(id, 1, (_, count) => count + 1);
            if (Remaining.AddOrUpdate(id, 0, (_, count) => count - 1) >= 0)
                throw new IOException("Falha transitória controlada antes do commit do pagamento.");
        }
        return base.SavingChangesAsync(data, result, ct);
    }
}

public sealed class CommitProbe : DbTransactionInterceptor
{
    public Guid FailOrder { get; set; }
    public TaskCompletionSource<bool> Committed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData data, CancellationToken ct = default)
    {
        if (FailOrder != Guid.Empty && data.Context!.ChangeTracker.Entries<PaymentReceipt>().Any(e => e.Entity.OrderId == FailOrder))
        {
            FailOrder = Guid.Empty;
            Committed.TrySetResult(true);
            throw new IOException("Falha controlada após commit da concessão e antes do ACK.");
        }
        return Task.CompletedTask;
    }
}

public sealed class CatalogFactory(Dictionary<string, string?> config, FailurePlan failures, CommitProbe? probe = null) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        foreach (var pair in config) builder.UseSetting(pair.Key, pair.Value);
        builder.ConfigureServices(services => services.AddDbContext<CatalogDbContext>(o => o.AddInterceptors(failures, probe ?? new CommitProbe())));
    }
}

public sealed class CatalogFixture : IAsyncLifetime
{
    public Dictionary<string, string> Env { get; }
    public Dictionary<string, string?> Config { get; }
    public string Root { get; }
    public CatalogFactory Factory { get; private set; } = null!;
    public HttpClient Client { get; private set; } = null!;
    public FailurePlan Failures { get; } = new();
    public CommitProbe Probe { get; } = new();
    private IBusControl bus = null!;
    public ConcurrentQueue<RemoteOrder> Orders { get; } = new();
    public ConcurrentQueue<RemotePayment> Errors { get; } = new();

    public CatalogFixture()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FiapCloudGames.Catalog.sln"))) directory = directory.Parent;
        Root = directory?.FullName ?? throw new InvalidOperationException("Raiz Catalog não encontrada.");
        Env = File.ReadAllLines(Path.Combine(Root, ".env")).Where(l => l.Length > 0 && !l.StartsWith('#')).Select(l => l.Split('=', 2)).ToDictionary(p => p[0], p => p[1]);
        var connection = new NpgsqlConnectionStringBuilder { Host = "127.0.0.1", Port = int.Parse(Env["TEST_DB_PORT"]), Database = "fcg_catalog_test", Username = "fcg_catalog_test", Password = Env["TEST_DB_PASSWORD"] }.ConnectionString;
        Config = new()
        {
            ["ConnectionStrings:Catalog"] = connection,
            ["Jwt:Key"] = Env["Jwt__Key"],
            ["Jwt:Issuer"] = Env["Jwt__Issuer"],
            ["Jwt:Audience"] = Env["Jwt__Audience"],
            ["RabbitMq:Host"] = "127.0.0.1",
            ["RabbitMq:Port"] = Env["TEST_RABBIT_PORT"],
            ["RabbitMq:VirtualHost"] = "fcg_test",
            ["RabbitMq:Username"] = "catalog_test",
            ["RabbitMq:Password"] = Env["TEST_RABBIT_PASSWORD"]
        };
    }
    public CatalogDbContext Db() => new(new DbContextOptionsBuilder<CatalogDbContext>().UseNpgsql(Config["ConnectionStrings:Catalog"]).Options);
    public string Token(Guid user, string role = "User", string? invalid = null)
    {
        var credentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(invalid == "signature" ? new string('x', 64) : Env["Jwt__Key"])), SecurityAlgorithms.HmacSha256);
        var claims = new[] { new Claim("sub", invalid == "subject" ? "invalid" : user.ToString()), new Claim("role", role) };
        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(invalid == "issuer" ? "invalid" : Env["Jwt__Issuer"], invalid == "audience" ? "invalid" : Env["Jwt__Audience"], claims,
            DateTime.UtcNow.AddHours(-2), invalid == "expired" ? DateTime.UtcNow.AddMinutes(-1) : DateTime.UtcNow.AddHours(1), credentials));
    }
    public HttpClient As(Guid user, string role = "User", string? invalid = null)
    {
        var client = Factory.CreateClient(); client.DefaultRequestHeaders.Authorization = new("Bearer", Token(user, role, invalid)); return client;
    }
    private void Host(IRabbitMqBusFactoryConfigurator cfg) => cfg.Host("127.0.0.1", ushort.Parse(Env["TEST_RABBIT_PORT"]), "fcg_test", h => { h.Username("catalog_test"); h.Password(Env["TEST_RABBIT_PASSWORD"]); });
    public async Task InitializeAsync()
    {
        await using (var db = Db()) await db.Database.MigrateAsync();
        bus = Bus.Factory.CreateUsingRabbitMq(cfg =>
        {
            Host(cfg);
            cfg.ReceiveEndpoint("catalog-g3-order-observer", e => e.Handler<RemoteOrder>(ctx => { Orders.Enqueue(ctx.Message); return Task.CompletedTask; }));
            cfg.ReceiveEndpoint("catalog-payment-processed-v1_error", e =>
            {
                e.ConfigureConsumeTopology = false;
                e.Handler<RemotePayment>(ctx => { Errors.Enqueue(ctx.Message); return Task.CompletedTask; });
            });
        });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        await bus.StartAsync(timeout.Token);
        StartApi();
        await Factory.Services.GetRequiredService<IBusControl>().StartAsync(timeout.Token);
    }
    private void StartApi() { Factory = new CatalogFactory(Config, Failures, Probe); Client = Factory.CreateClient(); }
    public async Task RestartApi(bool waitForBus = true)
    {
        Client.Dispose(); await Factory.DisposeAsync(); StartApi();
        if (waitForBus) { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45)); await Factory.Services.GetRequiredService<IBusControl>().StartAsync(timeout.Token); }
    }
    public async Task RecreatePaymentsQueue()
    {
        Client.Dispose(); await Factory.DisposeAsync();
        await Docker("exec", "-T", "rabbitmq", "gosu", "rabbitmq", "rabbitmqctl", "delete_queue", "-p", "fcg_test", "payments-order-placed-v1");
        StartApi();
    }
    public Task Publish(RemotePayment payment) => bus.Publish(payment, ctx => ctx.MessageId = Guid.NewGuid());
    public static async Task Wait(Func<Task<bool>> condition, int seconds = 60)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
        while (!await condition()) await Task.Delay(100, timeout.Token);
    }
    public Task WaitOrder(Guid id) => Wait(() => Task.FromResult(Orders.Any(e => e.OrderId == id)));
    public Task WaitReceipt(Guid eventId) => Wait(async () => { await using var db = Db(); return await db.PaymentReceipts.AnyAsync(x => x.EventId == eventId); });
    public Task WaitError(Guid eventId) => Wait(() => Task.FromResult(Errors.Any(e => e.EventId == eventId)));
    public async Task VerifyLatePayments(Guid orderId)
    {
        var received = new TaskCompletionSource<Guid>(TaskCreationOptions.RunContinuationsAsynchronously);
        var late = Bus.Factory.CreateUsingRabbitMq(cfg =>
        {
            Host(cfg);
            cfg.ReceiveEndpoint("payments-order-placed-v1", e => e.Handler<RemoteOrder>(ctx => { if (ctx.Message.OrderId == orderId) received.TrySetResult(orderId); return Task.CompletedTask; }));
        });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await late.StartAsync(timeout.Token); Assert.Equal(orderId, await received.Task.WaitAsync(timeout.Token)); }
        finally { await late.StopAsync(); }
    }
    public async Task Docker(params string[] command)
    {
        var start = new ProcessStartInfo("docker") { WorkingDirectory = Root, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var arg in new[] { "compose", "--env-file", ".env", "-f", "tests/compose.yaml" }.Concat(command)) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(150));
        await process.WaitForExitAsync(timeout.Token); await Task.WhenAll(stdout, stderr);
        Assert.True(process.ExitCode == 0, "Docker do ambiente de teste falhou; saída omitida para preservar credenciais.");
    }
    public async Task DisposeAsync()
    {
        Client?.Dispose(); if (Factory is not null) await Factory.DisposeAsync();
        if (bus is not null) { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15)); await bus.StopAsync(timeout.Token); }
    }
}
