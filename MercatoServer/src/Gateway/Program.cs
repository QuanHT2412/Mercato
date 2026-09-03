var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Edge validation (ADR 0002): a request with a bad or missing token for a protected
// route is rejected here and never reaches a service. Which routes are protected is
// configuration, not code — see "AuthorizationPolicy" on the routes in appsettings.json.
// The services still validate the same token again themselves.
builder.AddJwtValidation();

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddServiceDiscoveryDestinationResolver();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapDefaultEndpoints();
app.MapReverseProxy();

app.Run();
