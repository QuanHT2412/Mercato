using System.Text.Json.Nodes;

namespace Mercato.Identity.Api.Endpoints;

internal static class AuthEndpointExamples
{
    public static JsonNode Register() => new JsonObject
    {
        ["email"] = "customer@example.com",
        ["username"] = "customer1",
        ["password"] = "P@ssw0rd123",
        ["name"] = "Nguyen Van A",
    };

    public static JsonNode Login() => new JsonObject
    {
        ["email"] = "customer@example.com",
        ["password"] = "P@ssw0rd123",
    };
}
