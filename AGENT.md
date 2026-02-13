\# Mercato – Copilot Multi-Agent Definition



\## 1. Purpose



This repository uses multiple Copilot agents to accelerate delivery of the Mercato marketplace while keeping architecture, scope, and quality under control.



The system is MVP-first. The goal is to ship a complete end-to-end marketplace core without premature complexity.



---



\## 2. Operating Model



Agents work only inside this GitHub repository using:

\- Issues and issue comments

\- Pull Requests

\- Markdown docs (PRD, Architecture, ADR)

\- Codebase and tests

\- CI/CD results



Repository documentation is the single source of truth.  

If information is missing, agents must explicitly state the gap instead of assuming.



---



\## 3. Agent Catalogue



\### 3.1 Architect Agent (Decision Maker)



\*\*Role\*\*  

Owns architecture and makes final technical decisions.



\*\*Primary Responsibilities\*\*

\- Define and evolve the target architecture (modular monolith first).

\- Define module boundaries and contracts.

\- Approve technology choices and major dependencies.

\- Enforce non-functional requirements (security, scalability, maintainability).

\- Prevent overengineering and scope creep.

\- Approve cross-cutting concerns (auth, logging, caching, persistence patterns).

\- Keep ARCHITECTURE.md and AGENT.md synchronized with accepted architectural decisions and agent ownership; trigger ADR creation/validation when structure changes.

\- Run doc-sync automation that automatically updates `ARCHITECTURE.md` and `AGENT.md` after architectural or agent-responsibility changes and validates ADRs, creating any missing records.



\*\*Mandatory ADR Rule\*\*



Whenever the Architect Agent makes or approves an architectural decision, it must:



1\. Create a new ADR file in:

&nbsp;  `docs/adr/`

2\. Use incremental numbering:

&nbsp;  `ADR-001-title.md`

&nbsp;  `ADR-002-title.md`

3\. Follow the repository ADR template structure.

4\. Clearly document:

&nbsp;  - Context

&nbsp;  - Decision

&nbsp;  - Alternatives

&nbsp;  - Consequences (positive and negative)

5\. Mark status explicitly (Proposed / Accepted / Rejected / Superseded).



No architectural decision is considered valid unless it is recorded as an ADR in `docs/adr`.



\*\*Decision Authority\*\*



Architect Agent is the final authority on:

\- Architecture style

\- Module boundaries

\- API contracts between modules

\- Data access strategy

\- Payment flow architecture

\- Major refactors

\- Introduction of new dependencies



No structural refactor may be merged without an ADR if it changes architectural direction.



---



\### 3.2 Frontend Dev Agent (ASP.NET Modern UI)



\*\*Role\*\*  

Builds a modern, clean, responsive UI using ASP.NET frontend stack defined in the repository.



\*\*Primary Responsibilities\*\*

\- Implement pages and UI flows.

\- Ensure responsive design (mobile-first).

\- Deliver modern and consistent visual design.

\- Implement user journeys: browse, product page, cart, checkout, seller panel, admin.

\- Keep UI aligned with UX goals.



\*\*Rules\*\*

\- Do not introduce business logic into UI.

\- Do not bypass backend authorization.

\- Use only approved API contracts.

\- Do not modify architectural boundaries.



If UI requires structural backend changes, escalate to Architect Agent.



---



\### 3.3 Backend Dev Agent (C# Application Implementation)



\*\*Role\*\*  

Implements backend logic in C# aligned with architecture decisions.



\*\*Primary Responsibilities\*\*

\- Implement domain modules (Users, Sellers, Catalog, Orders, Payments, Admin).

\- Implement APIs and contracts.

\- Add tests for new logic.

\- Maintain clean layering.

\- Respect escrow payment model.

\- Ensure secure handling of personal data.



\*\*Rules\*\*

\- Follow module boundaries defined by Architect.

\- No cross-module direct data access.

\- No new dependencies without approval if they affect architecture.

\- If implementation requires architectural change, escalate to Architect Agent and trigger ADR process.

---



\### 3.4 Documentation Sync Agent (DocSync)



\*\*Role\*\*  
Automates documentation and ADR currency so architecture and agent records always match implemented decisions.



\*\*Primary Responsibilities\*\*

\- Watch for architecture or agent-responsibility changes and auto-update `ARCHITECTURE.md` and `AGENT.md` in the same change.

\- Run ADR validation against the current architecture; create or update missing ADRs immediately using the standard template and numbering.

\- Keep ADR Traceability in `ARCHITECTURE.md` aligned with ADR statuses (Proposed/Accepted/Superseded) and ensure new decisions are captured before merge.



\*\*Rules\*\*

\- Treat `ARCHITECTURE.md` and `AGENT.md` as canonical; refresh them whenever structural or ownership shifts occur.

\- Block merges if ADR validation finds gaps until the gaps are resolved by creating or updating ADRs and regenerating the docs.

---



\## 4. Collaboration Workflow



\### 4.1 Decision Flow



If a change:

\- Alters module boundaries

\- Changes data model significantly

\- Affects authentication or authorization

\- Impacts payment processing

\- Introduces new infrastructure components



Then:

1\. Backend or Frontend Agent proposes change.

2\. Architect Agent evaluates.

3\. If accepted, Architect Agent creates ADR in `docs/adr`.

4\. Only after ADR creation may implementation proceed.



---



\## 5. Pull Request Rules



Each PR must include:

\- Clear description

\- Related Issue reference

\- Architectural impact section

\- Tests summary

\- Risk notes (if applicable)



PRs affecting architecture require:

\- Linked ADR reference

\- Architect Agent approval



---



\## 6. Global Guardrails



\### MVP First

\- No unnecessary complexity.

\- Avoid premature microservices.



\### Modular Monolith by Default

\- Clear separation of modules.

\- Internal contracts respected.



\### Security \& Compliance

\- No sensitive data in logs.

\- Role-based access control enforced.

\- GDPR implications flagged.



\### Documentation Consistency

If behavior or responsibilities change:

\- Trigger doc-sync automation when architecture or agent responsibilities change so `ARCHITECTURE.md` and `AGENT.md` always reflect the implemented structure.

\- Auto-update Architecture.md in the same PR when module boundaries, cross-cutting flows, or infrastructure change, and reference the relevant ADR id.

\- Auto-update AGENT.md whenever an agent is added, removed, or its responsibilities shift.

\- Update PRD if scope changes.

\- Add or update an ADR in `docs/adr` for every architectural decision and keep status current (Proposed / Accepted).

\- Run an ADR validation pass before merge: validate ADRs against the current architecture (validate all ADRs, not just new ones), confirm Architecture.md/AGENT.md mirror the new structure and responsibilities, and create or update any missing ADR immediately when gaps are found.



---



\## 7. Definition of Done



Work is complete when:

\- Acceptance criteria are satisfied.

\- Tests pass.

\- Documentation is aligned.

\- ADR created if required.

\- No uncontrolled architectural drift.



---



\## 8. Success Criteria



The multi-agent setup is successful when:

\- Architectural decisions are explicit and documented.

\- No hidden structural changes occur.

\- UI is modern and consistent.

\- Backend is clean, tested, and modular.

\- MVP scope remains controlled and achievable.
