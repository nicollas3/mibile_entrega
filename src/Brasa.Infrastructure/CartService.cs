using Brasa.Contracts;
using Brasa.Domain;
using Microsoft.EntityFrameworkCore;
namespace Brasa.Infrastructure;

public sealed class CartService(AppDb db)
{
    public async Task<(Restaurant? Store, List<ItemSnapshot> Items)> Validate(List<CartLine> lines, CancellationToken ct)
    {
        if (lines.Count > 30 || lines.Select(x => x.ProductId).Distinct().Count() != lines.Count || lines.Any(x => x.Quantity is < 1 or > 50 || x.Notes.Length > 500 || x.OptionIds.Count > 20 || x.OptionIds.Distinct().Count() != x.OptionIds.Count))
            throw new RuleException("cart_lines", "Carrinho inválido: até 30 produtos distintos e 50 unidades por produto.");
        if (lines.Count == 0)
            return (null, []);
        var ids = lines.Select(x => x.ProductId).ToArray();
        var products = await db.Products.Include(x => x.Options).Where(x => ids.Contains(x.Id)).ToListAsync(ct);
        if (products.Count != lines.Count || products.Any(x => !x.Available))
            throw new RuleException("unavailable_item", "Um item foi removido ou está esgotado. Atualize o carrinho.", 409);
        if (products.Select(x => x.RestaurantId).Distinct().Count() != 1)
            throw new RuleException("one_restaurant", "Um pedido deve conter itens de apenas um restaurante. Esvazie o carrinho antes de trocar de loja.", 409);
        var store = await db.Restaurants.SingleAsync(x => x.Id == products[0].RestaurantId, ct);
        if (store.Status != StoreStatus.Open)
            throw new RuleException("store_closed", "A loja está fechada ou pausada.", 409);
        var snapshots = new List<ItemSnapshot>();
        foreach (var line in lines)
        {
            var p = products.Single(p => p.Id == line.ProductId);
            var options = p.Options.Where(x => line.OptionIds.Contains(x.Id)).ToList();
            if (options.Count != line.OptionIds.Count)
                throw new RuleException("option_unavailable", "Um adicional mudou. Revise o item.", 409);
            snapshots.Add(new(p.Id, p.Name, p.Price, line.Quantity, line.Notes, options.Select(x => new OptionDto(x.Id, x.Name, x.Price)).ToList()));
        }
        return (store, snapshots);
    }
    public async Task<CartDto> Read(Actor actor, string? coupon, CancellationToken ct)
    {
        var cart = await db.Carts.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == actor.Id, ct);
        var (store, items) = await Validate(cart == null ? [] : Dto.Read<CartLine>(cart.ItemsJson), ct);
        if (store == null)
            return new(null, [], null);
        var prices = await Price(store, items, coupon, ct);
        return new(store.Id, items, Dto.Totals(prices));
    }
    public async Task<CartDto> Save(Actor actor, CartInput input, CancellationToken ct)
    {
        actor.Require(Roles.Customer);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Lock("customer:" + actor.Id, ct);
        var cart = await db.Carts.SingleOrDefaultAsync(x => x.UserId == actor.Id, ct);
        var (store, items) = await Validate(input.Items, ct);
        if (cart?.RestaurantId is not null && store is not null && cart.RestaurantId != store.Id)
            throw new RuleException("one_restaurant", "Seu carrinho pertence a outro restaurante. Esvazie-o antes de trocar.", 409);
        if (cart == null)
        {
            cart = new()
            {
                UserId = actor.Id
            };
            db.Carts.Add(cart);
        }
        cart.RestaurantId = store?.Id;
        cart.ItemsJson = Dto.Json(input.Items);
        cart.Version = Guid.NewGuid();
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new(store?.Id, items, store == null ? null : Dto.Totals(await Price(store, items, null, ct)));
    }
    public async Task<PriceBreakdown> Price(Restaurant store, List<ItemSnapshot> items, string? code, CancellationToken ct)
    {
        decimal subtotal = items.Sum(x => (x.UnitPrice + x.Options.Sum(o => o.Price)) * x.Quantity), discount = 0;
        if (!string.IsNullOrWhiteSpace(code))
        {
            var coupon = await db.Coupons.SingleOrDefaultAsync(x => x.Code == code.ToUpperInvariant().Trim(), ct);
            if (coupon == null || !coupon.Active || coupon.ExpiresAt <= DateTimeOffset.UtcNow || coupon.Uses >= coupon.MaxUses || subtotal < coupon.MinimumSubtotal)
                throw new RuleException("invalid_coupon", "Cupom inválido, expirado, esgotado ou valor mínimo não atingido.");
            discount = Math.Min(coupon.Discount, subtotal);
        }
        var service = (await db.Settings.SingleAsync(ct)).ServiceFee;
        return PriceBreakdown.Calculate(subtotal, discount, store.DeliveryFee, service, store.CommissionRate);
    }
}
