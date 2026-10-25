# Fitness App

A fitness management system for gym reception: find customers, inspect memberships, register visits, and manage trainers and subscription types.

## Features

- Reception dashboard with customer search, daily visit counts, and recent check-ins.
- Customer directory and profiles with purchased passes, remaining visits, expiry status, and recent history.
- Customer pass assignment from the catalog, with automatic purchase/expiry dates and visit allowance.
- Check-in registration with an optional trainer, validation of existing pass rules, trainer corrections, and visit cancellation.
- Gym-pass catalog and trainer management with search, pagination, and validated forms.
- Account registration, sign-in, session renewal, and sign-out through the existing JWT and refresh-token services.

Receptionist customer creation, phone-based customer search, extending existing passes, payment processing, and staff roles still need backend support. Their status and the current application boundaries are described in [Frontend implementation](Documents/Frontend-Implementation.md).

To give a customer a pass, open **Customers → View profile → Sell a pass**, choose a pass type, and select **Confirm and assign pass**. The new membership appears in the customer's profile and the check-in selection. If the catalog is empty, create a type under **Gym passes → Add pass type** first. Payment is collected separately.

## Technologies

- ASP.NET Core MVC on .NET 10, Razor views, and the existing Web API endpoints.
- Tailwind CSS 4.3.3, compiled locally with the pinned official standalone CLI.
- Lightweight JavaScript and the existing jQuery form-validation libraries.
- Entity Framework Core and PostgreSQL.

There is one server-rendered application. No Node.js, npm, browser Tailwind CDN, or separate frontend application is required.

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

Publishing compiles minified Tailwind CSS. Configure the production connection string, signing secret, and HTTPS-compatible cookie settings in the hosting environment, then run the published application using the existing deployment process. No schema changes were introduced for the frontend.

See [Frontend build](Documents/Frontend-Build.md) for CSS build/watch options and offline CI, and [Frontend implementation](Documents/Frontend-Implementation.md) for routes, architecture, integration limits, and verification notes.
