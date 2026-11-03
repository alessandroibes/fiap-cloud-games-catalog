using System.Security.Cryptography;
using System.Globalization;
using System.Text;
using System.Text.Json;
using FiapCloudGames.Catalog.Application;
using FiapCloudGames.Catalog.Domain;
using FiapCloudGames.Contracts.V1;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FiapCloudGames.Catalog.Infrastructure;

public sealed class CatalogStore(CatalogDbContext db, IPublishEndpoint publisher, TimeProvider clock) : ICatalogStore
{
    private static string Fingerprint(PaymentResult p) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(p))));
    private static bool MatchesLegacyFingerprint(PaymentResult p, string fingerprint)
    {
        // Recibos anteriores preservavam a escala decimal do JSON. Aceita somente
        // representações numericamente iguais, mantendo todos os demais campos.
        for (var scale = 0; scale <= 28; scale++)
        {
            if (decimal.TryParse(p.Price.ToString("F" + scale, CultureInfo.InvariantCulture), NumberStyles.Number, CultureInfo.InvariantCulture, out var price)
                && price == p.Price && Fingerprint(p with { Price = price }) == fingerprint) return true;
        }
        return false;
    }
    // Todos os fluxos que alteram um jogo ou sua posse usam o mesmo lock PostgreSQL.
    private async Task<Game> LockGame(Guid id, CancellationToken ct) =>
        await db.Games.FromSqlInterpolated($"SELECT * FROM \"Games\" WHERE \"Id\" = {id} FOR UPDATE").SingleOrDefaultAsync(ct) ?? throw new NotFoundException();
    public async Task<IReadOnlyList<Game>> ListGames(CancellationToken ct) => await db.Games.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).ThenBy(x => x.Id).ToListAsync(ct);
    public async Task<Game> GetGame(Guid id, CancellationToken ct) => await db.Games.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.IsActive, ct) ?? throw new NotFoundException();
    public async Task<Game> CreateGame(Game game, CancellationToken ct)
    {
        db.Games.Add(game); await db.SaveChangesAsync(ct); return game;
    }
    public async Task UpdateGame(Guid id, string name, decimal price, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var game = await LockGame(id, ct);
        if (!game.IsActive) throw new NotFoundException();
        game.Update(name, price); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
    public async Task DeleteGame(Guid id, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        (await LockGame(id, ct)).Deactivate(); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
    public async Task<Order> PlaceOrder(Guid userId, Guid gameId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var game = await LockGame(gameId, ct);
        if (!game.IsActive) throw new NotFoundException();
        if (await db.Library.AnyAsync(x => x.UserId == userId && x.GameId == gameId, ct) ||
            await db.Orders.AnyAsync(x => x.UserId == userId && x.GameId == gameId && x.Status == OrderStatus.Pending, ct))
            throw new PurchaseConflictException();
        var order = Order.Create(userId, game, clock.GetUtcNow());
        db.Orders.Add(order);
        await publisher.Publish(new OrderPlacedEvent { EventId = Guid.NewGuid(), OccurredAt = order.CreatedAt, OrderId = order.Id, UserId = userId, GameId = gameId, Price = order.Price, Currency = order.Currency }, ct);
        try { await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505", ConstraintName: "UX_Orders_Pending" }) { throw new PurchaseConflictException(); }
        return order;
    }
    public async Task<Order> GetOrder(Guid userId, Guid orderId, CancellationToken ct) =>
        await db.Orders.AsNoTracking().SingleOrDefaultAsync(x => x.Id == orderId && x.UserId == userId, ct) ?? throw new NotFoundException();
    public async Task<IReadOnlyList<LibraryView>> GetLibrary(Guid userId, CancellationToken ct) =>
        await (from entry in db.Library.AsNoTracking()
               join game in db.Games on entry.GameId equals game.Id
               where entry.UserId == userId
               orderby entry.AcquiredAt, entry.GameId
               select new LibraryView(game.Id, game.Name, game.Price, game.IsActive, entry.OrderId, entry.AcquiredAt)).ToListAsync(ct);
    public async Task ProcessPayment(PaymentResult p, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Deduplicação lógica pelo EventId, independente do MessageId do transporte.
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({p.EventId.ToString()}, 0))", ct);
        var fingerprint = Fingerprint(p with { Price = decimal.Parse(p.Price.ToString("F2", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture) });
        var receipt = await db.PaymentReceipts.FindAsync([p.EventId], ct);
        if (receipt is not null)
        {
            if (receipt.Fingerprint != fingerprint && !MatchesLegacyFingerprint(p, receipt.Fingerprint)) throw new InvalidPaymentException("EventId reutilizado com conteúdo divergente.");
            return;
        }
        var orderGame = await db.Orders.Where(x => x.Id == p.OrderId).Select(x => (Guid?)x.GameId).SingleOrDefaultAsync(ct);
        if (orderGame is null) throw new InvalidPaymentException("Pedido inexistente.");
        await LockGame(orderGame.Value, ct);
        var order = await db.Orders.SingleAsync(x => x.Id == p.OrderId, ct);
        if (order.UserId != p.UserId || order.GameId != p.GameId || order.Price != p.Price || order.Currency != p.Currency)
            throw new InvalidPaymentException("Pagamento diverge do pedido.");
        var status = p.Status == "Approved" ? OrderStatus.Approved : OrderStatus.Rejected;
        bool changed;
        try { changed = order.Complete(p.PaymentId, status); }
        catch (InvalidOperationException) { throw new InvalidPaymentException("Resultado conflitante com pedido terminal."); }
        if (changed && status == OrderStatus.Approved) db.Library.Add(LibraryEntry.Grant(order, clock.GetUtcNow()));
        db.PaymentReceipts.Add(new PaymentReceipt { EventId = p.EventId, OrderId = p.OrderId, Fingerprint = fingerprint, ProcessedAt = clock.GetUtcNow() });
        try { await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" })
        { throw new InvalidPaymentException("Pagamento ou concessão já associados a outro pedido."); }
    }
}
