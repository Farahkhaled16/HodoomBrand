namespace HodoomBrand.Models;

public class CartItem
{
    public int ProductId { get; set; }
    public string Name { get; set; } = "";
    public string? Image { get; set; }
    public decimal UnitPrice { get; set; }
    public string? Size { get; set; }
    public string? Color { get; set; }
    public int Quantity { get; set; }

    public string Key => $"{ProductId}|{Size}|{Color}";
    public decimal LineTotal => UnitPrice * Quantity;
}