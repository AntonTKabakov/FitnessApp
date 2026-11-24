# Fitness App

The Assignment 9 migration now provides a separate vanilla TypeScript/Vite frontend in `FitnessApp.Frontend` and JSON APIs under `/api`. Follow [the decoupled application instructions](FitnessApp.Frontend/README.md) for current startup commands, endpoints, Docker configuration, and verification limits. The older MVC documentation below describes the retained compatibility implementation; legacy MVC routes are disabled by default. See [the implementation checklist](ASSIGNMENT9-CHECKLIST.md) for outstanding work.

A fitness management system for gym reception: find customers, inspect memberships, register visits, and manage trainers and subscription types.

## Features

- Reception dashboard with customer search, daily visit counts, and recent check-ins.
- Customer directory and profiles with purchased passes, remaining visits, expiry status, and recent history.
- Customer pass assignment from the catalog, with automatic purchase/expiry dates and visit allowance.
- Check-in registration with an optional trainer, validation of existing pass rules, trainer corrections, and visit cancellation.
- Gym-pass catalog and trainer management with search, pagination, and validated forms.
- Account registration, sign-in, session renewal, and sign-out through the existing JWT and refresh-token services.
- Admin/User roles: admins manage the club and account roles; users see only their own profile, memberships, and check-ins.
- Profile pictures with preview, upload progress, replacement/removal, private retrieval, and persistent WebP storage. See [Profile pictures](Documents/Profile-Pictures.md) for API examples, migration, configuration, and deployment.
- Global error handling with safe JSON responses, styled 404/500 pages, private daily error logs, and automated service tests. See [Error handling and testing](Documents/Error-Handling-and-Testing.md) for configuration, test commands, coverage, and file inventory.

Receptionist customer creation, phone-based customer search, extending existing passes, and payment processing still need backend support. Their status and the current application boundaries are described in [Frontend implementation](Documents/Frontend-Implementation.md).

To give a customer a pass, open **Customers → View profile → Sell a pass**, choose a pass type, and select **Confirm and assign pass**. The new membership appears in the customer's profile and the check-in selection. If the catalog is empty, create a type under **Gym passes → Add pass type** first. Payment is collected separately.

Sign-in is required for application pages and APIs. New and migrated accounts default to User. Admins can manage roles under **Account roles**. See [Roles and account access](Documents/Account-Access.md) for the permission matrix, database migration, explicit first-admin setup, and verification commands.

## Technologies

- ASP.NET Core MVC on .NET 10, Razor views, and the existing Web API endpoints.
- Tailwind CSS 4.3.3, compiled locally with the pinned official standalone CLI.
- Lightweight JavaScript and the existing jQuery form-validation libraries.
- Entity Framework Core and PostgreSQL.

The separate frontend requires Node.js and npm. The older server-rendered application remains available only through the explicit `LegacyMvc:Enabled` compatibility setting.

## Design

The original [Figma design](https://www.figma.com/design/VS9yETQddXPlMhJwyhP98j/Untitled?node-id=0-1) returned HTTP 403 during implementation. With the user's approval, [Documents/Design Brief.docx](Documents/Design%20Brief.docx) became the visual fallback, supported by the product requirements and application-flow documents. The interface uses neutral backgrounds, green/red pass states, clear forms, and large reception actions. Exact Figma typography, dimensions, assets, and screen parity have not been verified.

## Run locally

Requirements: a .NET SDK capable of building `net10.0`, the .NET 10 ASP.NET Core runtime, PostgreSQL, and Windows PowerShell 5.1 or PowerShell 7. The first CSS build downloads the checksum-verified Tailwind executable from GitHub into the ignored `.tools` directory.

Configure `ConnectionStrings:DefaultConnection` and `Jwt:AccessSecret` using your local configuration, environment, or secret store. Environment-variable equivalents are `ConnectionStrings__DefaultConnection` and `Jwt__AccessSecret`; the signing secret must contain at least 32 UTF-8 bytes. Review the existing `AuthCookie` settings for the selected environment. Keep secrets out of source control.

From the repository root:

```powershell
dotnet restore FitnessApp.slnx
dotnet build FitnessApp.slnx

# Apply the existing migrations with a compatible EF Core 10 CLI when needed.
dotnet ef database update --project FitnessApp/FitnessApp.csproj

dotnet run --project FitnessApp/FitnessApp.csproj --launch-profile https
```

The existing HTTPS launch profile listens on `https://localhost:7076` and `http://localhost:5290`. Use the existing configured database; the frontend does not insert demo customers, memberships, or trainers.

For development, run the CSS watcher in a second terminal:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ./scripts/build-css.ps1 -Watch
```

## Production build

```powershell
dotnet publish FitnessApp/FitnessApp.csproj -c Release -o artifacts/publish
```

Publishing compiles minified Tailwind CSS. Configure the production connection string, signing secret, and HTTPS-compatible cookie settings, apply the database migrations, and provide a persistent private profile-picture directory before running the published application. See [Profile pictures](Documents/Profile-Pictures.md) for storage settings, Docker volumes, and recovery.

See [Frontend build](Documents/Frontend-Build.md) for CSS build/watch options and offline CI, and [Frontend implementation](Documents/Frontend-Implementation.md) for routes, architecture, integration limits, and verification notes.

## Business logic patterns and integration fixes (Assignment 10)

The frontend now uses same-origin `/api` requests by default: Vite forwards them to port 5290 during development, and Nginx forwards them to the backend container in Compose. Start both applications using [the frontend instructions](FitnessApp.Frontend/README.md), then open http://localhost:5173. Existing route names, JSON contracts, roles, database mappings, and frontend styling are retained. No migration is needed.

### Repository pattern

Previously `CustomerPassService` and `CheckinService` mixed membership rules with EF queries, tracking, persistence, and transaction management. `ICustomerPassRepository` / `CustomerPassRepository` now handle customer/catalog lookup and membership persistence. `ICheckinRepository` / `CheckinRepository` handle visit queries, tracked membership/visit lookup, persistence, and serializable transaction execution. Neither service depends on EF Core or `AppDbContext` anymore. These are focused repositories; no generic CRUD abstraction or exposed `IQueryable` is added.

Purchase validity, expiry calculation, visit allowance, duplicate daily entry, and refund rules remain in services. The service coordinates a transaction callback while the repository owns its EF implementation. Creation keeps the existing serializable boundary around validation and balance decrement. Cancellation now also reads and refunds the balance inside one serializable transaction. Edits reject changing the membership or timestamp, accept unchanged trainers, and validate trainer existence. This closes paths that could bypass balance/daily-entry validation.

```csharp
builder.Services.AddScoped<ICustomerPassRepository, CustomerPassRepository>();
builder.Services.AddScoped<ICheckinRepository, CheckinRepository>();
// Services receive the interfaces through constructor injection.
```

Repositories and business services are scoped, sharing the request's scoped `AppDbContext`. Another storage implementation can replace either repository registration without changing the business rules. Relational tests use SQLite with the real repositories, including rollback after a save followed by a failure. Existing query-only reporting, authentication/profile persistence, catalog code, and compatibility controllers still use EF directly; this is a focused extraction of membership and check-in workflows, not a claim that every database access in the project has been converted.

### Strategy pattern

PDF and Excel are existing alternative algorithms for exporting the same statistics report. Previously both statistics controllers depended on both concrete generators and duplicated output metadata/filename construction. `IReportExportStrategy` now defines format, availability, content type, extension, and `Generate`. `PdfReportService` and `ExcelReportService` implement it. `ReportExportService` receives `IEnumerable<IReportExportStrategy>`, selects by format, and generates consistent export metadata. The MVC and JSON statistics controllers use this coordinator; `StatsService` also queries availability through it instead of depending on the PDF implementation.

```csharp
var export = exports.Generate("excel", report);
return File(export.Content, export.ContentType, export.Filename);
```

The generators, interface mappings, and coordinator are singletons. Each generator creates per-call document/workbook state. Registration factories alias the existing concrete registrations so each strategy has one instance; service code does not resolve dependencies from a container. PDF still requires an explicitly configured QuestPDF license. A future format implements the interface and is registered with DI; the coordinator does not require a new switch branch. Exposing that format publicly would also require a route/UI entry.

### Verification and limits

Backend regression coverage includes membership purchases, entry validation, trainer edits, cancellation/refund, repository rollback, API authorization/antiforgery, catalog CRUD, profile pictures, and report generation. The added API workflow test exercises purchase, member entry, duplicate rejection, ownership checks, trainer assignment, referenced-record deletion protection, and cancellation through the endpoints used by the frontend. Strategy tests check extension with another implementation, unavailable formats, and valid Excel output; existing statistics tests exercise PDF and Excel output.

Frontend tests exercise CSRF response headers, delayed concurrent unauthorized responses sharing one refresh, binary download renewal, and failed refresh termination. Downloads now use the shared API client. Run `npm test --prefix FitnessApp.Frontend`, `npm run build --prefix FitnessApp.Frontend`, and `dotnet test FitnessApp.slnx -p:BuildTailwind=false --disable-build-servers`. The CSS flag reuses the existing compiled stylesheet.

Live PostgreSQL concurrency, Docker startup, and interactive browser checks require their respective environments; SQLite/API tests do not establish those results. The existing PostgreSQL test is conditional, and Windows symbolic-link tests require host support. Existing compiler/analyzer warnings are separate from test failures.
