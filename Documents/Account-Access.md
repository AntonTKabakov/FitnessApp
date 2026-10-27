# Roles and account access

Accounts have a persisted role, either `Admin` or `User`. The migration adds
`customers.role` with default `User` and a check constraint. Existing accounts
are not silently promoted. Public registration always creates a User account;
submitted role properties are ignored.

| Capability | Admin | User |
| --- | --- | --- |
| Club dashboard, customer directory and profiles | All customers | Own portal at /Member |
| Memberships | Assign and view customer passes | View own passes |
| Check-in history and details | All records | Own records only |
| Confirm entry | Any valid customer pass | Own valid pass only |
| Change trainer or remove check-in | Yes | No |
| Trainer/pass-type management and APIs | Yes | No |
| Account role management | Yes | No |

All application endpoints require authentication by default. Sign-in,
registration, session refresh/logout, privacy/error pages, and public static
assets have explicit anonymous access where needed. Anonymous MVC GET requests
redirect to sign-in; APIs return 401. Forbidden API operations return 403.
MVC access-denied navigation ends on a 403 page.

Ownership filters are applied in database queries and before mutations.
Changing a URL ID or posting a different customer/pass ID does not widen access.
Other users' individual visit/pass IDs return 404. Users cannot purchase passes,
cancel visits to restore balances, or change roles.

JWTs contain a role claim, but each authenticated request loads the current
account role from the database before authorization. Promotions and demotions
therefore affect existing tokens immediately; deleted accounts cannot continue
using an unexpired token. Role changes require an admin and antiforgery token.
An admin cannot demote their own account, and role changes use a serializable
transaction with a last-admin check. Account responses are marked no-store.

## Migration and first administrator

Apply the migration before starting the new application:

```powershell
dotnet ef database update --project FitnessApp/FitnessApp.csproj
```

The migration preserves account data, defaults existing roles to User, and
allows the already-optional birth date to be null for staff setup.

First-admin setup is explicit. Configure `AdminBootstrap:Email` (environment
variable `AdminBootstrap__Email`) for an existing account to promote it when no
administrator exists. To create the initial account, also supply
`AdminBootstrap:Password` and optionally `AdminBootstrap:Name` through the local
environment or secret store. Passwords are hashed using the existing
`PasswordHasher<User>`; no bootstrap password is checked into source control.
The bootstrap runs only when there are no administrators.

For a setup-only run, after building:

```powershell
dotnet FitnessApp/bin/Debug/net10.0/FitnessApp.dll --contentRoot FitnessApp --bootstrap-admin-only true
```

Remove the bootstrap password from the environment after setup. Subsequent
role changes are available at **Account roles**. The requested local admin
account was created as `admin@fitnessapp.local`, display name `admim`.
Restart the running application to load the new authorization code.

## Verification

The integration suite creates an isolated database and two independent member
sessions. It checks anonymous access, default roles, role overposting,
cross-account IDs and body fields, scoped history, ownership before check-in,
protected management APIs, antiforgery, promotion/demotion with existing tokens,
self-demotion protection, and ordinary admin workflows.

```powershell
dotnet build FitnessApp/FitnessApp.csproj -c Roles
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/verify-mvc.ps1 -BuildDirectory FitnessApp/bin/Roles/net10.0
# After any browser verification:
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/verify-mvc.ps1 -Cleanup
```

The browser verifier accepts optional `-LoginEmail` and `-LoginPassword`
arguments for isolated fixture accounts. It logs in through the real auth API
and installs the resulting cookies in its temporary browser profile. Reports
and screenshots do not contain passwords.

Verification on 28 October 2026:

- Role build and Release publish passed, with the eight existing DTO/catch warnings.
- The migration was applied to the configured local database; EF reports no pending model changes.
- 107 integration assertions passed, including existing management workflows and cross-account security tests.
- 42 browser route/viewport checks passed: 21 Admin, 12 User, and 9 anonymous checks, across desktop, tablet, and mobile. There were no console errors or horizontal page overflows.
- The requested local administrator credentials were verified through the running application's login API (200); the verification refresh session was revoked by logout (204).
- Reports and screenshots are under ignored `artifacts/browser-roles-*` and `artifacts/mvc-verification`.
