using Brasa.Contracts;
using Brasa.Domain;
using Microsoft.EntityFrameworkCore;
namespace Brasa.Infrastructure;

public sealed class CatalogService(AppDb db, Access access)
{
    public async Task<PageDto<RestaurantDto>> Search(string? q, string? category, bool? open, decimal? maxFee, int? maxMinutes, decimal? maxPrice, double? minRating, int page, int size, CancellationToken ct)
    {
        page = Math.Max(1, page);
        size = Math.Clamp(size, 1, 50);
        var query = db.Restaurants.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = $"%{q.Trim()}%";
            query = query.Where(r => EF.Functions.ILike(r.Name, term) || r.Products.Any(p => EF.Functions.ILike(p.Name, term)));
        }
        if (!string.IsNullOrWhiteSpace(category))
            query = query.Where(r => r.Category == category || r.Products.Any(p => p.Category == category));
        if (open == true)
            query = query.Where(r => r.Status == StoreStatus.Open);
        if (maxFee.HasValue)
            query = query.Where(r => r.DeliveryFee <= maxFee);
        if (maxMinutes.HasValue)
            query = query.Where(r => r.PreparationMinutes <= maxMinutes);
        if (maxPrice.HasValue)
            query = query.Where(r => r.Products.Any(p => p.Available && p.Price <= maxPrice));
        if (minRating.HasValue)
            query = query.Where(r => db.Reviews.Where(v => v.RestaurantId == r.Id).Average(v => (double?)v.RestaurantRating) >= minRating);
        int total = await query.CountAsync(ct);
        var stores = await query.OrderBy(r => r.Name).Skip((page - 1) * size).Take(size).ToListAsync(ct);
        var ids = stores.Select(x => x.Id).ToArray();
        var ratings = await db.Reviews.Where(v => ids.Contains(v.RestaurantId)).GroupBy(v => v.RestaurantId).Select(g => new { Id = g.Key, Rating = g.Average(v => (double)v.RestaurantRating) }).ToDictionaryAsync(x => x.Id, x => x.Rating, ct);
        return new(stores.Select(r => Dto.Restaurant(r, ratings.TryGetValue(r.Id, out var avg) ? avg : null)).ToList(), page, size, total);
    }
    public async Task<RestaurantDto> Get(Guid id, CancellationToken ct) => Dto.Restaurant(await db.Restaurants.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new RuleException("not_found", "Restaurante não encontrado.", 404), await db.Reviews.Where(x => x.RestaurantId == id).AverageAsync(x => (double?)x.RestaurantRating, ct));
    public async Task<List<ProductDto>> Menu(Guid id, CancellationToken ct) => (await db.Products.AsNoTracking().Include(p => p.Options).Where(p => p.RestaurantId == id).OrderBy(p => p.Category).ThenBy(p => p.Name).ToListAsync(ct)).Select(Dto.Product).ToList();
    public async Task<Guid> SaveStore(Actor actor, Guid? id, RestaurantInput input, CancellationToken ct)
    {
        if (id.HasValue)
            await access.Store(actor, id.Value, StorePermission.Settings, ct);
        else
            actor.Require(Roles.Admin);
        if (!Enum.TryParse<StoreStatus>(input.Status, out var status) || !Enum.IsDefined(status))
            throw new RuleException("store_status", "Situação da loja inválida.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (id.HasValue)
            await db.Lock("store:" + id, ct);
        var r = id.HasValue ? await db.Restaurants.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new RuleException("not_found", "Restaurante não encontrado.", 404) : new Restaurant();
        r.Name = input.Name.Trim();
        r.Description = input.Description;
        r.Category = input.Category;
        r.Address = input.Address;
        r.Latitude = input.Latitude;
        r.Longitude = input.Longitude;
        r.RadiusKm = input.RadiusKm;
        r.Hours = input.Hours;
        r.Status = status;
        r.PreparationMinutes = input.PreparationMinutes;
        r.DeliveryFee = input.DeliveryFee;
        r.MinimumOrder = input.MinimumOrder;
        if (!id.HasValue)
            db.Restaurants.Add(r);
        db.AuditAction(actor.Id, "restaurant.save", r.Id.ToString());
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return r.Id;
    }
    public async Task<Guid> SaveProduct(Actor actor, Guid store, Guid? id, ProductInput input, CancellationToken ct)
    {
        await access.Store(actor, store, StorePermission.Menu, ct);
        if (input.Options.Count > 20 || input.Options.Any(x => x.Price < 0 || string.IsNullOrWhiteSpace(x.Name)))
            throw new RuleException("options", "Adicionais inválidos (máximo 20).");
        if (input.ImageUrl is not null && (!Uri.TryCreate(input.ImageUrl, UriKind.Absolute, out var uri) || uri.Scheme != "https"))
            throw new RuleException("image_url", "A foto precisa usar uma URL HTTPS.");
        var p = id.HasValue ? await db.Products.Include(x => x.Options).SingleOrDefaultAsync(x => x.Id == id && x.RestaurantId == store, ct) ?? throw new RuleException("not_found", "Produto não encontrado nesta loja.", 404) : new Product { RestaurantId = store };
        p.Name = input.Name;
        p.Description = input.Description;
        p.Category = input.Category;
        p.Allergens = input.Allergens;
        p.ImageUrl = input.ImageUrl;
        p.Price = PriceBreakdown.Money(input.Price);
        p.Available = input.Available;
        db.Set<ProductOption>().RemoveRange(p.Options);
        p.Options.Clear();
        foreach (var option in input.Options)
        {
            var added = new ProductOption { ProductId = p.Id, Name = option.Name, Price = PriceBreakdown.Money(option.Price) };
            db.Set<ProductOption>().Add(added);
            p.Options.Add(added);
        }
        if (!id.HasValue)
            db.Products.Add(p);
        db.AuditAction(actor.Id, "menu.save", p.Id.ToString());
        await db.SaveChangesAsync(ct);
        return p.Id;
    }
    public async Task<List<StoreAccessDto>> MyStores(Actor actor, CancellationToken ct)
    {
        if (actor.Is(Roles.Admin))
            return await db.Restaurants.Select(x => new StoreAccessDto(x.Id, x.Name, 31)).ToListAsync(ct);
        return await (from m in db.Memberships join r in db.Restaurants on m.RestaurantId equals r.Id where m.UserId == actor.Id select new StoreAccessDto(r.Id, r.Name, (int)m.Permissions)).ToListAsync(ct);
    }
    public async Task<List<MemberDto>> Members(Actor actor, Guid id, CancellationToken ct)
    {
        await access.Store(actor, id, StorePermission.Staff, ct);
        return await (from m in db.Memberships join u in db.Users on m.UserId equals u.Id where m.RestaurantId == id select new MemberDto(u.Id, u.DisplayName, u.Email!, (int)m.Permissions)).ToListAsync(ct);
    }
    public async Task SetMember(Actor actor, Guid id, MemberInput input, CancellationToken ct)
    {
        await access.Store(actor, id, StorePermission.Staff, ct);
        var user = await db.Users.SingleOrDefaultAsync(x => x.NormalizedEmail == input.Email.ToUpperInvariant(), ct) ?? throw new RuleException("not_found", "Cadastre primeiro a conta do funcionário.", 404);
        if (user.Id == actor.Id && !actor.Is(Roles.Admin))
            throw new RuleException("self_permission", "Solicite a outro administrador a alteração das suas permissões.");
        var m = await db.Memberships.SingleOrDefaultAsync(x => x.RestaurantId == id && x.UserId == user.Id, ct);
        if (m == null)
            db.Memberships.Add(new()
            {
                RestaurantId = id,
                UserId = user.Id,
                Permissions = (StorePermission)input.Permissions
            });
        else
            m.Permissions = (StorePermission)input.Permissions;
        db.AuditAction(actor.Id, "member.permissions", id.ToString(), $"Usuário {user.Id}: {input.Permissions}");
        await db.SaveChangesAsync(ct);
    }
}
