using System.Text.Json.Nodes;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Mercato.Ordering.Api.Endpoints;

internal static class OrderingSwagger
{
    public static void Configure(SwaggerGenOptions options)
    {
        options.SwaggerDoc("v1", new OpenApiInfo
        {
            Title = "Mercato Ordering API",
            Version = "v1",
        });

        // Every endpoint here needs a token: paste the accessToken from Identity's /login.
        options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "Access token from POST /api/identity/login",
        });
        options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference("Bearer", document)] = [],
        });

        options.OperationFilter<OrderingExamplesOperationFilter>();
    }
}

internal sealed class OrderingExamplesOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var requestBody = operation.RequestBody;

        if (requestBody is null)
        {
            return;
        }

        if (!requestBody!.Content!.TryGetValue("application/json", out var content))
        {
            return;
        }

        content!.Example = context.ApiDescription.RelativePath switch
        {
            "cart/items/{variantId}" => new JsonObject { ["quantity"] = 2 },
            "orders/checkout" => new JsonObject
            {
                ["receiver"] = "Nguyen Van A",
                ["phone"] = "0901234567",
                ["address"] = "12 Lê Lợi, Phường Bến Nghé, Quận 1, TP.HCM",
                ["note"] = "Giao giờ hành chính",
            },
            // Approved by default; send "tok_decline" to see the declined path.
            "orders/{id}/pay" => new JsonObject { ["paymentToken"] = null },
            _ => content.Example,
        };
    }
}
