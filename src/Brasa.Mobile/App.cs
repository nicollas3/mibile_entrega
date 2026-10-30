using Brasa.Client;
using Brasa.Contracts;
using System.Text.Json;
namespace Brasa.Mobile;

public sealed class SecureTokens : ITokenStore
{
    public async Task<AuthDto?> Read()
    {
        try
        {
            var raw = await SecureStorage.Default.GetAsync("brasa.session");
            return raw == null ? null : JsonSerializer.Deserialize<AuthDto>(raw);
        }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or JsonException) { SecureStorage.Default.Remove("brasa.session"); return null; }
    }
    public async Task Write(AuthDto? auth)
    {
        if (auth == null)
            SecureStorage.Default.Remove("brasa.session");
        else
            await SecureStorage.Default.SetAsync("brasa.session", JsonSerializer.Serialize(auth));
    }
}
public sealed class App(ApiClient api) : Application
{
    protected override Window CreateWindow(IActivationState? state) => new(new NavigationPage(new LoginPage(api)) { BarBackgroundColor = Color.FromArgb("244C3E"), BarTextColor = Colors.White });
}
