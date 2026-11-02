using Mercato.Ordering.Api.Endpoints;
using Mercato.Ordering.Api.Errors;
using Mercato.Ordering.Application;
using Mercato.Ordering.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddJwtValidation();
builder.AddOrderingInfrastructure();
builder.Services.AddOrderingApplication();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(OrderingSwagger.Configure);

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<OrderingExceptionHandler>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    await app.Services.InitializeOrderingDatabaseAsync();

    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();

app.MapDefaultEndpoints();
app.MapCartEndpoints();
app.MapOrderEndpoints();

app.Run();

public partial class Program;
