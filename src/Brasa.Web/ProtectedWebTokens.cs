using Brasa.Client;
using Brasa.Contracts;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
namespace Brasa.Web;
// O navegador recebe somente o envelope criptografado por ASP.NET Data Protection.
public sealed class ProtectedWebTokens(ProtectedSessionStorage storage) : ITokenStore
{
    public async Task<AuthDto?> Read()
    {
        try
        {
            var saved = await storage.GetAsync<AuthDto>("brasa.session");
            return saved.Success ? saved.Value : null;
        }
        catch (System.Security.Cryptography.CryptographicException) { await storage.DeleteAsync("brasa.session"); return null; }
    }
    public async Task Write(AuthDto? auth)
    {
        if (auth == null)
            await storage.DeleteAsync("brasa.session");
        else
            await storage.SetAsync("brasa.session", auth);
    }
}
