using Brasa.Client;
using Brasa.Contracts;
namespace Brasa.Mobile;

public sealed class LoginPage : UiPage
{
    readonly Entry email, password, name; readonly Switch courier = new(); bool initialized;
    public LoginPage(ApiClient api) : base(api, "Brasa · bem-vindo")
    {
        Text("Boas conexões.\nMelhores sabores.", true);
        Text("Demonstração: pagamentos fictícios, sem cobrança real.");
        email = Field("Email", keyboard: Keyboard.Email);
        password = Field("Senha (mínimo 12 caracteres)", password: true);
        name = Field("Seu nome (para cadastro)");
        Text("Cadastrar como entregador (sujeito a aprovação)");
        Body.Add(courier);
        Action("Entrar", async () => { await Api.Login(new(email.Text.Trim(), password.Text)); password.Text = ""; await OpenArea(); });
        Action("Criar conta", async () => { await Api.Register(new(email.Text.Trim(), password.Text, name.Text, courier.IsToggled)); password.Text = ""; await OpenArea(); });
        Action("Recuperar senha", async () => { await Api.Post<object>("api/auth/forgot-password", new ForgotRequest(email.Text)); Feedback.Text = "Se a conta existir, receberá as instruções. Em desenvolvimento, consulte a caixa de email local."; });
        Action("Já tenho um token de recuperação", () => Navigation.PushAsync(new ResetPasswordPage(Api)));
        Text("Coletamos os dados necessários para pedidos e entregas. Você pode solicitar acesso, correção ou exclusão na área de ajuda. A localização só é solicitada durante uma entrega ativa.");
    }
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (initialized)
            return;
        initialized = true;
        await Run(async () => { await Api.Initialize(); if (Api.Session != null) await OpenArea(); });
    }
    async Task OpenArea()
    {
        var roles = Api.Session!.User.Roles;
        bool customer = roles.Contains("Cliente"), driver = roles.Contains("Entregador");
        if (customer && driver)
        {
            var area = await DisplayActionSheetAsync("Qual área deseja abrir?", "Cancelar", null, "Cliente", "Entregador");
            if (area == "Cliente")
                await Navigation.PushAsync(new CustomerPage(Api));
            else if (area == "Entregador")
                await Navigation.PushAsync(new CourierPage(Api));
        }
        else if (driver)
            await Navigation.PushAsync(new CourierPage(Api));
        else if (customer)
            await Navigation.PushAsync(new CustomerPage(Api));
        else
            Feedback.Text = "Este perfil utiliza os painéis web de restaurante ou administração.";
    }
}
public sealed class ResetPasswordPage : UiPage
{
    public ResetPasswordPage(ApiClient api) : base(api, "Recuperar senha")
    {
        var email = Field("Email", keyboard: Keyboard.Email);
        var token = Field("Token de recuperação");
        var password = Field("Nova senha (12 caracteres ou mais)", password: true);
        Action("Alterar senha", async () => { await Api.Post<object>("api/auth/reset-password", new ResetRequest(email.Text, token.Text, password.Text)); password.Text = ""; token.Text = ""; Feedback.Text = "Senha alterada. Entre novamente."; });
    }
}
public sealed class ProfilePage : UiPage
{
    public ProfilePage(ApiClient api) : base(api, "Minha conta")
    {
        var name = Field("Nome", api.Session!.User.Name);
        var phone = Field("Telefone com DDD", api.Session.User.Phone ?? "", Keyboard.Telephone);
        Action("Salvar perfil", async () => { await Api.Put<object>("api/me", new ProfileRequest(name.Text, phone.Text)); Feedback.Text = "Perfil atualizado."; });
        if (Api.Session!.User.Roles.Contains("Cliente"))
            Action("Meus endereços", () => Navigation.PushAsync(new AddressesPage(Api)));
        Action("Ajuda e privacidade", () => Navigation.PushAsync(new HelpPage(Api, null)));
        Action("Exportar meus dados", async () => { var data = await Api.Get<System.Text.Json.JsonElement>("api/me/export"); var file = Path.Combine(FileSystem.CacheDirectory, "brasa-meus-dados.json"); await File.WriteAllTextAsync(file, data.GetRawText()); await Share.Default.RequestAsync(new ShareFileRequest("Meus dados na Brasa", new ShareFile(file))); });
        Action("Sair da conta", SignOut);
    }
}
public sealed class AddressesPage : UiPage
{
    public AddressesPage(ApiClient api) : base(api, "Endereços") { }
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await Run(Load);
    }
    async Task Load()
    {
        var list = await Api.Get<List<AddressDto>>("api/me/addresses");
        ResetBody();
        Text("Onde sua comida deve chegar?", true);
        foreach (var a in list)
        {
            Text(a.Label + "\n" + a.Formatted);
            Action("Editar endereço", () => Navigation.PushAsync(new AddressFormPage(Api, a)));
            Action("Ver no mapa", () => Map.Default.OpenAsync(new Location(a.Latitude, a.Longitude), new MapLaunchOptions { Name = a.Label }));
            Action("Remover endereço", async () => { if (await DisplayAlertAsync("Remover endereço?", a.Label, "Remover", "Manter")) { await Api.Delete($"api/me/addresses/{a.Id}"); await Load(); } });
        }
        Action("Adicionar endereço", () => Navigation.PushAsync(new AddressFormPage(Api)));
    }
}
public sealed class AddressFormPage : UiPage
{
    public AddressFormPage(ApiClient api, AddressDto? existing = null) : base(api, existing == null ? "Novo endereço" : "Editar endereço")
    {
        var label = Field("Nome do endereço", "Casa");
        var street = Field("Rua");
        var number = Field("Número");
        var district = Field("Bairro");
        var city = Field("Cidade");
        var state = Field("UF (duas letras)");
        var postal = Field("CEP (oito números)", keyboard: Keyboard.Numeric);
        var complement = Field("Complemento");
        var reference = Field("Referência");
        var lat = Field("Latitude", keyboard: Keyboard.Numeric);
        var lon = Field("Longitude", keyboard: Keyboard.Numeric);
        if (existing?.Details is { } d)
        {
            label.Text = d.Label;
            street.Text = d.Street;
            number.Text = d.Number;
            district.Text = d.District;
            city.Text = d.City;
            state.Text = d.State;
            postal.Text = d.PostalCode;
            complement.Text = d.Complement;
            reference.Text = d.Reference;
            lat.Text = d.Latitude.ToString(Presentation.Brazil);
            lon.Text = d.Longitude.ToString(Presentation.Brazil);
        }
        Action("Usar minha localização atual", async () => { if (!await DisplayAlertAsync("Localização para o endereço", "Usaremos o GPS uma vez para preencher as coordenadas. Você também pode informá-las manualmente.", "Permitir", "Agora não")) return; if (await Permissions.RequestAsync<Permissions.LocationWhenInUse>() != PermissionStatus.Granted) { Feedback.Text = "Permissão não concedida. Preencha as coordenadas manualmente."; return; } var l = await Geolocation.Default.GetLocationAsync(new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(10))); if (l == null) { Feedback.Text = "GPS indisponível. Informe as coordenadas manualmente."; return; } lat.Text = l.Latitude.ToString(Presentation.Brazil); lon.Text = l.Longitude.ToString(Presentation.Brazil); });
        Text("A conversão automática de endereço em coordenadas exige configurar um provedor de geocodificação. Confira a posição no mapa antes de salvar.");
        Action("Conferir coordenadas no mapa", async () => { if (Coordinates(lat.Text, lon.Text, out var x, out var y)) await Map.Default.OpenAsync(new Location(x, y), new MapLaunchOptions { Name = label.Text }); else Feedback.Text = "Informe latitude e longitude válidas."; });
        Action("Salvar endereço", async () => { if (!Coordinates(lat.Text, lon.Text, out var x, out var y)) { Feedback.Text = "Informe latitude e longitude válidas."; return; } var input = new AddressInput(label.Text, street.Text, number.Text, district.Text, city.Text, state.Text.ToUpperInvariant(), postal.Text, complement.Text, reference.Text, x, y); if (existing == null) await Api.Post<AddressDto>("api/me/addresses", input); else await Api.Put<object>($"api/me/addresses/{existing.Id}", input); await Navigation.PopAsync(); });
    }
    static bool Coordinates(string? lat, string? lon, out double x, out double y)
    {
        bool a = double.TryParse(lat, System.Globalization.NumberStyles.Float, Presentation.Brazil, out x) || double.TryParse(lat, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out x);
        bool b = double.TryParse(lon, System.Globalization.NumberStyles.Float, Presentation.Brazil, out y) || double.TryParse(lon, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out y);
        return a && b && x is >= -90 and <= 90 && y is >= -180 and <= 180;
    }
}
public sealed class HelpPage : UiPage
{
    public HelpPage(ApiClient api, Guid? order) : base(api, "Ajuda e privacidade")
    {
        Text("Conte o que aconteceu. Não envie senhas ou dados de cartão.");
        var subject = Field("Assunto", order.HasValue ? "Ajuda com pedido" : "Privacidade ou atendimento");
        var description = Field("Descrição");
        Action("Enviar solicitação", async () => { await Api.Post<object>("api/support", new TicketInput(order, subject.Text, description.Text)); Feedback.Text = "Solicitação registrada. A equipe poderá responder pela central de atendimento."; });
        Action("Consultar minhas solicitações", async () => { var page = await Api.Get<PageDto<TicketDto>>("api/support"); foreach (var t in page.Items) Text($"{t.Category}: {t.Description}\n{(t.State == "Open" ? "Em aberto" : t.Resolution)}"); if (page.Total == 0) Feedback.Text = "Nenhuma solicitação registrada."; });
    }
}
