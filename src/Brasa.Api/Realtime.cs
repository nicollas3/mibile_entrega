using System.Security.Claims;
using Brasa.Domain;
using Brasa.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
namespace Brasa.Api;

[Authorize]
public sealed class OrderHub(AppDb db, Access access) : Hub
{
    Actor Actor => new(Context.User!.FindFirstValue("sub")!, Context.User!.FindAll(ClaimTypes.Role).Select(x => x.Value).ToArray());
    public async Task JoinOrder(Guid id)
    {
        var order = await db.Orders.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id) ?? throw new HubException("Pedido não encontrado.");
        try
        {
            await access.ReadOrder(Actor, order);
        }
        catch (RuleException) { throw new HubException("Acesso não permitido."); }
        await Groups.AddToGroupAsync(Context.ConnectionId, "order:" + id);
    }
    public async Task JoinStore(Guid id)
    {
        try
        {
            await access.Store(Actor, id, StorePermission.Orders);
        }
        catch (RuleException) { throw new HubException("Acesso não permitido."); }
        await Groups.AddToGroupAsync(Context.ConnectionId, "store:" + id);
    }
    public override async Task OnConnectedAsync()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, "user:" + Actor.Id);
        await base.OnConnectedAsync();
    }
}
public sealed class OperationsWorker(IServiceScopeFactory scopes, IHubContext<OrderHub> hub, ILogger<OperationsWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(ct))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDb>();
                var delivery = scope.ServiceProvider.GetRequiredService<DeliveryService>();
                await delivery.ExpireOrders(ct);
                await delivery.Dispatch(ct);
                var pending = await db.Outbox.Where(x => x.SentAt == null).OrderBy(x => x.Id).Take(100).ToListAsync(ct);
                foreach (var msg in pending)
                {
                    var order = await db.Orders.AsNoTracking().SingleAsync(x => x.Id == msg.OrderId, ct);
                    // Os eventos contêm apenas ID e estado; detalhes sempre exigem nova autorização HTTP.
                    await hub.Clients.Groups("order:" + order.Id, "store:" + order.RestaurantId, "user:" + order.CustomerId).SendAsync("OrderChanged", new
                    {
                        order.Id,
                        msg.Status,
                        msg.At
                    }, ct);
                    await scope.ServiceProvider.GetRequiredService<IPushNotifier>().Send(order.CustomerId, "Atualização do pedido", msg.Status, ct);
                    msg.SentAt = DateTimeOffset.UtcNow;
                }
                await db.SaveChangesAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogWarning("Ciclo operacional falhou ({ExceptionType}); nova tentativa no próximo ciclo.", ex.GetType().Name); }
        }
    }
}
