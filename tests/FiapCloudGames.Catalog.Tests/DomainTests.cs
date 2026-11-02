using FiapCloudGames.Catalog.Domain;
using Xunit;

namespace FiapCloudGames.Catalog.Tests;

public sealed class DomainTests
{
    [Theory]
    [InlineData("", "10")]
    [InlineData("   ", "10")]
    [InlineData("Jogo", "-0.01")]
    [InlineData("Jogo", "0.001")]
    [InlineData("Jogo", "10000000000000000")]
    public void RejectsInvalidGame(string name, string price) => Assert.Throws<ArgumentException>(() => Game.Create(name, decimal.Parse(price, System.Globalization.CultureInfo.InvariantCulture)));

    [Fact]
    public void FreeGameAndSnapshotAreSupported()
    {
        var game = Game.Create("  Jogo  ", 0m);
        var order = Order.Create(Guid.NewGuid(), game, DateTimeOffset.UtcNow);
        game.Update("Novo", 20m);
        Assert.Equal(0m, order.Price); Assert.Equal("Novo", game.Name); Assert.Equal(OrderStatus.Pending, order.Status);
    }
    [Fact]
    public void InactiveGameCannotBePurchased()
    {
        var game = Game.Create("Jogo", 10); game.Deactivate();
        Assert.Throws<InvalidOperationException>(() => Order.Create(Guid.NewGuid(), game, DateTimeOffset.UtcNow));
    }
    [Theory]
    [InlineData(OrderStatus.Pending)]
    [InlineData(OrderStatus.Rejected)]
    public void OnlyApprovalGrantsLibrary(OrderStatus status)
    {
        var order = Order.Create(Guid.NewGuid(), Game.Create("Jogo", 10), DateTimeOffset.UtcNow);
        if (status == OrderStatus.Rejected) order.Complete(Guid.NewGuid(), status);
        Assert.Throws<InvalidOperationException>(() => LibraryEntry.Grant(order, DateTimeOffset.UtcNow));
    }
    [Fact]
    public void TerminalResultIsImmutableAndEquivalentReplayIsHarmless()
    {
        var order = Order.Create(Guid.NewGuid(), Game.Create("Jogo", 10), DateTimeOffset.UtcNow);
        var payment = Guid.NewGuid(); Assert.True(order.Complete(payment, OrderStatus.Approved));
        Assert.False(order.Complete(payment, OrderStatus.Approved));
        Assert.Throws<InvalidOperationException>(() => order.Complete(payment, OrderStatus.Rejected));
        Assert.Throws<InvalidOperationException>(() => order.Complete(Guid.NewGuid(), OrderStatus.Approved));
        Assert.Equal(order.Id, LibraryEntry.Grant(order, DateTimeOffset.UtcNow).OrderId);
    }
}
