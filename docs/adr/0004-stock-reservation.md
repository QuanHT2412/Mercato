# ADR 0004: Stock is reserved with holds, guarded by row locks

Status: Accepted
Date: 2026-10-11

## Context

Checkout must guarantee two things from section 6 of the brief: stock never goes negative,
and a customer who reached the payment step keeps their items for the payment window
(15 minutes). Two customers can hit checkout for the last unit at the same moment.

## Decision

**Holds, not decrements.** `CheckAndReserveStock` does not change `product_variants.stock`.
It inserts a row in `stock_reservations` (`order_id`, `variant_id`, `quantity`,
`expires_at`). What can still be sold is

    available = stock - SUM(quantity of reservations WHERE expires_at > now)

A hold ends in exactly one of three ways:

- **confirm** (order paid): `stock -= quantity` and the hold is deleted, in one transaction;
- **release** (order cancelled/expired): the hold is deleted, stock untouched;
- **expiry**: nobody does anything — the hold simply stops matching `expires_at > now`.

**Concurrency.** Reserve and confirm run in a transaction that first takes
`SELECT ... FOR UPDATE` on the variant rows involved, ordered by id. A second checkout for
the same variant waits, then recomputes availability against the first one's committed
hold.

**Idempotency.** `(order_id, variant_id)` is unique. A repeated reserve for an order that
already holds stock returns the existing hold; a repeated confirm/release finds no hold and
reports `found = false` without doing anything.

**All-or-nothing.** If any line of an order cannot be held, none is.

## Consequences

- Overselling is prevented by the database, not by timing: 20 concurrent requests for 5
  units yield exactly 5 holds (covered by an integration test).
- Row locks serialise checkouts **per variant** only; different products do not contend.
  Locking in id order means two carts sharing variants cannot deadlock.
- Expiry needs no background job in the critical path. Dead rows accumulate until someone
  deletes them; a periodic cleanup is a later nicety, not a correctness requirement.
- A confirm that arrives after its hold expired can find the units sold to someone else.
  The domain refuses to drive stock negative and the RPC fails with `FAILED_PRECONDITION` —
  the caller (Ordering) has to treat that order as unfulfillable.
- Out-of-stock is an expected answer, so the RPC returns it as data
  (`reserved = false`, reason, available quantity) instead of an error status.

## Alternatives considered

- **Decrement on reserve, increment on release** — simpler arithmetic, but a missed
  release (crash between steps) loses stock forever, and "stock" stops meaning "units on the
  shelf". With holds, a missed release heals itself at `expires_at`.
- **Optimistic concurrency (`xmin`) with retry** — fine under low contention, but the hot
  case here is precisely many buyers on one variant, where retries pile up.
- **Redis distributed lock per variant** — works, but adds a second system to the
  correctness path when Postgres already holds the data and can lock the row atomically
  with the write. Redis stays a cache.
- **A single atomic `UPDATE ... WHERE stock - reserved >= @qty`** — good for one line; an
  order with several lines still needs a transaction to stay all-or-nothing.
