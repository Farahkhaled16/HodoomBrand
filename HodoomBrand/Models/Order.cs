namespace HodoomBrand.Models;

public enum PaymentMethod { CashOnDelivery = 0, Online = 1 }
public enum PaymentStatus { Pending = 0, Paid = 1, Failed = 2 }
public enum OrderStatus { New = 0, Confirmed = 1, Shipped = 2, Delivered = 3, Cancelled = 4 }

public class Order
{
    public int Id { get; set; }
    public string? UserId { get; set; }   // null = Guest

    public string CustomerName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Governorate { get; set; } = "";
    public string Address { get; set; } = "";
    public string? Notes { get; set; }

    public decimal Subtotal { get; set; }
    public decimal ShippingFee { get; set; }
    public decimal Total { get; set; }

    public PaymentMethod PaymentMethod { get; set; }
    public PaymentStatus PaymentStatus { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.New;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ConfirmedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public bool CancelledByCustomer { get; set; }

    public string? PaymobTransactionId { get; set; }

    public List<OrderItem> Items { get; set; } = new();
}

public class OrderItem
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public Order? Order { get; set; }

    public int ProductId { get; set; }
    public string ProductName { get; set; } = "";
    public string? Size { get; set; }
    public string? Color { get; set; }
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
}