# Tests

One unit-test project and one integration-test project per service, mirroring `src/`:

```
tests/
├── Identity.Tests/               unit — Identity domain + application handlers
├── Identity.IntegrationTests/    Identity.Api on a Testcontainers Postgres, plus one test
│                                 through the real gateway (full AppHost) for the cookie path
├── Catalog.Tests/                unit — product/variant rules, stock reservation math
└── Catalog.IntegrationTests/     Catalog.Api on Testcontainers Postgres + Redis: FTS, cache
                                  invalidation, stock holds over gRPC (incl. a concurrency race)
```

Unit tests need nothing. Integration tests need Docker running.

```bash
dotnet test --project tests/Catalog.Tests
dotnet test --project tests/Catalog.IntegrationTests
```

Integration factories pass configuration to the service through environment variables (the
same way the AppHost does), so test classes inside one project run sequentially.
