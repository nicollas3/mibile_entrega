using Brasa.Client;
using Brasa.Web.Components;
using Brasa.Web;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddScoped<ProtectedSessionStorage>();
builder.Services.AddScoped<ITokenStore, ProtectedWebTokens>();
builder.Services.AddScoped(sp => new ApiClient(new HttpClient { BaseAddress = new Uri(builder.Configuration["Api:BaseUrl"] ?? "http://localhost:5070"), Timeout = TimeSpan.FromSeconds(20) }, sp.GetRequiredService<ITokenStore>()));
var app = builder.Build();
if (!app.Environment.IsDevelopment()) { app.UseExceptionHandler("/error"); app.UseHsts(); app.UseHttpsRedirection(); }
app.Use(async (ctx, next) => { ctx.Response.Headers["X-Content-Type-Options"] = "nosniff"; ctx.Response.Headers["Referrer-Policy"] = "no-referrer"; ctx.Response.Headers["Content-Security-Policy"] = "frame-ancestors 'none'; object-src 'none'; base-uri 'self'"; await next(); });
app.UseStaticFiles();
app.UseAntiforgery();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();
