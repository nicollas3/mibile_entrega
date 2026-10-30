using Brasa.Client;
using Microsoft.AspNetCore.Components;
namespace Brasa.Web.Components.Shared;

public abstract class PageBase : ComponentBase
{
    [Inject] public ApiClient Api { get; set; } = default!;
    protected string? Error, Message;
    protected bool Busy;
    protected async Task Run(Func<Task> action)
    {
        if (Busy)
            return;
        Busy = true;
        Error = null;
        Message = null;
        try
        {
            await action();
        }
        catch (ApiException ex) { Error = ex.Message; }
        catch (HttpRequestException) { Error = "Sem conexão com a API. Verifique o serviço e tente novamente."; }
        catch (TaskCanceledException) { Error = "A solicitação demorou demais. Tente novamente com os mesmos dados."; }
        finally { Busy = false; await InvokeAsync(StateHasChanged); }
    }
}
