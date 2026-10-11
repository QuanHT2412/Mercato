# ADR 0003: Catalog search on Postgres FTS, reads cached with cache-aside

Status: Accepted
Date: 2026-10-11

## Context

The catalog is read-heavy (browse, search, product detail) and written rarely (an admin
editing products). Two decisions follow from that: how product search works, and how reads
are kept cheap.

## Decision

**Search.** `products.search_vector` is a `tsvector` **stored generated column**
(`name` weighted A, `description` weighted B, `simple` config) with a GIN index. Queries use
`search_vector @@ to_tsquery(...)` ordered by `ts_rank`. User input is never passed to
`to_tsquery` as-is: it is split into words (letters/digits only) and rebuilt as
`word1:* & word2:*`, i.e. every word must match, each as a prefix.

**Caching.** Cache-aside on Redis, owned by the Application layer through an `ICatalogCache`
port:

- product detail: key per product, TTL 5 min, **deleted explicitly** on every write to that
  product (update, variant change, deactivate, stock committed by a paid order);
- list/search pages: the key embeds a **version number**; any write increments it (`INCR`),
  which orphans every cached page at once. TTL 1 min cleans the orphans up.
- admins bypass the cache entirely (they must see what they just saved, and inactive rows).

Every cache call is best-effort: a Redis error is logged and treated as a miss.

## Consequences

- No extra infrastructure for search (no Elasticsearch to run, sync or keep consistent). The
  generated column cannot drift from the row because Postgres maintains it.
- `simple` means no stemming and no stop words: fine for product names, especially
  Vietnamese ones, but "running" will not match "run". Diacritics are significant
  ("ao" does not find "áo") — `unaccent` would fix that and is left out for now.
- Prefix matching makes the search box narrow results as you type, at the cost of slightly
  noisier matches on very short prefixes.
- The domain entity knows nothing about `tsvector`: it is a shadow property, because the
  Domain project has no package references.
- Versioned list keys trade memory (orphaned pages live up to 1 min) for not having to
  enumerate or pattern-delete keys (`KEYS`/`SCAN` on a hot path).
- A reader that loads a product just before a write can still re-populate the cache with
  the old value just after the invalidation. The TTL bounds that staleness; it is accepted
  rather than solved with locking.
- Stock shown on a cached product page can be up to 5 minutes old. That is display only:
  the authoritative check happens at checkout, inside a transaction (ADR 0004).

## Alternatives considered

- **`ILIKE '%term%'`** — what the first version did. Cannot use an index, ignores the
  description, no ranking. Replaced.
- **Elasticsearch / OpenSearch** — better relevance, but a second datastore to keep in sync
  for a catalog this size. Out of scope by design.
- **Pattern-deleting list keys on write** — needs `SCAN`, is O(keys), and races with
  concurrent writers. Rejected in favour of the version counter.
- **Write-through / caching inside the repository** — hides the pattern in infrastructure;
  keeping it in the handlers makes "what is cached and when it is invalidated" readable in
  one place.
