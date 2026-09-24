using System.Text.Json.Nodes;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Mercato.Catalog.Api.Endpoints;

internal static class CatalogSwagger
{
    public static void Configure(SwaggerGenOptions options)
    {
        options.SwaggerDoc("v1", new OpenApiInfo
        {
            Title = "Mercato Catalog API",
            Version = "v1",
        });

        // "Authorize" button: paste the accessToken returned by Identity's /login.
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

        options.OperationFilter<CatalogExamplesOperationFilter>();
    }
}

// Same approach as Identity's AuthExamplesOperationFilter: Swashbuckle-native request
// examples for the endpoints someone will actually try from Swagger UI.
internal sealed class CatalogExamplesOperationFilter : IOperationFilter
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

        var method = context.ApiDescription.HttpMethod;

        content!.Example = (method, context.ApiDescription.RelativePath) switch
        {
            ("POST", "categories") => new JsonObject { ["name"] = "Áo thun", ["parentId"] = null },
            ("POST", "products") => CreateProduct(),
            _ => content.Example,
        };
    }

    private static JsonNode CreateProduct() => new JsonObject
    {
        ["name"] = "Áo thun cotton basic",
        ["description"] = "Áo thun cổ tròn 100% cotton, form regular.",
        ["categoryId"] = null,
        ["variants"] = new JsonArray
        {
            new JsonObject { ["sku"] = "AO-THUN-BASIC-M", ["name"] = "Trắng / M", ["price"] = 199000, ["stock"] = 25, ["imageUrl"] = null },
            new JsonObject { ["sku"] = "AO-THUN-BASIC-L", ["name"] = "Trắng / L", ["price"] = 199000, ["stock"] = 10, ["imageUrl"] = null },
        },
    };
}
