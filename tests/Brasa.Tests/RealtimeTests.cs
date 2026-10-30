using System.Net.Http.Json;
using Brasa.Contracts;
using Brasa.Infrastructure;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Xunit;
namespace Brasa.Tests;

[Collection("database")]
public sealed class RealtimeTests(ApiFixture fixture)
{
    [Fact]
    public async Task Hub_rejects_foreign_store_subscription()
    {
        using var client = fixture.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("loja2@brasa.local", ApiFixture.Password));
        response.EnsureSuccessStatusCode();
        var auth = (await response.Content.ReadFromJsonAsync<AuthDto>())!;
        await using var hub = new HubConnectionBuilder().WithUrl(new Uri(client.BaseAddress!, "hubs/orders"), options => { options.Transports = HttpTransportType.LongPolling; options.HttpMessageHandlerFactory = _ => fixture.Server.CreateHandler(); options.AccessTokenProvider = () => Task.FromResult<string?>(auth.AccessToken); }).Build();
        await hub.StartAsync();
        await Assert.ThrowsAsync<HubException>(() => hub.InvokeAsync("JoinStore", DemoSeed.FirstStore));
        await hub.InvokeAsync("JoinStore", DemoSeed.SecondStore);
    }
}
