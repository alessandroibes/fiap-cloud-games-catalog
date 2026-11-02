using FiapCloudGames.Catalog.Domain;

namespace FiapCloudGames.Catalog.Application;

public sealed class NotFoundException() : Exception("Recurso não encontrado.");
public sealed class PurchaseConflictException() : Exception("Jogo já adquirido ou com pedido pendente.");
public sealed class InvalidPaymentException(string message) : Exception(message);
public record LibraryView(Guid GameId, string Name, decimal Price, bool IsActive, Guid OrderId, DateTimeOffset AcquiredAt);
public record PaymentResult(Guid EventId, DateTimeOffset OccurredAt, Guid OrderId, Guid PaymentId, Guid UserId, Guid GameId, string Status, decimal Price, string Currency);

public interface ICatalogStore
{
    Task<IReadOnlyList<Game>> ListGames(CancellationToken ct);
    Task<Game> GetGame(Guid id, CancellationToken ct);
    Task<Game> CreateGame(Game game, CancellationToken ct);
    Task UpdateGame(Guid id, string name, decimal price, CancellationToken ct);
    Task DeleteGame(Guid id, CancellationToken ct);
    Task<Order> PlaceOrder(Guid userId, Guid gameId, CancellationToken ct);
    Task<Order> GetOrder(Guid userId, Guid orderId, CancellationToken ct);
    Task<IReadOnlyList<LibraryView>> GetLibrary(Guid userId, CancellationToken ct);
    Task ProcessPayment(PaymentResult payment, CancellationToken ct);
}

public sealed class CatalogService(ICatalogStore store)
{
    public Task<Game> CreateGame(string name, decimal price, CancellationToken ct) => store.CreateGame(Game.Create(name, price), ct);
    public Task<Order> PlaceOrder(Guid userId, Guid gameId, CancellationToken ct)
    {
        if (userId == Guid.Empty || gameId == Guid.Empty) throw new ArgumentException("Identificadores inválidos.");
        return store.PlaceOrder(userId, gameId, ct);
    }
    public Task ProcessPayment(PaymentResult p, CancellationToken ct)
    {
        if (p.EventId == Guid.Empty || p.OrderId == Guid.Empty || p.PaymentId == Guid.Empty || p.UserId == Guid.Empty || p.GameId == Guid.Empty ||
            p.OccurredAt == default || p.OccurredAt.Offset != TimeSpan.Zero || p.Currency != "BRL" || p.Status is not ("Approved" or "Rejected"))
            throw new InvalidPaymentException("Campos do pagamento inválidos.");
        try { Money.Validate(p.Price); }
        catch (ArgumentException) { throw new InvalidPaymentException("Preço do pagamento inválido."); }
        return store.ProcessPayment(p, ct);
    }
}
