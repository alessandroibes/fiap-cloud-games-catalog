using System.Text;
using System.Text.Json.Serialization;
using FiapCloudGames.Catalog.Api;
using FiapCloudGames.Catalog.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args.Where(a => a != "--migrate").ToArray());
builder.Logging.ClearProviders(); builder.Logging.AddJsonConsole();
builder.Services.AddControllers().AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails(); builder.Services.AddCatalogInfrastructure(builder.Configuration);
var key = builder.Configuration["Jwt:Key"] ?? "";
var issuer = builder.Configuration["Jwt:Issuer"];
var audience = builder.Configuration["Jwt:Audience"];
if (Encoding.UTF8.GetByteCount(key) < 32 || string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(audience))
    throw new InvalidOperationException("JWT exige chave externa de 32 bytes, issuer e audience.");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    o.MapInboundClaims = false;
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
        ValidateIssuer = true,
        ValidIssuer = issuer,
        ValidateAudience = true,
        ValidAudience = audience,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero,
        NameClaimType = "sub",
        RoleClaimType = "role",
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256]
    };
    o.Events = new JwtBearerEvents
    {
        OnTokenValidated = context =>
        {
            if (!Guid.TryParse(context.Principal?.FindFirst("sub")?.Value, out var id) || id == Guid.Empty) context.Fail("Identidade inválida.");
            return Task.CompletedTask;
        },
        OnChallenge = context => { context.HandleResponse(); return ErrorMiddleware.Write(context.HttpContext, 401, "Autenticação necessária."); },
        OnForbidden = context => ErrorMiddleware.Write(context.HttpContext, 403, "Acesso negado.")
    };
});
builder.Services.AddAuthorization();
await using var app = builder.Build();
if (args.Contains("--migrate"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().Database.MigrateAsync();
    return;
}
app.UseMiddleware<ErrorMiddleware>(); app.UseAuthentication(); app.UseAuthorization(); app.MapControllers();
app.MapGet("/health/live", () => Results.Ok(new { status = "Healthy" }));
app.MapGet("/health/ready", async (CatalogDbContext db, CancellationToken ct) =>
{
    try { return Results.StatusCode(await db.Database.CanConnectAsync(ct) && !(await db.Database.GetPendingMigrationsAsync(ct)).Any() ? 200 : 503); }
    catch { return Results.StatusCode(503); }
});
app.Run();
public partial class Program;
