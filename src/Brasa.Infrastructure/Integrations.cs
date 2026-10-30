using Brasa.Domain;
using Microsoft.Extensions.Hosting;
namespace Brasa.Infrastructure;

public record PaymentResult(string Reference, bool Approved, string Mode);
public interface IPaymentProvider
{
    Task<PaymentResult> Charge(Guid orderId, decimal amount, string method, CancellationToken ct);
    Task<bool> Refund(Guid orderId, decimal amount, string idempotencyKey, CancellationToken ct);
}
/// <summary>Não realiza nenhuma cobrança. Só é registrado no ambiente Development.</summary>
public sealed class DevelopmentPaymentProvider : IPaymentProvider
{
    public Task<PaymentResult> Charge(Guid orderId, decimal amount, string method, CancellationToken ct)
    {
        if (method is not ("demo-approved" or "demo-declined" or "demo-pending"))
            throw new RuleException("payment_method", "Escolha um pagamento de demonstração. Nenhuma cobrança real será feita.");
        return Task.FromResult(new PaymentResult("DEMO-" + orderId, method == "demo-approved", "development-only"));
    }
    public Task<bool> Refund(Guid orderId, decimal amount, string idempotencyKey, CancellationToken ct) => Task.FromResult(true);
}
public interface IMapsService
{
    double ApproximateDistanceKm(double lat1, double lon1, double lat2, double lon2);
    string RouteUri(double latitude, double longitude);
}
public sealed class ApproximateMaps : IMapsService
{
    public double ApproximateDistanceKm(double lat1, double lon1, double lat2, double lon2)
    {
        const double r = Math.PI / 180;
        double a = Math.Pow(Math.Sin((lat2 - lat1) * r / 2), 2) + Math.Cos(lat1 * r) * Math.Cos(lat2 * r) * Math.Pow(Math.Sin((lon2 - lon1) * r / 2), 2);
        return Math.Round(6371 * 2 * Math.Asin(Math.Sqrt(Math.Clamp(a, 0, 1))), 1);
    }
    public string RouteUri(double latitude, double longitude) => FormattableString.Invariant($"https://www.google.com/maps/dir/?api=1&destination={latitude},{longitude}");
}
public interface IGeocoder
{
    Task<(double Latitude, double Longitude)?> Find(string address, CancellationToken ct);
}
public sealed class UnconfiguredGeocoder : IGeocoder
{
    public Task<(double, double)?> Find(string address, CancellationToken ct) => Task.FromResult<(double, double)?>(null);
}
public interface IPushNotifier
{
    Task Send(string userId, string title, string text, CancellationToken ct);
}
public sealed class DevelopmentPush : IPushNotifier
{
    // SignalR e histórico persistente funcionam; push FCM/APNs não é enviado.
    public Task Send(string userId, string title, string text, CancellationToken ct) => Task.CompletedTask;
}
public interface IAccountEmail
{
    Task SendReset(string email, string token, CancellationToken ct);
}
public sealed class LocalAccountEmail(IHostEnvironment env) : IAccountEmail
{
    public async Task SendReset(string email, string token, CancellationToken ct)
    {
        if (!env.IsDevelopment())
            throw new InvalidOperationException("Configure um serviço de email para recuperação de senha.");
        var dir = Path.Combine(env.ContentRootPath, ".local", "mail");
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, Guid.NewGuid() + ".txt"), $"DEMONSTRAÇÃO LOCAL — NÃO É EMAIL REAL\nPara: {email}\nToken de recuperação: {token}", ct);
    }
}
