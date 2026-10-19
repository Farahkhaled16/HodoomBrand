using HodoomBrand.Data;
using HodoomBrand.Models;
using HodoomBrand.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HodoomBrand.Controllers;

[Authorize(Roles = "Admin")]
public class AdminOrdersController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly IOrderEmailService _email;

    public AdminOrdersController(ApplicationDbContext db, IOrderEmailService email)
    {
        _db = db;
        _email = email;
    }

    public async Task<IActionResult> Index(OrderStatus? status)
    {
        var query = _db.Orders.Include(o => o.Items).AsQueryable();
        if (status.HasValue) query = query.Where(o => o.Status == status.Value);

        ViewBag.Status = status;
        ViewBag.NewCount = await _db.Orders.CountAsync(o => o.Status == OrderStatus.New);
        ViewBag.Counts = await _db.Orders
            .GroupBy(o => o.Status)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);

        return View(await query.OrderByDescending(o => o.Id).ToListAsync());
    }
    public async Task<IActionResult> Details(int id)
    {
        var order = await _db.Orders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == id);
        if (order == null) return NotFound();
        return View(order);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateStatus(int id, OrderStatus status)
    {
        var order = await _db.Orders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == id);
        if (order == null) return NotFound();

        if (order.Status == OrderStatus.Cancelled)
        {
            TempData["Error"] = "A cancelled order can't be changed.";
            return RedirectToAction(nameof(Details), new { id });
        }

        if (order.Status == status)
            return RedirectToAction(nameof(Details), new { id });

        // Cancelling puts the pieces back in stock
        if (status == OrderStatus.Cancelled)
        {
            foreach (var item in order.Items)
            {
                var product = await _db.Products.FindAsync(item.ProductId);
                if (product != null) product.StockQuantity += item.Quantity;
            }
        }

        if (status == OrderStatus.Confirmed) order.ConfirmedAt = DateTime.UtcNow;
        if (status == OrderStatus.Cancelled)
        {
            order.CancelledAt = DateTime.UtcNow;
            order.CancelledByCustomer = false;
        }

        order.Status = status;
        if (status == OrderStatus.Delivered) order.PaymentStatus = PaymentStatus.Paid;

        await _db.SaveChangesAsync();
        await _email.SendStatusEmailAsync(order);

        TempData["Success"] = $"Order #{order.Id} is now {status}.";
        return RedirectToAction(nameof(Details), new { id });
    }
}