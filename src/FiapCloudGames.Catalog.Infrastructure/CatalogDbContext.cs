using FiapCloudGames.Catalog.Domain;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace FiapCloudGames.Catalog.Infrastructure;

public sealed class PaymentReceipt
{
    public Guid EventId { get; set; }
    public Guid OrderId { get; set; }
    public string Fingerprint { get; set; } = "";
    public DateTimeOffset ProcessedAt { get; set; }
}

public sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options)
{
    public DbSet<Game> Games => Set<Game>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<LibraryEntry> Library => Set<LibraryEntry>();
    public DbSet<PaymentReceipt> PaymentReceipts => Set<PaymentReceipt>();
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Game>(e =>
        {
            e.HasKey(x => x.Id); e.Property(x => x.Name).IsRequired(); e.Property(x => x.Price).HasPrecision(18, 2);
            e.ToTable("Games", t => t.HasCheckConstraint("CK_Games_Price", "\"Price\" >= 0"));
        });
        model.Entity<Order>(e =>
        {
            e.HasKey(x => x.Id); e.Property(x => x.Price).HasPrecision(18, 2); e.Property(x => x.Currency).IsRequired();
            e.Property(x => x.Status).HasConversion<string>();
            e.HasIndex(x => new { x.UserId, x.GameId }).IsUnique().HasFilter("\"Status\" = 'Pending'").HasDatabaseName("UX_Orders_Pending");
            e.HasIndex(x => x.PaymentId).IsUnique().HasDatabaseName("UX_Orders_PaymentId");
            e.HasOne<Game>().WithMany().HasForeignKey(x => x.GameId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable("Orders", t =>
            {
                t.HasCheckConstraint("CK_Orders_Money", "\"Price\" >= 0 AND \"Currency\" = 'BRL'");
                t.HasCheckConstraint("CK_Orders_State", "(\"Status\" = 'Pending' AND \"PaymentId\" IS NULL) OR (\"Status\" IN ('Approved', 'Rejected') AND \"PaymentId\" IS NOT NULL)");
            });
        });
        model.Entity<LibraryEntry>(e =>
        {
            e.HasKey(x => new { x.UserId, x.GameId }); e.HasIndex(x => x.OrderId).IsUnique();
            e.HasOne<Game>().WithMany().HasForeignKey(x => x.GameId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Order>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<PaymentReceipt>(e =>
        {
            e.HasKey(x => x.EventId); e.Property(x => x.Fingerprint).HasMaxLength(64).IsRequired();
            e.HasOne<Order>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
        });
        model.AddInboxStateEntity(); model.AddOutboxMessageEntity(); model.AddOutboxStateEntity();
    }
}
