using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi;
namespace Brasa.Api;

public static class OpenApiConfiguration
{
    public static void AddBrasaOpenApi(this IServiceCollection services) => services.AddOpenApi(options =>
    {
        options.AddDocumentTransformer((document, context, ct) =>
        {
            document.Info.Title = "Brasa API — desenvolvimento";
            document.Info.Version = "v1";
            document.Info.Description = "Marketplace fictício. Pagamentos demo-approved, demo-declined e demo-pending não realizam cobranças. Criação de pedidos e reembolsos exigem Idempotency-Key.";
            document.Components ??= new();
            document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
            document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme { Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT", Description = "Obtenha o accessToken em /api/auth/login." };
            return Task.CompletedTask;
        });
        options.AddOperationTransformer((operation, context, ct) =>
        {
            if (context.Description.ActionDescriptor.EndpointMetadata.OfType<IAuthorizeData>().Any())
                operation.Security = [new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Bearer", context.Document)] = [] }];
            return Task.CompletedTask;
        });
    });
}
