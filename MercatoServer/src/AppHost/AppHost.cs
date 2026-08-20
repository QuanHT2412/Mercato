var builder = DistributedApplication.CreateBuilder(args);

// Secret parameters — resolved from User Secrets in Development (see README "Setup").
// Never hardcoded, never committed.
var postgresPassword = builder.AddParameter("postgres-password", secret: true);
var rabbitmqPassword = builder.AddParameter("rabbitmq-password", secret: true);
var redisPassword = builder.AddParameter("redis-password", secret: true);

// JWT signing (ES256 — asymmetric per ADR 0002): private key is secret and goes to
// Identity only (the sole issuer); public key is not secret and goes to every service
// that will validate tokens (dual validation, wired starting slice 4).
var jwtPrivateKey = builder.AddParameter("jwt-private-key", secret: true);
var jwtPublicKey = builder.AddParameter("jwt-public-key", secret: false);

var postgres = builder.AddPostgres("postgres", password: postgresPassword)
    .WithDataVolume();

var identityDb = postgres.AddDatabase("identity-db", "identity_db");
var catalogDb = postgres.AddDatabase("catalog-db", "catalog_db");
var orderingDb = postgres.AddDatabase("ordering-db", "ordering_db");

// Wired now so the container is visible on the dashboard from M1; no service consumes it
// yet — that starts in M5 (outbox dispatcher + consumers).
builder.AddRabbitMQ("rabbitmq", password: rabbitmqPassword)
    .WithManagementPlugin()
    .WithDataVolume();

var redis = builder.AddRedis("redis", password: redisPassword)
    .WithDataVolume();

var identityApi = builder.AddProject<Projects.Mercato_Identity_Api>("identity-api")
    .WithReference(identityDb)
    .WaitFor(identityDb)
    .WithEnvironment("Jwt__PrivateKey", jwtPrivateKey)
    .WithEnvironment("Jwt__PublicKey", jwtPublicKey);

var catalogApi = builder.AddProject<Projects.Mercato_Catalog_Api>("catalog-api")
    .WithReference(catalogDb)
    .WithReference(redis)
    .WaitFor(catalogDb)
    .WaitFor(redis)
    .WithEnvironment("Jwt__PublicKey", jwtPublicKey);

var orderingApi = builder.AddProject<Projects.Mercato_Ordering_Api>("ordering-api")
    .WithReference(orderingDb)
    .WaitFor(orderingDb)
    .WithEnvironment("Jwt__PublicKey", jwtPublicKey);

builder.AddProject<Projects.Mercato_Gateway>("gateway")
    .WithReference(identityApi)
    .WithReference(catalogApi)
    .WithReference(orderingApi)
    .WaitFor(identityApi)
    .WaitFor(catalogApi)
    .WaitFor(orderingApi)
    .WithEnvironment("Jwt__PublicKey", jwtPublicKey)
    .WithExternalHttpEndpoints();

builder.Build().Run();
