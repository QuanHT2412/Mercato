using Xunit.Sdk;
using Xunit.v3;

// Same reason as the other integration projects: the factory feeds configuration through
// process-wide environment variables, so two factories must never initialize concurrently.
[assembly: Parallelization(Mode = ParallelMode.None)]
