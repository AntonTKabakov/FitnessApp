# Profile pictures

Open **My account**, select **Change Profile Picture**, preview an image, and
select **Upload picture**. **Cancel preview** keeps the saved picture.
**Remove Profile Picture** restores the default avatar. The responsive editor
shows progress and accessible success/error messages. Administrators can view
customer pictures in existing customer profiles. Only the owner can change a
picture.

## API and authentication

All endpoints use the existing validated access JWT, either in the HTTP-only
`access_token` cookie or the `Authorization: Bearer ...` header. Refresh tokens
are never accepted as access tokens. Cookie authentication takes precedence
when both are supplied, matching the existing JWT handler.

| Method | Route | Result |
| --- | --- | --- |
| GET | `/api/users/me` | Account details with nullable `profilePictureUrl`; CSRF token in the `RequestVerificationToken` response header |
| POST | `/api/users/me/profile-picture` | Multipart field `file`; 200 with `{ "profilePictureUrl": "/api/users/123/profile-picture?v=..." }` |
| GET | `/api/users/{id}/profile-picture` | `image/webp`, or the built-in SVG avatar if unset/missing |
| DELETE | `/api/users/me/profile-picture` | 204; repeated removal is safe |

Members may retrieve only their own picture; admins may retrieve any existing
customer's picture. Cross-account member retrieval returns 404, matching existing
private profile visibility. Nonexistent accounts return 404. Responses are
no-store, and images set `X-Content-Type-Options: nosniff`. URLs respect the app's
path base and never expose physical paths.

Cookie-authenticated upload and removal require the `RequestVerificationToken`
header plus its antiforgery cookie. The Razor page handles this automatically.
API clients can first GET `/api/users/me`, retain its cookies, and copy the
`RequestVerificationToken` response header. Bearer-only requests do not need
CSRF tokens. Supplying a bearer header alongside an access cookie still requires
CSRF validation because the existing JWT handler authenticates the cookie.

Errors follow the existing `{ "error": "..." }` convention: 400 for missing,
empty, corrupt/incomplete, or excessive-dimension images and invalid CSRF;
401 for missing/invalid authentication; 413 for files exceeding the upload limit;
415 for unsupported/mismatched formats or animations; 409 for concurrent picture
changes; 503 for database or storage failures. Framework/parser or reverse-proxy
errors can have their own response bodies.

### Postman examples

Set `baseUrl` to your app URL, such as `https://localhost:7076`. With Postman's
cookie jar enabled, sign in using the existing API:

```http
POST {{baseUrl}}/auth/login
Content-Type: application/json

{ "email": "your-account@example.com", "password": "your-password" }
```

Read the profile and save its CSRF header using a Postman post-response script:

```http
GET {{baseUrl}}/api/users/me
```

```javascript
pm.environment.set("profileCsrf", pm.response.headers.get("RequestVerificationToken"));
```

Upload with **Body → form-data**, key `file`, type **File**, and choose a picture.
Let Postman set the multipart Content-Type and boundary:

```http
POST {{baseUrl}}/api/users/me/profile-picture
RequestVerificationToken: {{profileCsrf}}
```

Retrieve the returned `profilePictureUrl` using the same session, or remove it:

```http
DELETE {{baseUrl}}/api/users/me/profile-picture
RequestVerificationToken: {{profileCsrf}}
```

Bearer access JWTs also work with curl (the existing login API returns cookies,
rather than exposing an access JWT in its JSON body):

```sh
curl -H "Authorization: Bearer $ACCESS_TOKEN" \
  -F "file=@portrait.png;type=image/png" \
  https://localhost:7076/api/users/me/profile-picture
```

## Validation and optimization

Accepts `.jpg`/`.jpeg` with `image/jpeg`, `.png` with `image/png`, and `.webp`
with `image/webp`. Extension, MIME, and decoded format must match. Default maximum
size: **5 MiB (5,242,880 bytes)**. Empty/corrupt/incomplete images and animations
are rejected. Headers are inspected before allocating decoded buffers: maximum
width/height 8192 and maximum 16,000,000 decoded pixels. Two processing slots
bound simultaneous image processing per application process. Originals are never
saved or served.

SkiaSharp corrects all eight EXIF orientations, fits within 512 × 512 without
upscaling or changing aspect ratio, and re-encodes fresh pixels as WebP at quality
80. EXIF/GPS/comments are discarded. SkiaSharp and its native assets are MIT
licensed; upstream native third-party notices remain applicable. See the
[upstream license](https://github.com/mono/SkiaSharp/blob/main/LICENSE.txt) and
[release notes](https://github.com/mono/SkiaSharp/releases).

## Schema and configuration

Migration `20261029213054_AddUserProfilePicture` adds nullable
`customers.profilepicturepath` (`varchar(64)`). Existing accounts retain null;
no image binary or Base64 is stored in PostgreSQL. The field is an EF concurrency
token, so competing picture replacements/removals return 409.

The configured development database already had this exact column and migration
history ID when inspected. The checked-in migration matches that existing ID and
shape, so it can also provision other databases without reapplying the column
to development. No schema or user-data writes were made to the configured
development database during this task.

Apply the migration before starting against an existing database:

```powershell
dotnet ef database update --project FitnessApp/FitnessApp.csproj
dotnet ef migrations has-pending-model-changes --project FitnessApp/FitnessApp.csproj
```

Defaults in `FitnessApp/appsettings.json`:

```json
{
  "FileStorage": {
    "ProfilePicturesPath": "uploads/profile-pictures",
    "MaxProfilePictureSizeMB": 5,
    "MaxImageDimension": 8192,
    "MaxDecodedPixels": 16000000
  }
}
```

Relative directories resolve against the application's content root. Absolute
deployment directories can be configured through environment variables, such as
`FileStorage__ProfilePicturesPath=/data/profile-pictures`. Limits can be lowered
but cannot exceed these security ceilings. Invalid settings fail startup.

The directory is created on first save and contains flat, server-generated GUID
filenames:

```text
uploads/profile-pictures/
  0123456789abcdef0123456789abcdef.webp
```

Reads/deletes accept only that key shape. Storage must be outside `wwwroot`;
directory ancestors cannot be symbolic links/junctions, and retrieval rejects
linked files. Give only the app account read/write/delete permissions. Never
expose this directory through a static-file alias or executable handler. Local
uploads are ignored by Git and excluded from publish content.

## Persistence, deployment, and recovery

Use persistent disk outside the replaceable release directory. Back up PostgreSQL
and images together. Multiple instances need the same persistent shared directory.
Native assets are included for Windows/macOS through SkiaSharp and Linux through
the matching `SkiaSharp.NativeAssets.Linux.NoDependencies` package. Publish for
the supported deployment OS/architecture and smoke-test it.

The repository has no Docker deployment. For an existing container deployment,
mount a named volume or persistent host directory:

```yaml
services:
  fitnessapp:
    environment:
      FileStorage__ProfilePicturesPath: /data/profile-pictures
    volumes:
      - profile-pictures:/data/profile-pictures
volumes:
  profile-pictures:
```

Allow at least 5 MiB plus multipart overhead at the reverse proxy (for example
6 MiB); the app still enforces its 5 MiB file ceiling. Monitor disk space and
storage failures.

Replacement saves the optimized file, updates the database reference, then
removes the old file after database success. Failed updates remove the new file
only after confirming it is unreferenced. Removal clears the reference first.
Cleanup completes independently of browser disconnects. Concurrent requests
cannot delete the winning picture.

If cleanup fails or database commit success is uncertain, files are retained and
their keys are logged for reconciliation, preserving valid references. There is
no automatic orphan sweeper. With picture writes paused during maintenance,
compare GUID WebP filenames against
`SELECT profilepicturepath FROM customers WHERE profilepicturepath IS NOT NULL;`
and remove only unreferenced files after backup and a suitable grace period.
Retry failed removals recorded in logs. Missing referenced files display the
default avatar and can be replaced by the owner.

## Verification

```powershell
dotnet build FitnessApp.slnx
dotnet test FitnessApp.slnx --no-build
```

The xUnit suite uses isolated SQLite databases, private temporary storage, real
JWT authentication, real antiforgery tokens, and real Skia decoders. It covers
formats, orientation/resize/metadata, limits, ownership, CSRF, replacement,
removal/retrieval, persistence, injected database/storage failures, and existing
registration/login/refresh/logout. PostgreSQL/browser checks use isolated fixture
databases; the configured application database is not modified by the tests.
