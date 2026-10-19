using HodoomBrand.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace HodoomBrand.Data;

public class ApplicationDbContext : IdentityDbContext<IdentityUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options) { }

    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductMedia> ProductMedia => Set<ProductMedia>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<Product>().Property(p => p.Price).HasPrecision(18, 2);
        builder.Entity<Product>().Property(p => p.DiscountPrice).HasPrecision(18, 2);
        builder.Entity<Order>().Property(o => o.Subtotal).HasPrecision(18, 2);
        builder.Entity<Order>().Property(o => o.ShippingFee).HasPrecision(18, 2);
        builder.Entity<Order>().Property(o => o.Total).HasPrecision(18, 2);
        builder.Entity<OrderItem>().Property(i => i.UnitPrice).HasPrecision(18, 2);
    }
}