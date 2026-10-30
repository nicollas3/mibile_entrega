using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Brasa.Contracts;
using Brasa.Domain;
using Brasa.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
namespace Brasa.Api;

public sealed class ValidationFilter : IEndpointFilter
{
    static string Label(string? field) => field switch
    {
        "Email" => "email", "Password" => "senha", "Name" => "nome", "Phone" => "telefone",
        "Label" => "nome do endereço", "Street" => "rua", "Number" => "número", "District" => "bairro",
        "City" => "cidade", "State" => "UF", "PostalCode" => "CEP", "Latitude" => "latitude", "Longitude" => "longitude",
        "Description" => "descrição", "Category" => "categoria", "Status" => "situação", "Reason" => "justificativa",
        "Price" => "preço", "Quantity" => "quantidade", "Notes" => "observações", "Items" => "itens",
        "Options" or "OptionIds" => "adicionais", "PreparationMinutes" => "tempo de preparo", "RadiusKm" => "área de atendimento",
        "DeliveryFee" => "taxa de entrega", "ServiceFee" => "taxa de serviço", "MinimumOrder" => "pedido mínimo",
        "Discount" => "desconto", "MinimumSubtotal" => "subtotal mínimo", "Code" => "código", "MaxUses" => "limite de usos",
        "RestaurantRating" => "nota do restaurante", "CourierRating" => "nota da entrega", "Comment" => "comentário",
        "Amount" => "valor", "Rate" => "comissão", "Resolution" => "resposta", "Permissions" => "permissões",
        "Token" or "RefreshToken" => "token", "PaymentMethod" => "forma de pagamento", "Stage" => "etapa da entrega",
        _ => "informado"
    };
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var errors = new List<string>();
        foreach (var arg in ctx.Arguments)
            Validate(arg, errors, 0);
        if (errors.Count > 0)
            return Results.Problem(statusCode: 400, title: "Revise os dados informados.", extensions: new Dictionary<string, object?> { { "errors", errors.Distinct().Take(20).ToArray() } });
        return await next(ctx);
    }
    static void Validate(object? value, List<string> errors, int depth)
    {
        if (value == null || depth > 5)
            return;
        if (value is System.Collections.IEnumerable list && value is not string)
        {
            int n = 0;
            foreach (var item in list)
            {
                if (++n > 100)
                {
                    errors.Add("Lista muito extensa.");
                    break;
                }
                Validate(item, errors, depth + 1);
            }
            return;
        }
        var type = value.GetType();
        if (type.Namespace != "Brasa.Contracts")
            return;
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(value, new(value), results, true);
        errors.AddRange(results.Select(r => $"Revise o campo {Label(r.MemberNames.FirstOrDefault())}: ele é obrigatório ou possui formato/valor fora dos limites permitidos."));
        var nullability = new NullabilityInfoContext();
        foreach (var p in type.GetProperties())
        {
            var v = p.GetValue(value);
            if (v == null && nullability.Create(p).ReadState == NullabilityState.NotNull && !p.PropertyType.IsValueType)
                errors.Add($"O campo {Label(p.Name)} é obrigatório.");
            if (v is string s && s.Length > 4000)
                errors.Add($"O campo {Label(p.Name)} é muito extenso.");
            if (v is double d && !double.IsFinite(d))
                errors.Add($"O campo {Label(p.Name)} precisa de um número válido.");
            Validate(v, errors, depth + 1);
        }
    }
}
public static class Endpoints
{
    public static Actor Actor(this ClaimsPrincipal p) => new(p.FindFirstValue("sub")!, p.FindAll(ClaimTypes.Role).Select(x => x.Value).ToArray());
    public static void MapBrasaEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api").AddEndpointFilter<ValidationFilter>();
        var auth = api.MapGroup("/auth").RequireRateLimiting("auth");
        auth.MapPost("/register", async (RegisterRequest input, AuthService s, CancellationToken ct) => Results.Ok(await s.Register(input, ct)));
        auth.MapPost("/login", async (LoginRequest input, AuthService s, CancellationToken ct) => Results.Ok(await s.Login(input, ct)));
        auth.MapPost("/refresh", async (RefreshRequest input, AuthService s, CancellationToken ct) => Results.Ok(await s.Refresh(input.RefreshToken, ct)));
        auth.MapPost("/forgot-password", async (ForgotRequest input, AuthService s, CancellationToken ct) => { await s.Forgot(input.Email, ct); return Results.Ok(new { message = "Se a conta existir, as instruções serão enviadas. No modo local, consulte a caixa de email de desenvolvimento." }); });
        auth.MapPost("/reset-password", async (ResetRequest input, AuthService s, CancellationToken ct) => { await s.Reset(input, ct); return Results.NoContent(); });
        auth.MapPost("/logout", async (ClaimsPrincipal p, AuthService s, CancellationToken ct) => { await s.Logout(Guid.Parse(p.FindFirstValue("sid")!), ct); return Results.NoContent(); }).RequireAuthorization();
        var me = api.MapGroup("/me").RequireAuthorization();
        me.MapGet("/", async (ClaimsPrincipal p, UserManager<AppUser> users, AuthService s) => Results.Ok(await s.User((await users.FindByIdAsync(p.Actor().Id))!)));
        me.MapPut("/", async (ProfileRequest input, ClaimsPrincipal p, UserManager<AppUser> users) => { var u = (await users.FindByIdAsync(p.Actor().Id))!; u.DisplayName = input.Name; u.PhoneNumber = input.Phone; AuthService.Ensure(await users.UpdateAsync(u)); return Results.NoContent(); });
        me.MapGet("/addresses", async (ClaimsPrincipal p, AppDb db, CancellationToken ct) => Results.Ok((await db.Addresses.Where(x => x.UserId == p.Actor().Id).ToListAsync(ct)).Select(Dto.Address)));
        me.MapPost("/addresses", async (AddressInput input, ClaimsPrincipal p, AppDb db, CancellationToken ct) =>
        {
            p.Actor().Require(Roles.Customer);
            if (await db.Addresses.CountAsync(x => x.UserId == p.Actor().Id, ct) >= 20)
                throw new RuleException("address_limit", "Limite de 20 endereços.");
            var a = new CustomerAddress { UserId = p.Actor().Id };
            Fill(a, input);
            db.Addresses.Add(a);
            await db.SaveChangesAsync(ct);
            return Results.Ok(Dto.Address(a));
        });
        me.MapPut("/addresses/{id:guid}", async (Guid id, AddressInput input, ClaimsPrincipal p, AppDb db, CancellationToken ct) => { var a = await db.Addresses.SingleOrDefaultAsync(x => x.Id == id && x.UserId == p.Actor().Id, ct) ?? throw new RuleException("not_found", "Endereço não encontrado.", 404); Fill(a, input); await db.SaveChangesAsync(ct); return Results.NoContent(); });
        me.MapDelete("/addresses/{id:guid}", async (Guid id, ClaimsPrincipal p, AppDb db, CancellationToken ct) => { await db.Addresses.Where(x => x.Id == id && x.UserId == p.Actor().Id).ExecuteDeleteAsync(ct); return Results.NoContent(); });
        me.MapGet("/export", async (ClaimsPrincipal p, AppDb db, AuthService auth, UserManager<AppUser> users, CancellationToken ct) =>
        {
            var id = p.Actor().Id;
            var u = (await users.FindByIdAsync(id))!;
            return Results.Ok(new
            {
                profile = await auth.User(u),
                addresses = (await db.Addresses.Where(x => x.UserId == id).ToListAsync(ct)).Select(Dto.Address),
                orders = (await db.Orders.Where(x => x.CustomerId == id).Take(1000).ToListAsync(ct)).Select(Dto.Order),
                support = (await db.Tickets.Where(x => x.UserId == id).Take(1000).ToListAsync(ct)).Select(Dto.Ticket),
                notice = "Para exportações maiores, correções ou exclusão, abra solicitação de privacidade. Registros financeiros podem exigir retenção legal."
            });
        });
        me.MapGet("/notifications", async (ClaimsPrincipal p, AppDb db, CancellationToken ct) => Results.Ok(await (from n in db.Outbox join o in db.Orders on n.OrderId equals o.Id where o.CustomerId == p.Actor().Id orderby n.Id descending select new { n.Id, n.OrderId, n.Status, n.At }).Take(100).ToListAsync(ct)));
        api.MapGet("/restaurants", async (string? q, string? category, bool? open, decimal? maxFee, int? maxMinutes, decimal? maxPrice, double? minRating, int? page, int? pageSize, CatalogService s, CancellationToken ct) => Results.Ok(await s.Search(q, category, open, maxFee, maxMinutes, maxPrice, minRating, page ?? 1, pageSize ?? 20, ct)));
        api.MapGet("/categories", async (AppDb db, CancellationToken ct) => Results.Ok(await db.Restaurants.Select(x => x.Category).Union(db.Products.Select(x => x.Category)).Where(x => x != "").Distinct().OrderBy(x => x).ToListAsync(ct)));
        api.MapGet("/restaurants/{id:guid}", async (Guid id, CatalogService s, CancellationToken ct) => Results.Ok(await s.Get(id, ct)));
        api.MapGet("/restaurants/{id:guid}/menu", async (Guid id, CatalogService s, CancellationToken ct) => Results.Ok(await s.Menu(id, ct)));
        api.MapGet("/restaurants/{id:guid}/reviews", async (Guid id, AppDb db, int? page, CancellationToken ct) => Results.Ok(await db.Reviews.Where(x => x.RestaurantId == id).OrderBy(x => x.OrderId).Skip((Math.Max(page ?? 1, 1) - 1) * 20).Take(20).Select(x => new ReviewDto(x.RestaurantRating, x.CourierRating, x.Comment)).ToListAsync(ct)));
        var stores = api.MapGroup("/stores").RequireAuthorization();
        stores.MapGet("/", async (ClaimsPrincipal p, CatalogService s, CancellationToken ct) => Results.Ok(await s.MyStores(p.Actor(), ct)));
        stores.MapPost("/", async (RestaurantInput input, ClaimsPrincipal p, CatalogService s, CancellationToken ct) => Results.Ok(new { id = await s.SaveStore(p.Actor(), null, input, ct) }));
        stores.MapPut("/{id:guid}", async (Guid id, RestaurantInput input, ClaimsPrincipal p, CatalogService s, CancellationToken ct) => { await s.SaveStore(p.Actor(), id, input, ct); return Results.NoContent(); });
        stores.MapPost("/{id:guid}/products", async (Guid id, ProductInput input, ClaimsPrincipal p, CatalogService s, CancellationToken ct) => Results.Ok(new { id = await s.SaveProduct(p.Actor(), id, null, input, ct) }));
        stores.MapPut("/{id:guid}/products/{product:guid}", async (Guid id, Guid product, ProductInput input, ClaimsPrincipal p, CatalogService s, CancellationToken ct) => { await s.SaveProduct(p.Actor(), id, product, input, ct); return Results.NoContent(); });
        stores.MapGet("/{id:guid}/members", async (Guid id, ClaimsPrincipal p, CatalogService s, CancellationToken ct) => Results.Ok(await s.Members(p.Actor(), id, ct)));
        stores.MapPut("/{id:guid}/members", async (Guid id, MemberInput input, ClaimsPrincipal p, CatalogService s, CancellationToken ct) => { await s.SetMember(p.Actor(), id, input, ct); return Results.NoContent(); });
        stores.MapGet("/{id:guid}/orders", async (Guid id, int? page, ClaimsPrincipal p, OrderService s, CancellationToken ct) => Results.Ok(await s.List(p.Actor(), id, page ?? 1, 50, ct)));
        stores.MapGet("/{id:guid}/report", async (Guid id, DateTimeOffset? from, DateTimeOffset? to, ClaimsPrincipal p, OperationsService s, CancellationToken ct) => Results.Ok(await s.Report(p.Actor(), id, from, to, null, null, null, ct)));
        var cart = api.MapGroup("/cart").RequireAuthorization();
        cart.MapGet("/", async (string? coupon, ClaimsPrincipal p, CartService s, CancellationToken ct) => Results.Ok(await s.Read(p.Actor(), coupon, ct)));
        cart.MapPut("/", async (CartInput input, ClaimsPrincipal p, CartService s, CancellationToken ct) => Results.Ok(await s.Save(p.Actor(), input, ct)));
        cart.MapDelete("/", async (ClaimsPrincipal p, CartService s, CancellationToken ct) => { await s.Save(p.Actor(), new([]), ct); return Results.NoContent(); });
        var orders = api.MapGroup("/orders").RequireAuthorization();
        orders.MapPost("/", async (CheckoutRequest input, HttpContext ctx, OrderService s, CancellationToken ct) => Results.Ok(await s.Checkout(ctx.User.Actor(), input, ctx.Request.Headers["Idempotency-Key"].ToString(), ct)));
        orders.MapGet("/", async (int? page, ClaimsPrincipal p, OrderService s, CancellationToken ct) => Results.Ok(await s.List(p.Actor(), null, page ?? 1, 50, ct)));
        orders.MapGet("/by-key/{key}", async (string key, ClaimsPrincipal p, OrderService s, CancellationToken ct) => Results.Ok(await s.ByKey(p.Actor(), key, ct)));
        orders.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal p, OrderService s, CancellationToken ct) => Results.Ok(await s.Get(p.Actor(), id, ct)));
        orders.MapGet("/{id:guid}/events", async (Guid id, ClaimsPrincipal p, OrderService s, Access a, CancellationToken ct) => { var o = await s.Find(id, ct); await a.ReadOrder(p.Actor(), o, ct); return Results.Ok(o.Events.OrderBy(x => x.Id).Select(x => new OrderEventDto(x.Previous, x.Next, x.Reason, x.At))); });
        orders.MapPost("/{id:guid}/transition", async (Guid id, TransitionRequest input, ClaimsPrincipal p, OrderService s, CancellationToken ct) => Results.Ok(await s.Transition(p.Actor(), id, input, ct)));
        orders.MapPost("/{id:guid}/handover", async (Guid id, ClaimsPrincipal p, OrderService s, CancellationToken ct) => Results.Ok(await s.Handover(p.Actor(), id, ct)));
        orders.MapPost("/{id:guid}/cancel", async (Guid id, ReasonRequest input, ClaimsPrincipal p, OrderService s, CancellationToken ct) => Results.Ok(await s.Cancel(p.Actor(), id, input.Reason, ct)));
        orders.MapPost("/{id:guid}/refund", async (Guid id, RefundInput input, HttpContext ctx, OrderService s, CancellationToken ct) => Results.Ok(await s.Refund(ctx.User.Actor(), id, input, ctx.Request.Headers["Idempotency-Key"].ToString(), ct)));
        orders.MapPost("/{id:guid}/review", async (Guid id, ReviewInput input, ClaimsPrincipal p, OperationsService s, CancellationToken ct) => { await s.Review(p.Actor(), id, input, ct); return Results.NoContent(); });
        orders.MapGet("/{id:guid}/tracking", async (Guid id, ClaimsPrincipal p, DeliveryService s, CancellationToken ct) => Results.Ok(await s.Tracking(p.Actor(), id, ct)));
        var courier = api.MapGroup("/courier").RequireAuthorization(p => p.RequireRole(Roles.Courier));
        courier.MapGet("/profile", async (ClaimsPrincipal p, AppDb db, CancellationToken ct) => { var c = await db.Couriers.SingleAsync(x => x.UserId == p.Actor().Id, ct); var u = await db.Users.SingleAsync(x => x.Id == c.UserId, ct); return Results.Ok(new CourierDto(c.UserId, u.DisplayName, c.Approved, c.Available, c.Vehicle)); });
        courier.MapPut("/availability", async (AvailabilityInput input, ClaimsPrincipal p, DeliveryService s, CancellationToken ct) => { await s.SetAvailability(p.Actor(), input, ct); return Results.NoContent(); });
        courier.MapGet("/offers", async (ClaimsPrincipal p, DeliveryService s, CancellationToken ct) => Results.Ok(await s.Offers(p.Actor(), ct)));
        courier.MapPost("/offers/{id:guid}/accept", async (Guid id, ClaimsPrincipal p, DeliveryService s, CancellationToken ct) => { await s.Accept(p.Actor(), id, true, ct); return Results.NoContent(); });
        courier.MapPost("/offers/{id:guid}/decline", async (Guid id, ClaimsPrincipal p, DeliveryService s, CancellationToken ct) => { await s.Accept(p.Actor(), id, false, ct); return Results.NoContent(); });
        courier.MapGet("/deliveries", async (ClaimsPrincipal p, DeliveryService s, CancellationToken ct) => Results.Ok(await s.History(p.Actor(), ct)));
        courier.MapPost("/deliveries/{id:guid}/stage", async (Guid id, DeliveryInput input, ClaimsPrincipal p, DeliveryService s, CancellationToken ct) => { await s.Advance(p.Actor(), id, input, ct); return Results.NoContent(); });
        courier.MapPost("/deliveries/{id:guid}/location", async (Guid id, LocationInput input, ClaimsPrincipal p, DeliveryService s, IHubContext<OrderHub> hub, CancellationToken ct) => { await s.UpdateLocation(p.Actor(), id, input, ct); await hub.Clients.Group("order:" + id).SendAsync("LocationChanged", new { id }, ct); return Results.NoContent(); });
        var support = api.MapGroup("/support").RequireAuthorization();
        support.MapPost("/", async (TicketInput input, ClaimsPrincipal p, OperationsService s, CancellationToken ct) => Results.Ok(new { id = await s.Ticket(p.Actor(), input, ct) }));
        support.MapGet("/", async (int? page, ClaimsPrincipal p, OperationsService s, CancellationToken ct) => Results.Ok(await s.Tickets(p.Actor(), page ?? 1, ct)));
        support.MapPost("/{id:guid}/resolve", async (Guid id, ResolveTicketInput input, ClaimsPrincipal p, OperationsService s, CancellationToken ct) => { await s.Resolve(p.Actor(), id, input.Resolution, ct); return Results.NoContent(); });
        var admin = api.MapGroup("/admin").RequireAuthorization(p => p.RequireRole(Roles.Admin));
        admin.MapGet("/accounts", async (int? page, string? q, ClaimsPrincipal p, OperationsService s, CancellationToken ct) => Results.Ok(await s.Accounts(p.Actor(), page ?? 1, q, ct)));
        admin.MapPost("/accounts/{id}/block", async (string id, BlockInput input, ClaimsPrincipal p, OperationsService s, CancellationToken ct) => { await s.Block(p.Actor(), id, input, ct); return Results.NoContent(); });
        admin.MapPost("/accounts/{id}/roles", async (string id, RoleInput input, ClaimsPrincipal p, OperationsService s, CancellationToken ct) => { await s.Grant(p.Actor(), id, input, ct); return Results.NoContent(); });
        admin.MapGet("/couriers", async (AppDb db, CancellationToken ct) => Results.Ok(await (from c in db.Couriers join u in db.Users on c.UserId equals u.Id select new CourierDto(c.UserId, u.DisplayName, c.Approved, c.Available, c.Vehicle)).Take(100).ToListAsync(ct)));
        admin.MapPost("/couriers/{id}/approval", async (string id, ApprovalInput input, ClaimsPrincipal p, OperationsService s, CancellationToken ct) => { await s.Approve(p.Actor(), id, input, ct); return Results.NoContent(); });
        admin.MapGet("/coupons", async (AppDb db, CancellationToken ct) => Results.Ok((await db.Coupons.OrderBy(x => x.Code).Take(100).ToListAsync(ct)).Select(Dto.Coupon)));
        admin.MapPut("/coupons", async (CouponInput input, ClaimsPrincipal p, OperationsService s, CancellationToken ct) => { await s.SaveCoupon(p.Actor(), input, ct); return Results.NoContent(); });
        admin.MapGet("/report", async (Guid? store, DateTimeOffset? from, DateTimeOffset? to, string? status, string? payment, string? region, ClaimsPrincipal p, OperationsService s, CancellationToken ct) => Results.Ok(await s.Report(p.Actor(), store, from, to, status, payment, region, ct)));
        admin.MapGet("/audit", async (int? page, AppDb db, CancellationToken ct) => Results.Ok(await db.Audit.OrderByDescending(x => x.Id).Skip((Math.Max(1, page ?? 1) - 1) * 50).Take(50).Select(x => new AuditDto(x.Id, x.ActorId, x.Action, x.Resource, x.Reason, x.At)).ToListAsync(ct)));
        admin.MapGet("/settings", async (AppDb db, CancellationToken ct) => { var s = await db.Settings.SingleAsync(ct); return Results.Ok(new SettingsInput(s.ServiceFee, s.RestaurantResponseMinutes, s.CourierWaitMinutes, s.OfferSeconds)); });
        admin.MapPut("/settings", async (SettingsInput input, ClaimsPrincipal p, AppDb db, CancellationToken ct) => { var s = await db.Settings.SingleAsync(ct); s.ServiceFee = input.ServiceFee; s.RestaurantResponseMinutes = input.RestaurantResponseMinutes; s.CourierWaitMinutes = input.CourierWaitMinutes; s.OfferSeconds = input.OfferSeconds; db.AuditAction(p.Actor().Id, "settings.save", "platform"); await db.SaveChangesAsync(ct); return Results.NoContent(); });
        admin.MapPost("/restaurants/{id:guid}/commission", async (Guid id, CommissionRequest input, ClaimsPrincipal p, AppDb db, CancellationToken ct) => { var s = await db.Restaurants.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new RuleException("not_found", "Loja não encontrada.", 404); s.CommissionRate = input.Rate; db.AuditAction(p.Actor().Id, "commission.save", id.ToString(), input.Reason); await db.SaveChangesAsync(ct); return Results.NoContent(); });
        api.MapPost("/payments/development/webhook", async (HttpContext ctx, OrderService s, IConfiguration config, CancellationToken ct) =>
        {
            var secret = config["Payments:WebhookSecret"] ?? throw new RuleException("webhook_unconfigured", "Configure o segredo do webhook de demonstração.", 503);
            using var reader = new StreamReader(ctx.Request.Body);
            var body = await reader.ReadToEndAsync(ct);
            var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body));
            byte[] signature;
            try
            {
                signature = Convert.FromHexString(ctx.Request.Headers["X-Brasa-Signature"].ToString());
            }
            catch (FormatException) { throw new RuleException("signature", "Assinatura inválida.", 401); }
            if (signature.Length != expected.Length || !CryptographicOperations.FixedTimeEquals(signature, expected))
                throw new RuleException("signature", "Assinatura inválida.", 401);
            var input = System.Text.Json.JsonSerializer.Deserialize<DevWebhook>(body, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)) ?? throw new RuleException("webhook", "Corpo inválido.");
            if (input.EventId is null || input.Outcome is null)
                throw new RuleException("webhook", "Evento inválido.");
            await s.Webhook(input, ct);
            return Results.NoContent();
        }).RequireRateLimiting("auth");
    }
    static void Fill(CustomerAddress a, AddressInput i)
    {
        a.Label = i.Label;
        a.Street = i.Street;
        a.Number = i.Number;
        a.District = i.District;
        a.City = i.City;
        a.State = i.State;
        a.PostalCode = i.PostalCode;
        a.Complement = i.Complement;
        a.Reference = i.Reference;
        a.Latitude = i.Latitude;
        a.Longitude = i.Longitude;
    }
}
