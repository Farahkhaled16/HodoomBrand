using HodoomBrand.Data;
using HodoomBrand.Models;
using HodoomBrand.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HodoomBrand.Controllers;

[Authorize]
public class OrdersController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<IdentityUser> _users;
    private readonly IConfiguration _config;
    private readonly IOrderEmailService _email;

    public OrdersController(ApplicationDbContext db, UserManager<IdentityUser> users,
        IConfiguration config, IOrderEmailService email)
    {
        _db = db;
        _users = users;
        _config = config;
        _email = email;
    }

    public async Task<IActionResult> Index()
    {
        var uid = _users.GetUserId(User);
        var orders = await _db.Orders.Include(o => o.Items)
            .Where(o => o.UserId == uid)
            .OrderByDescending(o => o.Id)
            .ToListAsync();
        return View(orders);
    }

    public async Task<IActionResult> Details(int id)
    {
        var uid = _users.GetUserId(User);
        var order = await _db.Orders.Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == id && o.UserId == uid);
        if (order == null) return NotFound();

        ViewBag.CanCancel = CanCancel(order, out var minutesLeft);
        ViewBag.MinutesLeft = minutesLeft;
        return View(order);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id)
    {
        var uid = _users.GetUserId(User);
        var order = await _db.Orders.Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == id && o.UserId == uid);
        if (order == null) return NotFound();

        if (!CanCancel(order, out _))
        {
            TempData["Error"] = "This order can no longer be cancelled online. Please contact us.";
            return RedirectToAction(nameof(Details), new { id });
        }

        foreach (var item in order.Items)
        {
            var product = await _db.Products.FindAsync(item.ProductId);
            if (product != null) product.StockQuantity += item.Quantity;
        }

        order.Status = OrderStatus.Cancelled;
        order.CancelledAt = DateTime.UtcNow;
        order.CancelledByCustomer = true;
        await _db.SaveChangesAsync();

        await _email.SendStatusEmailAsync(order);
        await _email.SendCustomerCancelledAsync(order);

        TempData["Success"] = $"Order #{order.Id} has been cancelled.";
        return RedirectToAction(nameof(Details), new { id });
    }

    // Allowed only for New/Confirmed orders, within the cancel window
    private bool CanCancel(Order o, out int minutesLeft)
    {
        var window = _config.GetValue<int>("Store:CancelWindowMinutes", 60);
        var left = window - (DateTime.UtcNow - o.CreatedAt).TotalMinutes;
        minutesLeft = (int)Math.Ceiling(Math.Max(left, 0));
        return left > 0 && (o.Status == OrderStatus.New || o.Status == OrderStatus.Confirmed);
    }
}