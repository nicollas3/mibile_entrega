using Brasa.Client;
using Brasa.Contracts;
namespace Brasa.Mobile;

public sealed class CourierPage : UiPage
{
    CancellationTokenSource? polling; Task? pollTask;
    public CourierPage(ApiClient api) : base(api, "Brasa · entregador") { }
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await Run(Load);
        polling = new();
        pollTask = Poll(polling.Token);
    }
    async Task Poll(CancellationToken ct)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
            while (await timer.WaitForNextTickAsync(ct))
                await MainThread.InvokeOnMainThreadAsync(() => Run(Load));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }
    async Task Load()
    {
        var p = await Api.Get<CourierDto>("api/courier/profile");
        var offers = await Api.Get<List<OfferDto>>("api/courier/offers");
        var deliveries = await Api.Get<List<DeliveryDto>>("api/courier/deliveries");
        ResetBody();
        Text("Seu caminho começa aqui.", true);
        Text($"{p.Name}\nCadastro: {(p.Approved ? "aprovado" : "aguardando aprovação")}\n{(p.Available ? "Disponível para ofertas" : "Indisponível")}");
        var vehicle = Field("Veículo", p.Vehicle);
        Action(p.Available ? "Ficar indisponível" : "Ficar disponível", async () => { await Api.Put<object>("api/courier/availability", new AvailabilityInput(!p.Available, vehicle.Text)); await Load(); });
        Text("Ofertas de entrega", true);
        if (offers.Count == 0)
            Text("Sem ofertas agora. Atualizamos automaticamente a cada 15 segundos.");
        foreach (var offer in offers)
        {
            Text($"{offer.RestaurantName}\nOrigem: {offer.Origin}\nDestino aproximado: {offer.ApproximateDestination}\n~{offer.ApproximateKm:F1} km em linha reta · {Money(offer.Amount)} previsto\nAceitar até {offer.ExpiresAt.ToLocalTime():HH:mm:ss}");
            Action("Aceitar oferta", async () => { if (await DisplayAlertAsync("Aceitar esta entrega?", "Você terá acesso ao endereço do cliente após o aceite.", "Aceitar", "Agora não")) { await Api.Post<object>($"api/courier/offers/{offer.Id}/accept"); await Load(); } });
            Action("Recusar oferta", async () => { await Api.Post<object>($"api/courier/offers/{offer.Id}/decline"); await Load(); });
        }
        Text("Minhas entregas", true);
        if (deliveries.Count == 0)
            Text("Nenhuma entrega no histórico.");
        foreach (var d in deliveries)
            Action($"{d.RestaurantName}\n{Status(d.Stage)} · {Status(d.OrderStatus)} · {Money(d.Amount)} previsto\n{(d.Rating.HasValue ? $"Avaliação: {d.Rating}/5" : "Aguardando avaliação")}", () => Navigation.PushAsync(new CourierDeliveryPage(Api, d.OrderId)));
        Text($"Total previsto em entregas concluídas: {Money(deliveries.Where(x => x.OrderStatus == "Delivered").Sum(x => x.Amount))}. Não representa um repasse bancário.");
        Action("Atualizar ofertas", Load);
        Action("Minha conta", () => Navigation.PushAsync(new ProfilePage(Api)));
        Action("Sair", SignOut);
    }
    protected override async void OnDisappearing()
    {
        base.OnDisappearing();
        polling?.Cancel();
        if (pollTask != null)
            await pollTask;
        polling?.Dispose();
        polling = null;
    }
}
public sealed class CourierDeliveryPage : UiPage
{
    readonly Guid id; DeliveryDto? delivery; CancellationTokenSource? gps; Task? gpsTask;
    public CourierDeliveryPage(ApiClient api, Guid id) : base(api, "Minha entrega") { this.id = id; }
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await Run(Load);
    }
    bool Active => delivery?.OrderStatus is "CourierAssigned" or "InTransit";
    async Task Load()
    {
        delivery = (await Api.Get<List<DeliveryDto>>("api/courier/deliveries")).Single(x => x.OrderId == id);
        var d = delivery;
        ResetBody();
        Text(d.RestaurantName, true);
        Text($"{Status(d.Stage)} · {Status(d.OrderStatus)}\n{Money(d.Amount)} previsto");
        Text("Retirada: " + d.Origin);
        Text("Destino: " + d.Destination);
        Action("Navegar até o restaurante", () => Map.Default.OpenAsync(new Location(d.OriginLatitude, d.OriginLongitude), new MapLaunchOptions { Name = d.RestaurantName, NavigationMode = NavigationMode.Driving }));
        Action("Navegar até o cliente", () => Map.Default.OpenAsync(new Location(d.DestinationLatitude, d.DestinationLongitude), new MapLaunchOptions { Name = "Entrega", NavigationMode = NavigationMode.Driving }));
        if (Active)
        {
            string? next = d.Stage switch
            {
                "Assigned" => "GoingToRestaurant",
                "GoingToRestaurant" => "AtRestaurant",
                "AtRestaurant" => "PickedUp",
                "PickedUp" => "GoingToCustomer",
                "GoingToCustomer" => "Delivered",
                _ => null
            };
            if (next != null)
                Action("Confirmar: " + Status(next), async () => { if (await DisplayAlertAsync("Confirmar etapa?", next == "Delivered" ? "Confirme somente após entregar ao cliente. Problemas devem ser registrados na ajuda." : Status(next), "Confirmar", "Voltar")) { await Api.Post<object>($"api/courier/deliveries/{id}/stage", new DeliveryInput(next, delivery!.Version)); await Load(); if (!Active) await StopGps(); } });
            if (gps == null)
                Action("Compartilhar localização nesta entrega", StartGps);
            else
                Action("Parar compartilhamento", async () => { await StopGps(); await Load(); });
            Text("O GPS só é enviado com sua autorização, durante uma entrega ativa e enquanto esta tela estiver aberta. Se faltar sinal, você pode atualizar as etapas manualmente.");
            if (d.Stage != "Problem")
                Action("Registrar problema na entrega", async () => { if (await DisplayAlertAsync("Há um problema?", "A etapa ficará como problema. Registre também uma ocorrência para o atendimento.", "Registrar", "Voltar")) { await Api.Post<object>($"api/courier/deliveries/{id}/stage", new DeliveryInput("Problem", delivery!.Version)); await StopGps(); await Navigation.PushAsync(new HelpPage(Api, id)); } });
        }
        Action("Atualizar entrega", Load);
        Action("Ocorrências e suporte", () => Navigation.PushAsync(new HelpPage(Api, id)));
    }
    async Task StartGps()
    {
        if (!Active)
            return;
        if (!await DisplayAlertAsync("Compartilhar GPS?", "Enviaremos a localização aproximadamente a cada 15 segundos, somente nesta tela e durante esta entrega.", "Compartilhar", "Agora não"))
            return;
        if (await Permissions.RequestAsync<Permissions.LocationWhenInUse>() != PermissionStatus.Granted)
        {
            Feedback.Text = "Localização não autorizada. As atualizações manuais continuam disponíveis.";
            return;
        }
        gps = new();
        gpsTask = Track(gps.Token);
        await Load();
    }
    async Task Track(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && Active)
            {
                try
                {
                    var l = await Geolocation.Default.GetLocationAsync(new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(10)), ct);
                    if (l != null)
                    {
                        await Api.Post<object>($"api/courier/deliveries/{id}/location", new LocationInput(l.Latitude, l.Longitude, l.Timestamp));
                        await MainThread.InvokeOnMainThreadAsync(() => Run(Load));
                    }
                    else
                        await MainThread.InvokeOnMainThreadAsync(() => Feedback.Text = "Sem sinal de GPS. As etapas continuam disponíveis.");
                }
                catch (ApiException ex) { await MainThread.InvokeOnMainThreadAsync(() => Feedback.Text = ex.Message); if (ex.Status is 401 or 403 or 409) break; }
                catch (HttpRequestException) { await MainThread.InvokeOnMainThreadAsync(() => Feedback.Text = "Sem conexão para enviar o GPS. Tentaremos novamente; você pode consultar as etapas."); }
                catch (Exception ex) when (ex is FeatureNotEnabledException or PermissionException) { await MainThread.InvokeOnMainThreadAsync(() => Feedback.Text = "GPS desligado ou não autorizado. Atualize as etapas manualmente."); break; }
                await Task.Delay(TimeSpan.FromSeconds(15), ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }
    async Task StopGps()
    {
        gps?.Cancel();
        if (gpsTask != null)
            await gpsTask;
        gps?.Dispose();
        gps = null;
        gpsTask = null;
    }
    protected override async void OnDisappearing()
    {
        base.OnDisappearing();
        await StopGps();
    }
}
