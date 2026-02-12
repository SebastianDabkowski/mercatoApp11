# ADR-002: Przelewy24 Integration and Marketplace Escrow-like Flow

Status: Accepted  
Date: 2025-12-12

## Context
Mercato requires payments for multi-seller orders, commission calculation, refunds, and weekly payouts. The selected provider is **Przelewy24**. The MVP must support:
1. Creating payment intents/transactions for checkout.
2. Receiving payment status updates (callbacks/webhooks).
3. Handling refunds (full/partial where applicable).
4. Strong auditability and idempotency for payment events.

## Decision
We will integrate **Przelewy24** as the payment provider and implement the marketplace flow in the application using these rules:
1. A single Mercato payment transaction is created for checkout; order creation is confirmed only after payment confirmation.
2. Przelewy24 callbacks/webhooks are processed in the Payments module with:
   a. Signature verification (if available) and strict validation.  
   b. Idempotency keys per transaction/callback.  
   c. Durable persistence of raw webhook payload + processing result (audit).  
3. Payments module publishes domain events:
   PaymentConfirmed, PaymentFailed, RefundCompleted.
4. Orders module updates order state based on payment events.
5. Settlements module calculates commission and schedules weekly payouts.
6. Outbox pattern is used for reliable event publishing and notification triggering.

## Consequences
Positive:
1. Clear ownership: Payments module is the single source of truth for payment state.
2. Predictable checkout workflow: order is paid only after confirmed payment event.
3. Auditable and resilient processing of callbacks through idempotency + persistence.
4. Supports future provider change by keeping Przelewy24 behind an adapter.

Negative / trade-offs:
1. Marketplace “escrow” semantics may require legal/finance validation depending on the exact flow supported by Przelewy24 and business model.
2. More complexity in handling edge cases: delayed callbacks, retries, partial failures.
3. Refund flows can become complex for multi-seller orders and require clear policy.

## Alternatives Considered
1. Another provider with native marketplace split payments  
   Not selected for MVP due to current shortlist constraint.

2. Bank transfer-only MVP  
   Rejected due to worse conversion and poor buyer UX.

3. Create order before payment confirmation  
   Rejected due to higher risk of orphan orders and reconciliation issues.

## Notes / Implementation Guidance
1. All webhook handlers must be idempotent and safe to retry.
2. Store provider transaction IDs and correlate with internal IDs.
3. Implement reconciliation exports for finance operations.
4. Keep PII out of logs; use redaction policies.
