using Mercato.Identity.Api.Auth;
using Mercato.Identity.Api.Endpoints;
using Mercato.Identity.Api.Errors;
using Microsoft.OpenApi;
using Mercato.Identity.Application;
using Mercato.Identity.Application.Abstractions;
using Mercato.Identity.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddJwtValidation();
builder.AddIdentityInfrastructure();
builder.Services.AddIdentityApplication();
builder.Services.Configure<AuthCookieOptions>(builder.Configuration.GetSection(AuthCookieOptions.SectionName));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Mercato Identity API",
        Version = "v1",
    });
    options.OperationFilter<Mercato.Identity.Api.Endpoints.AuthExamplesOperationFilter>();
});

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<IdentityExceptionHandler>();

var app = builder.Build();

// Fail fast on a missing/invalid JWT signing key instead of on the first login request.
app.Services.GetRequiredService<IAccessTokenGenerator>();

if (app.Environment.IsDevelopment())
{
    await app.Services.InitializeIdentityDatabaseAsync();

    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();

app.MapDefaultEndpoints();
app.MapAuthEndpoints();

app.Run();

public partial class Program;
