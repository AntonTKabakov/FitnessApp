# Separate FitnessApp frontend

Vite, strict TypeScript, native DOM APIs, native Fetch, HTML templates and the existing compiled CSS. No frontend framework or C# rendering is used. Dependencies are TypeScript and Vite (development/build only); `package-lock.json` pins the installed versions.

## Development

Configure the existing backend connection string and signing secret through user secrets or environment variables. In the backend terminal:

```powershell
$env:AuthCookie__Secure = 'false'
dotnet run --project FitnessApp/FitnessApp.csproj --launch-profile http
```

In another terminal, from the repository root:

```powershell
npm ci --prefix FitnessApp.Frontend
$env:VITE_API_URL = ''
npm run dev --prefix FitnessApp.Frontend -- --port 5173 --strictPort
```

Open **http://localhost:5173**. Vite proxies `/api` to `http://localhost:5290`, so cookies, profile pictures, and downloads use the frontend origin. Set `API_PROXY_TARGET` if the backend uses another address. The same proxy is available in `npm run preview`.

This HTTP configuration is for local development. To deliberately use separate origins, set `VITE_API_URL` to the backend URL and configure `Frontend:AllowedOrigins` on the backend. Use the same hostname and scheme for both servers so SameSite cookies work. For HTTPS deployment, use HTTPS and keep secure cookies enabled. Never use a wildcard CORS origin for credentialed requests. For cross-site deployment, configure `AuthCookie:SameSite=None` with `AuthCookie:Secure=true`; browser third-party-cookie restrictions still apply.

The frontend API URL is configured at build time with `VITE_API_URL`. The backend defaults to API-only controller discovery. `LegacyMvc:Enabled=true` temporarily enables legacy endpoints for compatibility; existing regression fixtures explicitly opt in. The separate frontend never calls these legacy endpoints.

## Build and verification

```powershell
dotnet build FitnessApp.slnx -p:BuildTailwind=false --disable-build-servers
npm run build --prefix FitnessApp.Frontend
npm test --prefix FitnessApp.Frontend
dotnet test FitnessApp.Tests/FitnessApp.Tests.csproj --no-build
```

The frontend reuses `public/css/tailwind.css`, copied from the existing application. If the design stylesheet changes, rebuild the existing stylesheet and refresh that copy. Nginx's `try_files` fallback and Vite's SPA fallback support direct navigation to frontend routes.

## API surface

| Resource | Endpoints |
| --- | --- |
| Session | GET `/api/session/antiforgery`; POST `/api/session`, `/api/session/registration`, `/api/session/refresh`; DELETE `/api/session` |
| Current account | GET `/api/users/me`, `/api/members/me` |
| Profile pictures | Existing POST/DELETE `/api/users/me/profile-picture`; GET `/api/users/{id}/profile-picture` |
| Dashboard | GET `/api/dashboard` |
| Customers | GET `/api/customers`, `/api/customers/{id}`, `/api/customers/{id}/pass-options`; POST `/api/customers/{id}/passes` |
| Pass catalog | GET/POST `/api/passes`; GET/PUT/DELETE `/api/passes/{id}` |
| Trainers | GET/POST `/api/trainers`; GET/PUT/DELETE `/api/trainers/{id}` |
| Visits | GET/POST `/api/visits`; GET/PUT/DELETE `/api/visits/{id}`; GET `/api/visits/options`, `/api/visits/trainers` |
| Account roles | GET `/api/accounts`; PUT `/api/accounts/{id}/role` |
| Statistics | GET `/api/statistics`, `/api/statistics/Data`, `/api/statistics/ExportPdf`, `/api/statistics/ExportExcel` |

Catalog searches accept `search` and `page`. Customers and visits accept `query` and `page`; visits additionally accept `date`. Statistics retain the existing `range`, `start`, and `end` filters. Mutating JSON requests send the `RequestVerificationToken` header obtained from the session antiforgery endpoint. Cookies are HttpOnly; tokens are never stored in localStorage. The Fetch client coordinates refresh requests and retries a protected request once following successful renewal.

Catalog edits preserve duplicate checks and reject deletion of referenced records. Visit edits only change the trainer, preserving the database timestamp and membership assignment. Creation and deletion use the existing balance/transaction logic. Members are scoped to their own memberships and visits. Account roles use the existing safeguards.

## Docker

`compose.yaml` defines independent backend, frontend/Nginx, and PostgreSQL services. Set `POSTGRES_PASSWORD` and `JWT_ACCESS_SECRET` in your shell or an untracked environment file before running `docker compose up --build`. The signing secret requires at least 32 UTF-8 bytes. No database is migrated automatically. Start the database first with `docker compose up -d database`, apply existing EF migrations using a connection string targeting `localhost:5433`, then start the applications. PostgreSQL and profile pictures use persistent volumes.

The Compose file is a local HTTP development configuration. Production requires HTTPS termination, secure cookies, persistent data-protection keys, and the existing reporting/storage settings. By default Nginx forwards `/api/` to `backend:8080`; the browser only needs frontend port 5173. API port 5290 remains available for direct access. `PUBLIC_API_URL` can override the frontend's API address during its Docker build; for that separate-origin mode, `FRONTEND_ORIGIN` controls backend CORS.

## Verification limits

Backend and frontend builds pass. Integration tests cover API-only controller discovery, principal read endpoints, credentialed CORS, catalog CRUD, authorization, antiforgery, and session login/refresh/logout. Existing service and security tests also run.

Docker execution is unverified because the Docker daemon was unavailable. Browser interaction and screenshot comparisons have not been performed. Page-by-page visual parity remains unfinished; some rendering is still in TypeScript rather than separate page HTML templates, and the profile-picture upload lacks the previous percentage-progress display. The repository checklist tracks these remaining requirements. Existing unsupported features (receptionist customer creation, payment processing, extending memberships) remain unsupported.
