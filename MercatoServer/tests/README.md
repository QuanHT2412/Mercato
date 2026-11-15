# Tests

One unit-test project and one integration-test project per service, mirroring `src/`, plus
one end-to-end project for the whole system:

```
tests/
├── Identity.Tests/               unit — Identity domain + application handlers
├── Identity.IntegrationTests/    Identity.Api on a Testcontainers Postgres, plus one test
│                                 through the real gateway for the refresh-cookie path
├── Catalog.Tests/                unit — product/variant rules, stock reservation math
├── Catalog.IntegrationTests/     Catalog.Api on Testcontainers Postgres + Redis: FTS, cache
│                                 invalidation, stock holds over gRPC (incl. a concurrency race)
├── Ordering.Tests/               unit — cart limits, order state machine, checkout/pay/expire
│                                 handlers including the compensation paths
├── Ordering.IntegrationTests/    Ordering.Api on a Testcontainers Postgres, Catalog replaced
│                                 at the IStockGateway port, controllable clock for expiry
└── EndToEnd.Tests/               the whole stack booted from the AppHost; a full purchase,
                                  black-box over HTTP through the gateway
```

| Kind | Needs | Speed |
|---|---|---|
| unit (`*.Tests`) | nothing | ~2 s each |
| integration (`*.IntegrationTests`) | Docker | ~15–40 s each |
| end-to-end | Docker + the AppHost secrets from the root README | ~1 min |

```bash
dotnet test --project tests/Ordering.Tests
dotnet test --project tests/Ordering.IntegrationTests
dotnet test --project tests/EndToEnd.Tests
```

Notes:

- Integration factories pass configuration to the service through environment variables
  (the same way the AppHost does), so test classes inside one project run sequentially.
- Integration tests sign their own access tokens with a throwaway ES256 key whose public
  half is given to the service — they play the role of Identity.
- `EndToEnd.Tests` strips the AppHost's data volumes, so it never touches the data of a
  locally running dev stack. `Identity.IntegrationTests`' gateway test does use the dev
  volumes (it predates that), which is why it generates unique users per run.
