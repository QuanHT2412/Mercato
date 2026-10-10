using Xunit.Sdk;
using Xunit.v3;

// Same reason as Identity.IntegrationTests: CatalogApiFactory feeds configuration through
// process-wide environment variables, so two factories must never initialize concurrently.
[assembly: Parallelization(Mode = ParallelMode.None)]
