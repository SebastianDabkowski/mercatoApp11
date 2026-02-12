# ADR-001: Modular Monolith Architecture

Status: Accepted  
Date: 2025-12-12

## Context
Mercato MVP must be delivered quickly with a predictable delivery path, while keeping clear boundaries between business areas (identity, product, orders, payments, settlements, etc.). The platform will integrate with external providers (Przelewy24, email, storage) and must remain maintainable as scope grows.

Key constraints:
1. Backend: ASP.NET Core (C#).
2. Cloud: Azure.
3. Single web application (no separate public API layer in MVP).
4. Clear module boundaries to avoid a “big ball of mud”.

## Decision
We will implement Mercato as a **Modular Monolith**:
1. One deployable unit and one runtime.
2. Internal business modules (bounded contexts) with strict boundaries.
3. Each module owns its domain model and persistence schema (table ownership + migrations).
4. Cross-module communication via explicit module interfaces and domain events.
5. External integrations are behind module-owned adapters.

## Consequences
Positive:
1. Faster MVP delivery and simpler operations than microservices.
2. Lower integration overhead and easier transactional consistency for checkout/payments/refunds.
3. Clear separation of concerns and lower long-term maintenance cost if boundaries are enforced.
4. Easier evolution: modules can be extracted later if needed.

Negative / trade-offs:
1. Requires discipline: dependency rules, module contracts, and schema ownership must be enforced.
2. Risk of accidental coupling if developers bypass module boundaries.
3. Scaling is “whole app” by default, though background jobs and infra can be scaled separately.

## Alternatives Considered
1. Microservices from day one  
   Rejected due to delivery risk, increased operational complexity, and slower feedback loops for MVP.

2. Traditional layered monolith without module boundaries  
   Rejected due to high risk of coupling and long-term maintainability issues.

3. Modular monolith + multiple deployables (semi-distributed)  
   Deferred until a real scaling/organizational need is proven.

## Notes / Implementation Guidance
1. Enforce module boundaries with solution structure + analyzers (dependency rules).
2. Use an Outbox pattern for domain events that trigger notifications/integration work.
3. Treat each module as a bounded context with its own persistence schema.
