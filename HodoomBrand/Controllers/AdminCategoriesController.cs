using HodoomBrand.Data;
using HodoomBrand.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HodoomBrand.Controllers;

[Authorize(Roles = "Admin")]
public class AdminCategoriesController : Controller
{
    private readonly ApplicationDbContext _db;
    public AdminCategoriesController(ApplicationDbContext db) => _db = db;

    public async Task<IActionResult> Index()
    {
        var cats = await _db.Categories.Include(c => c.Products).OrderBy(c => c.Name).ToListAsync();
        return View(cats);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string name)
    {
        name = (name ?? "").Trim();
        if (name.Length > 0 && !await _db.Categories.AnyAsync(c => c.Name == name))
        {
            _db.Categories.Add(new Category { Name = name });
            await _db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var cat = await _db.Categories.Include(c => c.Products).FirstOrDefaultAsync(c => c.Id == id);
        if (cat == null) return NotFound();

        if (cat.Products.Any())
            TempData["Error"] = "You can't delete a category that still has products.";
        else
        {
            _db.Categories.Remove(cat);
            await _db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Index));
    }
}