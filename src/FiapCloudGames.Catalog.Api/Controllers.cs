using FiapCloudGames.Catalog.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

namespace FiapCloudGames.Catalog.Api;

public record GameInput([Required] string Name, [Required] decimal? Price);
public record OrderInput(Guid GameId);

[ApiController, Route("api/games")]
public sealed class GamesController(CatalogService service, ICatalogStore store) : ControllerBase
{
    [HttpGet] public async Task<IActionResult> List(CancellationToken ct) => Ok(await store.ListGames(ct));
    [HttpGet("{id:guid}")] public async Task<IActionResult> Get(Guid id, CancellationToken ct) => Ok(await store.GetGame(id, ct));
    [HttpPost, Authorize(Roles = "Admin")]
    public async Task<IActionResult> Create(GameInput input, CancellationToken ct)
    {
        var game = await service.CreateGame(input.Name, input.Price!.Value, ct);
        return CreatedAtAction(nameof(Get), new { id = game.Id }, game);
    }
    [HttpPut("{id:guid}"), Authorize(Roles = "Admin")]
    public async Task<IActionResult> Update(Guid id, GameInput input, CancellationToken ct) { await store.UpdateGame(id, input.Name, input.Price!.Value, ct); return NoContent(); }
    [HttpDelete("{id:guid}"), Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) { await store.DeleteGame(id, ct); return NoContent(); }
}

[ApiController, Route("api/orders"), Authorize]
public sealed class OrdersController(CatalogService service, ICatalogStore store) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirst("sub")!.Value);
    [HttpPost]
    public async Task<IActionResult> Place(OrderInput input, CancellationToken ct)
    {
        var order = await service.PlaceOrder(UserId, input.GameId, ct);
        return AcceptedAtAction(nameof(Get), new { orderId = order.Id }, new { orderId = order.Id, status = order.Status.ToString() });
    }
    [HttpGet("{orderId:guid}")] public async Task<IActionResult> Get(Guid orderId, CancellationToken ct) => Ok(await store.GetOrder(UserId, orderId, ct));
}

[ApiController, Route("api/library"), Authorize]
public sealed class LibraryController(ICatalogStore store) : ControllerBase
{
    [HttpGet] public async Task<IActionResult> Get(CancellationToken ct) => Ok(await store.GetLibrary(Guid.Parse(User.FindFirst("sub")!.Value), ct));
}
