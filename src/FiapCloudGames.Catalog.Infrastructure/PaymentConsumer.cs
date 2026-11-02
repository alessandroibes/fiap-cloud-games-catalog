using FiapCloudGames.Catalog.Application;
using FiapCloudGames.Contracts.V1;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace FiapCloudGames.Catalog.Infrastructure;

public sealed class PaymentConsumer(CatalogService service, ILogger<PaymentConsumer> logger) : IConsumer<PaymentProcessedEvent>
{
    public async Task Consume(ConsumeContext<PaymentProcessedEvent> context)
    {
        var p = context.Message;
        await service.ProcessPayment(new PaymentResult(p.EventId, p.OccurredAt, p.OrderId, p.PaymentId, p.UserId, p.GameId, p.Status, p.Price, p.Currency), context.CancellationToken);
        logger.LogInformation("Pagamento processado. EventId {EventId}, OrderId {OrderId}, Status {Status}", p.EventId, p.OrderId, p.Status);
    }
}
