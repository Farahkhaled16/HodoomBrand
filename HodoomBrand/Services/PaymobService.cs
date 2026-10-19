using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HodoomBrand.Models;

namespace HodoomBrand.Services;

public interface IPaymobService
{
    bool IsConfigured { get; }
    Task<string?> CreatePaymentUrlAsync(Order order, string baseUrl);
    bool VerifyHmac(IDictionary<string, string> values, string? receivedHmac);
}

public class PaymobService : IPaymobService
{
    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly ILogger<PaymobService> _log;

    // The fields Paymob signs, in this exact order
    public static readonly string[] HmacKeys =
    {
        "amount_cents", "created_at", "currency", "error_occured", "has_parent_transaction", "id",
        "integration_id", "is_3d_secure", "is_auth", "is_capture", "is_refunded",
        "is_standalone_payment", "is_voided", "order.id", "owner", "pending",
        "source_data.pan", "source_data.sub_type", "source_data.type", "success"
    };

    public PaymobService(HttpClient http, IConfiguration config, ILogger<PaymobService> log)
    {
        _http = http;
        _config = config;
        _log = log;
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_config["Paymob:SecretKey"]) &&
        !string.IsNullOrWhiteSpace(_config["Paymob:PublicKey"]) &&
        !string.IsNullOrWhiteSpace(_config["Paymob:IntegrationId"]) &&
        !string.IsNullOrWhiteSpace(_config["Paymob:HmacSecret"]);

    public async Task<string?> CreatePaymentUrlAsync(Order order, string baseUrl)
    {
        if (!IsConfigured)
        {
            _log.LogError("Paymob is not configured.");
            return null;
        }

        try
        {
            var items = new List<object>();
            foreach (var i in order.Items)
            {
                var desc = string.Join(" / ", new[] { i.Size, i.Color }.Where(x => !string.IsNullOrWhiteSpace(x)));
                items.Add(new
                {
                    name = i.ProductName.Length > 50 ? i.ProductName[..50] : i.ProductName,
                    amount = (int)Math.Round(i.UnitPrice * 100),
                    description = desc.Length > 0 ? desc : "Item",
                    quantity = i.Quantity
                });
            }
            items.Add(new { name = "Shipping", amount = (int)Math.Round(order.ShippingFee * 100), description = "Shipping", quantity = 1 });

            var parts = order.CustomerName.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            var first = parts.Length > 0 ? parts[0] : "Customer";
            var last = parts.Length > 1 ? parts[1] : "NA";

            var body = new
            {
                amount = (int)Math.Round(order.Total * 100),
                currency = "EGP",
                payment_methods = new[] { int.Parse(_config["Paymob:IntegrationId"]!) },
                items,
                billing_data = new
                {
                    first_name = first,
                    last_name = last,
                    email = order.Email,
                    phone_number = "+2" + order.Phone,
                    apartment = "NA",
                    floor = "NA",
                    building = "NA",
                    street = order.Address.Length > 100 ? order.Address[..100] : order.Address,
                    city = order.Governorate,
                    state = order.Governorate,
                    country = "EG"
                },
                // unique per attempt; the order id is the 2nd part
                special_reference = $"BORDO-{order.Id}-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}",
                notification_url = $"{baseUrl}/Payment/Webhook",
                redirection_url = $"{baseUrl}/Payment/Return"
            };

            var req = new HttpRequestMessage(HttpMethod.Post, "https://accept.paymob.com/v1/intention/")
            {
                Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Token", _config["Paymob:SecretKey"]);

            var res = await _http.SendAsync(req);
            var json = await res.Content.ReadAsStringAsync();
            if (!res.IsSuccessStatusCode)
            {
                _log.LogError("Paymob intention failed {Status}: {Body}", (int)res.StatusCode, json);
                return null;
            }

            using var doc = JsonDocument.Parse(json);
            var secret = doc.RootElement.GetProperty("client_secret").GetString();
            return $"https://accept.paymob.com/unifiedcheckout/?publicKey={Uri.EscapeDataString(_config["Paymob:PublicKey"]!)}&clientSecret={Uri.EscapeDataString(secret!)}";
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Paymob intention error for order {Id}", order.Id);
            return null;
        }
    }

    public bool VerifyHmac(IDictionary<string, string> values, string? receivedHmac)
    {
        var secret = _config["Paymob:HmacSecret"];
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(receivedHmac)) return false;

        var sb = new StringBuilder();
        foreach (var k in HmacKeys)
            sb.Append(values.TryGetValue(k, out var v) ? v : "");

        using var h = new HMACSHA512(Encoding.UTF8.GetBytes(secret));
        var calc = Convert.ToHexString(h.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()))).ToLowerInvariant();

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(calc),
            Encoding.UTF8.GetBytes(receivedHmac.ToLowerInvariant()));
    }
}