using Brasa.Contracts;
using Brasa.Domain;
using Microsoft.EntityFrameworkCore;
namespace Brasa.Infrastructure;

public sealed record Actor(string Id, string[] Roles)
{
    public bool Is(string role) => Roles.Contains(role);
    public void Require(params string[] roles)
    {
        if (!roles.Any(Is))
            throw new RuleException("forbidden", "Acesso não permitido.", 403);
    }
}
public sealed class Access(AppDb db)
{
    public async Task Store(Actor actor, Guid restaurant, StorePermission permission, CancellationToken ct = default)
    {
        if (actor.Is(Roles.Admin))
            return;
        var member = await db.Memberships.SingleOrDefaultAsync(x => x.UserId == actor.Id && x.RestaurantId == restaurant, ct);
        if (member == null || (member.Permissions & permission) != permission)
            throw new RuleException("store_forbidden", "Você não tem permissão neste restaurante.", 403);
    }
    public async Task ReadOrder(Actor actor, Order order, CancellationToken ct = default)
    {
        if (actor.Is(Roles.Admin) || actor.Is(Roles.Support) || order.CustomerId == actor.Id || order.CourierId == actor.Id)
            return;
        await Store(actor, order.RestaurantId, StorePermission.Orders, ct);
    }
}
public static class Dto
{
    public static AddressDto Address(CustomerAddress a) => new(a.Id, a.Label, a.ToString(), a.Latitude, a.Longitude, new(a.Label, a.Street, a.Number, a.District, a.City, a.State, a.PostalCode, a.Complement, a.Reference, a.Latitude, a.Longitude));
    public static List<T> Read<T>(string json) => System.Text.Json.JsonSerializer.Deserialize<List<T>>(json) ?? [];
    public static string Json<T>(T value) => System.Text.Json.JsonSerializer.Serialize(value);
    public static TotalsDto Totals(PriceBreakdown p) => new(p.Subtotal, p.Discount, p.DeliveryFee, p.ServiceFee, p.Total, p.Commission, p.RestaurantAmount, p.CourierAmount);
    public static OrderDto Order(Order o) => new(o.Id, o.RestaurantId, o.RestaurantName, o.Status.ToString(), Read<ItemSnapshot>(o.ItemsSnapshot), Totals(o.Prices), o.AddressSnapshot, o.CourierId, o.PaymentMethod, o.RefundStatus.ToString(), o.Refunded, o.CreatedAt, o.Version, o.HandoverConfirmed);
    public static ProductDto Product(Product p) => new(p.Id, p.RestaurantId, p.Name, p.Description, p.Category, p.Allergens, p.ImageUrl, p.Price, p.Available, p.Options.Select(x => new OptionDto(x.Id, x.Name, x.Price)).ToList());
    public static RestaurantDto Restaurant(Restaurant r, double? rating = null) => new(r.Id, r.Name, r.Description, r.Category, r.Address, r.Hours, r.Status.ToString(), r.PreparationMinutes, r.DeliveryFee, r.MinimumOrder, rating, r.Latitude, r.Longitude, r.RadiusKm, r.CommissionRate);
    public static TicketDto Ticket(SupportTicket t) => new(t.Id, t.OrderId, t.Category, t.Description, t.State, t.Resolution, t.At);
    public static CouponDto Coupon(Coupon c) => new(c.Code, c.Discount, c.MinimumSubtotal, c.ExpiresAt, c.MaxUses, c.Uses, c.Active);
}
