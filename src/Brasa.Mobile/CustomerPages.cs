using Brasa.Client;
using Brasa.Contracts;
using Microsoft.AspNetCore.SignalR.Client;
namespace Brasa.Mobile;

public sealed class CustomerPage : UiPage
{
    readonly Entry query, category, fee, minutes, price, rating; readonly Switch open = new(); readonly VerticalStackLayout results = new() { Spacing = 15 }; int page = 1;
    public CustomerPage(ApiClient api) : base(api, "Brasa · cliente")
    {
        Text("Seu próximo sabor está por aqui.", true);
        query = Field("Restaurante ou prato");
        category = Field("Categoria (opcional)");
        fee = Field("Taxa máxima de entrega (R$)", keyboard: Keyboard.Numeric);
        minutes = Field("Preparo máximo (min)", keyboard: Keyboard.Numeric);
        price = Field("Preço máximo do prato (R$)", keyboard: Keyboard.Numeric);
        rating = Field("Avaliação mínima (1 a 5)", keyboard: Keyboard.Numeric);
        Text("Apenas lojas abertas");
        Body.Add(open);
        Action("Buscar cozinhas", async () => { page = 1; await Search(); });
        Action("Próxima página", async () => { page++; await Search(); });
        Body.Add(results);
        Action("Meu carrinho", () => Navigation.PushAsync(new CartPage(Api)));
        Action("Meus pedidos", () => Navigation.PushAsync(new CustomerOrdersPage(Api)));
        Action("Minha conta e endereços", () => Navigation.PushAsync(new ProfilePage(Api)));
        Action("Sair", SignOut);
    }
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await Run(Search);
    }
    async Task Search()
    {
        string Numeric(Entry e) => e.Text?.Replace(',', '.') ?? "";
        var list = await Api.Get<PageDto<RestaurantDto>>($"api/restaurants?q={Uri.EscapeDataString(query.Text ?? "")}&category={Uri.EscapeDataString(category.Text ?? "")}&maxFee={Numeric(fee)}&maxMinutes={Numeric(minutes)}&maxPrice={Numeric(price)}&minRating={Numeric(rating)}&open={open.IsToggled}&page={page}");
        results.Clear();
        foreach (var r in list.Items)
        {
            var b = new Button { Text = $"{r.Name}\n{r.Category} · {Status(r.Status)}\n~{r.PreparationMinutes} min · {Money(r.DeliveryFee)} entrega · {(r.Rating?.ToString("F1") ?? "Novo")} ★", MinimumHeightRequest = 100, BackgroundColor = Color.FromArgb("244C3E"), TextColor = Colors.White };
            b.Clicked += async (s, e) => await Run(() => Navigation.PushAsync(new RestaurantPage(Api, r)));
            results.Add(b);
        }
        if (list.Items.Count == 0)
            results.Add(new Label { Text = "Nenhum restaurante encontrado. Ajuste os filtros ou volte à primeira página." });
    }
}
public sealed class RestaurantPage : UiPage
{
    readonly RestaurantDto restaurant;
    public RestaurantPage(ApiClient api, RestaurantDto r) : base(api, r.Name) { restaurant = r; }
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await Run(Load);
    }
    async Task Load()
    {
        var menu = await Api.Get<List<ProductDto>>($"api/restaurants/{restaurant.Id}/menu");
        ResetBody();
        Text(restaurant.Name, true);
        Text(restaurant.Description);
        Text($"{restaurant.Hours}\n{Status(restaurant.Status)} · Mínimo {Money(restaurant.MinimumOrder)}\nPreparo aproximado: {restaurant.PreparationMinutes} min");
        foreach (var p in menu)
        {
            Text($"{p.Category} · {p.Name}\n{Money(p.Price)} · {(p.Available ? "Disponível" : "Esgotado")}");
            Action("Ver " + p.Name, () => Navigation.PushAsync(new ProductPage(Api, p)));
        }
        var reviews = await Api.Get<List<ReviewDto>>($"api/restaurants/{restaurant.Id}/reviews");
        Text("Avaliações", true);
        foreach (var r in reviews)
            Text($"{r.RestaurantRating} ★ · {r.Comment}");
        if (reviews.Count == 0)
            Text("Esta cozinha ainda não recebeu avaliações.");
    }
}
public sealed class ProductPage : UiPage
{
    public ProductPage(ApiClient api, ProductDto p) : base(api, p.Name)
    {
        Text(p.Name, true);
        if (p.ImageUrl != null)
            Body.Add(new Image { Source = p.ImageUrl, HeightRequest = 180, Aspect = Aspect.AspectFill });
        Text(p.Description);
        Text($"{Money(p.Price)}\nAlérgenos informados pelo restaurante: {p.Allergens}");
        var choices = new Dictionary<Guid, CheckBox>();
        foreach (var option in p.Options)
        {
            Text($"{option.Name} + {Money(option.Price)}");
            var check = new CheckBox();
            SemanticProperties.SetDescription(check, option.Name);
            Body.Add(check);
            choices[option.Id] = check;
        }
        var quantity = Field("Quantidade", "1", Keyboard.Numeric);
        var notes = Field("Observações");
        Action("Adicionar ao carrinho", async () =>
        {
            if (!p.Available)
            {
                Feedback.Text = "Este item está esgotado.";
                return;
            }
            if (!int.TryParse(quantity.Text, out var n) || n is < 1 or > 50)
            {
                Feedback.Text = "Informe de 1 a 50 unidades.";
                return;
            }
            var cart = await Api.Get<CartDto>("api/cart");
            if (cart.RestaurantId.HasValue && cart.RestaurantId != p.RestaurantId)
            {
                Feedback.Text = "O carrinho é de outra loja. Esvazie o carrinho antes de trocar de restaurante.";
                return;
            }
            var lines = cart.Items.Where(x => x.ProductId != p.Id).Select(x => new CartLine(x.ProductId, x.Quantity, x.Notes, x.Options.Select(o => o.Id).ToList())).ToList();
            lines.Add(new(p.Id, n, notes.Text, choices.Where(x => x.Value.IsChecked).Select(x => x.Key).ToList()));
            await Api.Put<CartDto>("api/cart", new CartInput(lines));
            Feedback.Text = "Item salvo no carrinho.";
        });
        Action("Abrir carrinho", () => Navigation.PushAsync(new CartPage(Api)));
    }
}
public sealed class CartPage : UiPage
{
    string coupon = "", method = "demo-approved"; Guid selectedAddress; string key = Guid.NewGuid().ToString("N"), fingerprint = "";
    public CartPage(ApiClient api) : base(api, "Meu carrinho") { Action("Esvaziar carrinho", ClearCart); }
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await Run(Load);
    }
    async Task Load()
    {
        var storage = "brasa.checkout." + Api.Session!.User.Id;
        var pending = await SecureStorage.Default.GetAsync(storage);
        if (pending != null)
        {
            var parts = System.Text.Json.JsonSerializer.Deserialize<string[]>(pending)!;
            try
            {
                var previous = await Api.Get<OrderDto>("api/orders/by-key/" + Uri.EscapeDataString(parts[1]));
                SecureStorage.Default.Remove(storage);
                await Navigation.PushAsync(new CustomerOrderPage(Api, previous.Id));
                return;
            }
            catch (ApiException ex) when (ex.Status == 404) { }
        }
        var cart = await Api.Get<CartDto>("api/cart?coupon=" + Uri.EscapeDataString(coupon));
        var addresses = await Api.Get<List<AddressDto>>("api/me/addresses");
        ResetBody();
        Text("À sua mesa.", true);
        Action("Esvaziar carrinho", ClearCart);
        if (cart.Items.Count == 0)
        {
            Text("Seu carrinho está vazio.");
            return;
        }
        foreach (var line in cart.Items)
        {
            Text($"{line.Quantity} × {line.Name} · {Money((line.UnitPrice + line.Options.Sum(x => x.Price)) * line.Quantity)}\n{line.Notes}");
            Action("Remover " + line.Name, async () => { await Api.Put<CartDto>("api/cart", new CartInput(cart.Items.Where(x => x.ProductId != line.ProductId).Select(x => new CartLine(x.ProductId, x.Quantity, x.Notes, x.Options.Select(o => o.Id).ToList())).ToList())); await Load(); });
        }
        if (cart.Totals is { } t)
            Text($"Subtotal: {Money(t.Subtotal)}\nDesconto: − {Money(t.Discount)}\nEntrega: {Money(t.DeliveryFee)}\nServiço: {Money(t.ServiceFee)}\nTotal: {Money(t.Total)}", true);
        var couponEntry = Field("Cupom", coupon);
        Action("Aplicar cupom", async () => { coupon = couponEntry.Text ?? ""; await Load(); });
        Text("Endereço de entrega");
        var address = new Picker { Title = "Escolha o endereço", ItemsSource = addresses.Select(x => x.Label + " — " + x.Formatted).ToList() };
        Body.Add(address);
        if (addresses.Count > 0)
        {
            var i = addresses.FindIndex(x => x.Id == selectedAddress);
            address.SelectedIndex = i >= 0 ? i : 0;
            selectedAddress = addresses[address.SelectedIndex].Id;
        }
        address.SelectedIndexChanged += (s, e) => { if (address.SelectedIndex >= 0) selectedAddress = addresses[address.SelectedIndex].Id; };
        Action("Gerenciar endereços", () => Navigation.PushAsync(new AddressesPage(Api)));
        Text("Pagamento fictício — nenhuma cobrança real");
        var payment = new Picker { Title = "Resultado da simulação", ItemsSource = new List<string> { "Simular aprovação", "Simular recusa", "Aguardar webhook de demonstração" } };
        payment.SelectedIndex = method == "demo-approved" ? 0 : method == "demo-declined" ? 1 : 2;
        payment.SelectedIndexChanged += (s, e) => method = payment.SelectedIndex == 0 ? "demo-approved" : payment.SelectedIndex == 1 ? "demo-declined" : "demo-pending";
        Body.Add(payment);
        Action("Confirmar pedido de demonstração", async () =>
        {
            if (selectedAddress == Guid.Empty)
            {
                Feedback.Text = "Selecione ou cadastre um endereço.";
                return;
            }
            if (!await DisplayAlertAsync("Confirmar pedido?", "O pagamento é simulado. Nenhuma cobrança será feita.", "Confirmar", "Revisar"))
                return;
            var input = new CheckoutRequest(selectedAddress, coupon, method);
            var current = System.Text.Json.JsonSerializer.Serialize(input);
            var saved = await SecureStorage.Default.GetAsync(storage);
            if (saved != null)
            {
                var parts = System.Text.Json.JsonSerializer.Deserialize<string[]>(saved)!;
                if (parts[0] == current)
                    key = parts[1];
                else
                    key = Guid.NewGuid().ToString("N");
            }
            else if (fingerprint != current)
                key = Guid.NewGuid().ToString("N");
            fingerprint = current;
            await SecureStorage.Default.SetAsync(storage, System.Text.Json.JsonSerializer.Serialize(new[] { current, key }));
            var order = await Api.Post<OrderDto>("api/orders", input, key);
            SecureStorage.Default.Remove(storage);
            await Navigation.PushAsync(new CustomerOrderPage(Api, order.Id));
        });
    }
    async Task ClearCart()
    {
        if (await DisplayAlertAsync("Esvaziar carrinho?", "Os itens serão removidos. Um pedido já confirmado não será cancelado por esta ação.", "Esvaziar", "Manter"))
        {
            await Api.Delete("api/cart");
            await Load();
        }
    }
}
public sealed class CustomerOrdersPage : UiPage
{
    int page = 1;
    public CustomerOrdersPage(ApiClient api) : base(api, "Meus pedidos") { }
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await Run(Load);
    }
    async Task Load()
    {
        var orders = await Api.Get<PageDto<OrderDto>>($"api/orders?page={page}");
        ResetBody();
        foreach (var o in orders.Items)
            Action($"{o.RestaurantName}\n{Status(o.Status)} · {Money(o.Prices.Total)}\n{o.CreatedAt.ToOffset(TimeSpan.FromHours(-3)):dd/MM/yyyy HH:mm}", () => Navigation.PushAsync(new CustomerOrderPage(Api, o.Id)));
        if (orders.Total == 0)
            Text("Seu primeiro pedido está por vir.");
        if (page > 1)
            Action("Anteriores", async () => { page--; await Load(); });
        if (page * 50 < orders.Total)
            Action("Próximos", async () => { page++; await Load(); });
    }
}
public sealed class CustomerOrderPage : UiPage
{
    readonly Guid id; HubConnection? hub;
    public CustomerOrderPage(ApiClient api, Guid id) : base(api, "Acompanhar pedido") { this.id = id; }
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await Run(async () => { await Load(); try { hub = await Api.Connect(Changed, Changed); await hub.InvokeAsync("JoinOrder", id); hub.Reconnected += async connection => { await hub.InvokeAsync("JoinOrder", id); Changed(id); }; } catch (Exception ex) when (ex is HttpRequestException or Microsoft.AspNetCore.SignalR.HubException) { Feedback.Text = "Tempo real indisponível. Use Atualizar para consultar o pedido."; } });
    }
    void Changed(Guid changed)
    {
        if (changed == id)
            MainThread.BeginInvokeOnMainThread(async () => await Run(Load));
    }
    async Task Load()
    {
        var o = await Api.Get<OrderDto>($"api/orders/{id}");
        var tracking = await Api.Get<TrackingDto>($"api/orders/{id}/tracking");
        var events = await Api.Get<List<OrderEventDto>>($"api/orders/{id}/events");
        ResetBody();
        Text(o.RestaurantName, true);
        Text(Status(o.Status), true);
        Text(o.Address);
        foreach (var i in o.Items)
            Text($"{i.Quantity} × {i.Name} · {i.Notes}");
        Text($"Total {Money(o.Prices.Total)}\nReembolso: {Status(o.RefundStatus)} · {Money(o.Refunded)}");
        foreach (var e in events)
            Text($"{e.At.ToOffset(TimeSpan.FromHours(-3)):dd/MM HH:mm} · {Status(e.Next)}\n{e.Reason}");
        Text("Entrega: " + Status(tracking.Stage));
        if (tracking.Latitude is double lat && tracking.Longitude is double lon)
        {
            Text($"Última localização: {tracking.At?.ToOffset(TimeSpan.FromHours(-3)):HH:mm:ss}");
            Action("Ver entregador no mapa", () => Map.Default.OpenAsync(new Location(lat, lon), new MapLaunchOptions { Name = "Última localização informada" }));
        }
        else
            Text("Sem localização informada. O acompanhamento das etapas continua disponível.");
        Action("Atualizar pedido", Load);
        if (o.Status is "AwaitingPayment" or "SentToRestaurant" or "Accepted")
        {
            var reason = Field("Motivo do cancelamento");
            Action("Solicitar cancelamento", async () => { if (await DisplayAlertAsync("Cancelar pedido?", "A solicitação será validada conforme a etapa do preparo.", "Solicitar", "Manter")) { await Api.Post<OrderDto>($"api/orders/{id}/cancel", new ReasonRequest(reason.Text)); await Load(); } });
        }
        if (o.Status == "Delivered")
        {
            var restaurant = Field("Nota do restaurante (1 a 5)", "5", Keyboard.Numeric);
            var courier = Field("Nota da entrega (1 a 5)", "5", Keyboard.Numeric);
            var comment = Field("Comentário");
            Action("Avaliar", async () => { if (!int.TryParse(restaurant.Text, out var a) || !int.TryParse(courier.Text, out var b)) { Feedback.Text = "Informe notas de 1 a 5."; return; } await Api.Post<object>($"api/orders/{id}/review", new ReviewInput(a, b, comment.Text)); Feedback.Text = "Avaliação enviada. Obrigado!"; });
        }
        Action("Ajuda com este pedido", () => Navigation.PushAsync(new HelpPage(Api, id)));
    }
    protected override async void OnDisappearing()
    {
        base.OnDisappearing();
        if (hub != null)
        {
            await hub.DisposeAsync();
            hub = null;
        }
    }
}
