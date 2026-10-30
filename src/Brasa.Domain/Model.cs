namespace Brasa.Domain;

public sealed class RuleException(string code, string message, int status = 422) : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
}
public static class Roles
{
    public const string Customer = "Cliente", Courier = "Entregador", Owner = "Restaurante", Employee = "Funcionario", Admin = "Administrador", Support = "Suporte";
    public static readonly string[] All = [Customer, Courier, Owner, Employee, Admin, Support];
}
[Flags]
public enum StorePermission
{
    None = 0, Orders = 1, Menu = 2, Settings = 4, Reports = 8, Staff = 16, All = 31
}
public enum StoreStatus
{
    Open, Closed, Paused, Unavailable
}
public enum OrderStatus
{
    AwaitingPayment, PaymentDeclined, SentToRestaurant, Accepted, Preparing, Ready, CourierAssigned, InTransit, Delivered, Rejected, Cancelled
}
public enum DeliveryStage
{
    Assigned, GoingToRestaurant, AtRestaurant, PickedUp, GoingToCustomer, Delivered, Problem
}
public enum RefundStatus
{
    None, Pending, Processed, Declined
}
public sealed class Restaurant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Category { get; set; } = "";
    public string Address { get; set; } = "";
    public double Latitude
    {
        get; set;
    }
    public double Longitude
    {
        get; set;
    }
    public double RadiusKm { get; set; } = 8;
    public string Hours { get; set; } = "Todos os dias, 11h–23h";
    public StoreStatus Status { get; set; } = StoreStatus.Open;
    public int PreparationMinutes { get; set; } = 30;
    public decimal DeliveryFee { get; set; } = 7;
    public decimal MinimumOrder { get; set; } = 15;
    public decimal CommissionRate { get; set; } = .12m;
    public List<Product> Products { get; set; } = [];
}
public sealed class Membership
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RestaurantId
    {
        get; set;
    }
    public string UserId { get; set; } = "";
    public StorePermission Permissions
    {
        get; set;
    }
}
public sealed class Product
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RestaurantId
    {
        get; set;
    }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Category { get; set; } = "";
    public string Allergens { get; set; } = "";
    public string? ImageUrl
    {
        get; set;
    }
    public decimal Price
    {
        get; set;
    }
    public bool Available { get; set; } = true;
    public List<ProductOption> Options { get; set; } = [];
}
public sealed class ProductOption
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProductId
    {
        get; set;
    }
    public string Name { get; set; } = "";
    public decimal Price
    {
        get; set;
    }
}
public sealed class CustomerAddress
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserId { get; set; } = "";
    public string Label { get; set; } = "Casa";
    public string Street { get; set; } = "";
    public string Number { get; set; } = "";
    public string District { get; set; } = "";
    public string City { get; set; } = "";
    public string State { get; set; } = "";
    public string PostalCode { get; set; } = "";
    public string Complement { get; set; } = "";
    public string Reference { get; set; } = "";
    public double Latitude
    {
        get; set;
    }
    public double Longitude
    {
        get; set;
    }
    public override string ToString() => $"{Street}, {Number}, {Complement} — {District}, {City}/{State}, CEP {PostalCode}. Referência: {Reference}";
}
public sealed class Coupon
{
    public string Code { get; set; } = "";
    public decimal Discount
    {
        get; set;
    }
    public decimal MinimumSubtotal
    {
        get; set;
    }
    public DateTimeOffset ExpiresAt
    {
        get; set;
    }
    public int MaxUses { get; set; } = 100;
    public int Uses
    {
        get; set;
    }
    public bool Active { get; set; } = true;
}
public sealed class Cart
{
    public string UserId { get; set; } = "";
    public Guid? RestaurantId
    {
        get; set;
    }
    public string ItemsJson { get; set; } = "[]";
    public Guid Version { get; set; } = Guid.NewGuid();
}
public sealed record PriceBreakdown(decimal Subtotal, decimal Discount, decimal DeliveryFee, decimal ServiceFee, decimal Total, decimal Commission, decimal RestaurantAmount, decimal CourierAmount)
{
    public static decimal Money(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
    public static PriceBreakdown Calculate(decimal subtotal, decimal discount, decimal delivery, decimal service, decimal commissionRate)
    {
        if (subtotal <= 0 || discount < 0 || discount > subtotal || delivery < 0 || service < 0 || commissionRate is < 0 or > 1)
            throw new RuleException("invalid_money", "Valores do pedido inválidos.");
        var net = Money(subtotal - discount);
        var commission = Money(net * commissionRate);
        return new(Money(subtotal), Money(discount), Money(delivery), Money(service), Money(net + delivery + service), commission, net - commission, Money(delivery));
    }
}
public sealed class Order
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string CustomerId { get; set; } = "";
    public Guid RestaurantId
    {
        get; set;
    }
    public string RestaurantName { get; set; } = "";
    public string? CourierId
    {
        get; set;
    }
    public bool HandoverConfirmed
    {
        get; set;
    }
    public string AddressSnapshot { get; set; } = "";
    public double Latitude
    {
        get; set;
    }
    public double Longitude
    {
        get; set;
    }
    public string ItemsSnapshot { get; set; } = "[]";
    public string IdempotencyKey { get; set; } = "";
    public string RequestHash { get; set; } = "";
    public OrderStatus Status { get; private set; } = OrderStatus.AwaitingPayment;
    public PriceBreakdown Prices { get; set; } = new(0, 0, 0, 0, 0, 0, 0, 0);
    public string? CouponCode
    {
        get; set;
    }
    public string PaymentReference { get; set; } = "";
    public string PaymentMethod { get; set; } = "";
    public RefundStatus RefundStatus
    {
        get; set;
    }
    public decimal Refunded
    {
        get; set;
    }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public Guid Version { get; set; } = Guid.NewGuid();
    public List<OrderEvent> Events { get; set; } = [];
    public bool Paid => Status != OrderStatus.AwaitingPayment && Status != OrderStatus.PaymentDeclined && PaymentReference.Length > 0;
    public bool Terminal => Status is OrderStatus.Cancelled or OrderStatus.Rejected or OrderStatus.Delivered or OrderStatus.PaymentDeclined;
    public void Transition(OrderStatus next, string actor, string? reason = null)
    {
        bool valid = (Status, next) switch
        {
            (OrderStatus.AwaitingPayment, OrderStatus.SentToRestaurant or OrderStatus.PaymentDeclined or OrderStatus.Cancelled) => true,
            (OrderStatus.SentToRestaurant, OrderStatus.Accepted or OrderStatus.Rejected or OrderStatus.Cancelled) => true,
            (OrderStatus.Accepted, OrderStatus.Preparing or OrderStatus.Cancelled) => true,
            (OrderStatus.Preparing, OrderStatus.Ready or OrderStatus.Cancelled) => true,
            (OrderStatus.Ready, OrderStatus.CourierAssigned or OrderStatus.Cancelled) => true,
            (OrderStatus.CourierAssigned, OrderStatus.InTransit or OrderStatus.Cancelled) => true,
            (OrderStatus.InTransit, OrderStatus.Delivered or OrderStatus.Cancelled) => true,
            _ => false
        };
        if (!valid)
            throw new RuleException("invalid_transition", $"Não é possível alterar {Status} para {next}.", 409);
        if ((next is OrderStatus.Cancelled or OrderStatus.Rejected) && string.IsNullOrWhiteSpace(reason))
            throw new RuleException("reason_required", "Informe o motivo.");
        Events.Add(new()
        {
            OrderId = Id,
            ActorId = actor,
            Previous = Status.ToString(),
            Next = next.ToString(),
            Reason = reason
        });
        Status = next;
        UpdatedAt = DateTimeOffset.UtcNow;
        Version = Guid.NewGuid();
    }
}
public sealed class OrderEvent
{
    public long Id
    {
        get; set;
    }
    public Guid OrderId
    {
        get; set;
    }
    public string ActorId { get; set; } = "";
    public string Previous { get; set; } = "";
    public string Next { get; set; } = "";
    public string? Reason
    {
        get; set;
    }
    public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;
}
public sealed class CourierProfile
{
    public string UserId { get; set; } = "";
    public bool Approved
    {
        get; set;
    }
    public bool Available
    {
        get; set;
    }
    public string Vehicle { get; set; } = "Bicicleta";
}
public sealed class Delivery
{
    public Guid OrderId
    {
        get; set;
    }
    public string CourierId { get; set; } = "";
    public DeliveryStage Stage
    {
        get; private set;
    }
    public double? Latitude
    {
        get; set;
    }
    public double? Longitude
    {
        get; set;
    }
    public DateTimeOffset? LocationAt
    {
        get; set;
    }
    public Guid Version { get; set; } = Guid.NewGuid();
    public void Advance(DeliveryStage next)
    {
        if (Stage == DeliveryStage.Delivered || next == Stage || (next != DeliveryStage.Problem && (int)next != (int)Stage + 1))
            throw new RuleException("delivery_transition", "Etapa de entrega inválida.", 409);
        Stage = next;
        Version = Guid.NewGuid();
    }
}
public sealed class DeliveryOffer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId
    {
        get; set;
    }
    public string CourierId { get; set; } = "";
    public DateTimeOffset ExpiresAt
    {
        get; set;
    }
    public string State { get; set; } = "Pending";
}
public sealed class Review
{
    public Guid OrderId
    {
        get; set;
    }
    public Guid RestaurantId
    {
        get; set;
    }
    public string CustomerId { get; set; } = "";
    public int RestaurantRating
    {
        get; set;
    }
    public int CourierRating
    {
        get; set;
    }
    public string Comment { get; set; } = "";
}
public sealed class SupportTicket
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? OrderId
    {
        get; set;
    }
    public string UserId { get; set; } = "";
    public string Category { get; set; } = "";
    public string Description { get; set; } = "";
    public string State { get; set; } = "Open";
    public string? Resolution
    {
        get; set;
    }
    public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;
}
public sealed class AuditEntry
{
    public long Id
    {
        get; set;
    }
    public string ActorId { get; set; } = "";
    public string Action { get; set; } = "";
    public string Resource { get; set; } = "";
    public string Reason { get; set; } = "";
    public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;
}
public sealed class PaymentEvent
{
    public string Id { get; set; } = "";
    public Guid OrderId
    {
        get; set;
    }
    public string Kind { get; set; } = "";
    public string Result { get; set; } = "";
    public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;
}
public sealed class OperationSettings
{
    public int Id { get; set; } = 1;
    public decimal ServiceFee { get; set; } = 2.5m;
    public int RestaurantResponseMinutes { get; set; } = 10;
    public int CourierWaitMinutes { get; set; } = 30;
    public int OfferSeconds { get; set; } = 90;
}
