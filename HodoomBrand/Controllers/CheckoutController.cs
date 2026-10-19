using HodoomBrand.Data;
using HodoomBrand.Helpers;
using HodoomBrand.Models;
using HodoomBrand.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HodoomBrand.Controllers;

public class CheckoutController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly IConfiguration _config;
    private readonly UserManager<IdentityUser> _users;
    private readonly IOrderEmailService _email;
    private readonly IPaymobService _paymob;

    public static readonly string[] Governorates =
    {
        "Cairo", "Giza", "Alexandria", "Qalyubia", "Port Said", "Suez", "Dakahlia", "Sharqia",
        "Gharbia", "Monufia", "Beheira", "Kafr El Sheikh", "Damietta", "Ismailia", "Fayoum",
        "Beni Suef", "Minya", "Assiut", "Sohag", "Qena", "Luxor", "Aswan", "Red Sea",
        "New Valley", "Matrouh", "North Sinai", "South Sinai"
    };

    public CheckoutController(ApplicationDbContext db, IConfiguration config,
        UserManager<IdentityUser> users, IOrderEmailService email, IPaymobService paymob)
    {
        _db = db;
        _config = config;
        _users = users;
        _email = email;
        _paymob = paymob;
    }

    // GET: login-or-guest choice, then the form
    public async Task<IActionResult> Index(bool guest = false)
    {
        var cart = CartSession.Get(HttpContext.Session);
        if (!cart.Any()) return RedirectToAction("Index", "Cart");

        // Not logged in and hasn't chosen to continue as guest yet
        if (User.Identity?.IsAuthenticated != true && !guest)
            return View("Choice");

        var vm = new CheckoutViewModel();
        if (User.Identity?.IsAuthenticated == true)
        {
            var user = await _users.GetUserAsync(User);
            vm.Email = user?.Email ?? "";
        }

        ViewBag.OnlineEnabled = _paymob.IsConfigured;
        return View(vm);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(CheckoutViewModel vm)
    {
        var cart = CartSession.Get(HttpContext.Session);
        if (!cart.Any()) return RedirectToAction("Index", "Cart");

        ViewBag.OnlineEnabled = _paymob.IsConfigured;

        if (vm.PayOnline && !_paymob.IsConfigured)
            ModelState.AddModelError("", "Online payment is not available right now.");

        if (!Governorates.Contains(vm.Governorate))
            ModelState.AddModelError(nameof(vm.Governorate), "Please choose your governorate.");

        if (!ModelState.IsValid) return View(vm);

        // Load products fresh from the database
        var ids = cart.Select(i => i.ProductId).Distinct().ToList();
        var products = await _db.Products
            .Where(p => ids.Contains(p.Id) && p.IsActive)
            .ToDictionaryAsync(p => p.Id);

        // Check availability (total quantity per product across sizes/colors)
        foreach (var group in cart.GroupBy(i => i.ProductId))
        {
            if (!products.TryGetValue(group.Key, out var p))
            {
                TempData["Error"] = $"\"{group.First().Name}\" is no longer available. Please remove it from your cart.";
                return RedirectToAction("Index", "Cart");
            }
            if (group.Sum(i => i.Quantity) > p.StockQuantity)
            {
                TempData["Error"] = $"Only {p.StockQuantity} piece(s) of \"{p.Name}\" available.";
                return RedirectToAction("Index", "Cart");
            }
        }

        var shipping = _config.GetValue<decimal>("Store:ShippingFee", 60);

        var order = new Order
        {
            UserId = _users.GetUserId(User),   // null for guests
            CustomerName = vm.CustomerName.Trim(),
            Email = vm.Email.Trim(),
            Phone = vm.Phone.Trim(),
            Governorate = vm.Governorate,
            Address = vm.Address.Trim(),
            Notes = vm.Notes?.Trim(),
            PaymentMethod = vm.PayOnline ? PaymentMethod.Online : PaymentMethod.CashOnDelivery,
            PaymentStatus = PaymentStatus.Pending,
            Status = OrderStatus.New,
            ShippingFee = shipping,
            CreatedAt = DateTime.UtcNow
        };

        foreach (var item in cart)
        {
            var p = products[item.ProductId];
            order.Items.Add(new OrderItem
            {
                ProductId = p.Id,
                ProductName = p.Name,
                Size = item.Size,
                Color = item.Color,
                UnitPrice = p.DiscountPrice ?? p.Price,   // price from the database
                Quantity = item.Quantity
            });
            p.StockQuantity -= item.Quantity;
        }

        order.Subtotal = order.Items.Sum(i => i.UnitPrice * i.Quantity);
        order.Total = order.Subtotal + order.ShippingFee;

        _db.Orders.Add(order);
        await _db.SaveChangesAsync();   // order + stock update in one save

        HttpContext.Session.SetInt32("lastOrderId", order.Id);

        // Online: go pay first. Emails + clearing the cart happen after payment succeeds.
        if (order.PaymentMethod == PaymentMethod.Online)
            return RedirectToAction("Pay", "Payment", new { id = order.Id });

        // Cash on delivery
        await _email.SendOrderEmailsAsync(order);
        CartSession.Clear(HttpContext.Session);
        return RedirectToAction(nameof(Success), new { id = order.Id });
    }

    public async Task<IActionResult> Success(int id)
    {
        var order = await _db.Orders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == id);
        if (order == null) return NotFound();

        // Only the person who placed it (this session / this account) or the admin can see it
        var isOwnerSession = HttpContext.Session.GetInt32("lastOrderId") == id;
        var isOwnerUser = order.UserId != null && order.UserId == _users.GetUserId(User);
        if (!isOwnerSession && !isOwnerUser && !User.IsInRole("Admin"))
            return NotFound();

        return View(order);
    }
}