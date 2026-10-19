using System.Net;
using System.Text;
using HodoomBrand.Models;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace HodoomBrand.Services;

public interface IOrderEmailService
{
    Task SendOrderEmailsAsync(Order order);
    Task SendStatusEmailAsync(Order order);
    Task SendCustomerCancelledAsync(Order order);
}

public class OrderEmailService : IOrderEmailService
{
    private readonly EmailSettings _settings;
    private readonly IConfiguration _config;
    private readonly ILogger<OrderEmailService> _log;

    public OrderEmailService(IOptions<EmailSettings> settings, IConfiguration config, ILogger<OrderEmailService> log)
    {
        _settings = settings.Value;
        _config = config;
        _log = log;
    }

    private bool EmailConfigured =>
        !string.IsNullOrWhiteSpace(_settings.Username) && !string.IsNullOrWhiteSpace(_settings.AppPassword);

    public async Task SendCustomerCancelledAsync(Order order)
    {
        if (!EmailConfigured) return;

        var adminEmail = _config["Admin:Email"];
        if (string.IsNullOrWhiteSpace(adminEmail)) return;

        try
        {
            var html =
                "<div style='font-family:Arial,sans-serif;max-width:600px;margin:auto;color:#2B2B2B'>" +
                $"<h2 style='color:#6D0F23'>Order #{order.Id} was cancelled by the customer</h2>" +
                $"<p>{H(order.CustomerName)}<br>{H(order.Phone)}<br>{H(order.Email)}</p>" +
                $"<p><b>Total:</b> {order.Total:0.##} EGP. The stock has been returned automatically.</p>" +
                "</div>";

            var msg = Create(adminEmail, "Admin", $"Order #{order.Id} cancelled by customer", html, order.Email);
            await SendAsync(msg);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to send cancel notice for order {OrderId}", order.Id);
        }
    }

    public async Task SendStatusEmailAsync(Order order)
    {
        if (!EmailConfigured) return;

        string? text = order.Status switch
        {
            OrderStatus.Confirmed => "Your order has been confirmed and is being prepared.",
            OrderStatus.Shipped => "Good news! Your order is on its way to you.",
            OrderStatus.Delivered => "Your order has been delivered. We hope you love it!",
            OrderStatus.Cancelled => order.CancelledByCustomer
                ? "Your order has been cancelled as you requested."
                : "Your order has been cancelled. If this is a surprise, please contact us.",
            _ => null
        };
        if (text == null) return;

        try
        {
            var storeName = _config["Store:Name"] ?? "BORDO";
            var payText = order.PaymentMethod == PaymentMethod.Online ? "paid online" : "cash on delivery";
            var html =
                "<div style='font-family:Arial,sans-serif;max-width:600px;margin:auto;color:#2B2B2B'>" +
                $"<div style='background:#6D0F23;color:#C9A24B;padding:18px;text-align:center;font-size:24px;letter-spacing:3px'>{H(storeName)}</div>" +
                "<div style='padding:20px;background:#F7F1EA'>" +
                $"<h2 style='color:#6D0F23'>Order #{order.Id}: {order.Status}</h2>" +
                $"<p>Hi {H(order.CustomerName)},</p><p>{text}</p>" +
                $"<p><b>Total:</b> {order.Total:0.##} EGP ({payText})</p>" +
                "</div></div>";

            var msg = Create(order.Email, order.CustomerName,
                $"{storeName} - Order #{order.Id} {order.Status}", html, null);
            await SendAsync(msg);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to send status email for order {OrderId}", order.Id);
        }
    }

    public async Task SendOrderEmailsAsync(Order order)
    {
        if (!EmailConfigured)
        {
            _log.LogWarning("Email is not configured (Email:Username / Email:AppPassword). Skipping order emails.");
            return;
        }

        try
        {
            var storeName = _config["Store:Name"] ?? "BORDO";
            var messages = new List<MimeMessage>();

            // 1) Customer
            messages.Add(Create(order.Email, order.CustomerName,
                $"{storeName} - Order #{order.Id} confirmed",
                BuildHtml(order, storeName, forAdmin: false), null));

            // 2) Admin
            var adminEmail = _config["Admin:Email"];
            if (!string.IsNullOrWhiteSpace(adminEmail))
            {
                messages.Add(Create(adminEmail, "Admin",
                    $"New order #{order.Id} - {order.Total:0.##} EGP",
                    BuildHtml(order, storeName, forAdmin: true), order.Email));
            }

            await SendAsync(messages.ToArray());
        }
        catch (Exception ex)
        {
            // Never break the order because of an email problem
            _log.LogError(ex, "Failed to send emails for order {OrderId}", order.Id);
        }
    }

    private async Task SendAsync(params MimeMessage[] messages)
    {
        using var client = new SmtpClient();
        await client.ConnectAsync(_settings.Host, _settings.Port, SecureSocketOptions.StartTls);
        await client.AuthenticateAsync(_settings.Username, _settings.AppPassword);
        foreach (var m in messages) await client.SendAsync(m);
        await client.DisconnectAsync(true);
    }

    private MimeMessage Create(string toEmail, string toName, string subject, string html, string? replyTo)
    {
        var m = new MimeMessage();
        m.From.Add(new MailboxAddress(_settings.FromName, _settings.Username));
        m.To.Add(new MailboxAddress(toName, toEmail));
        if (!string.IsNullOrWhiteSpace(replyTo)) m.ReplyTo.Add(MailboxAddress.Parse(replyTo));
        m.Subject = subject;
        m.Body = new BodyBuilder { HtmlBody = html }.ToMessageBody();
        return m;
    }

    private static string H(string? s) => WebUtility.HtmlEncode(s ?? "");

    private static string BuildHtml(Order o, string storeName, bool forAdmin)
    {
        var sb = new StringBuilder();
        sb.Append("<div style='font-family:Arial,sans-serif;max-width:600px;margin:auto;color:#2B2B2B'>");
        sb.Append($"<div style='background:#6D0F23;color:#C9A24B;padding:18px;text-align:center;font-size:24px;letter-spacing:3px'>{H(storeName)}</div>");
        sb.Append("<div style='padding:20px;background:#F7F1EA'>");

        if (forAdmin)
            sb.Append($"<h2 style='color:#6D0F23'>New order #{o.Id}</h2>");
        else
            sb.Append($"<h2 style='color:#6D0F23'>Thank you, {H(o.CustomerName)}!</h2><p>Your order <b>#{o.Id}</b> has been received. We will contact you soon to confirm delivery.</p>");

        sb.Append("<table style='width:100%;border-collapse:collapse;background:#fff'>");
        sb.Append("<tr style='background:#eadfd3'><th align='left' style='padding:8px'>Item</th><th style='padding:8px'>Qty</th><th align='right' style='padding:8px'>Total</th></tr>");
        foreach (var i in o.Items)
        {
            var opts = string.Join(" / ", new[] { i.Size, i.Color }.Where(x => !string.IsNullOrWhiteSpace(x)));
            sb.Append("<tr>");
            sb.Append($"<td style='padding:8px;border-bottom:1px solid #eee'>{H(i.ProductName)}" +
                      (opts.Length > 0 ? $"<br><small style='color:#7A6F6A'>{H(opts)}</small>" : "") + "</td>");
            sb.Append($"<td align='center' style='padding:8px;border-bottom:1px solid #eee'>{i.Quantity}</td>");
            sb.Append($"<td align='right' style='padding:8px;border-bottom:1px solid #eee'>{(i.UnitPrice * i.Quantity):0.##} EGP</td>");
            sb.Append("</tr>");
        }
        sb.Append("</table>");

        sb.Append("<p style='text-align:right'>");
        sb.Append($"Subtotal: {o.Subtotal:0.##} EGP<br>Shipping: {o.ShippingFee:0.##} EGP<br>");
        sb.Append($"<b style='font-size:18px;color:#6D0F23'>Total: {o.Total:0.##} EGP</b></p>");
        sb.Append($"<p><b>Payment:</b> {(o.PaymentMethod == PaymentMethod.Online ? "Paid online" : "Cash on delivery")}</p>");

        sb.Append("<h3 style='color:#6D0F23'>Delivery details</h3>");
        sb.Append($"<p>{H(o.CustomerName)}<br>{H(o.Phone)}<br>{H(o.Address)}, {H(o.Governorate)}");
        if (forAdmin) sb.Append($"<br>{H(o.Email)}");
        sb.Append("</p>");
        if (!string.IsNullOrWhiteSpace(o.Notes)) sb.Append($"<p><b>Notes:</b> {H(o.Notes)}</p>");

        sb.Append("</div></div>");
        return sb.ToString();
    }
}