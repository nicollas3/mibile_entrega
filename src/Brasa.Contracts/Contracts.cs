using System.ComponentModel.DataAnnotations;
namespace Brasa.Contracts;

public record RegisterRequest([property: Required, EmailAddress] string Email, [property: Required, MinLength(12)] string Password, [property: Required, StringLength(100, MinimumLength = 2)] string Name, bool Courier = false);
public record LoginRequest([property: Required, EmailAddress] string Email, [property: Required] string Password);
public record RefreshRequest([property: Required] string RefreshToken);
public record ResetRequest([property: Required, EmailAddress] string Email, [property: Required] string Token, [property: Required, MinLength(12)] string Password);
public record ForgotRequest([property: Required, EmailAddress] string Email);
public record ProfileRequest([property: Required, StringLength(100, MinimumLength = 2)] string Name, [property: Phone] string? Phone);
public record UserDto(string Id, string Name, string Email, string? Phone, string[] Roles);
public record AuthDto(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt, UserDto User);
public record AddressInput([property: Required] string Label, [property: Required] string Street, [property: Required] string Number, [property: Required] string District, [property: Required] string City, [property: RegularExpression("^[A-Z]{2}$")] string State, [property: RegularExpression("^[0-9]{8}$")] string PostalCode, string Complement, string Reference, [property: Range(-90, 90)] double Latitude, [property: Range(-180, 180)] double Longitude);
public record AddressDto(Guid Id, string Label, string Formatted, double Latitude, double Longitude, AddressInput? Details = null);
public record RestaurantDto(Guid Id, string Name, string Description, string Category, string Address, string Hours, string Status, int PreparationMinutes, decimal DeliveryFee, decimal MinimumOrder, double? Rating, double Latitude, double Longitude, double RadiusKm, decimal CommissionRate);
public record RestaurantInput([property: Required, StringLength(150)] string Name, string Description, [property: Required] string Category, [property: Required] string Address, [property: Range(-90, 90)] double Latitude, [property: Range(-180, 180)] double Longitude, [property: Range(.1, 100)] double RadiusKm, string Hours, [property: Required] string Status, [property: Range(5, 180)] int PreparationMinutes, [property: Range(0, 100)] decimal DeliveryFee, [property: Range(0, 1000)] decimal MinimumOrder);
public record OptionDto(Guid Id, string Name, decimal Price);
public record ProductDto(Guid Id, Guid RestaurantId, string Name, string Description, string Category, string Allergens, string? ImageUrl, decimal Price, bool Available, List<OptionDto> Options);
public record OptionInput([property: Required] string Name, [property: Range(0, 1000)] decimal Price);
public record ProductInput([property: Required, StringLength(150)] string Name, string Description, [property: Required] string Category, string Allergens, string? ImageUrl, [property: Range(.01, 10000)] decimal Price, bool Available, List<OptionInput> Options);
public record CartLine(Guid ProductId, [property: Range(1, 50)] int Quantity, [property: StringLength(500)] string Notes, List<Guid> OptionIds);
public record CartInput(List<CartLine> Items);
public record ItemSnapshot(Guid ProductId, string Name, decimal UnitPrice, int Quantity, string Notes, List<OptionDto> Options);
public record TotalsDto(decimal Subtotal, decimal Discount, decimal DeliveryFee, decimal ServiceFee, decimal Total, decimal Commission, decimal RestaurantAmount, decimal CourierAmount);
public record CartDto(Guid? RestaurantId, List<ItemSnapshot> Items, TotalsDto? Totals);
public record CheckoutRequest(Guid AddressId, string? CouponCode, [property: Required] string PaymentMethod);
public record TransitionRequest([property: Required] string Status, Guid Version, [property: StringLength(500)] string? Reason);
public record ReasonRequest([property: Required, StringLength(500, MinimumLength = 3)] string Reason);
public record OrderDto(Guid Id, Guid RestaurantId, string RestaurantName, string Status, List<ItemSnapshot> Items, TotalsDto Prices, string Address, string? CourierId, string PaymentMethod, string RefundStatus, decimal Refunded, DateTimeOffset CreatedAt, Guid Version, bool HandoverConfirmed);
public record OrderEventDto(string Previous, string Next, string? Reason, DateTimeOffset At);
public record PageDto<T>(List<T> Items, int Page, int PageSize, int Total);
public record ReviewInput([property: Range(1, 5)] int RestaurantRating, [property: Range(1, 5)] int CourierRating, [property: StringLength(1000)] string Comment);
public record ReviewDto(int RestaurantRating, int CourierRating, string Comment);
public record TicketInput(Guid? OrderId, [property: Required] string Category, [property: Required, StringLength(2000, MinimumLength = 5)] string Description);
public record TicketDto(Guid Id, Guid? OrderId, string Category, string Description, string State, string? Resolution, DateTimeOffset At);
public record ResolveTicketInput([property: Required] string Resolution);
public record MemberInput([property: Required, EmailAddress] string Email, [property: Range(0, 31)] int Permissions);
public record MemberDto(string UserId, string Name, string Email, int Permissions);
public record StoreAccessDto(Guid RestaurantId, string Name, int Permissions);
public record AvailabilityInput(bool Available, string Vehicle = "Bicicleta");
public record CourierDto(string UserId, string Name, bool Approved, bool Available, string Vehicle);
public record OfferDto(Guid Id, Guid OrderId, string RestaurantName, string Origin, string ApproximateDestination, double ApproximateKm, decimal Amount, DateTimeOffset ExpiresAt);
public record DeliveryDto(Guid OrderId, string Stage, string RestaurantName, string Origin, double OriginLatitude, double OriginLongitude, string Destination, double DestinationLatitude, double DestinationLongitude, decimal Amount, Guid Version, double? Latitude, double? Longitude, DateTimeOffset? LocationAt, string OrderStatus, int? Rating = null);
public record DeliveryInput([property: Required] string Stage, Guid Version);
public record LocationInput([property: Range(-90, 90)] double Latitude, [property: Range(-180, 180)] double Longitude, DateTimeOffset At);
public record TrackingDto(string Stage, double? Latitude, double? Longitude, DateTimeOffset? At);
public record CouponInput([property: Required, RegularExpression("^[A-Z0-9_-]{3,30}$")] string Code, [property: Range(.01, 1000)] decimal Discount, [property: Range(0, 10000)] decimal MinimumSubtotal, DateTimeOffset ExpiresAt, [property: Range(1, 1000000)] int MaxUses, bool Active);
public record CouponDto(string Code, decimal Discount, decimal MinimumSubtotal, DateTimeOffset ExpiresAt, int MaxUses, int Uses, bool Active);
public record BlockInput(bool Blocked, [property: Required] string Reason);
public record ApprovalInput(bool Approved, [property: Required] string Reason);
public record AccountDto(string Id, string Name, string Email, bool Blocked, string[] Roles);
public record RoleInput([property: Required] string Role, [property: Required] string Reason);
public record RefundInput([property: Range(.01, 1000000)] decimal Amount, [property: Required] string Reason);
public record ReportDto(int Orders, int Active, int Cancelled, decimal GrossSales, decimal Refunds, decimal Commission, decimal RestaurantAmount, decimal CourierAmount, decimal ServiceFees);
public record AuditDto(long Id, string ActorId, string Action, string Resource, string Reason, DateTimeOffset At);
public record SettingsInput([property: Range(0, 100)] decimal ServiceFee, [property: Range(1, 120)] int RestaurantResponseMinutes, [property: Range(1, 240)] int CourierWaitMinutes, [property: Range(15, 300)] int OfferSeconds);
public record DevWebhook(string EventId, Guid OrderId, string Outcome);
public record CommissionRequest([property: Range(0, 1)] decimal Rate, [property: Required] string Reason);
public static class Presentation
{
    public static readonly System.Globalization.CultureInfo Brazil = System.Globalization.CultureInfo.GetCultureInfo("pt-BR");
    public static string Money(decimal value) => value.ToString("C", Brazil);
    public static string Status(string value) => value switch
    {
        "AwaitingPayment" => "Aguardando pagamento",
        "PaymentDeclined" => "Pagamento recusado",
        "SentToRestaurant" => "Enviado ao restaurante",
        "Accepted" => "Aceito",
        "Preparing" => "Em preparo",
        "Ready" => "Pronto para retirada",
        "CourierAssigned" => "Entregador atribuído",
        "InTransit" => "A caminho",
        "Delivered" => "Entregue",
        "Rejected" => "Recusado",
        "Cancelled" => "Cancelado",
        "Assigned" => "Atribuída",
        "GoingToRestaurant" => "Indo ao restaurante",
        "AtRestaurant" => "No restaurante",
        "PickedUp" => "Retirado",
        "GoingToCustomer" => "Indo ao cliente",
        "Problem" => "Problema na entrega",
        "Open" => "Aberta",
        "Closed" => "Fechada",
        "Paused" => "Pausada",
        "Unavailable" => "Indisponível",
        "None" => "Sem reembolso",
        "Pending" => "Pendente",
        "Processed" => "Processado",
        "Declined" => "Recusado",
        _ => value
    };
}
