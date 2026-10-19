using HodoomBrand.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HodoomBrand.Controllers;

public class ShopController : Controller
{
    private readonly ApplicationDbContext _db;
    public ShopController(ApplicationDbContext db) => _db = db;

    public async Task<IActionResult> Index(int? category, string? q)
    {
        var query = _db.Products
            .Include(p => p.Media)
            .Include(p => p.Category)
            .Where(p => p.IsActive);

        if (category.HasValue) query = query.Where(p => p.CategoryId == category.Value);
        if (!string.IsNullOrWhiteSpace(q)) query = query.Where(p => p.Name.Contains(q));

        ViewBag.Categories = await _db.Categories.OrderBy(c => c.Name).ToListAsync();
        ViewBag.SelectedCategory = category;
        ViewBag.Q = q;

        return View(await query.OrderByDescending(p => p.Id).ToListAsync());
    }

    public async Task<IActionResult> Details(int id)
    {
        var product = await _db.Products
            .Include(p => p.Media)
            .Include(p => p.Category)
            .FirstOrDefaultAsync(p => p.Id == id && p.IsActive);

        if (product == null) return NotFound();
        return View(product);
    }
}