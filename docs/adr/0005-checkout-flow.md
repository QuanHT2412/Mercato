# ADR 0005: Checkout as an ordered sequence with compensation (sync path first)

Status: Accepted — the synchronous half. The asynchronous half (outbox + events) is M5.
Date: 2026-11-15

## Context

Checkout spans two services with two databases (ADR 0001): Catalog owns stock, Ordering
owns carts and orders. There is no transaction that covers both. Whatever order the steps
run in, the process can die between any two of them, so every step needs a defined answer
to "what if it stops here?".

## Decision

**Step order.** Checkout runs, in this order:

1. validate locally — cart not empty, shipping details valid;
2. reserve stock in Catalog (gRPC `CheckAndReserveStock`, all-or-nothing, ADR 0004);
3. in ONE Ordering transaction: insert the order (`AwaitingPayment`, 15-minute deadline)
   with lines snapshotted from the reservation reply, and empty the cart.

| Stops after | State left behind | Recovery |
|---|---|---|
| 1 | nothing | — |
| 2 (step 3 fails) | a hold in Catalog for an order that does not exist | **compensate**: `ReleaseReservation`; if that fails too, the hold lapses at its own expiry |
| 3 | order awaiting payment, owning the hold | paid → confirm; cancelled / expired → release |

The order id is generated before step 2 so the hold and the order share it; every Catalog
operation is idempotent on that id, which is what makes retries and compensations safe.

**Prices.** The order's lines (name, unit price) come from Catalog's reservation reply —
not from the client and not from the cart, which stores only variant ids and quantities.
The total is computed once, in the `Order` aggregate.

**After checkout.** `pay` confirms the order then asks Catalog to commit the hold;
`cancel` and the expiry job cancel/expire the order then ask Catalog to release it. In each
case the order's own state is saved first and the Catalog call follows.

**Concurrency on one order.** `orders` has an `xmin` concurrency token. Pay, cancel and the
expiry job can race on the same `AwaitingPayment` order; the first commit wins and the
others get a conflict instead of overwriting it.

**Expiry.** A Hangfire recurring job scans, every minute, for `AwaitingPayment` orders past
their deadline (partial index). The deadline is also enforced by the aggregate itself, so
an order is unpayable the moment it is overdue, not up to a minute later. Stock holds are
taken for the payment window plus two minutes so a last-second payment still finds its
hold.

## Consequences

- No distributed transaction, and no state in which stock is held forever: holds carry
  their own expiry, so the worst case of any failed compensation is "units unavailable
  until the hold lapses".
- Cheap local failures (bad address, empty cart) never cost a remote call.
- Checkout depends on Catalog being reachable: if it is down, checkout answers 503. That
  is a deliberate consistency-over-availability choice for the one step that must not
  oversell.

### Known gaps — exactly what M5 is for

- **Dual write after payment.** "Save order as Confirmed" and "tell Catalog to commit the
  hold" are two separate writes. If the second fails, the order is Confirmed while the
  units are only held (and the hold will eventually lapse). Today this is logged, not
  repaired. M5 replaces the direct call with an `OrderConfirmed` event written to an
  **outbox** table in the same transaction as the status change, relayed to RabbitMQ and
  consumed idempotently by Catalog. Same for cancel/expire → `OrderCancelled` /
  `OrderExpired`.
- **Charged but not confirmable.** If the order expires or is cancelled while the provider
  is processing the charge, the payment succeeded but the order cannot be confirmed. This
  is logged at Critical for manual follow-up; refunds are out of scope for v1.
- **Internal gRPC is unauthenticated.** It is not routed by the gateway (covered by a
  test), but a caller inside the network can reach it. Service-to-service auth is not
  built.

## Alternatives considered

- **Create the order first, reserve second.** Leaves `Pending` orders with no stock behind
  on failure, and the price snapshot would have to be fetched separately from the
  reservation. Reserving first means a failed reservation leaves no trace in Ordering.
- **Orchestrated saga with a state machine (Wolverine/MassTransit saga).** The right tool
  once there are more participants or long-running steps; for two services and three steps
  it would be more machinery than flow.
- **One delayed Hangfire job per order** for expiry. A second piece of state that can be
  lost or drift from the order; the scan derives everything from the orders table.
- **Two-phase commit across the databases.** Not available across service boundaries in
  any sense worth having, and the opposite of what the project is meant to demonstrate.
