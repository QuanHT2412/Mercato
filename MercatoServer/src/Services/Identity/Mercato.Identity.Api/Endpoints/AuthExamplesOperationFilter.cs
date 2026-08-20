using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Mercato.Identity.Api.Endpoints;

// Swashbuckle-native way to attach request examples to minimal API endpoints —
// RouteHandlerBuilder.WithOpenApi() is deprecated in .NET 10 (ASPDEPR002) and,
// being tied to the built-in Microsoft.AspNetCore.OpenApi generator, would not
// even apply to Swashbuckle's own document anyway.
internal sealed class AuthExamplesOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var requestBody = operation.RequestBody;

        if (requestBody is null)
        {
            return;
        }

        // Microsoft.OpenApi 2.x's IOpenApiRequestBody/Content ship without nullable
        // metadata, so the analyzer can't see these are guarded above/by TryGetValue.
        if (!requestBody!.Content!.TryGetValue("application/json", out var content))
        {
            return;
        }

        content!.Example = context.ApiDescription.RelativePath switch
        {
            "register" => AuthEndpointExamples.Register(),
            "login" => AuthEndpointExamples.Login(),
            _ => content.Example,
        };
    }
}
