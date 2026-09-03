var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddJwtValidation();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapDefaultEndpoints();

app.Run();
