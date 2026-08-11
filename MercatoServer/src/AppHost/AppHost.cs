var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithDataVolume();

var identityDb = postgres.AddDatabase("identity-db", "identity_db");
var catalogDb = postgres.AddDatabase("catalog-db", "catalog_db");
var orderingDb = postgres.AddDatabase("ordering-db", "ordering_db");

// Wired now so the container is visible on the dashboard from M1; no service consumes it
// yet — that starts in M5 (outbox dispatcher + consumers).
builder.AddRabbitMQ("rabbitmq")
    .WithManagementPlugin()
    .WithDataVolume();

var redis = builder.AddRedis("redis")
    .WithDataVolume();

var identityApi = builder.AddProject<Projects.Mercato_Identity_Api>("identity-api")
    .WithReference(identityDb)
    .WaitFor(identityDb);

var catalogApi = builder.AddProject<Projects.Mercato_Catalog_Api>("catalog-api")
    .WithReference(catalogDb)
    .WithReference(redis)
    .WaitFor(catalogDb)
    .WaitFor(redis);

var orderingApi = builder.AddProject<Projects.Mercato_Ordering_Api>("ordering-api")
    .WithReference(orderingDb)
    .WaitFor(orderingDb);

builder.AddProject<Projects.Mercato_Gateway>("gateway")
    .WithReference(identityApi)
    .WithReference(catalogApi)
    .WithReference(orderingApi)
    .WaitFor(identityApi)
    .WaitFor(catalogApi)
    .WaitFor(orderingApi)
    .WithExternalHttpEndpoints();

builder.Build().Run();
