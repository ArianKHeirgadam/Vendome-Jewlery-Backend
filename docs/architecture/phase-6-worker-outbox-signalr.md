# Phase 6: Worker, durable outbox, and SignalR

## Scope and result

Phase 6 turns the existing `integration.OutboxMessages` schema foundation into a transactional event pipeline. It adds versioned events for invoice creation, inventory changes, order-status changes, and accepted market-price snapshots; atomic SQL Server claiming; lock recovery and heartbeat renewal; bounded retry and dead-letter behavior; permission-protected reprocessing; authorized SignalR delivery; and an audience-scoped recovery API.

No table or column is added. The existing Outbox columns and indexes are sufficient, so Phase 6 intentionally creates no EF Core migration and does not add an unused Inbox table. SignalR notifications are hints, not the authoritative business state.

## Transactional event production

`IOutboxWriter` adds an `OutboxMessage` to the same tracked `GoldInvoiceDbContext` used by the business use case. The caller saves the state transition and message together and commits the surrounding transaction before any dispatcher can claim it.

The stable message types are:

- `invoice.created.v1`
- `inventory.changed.v1`
- `order.status-changed.v1`
- `market-price.updated.v1`

The envelope contains the aggregate type and ID, correlation and optional causation metadata, a server-generated bounded audience, and a typed data object. The Outbox row ID is the event ID. Payloads deliberately exclude customer names, national IDs, provider configuration references, raw callbacks, credentials, and secrets.

Events are produced by the order, payment/invoice, inventory, reservation, and market-price services before their existing `SaveChanges`/commit boundary. Invalid or rejected market quotes do not emit `market-price.updated.v1`.

## Claim, retry, and shutdown behavior

SQL Server claiming uses one atomic CTE `UPDATE` with `UPDLOCK`, `READPAST`, and `ROWLOCK`. A claim selects due Pending/Failed messages and expired Processing locks, then writes a unique lock ID and expiry. Multiple API instances can therefore compete without intentionally delivering the same active claim.

The dispatcher:

- renews a message lock while a handler runs;
- marks a message Processed only after every registered handler succeeds;
- records only sanitized exception classifications for transient failures;
- uses capped exponential backoff;
- moves permanent contract failures or exhausted retries to DeadLetter;
- releases an in-flight claim without consuming an attempt during graceful cancellation;
- retains retry count and previous failure detail when an operator requests reprocessing.

Dead-letter operations require `Outbox.Reprocess`:

- `GET /api/v1/integration/outbox/dead-letters`
- `POST /api/v1/integration/outbox/dead-letters/{messageId}/reprocess`

Reprocessing requires a reason and rowversion. It writes an append-only `AuditLog`; retry history is not silently reset. Payload content is not returned by the inspection endpoint.

## SignalR security and recovery

The authorized Hub is exposed at `/hubs/integration`. Group membership is server-owned:

- `user:{user-id}` for the current authenticated subject;
- `role:{role}` from roles re-resolved during live access-token validation;
- `device:{device-id}` only when the requested existing Desktop device is active and belongs to the authenticated user.

The Hub exposes no client-callable group-join operation. Browser WebSocket negotiation may supply `access_token` in the query string only for the Hub path; every other API path continues to require the Authorization header.

The client receives the `integrationEvent` method with the event ID, type, occurrence time, aggregate identity, and safe typed data. Clients de-duplicate hints by event ID and reload authoritative resources after a hint.

Reconnect recovery is bounded and audience-scoped:

```text
GET /api/v1/integration/events?afterOccurredAt=<timestamp>&afterEventId=<guid>&pageSize=50
```

Both cursor fields are supplied together. The response returns the next scan cursor even when intervening messages are not visible to the caller, preventing cross-audience data disclosure and stalled recovery.

## Process topology

SignalR connections live in the API process, so the Outbox dispatcher is hosted by `GoldInvoice.Api`. SQL claiming still supports multiple API dispatcher instances. A future multi-node deployment must configure a supported SignalR scale-out backplane or managed SignalR service before expecting a notification emitted by one API node to reach connections attached to another node.

`GoldInvoice.Worker` now uses independent hosted services and schedules for market-price polling and reservation expiration. A failure or provider delay in one workload does not stop or postpone the other workload. Market-price commits still feed the same Outbox consumed by the API dispatcher.

## Configuration

```json
{
  "Outbox": {
    "BatchSize": 50,
    "PollIntervalMilliseconds": 1000,
    "LockDurationSeconds": 60,
    "HeartbeatIntervalSeconds": 20,
    "MaximumAttempts": 5,
    "RetryBaseDelaySeconds": 5,
    "MaximumRetryDelaySeconds": 300
  },
  "Worker": {
    "ReservationSweepIntervalSeconds": 30
  }
}
```

The heartbeat interval must remain shorter than the lock duration. Settings are validated during host startup.

## Verification coverage

Phase 6 adds tests for lock ownership, expired-lock recovery, heartbeat-compatible state transitions, retry timing, permanent dead-letter transition, graceful release, duplicate dispatch suppression, audience-scoped recovery, audited reprocessing, Hub authorization, absence of arbitrary group joins, and sensitive-field exclusion. Existing Phase 4/5 workflow tests now also verify valid-only market-price events and idempotent transactional Outbox production.

This environment does not contain the .NET SDK or SQL Server, so Release build, the complete test suite, the SQL Server concurrent-claim test, and `HasPendingModelChanges()` must be run on the user's .NET 8/SQL Server environment before Phase 6 is closed.
