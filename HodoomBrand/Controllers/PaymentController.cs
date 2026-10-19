using System.Text.Json;
using HodoomBrand.Data;
using HodoomBrand.Helpers;
using HodoomBrand.Models;
using HodoomBrand.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HodoomBrand.Controllers;

public class PaymentController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly IPaymobService _paymob;
    private readonly IOrderEmailService _email;
    private readonly UserManager<IdentityUser> _users;
    private readonly ILogger<PaymentController> _log;

    public PaymentController(ApplicationDbContext db, IPaymobService paymob, IOrderEmailService email,
        UserManager<IdentityUser> users, ILogger<PaymentController> log)
    {
        _db = db;
        _paymob = paymob;
        _email = email;
        _users = users;
        _log = log;
    }

    // Send the customer to Paymob (also used for "Try again")
    public async Task<IActionResult> Pay(int id)
    {
        var order = await _db.Orders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == id);
        if (order == null) return NotFound();

        var isOwnerSession = HttpContext.Session.GetInt32("lastOrderId") == id;
        var isOwnerUser = order.UserId != null && order.UserId == _users.GetUserId(User);
        if (!isOwnerSession && !isOwnerUser) return NotFound();

        if (order.PaymentMethod != PaymentMethod.Online ||
            order.PaymentStatus == PaymentStatus.Paid ||
            order.Status != OrderStatus.New)
            return NotFound();

        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var url = await _paymob.CreatePaymentUrlAsync(order, baseUrl);
        if (url == null)
        {
            ViewBag.OrderId = id;
            ViewBag.Message = "We couldn't start the payment. Please try again in a moment.";
            return View("Failed");
        }
        return Redirect(url);
    }

    // The customer comes back here from Paymob (GET with query string)
    [HttpGet]
    public async Task<IActionResult> Return()
    {
        var q = Request.Query.ToDictionary(k => k.Key, v => v.Value.ToString());
        if (!q.TryGetValue("hmac", out var hmac)) return NotFound();

        var values = new Dictionary<string, string>(q);
        if (q.TryGetValue("order", out var oid)) values["order.id"] = oid;

        if (!_paymob.VerifyHmac(values, hmac))
        {
            _log.LogWarning("Paymob return: invalid HMAC");
            return BadRequest("Invalid signature.");
        }

        var orderId = ParseOrderId(q.GetValueOrDefault("merchant_order_id"));
        if (orderId == null) return NotFound();

        var success = q.GetValueOrDefault("success") == "true";
        var pending = q.GetValueOrDefault("pending") == "true";

        if (success)
        {
            await MarkPaidAsync(orderId.Value, q.GetValueOrDefault("id"));
            CartSession.Clear(HttpContext.Session);
            HttpContext.Session.SetInt32("lastOrderId", orderId.Value);
            return RedirectToAction("Success", "Checkout", new { id = orderId.Value });
        }

        ViewBag.OrderId = orderId.Value;
        ViewBag.Message = pending
            ? "Your payment is still being processed. We'll email you when it's confirmed."
            : "Your payment was not completed. You have not been charged.";
        return View("Failed");
    }

    // Paymob calls this server-to-server (safety net if the customer closes the browser)
    [HttpPost]
    public async Task<IActionResult> Webhook()
    {
        using var doc = await JsonDocument.ParseAsync(Request.Body);
        if (!doc.RootElement.TryGetProperty("obj", out var obj)) return BadRequest();

        var values = new Dictionary<string, string>();
        foreach (var key in PaymobService.HmacKeys)
            values[key] = ReadPath(obj, key.Split('.'));

        var hmac = Request.Query["hmac"].ToString();
        if (!_paymob.VerifyHmac(values, hmac))
        {
            _log.LogWarning("Paymob webhook: invalid HMAC");
            return BadRequest();
        }

        var orderId = ParseOrderId(ReadPath(obj, new[] { "order", "merchant_order_id" }));
        if (orderId != null && values["success"] == "true")
            await MarkPaidAsync(orderId.Value, values["id"]);

        return Ok();
    }

    private async Task MarkPaidAsync(int orderId, string? transactionId)
    {
        var order = await _db.Orders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == orderId);
        if (order == null || order.PaymentStatus == PaymentStatus.Paid) return;   // already handled

        order.PaymentStatus = PaymentStatus.Paid;
        order.PaymobTransactionId = transactionId;
        await _db.SaveChangesAsync();

        await _email.SendOrderEmailsAsync(order);   // emails go out only after payment
    }

    // "BORDO-12-1760000000" -> 12
    private static int? ParseOrderId(string? merchantOrderId)
    {
        var parts = (merchantOrderId ?? "").Split('-');
        return parts.Length >= 2 && parts[0] == "BORDO" && int.TryParse(parts[1], out var id) ? id : null;
    }

    private static string ReadPath(JsonElement e, string[] path)
    {
        foreach (var p in path)
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(p, out e)) return "";

        return e.ValueKind switch
        {
            JsonValueKind.String => e.GetString() ?? "",
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => "",
            _ => e.GetRawText()
        };
    }
}