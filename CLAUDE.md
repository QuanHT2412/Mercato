# Project Brief — "Mercato" E-Commerce Microservices

> Portfolio project by a solo developer (final-year SE student, backend-focused).
> Goal: demonstrate **microservices + Clean Architecture** competence with real business logic —
> depth over breadth. This file is the source of truth for scope. When in doubt, do LESS but deeper.

## 0. Repository layout (monorepo)

```
/
├── MercatoServer/    # backend — .NET 10 microservices (the showcase; most effort goes here)
├── MercatoClient/    # frontend — React + TypeScript (Vite); thin client, demo layer only
├── docs/adr/         # architecture decision records
└── CLAUDE.md         # this file
```

**The backend is the star of this project.** MercatoClient exists to make the demo tangible;
it must never drive backend design decisions or consume disproportionate effort.

## 1. What this project is

A small but production-minded e-commerce system: backend split into **3 services + 1 gateway**,
communicating via REST (external), gRPC (internal sync), and RabbitMQ (async events), plus a
lightweight React client. Service boundaries follow an already-audited domain design
(zero circular references) originally modeled as PostgreSQL schemas: `auth`, `shop`, `ordering`.

**This is a CV project.** Every architectural decision should be explainable in an interview.
Prefer the solution that teaches/demonstrates a named pattern (outbox, saga, token rotation)
over the quickest hack — but never gold-plate beyond the defined scope.

## 2. System architecture

```
   MercatoClient (React) ──REST──▶ ┌─────────────┐
                                   │  Gateway     │ YARP reverse proxy
                                   └──────┬──────┘
                 ┌────────────────────────┼─────────────────┐
                 ▼                        ▼                 ▼
          ┌────────────┐          ┌────────────┐   ┌────────────┐
          │  Identity   │          │  Catalog    │◀──│  Ordering   │  gRPC: stock check
          │  Service    │          │  Service    │   │  Service    │
          └──────┬─────┘          └──────┬─────┘   └──────┬─────┘
                 │                       │                 │
            identity_db             catalog_db        ordering_db     (PostgreSQL, db-per-service)
                 │                       │                 │
                 └───────────────────────┴───────┬─────────┘
                                                 ▼
                                            RabbitMQ  (async events, outbox pattern)
                                                 │
                                               Redis   (catalog cache, distributed locks)
```

The frontend talks to **the gateway only** (single base URL via `VITE_API_URL`) — never
directly to individual services.

Local orchestration: **.NET Aspire** AppHost + Docker Compose. Aspire dashboard provides
OpenTelemetry traces/metrics/logs across all backend services.

## 3. Services & responsibilities (MercatoServer)

### 3.1 Identity Service (`identity_db`)
- Registration, login, JWT **access token + refresh token rotation per RFC 9700**
  (rotation on every refresh, reuse detection → revoke token family).
- Roles: `Customer`, `Admin`. Simple RBAC via claims.
- Publishes: `UserRegistered`.
- Deliberately hand-built (no Keycloak/Identity Server) — auth implementation is a
  showcase area. Use ASP.NET Core Identity primitives where sensible (password hashing).

### 3.2 Catalog Service (`catalog_db`)
- Products, categories, inventory (stock quantity).
- Read-heavy: product list/detail cached in **Redis** (cache-aside, explicit invalidation
  on product update).
- Exposes **gRPC** endpoint for Ordering: `CheckAndReserveStock`.
- Full-text product search via **PostgreSQL tsvector** (NOT Elasticsearch).
- Consumes: `OrderConfirmed` (decrement stock), `OrderCancelled` / `OrderExpired` (release stock).

### 3.3 Ordering Service (`ordering_db`)
- Cart, checkout, order lifecycle: `Pending → AwaitingPayment → Confirmed → Cancelled/Expired`.
- **Outbox pattern is mandatory** for all published events (`OrderCreated`, `OrderConfirmed`,
  `OrderCancelled`, `OrderExpired`): write event to outbox table in the same DB transaction
  as the state change; background dispatcher relays to RabbitMQ.
- **Hangfire** job: auto-expire orders unpaid after 15 minutes.
- Payment: **mock/simulated payment provider** behind an interface (design as if VNPay;
  do NOT integrate a real gateway in v1).
- Checkout flow = the "star" of the project: stock reservation via gRPC, saga-style
  compensation on failure (release reservation if payment fails/expires).

### 3.4 Gateway (YARP)
- Route `/api/identity/*`, `/api/catalog/*`, `/api/ordering/*` to services.
- JWT validation at gateway edge + per-route rate limiting. Keep config-driven, minimal code.
- CORS configured here (allow MercatoClient dev origin) — not in individual services.

## 4. Frontend scope (MercatoClient)

A thin demo client — enough to walk through the full user journey live, nothing more.

**Pages (complete list, do not expand):**
1. Product list (with search box → Catalog FTS) + product detail.
2. Register / Login (token storage, auto-refresh on 401 via refresh token flow).
3. Cart + checkout (triggers the backend saga; show order status transitions).
4. My orders (list + detail with live status; simple polling is fine — no SignalR/websockets).
5. Minimal admin: product CRUD form (Admin role only).

**Frontend rules:**
- Stack: React 19 + TypeScript + Vite, axios (single instance with interceptors for
  JWT attach + refresh-on-401), react-router-dom, Tailwind CSS.
- State: React state/context is enough. **Do NOT add Redux/Zustand/TanStack Query** unless a
  concrete pain appears — then ask first.
- All API calls go through a typed service layer (`src/services/*`), mirroring the Axios
  service-file pattern; components never call axios directly.
- No secrets in frontend code or `VITE_*` variables — `VITE_*` values are embedded in the
  public bundle. `VITE_API_URL` (gateway URL) is the only required env var.
- No SSR, no Next.js migration, no component library debates — plain Tailwind, keep it simple.
- UI polish is capped: clean and functional beats pretty. Backend depth is the selling point.

## 5. Tech stack (decided — do not relitigate)

| Concern | Choice | Notes |
|---|---|---|
| Runtime (BE) | .NET 10 (LTS), C# | |
| Architecture per service | Clean Architecture: Domain / Application / Infrastructure / Api | Domain has ZERO external package references (no EF Core in Domain) |
| ORM | EF Core, code-first migrations, Npgsql | |
| DB | PostgreSQL, one database per service | Single Postgres instance locally is fine |
| Mediator / handlers | Plain handler interfaces or Wolverine handlers | **Do NOT add MediatR** (commercial license) |
| Object mapping | **Mapperly** (source-gen) or manual mapping | **Do NOT add AutoMapper** (commercial license) |
| Messaging | RabbitMQ + **Wolverine** (MIT) | **Do NOT add MassTransit v9** (commercial). If Wolverine blocks hard, MassTransit v8 is the fallback — ask first |
| Internal sync calls | gRPC (Ordering → Catalog only) | Everything else async via events |
| Cache / locks | Redis (StackExchange.Redis) | |
| Background jobs | Hangfire (Ordering service only) | |
| Gateway | YARP | |
| Local orchestration | .NET Aspire + Docker Compose | |
| Observability | Serilog → OpenTelemetry; traces/metrics via Aspire defaults | Correlation across HTTP + gRPC + RabbitMQ hops must work |
| Frontend | React + TypeScript + Vite, axios, react-router-dom, Tailwind | See section 4 rules; no extra state libs |
| Testing (BE) | xUnit + **Testcontainers** (real Postgres/RabbitMQ) for integration tests | Unit tests for domain logic; integration tests for the checkout flow are highest priority |
| Testing (FE) | None required in v1 | Optional Vitest smoke tests only if time remains after M7 |
| CI | GitHub Actions: build BE, test BE, lint+build FE, docker build | |
| Deploy (optional, last) | Azure Container Apps | Local compose demo is acceptable for v1 |

## 6. Key business rules

- Stock can never go negative; reservation holds stock for the payment window.
- Order auto-expires 15 minutes after entering `AwaitingPayment`; expiry releases reserved stock.
- Cart max 20 line items; quantity per line 1–10.
- A cancelled/expired order cannot be reactivated (create a new order).
- Refresh token reuse → revoke the entire token family and force re-login.
- Prices are snapshotted onto the order at checkout (later catalog price changes don't affect existing orders).
- All money values: `decimal`, VND, no floating point. Frontend renders VND formatting only —
  never computes totals itself; totals always come from the backend.

## 7. Explicitly OUT of scope (do not build, do not suggest unless asked)

- Kubernetes, Helm, service mesh — Compose + Aspire only.
- Kafka / event streaming — RabbitMQ is sufficient.
- Full CQRS with separate read stores, Event Sourcing.
- Elasticsearch — Postgres full-text only.
- Real payment gateway integration, real email sending (log/mock instead).
- Social/review features (the original `social` schema is deferred to a hypothetical phase 2).
- Frontend beyond the 5 pages in section 4: no wishlist, no product reviews, no SSR,
  no realtime (SignalR/websockets), no state-management libraries, no i18n.
- Multi-region, sharding, API versioning schemes beyond `/v1`.

## 8. Build order (milestones — each must end in a runnable, demoable state)

1. **M1 — Skeleton**: Aspire AppHost, 3 empty services + gateway, Postgres/RabbitMQ/Redis
   containers wired, health checks, Serilog + OTel visible in Aspire dashboard.
2. **M2 — Identity**: register/login, JWT + refresh rotation + reuse detection, RBAC. Tests.
3. **M3 — Catalog**: products/categories CRUD (Admin), public browse + Postgres FTS,
   Redis cache-aside, gRPC stock endpoint. Tests.
4. **M4 — Ordering (sync path)**: cart, checkout calling Catalog via gRPC, mock payment,
   order lifecycle, Hangfire expiry job.
5. **M5 — Async & outbox**: outbox table + dispatcher, RabbitMQ events, Catalog consumers,
   compensation on cancel/expire. Testcontainers integration tests for the full checkout saga.
6. **M6 — Frontend**: the 5 pages in section 4, wired through the gateway, JWT refresh
   interceptor working end-to-end.
7. **M7 — Polish**: rate limiting + CORS at gateway, README with architecture diagram,
   seed data, demo script. (Optional: deploy to Azure Container Apps.)

Backend milestones M1–M5 come first; do not start the frontend early "to have something to
look at" — Swagger is the UI until M6. Do not start M(n+1) features while M(n) is broken.

## 9. Conventions

**Backend (MercatoServer/):**
- Solution layout:
  `MercatoServer/src/Services/{Identity|Catalog|Ordering}/{Service}.{Domain|Application|Infrastructure|Api}`,
  `MercatoServer/src/Gateway`, `MercatoServer/src/Shared` (ONLY event contracts + truly
  cross-cutting primitives — keep Shared tiny, no shared domain logic),
  `MercatoServer/src/AppHost` (Aspire), `MercatoServer/tests/` mirroring src.
- Event contracts: records in `Shared.Contracts`, versioned by name (`OrderCreatedV1` only if a
  breaking change ever happens; start with plain names).
- Each service owns its migrations; never point two services at one database.
- Async suffix on async methods; nullable reference types enabled; treat warnings as errors.

**Frontend (MercatoClient/):**
- Layout: `src/pages/`, `src/components/`, `src/services/` (typed API layer),
  `src/hooks/`, `src/types/` (DTO types mirroring backend contracts).
- One axios instance in `src/services/apiClient.ts` with JWT attach + refresh-on-401
  interceptors; nothing else imports axios directly.
- ESLint + Prettier as scaffolded; `npm run lint` must pass in CI.

**Repo-wide:**
- Commit style: conventional commits (`feat:`, `fix:`, `test:`, `chore:`), scope prefix when
  useful (`feat(ordering): ...`, `feat(client): ...`).
- Every non-obvious architectural choice gets a short ADR in `docs/adr/` (one paragraph is
  enough) — these double as interview prep.

## 10. Definition of done (per feature)

- Domain logic unit-tested; happy-path integration test where infra is involved (backend).
- Appears in OpenTelemetry trace with correct correlation (backend).
- Swagger documented (request/response examples for the main endpoints).
- Frontend: page works against the real gateway locally, lint passes, no hardcoded URLs.
- No `TODO` left without a tracking note in README.
