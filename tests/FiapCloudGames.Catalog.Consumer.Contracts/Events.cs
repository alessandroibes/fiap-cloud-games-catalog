namespace FiapCloudGames.Contracts.V1;

// Fixture independente: nenhum ProjectReference ao assembly de contratos do serviço.
public sealed record OrderPlacedEvent
{
    public required Guid EventId { get; init; }
    public required DateTimeOffset OccurredAt { get; init; }
    public required Guid OrderId { get; init; }
    public required Guid UserId { get; init; }
    public required Guid GameId { get; init; }
    public required decimal Price { get; init; }
    public required string Currency { get; init; }
}
public sealed record PaymentProcessedEvent
{
    public required Guid EventId { get; init; }
    public required DateTimeOffset OccurredAt { get; init; }
    public required Guid OrderId { get; init; }
    public required Guid PaymentId { get; init; }
    public required Guid UserId { get; init; }
    public required Guid GameId { get; init; }
    public required string Status { get; init; }
    public required decimal Price { get; init; }
    public required string Currency { get; init; }
}
