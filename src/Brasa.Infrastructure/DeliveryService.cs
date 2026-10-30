using Brasa.Contracts;
using Brasa.Domain;
using Microsoft.EntityFrameworkCore;
namespace Brasa.Infrastructure;

public sealed class DeliveryService(AppDb db, Access access, IMapsService maps)
{
    public async Task SetAvailability(Actor actor, AvailabilityInput input, CancellationToken ct)
    {
        actor.Require(Roles.Courier);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Lock("courier:" + actor.Id, ct);
        var p = await db.Couriers.SingleAsync(x => x.UserId == actor.Id, ct);
        if (!p.Approved)
            throw new RuleException("courier_pending", "Seu cadastro aguarda aprovação.", 403);
        if (!input.Available && await db.Orders.AnyAsync(x => x.CourierId == actor.Id && (x.Status == OrderStatus.CourierAssigned || x.Status == OrderStatus.InTransit), ct))
            throw new RuleException("active_delivery", "Finalize a entrega ativa antes de ficar indisponível.", 409);
        p.Available = input.Available;
        p.Vehicle = input.Vehicle.Length <= 60 ? input.Vehicle : throw new RuleException("vehicle", "Veículo inválido.");
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }
    public async Task Dispatch(CancellationToken ct)
    {
        var ids = await db.Orders.Where(x => x.Status == OrderStatus.Ready).Select(x => x.Id).Take(100).ToListAsync(ct);
        var settings = await db.Settings.SingleAsync(ct);
        foreach (var id in ids)
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await db.Lock("order:" + id, ct);
            var order = await db.Orders.SingleAsync(x => x.Id == id, ct);
            if (order.Status != OrderStatus.Ready)
                continue;
            var courierIds = await (from c in db.Couriers join u in db.Users on c.UserId equals u.Id where c.Approved && c.Available && !u.Blocked && !db.Orders.Any(o => o.CourierId == c.UserId && (o.Status == OrderStatus.InTransit || o.Status == OrderStatus.CourierAssigned)) select c.UserId).Take(50).ToListAsync(ct);
            var offers = await db.Offers.Where(x => x.OrderId == id).ToListAsync(ct);
            foreach (var courier in courierIds)
            {
                var offer = offers.SingleOrDefault(x => x.CourierId == courier);
                if (offer == null)
                    db.Offers.Add(new()
                    {
                        OrderId = id,
                        CourierId = courier,
                        ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(settings.OfferSeconds)
                    });
                else if (offer.State == "Pending" && offer.ExpiresAt < DateTimeOffset.UtcNow)
                    offer.State = "Expired";
            }
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
    }
    public async Task<List<OfferDto>> Offers(Actor actor, CancellationToken ct)
    {
        actor.Require(Roles.Courier);
        var now = DateTimeOffset.UtcNow;
        var data = await (from f in db.Offers join o in db.Orders on f.OrderId equals o.Id join r in db.Restaurants on o.RestaurantId equals r.Id where f.CourierId == actor.Id && f.State == "Pending" && f.ExpiresAt > now && o.Status == OrderStatus.Ready select new { f, o, r }).ToListAsync(ct);
        return data.Select(x => new OfferDto(x.f.Id, x.o.Id, x.r.Name, x.r.Address, "Dentro da área de atendimento da loja", maps.ApproximateDistanceKm(x.r.Latitude, x.r.Longitude, x.o.Latitude, x.o.Longitude), x.o.Prices.CourierAmount, x.f.ExpiresAt)).ToList();
    }
    public async Task Accept(Actor actor, Guid id, bool accepted, CancellationToken ct)
    {
        actor.Require(Roles.Courier);
        var reference = await db.Offers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.CourierId == actor.Id, ct) ?? throw new RuleException("not_found", "Oferta não encontrada.", 404);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Lock("order:" + reference.OrderId, ct);
        await db.Lock("courier:" + actor.Id, ct);
        var offer = await db.Offers.SingleAsync(x => x.Id == id, ct);
        var o = await db.Orders.SingleAsync(x => x.Id == offer.OrderId, ct);
        if (offer.State != "Pending" || offer.ExpiresAt < DateTimeOffset.UtcNow || o.Status != OrderStatus.Ready)
            throw new RuleException("offer_expired", "A oferta expirou ou já foi atribuída.", 409);
        if (!accepted)
            offer.State = "Declined";
        else
        {
            var profile = await db.Couriers.SingleAsync(x => x.UserId == actor.Id, ct);
            if (!profile.Approved || !profile.Available)
                throw new RuleException("courier_unavailable", "Fique disponível e aguarde a aprovação.", 403);
            if (await db.Orders.AnyAsync(x => x.CourierId == actor.Id && (x.Status == OrderStatus.CourierAssigned || x.Status == OrderStatus.InTransit), ct))
                throw new RuleException("active_delivery", "Você já tem uma entrega ativa.", 409);
            o.CourierId = actor.Id;
            o.Transition(OrderStatus.CourierAssigned, actor.Id);
            db.Deliveries.Add(new()
            {
                OrderId = o.Id,
                CourierId = actor.Id
            });
            offer.State = "Accepted";
            await db.Offers.Where(x => x.OrderId == o.Id && x.Id != id && x.State == "Pending").ExecuteUpdateAsync(s => s.SetProperty(x => x.State, "Unavailable"), ct);
            db.Notify(o);
            db.AuditAction(actor.Id, "delivery.accept", o.Id.ToString());
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }
    public async Task<List<DeliveryDto>> History(Actor actor, CancellationToken ct)
    {
        actor.Require(Roles.Courier);
        var data = await (from d in db.Deliveries join o in db.Orders on d.OrderId equals o.Id join r in db.Restaurants on o.RestaurantId equals r.Id where d.CourierId == actor.Id orderby o.CreatedAt descending select new { d, o, r }).Take(100).ToListAsync(ct);
        var ids = data.Select(x => x.o.Id).ToArray();
        var ratings = await db.Reviews.Where(x => ids.Contains(x.OrderId)).ToDictionaryAsync(x => x.OrderId, x => x.CourierRating, ct);
        return data.Select(x => new DeliveryDto(x.o.Id, x.d.Stage.ToString(), x.r.Name, x.r.Address, x.r.Latitude, x.r.Longitude, x.o.AddressSnapshot, x.o.Latitude, x.o.Longitude, x.o.Prices.CourierAmount, x.d.Version, x.d.Latitude, x.d.Longitude, x.d.LocationAt, x.o.Status.ToString(), ratings.TryGetValue(x.o.Id, out var rating) ? rating : null)).ToList();
    }
    public async Task Advance(Actor actor, Guid id, DeliveryInput input, CancellationToken ct)
    {
        actor.Require(Roles.Courier);
        if (!Enum.TryParse<DeliveryStage>(input.Stage, out var next) || !Enum.IsDefined(next))
            throw new RuleException("stage", "Etapa inválida.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Lock("order:" + id, ct);
        var d = await db.Deliveries.SingleOrDefaultAsync(x => x.OrderId == id && x.CourierId == actor.Id, ct) ?? throw new RuleException("forbidden", "Entrega não atribuída a você.", 403);
        var order = await db.Orders.SingleAsync(x => x.Id == id, ct);
        if (order.Terminal)
            throw new RuleException("delivery_closed", "O pedido foi encerrado.", 409);
        if (d.Version != input.Version)
            throw new RuleException("stale_delivery", "A entrega mudou. Recarregue.", 409);
        if (next == DeliveryStage.PickedUp && !order.HandoverConfirmed)
            throw new RuleException("handover_required", "Aguarde a loja confirmar a entrega do pedido a você.", 409);
        d.Advance(next);
        if (next == DeliveryStage.PickedUp)
            order.Transition(OrderStatus.InTransit, actor.Id);
        if (next == DeliveryStage.Delivered)
        {
            order.Transition(OrderStatus.Delivered, actor.Id);
            d.Latitude = null;
            d.Longitude = null;
            d.LocationAt = null;
        }
        order.Events.Add(new()
        {
            OrderId = id,
            ActorId = actor.Id,
            Previous = "Entrega",
            Next = next.ToString()
        });
        db.AuditAction(actor.Id, "delivery.stage", id.ToString(), next.ToString());
        db.Notify(order);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }
    public async Task UpdateLocation(Actor actor, Guid id, LocationInput input, CancellationToken ct)
    {
        actor.Require(Roles.Courier);
        var now = DateTimeOffset.UtcNow;
        if (input.At < now.AddMinutes(-2) || input.At > now.AddSeconds(30))
            throw new RuleException("location_time", "Localização antiga ou com horário inválido.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Lock("order:" + id, ct);
        var o = await db.Orders.SingleOrDefaultAsync(x => x.Id == id && x.CourierId == actor.Id, ct) ?? throw new RuleException("forbidden", "Entrega não atribuída a você.", 403);
        if (o.Status is not (OrderStatus.CourierAssigned or OrderStatus.InTransit))
            throw new RuleException("tracking_inactive", "A localização só é aceita durante uma entrega ativa.", 409);
        var d = await db.Deliveries.SingleAsync(x => x.OrderId == id, ct);
        if (d.LocationAt.HasValue && input.At <= d.LocationAt)
            throw new RuleException("stale_location", "Localização fora de ordem.", 409);
        d.Latitude = input.Latitude;
        d.Longitude = input.Longitude;
        d.LocationAt = input.At.ToUniversalTime();
        d.Version = Guid.NewGuid();
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }
    public async Task<TrackingDto> Tracking(Actor actor, Guid id, CancellationToken ct)
    {
        var o = await db.Orders.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new RuleException("not_found", "Pedido não encontrado.", 404);
        await access.ReadOrder(actor, o, ct);
        var d = await db.Deliveries.AsNoTracking().SingleOrDefaultAsync(x => x.OrderId == id, ct);
        bool active = o.Status is OrderStatus.CourierAssigned or OrderStatus.InTransit;
        return new(d?.Stage.ToString() ?? "Aguardando entregador", active ? d?.Latitude : null, active ? d?.Longitude : null, active ? d?.LocationAt : null);
    }
    public async Task ExpireOrders(CancellationToken ct)
    {
        var s = await db.Settings.SingleAsync(ct);
        var now = DateTimeOffset.UtcNow;
        var ids = await db.Orders.Where(x => (x.Status == OrderStatus.SentToRestaurant && x.UpdatedAt < now.AddMinutes(-s.RestaurantResponseMinutes)) || (x.Status == OrderStatus.Ready && x.UpdatedAt < now.AddMinutes(-s.CourierWaitMinutes)) || (x.Status == OrderStatus.AwaitingPayment && x.CreatedAt < now.AddMinutes(-30))).Select(x => x.Id).Take(100).ToListAsync(ct);
        foreach (var id in ids)
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await db.Lock("order:" + id, ct);
            var o = await db.Orders.SingleAsync(x => x.Id == id, ct);
            bool expired = (o.Status == OrderStatus.SentToRestaurant && o.UpdatedAt < now.AddMinutes(-s.RestaurantResponseMinutes)) || (o.Status == OrderStatus.Ready && o.UpdatedAt < now.AddMinutes(-s.CourierWaitMinutes)) || (o.Status == OrderStatus.AwaitingPayment && o.CreatedAt < now.AddMinutes(-30));
            if (!expired)
                continue;
            var paid = o.Paid;
            o.Transition(OrderStatus.Cancelled, "system", "Prazo operacional excedido: pagamento, resposta da loja ou entregador indisponível.");
            if (paid)
                o.RefundStatus = RefundStatus.Pending;
            db.Notify(o);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
    }
}
