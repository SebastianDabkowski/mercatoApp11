# ADR-004: Multi-language Support with PLN-only Currency in MVP

Status: Accepted  
Date: 2025-12-12

## Context
Mercato MVP requires multi-language support, but currency is limited to PLN. The UI and selected content must be localized, while financial calculations remain in PLN.

## Decision
We will implement multi-language (i18n) across the web application:
1. UI strings localized using a standard Angular i18n approach (or equivalent localization library).
2. Domain-facing text fields (product title/description, store description) remain user-generated and are not auto-translated in MVP.
3. System-generated content (emails, notifications, legal texts) is template-based and localized.
4. Currency is fixed to **PLN** for all transactions in MVP; money is stored as integer minor units (grosze) with explicit currency code.
5. Admin can manage which languages are enabled and edit localized legal content and templates.

## Consequences
Positive:
1. Multi-language UX from day one without complicating settlement logic.
2. Data model remains future-proof by keeping currency code and minor units format.
3. Clear separation between localized UI/system templates and user content.

Negative / trade-offs:
1. Localization increases QA scope (multiple languages to test).
2. Admin workflows for template/legal content management add configuration complexity.
3. Future multi-currency will require additional domain rules (tax, rounding, pricing, payouts).

## Alternatives Considered
1. Single-language MVP  
   Rejected because multi-language is a stated requirement.

2. Multi-language + multi-currency in MVP  
   Rejected due to scope and risk; deferred to later phase.

## Notes / Implementation Guidance
1. Use culture-based routing or user preference stored in Identity profile.
2. Keep monetary values as integers and avoid floating point.
3. Ensure email templates are versioned and testable per language.
