using Mercato.Catalog.Api.Endpoints;
using Mercato.Catalog.Api.Errors;
using Mercato.Catalog.Api.Grpc;
using Mercato.Catalog.Application;
using Mercato.Catalog.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddJwtValidation();
builder.AddCatalogInfrastructure();
builder.Services.AddCatalogApplication();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(CatalogSwagger.Configure);

builder.Services.AddGrpc();

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<CatalogExceptionHandler>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    await app.Services.InitializeCatalogDatabaseAsync();

    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();

app.MapDefaultEndpoints();
app.MapCategoryEndpoints();
app.MapProductEndpoints();

// Internal only: called by Ordering over service discovery, never routed by the gateway
// (its catalog route only forwards /products and /categories).
app.MapGrpcService<StockGrpcService>();

app.Run();

public partial class Program;
