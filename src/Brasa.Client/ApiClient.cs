using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Brasa.Contracts;
using Microsoft.AspNetCore.SignalR.Client;
namespace Brasa.Client;

public interface ITokenStore
{
    Task<AuthDto?> Read();
    Task Write(AuthDto? auth);
}
public sealed class MemoryTokenStore : ITokenStore
{
    AuthDto? value;
    public Task<AuthDto?> Read() => Task.FromResult(value);
    public Task Write(AuthDto? auth)
    {
        value = auth;
        return Task.CompletedTask;
    }
}
public sealed class ApiException(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}
public sealed class ApiClient(HttpClient http, ITokenStore store)
{
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    readonly SemaphoreSlim refreshLock = new(1, 1);
    public AuthDto? Session
    {
        get; private set;
    }
    public event Action? Changed;
    public Uri BaseAddress => http.BaseAddress!;
    public async Task Initialize()
    {
        Session = await store.Read();
        Changed?.Invoke();
    }
    public async Task Login(LoginRequest input)
    {
        await Set(await Send<AuthDto>(HttpMethod.Post, "api/auth/login", input, false));
    }
    public async Task Register(RegisterRequest input)
    {
        await Set(await Send<AuthDto>(HttpMethod.Post, "api/auth/register", input, false));
    }
    async Task Set(AuthDto? auth)
    {
        Session = auth;
        await store.Write(auth);
        Changed?.Invoke();
    }
    public async Task Logout()
    {
        try
        {
            if (Session != null)
                await Send<object>(HttpMethod.Post, "api/auth/logout");
        }
        finally { await Set(null); }
    }
    public async Task<string?> AccessToken()
    {
        await refreshLock.WaitAsync();
        try
        {
            if (Session != null && Session.ExpiresAt < DateTimeOffset.UtcNow.AddSeconds(45))
            {
                try
                {
                    var next = await Send<AuthDto>(HttpMethod.Post, "api/auth/refresh", new RefreshRequest(Session.RefreshToken), false);
                    await Set(next);
                }
                catch (ApiException ex) when (ex.Status == 401) { await Set(null); throw; }
            }
            return Session?.AccessToken;
        }
        finally { refreshLock.Release(); }
    }
    public Task<T> Get<T>(string path) => Send<T>(HttpMethod.Get, path);
    public Task<T> Post<T>(string path, object? body = null, string? key = null) => Send<T>(HttpMethod.Post, path, body, true, key);
    public Task<T> Put<T>(string path, object body) => Send<T>(HttpMethod.Put, path, body);
    public Task<object> Delete(string path) => Send<object>(HttpMethod.Delete, path);
    public async Task<T> Send<T>(HttpMethod method, string path, object? body = null, bool authenticated = true, string? key = null)
    {
        // Filtros opcionais numéricos/UUID precisam ser omitidos; `campo=` não é null para o binder HTTP.
        if (path.Contains('?'))
        {
            var parts = path.Split('?', 2);
            var query = string.Join('&', parts[1].Split('&').Where(p => !p.Contains('=') || p.Split('=', 2)[1].Length > 0));
            path = parts[0] + (query.Length > 0 ? "?" + query : "");
        }
        using var request = new HttpRequestMessage(method, path);
        if (body != null)
            request.Content = JsonContent.Create(body, options: Json);
        if (key != null)
            request.Headers.Add("Idempotency-Key", key);
        if (authenticated && await AccessToken() is string token)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            string message = response.StatusCode == HttpStatusCode.Unauthorized ? "Sessão encerrada. Entre novamente." : "Não foi possível concluir. Tente novamente.";
            try
            {
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                if (doc.RootElement.TryGetProperty("title", out var title))
                    message = title.GetString() ?? message;
                if (doc.RootElement.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array)
                    message += " " + string.Join(" ", errors.EnumerateArray().Select(x => x.GetString()));
            }
            catch (JsonException) { }
            throw new ApiException((int)response.StatusCode, message);
        }
        if (response.StatusCode == HttpStatusCode.NoContent || response.Content.Headers.ContentLength == 0)
            return default!;
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }
    public async Task<HubConnection> Connect(Action<Guid> onOrderChanged, Action<Guid>? onLocation = null)
    {
        var hub = new HubConnectionBuilder().WithUrl(new Uri(BaseAddress, "hubs/orders"), o => o.AccessTokenProvider = AccessToken).WithAutomaticReconnect().Build();
        hub.On<JsonElement>("OrderChanged", e => { if (e.TryGetProperty("id", out var id)) onOrderChanged(id.GetGuid()); });
        hub.On<JsonElement>("LocationChanged", e => { if (e.TryGetProperty("id", out var id)) onLocation?.Invoke(id.GetGuid()); });
        await hub.StartAsync();
        return hub;
    }
}
