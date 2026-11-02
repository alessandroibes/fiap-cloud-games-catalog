namespace FiapCloudGames.Catalog.Domain;

public sealed class Game
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = "";
    public decimal Price { get; private set; }
    public bool IsActive { get; private set; }
    private Game() { }
    public static Game Create(string name, decimal price)
    {
        var game = new Game { Id = Guid.NewGuid(), IsActive = true };
        game.Update(name, price);
        return game;
    }
    public void Update(string name, decimal price)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Nome obrigatório.");
        Money.Validate(price);
        Name = name.Trim(); Price = price;
    }
    public void Deactivate() => IsActive = false;
}

public static class Money
{
    public static void Validate(decimal price)
    {
        if (price < 0 || price > 9999999999999999.99m || decimal.Round(price, 2) != price)
            throw new ArgumentException("Preço deve ser não negativo, com até duas casas decimais e precisão 18,2.");
    }
}
