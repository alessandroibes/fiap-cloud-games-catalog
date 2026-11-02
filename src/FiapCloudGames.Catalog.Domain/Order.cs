namespace FiapCloudGames.Catalog.Domain;

public enum OrderStatus { Pending, Approved, Rejected }

public sealed class Order
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public Guid GameId { get; private set; }
    public decimal Price { get; private set; }
    public string Currency { get; private set; } = "BRL";
    public OrderStatus Status { get; private set; }
    public Guid? PaymentId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    private Order() { }
    public static Order Create(Guid userId, Game game, DateTimeOffset now)
    {
        if (userId == Guid.Empty) throw new ArgumentException("Usuário inválido.");
        if (!game.IsActive) throw new InvalidOperationException("Jogo inativo.");
        return new Order { Id = Guid.NewGuid(), UserId = userId, GameId = game.Id, Price = game.Price, CreatedAt = now.ToUniversalTime() };
    }
    public bool Complete(Guid paymentId, OrderStatus status)
    {
        if (paymentId == Guid.Empty || status is not (OrderStatus.Approved or OrderStatus.Rejected))
            throw new ArgumentException("Resultado de pagamento inválido.");
        if (Status != OrderStatus.Pending)
        {
            if (Status != status || PaymentId != paymentId) throw new InvalidOperationException("Resultado conflitante com pedido terminal.");
            return false;
        }
        Status = status; PaymentId = paymentId;
        return true;
    }
}

public sealed class LibraryEntry
{
    public Guid UserId { get; private set; }
    public Guid GameId { get; private set; }
    public Guid OrderId { get; private set; }
    public DateTimeOffset AcquiredAt { get; private set; }
    private LibraryEntry() { }
    public static LibraryEntry Grant(Order order, DateTimeOffset now)
    {
        if (order.Status != OrderStatus.Approved) throw new InvalidOperationException("Concessão exige pagamento aprovado.");
        return new LibraryEntry { UserId = order.UserId, GameId = order.GameId, OrderId = order.Id, AcquiredAt = now.ToUniversalTime() };
    }
}
