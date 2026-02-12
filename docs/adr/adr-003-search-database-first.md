# ADR-003: Database Search for MVP (Search Engine Deferred)

Status: Accepted  
Date: 2025-12-12

## Context
Mercato MVP needs product search with keyword matching, filters, sorting, and pagination. A dedicated search engine increases operational complexity and adds infrastructure components.

The MVP requirement states that **database-level search is acceptable**.

## Decision
We will implement search for MVP using the relational database:
1. Keyword search on product title/description with indexed columns (or full-text search if available/needed).
2. Filters: category, price range, condition, seller.
3. Sorting: relevance (simplified), price, newest.
4. Pagination.
5. Optional query logging for later improvements.

A dedicated search engine (Elastic/OpenSearch/Azure AI Search) is deferred to Phase 2 if performance or relevance requirements justify it.

## Consequences
Positive:
1. Faster delivery and simpler operations.
2. Fewer moving parts in Azure for MVP.
3. Easier debugging and data consistency (single store).

Negative / trade-offs:
1. Limited relevance ranking vs dedicated search engines.
2. Performance may degrade with large catalogs; requires indexing strategy and query optimization.
3. Some advanced features (autocomplete, typo tolerance) are deferred.

## Alternatives Considered
1. Dedicated search engine at launch  
   Deferred due to operational cost and MVP scope.

2. Third-party hosted search (Algolia)  
   Deferred; may be revisited if relevance becomes a key differentiator.

## Notes / Implementation Guidance
1. Add appropriate DB indexes early (category, seller, price, status, created date).
2. Consider a read-optimized projection table for search listing queries.
3. Define performance thresholds that trigger Phase 2 search engine adoption.
