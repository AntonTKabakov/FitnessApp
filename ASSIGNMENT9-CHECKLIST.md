# Assignment 9 implementation checklist

- [x] Preserve initial CORS and profile role changes.
- [x] Implement separate JSON catalog endpoints with validation and deletion safeguards.
- [x] Implement JSON reception, customer, membership, visit, and account-role operations.
- [x] Verify catalog CRUD, read endpoints, role restrictions, and session antiforgery with integration tests against API-only hosting.
- [x] Complete JSON statistics and session/antiforgery endpoints.
- [x] Disable legacy MVC controllers by default; frontend business operations call only JSON APIs.
- [x] Create separate strict TypeScript/Vite application and typed Fetch client.
- [x] Implement frontend routes for authentication, dashboard, member profiles, customers, purchases, visits, catalogs, roles, statistics, privacy, and errors.
- [ ] Complete page-by-page visual parity against every original view (including icons, details layouts, profile-picture progress, and exact chart behavior).
- [ ] Finish moving page markup into HTML templates and splitting page modules out of main.ts.
- [x] Verify session login/refresh/logout and HttpOnly cookies in integration tests.
- [ ] Verify profile picture upload and expired-session recovery end to end in a browser.
- [x] Configure independent Docker services and document development commands.
- [ ] Build/run Docker services (daemon unavailable in this environment).
- [x] Build backend and strict TypeScript frontend; regression run: 154 passed, 4 skipped.
- [ ] Verify navigation, forms, and responsive behavior in a browser.

Checked implementation items indicate code exists; endpoint verification is tracked separately.
