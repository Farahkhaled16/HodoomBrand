namespace HodoomBrand.Models;

public enum MediaType { Image = 0, Video = 1 }

public class ProductMedia
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public Product? Product { get; set; }

    public string FilePath { get; set; } = "";   // /uploads/products/xxx.jpg
    public MediaType Type { get; set; }
    public bool IsMain { get; set; }
}