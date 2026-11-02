using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FiapCloudGames.Catalog.Infrastructure;

public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<CatalogDbContext>
{
    public CatalogDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<CatalogDbContext>()
        .UseNpgsql(Environment.GetEnvironmentVariable("ConnectionStrings__Catalog") ?? throw new InvalidOperationException("Configure ConnectionStrings__Catalog para executar EF.")).Options);
}
