using HodoomBrand.Data;
using HodoomBrand.Helpers;
using HodoomBrand.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HodoomBrand.Controllers;

public class CartController : Controller
{
    private readonly ApplicationDbContext _db;
    public CartController(ApplicationDbContext db) => _db = db;

    public IActionResult Index()
    {
        return View(CartSession.Get(HttpContext.Session));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Add(int productId, string? size, string? color, int quantity = 1)
    {
        var product = await _db.Products.Include(p => p.Media)
            .FirstOrDefaultAsync(p => p.Id == productId && p.IsActive);
        if (product == null) return NotFound();

        if (quantity < 1) quantity = 1;

        // The price always comes from the database, never from the browser
        var price = product.DiscountPrice ?? product.Price;

        var cart = CartSession.Get(HttpContext.Session);
        var key = $"{productId}|{size}|{color}";
        var existing = cart.FirstOrDefault(i => i.Key == key);

        var newQty = (existing?.Quantity ?? 0) + quantity;
        if (newQty > product.StockQuantity)
        {
            TempData["Error"] = $"Only {product.StockQuantity} piece(s) of \"{product.Name}\" available.";
            return RedirectToAction("Details", "Shop", new { id = productId });
        }

        if (existing != null)
        {
            existing.Quantity = newQty;
        }
        else
        {
            var main = product.Media.FirstOrDefault(m => m.IsMain)
                       ?? product.Media.FirstOrDefault(m => m.Type == MediaType.Image);
            cart.Add(new CartItem
            {
                ProductId = product.Id,
                Name = product.Name,
                Image = main?.FilePath,
                UnitPrice = price,
                Size = size,
                Color = color,
                Quantity = quantity
            });
        }

        CartSession.Save(HttpContext.Session, cart);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(string key, int quantity)
    {
        var cart = CartSession.Get(HttpContext.Session);
        var item = cart.FirstOrDefault(i => i.Key == key);
        if (item != null)
        {
            if (quantity < 1)
            {
                cart.Remove(item);
            }
            else
            {
                var stock = await _db.Products.Where(p => p.Id == item.ProductId)
                    .Select(p => p.StockQuantity).FirstOrDefaultAsync();
                if (quantity > stock)
                {
                    quantity = stock;
                    TempData["Error"] = $"Only {stock} piece(s) available for \"{item.Name}\".";
                }
                item.Quantity = quantity;
            }
            CartSession.Save(HttpContext.Session, cart);
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult Remove(string key)
    {
        var cart = CartSession.Get(HttpContext.Session);
        cart.RemoveAll(i => i.Key == key);
        CartSession.Save(HttpContext.Session, cart);
        return RedirectToAction(nameof(Index));
    }
}