# Frontend build

The existing .NET 10 MVC project uses the official standalone Tailwind CSS CLI.
Node.js, npm, a browser CDN, and a separate frontend application are not required.
The version and SHA256 checksums are pinned in `scripts/tailwind-release.json`.
The build script downloads the matching executable to the ignored `.tools`
directory on first use and checks its checksum before every execution.

Run commands from the repository root with Windows PowerShell 5.1 or newer:

```powershell
# Compile minified production CSS.
powershell -NoProfile -ExecutionPolicy Bypass -File ./scripts/build-css.ps1

# Recompile while Razor views or JavaScript change; stop with Ctrl+C.
powershell -NoProfile -ExecutionPolicy Bypass -File ./scripts/build-css.ps1 -Watch

# One development build without minification.
powershell -NoProfile -ExecutionPolicy Bypass -File ./scripts/build-css.ps1 -NoMinify

# Compile CSS and the existing application.
dotnet build FitnessApp.slnx

# Publish includes a fresh CSS build.
dotnet publish FitnessApp/FitnessApp.csproj -c Release
```

On Linux and macOS, install PowerShell 7 and use `pwsh` in place of `powershell`.
The checked-in assets support Windows x64, Linux x64/ARM64 with glibc, and macOS
x64/ARM64. Alpine/musl and other platforms require a matching pinned asset.
The first build needs access to GitHub release downloads; later builds reuse
the verified executable. The script makes no global tool or PATH changes.

MSBuild compiles CSS before the application build and before publishing, except
during Visual Studio design-time builds. Razor views, JavaScript, the stylesheet,
and build configuration are tracked as inputs, so unchanged builds skip CSS
compilation. If CSS was built separately in an offline CI stage, use
`-p:BuildTailwind=false`; that mode requires the compiled CSS to exist.
Use `-p:TailwindPowerShell=/path/to/pwsh` to override the PowerShell executable.

Edit `FitnessApp/Styles/tailwind.css` rather than the generated
`FitnessApp/wwwroot/css/tailwind.css`. Tailwind v4 explicitly scans `.cshtml`
files under `Views` and `.js` files under `wwwroot/js`. Use complete literal
utility class names in Razor conditionals and JavaScript, rather than composing
class names from fragments. Add shared design tokens with v4 `@theme` directives
when the approved design specification is available; no Figma tokens have been
inferred by the build configuration.

References: [official standalone CLI](https://tailwindcss.com/blog/standalone-cli),
[Tailwind source detection](https://tailwindcss.com/docs/detecting-classes-in-source-files),
and [the pinned v4.3.3 release](https://github.com/tailwindlabs/tailwindcss/releases/tag/v4.3.3).

## MVC integration verification

After a Debug build, run the HTTP and database verification suite with local
PostgreSQL command-line tools available:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ./scripts/verify-mvc.ps1

# Once any browser inspection of the synthetic data is complete:
powershell -NoProfile -ExecutionPolicy Bypass -File ./scripts/verify-mvc.ps1 -Cleanup
```

Override `-PostgresBin` if PostgreSQL is installed outside
`C:\Program Files\PostgreSQL\18\bin`; use `-Port` to select a free test port.
Use `-BuildDirectory artifacts/assignment-build` to verify a separately built
application when the default Debug output is locked by a running local app.
The suite reads the development connection locally, exports only its schema,
and creates a separate `fitnessapp_ui_verify_<random>` database containing
synthetic fixtures. Existing application data is never imported or modified.
The database role needs permission to create databases. Credentials are supplied
through child process environment variables and are not written into commands
or verification reports. An isolated copy of the built app runs on port 5291
by default and remains available for browser inspection. The script verifies
that it owns that listener before sending any form or API mutations.

Checks cover antiforgery enforcement, account registration and session rotation,
server-side validation, pass and trainer CRUD, historical-record deletion guards,
check-in balance changes and duplicate denial, customer search and pagination,
customer membership assignment and its first check-in, and existing API routes. Results and the exact database/process IDs are written
to ignored `artifacts/mvc-verification/state.json`. Cleanup validates the recorded
database name and server process before stopping it and dropping only that test
database. Run cleanup before starting another verification run.
