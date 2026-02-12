# Mercato
Multi-Vendor Marketplace Platform

## Overview
Mercato is a multi-vendor marketplace platform that connects buyers and sellers in one unified system. Sellers can onboard, manage products, handle orders and payouts, while buyers can browse multiple stores, purchase in a single checkout, and track orders centrally.

The primary goal is to deliver a scalable marketplace MVP that validates the business model and can evolve into a full e-commerce ecosystem.

## Business Goal
Mercato is built around a commission-based revenue model. The platform earns when sellers generate sales. Additional monetization options such as subscriptions, promoted listings, or premium seller features are planned for later phases.

## Users
The platform serves three main user groups:
- Buyers who browse products, place orders, and manage purchase history.
- Sellers who manage stores, catalogs, orders, and payouts.
- Administrators who moderate content, configure the platform, and monitor KPIs.

## MVP Scope
The MVP focuses on a complete end-to-end purchase flow:
- User registration and authentication.
- Seller onboarding and store profiles.
- Product catalog management.
- Search, filtering, and browsing.
- Shopping cart and checkout.
- Online payments with escrow logic.
- Basic order management and status tracking.
- Email notifications.

Out of scope for MVP are mobile apps, loyalty programs, advanced analytics, and deep third-party integrations.

## Architecture
Mercato follows a modular monolith approach. Core domains such as identity, catalog, orders, and payments are clearly separated at the code and responsibility level. The architecture is cloud-ready and designed to support future scaling and service extraction if required.

Key architectural principles:
- Clear module boundaries.
- API-first thinking inside the system.
- Cloud deployment with scalability and security in mind.
- Readiness for future integrations.

## Security and Compliance
The platform is designed to be GDPR-compliant. Mercato acts as the central data controller. Sellers receive access only to data required to fulfill orders. Security, access control, auditability, and secure payment handling are treated as first-class concerns.

## Documentation Approach
The project uses structured documentation:
- PRD for product requirements.
- ADRs for architectural decisions.
- Epics and user stories defined in structured JSON.
This ensures traceability from business goals to implementation.

## Project Status
The project is in the MVP definition and architecture phase. Requirements and scope are validated based on stakeholder interviews. Development is planned to proceed iteratively, starting with core marketplace capabilities.

## Next Steps
- Finalize MVP backlog.
- Confirm technology stack and cloud provider.
- Prepare development estimates.
- Start implementation of core modules.
