# Mercato

An e-commerce backend split into three .NET 10 microservices behind a gateway — a portfolio
project about **microservices + Clean Architecture with real business logic**, not about
feature count. Scope and rules live in [CLAUDE.md](CLAUDE.md).

> **Status: work in progress.** The backend's synchronous path is done and tested end to
> end (browse → cart → checkout → pay / cancel / expire). The asynchronous half (outbox +
> RabbitMQ events), the React client and the gateway polish are next — see
> [Roadmap](#roadmap).

## Architecture

```
                     ┌─────────────┐
   client ──REST──▶  │   Gateway    │  YARP — routing, JWT validation at the edge
                     └──────┬──────┘
          ┌─────────────────┼──────────────────┐
          ▼                 ▼                  ▼
   ┌────────────┐    ┌────────────┐     ┌────────────┐
   │  Identity   │    │  Catalog    │◀────│  Ordering   │  gRPC: reserve / confirm / release stock
   └──────┬─────┘    └──────┬─────┘     └──────┬─────┘
          │                 │                  │
     identity_db       catalog_db         ordering_db       PostgreSQL, one database per service
                            │
                          Redis                             catalog read cache
```

Each service is four projects — `Domain` (no package references at all), `Application`
(handlers + ports), `Infrastructure` (EF Core, Redis, gRPC, Hangfire), `Api` (minimal API
endpoints). Local orchestration is .NET Aspire; its dashboard shows traces, metrics and
logs across the services.

RabbitMQ is started by the AppHost but nothing publishes to it yet (that is M5).

## What is built

| Milestone | Status | What it covers |
|---|---|---|
| M1 — Skeleton | done | Aspire AppHost, gateway, 3 services, Postgres/RabbitMQ/Redis, health checks, Serilog → OTel |
| M2 — Identity | done | register / login, ES256 JWT, refresh-token rotation with reuse detection, roles, `/me` |
| M3 — Catalog | done | categories + products (Admin CRUD), Postgres full-text search, Redis cache-aside, gRPC stock service |
| M4 — Ordering (sync) | done | cart, checkout with stock reservation + compensation, mock payment, order lifecycle, Hangfire expiry |
| M5 — Async & outbox | **next** | outbox table + dispatcher, RabbitMQ events, Catalog consumers |
| M6 — Frontend | planned | the 5-page React client |
| M7 — Polish | planned | rate limiting + CORS at the gateway, seed data, demo script |

Things worth reading in the code:

- **Refresh tokens** — rotation on every refresh, a 30-second grace window for the
  two-tabs race, family revocation on reuse, and an `xmin` concurrency token so two
  concurrent refreshes cannot both mint a successor.
  `MercatoServer/src/Services/Identity/Mercato.Identity.Application/Tokens/RefreshAccessToken.cs`
- **Stock reservation** — holds instead of decrements, guarded by `SELECT … FOR UPDATE`;
  20 concurrent checkouts for 5 units yield exactly 5 holds.
  `MercatoServer/src/Services/Catalog/Mercato.Catalog.Application/Stock/ReserveStock.cs`
- **Checkout** — reserve in Catalog, then order + emptied cart in one transaction,
  releasing the hold if that fails.
  `MercatoServer/src/Services/Ordering/Mercato.Ordering.Application/Orders/Checkout.cs`
- **Order lifecycle** — every transition guarded in the aggregate.
  `MercatoServer/src/Services/Ordering/Mercato.Ordering.Domain/Orders/Order.cs`

The decisions behind them are in [docs/adr](docs/adr):

| ADR | Decision |
|---|---|
| 0001 | Database per service, no foreign keys across services |
| 0002 | Authentication token strategy (ES256, rotation, cookie + in-memory split) |
| [0003](docs/adr/0003-catalog-search-and-caching.md) | Postgres FTS for search, cache-aside with versioned list keys |
| [0004](docs/adr/0004-stock-reservation.md) | Stock holds guarded by row locks |
| [0005](docs/adr/0005-checkout-flow.md) | Checkout as an ordered sequence with compensation |

## API (through the gateway)

| Route | Auth | |
|---|---|---|
| `POST /api/identity/register` · `login` · `refresh` · `logout` | — | refresh token travels in an httpOnly cookie |
| `GET /api/identity/me` | token | |
| `GET /api/catalog/products?search=&categoryId=&page=&pageSize=` | — | full-text, prefix matching |
| `GET /api/catalog/products/{id}` · `GET /api/catalog/categories` | — | |
| `POST/PUT/DELETE /api/catalog/products…` · `categories…` | Admin | delete is a soft delete |
| `GET /api/ordering/cart` · `PUT/DELETE /api/ordering/cart/items/{variantId}` | token | max 20 lines, quantity 1–10 |
| `POST /api/ordering/orders/checkout` | token | reserves stock, snapshots prices, 15-minute payment window |
| `GET /api/ordering/orders` · `/{id}` | token | only the caller's own orders |
| `POST /api/ordering/orders/{id}/pay` · `/cancel` | token | mock provider; send `"paymentToken": "tok_decline"` to see a decline |

Each service also serves Swagger UI at `/swagger` in Development (open the service's URL
from the Aspire dashboard); it has an *Authorize* button for the access token.

## Setup

Requires the .NET 10 SDK and Docker Desktop (or another container runtime) running —
Postgres, RabbitMQ and Redis are started as containers by Aspire.

### Backend secrets (MercatoServer/src/AppHost)

The AppHost declares the Postgres/RabbitMQ/Redis passwords as secret parameters — never
hardcoded, never committed. Before the first `dotnet run`, set them via .NET User Secrets:

```bash
cd MercatoServer/src/AppHost
dotnet user-secrets set "Parameters:postgres-password" "<pick-a-dev-password>"
dotnet user-secrets set "Parameters:rabbitmq-password" "<pick-a-dev-password>"
dotnet user-secrets set "Parameters:redis-password" "<pick-a-dev-password>"
```

Access tokens are signed with ES256 (ADR 0002): Identity holds the private key, the gateway
and every service get only the public key. Generate a dev key pair once and store both:

```bash
openssl ecparam -name prime256v1 -genkey -noout -out jwt-private.pem
openssl ec -in jwt-private.pem -pubout -out jwt-public.pem
dotnet user-secrets set "Parameters:jwt-private-key" "$(cat jwt-private.pem)"
dotnet user-secrets set "Parameters:jwt-public-key" "$(cat jwt-public.pem)"
rm jwt-private.pem jwt-public.pem
```

Optionally, to get an Admin account (`admin@mercato.local`) seeded on startup — needed for
the catalog management endpoints:

```bash
dotnet user-secrets set "Parameters:admin-password" "<pick-a-dev-password>"
```

These are stored outside the repo (`%APPDATA%\Microsoft\UserSecrets\<UserSecretsId>\secrets.json`
on Windows), scoped to the `Mercato.AppHost` project. Each developer sets their own values —
they don't need to match anything, they just need to exist.

### Running

```bash
dotnet run --project MercatoServer/src/AppHost
```

The console prints the Aspire dashboard URL; the gateway's URL is listed there.

### Trying it

With the gateway URL from the dashboard as `$GW`:

1. `POST $GW/api/identity/login` as the seeded admin → copy `accessToken`.
2. `POST $GW/api/catalog/products` (Bearer admin token) with a name and one variant
   (`sku`, `name`, `price`, `stock`).
3. `POST $GW/api/identity/register`, then `login` as that customer.
4. `PUT $GW/api/ordering/cart/items/{variantId}` with `{ "quantity": 2 }`.
5. `POST $GW/api/ordering/orders/checkout` with `receiver`, `phone`, `address` →
   the order comes back `AwaitingPayment` with the total computed by the backend.
6. `POST $GW/api/ordering/orders/{id}/pay` → `Confirmed`; the variant's stock in the
   catalog has dropped. Or wait 15 minutes and watch it become `Expired`.

`MercatoServer/tests/EndToEnd.Tests` runs exactly this journey automatically.

## Tests

```bash
dotnet test --solution MercatoServer/MercatoServer.slnx
```

Unit tests need nothing; integration and end-to-end tests need Docker (and, for the
end-to-end ones, the secrets above). See [MercatoServer/tests/README.md](MercatoServer/tests/README.md).

## Roadmap

Open work, in order. Everything listed here is a known gap, not a surprise:

- **M5 — outbox and events.** Today, after a payment Ordering calls Catalog directly to
  commit the stock hold, and after cancel/expiry to release it. Those are two writes to two
  databases; if the second fails it is only logged (the hold then lapses by itself). M5
  writes `OrderCreated` / `OrderConfirmed` / `OrderCancelled` / `OrderExpired` to an outbox
  table in the same transaction as the status change and relays them over RabbitMQ to
  idempotent consumers in Catalog. Identity's `UserRegistered` goes the same way.
  Details: [ADR 0005](docs/adr/0005-checkout-flow.md).
- **M6 — frontend.** `MercatoClient/` is still the Vite scaffold.
- **M7 — gateway polish.** Per-route rate limiting and CORS; seed data; a scripted demo.

Smaller gaps:

- The internal gRPC stock service has no service-to-service authentication. It is not
  routed by the gateway (covered by a test) but is reachable from inside the network.
- A payment that succeeds while its order expires/gets cancelled is logged for manual
  follow-up; there is no refund flow (out of scope for v1).
- Expired stock holds stop counting immediately but their rows are never deleted.
- Search is accent-sensitive (`ao` does not find `áo`); adding `unaccent` would fix it.
- CI runs the whole test suite, including the tests that boot the AppHost; those need the
  AppHost parameters to be provided in the CI environment.
