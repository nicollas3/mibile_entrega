using Brasa.Contracts;
using Brasa.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
namespace Brasa.Infrastructure;

public sealed class OperationsService(AppDb db, Access access, UserManager<AppUser> users)
{
    public async Task<Guid> Ticket(Actor actor, TicketInput input, CancellationToken ct)
    {
        if (input.OrderId.HasValue)
        {
            var order = await db.Orders.SingleOrDefaultAsync(x => x.Id == input.OrderId, ct) ?? throw new RuleException("not_found", "Pedido não encontrado.", 404);
            await access.ReadOrder(actor, order, ct);
        }
        var t = new SupportTicket { OrderId = input.OrderId, UserId = actor.Id, Category = input.Category, Description = input.Description };
        db.Tickets.Add(t);
        db.AuditAction(actor.Id, "support.open", t.Id.ToString(), input.Category);
        await db.SaveChangesAsync(ct);
        return t.Id;
    }
    public async Task<PageDto<TicketDto>> Tickets(Actor actor, int page, CancellationToken ct)
    {
        var q = db.Tickets.AsNoTracking().AsQueryable();
        if (!actor.Is(Roles.Admin) && !actor.Is(Roles.Support))
            q = q.Where(x => x.UserId == actor.Id);
        page = Math.Max(1, page);
        return new((await q.OrderByDescending(x => x.At).Skip((page - 1) * 50).Take(50).ToListAsync(ct)).Select(Dto.Ticket).ToList(), page, 50, await q.CountAsync(ct));
    }
    public async Task Resolve(Actor actor, Guid id, string resolution, CancellationToken ct)
    {
        actor.Require(Roles.Admin, Roles.Support);
        var t = await db.Tickets.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new RuleException("not_found", "Solicitação não encontrada.", 404);
        t.State = "Resolved";
        t.Resolution = resolution;
        db.AuditAction(actor.Id, "support.resolve", id.ToString(), resolution);
        await db.SaveChangesAsync(ct);
    }
    public async Task Review(Actor actor, Guid id, ReviewInput input, CancellationToken ct)
    {
        var o = await db.Orders.SingleOrDefaultAsync(x => x.Id == id && x.CustomerId == actor.Id, ct) ?? throw new RuleException("forbidden", "Pedido não pertence a você.", 403);
        if (o.Status != OrderStatus.Delivered)
            throw new RuleException("review_status", "Avalie somente pedidos entregues.");
        if (await db.Reviews.AnyAsync(x => x.OrderId == id, ct))
            throw new RuleException("already_reviewed", "Pedido já avaliado.", 409);
        db.Reviews.Add(new()
        {
            OrderId = id,
            CustomerId = actor.Id,
            RestaurantId = o.RestaurantId,
            RestaurantRating = input.RestaurantRating,
            CourierRating = input.CourierRating,
            Comment = input.Comment
        });
        await db.SaveChangesAsync(ct);
    }
    public async Task<ReportDto> Report(Actor actor, Guid? store, DateTimeOffset? from, DateTimeOffset? to, string? status, string? payment, string? region, CancellationToken ct)
    {
        if (store.HasValue)
            await access.Store(actor, store.Value, StorePermission.Reports, ct);
        else
            actor.Require(Roles.Admin);
        var start = (from ?? DateTimeOffset.UtcNow.AddDays(-30)).ToUniversalTime();
        var end = (to ?? DateTimeOffset.UtcNow).ToUniversalTime();
        if (end < start || end - start > TimeSpan.FromDays(366))
            throw new RuleException("report_period", "Escolha um período de até 366 dias.");
        var q = db.Orders.AsNoTracking().Where(x => x.CreatedAt >= start && x.CreatedAt <= end);
        if (store.HasValue)
            q = q.Where(x => x.RestaurantId == store);
        if (!string.IsNullOrEmpty(status))
        {
            if (!Enum.TryParse<OrderStatus>(status, out var s))
                throw new RuleException("status", "Status inválido.");
            q = q.Where(x => x.Status == s);
        }
        if (!string.IsNullOrEmpty(payment))
            q = q.Where(x => x.PaymentMethod == payment);
        if (!string.IsNullOrWhiteSpace(region))
            q = q.Where(x => EF.Functions.ILike(x.AddressSnapshot, $"%{region}%"));
        var all = await q.ToListAsync(ct);
        var paid = all.Where(x => x.Paid).ToList();
        decimal Net(Order o, decimal v) => PriceBreakdown.Money(v * (1 - o.Refunded / o.Prices.Total));
        return new(all.Count, all.Count(x => !x.Terminal), all.Count(x => x.Status is OrderStatus.Cancelled or OrderStatus.Rejected), paid.Sum(x => x.Prices.Total), paid.Sum(x => x.Refunded), paid.Sum(x => Net(x, x.Prices.Commission)), paid.Sum(x => Net(x, x.Prices.RestaurantAmount)), paid.Sum(x => Net(x, x.Prices.CourierAmount)), paid.Sum(x => Net(x, x.Prices.ServiceFee)));
    }
    public async Task<PageDto<AccountDto>> Accounts(Actor actor, int page, string? q, CancellationToken ct)
    {
        actor.Require(Roles.Admin);
        page = Math.Max(1, page);
        var query = db.Users.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(x => EF.Functions.ILike(x.DisplayName, $"%{q}%") || EF.Functions.ILike(x.Email!, $"%{q}%"));
        var records = await query.OrderBy(x => x.DisplayName).Skip((page - 1) * 50).Take(50).ToListAsync(ct);
        var list = new List<AccountDto>();
        foreach (var u in records)
            list.Add(new(u.Id, u.DisplayName, u.Email!, u.Blocked, (await users.GetRolesAsync(u)).ToArray()));
        return new(list, page, 50, await query.CountAsync(ct));
    }
    public async Task Block(Actor actor, string id, BlockInput input, CancellationToken ct)
    {
        actor.Require(Roles.Admin);
        if (id == actor.Id)
            throw new RuleException("self_block", "Você não pode bloquear sua própria conta.");
        var u = await users.FindByIdAsync(id) ?? throw new RuleException("not_found", "Conta não encontrada.", 404);
        u.Blocked = input.Blocked;
        u.BlockReason = input.Reason;
        if (!(await users.UpdateAsync(u)).Succeeded)
            throw new RuleException("account_conflict", "A conta foi alterada por outra operação. Recarregue.", 409);
        await db.Sessions.Where(x => x.UserId == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow), ct);
        db.AuditAction(actor.Id, "account.block", id, input.Reason);
        await db.SaveChangesAsync(ct);
    }
    public async Task Grant(Actor actor, string id, RoleInput input, CancellationToken ct)
    {
        actor.Require(Roles.Admin);
        if (!Roles.All.Contains(input.Role))
            throw new RuleException("role", "Perfil inválido.");
        var u = await users.FindByIdAsync(id) ?? throw new RuleException("not_found", "Conta não encontrada.", 404);
        var result = await users.AddToRoleAsync(u, input.Role);
        if (!result.Succeeded)
            throw new RuleException("role", "Não foi possível atribuir o perfil.");
        if (input.Role == Roles.Courier && !await db.Couriers.AnyAsync(x => x.UserId == id, ct))
            db.Couriers.Add(new()
            {
                UserId = id
            });
        await db.Sessions.Where(x => x.UserId == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow), ct);
        db.AuditAction(actor.Id, "account.role", id, input.Reason);
        await db.SaveChangesAsync(ct);
    }
    public async Task Approve(Actor actor, string id, ApprovalInput input, CancellationToken ct)
    {
        actor.Require(Roles.Admin);
        var p = await db.Couriers.SingleOrDefaultAsync(x => x.UserId == id, ct) ?? throw new RuleException("not_found", "Entregador não encontrado.", 404);
        if (!input.Approved && await db.Orders.AnyAsync(x => x.CourierId == id && (x.Status == OrderStatus.InTransit || x.Status == OrderStatus.CourierAssigned), ct))
            throw new RuleException("active_delivery", "Resolva a entrega ativa antes de suspender o entregador.", 409);
        p.Approved = input.Approved;
        if (!input.Approved)
            p.Available = false;
        db.AuditAction(actor.Id, "courier.approval", id, input.Reason);
        await db.SaveChangesAsync(ct);
    }
    public async Task SaveCoupon(Actor actor, CouponInput input, CancellationToken ct)
    {
        actor.Require(Roles.Admin);
        if (input.ExpiresAt <= DateTimeOffset.UtcNow)
            throw new RuleException("coupon_expiry", "A validade deve ser futura.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Lock("coupon:" + input.Code, ct);
        var c = await db.Coupons.SingleOrDefaultAsync(x => x.Code == input.Code, ct);
        if (c == null)
        {
            c = new()
            {
                Code = input.Code
            };
            db.Coupons.Add(c);
        }
        if (input.MaxUses < c.Uses)
            throw new RuleException("coupon_uses", "O limite não pode ser menor que os usos existentes.");
        c.Discount = input.Discount;
        c.MinimumSubtotal = input.MinimumSubtotal;
        c.ExpiresAt = input.ExpiresAt.ToUniversalTime();
        c.MaxUses = input.MaxUses;
        c.Active = input.Active;
        db.AuditAction(actor.Id, "coupon.save", c.Code);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }
}
