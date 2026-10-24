# Frontend implementation

## Visual source and scope

The provided Figma URL returned HTTP 403, and the repository contained no exported UI screens or Figma assets. The PNG files under `Documents` are database diagrams. No Figma frames, interactions, typography, spacing values, or assets were inspected, and no comparison against Figma was performed.

The user explicitly selected `Documents/Design Brief.docx` as the visual fallback. The implementation follows its reception-focused structure, neutral backgrounds, green active-pass states, red expired/exhausted-pass states, readable text, and large actions. The other documents under `Documents` supplied the customer, subscription, trainer, validation, and check-in requirements. Existing English interface conventions were retained; no currency was inferred from the Bulgarian requirements.

## Application structure

The existing .NET 10 project, conventional MVC routing, PostgreSQL schema, dependency injection, JWT cookies, and services remain in place. All new screens render through Razor views; there is no SPA or separate frontend application.

New MVC controllers use distinct names so the existing API action signatures and routes remain available:

| Route | Screen and integration |
| --- | --- |
| `/` or `/Home/Index` | Reception dashboard with real counts and recent visits. |
| `/Customers` | Search by name/email and paginated customer results. |
| `/Customers/Details/{id}` | Customer information, purchased passes with validity states, and recent visits. |
| `/Customers/Create` | Explains that receptionist customer creation is unavailable; no customer is created. |
| `/Customers/Purchase/{id}` | Membership-sale presentation with an explicit unavailable state; no purchase or payment is submitted. |
| `/Visits` | Searchable, date-filtered, paginated check-in history. |
| `/Visits/Create` | Select an existing valid customer pass and an optional trainer, then confirm entry through `ICheckinService`. |
| `/Visits/Details/{id}` | A recorded visit and related customer, pass, and trainer. |
| `/Visits/Edit/{id}` | Update only the visit's trainer through the existing service. |
| `/Visits/Delete/{id}` | Confirm cancellation through the existing service, which restores a visit to the pass. |
| `/Passes` | Pass-type search, pagination, details, creation, editing, and deletion. |
| `/Team` | Trainer search, pagination, details, creation, editing, and deletion. |
| `/Account/Login`, `/Account/Register` | Strongly typed forms backed by the existing asynchronous `AuthService`. |
| `/Account/Refresh`, `/Account/Logout` | POST forms for existing session renewal and sign-out behavior. |
| `/Home/Privacy`, `/Home/Error` | Existing supporting pages within the updated layout. |

Existing `/Trainers`, `/Gympass`, and `/Checkin` API controllers continue to expose `GetAll`, `GetOne`, `Create`, `Update`, and `Delete` with their original HTTP methods and binding. The `/auth/register`, `/auth/login`, `/auth/refresh`, and `/auth/session` endpoints also remain unchanged.

`ReceptionQueryService` supplies asynchronous, read-only projections from the existing `AppDbContext`. The original collection DTOs omit identifiers and some display fields, so those API responses are not sufficient for linked MVC lists. Projection queries retrieve real database records, use stable ordering and bounded pagination, and do not expose password hashes or token data.

The pass and trainer MVC controllers similarly use asynchronous EF read projections while routing mutations through `IGympassService` and `ITrainerService`. Forms enforce the documented positive validity/visit limits, nonnegative prices, database field lengths, unique pass names, and unique trainer phones. Referenced pass types and trainers cannot be deleted through these screens.

Check-in views remain under `Views/Checkin`, rendered by the new `VisitsController`. The existing check-in update API permits pass reassignment without reconciling visit balances. The MVC edit form therefore changes the trainer only and reloads the pass and original timestamp from the database. Check-in creation and cancellation retain the existing service's pass-validation and visit-balance behavior.

## Shared frontend

`Views/Shared/_Layout.cshtml` is the primary application layout. Shared Razor partials provide navigation, icons, notifications, pagination, and visit tables. Catalog forms and summaries are partials within their corresponding view folders. Strongly typed models live under `ViewModels`; business queries and mutations remain outside views.

The layout loads compiled Tailwind CSS in place of Bootstrap. `Styles/components.css` supplies reusable Tailwind component classes for panels, fields, buttons, notices, tables, and statuses. Tailwind utilities handle responsive layouts. Lightweight JavaScript provides mobile navigation, accessible interaction states, and form feedback; the existing jQuery validation libraries remain available for client validation. Server validation and antiforgery remain authoritative.

The standalone Tailwind 4.3.3 CLI is pinned by SHA256 in `scripts/tailwind-release.json`. `scripts/build-css.ps1` supports production compilation, development watch, and unminified builds. MSBuild runs CSS compilation before build/publish, and the generated `wwwroot/css/tailwind.css` is served through ASP.NET Core static assets. See [Frontend build](Frontend-Build.md) for exact commands, supported platforms, offline behavior, and source detection.

All new state-changing MVC forms use POST and antiforgery validation. Authentication calls reuse the existing HTTP-only cookie services, and return URLs are restricted to local paths. No global antiforgery filter was added to the API controllers. Database failures on the new reception screens render a reusable HTTP 503 page; account/catalog forms show actionable errors. Unavailable data is never replaced with invented statistics or demonstration records.

Reception date labels use `Europe/Sofia`. The original check-in service still stores local timestamps in PostgreSQL `timestamp without time zone`, while refresh-token timestamps remain UTC. The frontend does not migrate or reinterpret stored timestamps. Keep the application host/database local timezone aligned with Europe/Sofia so the preserved service's date rules and the reception indicators agree around midnight.

## Missing backend dependencies

| Requirement | Current boundary |
| --- | --- |
| Customer phone and phone search | The current `User`/customer entity has no phone field. Customer search uses the persisted name and email. Adding phone requires a separately reviewed data/service change. |
| Receptionist customer creation/editing | There is no dedicated customer-management service. Existing account registration signs in the new account, so it is presented as self-registration rather than silently replacing the receptionist's session. |
| Pass sales, renewals, and payment | Purchased-pass entities exist, but no purchase/renewal/payment service exists. The sale screen has an explicit unavailable state; no payment API or transaction is invented. |
| Live active workouts | The system records entry timestamps but has no exit/session-lifecycle data. The dashboard reports today's trainer-assisted visits rather than claiming to count active workouts. |
| Staff/admin roles | The existing user model and JWT contain no staff/role permissions, and the original management API controllers are unauthenticated. This frontend preserves that behavior and does not claim to implement staff authorization. |
| Exact Figma fidelity | Figma access is still required to confirm exact screens, assets, type, spacing, and responsive specifications. The approved document fallback is the implemented visual source. |

No database schema, migrations, payment integration, customer phone field, role model, or replacement business service was added as part of this frontend work.

## Significant file changes

- `Controllers/AccountController.cs`, `CustomersController.cs`, `VisitsController.cs`, `PassesController.cs`, and `TeamController.cs`: new MVC flows that preserve existing API controllers.
- `Controllers/HomeController.cs` and `Program.cs`: dashboard integration and registration of the read-only reception query service.
- `Service/ReceptionQueryService.cs`, `ViewModels/*.cs`, and `Filters/DatabaseUnavailableFilter.cs`: screen projections, typed forms, pagination, and controlled database-failure presentation.
- `Views/Shared`, `Views/Home`, `Views/Customers`, `Views/Checkin`, `Views/Account`, `Views/Passes`, and `Views/Team`: layout, shared components, and complete available screen flows.
- `Styles/tailwind.css`, `Styles/components.css`, `wwwroot/css/tailwind.css`, and `wwwroot/js/site.js`: compiled styling and lightweight interaction behavior.
- `FitnessApp.csproj`, `scripts/build-css.ps1`, `scripts/tailwind-release.json`, and `.gitignore`: reproducible frontend build integration and local tool-cache exclusion.

## Verification record

Completed verification on 27 October 2026:

- `dotnet build FitnessApp.slnx --nologo --no-restore`: passed, zero errors and eight pre-existing DTO-nullability/unused-catch warnings.
- `dotnet publish FitnessApp/FitnessApp.csproj --no-restore -c Release -o artifacts/publish --nologo`: passed. The published static assets include compiled, minified Tailwind CSS.
- Tailwind installation/checksum verification, compilation, incremental rebuild, and unchanged-input skipping: passed.
- `scripts/verify-mvc.ps1`: 56/56 HTTP and database assertions passed against an isolated PostgreSQL database cloned from the existing schema with synthetic fixtures. Checks covered antiforgery rejection, registration age/model validation, password hashing, login/logout/refresh-token rotation, local redirects, trainer/pass CRUD and uniqueness, deletion guards, check-in rejection/deduction/restoration, trainer-only edits, customer search/pagination, and existing API routes. The original application database was not modified.
- `scripts/verify-browser.ps1`: 78/78 route/viewport combinations passed across 26 paths at 1440×1000, 768×1024, and 390×844. All pages returned HTTP 200, loaded styling, and had no JavaScript console errors or horizontal page overflow. Keyboard Enter/Tab/Escape navigation, focus restoration, required-field validation, and POST antiforgery presence passed.
- Rendered screenshots were inspected for dashboard spacing, mobile layout, customer profiles, active/expired/exhausted pass states, populated tables, and check-in pass selection. Wide tables scroll within their containers.
- `git diff --check`: passed. Existing user changes to `appsettings.Development.json` and `.vs/` were left untouched.

Local test reports and screenshots are under ignored `artifacts/mvc-verification` and `artifacts/browser-final`. The integration script includes a guarded `-Cleanup` command for its recorded temporary database and server. Build/run and verification commands are documented in [Frontend build](Frontend-Build.md).

Exact Figma comparison remains unavailable because the design was not accessible. An additional database-outage injection command was rejected by automatic approval review without a detailed reason, so that specific failure-state test was not executed.
