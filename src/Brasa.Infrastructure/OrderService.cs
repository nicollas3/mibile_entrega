using System.Security.Cryptography;
using System.Text;
using Brasa.Contracts;
using Brasa.Domain;
using Microsoft.EntityFrameworkCore;
namespace Brasa.Infrastructure;

public sealed class OrderService(AppDb db, CartService carts, Access access, IPaymentProvider payments, IMapsService maps)
{
    public async Task<OrderDto> Checkout(Actor actor, CheckoutRequest input, string key, CancellationToken ct)
    {
        actor.Require(Roles.Customer);
        if (string.IsNullOrWhiteSpace(key) || key.Length > 80 || key.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_'))
            throw new RuleException("idempotency_key", "Envie Idempotency-Key (1 a 80 letras, números, - ou _).");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Dto.Json(input))));
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Lock("customer:" + actor.Id, ct);
        var previous = await db.Orders.SingleOrDefaultAsync(x => x.CustomerId == actor.Id && x.IdempotencyKey == key, ct);
        if (previous != null)
        {
            if (previous.RequestHash != hash)
                throw new RuleException("idempotency_conflict", "A chave já foi usada com outros dados.", 409);
            return Dto.Order(previous);
        }
        var address = await db.Addresses.SingleOrDefaultAsync(x => x.Id == input.AddressId && x.UserId == actor.Id, ct) ?? throw new RuleException("address", "Endereço não encontrado.", 404);
        var cart = await db.Carts.SingleOrDefaultAsync(x => x.UserId == actor.Id, ct) ?? throw new RuleException("empty_cart", "O carrinho está vazio.");
        if (cart.RestaurantId == null)
            throw new RuleException("empty_cart", "O carrinho está vazio.");
        await db.Lock("store:" + cart.RestaurantId, ct);
        var (store, items) = await carts.Validate(Dto.Read<CartLine>(cart.ItemsJson), ct);
        if (store == null || items.Count == 0)
            throw new RuleException("empty_cart", "O carrinho está vazio.");
        if (maps.ApproximateDistanceKm(store.Latitude, store.Longitude, address.Latitude, address.Longitude) > store.RadiusKm)
            throw new RuleException("outside_area", "Endereço fora da área de atendimento aproximada.");
        var code = input.CouponCode?.ToUpperInvariant().Trim();
        if (!string.IsNullOrEmpty(code))
            await db.Lock("coupon:" + code, ct);
        var prices = await carts.Price(store, items, code, ct);
        if (prices.Subtotal < store.MinimumOrder)
            throw new RuleException("minimum_order", $"O pedido mínimo é R$ {store.MinimumOrder:F2}.");
        var order = new Order { CustomerId = actor.Id, RestaurantId = store.Id, RestaurantName = store.Name, AddressSnapshot = address.ToString(), Latitude = address.Latitude, Longitude = address.Longitude, ItemsSnapshot = Dto.Json(items), Prices = prices, IdempotencyKey = key, RequestHash = hash, CouponCode = code, PaymentMethod = input.PaymentMethod };
        order.Events.Add(new()
        {
            OrderId = order.Id,
            ActorId = actor.Id,
            Previous = "",
            Next = OrderStatus.AwaitingPayment.ToString()
        });
        var result = await payments.Charge(order.Id, prices.Total, input.PaymentMethod, ct);
        if (input.PaymentMethod != "demo-pending")
        {
            order.PaymentReference = result.Approved ? result.Reference : "";
            order.Transition(result.Approved ? OrderStatus.SentToRestaurant : OrderStatus.PaymentDeclined, "payment:development");
        }
        db.PaymentEvents.Add(new()
        {
            Id = "charge:" + order.Id,
            OrderId = order.Id,
            Kind = "development-only",
            Result = input.PaymentMethod == "demo-pending" ? "Pending" : result.Approved ? "Approved" : "Declined"
        });
        if (!string.IsNullOrEmpty(code) && order.Status != OrderStatus.PaymentDeclined)
            (await db.Coupons.SingleAsync(x => x.Code == code, ct)).Uses++;
        db.Orders.Add(order);
        db.Notify(order);
        cart.ItemsJson = "[]";
        cart.RestaurantId = null;
        cart.Version = Guid.NewGuid();
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Dto.Order(order);
    }
    public async Task<Order> Find(Guid id, CancellationToken ct) => await db.Orders.Include(x => x.Events).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new RuleException("not_found", "Pedido não encontrado.", 404);
    public async Task<OrderDto> ByKey(Actor actor, string key, CancellationToken ct)
    {
        actor.Require(Roles.Customer);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Lock("customer:" + actor.Id, ct);
        var o = await db.Orders.AsNoTracking().SingleOrDefaultAsync(x => x.CustomerId == actor.Id && x.IdempotencyKey == key, ct) ?? throw new RuleException("not_found", "Nenhum pedido confirmado nesta tentativa.", 404);
        await tx.CommitAsync(ct);
        return Dto.Order(o);
    }
    public async Task<OrderDto> Get(Actor actor, Guid id, CancellationToken ct)
    {
        var order = await Find(id, ct);
        await access.ReadOrder(actor, order, ct);
        return Dto.Order(order);
    }
    public async Task<PageDto<OrderDto>> List(Actor actor, Guid? store, int page, int size, CancellationToken ct)
    {
        var query = db.Orders.AsNoTracking().AsQueryable();
        if (store.HasValue)
        {
            await access.Store(actor, store.Value, StorePermission.Orders, ct);
            query = query.Where(x => x.RestaurantId == store);
        }
        else if (!actor.Is(Roles.Admin) && !actor.Is(Roles.Support))
            query = query.Where(x => x.CustomerId == actor.Id || x.CourierId == actor.Id);
        page = Math.Max(1, page);
        size = Math.Clamp(size, 1, 100);
        var total = await query.CountAsync(ct);
        var orders = await query.OrderByDescending(x => x.CreatedAt).Skip((page - 1) * size).Take(size).ToListAsync(ct);
        return new(orders.Select(Dto.Order).ToList(), page, size, total);
    }
    public async Task<OrderDto> Transition(Actor actor, Guid id, TransitionRequest input, CancellationToken ct)
    {
        if (!Enum.TryParse<OrderStatus>(input.Status, out var next) || next is not (OrderStatus.Accepted or OrderStatus.Rejected or OrderStatus.Preparing or OrderStatus.Ready))
            throw new RuleException("store_transition", "Etapa do restaurante inválida.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Lock("order:" + id, ct);
        var order = await Find(id, ct);
        await access.Store(actor, order.RestaurantId, StorePermission.Orders, ct);
        if (order.Version != input.Version)
            throw new RuleException("stale_order", "O pedido mudou. Recarregue os dados.", 409);
        order.Transition(next, actor.Id, input.Reason);
        if (next == OrderStatus.Rejected && order.Paid)
            order.RefundStatus = RefundStatus.Pending;
        db.AuditAction(actor.Id, "order.transition", id.ToString(), input.Reason ?? "");
        db.Notify(order);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Dto.Order(order);
    }
    public async Task<OrderDto> Cancel(Actor actor, Guid id, string reason, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Lock("order:" + id, ct);
        var o = await Find(id, ct);
        if (actor.Is(Roles.Admin))
        {
        }
        else if (o.CustomerId == actor.Id)
        {
            if (o.Status is not (OrderStatus.AwaitingPayment or OrderStatus.SentToRestaurant or OrderStatus.Accepted))
                throw new RuleException("support_required", "O preparo já começou. Abra uma solicitação de suporte para analisar o cancelamento.", 409);
        }
        else
            throw new RuleException("forbidden", "Apenas o cliente ou a administração pode cancelar.", 403);
        var paid = o.Paid;
        o.Transition(OrderStatus.Cancelled, actor.Id, reason);
        if (paid)
            o.RefundStatus = RefundStatus.Pending;
        await db.Offers.Where(x => x.OrderId == id && x.State == "Pending").ExecuteUpdateAsync(s => s.SetProperty(x => x.State, "Cancelled"), ct);
        await db.Deliveries.Where(x => x.OrderId == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Latitude, (double?)null).SetProperty(x => x.Longitude, (double?)null).SetProperty(x => x.LocationAt, (DateTimeOffset?)null), ct);
        db.AuditAction(actor.Id, "order.cancel", id.ToString(), reason);
        db.Notify(o);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Dto.Order(o);
    }
    public async Task<OrderDto> Handover(Actor actor, Guid id, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Lock("order:" + id, ct);
        var o = await Find(id, ct);
        await access.Store(actor, o.RestaurantId, StorePermission.Orders, ct);
        var delivery = await db.Deliveries.SingleOrDefaultAsync(x => x.OrderId == id, ct);
        if (o.Status != OrderStatus.CourierAssigned || o.HandoverConfirmed || delivery?.Stage != DeliveryStage.AtRestaurant)
            throw new RuleException("handover", "A retirada exige entregador presente na loja e ainda não pode estar confirmada.", 409);
        o.HandoverConfirmed = true;
        o.Version = Guid.NewGuid();
        o.Events.Add(new()
        {
            OrderId = id,
            ActorId = actor.Id,
            Previous = "Retirada pendente",
            Next = "Retirada confirmada pela loja"
        });
        db.AuditAction(actor.Id, "order.handover", id.ToString());
        db.Notify(o);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Dto.Order(o);
    }
    public async Task<OrderDto> Refund(Actor actor, Guid id, RefundInput input, string key, CancellationToken ct)
    {
        actor.Require(Roles.Admin);
        if (string.IsNullOrWhiteSpace(key) || key.Length > 80)
            throw new RuleException("idempotency_key", "Informe uma chave de idempotência para o reembolso.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Lock("order:" + id, ct);
        var order = await Find(id, ct);
        var eventId = "refund:" + id + ":" + key;
        var old = await db.PaymentEvents.SingleOrDefaultAsync(x => x.Id == eventId, ct);
        var fingerprint = Dto.Json(input);
        if (old != null)
        {
            if (old.Result != fingerprint)
                throw new RuleException("idempotency_conflict", "Reembolso repetido com valores diferentes.", 409);
            return Dto.Order(order);
        }
        if (!order.Paid || order.Refunded + input.Amount > order.Prices.Total || input.Amount != PriceBreakdown.Money(input.Amount))
            throw new RuleException("refund_amount", "Valor de reembolso inválido.");
        order.RefundStatus = RefundStatus.Pending;
        await db.SaveChangesAsync(ct);
        var ok = await payments.Refund(order.Id, input.Amount, eventId, ct);
        order.RefundStatus = ok ? RefundStatus.Processed : RefundStatus.Declined;
        if (ok)
            order.Refunded += input.Amount;
        order.Version = Guid.NewGuid();
        db.PaymentEvents.Add(new()
        {
            Id = eventId,
            OrderId = id,
            Kind = "refund:development-only",
            Result = fingerprint
        });
        db.AuditAction(actor.Id, "payment.refund", id.ToString(), input.Reason);
        db.Notify(order);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Dto.Order(order);
    }
    public async Task Webhook(DevWebhook input, CancellationToken ct)
    {
        if (input.EventId.Length is < 1 or > 80 || input.Outcome is not ("approved" or "declined"))
            throw new RuleException("webhook", "Evento de demonstração inválido.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Lock("webhook:" + input.EventId, ct);
        await db.Lock("order:" + input.OrderId, ct);
        string key = "webhook:dev:" + input.EventId, payload = Dto.Json(input);
        var old = await db.PaymentEvents.SingleOrDefaultAsync(x => x.Id == key, ct);
        if (old != null)
        {
            if (old.Result != payload)
                throw new RuleException("duplicate_event", "Evento duplicado com conteúdo diferente.", 409);
            return;
        }
        var order = await Find(input.OrderId, ct);
        if (order.Status != OrderStatus.AwaitingPayment)
            throw new RuleException("payment_final", "Pagamento já finalizado.", 409);
        var store = await db.Restaurants.SingleAsync(x => x.Id == order.RestaurantId, ct);
        order.PaymentReference = input.Outcome == "approved" ? "DEMO-" + order.Id : "";
        order.Transition(input.Outcome == "approved" ? OrderStatus.SentToRestaurant : OrderStatus.PaymentDeclined, "webhook:development");
        if (input.Outcome == "approved" && store.Status != StoreStatus.Open)
        {
            order.Transition(OrderStatus.Cancelled, "system", "Restaurante indisponível após aprovação do pagamento.");
            order.RefundStatus = RefundStatus.Pending;
        }
        db.PaymentEvents.Add(new()
        {
            Id = key,
            OrderId = order.Id,
            Kind = "webhook:development-only",
            Result = payload
        });
        db.Notify(order);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }
}
