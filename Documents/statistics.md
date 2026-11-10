# Admin statistics

Sign in with an existing **Admin** account and choose **Statistics** in the sidebar, or visit `/Stats`. Ordinary members cannot access statistics or either export. The existing JWT validation reads the account's current role on every request, including exports.

## Routes

| GET route | Result |
| --- | --- |
| `/Stats` | MVC dashboard; defaults to the last 30 calendar days, including today |
| `/Stats/Data` | Razor partial used for asynchronous filter updates |
| `/Stats/ExportPdf` | Server-generated PDF download |
| `/Stats/ExportExcel` | Server-generated five-sheet `.xlsx` download |

All routes accept `Range=Today`, `Last7Days`, `Last30Days`, `ThisMonth`, `ThisYear`, or `Custom`. A custom range also requires ISO `Start` and `End` dates. Example:

```text
/Stats?Range=Custom&Start=2026-10-01&End=2026-10-31
/Stats/ExportPdf?Range=Custom&Start=2026-10-01&End=2026-10-31
/Stats/ExportExcel?Range=Custom&Start=2026-10-01&End=2026-10-31
```

Both endpoints validate dates, reject future dates and reversed ranges, and limit reports to 366 inclusive days. Export links carry the exact dates of the displayed report, even when a relative preset was selected. Filtering also works as an ordinary GET form without JavaScript. Chart data remains available in accessible HTML tables if the chart library fails to load. A failed asynchronous update retains the previous successful data and export links.

## Calculations and database limitations

- `StatsService` uses real EF entities and server-side counts, sums, date groups, and trainer projections. Only aggregated data is loaded. Report queries run sequentially in a PostgreSQL repeatable-read transaction, so each report sees one database snapshot.
- Dates follow the existing `ReceptionTime` convention: **Europe/Sofia**. Purchases are `DateOnly`; check-ins are Sofia wall-clock values in `timestamp without time zone`. Attendance uses an inclusive start and exclusive next-day end, with no UTC conversion of those stored wall-clock values. Missing days and months are filled with zero after aggregation.
- Total customers, total check-ins, and total estimated revenue are all-time metrics. Customer totals include administrator accounts, matching the existing customer directory. Trainers are the current directory count.
- Active memberships follow the existing purchase/check-in services: `Expirydate >= today && Remainingvisits > 0`. Expiry today is valid. There is no unlimited-membership sentinel in the current business logic.
- All charts, period check-ins, period revenue, purchases, and distinct purchasing customers use the selected period. Active memberships are current status, not a historical snapshot. Trainers with zero check-ins are included; unassigned check-ins are disclosed separately and included in attendance totals.
- **Revenue is estimated** by joining customer passes to their current gym pass price. There are no payment records or purchase-time prices in the model. Changing a pass price therefore changes estimates for previous purchases. The dashboard, PDF, and workbook explicitly disclose this limitation.
- Account creation dates are not stored, so a count of newly registered customers during a period cannot be calculated. Distinct purchasing customers is labeled separately.
- No schema or migration changes are required. Report files are generated in memory. Accounts, passwords, refresh tokens, emails, and phone numbers are not included in report data.

## PDF licensing and dependencies

The project already referenced **QuestPDF 2026.9.1** and **ClosedXML 0.105.1** when this feature was implemented; no additional NuGet packages were needed. Chart.js **4.5.1** is pinned and served locally from `wwwroot/lib/chartjs`, with its MIT license. There is no browser CDN dependency.

`Reporting:QuestPdfLicense` in `appsettings.json` is set to `Community` following the project owner's confirmation of eligibility. QuestPDF requires the operator to select the appropriate license; it does not require a license key. See the [official license configuration](https://www.questpdf.com/license/configuration.html) and [current license terms](https://www.questpdf.com/license/).

Deployments can override the setting with `Reporting__QuestPdfLicense=Community`, `Professional`, or `Enterprise`, according to actual eligibility and purchased licensing. An empty setting disables PDF export while leaving the dashboard and Excel available. Unsupported values fail startup with a configuration error. No commercial entitlement is assumed.

## Validation and manual checks

From the repository root, with a working .NET 10 SDK:

```powershell
dotnet restore FitnessApp.slnx
dotnet build FitnessApp.slnx
dotnet test FitnessApp.Tests/FitnessApp.Tests.csproj
```

The build uses the existing Tailwind v4 script and source directives; compiled CSS is checked in. `StatsTests` covers preset boundaries, leap years, date validation on every endpoint, empty reports, all-time vs period aggregates, expiry-day membership status, exhausted visits, zero-activity trainers, unassigned check-ins, XSS encoding, Excel literal text, report content types, worksheet values and formatting, PDF generation through the maximum reporting period, authorization and immediate role demotion, and rendering of existing admin/member pages.

The optional Npgsql provider test creates and deletes its own uniquely named `fitness_stats_test_<guid>` database. Set `FITNESS_STATS_TEST_POSTGRES` to a disposable PostgreSQL server with database-creation permission to enable it; without this setting, that one test is explicitly skipped. Never point integration tests at a production server.

```powershell
$env:FITNESS_STATS_TEST_POSTGRES = 'Host=localhost;Port=55439;Username=fitness_stats;Database=postgres'
dotnet test FitnessApp.Tests/FitnessApp.Tests.csproj --filter FullyQualifiedName~StatsTests
```

To check the UI, start the application using the existing database/authentication configuration, sign in as an Admin, open `/Stats`, and switch between each preset and custom dates. Check the loading and empty states, resize to mobile/tablet/desktop, and download both reports. The Excel workbook contains Overview, Revenue (month and pass type), Attendance, Memberships, and Trainers sheets. Compare their totals and dates with the dashboard. Sign in as an ordinary User to verify access is denied. Data can change between separate dashboard/export requests; each request calculates a fresh, internally consistent snapshot for the same selected dates.

## Feature files

Created: `Controllers/StatsController.cs`, `Service/StatsService.cs`, `Service/Interfaces/IStatsService.cs`, `Service/PdfReportService.cs`, `Service/ExcelReportService.cs`, `ViewModels/StatsViewModels.cs`, `Views/Stats/Index.cshtml`, `Views/Stats/_Dashboard.cshtml`, `wwwroot/js/stats.js`, `wwwroot/lib/chartjs/chart.umd.min.js`, `wwwroot/lib/chartjs/LICENSE.md`, `FitnessApp.Tests/StatsTests.cs` (in the sibling test project), and this document.

Updated: `Program.cs` (DI and QuestPDF configuration), `appsettings.json` (confirmed license selection), `Views/Shared/_Navigation.cshtml`, `Views/Shared/_Icon.cshtml`, and generated `wwwroot/css/tailwind.css`.
