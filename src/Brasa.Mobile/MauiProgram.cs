using Brasa.Client;
using Microsoft.Extensions.DependencyInjection;
namespace Brasa.Mobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder().UseMauiApp<App>();
        var url = typeof(MauiProgram).Assembly.GetCustomAttributes<System.Reflection.AssemblyMetadataAttribute>().Single(x => x.Key == "BrasaApiUrl").Value!;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            throw new InvalidOperationException("Configure a URL da API.");
#if !DEBUG
  if(uri.Scheme!="https")throw new InvalidOperationException("A API publicada deve usar HTTPS.");
#endif
        builder.Services.AddSingleton<ITokenStore, SecureTokens>();
        builder.Services.AddSingleton(sp => new ApiClient(new HttpClient { BaseAddress = uri, Timeout = TimeSpan.FromSeconds(20) }, sp.GetRequiredService<ITokenStore>()));
        return builder.Build();
    }
}
