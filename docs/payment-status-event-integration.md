# Payment Status Events in `payment-application`

## Purpose

This document describes the implementation steps for receiving a payment status
notification from `checkout-api` and notifying the correct Blip conversation.
The recommended receiver is `payment-application`, not the C# SDK.

This is an implementation proposal based on the Backstage artifacts and the
latest available review of [ADR 0005 in `payment-application`](https://github.com/blip-ai/payment-application/blob/main/docs/adr/0005-evento-status-pagamento.md).
The October 27, 2026 review points to HTTP delivery with an outbox and retries,
and treats the notification as a trigger. Confirm that this revision is approved,
merged, and deployed, and agree on its final HTTP contract before implementation.

## Existing Boundaries

- `checkout-api` already handles payment-provider callbacks and has a
  `PaymentStatusUpdateEvent` path for notifying merchant-configured webhooks.
  That merchant webhook is not, by itself, the internal delivery contract for
  `payment-application`.
- The latest ADR review describes a separate Checkout-to-`payment-application`
  delivery path. Prefer this authenticated internal HTTP callback over adding a
  Kafka consumer to the SDK or forwarding payment events as customer messages.
- In this SDK, `CheckoutAction` creates checkout links and can request saved-card
  payment. Its `checkout.pending` context variable correlates a short-lived
  saved-card choice; it is not durable payment-status correlation.
- The existing Builder flow does not announce approval for saved-card payments.
  Keep payment-status handling explicit; do not infer approval from a successful
  command response.

## Recommended Flow

1. `checkout-api` commits the payment status change and an outbox record together.
2. An outbox worker delivers a minimal, authenticated HTTP notification to
   `payment-application`, retrying delivery when necessary.
3. `payment-application` durably records and deduplicates the notification.
4. A worker resolves the purchase from context recorded by `payment-application`,
   then queries Checkout for the authoritative current status.
5. The application sends the configured status message to the conversation
   associated with that purchase and records the notification outcome.

The notification is a trigger only. It must not cause a charge, refund, purchase
mutation, or any other payment operation.

## Implementation Steps

### 1. Confirm the Cross-Repository Contract

- Confirm the latest ADR decision and ownership approval with the Payments team.
- Agree on the callback route, HTTP method, authentication mechanism, timeout,
  retry policy, and success response semantics.
- Freeze a versioned schema. The ADR describes identifiers and status rather
  than customer personal data; confirm the final fields, including the event
  deduplication identifier, purchase identifier, tenant, status, and occurrence
  time as applicable.
- Define behavior for unknown schema versions, invalid payloads, unknown
  purchases, and tenant mismatches. Do not silently treat an invalid event as a
  successful notification.
- Confirm the authenticated Checkout endpoint that returns the authoritative
  purchase status. Do not rely on a status included in the callback as final.

### 2. Persist Purchase-to-Conversation Context

When `payment-application` creates a purchase, persist a durable association
between the Checkout purchase identifier and the context needed to notify the
conversation. Include only fields required by the product, such as:

- tenant and merchant identifiers;
- bot identity and customer conversation identity;
- the configured success, pending, failure, or cancellation message/template;
- creation time, current processing state, and any correlation/version data
  required to reject stale updates.

Create this association before an event can be processed. Do not derive the bot,
customer, or destination from untrusted callback fields. Keep this record separate
from the SDK's expiring `checkout.pending` variable.

### 3. Add the Internal Callback Endpoint

- Add a dedicated route in `payment-application` for Checkout status callbacks.
- Authenticate Checkout as a service using the approved service-to-service
  mechanism. Validate issuer, audience, expiry, and required claims where the
  chosen token format supports them; do not accept merchant credentials as
  service authentication.
- Validate content type, payload size, required fields, schema version, and
  identifier formats before accepting the event.
- Avoid logging tokens or full payloads. The callback should contain no customer
  personal data unless the contract explicitly requires it.

### 4. Make Receipt Durable and Idempotent

- Persist each accepted callback in an inbox or equivalent durable work store
  before returning the success response that stops Checkout retries.
- Enforce uniqueness on the event identifier so repeated delivery of the same
  event does not create duplicate work.
- Return a retryable failure if durable persistence is unavailable. Return
  success for an already-persisted duplicate.
- Keep request handling short; process payment lookup and message delivery in a
  background worker rather than holding the Checkout request open.

### 5. Process and Verify the Status

- Resolve the purchase association using the purchase identifier from the event.
- Reject and count tenant mismatches against the locally recorded purchase
  context. Ignore callback destination or identity fields for recipient selection.
- Query Checkout through its authenticated internal API and use the returned
  status as the source of truth.
- Apply an explicit status-transition policy. Ignore stale or disallowed
  transitions; never regress a terminal state because events arrived out of order.
- Treat missing purchase context, temporary Checkout errors, and invalid status
  responses as explicit outcomes with appropriate retry or quarantine behavior.

### 6. Notify the Conversation Exactly Once in Effect

- Select the message configured for the verified status and the persisted
  conversation context.
- Persist notification work before sending, preferably through an outbox so a
  process restart cannot lose an accepted status update.
- Deduplicate not only by event identifier but also by the effective notification
  key (for example, purchase plus status and transition/version). Different event
  identifiers can represent the same effective status.
- Record send attempts and outcomes. If the messaging transport cannot guarantee
  idempotent sends, document and handle the remaining duplicate-delivery window.
- Do not pass the callback into the Builder as ordinary user text. If automatic
  flow resumption is a product requirement, define a separate typed internal
  event and validate the user's current flow/session before resuming it.

### 7. Add Operations and Security Controls

- Emit structured metrics for callback acceptance, duplicate events, invalid
  payloads, tenant mismatches, processing latency, Checkout lookup failures,
  notification outcomes, retry counts, and pending outbox age.
- Alert on sustained delivery backlog and repeated processing failures.
- Apply bounded retries with backoff and an operational path to inspect and
  replay quarantined events safely.
- Limit access to stored conversation context and define retention for inbox,
  outbox, and audit records. Never log secrets or service tokens.

### 8. Test the End-to-End Behavior

Cover at least:

- valid callback, status lookup, and message sent to the recorded conversation;
- duplicate callback and distinct events that resolve to the same status;
- out-of-order status updates and attempts to regress a terminal state;
- invalid authentication, schema, version, or identifiers;
- unknown purchase and tenant mismatch;
- persistence failure before acknowledgement;
- Checkout or messaging outage, retry, process restart, and replay;
- callback fields attempting to redirect the notification to another tenant,
  bot, or customer;
- no charge, refund, or purchase mutation as a side effect of event processing.

## Acceptance Criteria

- An accepted callback is durably stored before Checkout is acknowledged.
- Duplicate delivery does not cause duplicate effective customer messages.
- Only a purchase created and correlated by `payment-application` can notify a
  conversation, and its destination comes from the persisted context.
- The message reflects the current status verified with Checkout, not an
  unverified callback value.
- Temporary failures are observable and recoverable without losing accepted
  events or initiating payment operations.
- The behavior is covered by automated tests and documented operational metrics.

## Scope for This SDK

No SDK change is required to receive the internal Checkout callback when
`payment-application` owns the endpoint and sends the conversation message via
Iris. Revisit the SDK only if product requirements call for a reusable Builder
block to configure status messages or a typed, validated flow-resumption event.

The SDK's existing Checkout behavior is documented in
[`sending.md`](sending.md#builder-checkout-with-a-saved-card). The receiving
interfaces documented in [`receiving.md`](receiving.md) handle Blip/LIME
messages and notifications; they are not a generic Checkout webhook endpoint.