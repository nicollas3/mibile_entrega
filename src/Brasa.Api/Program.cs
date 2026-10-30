using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Brasa.Api;
using Brasa.Domain;
using Brasa.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
var connection = builder.Configuration.GetConnectionString("Database") ?? throw new InvalidOperationException("Defina ConnectionStrings__Database.");
var jwtKey = builder.Configuration["Jwt:Key"];
if (string.IsNullOrWhiteSpace(jwtKey) || Encoding.UTF8.GetByteCount(jwtKey) < 32) throw new InvalidOperationException("Defina Jwt__Key aleatório com pelo menos 32 bytes.");
if (!builder.Environment.IsDevelopment()) throw new InvalidOperationException("Esta versão usa integrações de desenvolvimento. Configure provedores reais de pagamento, email, geocodificação e push antes de habilitar produção.");
builder.WebHost.ConfigureKestrel(x => x.Limits.MaxRequestBodySize = 128 * 1024);
builder.Services.AddDbContext<AppDb>(x => x.UseNpgsql(connection, o => o.MigrationsAssembly("Brasa.Api")));
builder.Services.AddIdentityCore<AppUser>(o => { o.Password.RequiredLength = 12; o.User.RequireUniqueEmail = true; o.Lockout.MaxFailedAccessAttempts = 5; o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15); }).AddRoles<IdentityRole>().AddEntityFrameworkStores<AppDb>().AddDefaultTokenProviders();
builder.Services.Configure<DataProtectionTokenProviderOptions>(x => x.TokenLifespan = TimeSpan.FromHours(1));
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    o.MapInboundClaims = false;
    o.TokenValidationParameters = new()
    {
        ValidateIssuer = true,
        ValidIssuer = "brasa-api",
        ValidateAudience = true,
        ValidAudience = "brasa-clients",
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
        ClockSkew = TimeSpan.FromSeconds(20),
        RoleClaimType = ClaimTypes.Role,
        NameClaimType = ClaimTypes.Name
    };
    o.Events = new()
    {
        OnMessageReceived = ctx => { if (ctx.Request.Path.StartsWithSegments("/hubs/orders") && ctx.Request.Query.TryGetValue("access_token", out var token)) ctx.Token = token; return Task.CompletedTask; },
        OnTokenValidated = async ctx =>
        {
            var db = ctx.HttpContext.RequestServices.GetRequiredService<AppDb>();
            var sub = ctx.Principal?.FindFirstValue("sub");
            var sid = ctx.Principal?.FindFirstValue("sid");
            if (!Guid.TryParse(sid, out var id) || !await db.Sessions.AnyAsync(x => x.Id == id && x.UserId == sub && x.RevokedAt == null && x.ExpiresAt > DateTimeOffset.UtcNow) || !await db.Users.AnyAsync(x => x.Id == sub && !x.Blocked))
                ctx.Fail("Sessão encerrada.");
        }
    };
});
builder.Services.AddAuthorization();
builder.Services.AddBrasaOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddSignalR(o => o.MaximumReceiveMessageSize = 8192);
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:5080"]).AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.GlobalLimiter = PartitionedRateLimiter.Create<Microsoft.AspNetCore.Http.HttpContext, string>(ctx => RateLimitPartition.GetFixedWindowLimiter(ctx.User.FindFirstValue("sub") ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "local", _ => new() { PermitLimit = 300, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    o.AddPolicy("auth", ctx => RateLimitPartition.GetFixedWindowLimiter(ctx.Connection.RemoteIpAddress?.ToString() ?? "local", _ => new() { PermitLimit = builder.Configuration.GetValue<int?>("RateLimits:AuthPerMinute") ?? 25, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<Access>();
builder.Services.AddScoped<CatalogService>();
builder.Services.AddScoped<CartService>();
builder.Services.AddScoped<OrderService>();
builder.Services.AddScoped<DeliveryService>();
builder.Services.AddScoped<OperationsService>();
builder.Services.AddSingleton<IPaymentProvider, DevelopmentPaymentProvider>();
builder.Services.AddSingleton<IMapsService, ApproximateMaps>();
builder.Services.AddSingleton<IGeocoder, UnconfiguredGeocoder>();
builder.Services.AddSingleton<IPushNotifier, DevelopmentPush>();
builder.Services.AddSingleton<IAccountEmail, LocalAccountEmail>();
builder.Services.AddHostedService<OperationsWorker>();
var app = builder.Build();
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
    try
    {
        await next();
    }
    catch (RuleException ex) { await Results.Problem(statusCode: ex.Status, title: ex.Message, extensions: new Dictionary<string, object?> { { "code", ex.Code }, { "traceId", ctx.TraceIdentifier } }).ExecuteAsync(ctx); }
    catch (BadHttpRequestException) { await Results.Problem(statusCode: 400, title: "Requisição inválida.").ExecuteAsync(ctx); }
    catch (System.Text.Json.JsonException) { await Results.Problem(statusCode: 400, title: "Formato JSON inválido.").ExecuteAsync(ctx); }
    catch (DbUpdateConcurrencyException) { await Results.Problem(statusCode: 409, title: "Os dados foram atualizados por outra operação. Recarregue.").ExecuteAsync(ctx); }
    catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException pg && pg.SqlState == "23505") { await Results.Problem(statusCode: 409, title: "Operação duplicada. Recarregue os dados.").ExecuteAsync(ctx); }
    catch (Exception ex) { app.Logger.LogError("Falha {ExceptionType}; rastreio {TraceId}", ex.GetType().Name, ctx.TraceIdentifier); await Results.Problem(statusCode: 500, title: "Não foi possível concluir a operação.", extensions: new Dictionary<string, object?> { { "traceId", ctx.TraceIdentifier } }).ExecuteAsync(ctx); }
});
app.UseCors();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
app.MapGet("/health", async (AppDb db, CancellationToken ct) => await db.Database.CanConnectAsync(ct) ? Results.Ok(new { status = "ok", payments = "development-only" }) : Results.StatusCode(503));
app.MapOpenApi();
app.UseSwaggerUI(o => { o.SwaggerEndpoint("/openapi/v1.json", "Brasa API v1 — desenvolvimento"); o.RoutePrefix = "docs"; });
app.MapBrasaEndpoints();
app.MapHub<OrderHub>("/hubs/orders", o => o.CloseOnAuthenticationExpiration = true).RequireAuthorization();
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDb>();
    if (builder.Configuration.GetValue<bool>("Database:Migrate"))
        await db.Database.MigrateAsync();
    if (!args.Contains("--schema-only"))
        await DemoSeed.Run(db, scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>(), scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>(), app.Environment, app.Configuration);
}
await app.RunAsync();
public partial class Program;
