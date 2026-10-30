using Brasa.Client;
using Brasa.Contracts;
namespace Brasa.Mobile;

public abstract class UiPage : ContentPage
{
    protected readonly ApiClient Api; protected readonly VerticalStackLayout Body = new() { Spacing = 16, Padding = new Thickness(22) }; protected readonly Label Feedback = new() { TextColor = Color.FromArgb("8A3019"), LineBreakMode = LineBreakMode.WordWrap }; bool busy;
    protected UiPage(ApiClient api, string title)
    {
        Api = api;
        Title = title;
        BackgroundColor = Color.FromArgb("F6F5EE");
        Content = new ScrollView { Content = Body };
        Body.Add(Feedback);
        SemanticProperties.SetDescription(Feedback, "Resultado da operação");
    }
    protected Label Text(string value, bool heading = false)
    {
        var label = new Label { Text = value, FontSize = heading ? 24 : 15, FontAttributes = heading ? FontAttributes.Bold : FontAttributes.None, TextColor = Color.FromArgb("20332C") };
        Body.Add(label);
        return label;
    }
    protected Entry Field(string caption, string value = "", Keyboard? keyboard = null, bool password = false)
    {
        Text(caption);
        var field = new Entry { Text = value, Placeholder = caption, Keyboard = keyboard ?? Keyboard.Default, IsPassword = password, TextColor = Color.FromArgb("20332C") };
        SemanticProperties.SetDescription(field, caption);
        Body.Add(field);
        return field;
    }
    protected Button Action(string label, Func<Task> action)
    {
        var button = new Button { Text = label, BackgroundColor = Color.FromArgb("B44420"), TextColor = Colors.White, CornerRadius = 10, MinimumHeightRequest = 48 };
        button.Clicked += async (sender, args) => await Run(action);
        Body.Add(button);
        return button;
    }
    protected async Task Run(Func<Task> action)
    {
        if (busy)
            return;
        busy = true;
        Feedback.Text = "Carregando…";
        try
        {
            await action();
            if (Feedback.Text == "Carregando…")
                Feedback.Text = "";
        }
        catch (ApiException ex) { Feedback.Text = ex.Message; }
        catch (HttpRequestException) { Feedback.Text = "Sem conexão. Seus dados preenchidos foram mantidos. Tente novamente."; }
        catch (TaskCanceledException) { Feedback.Text = "A conexão demorou. Tente novamente sem alterar os dados do pedido."; }
        catch (Exception ex) when (ex is PermissionException or FeatureNotSupportedException or FeatureNotEnabledException) { Feedback.Text = "Este recurso não está disponível ou não foi autorizado. Você pode continuar usando as outras funções."; }
        finally { busy = false; }
    }
    protected void ResetBody()
    {
        Body.Clear();
        Body.Add(Feedback);
    }
    protected async Task SignOut()
    {
        await Api.Logout();
        await Navigation.PopToRootAsync();
    }
    protected static string Money(decimal value) => Presentation.Money(value);
    protected static string Status(string value) => Presentation.Status(value);
}
