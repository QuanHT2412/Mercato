using Xunit.Sdk;
using Xunit.v3;

// IdentityApiFactory feeds the app configuration via process-wide environment
// variables (see IdentityApiFactory.InitializeAsync) because Aspire's
// AddNpgsqlDbContext didn't reliably pick up ConfigureAppConfiguration overrides in
// WebApplicationFactory. That makes different IdentityApiFactory instances (one per
// test class, each with its own Testcontainers Postgres) unsafe to initialize
// concurrently — they'd race to overwrite each other's connection string/JWT env vars.
[assembly: Parallelization(Mode = ParallelMode.None)]
