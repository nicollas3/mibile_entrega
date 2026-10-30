using Brasa.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
namespace Brasa.Infrastructure;

public static class DemoSeed
{
    public static readonly Guid FirstStore = Guid.Parse("10000000-0000-0000-0000-000000000001"), SecondStore = Guid.Parse("10000000-0000-0000-0000-000000000002");
    public static async Task Run(AppDb db, UserManager<AppUser> users, RoleManager<IdentityRole> roles, IHostEnvironment env, IConfiguration config)
    {
        foreach (var name in Roles.All)
        if (!await roles.RoleExistsAsync(name))
            Check(await roles.CreateAsync(new(name)));
        if (!await db.Settings.AnyAsync())
        {
            db.Settings.Add(new());
            await db.SaveChangesAsync();
        }
        if (!env.IsDevelopment() || !config.GetValue<bool>("Demo:Seed"))
            return;
        var password = config["Demo:Password"] ?? throw new InvalidOperationException("Defina Demo__Password para criar os dados fictícios.");
        foreach (var (id, name, role) in new[] { ("cliente", "Cliente de demonstração", Roles.Customer), ("outro", "Outro cliente fictício", Roles.Customer), ("loja", "Gestor da Cozinha Aurora", Roles.Owner), ("loja2", "Gestor do Verde no Pote", Roles.Owner), ("entregador", "Entregador fictício", Roles.Courier), ("entregador2", "Segundo entregador fictício", Roles.Courier), ("admin", "Administração local", Roles.Admin), ("suporte", "Atendimento local", Roles.Support), ("funcionario", "Equipe local", Roles.Employee) })
        {
            var email = $"{id}@brasa.local";
            if (await users.FindByEmailAsync(email) != null)
                continue;
            var user = new AppUser { Id = "demo-" + id, Email = email, UserName = email, DisplayName = name, EmailConfirmed = true };
            Check(await users.CreateAsync(user, password));
            Check(await users.AddToRoleAsync(user, role));
            if (role == Roles.Courier)
                db.Couriers.Add(new()
                {
                    UserId = user.Id,
                    Approved = true,
                    Available = true
                });
        }
        if (!await db.Settings.AnyAsync())
            db.Settings.Add(new());
        if (!await db.Restaurants.AnyAsync())
        {
            var r = new Restaurant { Id = FirstStore, Name = "Cozinha Aurora", Category = "Comida brasileira", Description = "Comida de casa, feita com calma. Loja e dados inteiramente fictícios.", Address = "Rua de Demonstração, 100 — São Paulo/SP", Latitude = -23.5505, Longitude = -46.6333, Products = [new() { Name = "Bowl da horta", Description = "Arroz integral, legumes assados, feijão e molho de ervas.", Category = "Pratos", Allergens = "Pode conter soja e leite.", Price = 29.90m, Options = [new() { Name = "Queijo da casa", Price = 4 }, new() { Name = "Porção extra de legumes", Price = 6 }] }, new() { Name = "Frango da casa", Description = "Frango grelhado, arroz, feijão e salada.", Category = "Pratos", Allergens = "Contém leite.", Price = 34.90m }, new() { Name = "Suco de laranja", Description = "300 ml, preparado na hora.", Category = "Bebidas", Allergens = "Sem alérgenos declarados.", Price = 9.50m }] };
            var r2 = new Restaurant { Id = SecondStore, Name = "Verde no Pote", Category = "Vegetariana", Description = "Receitas frescas para a sua rotina. Estabelecimento fictício.", Address = "Alameda Fictícia, 200 — São Paulo/SP", Latitude = -23.555, Longitude = -46.64, DeliveryFee = 5, Products = [new() { Name = "Salada colorida", Description = "Folhas, grãos e legumes da estação.", Category = "Saladas", Allergens = "Contém castanhas.", Price = 27 }, new() { Name = "Sanduíche de cogumelos", Description = "Pão artesanal, cogumelos e rúcula.", Category = "Lanches", Allergens = "Contém glúten.", Price = 25 }] };
            db.Restaurants.AddRange(r, r2);
            db.Memberships.AddRange(new()
            {
                RestaurantId = r.Id,
                UserId = "demo-loja",
                Permissions = StorePermission.All
            }, new()
            {
                RestaurantId = r2.Id,
                UserId = "demo-loja2",
                Permissions = StorePermission.All
            }, new()
            {
                RestaurantId = r.Id,
                UserId = "demo-funcionario",
                Permissions = StorePermission.Orders
            });
            foreach (var user in new[] { "demo-cliente", "demo-outro" })
                db.Addresses.Add(new()
                {
                    UserId = user,
                    Label = "Casa fictícia",
                    Street = "Rua Exemplo",
                    Number = "123",
                    District = "Centro",
                    City = "São Paulo",
                    State = "SP",
                    PostalCode = "01001000",
                    Latitude = -23.551,
                    Longitude = -46.635
                });
            db.Coupons.Add(new()
            {
                Code = "BEMVINDO",
                Discount = 8,
                MinimumSubtotal = 25,
                ExpiresAt = DateTimeOffset.UtcNow.AddMonths(3)
            });
        }
        await db.SaveChangesAsync();
        // Apenas pedidos fictícios para o painel local; nunca cobrados ou enviados a terceiros.
        if (!await db.Orders.AnyAsync())
        {
            var store = await db.Restaurants.Include(x => x.Products).SingleAsync(x => x.Id == FirstStore);
            foreach (var status in new[] { OrderStatus.SentToRestaurant, OrderStatus.Preparing, OrderStatus.Delivered })
            {
                var p = store.Products[0];
                var item = new Brasa.Contracts.ItemSnapshot(p.Id, p.Name, p.Price, 1, "Pedido fictício de demonstração", []);
                var o = new Order { CustomerId = "demo-cliente", RestaurantId = store.Id, RestaurantName = store.Name, AddressSnapshot = "Rua Exemplo, 123 — endereço fictício", Latitude = -23.551, Longitude = -46.635, ItemsSnapshot = Dto.Json(new[] { item }), Prices = PriceBreakdown.Calculate(p.Price, 0, 7, 2.5m, .12m), IdempotencyKey = "seed-" + status, RequestHash = "demo", PaymentReference = "DEMO-SEED", PaymentMethod = "demo-approved" };
                o.Transition(OrderStatus.SentToRestaurant, "seed:development");
                if (status != OrderStatus.SentToRestaurant)
                {
                    o.Transition(OrderStatus.Accepted, "seed:development");
                    o.Transition(OrderStatus.Preparing, "seed:development");
                }
                if (status == OrderStatus.Delivered)
                {
                    o.Transition(OrderStatus.Ready, "seed:development");
                    o.Transition(OrderStatus.CourierAssigned, "seed:development");
                    o.Transition(OrderStatus.InTransit, "seed:development");
                    o.Transition(OrderStatus.Delivered, "seed:development");
                }
                db.Orders.Add(o);
            }
            await db.SaveChangesAsync();
        }
    }
    static void Check(IdentityResult result)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException("Falha ao preparar contas de demonstração: " + string.Join("; ", result.Errors.Select(e => e.Code)));
    }
}
