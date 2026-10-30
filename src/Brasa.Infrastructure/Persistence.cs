using Brasa.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
namespace Brasa.Infrastructure;

public sealed class AppUser : IdentityUser
{
    public string DisplayName { get; set; } = "";
    public bool Blocked
    {
        get; set;
    }
    public string? BlockReason
    {
        get; set;
    }
}
public sealed class AuthSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserId { get; set; } = "";
    public DateTimeOffset? RevokedAt
    {
        get; set;
    }
    public DateTimeOffset ExpiresAt { get; set; } = DateTimeOffset.UtcNow.AddDays(30);
}
public sealed class RefreshToken
{
    public string Hash { get; set; } = "";
    public Guid SessionId
    {
        get; set;
    }
    public DateTimeOffset ExpiresAt { get; set; } = DateTimeOffset.UtcNow.AddDays(7);
    public DateTimeOffset? UsedAt
    {
        get; set;
    }
}
public sealed class OutboxMessage
{
    public long Id
    {
        get; set;
    }
    public Guid OrderId
    {
        get; set;
    }
    public string Status { get; set; } = "";
    public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? SentAt
    {
        get; set;
    }
}
public sealed class AppDb(DbContextOptions<AppDb> options) : IdentityDbContext<AppUser>(options)
{
    public DbSet<Restaurant> Restaurants => Set<Restaurant>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Membership> Memberships => Set<Membership>();
    public DbSet<CustomerAddress> Addresses => Set<CustomerAddress>();
    public DbSet<Cart> Carts => Set<Cart>();
    public DbSet<Coupon> Coupons => Set<Coupon>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderEvent> OrderEvents => Set<OrderEvent>();
    public DbSet<CourierProfile> Couriers => Set<CourierProfile>();
    public DbSet<Delivery> Deliveries => Set<Delivery>();
    public DbSet<DeliveryOffer> Offers => Set<DeliveryOffer>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<SupportTicket> Tickets => Set<SupportTicket>();
    public DbSet<AuditEntry> Audit => Set<AuditEntry>();
    public DbSet<PaymentEvent> PaymentEvents => Set<PaymentEvent>();
    public DbSet<AuthSession> Sessions => Set<AuthSession>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<OperationSettings> Settings => Set<OperationSettings>();
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.Entity<Cart>().HasKey(x => x.UserId);
        b.Entity<Cart>().Property(x => x.ItemsJson).HasColumnType("jsonb");
        b.Entity<Cart>().Property(x => x.Version).IsConcurrencyToken();
        b.Entity<Coupon>().HasKey(x => x.Code);
        b.Entity<CourierProfile>().HasKey(x => x.UserId);
        b.Entity<Delivery>().HasKey(x => x.OrderId);
        b.Entity<Review>().HasKey(x => x.OrderId);
        b.Entity<RefreshToken>().HasKey(x => x.Hash);
        b.Entity<PaymentEvent>().HasKey(x => x.Id);
        b.Entity<Order>().HasIndex(x => new { x.CustomerId, x.IdempotencyKey }).IsUnique();
        b.Entity<Order>().HasIndex(x => new { x.RestaurantId, x.CreatedAt });
        b.Entity<Order>().Property(x => x.ItemsSnapshot).HasColumnType("jsonb");
        b.Entity<Order>().Property(x => x.Version).IsConcurrencyToken();
        b.Entity<Order>().Ignore(x => x.Paid).Ignore(x => x.Terminal);
        b.Entity<Order>().OwnsOne(x => x.Prices);
        b.Entity<Order>().HasMany(x => x.Events).WithOne().HasForeignKey(x => x.OrderId);
        b.Entity<Restaurant>().HasMany(x => x.Products).WithOne().HasForeignKey(x => x.RestaurantId);
        b.Entity<Product>().HasMany(x => x.Options).WithOne().HasForeignKey(x => x.ProductId);
        b.Entity<Delivery>().Property(x => x.Version).IsConcurrencyToken();
        b.Entity<Delivery>().HasOne<Order>().WithOne().HasForeignKey<Delivery>(x => x.OrderId);
        b.Entity<Membership>().HasIndex(x => new { x.RestaurantId, x.UserId }).IsUnique();
        b.Entity<Membership>().HasOne<Restaurant>().WithMany().HasForeignKey(x => x.RestaurantId);
        b.Entity<Membership>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId);
        b.Entity<CourierProfile>().HasOne<AppUser>().WithOne().HasForeignKey<CourierProfile>(x => x.UserId);
        b.Entity<CustomerAddress>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId);
        b.Entity<AuthSession>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId);
        b.Entity<RefreshToken>().HasOne<AuthSession>().WithMany().HasForeignKey(x => x.SessionId);
        b.Entity<DeliveryOffer>().HasIndex(x => new { x.OrderId, x.CourierId }).IsUnique();
        b.Entity<OutboxMessage>().HasIndex(x => x.SentAt);
        foreach (var entity in b.Model.GetEntityTypes())
            foreach (var property in entity.GetProperties().Where(p => p.ClrType == typeof(decimal)))
            {
                property.SetPrecision(14);
                property.SetScale(2);
            }
        b.Entity<Restaurant>().Property(x => x.CommissionRate).HasPrecision(5, 4);
    }
    public Task Lock(string key, CancellationToken ct = default) => Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", ct);
    public void AuditAction(string actor, string action, string resource, string reason = "") => Audit.Add(new() { ActorId = actor, Action = action, Resource = resource, Reason = reason });
    public void Notify(Order order) => Outbox.Add(new() { OrderId = order.Id, Status = order.Status.ToString() });
}
