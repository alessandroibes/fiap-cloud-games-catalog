using FiapCloudGames.Catalog.Application;
using FiapCloudGames.Contracts.V1;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FiapCloudGames.Catalog.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddCatalogInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        services.AddDbContext<CatalogDbContext>(o => o.UseNpgsql(config.GetConnectionString("Catalog") ?? throw new InvalidOperationException("ConnectionStrings:Catalog obrigatório.")));
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<ICatalogStore, CatalogStore>(); services.AddScoped<CatalogService>();
        services.AddMassTransit(bus =>
        {
            bus.AddConsumer<PaymentConsumer>();
            bus.AddEntityFrameworkOutbox<CatalogDbContext>(o => { o.UsePostgres(); o.UseBusOutbox(); o.QueryDelay = TimeSpan.FromSeconds(1); });
            bus.UsingRabbitMq((context, cfg) =>
            {
                cfg.DeployPublishTopology = true;
                cfg.Publish<OrderPlacedEvent>(p => p.BindQueue("FiapCloudGames.Contracts.V1:OrderPlacedEvent", "payments-order-placed-v1", b => { b.Durable = true; b.AutoDelete = false; }));
                cfg.Host(config["RabbitMq:Host"] ?? "localhost", config.GetValue<ushort>("RabbitMq:Port", 5672), config["RabbitMq:VirtualHost"] ?? "fcg", h =>
                {
                    h.Username(config["RabbitMq:Username"] ?? throw new InvalidOperationException("RabbitMq:Username obrigatório."));
                    h.Password(config["RabbitMq:Password"] ?? throw new InvalidOperationException("RabbitMq:Password obrigatório."));
                });
                cfg.ReceiveEndpoint("catalog-payment-processed-v1", e =>
                {
                    e.UseMessageRetry(r => { r.Ignore<InvalidPaymentException>(); r.Intervals(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15)); });
                    e.ConfigureConsumer<PaymentConsumer>(context);
                });
            });
        });
        services.Configure<MassTransitHostOptions>(o => { o.WaitUntilStarted = false; o.StopTimeout = TimeSpan.FromSeconds(10); });
        return services;
    }
}
