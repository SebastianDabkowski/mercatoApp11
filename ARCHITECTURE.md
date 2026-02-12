# Architecture Overview

This solution uses a modular, layered architecture with a Razor Pages Web App and a feature module for Products.

## Projects
- `Application/SD.ProjectName.WebApp`: Razor Pages UI, app startup, DI, EF Core Identity.
- `Modules/SD.ProjectName.Modules.Products`: Feature module containing `Domain`, `Application`, `Infrastructure` for Products.
- `Tests/SD.ProjectName.Tests.Products`: Unit tests for module/application logic.

## Module Structure
- `Domain`: Core entities and repository interfaces.
  - `ProductModel`
  - `Interfaces/IProductRepository`
- `Application`: Use cases (application services) depending on `Domain` interfaces.
  - `GetProducts`
- `Infrastructure`: Implementations and persistence for the module.
  - `ProductDbContext` + `Migrations`
  - `ProductRepository` (implements `IProductRepository`)

## Data
- Web App has `ApplicationDbContext` for Identity.
- Products module has its own `ProductDbContext` with separate migrations.

## Dependency Flow
- WebApp -> Application (use cases) -> Domain (interfaces)
- Infrastructure implements Domain interfaces and is registered in DI at startup.
- Tests reference Application and Domain, mocking `IProductRepository`.

## DI and Startup
- `Program.cs` configures:
  - Razor Pages
  - Identity `ApplicationDbContext`
  - Products module `ProductDbContext`
  - Registers `IProductRepository` with `ProductRepository`
  - Registers application services like `GetProducts`

## UI Pages (Razor Pages)
- Pages in `Application/SD.ProjectName.WebApp/Pages/*`
- Example: `Pages/Products/List.cshtml` + `List.cshtml.cs` consumes `GetProducts` via DI.

---

# Working With This Architecture (Agent Guide)

## General Rules
- Keep business rules in `Domain`.
- Add application behavior in `Application` as small, testable services/use-cases.
- Keep EF Core and external integrations in `Infrastructure`.
- UI (Razor Pages) calls Application services via DI.

## Common Tasks
- Update `Program.cs` only for DI, DbContexts, and minimal web configuration.
- Put migrations under the correct DbContext project (WebApp vs Products module).
- Write unit tests in `Tests/*` using Moq for interfaces.

---

# Adding a New Feature (Example: Create Product)

1) Domain
- Add entity changes if needed (e.g., validations).
- Add interface method to `Interfaces/IProductRepository` (e.g., `Task Add(ProductModel product)`).

2) Infrastructure
- Implement the new method in `ProductRepository`.
- Update `ProductDbContext` entity configuration if needed.
- Add EF Core migration in the Products module if schema changes:
  - Run from solution directory: `dotnet ef migrations add <Name> -p Modules/SD.ProjectName.Modules.Products -s Application/SD.ProjectName.WebApp -c ProductDbContext`
  - Update database: `dotnet ef database update -p Modules/SD.ProjectName.Modules.Products -s Application/SD.ProjectName.WebApp -c ProductDbContext`

3) Application
- Create a new use case (e.g., `CreateProduct`) that depends on `IProductRepository`.
- Keep logic small and testable; avoid EF specifics here.

4) Web UI (Razor Pages)
- Add page model (e.g., `Pages/Products/Create.cshtml.cs`) injecting `CreateProduct`.
- Add view `Create.cshtml` with form binding.

5) DI Registration (Program.cs)
- Ensure `IProductRepository` and new use case (`CreateProduct`) are registered.

6) Tests
- Add unit tests in `Tests/SD.ProjectName.Tests.Products`:
  - Mock `IProductRepository` to verify interactions.
  - Test success, validation, and edge cases.

---

# Conventions
- Application services: simple classes with clear method names (`GetList`, `Create`, `Update`).
- Avoid coupling Application to EF Core—use interfaces.
- Keep Razor Pages lean; delegate work to Application.
- One DbContext per bounded context (Identity vs Products).

# Performance & Maintainability
- Query shaping is in repository implementations.
- Use async everywhere (`Task`, `await`).
- Prefer small, composable services.

# Checklist When Modifying
- Domain API changes require Infrastructure updates and tests.
- Schema changes require migrations in the correct project and DbContext.
- Update DI registrations in `Program.cs`.
- Add/adjust Razor Pages if the UI changes.
- Ensure unit tests cover new paths.

# Useful Paths
- WebApp: `Application/SD.ProjectName.WebApp`.
- Products Module: `Modules/SD.ProjectName.Modules.Products`.
- Tests: `Tests/SD.ProjectName.Tests.Products`.

# Notes
- Target: .NET 9, C# 13.
- Prefer minimal changes that follow existing patterns.
