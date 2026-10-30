using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Brasa.Contracts;
using Brasa.Domain;
using Brasa.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
namespace Brasa.Tests;

[Collection("database")]
public sealed class FlowTests(ApiFixture fixture)
{
    [Fact]
    public async Task Reports_accept_Brazilian_timezone_offsets()
    {
        var (client, _) = await Login("admin");
        using var c = client;
        var from = DateTimeOffset.UtcNow.AddDays(-1).ToOffset(TimeSpan.FromHours(-3));
        var to = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-3));
        var report = await c.GetFromJsonAsync<ReportDto>($"/api/admin/report?from={Uri.EscapeDataString(from.ToString("O"))}&to={Uri.EscapeDataString(to.ToString("O"))}");
        Assert.True(report!.Orders >= 3);
    }
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    async Task<(HttpClient Client, AuthDto Auth)> Login(string name)
    {
        var c = fixture.CreateClient();
        var r = await c.PostAsJsonAsync("/api/auth/login", new LoginRequest(name + "@brasa.local", ApiFixture.Password));
        r.EnsureSuccessStatusCode();
        var a = (await r.Content.ReadFromJsonAsync<AuthDto>())!;
        c.DefaultRequestHeaders.Authorization = new("Bearer", a.AccessToken);
        return (c, a);
    }
    async Task<HttpClient> Customer()
    {
        var c = fixture.CreateClient();
        var r = await c.PostAsJsonAsync("/api/auth/register", new RegisterRequest(Guid.NewGuid() + "@example.test", ApiFixture.Password, "Cliente de teste"));
        r.EnsureSuccessStatusCode();
        var a = (await r.Content.ReadFromJsonAsync<AuthDto>())!;
        c.DefaultRequestHeaders.Authorization = new("Bearer", a.AccessToken);
        return c;
    }
    async Task<CheckoutRequest> Prepare(HttpClient c, string method = "demo-approved", string? coupon = null)
    {
        await c.DeleteAsync("/api/cart");
        var menu = (await c.GetFromJsonAsync<List<ProductDto>>($"/api/restaurants/{DemoSeed.FirstStore}/menu"))!;
        var p = menu.First(x => x.Price >= 25);
        var cart = await c.PutAsJsonAsync("/api/cart", new CartInput([new(p.Id, 1, "Sem talheres", [])]));
        cart.EnsureSuccessStatusCode();
        var adr = await c.PostAsJsonAsync("/api/me/addresses", new AddressInput("Teste", "Rua Fictícia", "1", "Centro", "São Paulo", "SP", "01001000", "", "", -23.551, -46.635));
        adr.EnsureSuccessStatusCode();
        var address = (await adr.Content.ReadFromJsonAsync<AddressDto>())!;
        return new(address.Id, coupon, method);
    }
    static async Task<OrderDto> Create(HttpClient c, CheckoutRequest input, string? key = null)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/orders") { Content = JsonContent.Create(input) };
        req.Headers.Add("Idempotency-Key", key ?? Guid.NewGuid().ToString("N"));
        var r = await c.SendAsync(req);
        Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadFromJsonAsync<OrderDto>())!;
    }
    static async Task<OrderDto> Step(HttpClient c, OrderDto o, string next)
    {
        var r = await c.PostAsJsonAsync($"/api/orders/{o.Id}/transition", new TransitionRequest(next, o.Version, "Operação de teste"));
        Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadFromJsonAsync<OrderDto>())!;
    }
    [Fact]
    public async Task Anonymous_protected_routes_and_customer_privilege_escalation_are_denied()
    {
        using var anonymous = fixture.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/orders")).StatusCode);
        using var customer = await Customer();
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.GetAsync("/api/admin/accounts")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.GetAsync("/api/courier/offers")).StatusCode);
    }
    [Fact]
    public async Task Shared_client_search_omits_empty_optional_filters()
    {
        var api = new Brasa.Client.ApiClient(fixture.CreateClient(), new Brasa.Client.MemoryTokenStore());
        var found = await api.Get<PageDto<RestaurantDto>>("api/restaurants?q=&category=&open=false&maxFee=&maxMinutes=&maxPrice=&minRating=&page=1");
        Assert.True(found.Total >= 2);
    }
    [Fact]
    public async Task Cart_rejects_mixed_restaurants_and_keeps_previous_cart()
    {
        using var c = await Customer();
        await Prepare(c);
        var p = (await c.GetFromJsonAsync<List<ProductDto>>($"/api/restaurants/{DemoSeed.SecondStore}/menu"))![0];
        Assert.Equal(HttpStatusCode.Conflict, (await c.PutAsJsonAsync("/api/cart", new CartInput([new(p.Id, 1, "", [])]))).StatusCode);
        Assert.Equal(DemoSeed.FirstStore, (await c.GetFromJsonAsync<CartDto>("/api/cart"))!.RestaurantId);
        var invalid = await c.PutAsJsonAsync("/api/cart", new CartInput([new(p.Id, 0, "", [])]));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }
    [Fact]
    public async Task Concurrent_checkout_is_idempotent_and_mismatched_replay_is_rejected()
    {
        using var c = await Customer();
        var input = await Prepare(c, coupon: "BEMVINDO");
        var key = Guid.NewGuid().ToString("N");
        var results = await Task.WhenAll(Create(c, input, key), Create(c, input, key));
        Assert.Equal(results[0].Id, results[1].Id);
        Assert.Equal(8, results[0].Prices.Discount);
        Assert.Empty((await c.GetFromJsonAsync<CartDto>("/api/cart"))!.Items);
        Assert.Equal(results[0].Id, (await c.GetFromJsonAsync<OrderDto>("/api/orders/by-key/" + key))!.Id);
        using var foreign = await Customer();
        Assert.Equal(HttpStatusCode.NotFound, (await foreign.GetAsync("/api/orders/by-key/" + key)).StatusCode);
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/orders") { Content = JsonContent.Create(input with { PaymentMethod = "demo-declined" }) };
        req.Headers.Add("Idempotency-Key", key);
        Assert.Equal(HttpStatusCode.Conflict, (await c.SendAsync(req)).StatusCode);
    }
    [Fact]
    public async Task Restaurant_isolation_versions_state_machine_and_price_snapshots_work()
    {
        using var customer = await Customer();
        var o = await Create(customer, await Prepare(customer));
        var (owner, oa) = await Login("loja");
        using var own = owner;
        var (foreign, fa) = await Login("loja2");
        using var other = foreign;
        Assert.Equal(HttpStatusCode.Forbidden, (await other.GetAsync($"/api/orders/{o.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.PostAsJsonAsync($"/api/orders/{o.Id}/transition", new TransitionRequest("Accepted", o.Version, null))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await own.PostAsJsonAsync($"/api/orders/{o.Id}/transition", new TransitionRequest("Ready", o.Version, null))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await own.PostAsJsonAsync($"/api/orders/{o.Id}/transition", new TransitionRequest("Accepted", Guid.NewGuid(), null))).StatusCode);
        var accepted = await Step(own, o, "Accepted");
        await Step(own, accepted, "Preparing");
        Assert.Equal(HttpStatusCode.Conflict, (await customer.PostAsJsonAsync($"/api/orders/{o.Id}/cancel", new ReasonRequest("Desistência após preparo"))).StatusCode);
        var menu = (await own.GetFromJsonAsync<List<ProductDto>>($"/api/restaurants/{o.RestaurantId}/menu"))!;
        var product = menu.Single(x => x.Id == o.Items[0].ProductId);
        var change = await own.PutAsJsonAsync($"/api/stores/{o.RestaurantId}/products/{product.Id}", new ProductInput(product.Name, product.Description, product.Category, product.Allergens, null, product.Price + 10, true, []));
        change.EnsureSuccessStatusCode();
        var after = (await customer.GetFromJsonAsync<OrderDto>($"/api/orders/{o.Id}"))!;
        Assert.Equal(o.Prices.Total, after.Prices.Total);
        Assert.Equal(o.Items[0].UnitPrice, after.Items[0].UnitPrice);
        var restore = await own.PutAsJsonAsync($"/api/stores/{o.RestaurantId}/products/{product.Id}", new ProductInput(product.Name, product.Description, product.Category, product.Allergens, product.ImageUrl, product.Price, product.Available, product.Options.Select(x => new OptionInput(x.Name, x.Price)).ToList()));
        restore.EnsureSuccessStatusCode();
    }
    [Fact]
    public async Task Full_delivery_blocks_other_courier_and_location_after_completion()
    {
        using var customer = await Customer();
        var o = await Create(customer, await Prepare(customer));
        var (owner, _) = await Login("loja");
        using var own = owner;
        foreach (var next in new[] { "Accepted", "Preparing", "Ready" })
            o = await Step(own, o, next);
        using (var scope = fixture.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<DeliveryService>().Dispatch(CancellationToken.None);
        var (driver, _) = await Login("entregador");
        using var courier = driver;
        var (second, _) = await Login("entregador2");
        using var foreign = second;
        var offers = (await courier.GetFromJsonAsync<List<OfferDto>>("/api/courier/offers"))!;
        var offer = offers.Single(x => x.OrderId == o.Id);
        (await courier.PostAsync($"/api/courier/offers/{offer.Id}/accept", null)).EnsureSuccessStatusCode();
        var ds = (await courier.GetFromJsonAsync<List<DeliveryDto>>("/api/courier/deliveries"))!.Single(x => x.OrderId == o.Id);
        Assert.Equal(HttpStatusCode.Forbidden, (await foreign.GetAsync($"/api/orders/{o.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await foreign.PostAsJsonAsync($"/api/courier/deliveries/{o.Id}/stage", new DeliveryInput("GoingToRestaurant", ds.Version))).StatusCode);
        foreach (var next in new[] { "GoingToRestaurant", "AtRestaurant", "PickedUp", "GoingToCustomer", "Delivered" })
        {
            if (next == "PickedUp")
            {
                Assert.Equal(HttpStatusCode.Conflict, (await courier.PostAsJsonAsync($"/api/courier/deliveries/{o.Id}/stage", new DeliveryInput(next, ds.Version))).StatusCode);
                (await own.PostAsync($"/api/orders/{o.Id}/handover", null)).EnsureSuccessStatusCode();
            }
            (await courier.PostAsJsonAsync($"/api/courier/deliveries/{o.Id}/stage", new DeliveryInput(next, ds.Version))).EnsureSuccessStatusCode();
            ds = (await courier.GetFromJsonAsync<List<DeliveryDto>>("/api/courier/deliveries"))!.Single(x => x.OrderId == o.Id);
            if (next == "PickedUp")
            {
                (await courier.PostAsJsonAsync($"/api/courier/deliveries/{o.Id}/location", new LocationInput(-23.55, -46.63, DateTimeOffset.UtcNow))).EnsureSuccessStatusCode();
                ds = (await courier.GetFromJsonAsync<List<DeliveryDto>>("/api/courier/deliveries"))!.Single(x => x.OrderId == o.Id);
            }
        }
        Assert.Equal("Delivered", (await customer.GetFromJsonAsync<OrderDto>($"/api/orders/{o.Id}"))!.Status);
        Assert.Equal(HttpStatusCode.Conflict, (await courier.PostAsJsonAsync($"/api/courier/deliveries/{o.Id}/location", new LocationInput(-23.55, -46.63, DateTimeOffset.UtcNow))).StatusCode);
        Assert.Null((await customer.GetFromJsonAsync<TrackingDto>($"/api/orders/{o.Id}/tracking"))!.Latitude);
        (await customer.PostAsJsonAsync($"/api/orders/{o.Id}/review", new ReviewInput(5, 4, "Ótima demonstração"))).EnsureSuccessStatusCode();
        Assert.Equal(4, (await courier.GetFromJsonAsync<List<DeliveryDto>>("/api/courier/deliveries"))!.Single(x => x.OrderId == o.Id).Rating);
        Assert.Equal(HttpStatusCode.Conflict, (await customer.PostAsJsonAsync($"/api/orders/{o.Id}/review", new ReviewInput(5, 4, "Duplicada"))).StatusCode);
    }
    [Fact]
    public async Task Declined_payment_is_not_sent_to_restaurant()
    {
        using var c = await Customer();
        var o = await Create(c, await Prepare(c, "demo-declined"));
        Assert.Equal("PaymentDeclined", o.Status);
        var (owner, _) = await Login("loja");
        using var own = owner;
        Assert.Equal(HttpStatusCode.Conflict, (await own.PostAsJsonAsync($"/api/orders/{o.Id}/transition", new TransitionRequest("Accepted", o.Version, null))).StatusCode);
    }
    [Fact]
    public async Task Signed_webhook_replay_and_partial_refund_are_idempotent()
    {
        using var c = await Customer();
        var o = await Create(c, await Prepare(c, "demo-pending"));
        using var anon = fixture.CreateClient();
        var input = new DevWebhook(Guid.NewGuid().ToString("N"), o.Id, "approved");
        var body = JsonSerializer.Serialize(input, Json);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsync("/api/payments/development/webhook", new StringContent(body, Encoding.UTF8, "application/json"))).StatusCode);
        async Task Send()
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "/api/payments/development/webhook") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            req.Headers.Add("X-Brasa-Signature", Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(ApiFixture.WebhookSecret), Encoding.UTF8.GetBytes(body))));
            (await anon.SendAsync(req)).EnsureSuccessStatusCode();
        }
        await Send();
        await Send();
        o = (await c.GetFromJsonAsync<OrderDto>($"/api/orders/{o.Id}"))!;
        Assert.Equal("SentToRestaurant", o.Status);
        var (admin, _) = await Login("admin");
        using var a = admin;
        string key = Guid.NewGuid().ToString("N");
        async Task<OrderDto> Refund(decimal amount)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"/api/orders/{o.Id}/refund") { Content = JsonContent.Create(new RefundInput(amount, "Reembolso parcial de demonstração")) };
            req.Headers.Add("Idempotency-Key", key);
            var r = await a.SendAsync(req);
            r.EnsureSuccessStatusCode();
            return (await r.Content.ReadFromJsonAsync<OrderDto>())!;
        }
        var first = await Refund(5);
        var secondRefund = await Refund(5);
        Assert.Equal(5, first.Refunded);
        Assert.Equal(first.Refunded, secondRefund.Refunded);
    }
    [Fact]
    public async Task Refresh_reuse_revokes_new_access_and_logout_invalidates_session()
    {
        var (c, a) = await Login("outro");
        using var client = c;
        using var anon = fixture.CreateClient();
        var r = await anon.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(a.RefreshToken));
        r.EnsureSuccessStatusCode();
        var next = (await r.Content.ReadFromJsonAsync<AuthDto>())!;
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(a.RefreshToken))).StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", next.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/me")).StatusCode);
        var (c2, _) = await Login("outro");
        using var second = c2;
        (await second.PostAsync("/api/auth/logout", null)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await second.GetAsync("/api/me")).StatusCode);
    }
    [Fact]
    public async Task Staff_permissions_support_limits_and_cross_user_addresses_are_enforced()
    {
        using var c = await Customer();
        var input = await Prepare(c);
        using var other = await Customer();
        await Prepare(other);
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/orders") { Content = JsonContent.Create(input) };
        req.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        Assert.Equal(HttpStatusCode.NotFound, (await other.SendAsync(req)).StatusCode);
        var (staff, _) = await Login("funcionario");
        using var s = staff;
        Assert.Equal(HttpStatusCode.OK, (await s.GetAsync($"/api/stores/{DemoSeed.FirstStore}/orders")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await s.GetAsync($"/api/stores/{DemoSeed.FirstStore}/members")).StatusCode);
        var (support, _) = await Login("suporte");
        using var help = support;
        Assert.Equal(HttpStatusCode.Forbidden, (await help.GetAsync("/api/admin/accounts")).StatusCode);
    }
    [Fact]
    public async Task Password_reset_revokes_sessions_and_accepts_only_valid_token()
    {
        using var c = await Customer();
        var profile = (await c.GetFromJsonAsync<UserDto>("/api/me"))!;
        using var anon = fixture.CreateClient();
        (await anon.PostAsJsonAsync("/api/auth/forgot-password", new ForgotRequest(profile.Email))).EnsureSuccessStatusCode();
        var email = fixture.Services.GetRequiredService<TestEmail>();
        Assert.True(email.Tokens.TryGetValue(profile.Email, out var token));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await anon.PostAsJsonAsync("/api/auth/reset-password", new ResetRequest(profile.Email, "invalid", ApiFixture.Password + "2"))).StatusCode);
        (await anon.PostAsJsonAsync("/api/auth/reset-password", new ResetRequest(profile.Email, token!, ApiFixture.Password + "2"))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/me")).StatusCode);
        (await anon.PostAsJsonAsync("/api/auth/login", new LoginRequest(profile.Email, ApiFixture.Password + "2"))).EnsureSuccessStatusCode();
    }
    [Fact]
    public async Task Invalid_email_missing_fields_and_unknown_coupon_are_rejected()
    {
        using var anon = fixture.CreateClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await anon.PostAsJsonAsync("/api/auth/register", new RegisterRequest("bad-email", ApiFixture.Password, "Teste"))).StatusCode);
        using var c = await Customer();
        await Prepare(c);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await c.GetAsync("/api/cart?coupon=INVALIDO")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsJsonAsync("/api/cart", new
        {
            items = (object?)null
        })).StatusCode);
    }
    [Fact]
    public async Task Restaurant_becoming_unavailable_after_payment_approval_queues_refund()
    {
        using var c = await Customer();
        var o = await Create(c, await Prepare(c, "demo-pending"));
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDb>();
            var store = await db.Restaurants.SingleAsync(x => x.Id == DemoSeed.FirstStore);
            store.Status = StoreStatus.Paused;
            await db.SaveChangesAsync();
            try
            {
                await scope.ServiceProvider.GetRequiredService<OrderService>().Webhook(new(Guid.NewGuid().ToString("N"), o.Id, "approved"), CancellationToken.None);
            }
            finally { store.Status = StoreStatus.Open; await db.SaveChangesAsync(); }
        }
        var after = (await c.GetFromJsonAsync<OrderDto>($"/api/orders/{o.Id}"))!;
        Assert.Equal("Cancelled", after.Status);
        Assert.Equal("Pending", after.RefundStatus);
    }
    [Fact]
    public async Task Pending_payment_timeout_cancels_without_refund()
    {
        using var c = await Customer();
        var o = await Create(c, await Prepare(c, "demo-pending"));
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDb>();
            var order = await db.Orders.SingleAsync(x => x.Id == o.Id);
            order.CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-31);
            await db.SaveChangesAsync();
            await scope.ServiceProvider.GetRequiredService<DeliveryService>().ExpireOrders(CancellationToken.None);
        }
        var after = (await c.GetFromJsonAsync<OrderDto>($"/api/orders/{o.Id}"))!;
        Assert.Equal("Cancelled", after.Status);
        Assert.Equal("None", after.RefundStatus);
    }
    [Fact]
    public async Task Account_block_immediately_revokes_existing_access()
    {
        using var c = await Customer();
        var profile = (await c.GetFromJsonAsync<UserDto>("/api/me"))!;
        var (admin, _) = await Login("admin");
        using var a = admin;
        (await a.PostAsJsonAsync($"/api/admin/accounts/{profile.Id}/block", new BlockInput(true, "Bloqueio de teste autorizado"))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/me")).StatusCode);
    }
    [Fact]
    public async Task Authentication_endpoint_enforces_rate_limit()
    {
        await using var limited = fixture.WithWebHostBuilder(b => b.UseSetting("RateLimits:AuthPerMinute", "25"));
        using var client = limited.CreateClient();
        for (int i = 0; i < 25; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("inexistente@example.test", ApiFixture.Password))).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("inexistente@example.test", ApiFixture.Password))).StatusCode);
    }
}
