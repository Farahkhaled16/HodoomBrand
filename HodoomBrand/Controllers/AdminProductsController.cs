using HodoomBrand.Data;
using HodoomBrand.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace HodoomBrand.Controllers;

[Authorize(Roles = "Admin")]
public class AdminProductsController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly IWebHostEnvironment _env;

    private static readonly string[] ImageExt = { ".jpg", ".jpeg", ".png", ".webp" };
    private static readonly string[] VideoExt = { ".mp4", ".webm" };

    public AdminProductsController(ApplicationDbContext db, IWebHostEnvironment env)
    {
        _db = db;
        _env = env;
    }

    public async Task<IActionResult> Index()
    {
        var products = await _db.Products
            .Include(p => p.Category)
            .Include(p => p.Media)
            .OrderByDescending(p => p.Id)
            .ToListAsync();
        return View(products);
    }

    public async Task<IActionResult> Create()
    {
        await LoadCategories();
        return View(new Product());
    }

    [HttpPost, ValidateAntiForgeryToken]
    [RequestSizeLimit(209715200)]
    [RequestFormLimits(MultipartBodyLengthLimit = 209715200)]
    public async Task<IActionResult> Create(Product product, List<IFormFile>? files)
    {
        ModelState.Remove(nameof(Product.Category));
        ModelState.Remove(nameof(Product.Media));

        if (!ModelState.IsValid)
        {
            await LoadCategories(product.CategoryId);
            return View(product);
        }

        if (!await _db.Categories.AnyAsync(c => c.Id == product.CategoryId))
            ModelState.AddModelError(nameof(Product.CategoryId), "Please choose a category (add one first from the Categories page).");

        if (!ModelState.IsValid)
        {
            await LoadCategories(product.CategoryId);
            return View(product);
        }

        product.CreatedAt = DateTime.UtcNow;
        _db.Products.Add(product);
        await _db.SaveChangesAsync();
        await SaveFiles(product.Id, files);

        TempData["Success"] = "Product added.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int id)
    {
        var product = await _db.Products.Include(p => p.Media).FirstOrDefaultAsync(p => p.Id == id);
        if (product == null) return NotFound();
        await LoadCategories(product.CategoryId);
        return View(product);
    }

    [HttpPost, ValidateAntiForgeryToken]
    [RequestSizeLimit(209715200)]
    [RequestFormLimits(MultipartBodyLengthLimit = 209715200)]
    public async Task<IActionResult> Edit(Product product, List<IFormFile>? files)
    {
        ModelState.Remove(nameof(Product.Category));
        ModelState.Remove(nameof(Product.Media));

        var existing = await _db.Products.Include(p => p.Media).FirstOrDefaultAsync(p => p.Id == product.Id);
        if (existing == null) return NotFound();

        if (!ModelState.IsValid)
        {
            product.Media = existing.Media;
            await LoadCategories(product.CategoryId);
            return View(product);
        }

        existing.Name = product.Name;
        existing.Description = product.Description;
        existing.Price = product.Price;
        existing.DiscountPrice = product.DiscountPrice;
        existing.CategoryId = product.CategoryId;
        existing.Sizes = product.Sizes;
        existing.Colors = product.Colors;
        existing.StockQuantity = product.StockQuantity;
        existing.IsActive = product.IsActive;

        await _db.SaveChangesAsync();
        await SaveFiles(existing.Id, files);

        TempData["Success"] = "Product updated.";
        return RedirectToAction(nameof(Edit), new { id = existing.Id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var product = await _db.Products.Include(p => p.Media).FirstOrDefaultAsync(p => p.Id == id);
        if (product == null) return NotFound();

        foreach (var m in product.Media) DeletePhysicalFile(m.FilePath);

        _db.Products.Remove(product);
        await _db.SaveChangesAsync();

        TempData["Success"] = "Product deleted.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteMedia(int id)
    {
        var media = await _db.ProductMedia.FindAsync(id);
        if (media == null) return NotFound();

        var productId = media.ProductId;
        var wasMain = media.IsMain;

        DeletePhysicalFile(media.FilePath);
        _db.ProductMedia.Remove(media);
        await _db.SaveChangesAsync();

        if (wasMain)
        {
            var next = await _db.ProductMedia
                .FirstOrDefaultAsync(m => m.ProductId == productId && m.Type == MediaType.Image);
            if (next != null)
            {
                next.IsMain = true;
                await _db.SaveChangesAsync();
            }
        }
        return RedirectToAction(nameof(Edit), new { id = productId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SetMain(int id)
    {
        var media = await _db.ProductMedia.FindAsync(id);
        if (media == null || media.Type != MediaType.Image) return NotFound();

        var all = await _db.ProductMedia.Where(m => m.ProductId == media.ProductId).ToListAsync();
        foreach (var m in all) m.IsMain = (m.Id == media.Id);
        await _db.SaveChangesAsync();

        return RedirectToAction(nameof(Edit), new { id = media.ProductId });
    }

    // ---------- Helpers ----------

    private async Task LoadCategories(int? selected = null)
    {
        var cats = await _db.Categories.OrderBy(c => c.Name).ToListAsync();
        ViewBag.Categories = new SelectList(cats, "Id", "Name", selected);
    }

    private async Task SaveFiles(int productId, List<IFormFile>? files)
    {
        if (files == null || files.Count == 0) return;

        var folder = Path.Combine(_env.WebRootPath, "uploads", "products");
        Directory.CreateDirectory(folder);

        bool hasMain = await _db.ProductMedia.AnyAsync(m => m.ProductId == productId && m.IsMain);

        foreach (var file in files)
        {
            if (file.Length == 0) continue;

            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            MediaType type;
            if (ImageExt.Contains(ext)) type = MediaType.Image;
            else if (VideoExt.Contains(ext)) type = MediaType.Video;
            else continue; // ignore any other file type

            var name = $"{Guid.NewGuid():N}{ext}";
            using (var stream = System.IO.File.Create(Path.Combine(folder, name)))
            {
                await file.CopyToAsync(stream);
            }

            var isMain = !hasMain && type == MediaType.Image;
            if (isMain) hasMain = true;

            _db.ProductMedia.Add(new ProductMedia
            {
                ProductId = productId,
                FilePath = "/uploads/products/" + name,
                Type = type,
                IsMain = isMain
            });
        }
        await _db.SaveChangesAsync();
    }

    private void DeletePhysicalFile(string webPath)
    {
        var full = Path.Combine(_env.WebRootPath, webPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
        if (System.IO.File.Exists(full)) System.IO.File.Delete(full);
    }
}