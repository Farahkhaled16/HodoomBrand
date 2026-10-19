using System.ComponentModel.DataAnnotations;

namespace HodoomBrand.Models;

public class Product
{
    public int Id { get; set; }

    [Required, StringLength(150)]
    public string Name { get; set; } = "";

    public string? Description { get; set; }

    [Range(0, 1000000)]
    public decimal Price { get; set; }

    public decimal? DiscountPrice { get; set; }

    public int CategoryId { get; set; }
    public Category? Category { get; set; }

    // Sizes w colors bnekhazenhom keda: "S,M,L,XL" w "Black,Burgundy"
    public string? Sizes { get; set; }
    public string? Colors { get; set; }

    public int StockQuantity { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<ProductMedia> Media { get; set; } = new();
}