# Tests

Empty at M1 — there is no business logic yet to test. From M2 onward, test projects will be
added here mirroring `src/`, one per service:

```
tests/
├── Identity.Tests/     (xUnit — unit tests for Identity.Domain/Application)
├── Catalog.Tests/
├── Ordering.Tests/
└── Ordering.IntegrationTests/   (xUnit + Testcontainers — checkout saga, highest priority)
```
