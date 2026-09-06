# Mercato

E-commerce microservices portfolio project. See [CLAUDE.md](CLAUDE.md) for full scope,
architecture, and build order.

## Setup

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

Requires Docker Desktop (or another container runtime) running, since Postgres, RabbitMQ, and
Redis are started as containers by Aspire.
