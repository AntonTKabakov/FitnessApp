# Profile picture implementation report

Verified on 30 October 2026. The existing ASP.NET Core MVC application now has
private profile picture upload, replacement, retrieval, and removal, using the
existing JWT/cookie authentication and account roles. The existing Razor layout
is preserved. See [usage and deployment](Profile-Pictures.md) for API requests,
Postman examples, configuration, security limits, and persistent storage.

## Verification results

| Check | Result | Local report |
| --- | --- | --- |
| Solution build | Passed, zero errors | `dotnet build FitnessApp.slnx --no-restore` |
| Automated integration/storage suite | 57 passed, 0 failed, 0 skipped | `artifacts/test-results/profile-pictures.trx` |
| Existing PostgreSQL/MVC regressions | 107 passed, 0 failed | `artifacts/profile-picture-mvc/state.json` |
| Desktop/mobile browser interactions | 25 passed, no JavaScript exceptions or horizontal overflow | `artifacts/browser-profile-picture/report.json` |
| Process restart persistence and PostgreSQL migration checks | 13 passed | `artifacts/profile-picture-mvc/persistence-report.json` |
| Release publish | Passed | `artifacts/profile-picture-publish` |
| Published production-mode smoke checks | 4 passed: login, avatar, unauthorized profile/upload | `artifacts/profile-picture-release-smoke/report.json` |
| EF model/migration consistency | No pending model changes; development database has all migration IDs | EF CLI verification |
| Production package vulnerability audit | No reported vulnerabilities from current NuGet sources | `dotnet list FitnessApp/FitnessApp.csproj package --vulnerable --include-transitive` |
| Publish storage isolation | Default avatar/native libraries included; private uploads excluded | Publish directory inspected |
| JavaScript/PowerShell validation | Passed syntax/parsing and final diff whitespace check | Local verification |

The tests cover JPEG, PNG, WebP, all eight EXIF orientations, metadata removal,
aspect ratio/no upscaling, corrupt/empty/animated/spoofed files, pixel/dimension
limits, normal and unknown-length oversized multipart requests, malformed forms,
real JWT and CSRF behavior, cross-account access, ID overposting, traversal and
symlink protection, replacement/removal, stale concurrent updates, database
failures, actual filesystem failures, cleanup failures, missing images, persistent
storage, registration, login, refresh, and logout. Browser screenshots are in
`artifacts/browser-profile-picture/desktop-uploaded.png` and `mobile-uploaded.png`.

PostgreSQL mutation tests ran against isolated fixture databases, which were
cleaned up. Restart testing compared actual saved image bytes before/after
recreating the server process. Migration verification removed/recreated the
nullable column only in the isolated database and checked user counts/defaults.
The configured development database received read-only metadata/history checks.

## Database, packages, and configuration

- `User.ProfilePicturePath` maps to nullable `customers.profilepicturepath`,
  `varchar(64)`, and is an EF concurrency token. Only a relative GUID WebP storage
  key is persisted. No other entity schema changed.
- The development database already had the matching column and migration
  `20261029213054_AddUserProfilePicture`. The generated migration was aligned
  with this verified ID to avoid a duplicate-column migration. Other databases
  should apply the normal EF migration before running the updated app.
- Production packages added: `SkiaSharp` **4.153.2** and
  `SkiaSharp.NativeAssets.Linux.NoDependencies` **4.153.2**.
- Test packages added: `Microsoft.AspNetCore.Mvc.Testing` **10.0.13**,
  `Microsoft.EntityFrameworkCore.Sqlite` **10.0.13**,
  `Microsoft.NET.Test.Sdk` **18.0.1**, `xunit.v3` **3.2.2**, and
  `xunit.runner.visualstudio` **3.1.5**.
- `FileStorage` settings now configure the private persistent directory, file
  size, dimension limit, and decoded-pixel limit. Defaults work locally;
  production should set `FileStorage__ProfilePicturesPath` to durable storage
  outside the replaceable application release directory. Docker deployments
  need a persistent volume at that location.

## Endpoints

| Method | Route |
| --- | --- |
| GET | `/api/users/me` |
| POST | `/api/users/me/profile-picture` |
| GET | `/api/users/{id}/profile-picture` |
| DELETE | `/api/users/me/profile-picture` |

## Files added

| File | Purpose |
| --- | --- |
| `Documents/Profile-Pictures.md` | Usage, API/Postman examples, migration, configuration, deployment/recovery |
| `Documents/Profile-Pictures-Implementation.md` | This report and full file inventory |
| `FitnessApp/Controllers/UsersController.cs` | Authenticated profile/picture endpoints |
| `FitnessApp/DTO/UserProfileResponseDTO.cs` | Account response with nullable picture URL |
| `FitnessApp/Filters/ProfilePictureAntiforgeryFilter.cs` | Cookie mutation CSRF protection |
| `FitnessApp/Filters/ProfilePictureExceptionFilter.cs` | Safe, consistent storage/database API errors |
| `FitnessApp/Filters/ProfilePictureUploadLimitFilter.cs` | Bounded multipart parsing and explicit 413/400 responses |
| `FitnessApp/Migrations/20261029213054_AddUserProfilePicture.cs` | Nullable picture storage key migration |
| `FitnessApp/Migrations/20261029213054_AddUserProfilePicture.Designer.cs` | Generated migration target model |
| `FitnessApp/Service/Interfaces/IProfilePictureStorageService.cs` | Save/read/delete storage abstraction |
| `FitnessApp/Service/LocalProfilePictureStorageService.cs` | Secure validation, optimization, persistent local storage |
| `FitnessApp/Service/ProfilePictureException.cs` | Safe profile picture error/status type |
| `FitnessApp/Service/ProfilePictureStorageOptions.cs` | Validated storage/processing settings |
| `FitnessApp/Service/UserProfileService.cs` | Database updates, concurrency, and file cleanup |
| `FitnessApp/wwwroot/images/default-avatar.svg` | Built-in avatar |
| `FitnessApp/wwwroot/js/profile-picture.js` | Preview, progress, upload/removal, accessible feedback |
| `FitnessApp.Tests/FitnessApp.Tests.csproj` | .NET 10 xUnit test project |
| `FitnessApp.Tests/FitnessAppFactory.cs` | Isolated real-auth/SQLite/storage test host and fault injection |
| `FitnessApp.Tests/ProfilePictureTests.cs` | End-to-end API, processing, lifecycle, auth and failure tests |
| `FitnessApp.Tests/StorageSecurityTests.cs` | Filesystem, bounded stream, animation and symlink tests |
| `scripts/verify-profile-picture-browser.ps1` | Guarded real-browser desktop/mobile interaction checks |

## Files modified

| File | Change |
| --- | --- |
| `.gitignore` | Exclude local private uploads |
| `FitnessApp.slnx` | Include automated test project |
| `FitnessApp/Data/AppDbContext.cs` | Map nullable picture key/concurrency token |
| `FitnessApp/FitnessApp.csproj` | Image packages; exclude uploads from publish content |
| `FitnessApp/Migrations/AppDbContextModelSnapshot.cs` | Generated picture field mapping |
| `FitnessApp/Models/User.cs` | Nullable `ProfilePicturePath` |
| `FitnessApp/Program.cs` | DI, options/startup validation, antiforgery header and test host entry point |
| `FitnessApp/Service/ReceptionQueryService.cs` | Populate picture URL; preserve explicitly selected check-in passes |
| `FitnessApp/ViewModels/ReceptionViewModels.cs` | Picture URL/presence on existing customer profile model |
| `FitnessApp/Views/Customers/Details.cshtml` | Read-only customer picture |
| `FitnessApp/Views/Member/Index.cshtml` | Existing profile's picture editor |
| `FitnessApp/Views/Shared/_Navigation.cshtml` | Make My account available to administrators |
| `FitnessApp/appsettings.json` | Default `FileStorage` settings |
| `FitnessApp/wwwroot/css/tailwind.css` | Recompiled existing Tailwind styles for new UI classes |
| `README.md` | Feature/deployment documentation links |
| `scripts/verify-mvc.ps1` | Isolated artifact/storage/JWT settings, optional migration SQL, cleanup guard, accurate pagination assertion |

The existing regression suite exposed an early return that discarded an explicitly
selected pass on the check-in page. A one-condition correction preserves that
selection. Its pagination assertion was also updated to match the existing
four-customer page size. No unrelated screens were redesigned.

## Remaining operational notes

No feature-blocking issues remain. Release compilation retains eight pre-existing
nullable DTO/unused catch-variable warnings. Windows execution was verified;
Linux native assets are packaged, but no Linux/container runtime was available
for execution here. Configure persistent storage/permissions and apply migrations
on deployment databases, then restart the app to load the feature.

Cleanup failures are logged and may leave unreferenced files. There is no automatic
orphan sweeper; the documented maintenance procedure reconciles those files safely.
