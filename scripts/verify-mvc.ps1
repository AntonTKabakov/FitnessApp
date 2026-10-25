[CmdletBinding()]
param(
    [ValidateRange(1024, 65535)][int]$Port = 5291,
    [string]$PostgresBin = 'C:\Program Files\PostgreSQL\18\bin',
    [string]$BuildDirectory = 'FitnessApp/bin/Debug/net10.0',
    [switch]$Cleanup
)

$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskArtifacts = Join-Path $taskRoot 'artifacts/mvc-verification'
$taskStatePath = Join-Path $taskArtifacts 'state.json'
New-Item -ItemType Directory -Path $taskArtifacts -Force | Out-Null
Add-Type -AssemblyName System.Data
Add-Type -AssemblyName System.Net.Http
$taskConfiguration = Get-Content -LiteralPath (Join-Path $taskRoot 'FitnessApp/appsettings.Development.json') -Raw | ConvertFrom-Json
$taskConnection = New-Object System.Data.Common.DbConnectionStringBuilder
$taskConnection.set_ConnectionString($taskConfiguration.ConnectionStrings.DefaultConnection)
if (-not $taskConnection.ContainsKey('Database')) { throw 'A development database connection is required.' }
$taskOriginalDatabase = [string]$taskConnection['Database']
$taskPsql = Join-Path $PostgresBin 'psql.exe'
$taskPgDump = Join-Path $PostgresBin 'pg_dump.exe'
if (-not (Test-Path -LiteralPath $taskPsql)) { throw 'PostgreSQL command-line tools were not found; pass -PostgresBin.' }
$taskPreviousEnvironment = @{}
foreach ($taskVariable in @('PGHOST', 'PGPORT', 'PGUSER', 'PGPASSWORD', 'PGCLIENTENCODING', 'ASPNETCORE_ENVIRONMENT', 'ConnectionStrings__DefaultConnection', 'AuthCookie__Secure', 'AuthCookie__SameSite')) {
    $taskPreviousEnvironment[$taskVariable] = [Environment]::GetEnvironmentVariable($taskVariable)
}
$env:PGHOST = [string]$taskConnection['Host']
$env:PGPORT = if ($taskConnection.ContainsKey('Port')) { [string]$taskConnection['Port'] } else { '5432' }
$env:PGUSER = if ($taskConnection.ContainsKey('Username')) { [string]$taskConnection['Username'] } else { [string]$taskConnection['User ID'] }
$env:PGPASSWORD = [string]$taskConnection['Password']
$env:PGCLIENTENCODING = 'UTF8'
$taskChecks = New-Object 'System.Collections.Generic.List[object]'

function Invoke-VerificationSql([string]$Database, [string]$Sql) {
    $taskSqlOutput = $Sql | & $taskPsql -X --no-psqlrc --set ON_ERROR_STOP=1 --tuples-only --no-align --dbname $Database 2> (Join-Path $taskArtifacts 'postgres-error.log')
    if ($LASTEXITCODE -ne 0) { throw 'A database verification command failed. See the local postgres-error.log.' }
    return ($taskSqlOutput -join "`n").Trim()
}

function Assert-Verification([string]$Name, [bool]$Condition) {
    $taskChecks.Add([pscustomobject]@{ Name = $Name; Passed = $Condition })
    if ($Condition) { Write-Host "PASS $Name" } else { Write-Host "FAIL $Name" }
}

function New-VerificationClient {
    $taskHandler = New-Object System.Net.Http.HttpClientHandler
    $taskHandler.AllowAutoRedirect = $false
    $taskHandler.CookieContainer = New-Object System.Net.CookieContainer
    $taskClient = New-Object System.Net.Http.HttpClient($taskHandler)
    $taskClient.BaseAddress = [Uri]$taskBaseUrl
    $taskClient.Timeout = [TimeSpan]::FromSeconds(20)
    return $taskClient
}

function Send-VerificationRequest($Client, [string]$Method, [string]$Path, [hashtable]$Form = $null, $Json = $null) {
    $taskRequest = New-Object System.Net.Http.HttpRequestMessage([System.Net.Http.HttpMethod]::new($Method), $Path)
    try {
        if ($null -ne $Form) {
            $taskEncoded = ($Form.GetEnumerator() | ForEach-Object {
                [Uri]::EscapeDataString([string]$_.Key) + '=' + [Uri]::EscapeDataString([string]$_.Value)
            }) -join '&'
            $taskRequest.Content = New-Object System.Net.Http.StringContent($taskEncoded, [Text.Encoding]::UTF8, 'application/x-www-form-urlencoded')
        } elseif ($null -ne $Json) {
            $taskRequest.Content = New-Object System.Net.Http.StringContent(($Json | ConvertTo-Json -Depth 8 -Compress), [Text.Encoding]::UTF8, 'application/json')
        }
        $taskResponse = $Client.SendAsync($taskRequest).GetAwaiter().GetResult()
        try {
            return [pscustomobject]@{
                Status = [int]$taskResponse.StatusCode
                Location = [string]$taskResponse.Headers.Location
                Html = $taskResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult()
            }
        } finally { $taskResponse.Dispose() }
    } finally { $taskRequest.Dispose() }
}

function Submit-VerificationForm($Client, [string]$GetPath, [string]$PostPath, [hashtable]$Fields) {
    $taskPage = Send-VerificationRequest $Client GET $GetPath
    if ($taskPage.Status -ne 200) { throw "Unable to read form '$GetPath' (HTTP $($taskPage.Status))." }
    $taskTokenMatch = [regex]::Match($taskPage.Html, '<input\b[^>]*\bname="__RequestVerificationToken"[^>]*\bvalue="([^"]+)"')
    if (-not $taskTokenMatch.Success) { throw "Antiforgery token was missing from '$GetPath'." }
    $Fields['__RequestVerificationToken'] = [Net.WebUtility]::HtmlDecode($taskTokenMatch.Groups[1].Value)
    return Send-VerificationRequest $Client POST $PostPath $Fields
}

try {
    if ($Cleanup) {
        if (-not (Test-Path -LiteralPath $taskStatePath)) { throw 'No recorded verification database exists to clean up.' }
        $taskState = Get-Content -LiteralPath $taskStatePath -Raw | ConvertFrom-Json
        if ($taskState.Database -notmatch '^fitnessapp_ui_verify_[a-f0-9]{12}$' -or $taskState.Database -eq $taskOriginalDatabase) {
            throw 'Refusing to clean up a database that is not an explicitly recorded verification database.'
        }
        $taskProcess = Get-CimInstance Win32_Process -Filter "ProcessId = $($taskState.ProcessId)" -ErrorAction SilentlyContinue
        if ($taskProcess) {
            if ($taskProcess.CommandLine -notlike "*$($taskState.ServerAssembly)*" -or $taskProcess.CommandLine -notlike "*$($taskState.BaseUrl)*") {
                throw 'The recorded process ID now belongs to a different process; cleanup stopped.'
            }
            Stop-Process -Id $taskState.ProcessId -Force
        }
        Invoke-VerificationSql postgres "DROP DATABASE IF EXISTS $($taskState.Database) WITH (FORCE);" | Out-Null
        $taskState | Add-Member -NotePropertyName CleanedUp -NotePropertyValue $true -Force
        $taskState | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $taskStatePath -Encoding UTF8
        Write-Host "Removed recorded verification database $($taskState.Database) and stopped its server."
        return
    }

    if (Test-Path -LiteralPath $taskStatePath) {
        $taskPreviousState = Get-Content -LiteralPath $taskStatePath -Raw | ConvertFrom-Json
        if (-not $taskPreviousState.CleanedUp) { throw 'A verification run is already recorded. Run this script with -Cleanup before starting another.' }
    }
    $taskExistingListeners = [Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpListeners()
    if (@($taskExistingListeners | Where-Object Port -eq $Port).Count -gt 0) {
        throw "Port $Port is already in use. Verification must start its own isolated server."
    }
    $taskDatabase = 'fitnessapp_ui_verify_' + [Guid]::NewGuid().ToString('N').Substring(0, 12)
    $taskRunDirectory = Join-Path $taskArtifacts $taskDatabase
    New-Item -ItemType Directory -Path $taskRunDirectory | Out-Null
    $taskSchema = Join-Path $taskRunDirectory 'schema.sql'
    & $taskPgDump --schema-only --no-owner --no-acl --file $taskSchema --dbname $taskOriginalDatabase 2> (Join-Path $taskArtifacts 'postgres-error.log')
    if ($LASTEXITCODE -ne 0) { throw 'Unable to read the development database schema.' }
    Invoke-VerificationSql postgres "CREATE DATABASE $taskDatabase;" | Out-Null
    $taskState = [ordered]@{ Database = $taskDatabase; Port = $Port; BaseUrl = "http://127.0.0.1:$Port"; ProcessId = 0; ServerAssembly = ''; CleanedUp = $false; Checks = @(); Fixtures = @{ CustomerId = 1001; ActivePassId = 301; ExpiredPassId = 302; ExhaustedPassId = 303; ApiPassId = 304; TrainerId = 201; OtherTrainerId = 202 } }
    $taskState | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $taskStatePath -Encoding UTF8
    & $taskPsql -X --no-psqlrc --set ON_ERROR_STOP=1 --quiet --dbname $taskDatabase --file $taskSchema 1> (Join-Path $taskRunDirectory 'schema-import.log') 2> (Join-Path $taskArtifacts 'postgres-error.log')
    if ($LASTEXITCODE -ne 0) { throw 'Unable to restore the isolated verification schema.' }

    $taskFixtureSql = @'
INSERT INTO customers (customerid, fullname, email, password, dateofbirth)
SELECT 1000 + i, 'Verification Member ' || lpad(i::text, 2, '0'), 'member' || i || '@verification.invalid', 'synthetic-fixture-not-a-login', DATE '1990-01-01'
FROM generate_series(1, 26) i;
UPDATE customers SET fullname = 'Maya Petrova' WHERE customerid = 1001;
UPDATE customers SET fullname = 'Alex Ivanov' WHERE customerid = 1002;
UPDATE customers SET fullname = 'Iva Georgieva' WHERE customerid = 1003;
INSERT INTO gympasses (passtypeid, passname, price, maxvisits, validitydays) VALUES
(101, 'Monthly Training', 70.00, 12, 30), (102, 'Day Entry', 15.00, 1, 1), (103, 'Ten Visits', 60.00, 10, 60);
INSERT INTO trainers (trainerid, fullname, specialty, phone) VALUES
(201, 'Elena Dimitrova', 'Strength training', '+359888100201'), (202, 'Boris Petrov', 'Mobility', '+359888100202');
INSERT INTO customerpasses (customerpassid, customerid, passtypeid, purchasedate, expirydate, remainingvisits) VALUES
(301, 1001, 101, CURRENT_DATE - 2, CURRENT_DATE + 28, 12),
(302, 1002, 101, CURRENT_DATE - 40, CURRENT_DATE - 10, 5),
(303, 1003, 103, CURRENT_DATE - 10, CURRENT_DATE + 50, 0),
(304, 1004, 101, CURRENT_DATE - 2, CURRENT_DATE + 28, 12);
INSERT INTO checkins (checkinid, customerpassid, checkindatetime, trainerid)
VALUES (401, 301, CURRENT_TIMESTAMP - INTERVAL '1 day', 201);
'@
    Invoke-VerificationSql $taskDatabase $taskFixtureSql | Out-Null
    $taskConnection['Database'] = $taskDatabase
    $env:ConnectionStrings__DefaultConnection = $taskConnection.get_ConnectionString()
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:AuthCookie__Secure = 'false'
    $env:AuthCookie__SameSite = 'Lax'
    $taskBuiltApp = if ([IO.Path]::IsPathRooted($BuildDirectory)) { $BuildDirectory } else { Join-Path $taskRoot $BuildDirectory }
    if (-not (Test-Path -LiteralPath (Join-Path $taskBuiltApp 'FitnessApp.dll'))) { throw 'Build the Debug application before running verification.' }
    $taskSnapshot = Join-Path $taskRunDirectory 'app'
    New-Item -ItemType Directory -Path $taskSnapshot | Out-Null
    Copy-Item -Path (Join-Path $taskBuiltApp '*') -Destination $taskSnapshot -Recurse
    $taskAssembly = Join-Path $taskSnapshot 'FitnessApp.dll'
    $taskBaseUrl = "http://127.0.0.1:$Port"
    $taskServer = Start-Process -FilePath 'dotnet' -ArgumentList @($taskAssembly, '--urls', $taskBaseUrl, '--contentRoot', (Join-Path $taskRoot 'FitnessApp')) -WorkingDirectory (Join-Path $taskRoot 'FitnessApp') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $taskRunDirectory 'server.log') -RedirectStandardError (Join-Path $taskRunDirectory 'server-error.log')
    $taskState.ProcessId = $taskServer.Id
    $taskState.ServerAssembly = $taskAssembly
    $taskState | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $taskStatePath -Encoding UTF8
    $taskClient = New-VerificationClient
    $taskReady = $false
    for ($taskAttempt = 0; $taskAttempt -lt 40; $taskAttempt++) {
        try { $taskReady = (Send-VerificationRequest $taskClient GET '/').Status -eq 200 } catch { }
        if ($taskReady) { break }
        if ($taskServer.HasExited) { throw 'The isolated verification server exited. See its local logs.' }
        Start-Sleep -Milliseconds 500
    }
    if (-not $taskReady) { throw 'The isolated verification server did not become ready.' }
    $taskListener = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction Stop
    if ($taskServer.HasExited -or @($taskListener | Where-Object OwningProcess -eq $taskServer.Id).Count -eq 0) {
        throw 'The responding server does not belong to this verification run; no HTTP mutations were sent.'
    }

    $taskResponse = Send-VerificationRequest $taskClient POST '/Passes/Create' @{ Name = 'No token'; Price = '1'; MaxVisits = '1'; ValidityDays = '1' }
    Assert-Verification 'MVC mutation rejects missing antiforgery token' ($taskResponse.Status -eq 400)
    $taskPassword = 'Verification-Only-42!'
    $taskRegistration = @{ FullName = 'Verification Account'; Email = 'account@verification.invalid'; DateOfBirth = (Get-Date).AddYears(-10).ToString('yyyy-MM-dd'); Password = $taskPassword; ConfirmPassword = $taskPassword }
    $taskResponse = Submit-VerificationForm $taskClient '/Account/Register' '/Account/Register' $taskRegistration
    Assert-Verification 'Registration rejects age below fourteen' ($taskResponse.Status -eq 200 -and $taskResponse.Html -match 'at least 14 years old')
    Assert-Verification 'Invalid registration creates no customer' ((Invoke-VerificationSql $taskDatabase "SELECT count(*) FROM customers WHERE email = 'account@verification.invalid';") -eq '0')
    $taskRegistration.DateOfBirth = '1990-01-01'
    $taskResponse = Submit-VerificationForm $taskClient '/Account/Register' '/Account/Register' $taskRegistration
    Assert-Verification 'Valid registration redirects after creating account' ($taskResponse.Status -eq 302)
    Assert-Verification 'Registration stores a hashed password and refresh token' ((Invoke-VerificationSql $taskDatabase "SELECT count(*) FROM customers c JOIN refreshtokens r ON r.userid = c.customerid WHERE c.email = 'account@verification.invalid' AND c.password LIKE 'AQAAAA%';") -eq '1')
    $taskResponse = Submit-VerificationForm $taskClient '/' '/Account/Refresh' @{ returnUrl = '/Customers' }
    Assert-Verification 'MVC refresh renews session and uses local return URL' ($taskResponse.Status -eq 302 -and $taskResponse.Location -eq '/Customers')
    Assert-Verification 'Refresh rotates rather than accumulating tokens' ((Invoke-VerificationSql $taskDatabase "SELECT count(*) FROM refreshtokens r JOIN customers c ON c.customerid = r.userid WHERE c.email = 'account@verification.invalid';") -eq '1')
    $taskResponse = Submit-VerificationForm $taskClient '/' '/Account/Logout' @{}
    Assert-Verification 'MVC logout redirects to sign-in' ($taskResponse.Status -eq 302 -and $taskResponse.Location -eq '/Account/Login')
    Assert-Verification 'Logout removes the stored refresh token' ((Invoke-VerificationSql $taskDatabase "SELECT count(*) FROM refreshtokens r JOIN customers c ON c.customerid = r.userid WHERE c.email = 'account@verification.invalid';") -eq '0')
    $taskResponse = Submit-VerificationForm $taskClient '/Account/Login' '/Account/Login' @{ Email = 'account@verification.invalid'; Password = 'incorrect-password' }
    Assert-Verification 'Invalid MVC login renders validation' ($taskResponse.Status -eq 200 -and $taskResponse.Html -match 'Invalid email or password')
    $taskResponse = Submit-VerificationForm $taskClient '/Account/Login' '/Account/Login' @{ Email = 'account@verification.invalid'; Password = $taskPassword; ReturnUrl = 'https://example.invalid/redirect' }
    Assert-Verification 'Valid MVC login rejects external return URL' ($taskResponse.Status -eq 302 -and $taskResponse.Location -eq '/')

    $taskPassForm = @{ Name = 'Verification Pass'; Price = '75.50'; MaxVisits = '15'; ValidityDays = '45' }
    $taskResponse = Submit-VerificationForm $taskClient '/Passes/Create' '/Passes/Create' $taskPassForm
    Assert-Verification 'Create pass binds the MVC form' ($taskResponse.Status -eq 302)
    $taskPassId = Invoke-VerificationSql $taskDatabase "SELECT passtypeid FROM gympasses WHERE passname = 'Verification Pass';"
    if (-not $taskPassId) { throw 'Pass creation failed; dependent CRUD checks cannot continue.' }
    Assert-Verification 'Pass values are stored correctly' ((Invoke-VerificationSql $taskDatabase "SELECT price || ',' || maxvisits || ',' || validitydays FROM gympasses WHERE passtypeid = $taskPassId;") -eq '75.50,15,45')
    $taskResponse = Submit-VerificationForm $taskClient '/Passes/Create' '/Passes/Create' $taskPassForm
    Assert-Verification 'Duplicate pass name produces validation' ($taskResponse.Status -eq 200 -and $taskResponse.Html -match 'already exists')
    $taskPassForm.Id = $taskPassId
    $taskResponse = Submit-VerificationForm $taskClient "/Passes/Edit/$taskPassId" "/Passes/Edit/$taskPassId" $taskPassForm
    Assert-Verification 'Unchanged pass edit succeeds' ($taskResponse.Status -eq 302)
    $taskPassForm.Price = '80.25'
    $taskResponse = Submit-VerificationForm $taskClient "/Passes/Edit/$taskPassId" "/Passes/Edit/$taskPassId" $taskPassForm
    Assert-Verification 'Changed pass edit persists decimal price' ($taskResponse.Status -eq 302 -and (Invoke-VerificationSql $taskDatabase "SELECT price FROM gympasses WHERE passtypeid = $taskPassId;") -eq '80.25')
    $taskResponse = Submit-VerificationForm $taskClient '/Passes/Delete/101' '/Passes/Delete/101' @{}
    Assert-Verification 'Purchased pass deletion preserves customer history' ($taskResponse.Status -eq 302 -and (Invoke-VerificationSql $taskDatabase 'SELECT count(*) FROM gympasses WHERE passtypeid = 101;') -eq '1')
    $taskResponse = Submit-VerificationForm $taskClient "/Passes/Delete/$taskPassId" "/Passes/Delete/$taskPassId" @{}
    Assert-Verification 'Unused pass can be deleted' ($taskResponse.Status -eq 302 -and (Invoke-VerificationSql $taskDatabase "SELECT count(*) FROM gympasses WHERE passtypeid = $taskPassId;") -eq '0')
    $taskResponse = Submit-VerificationForm $taskClient '/Passes/Create' '/Passes/Create' @{ Name = 'Invalid Price'; Price = '-1'; MaxVisits = '0'; ValidityDays = '0' }
    Assert-Verification 'Pass numeric constraints are enforced server-side' ($taskResponse.Status -eq 200 -and (Invoke-VerificationSql $taskDatabase "SELECT count(*) FROM gympasses WHERE passname = 'Invalid Price';") -eq '0')

    $taskTrainerForm = @{ FullName = 'Verification Trainer'; Specialty = 'Conditioning'; Phone = '+359888000001' }
    $taskResponse = Submit-VerificationForm $taskClient '/Team/Create' '/Team/Create' $taskTrainerForm
    Assert-Verification 'Trainer creation binds name specialty and phone' ($taskResponse.Status -eq 302)
    $taskTrainerId = Invoke-VerificationSql $taskDatabase "SELECT trainerid FROM trainers WHERE phone = '+359888000001';"
    if (-not $taskTrainerId) { throw 'Trainer creation failed; dependent CRUD checks cannot continue.' }
    $taskResponse = Submit-VerificationForm $taskClient '/Team/Create' '/Team/Create' $taskTrainerForm
    Assert-Verification 'Duplicate trainer phone produces validation' ($taskResponse.Status -eq 200 -and $taskResponse.Html -match 'already exists')
    $taskTrainerForm.Id = $taskTrainerId
    $taskResponse = Submit-VerificationForm $taskClient "/Team/Edit/$taskTrainerId" "/Team/Edit/$taskTrainerId" $taskTrainerForm
    Assert-Verification 'Unchanged trainer edit succeeds' ($taskResponse.Status -eq 302)
    $taskTrainerForm.Specialty = 'Strength'
    $taskResponse = Submit-VerificationForm $taskClient "/Team/Edit/$taskTrainerId" "/Team/Edit/$taskTrainerId" $taskTrainerForm
    Assert-Verification 'Changed trainer edit persists' ($taskResponse.Status -eq 302 -and (Invoke-VerificationSql $taskDatabase "SELECT specialty FROM trainers WHERE trainerid = $taskTrainerId;") -eq 'Strength')
    $taskResponse = Submit-VerificationForm $taskClient '/Team/Delete/201' '/Team/Delete/201' @{}
    Assert-Verification 'Trainer deletion preserves visit history' ($taskResponse.Status -eq 302 -and (Invoke-VerificationSql $taskDatabase 'SELECT count(*) FROM trainers WHERE trainerid = 201;') -eq '1')
    $taskResponse = Submit-VerificationForm $taskClient "/Team/Delete/$taskTrainerId" "/Team/Delete/$taskTrainerId" @{}
    Assert-Verification 'Unused trainer can be deleted' ($taskResponse.Status -eq 302 -and (Invoke-VerificationSql $taskDatabase "SELECT count(*) FROM trainers WHERE trainerid = $taskTrainerId;") -eq '0')

    foreach ($taskInvalidPass in @(302, 303)) {
        $taskResponse = Submit-VerificationForm $taskClient '/Visits/Create' '/Visits/Create' @{ CustomerPassId = [string]$taskInvalidPass; TrainerId = '' }
        Assert-Verification "Expired or exhausted pass $taskInvalidPass cannot check in" ($taskResponse.Status -eq 200 -and $taskResponse.Html -match 'Choose a valid pass')
    }
    $taskResponse = Submit-VerificationForm $taskClient '/Visits/Create?passId=301' '/Visits/Create' @{ CustomerPassId = '301'; TrainerId = '201' }
    Assert-Verification 'Valid check-in redirects to visit details' ($taskResponse.Status -eq 302 -and $taskResponse.Location -match '/Visits/Details/')
    $taskVisitId = Invoke-VerificationSql $taskDatabase 'SELECT checkinid FROM checkins WHERE customerpassid = 301 ORDER BY checkindatetime DESC LIMIT 1;'
    Assert-Verification 'Check-in deducts exactly one visit' ((Invoke-VerificationSql $taskDatabase 'SELECT remainingvisits FROM customerpasses WHERE customerpassid = 301;') -eq '11')
    $taskResponse = Submit-VerificationForm $taskClient '/Visits/Create?passId=301' '/Visits/Create' @{ CustomerPassId = '301'; TrainerId = '' }
    Assert-Verification 'Second same-day check-in is rejected' ($taskResponse.Status -eq 200 -and $taskResponse.Html -match 'already been checked in today')
    Assert-Verification 'Rejected duplicate does not deduct another visit' ((Invoke-VerificationSql $taskDatabase 'SELECT remainingvisits FROM customerpasses WHERE customerpassid = 301;') -eq '11')
    $taskResponse = Submit-VerificationForm $taskClient "/Visits/Edit/$taskVisitId" "/Visits/Edit/$taskVisitId" @{ Id = $taskVisitId; TrainerId = '202'; CustomerPassId = '304'; CheckedInAt = '2000-01-01' }
    Assert-Verification 'Visit edit changes only trainer and ignores injected pass fields' ($taskResponse.Status -eq 302 -and (Invoke-VerificationSql $taskDatabase "SELECT customerpassid || ',' || trainerid FROM checkins WHERE checkinid = $taskVisitId;") -eq '301,202')
    Assert-Verification 'Editing a trainer leaves pass balance unchanged' ((Invoke-VerificationSql $taskDatabase 'SELECT remainingvisits FROM customerpasses WHERE customerpassid = 301;') -eq '11')
    $taskResponse = Submit-VerificationForm $taskClient "/Visits/Delete/$taskVisitId" "/Visits/Delete/$taskVisitId" @{}
    Assert-Verification 'Removing a check-in restores exactly one visit' ($taskResponse.Status -eq 302 -and (Invoke-VerificationSql $taskDatabase 'SELECT remainingvisits FROM customerpasses WHERE customerpassid = 301;') -eq '12')

    $taskResponse = Send-VerificationRequest $taskClient GET '/Customers?query=Maya'
    Assert-Verification 'Customer search filters real database records' ($taskResponse.Status -eq 200 -and $taskResponse.Html -match 'Maya Petrova' -and $taskResponse.Html -notmatch 'Alex Ivanov')
    $taskResponse = Send-VerificationRequest $taskClient GET '/Customers?page=2'
    Assert-Verification 'Customer pagination renders second-page data' ($taskResponse.Status -eq 200 -and $taskResponse.Html -match 'Verification Member 26')
    $taskResponse = Send-VerificationRequest $taskClient GET '/Customers?page=2147483647'
    Assert-Verification 'Out-of-range customer pagination clamps safely' ($taskResponse.Status -eq 200 -and $taskResponse.Html -match 'Verification Member 26')
    foreach ($taskRoute in @('/Customers/Details/1001', '/Customers/Purchase/1001', '/Visits', '/Passes', '/Team')) {
        Assert-Verification "MVC route $taskRoute renders real data" ((Send-VerificationRequest $taskClient GET $taskRoute).Status -eq 200)
    }

    foreach ($taskApi in @(@{ Controller = 'Gympass'; Id = 101 }, @{ Controller = 'Trainers'; Id = 201 }, @{ Controller = 'Checkin'; Id = 401 })) {
        Assert-Verification "Existing $($taskApi.Controller) GET API still works" ((Send-VerificationRequest $taskClient GET "/$($taskApi.Controller)/GetOne?id=$($taskApi.Id)").Status -eq 200)
        Assert-Verification "Existing $($taskApi.Controller) POST query API still works" ((Send-VerificationRequest $taskClient POST "/$($taskApi.Controller)/GetAll" $null @{ Page = 1; PageSize = 20 }).Status -eq 200)
    }
    $taskResponse = Send-VerificationRequest $taskClient POST '/Gympass/Create' $null @{ PassName = 'API Verification Pass'; Price = 10; MaxVisits = 2; ValidityDays = 5 }
    Assert-Verification 'Existing pass POST mutation API still works' ($taskResponse.Status -eq 201)
    $taskApiPassId = Invoke-VerificationSql $taskDatabase "SELECT passtypeid FROM gympasses WHERE passname = 'API Verification Pass';"
    $taskResponse = Send-VerificationRequest $taskClient PUT '/Gympass/Update' $null @{ PassTypeId = [int]$taskApiPassId; PassName = 'API Verification Pass'; Price = 12; MaxVisits = 2; ValidityDays = 5 }
    Assert-Verification 'Existing pass PUT API still works' ($taskResponse.Status -eq 200)
    Assert-Verification 'Existing pass DELETE API still works' ((Send-VerificationRequest $taskClient DELETE "/Gympass/Delete?id=$taskApiPassId").Status -eq 200)
    $taskResponse = Send-VerificationRequest $taskClient POST '/Checkin/Create' $null @{ CustomerPassId = 304; TrainerId = 201 }
    Assert-Verification 'Existing check-in POST API still works' ($taskResponse.Status -eq 201 -and (Invoke-VerificationSql $taskDatabase 'SELECT remainingvisits FROM customerpasses WHERE customerpassid = 304;') -eq '11')
    $taskApiVisitId = Invoke-VerificationSql $taskDatabase 'SELECT checkinid FROM checkins WHERE customerpassid = 304 LIMIT 1;'
    Assert-Verification 'Existing check-in DELETE API still restores balance' ((Send-VerificationRequest $taskClient DELETE "/Checkin/Delete?id=$taskApiVisitId").Status -eq 200 -and (Invoke-VerificationSql $taskDatabase 'SELECT remainingvisits FROM customerpasses WHERE customerpassid = 304;') -eq '12')

    $taskApiClient = New-VerificationClient
    Assert-Verification 'Existing auth login API still works' ((Send-VerificationRequest $taskApiClient POST '/auth/login' $null @{ Email = 'account@verification.invalid'; Password = $taskPassword }).Status -eq 200)
    Assert-Verification 'Existing auth refresh API still works' ((Send-VerificationRequest $taskApiClient POST '/auth/refresh').Status -eq 200)
    Assert-Verification 'Existing auth logout API still works' ((Send-VerificationRequest $taskApiClient DELETE '/auth/session').Status -eq 204)
    $taskApiClient.Dispose()

    $taskResponse = Send-VerificationRequest $taskClient POST '/Customers/Purchase/1026' @{ PassTypeId = '102' }
    Assert-Verification 'Customer pass assignment requires antiforgery' ($taskResponse.Status -eq 400)
    $taskResponse = Submit-VerificationForm $taskClient '/Customers/Purchase/1026' '/Customers/Purchase/1026' @{ PassTypeId = '' }
    Assert-Verification 'Customer pass assignment requires a selected type' ($taskResponse.Status -eq 200 -and $taskResponse.Html -match 'field-validation-error')
    $taskResponse = Submit-VerificationForm $taskClient '/Customers/Purchase/1026' '/Customers/Purchase/1026' @{ PassTypeId = '999999' }
    Assert-Verification 'Unknown pass type is rejected without a membership' ($taskResponse.Status -eq 200 -and (Invoke-VerificationSql $taskDatabase 'SELECT count(*) FROM customerpasses WHERE customerid = 1026;') -eq '0')
    $taskResponse = Submit-VerificationForm $taskClient '/Customers/Purchase/1026' '/Customers/Purchase/1026' @{ PassTypeId = '102'; CustomerId = '1001'; RemainingVisits = '999'; Expirydate = '2099-01-01' }
    Assert-Verification 'Customer pass assignment redirects to the customer profile' ($taskResponse.Status -eq 302 -and $taskResponse.Location -eq '/Customers/Details/1026')
    $taskAssignedPassId = Invoke-VerificationSql $taskDatabase 'SELECT customerpassid FROM customerpasses WHERE customerid = 1026 ORDER BY customerpassid DESC LIMIT 1;'
    Assert-Verification 'Assigned membership uses route customer and catalog visit allowance' ((Invoke-VerificationSql $taskDatabase "SELECT customerid || ',' || passtypeid || ',' || remainingvisits FROM customerpasses WHERE customerpassid = $taskAssignedPassId;") -eq '1026,102,1')
    Assert-Verification 'Purchase and expiry dates come from the server and catalog' ((Invoke-VerificationSql $taskDatabase "SELECT count(*) FROM customerpasses WHERE customerpassid = $taskAssignedPassId AND purchasedate = CURRENT_DATE AND expirydate = CURRENT_DATE + 1;") -eq '1')
    $taskResponse = Send-VerificationRequest $taskClient GET "/Visits/Create?passId=$taskAssignedPassId"
    Assert-Verification 'Newly assigned pass appears in check-in selection' ($taskResponse.Status -eq 200 -and $taskResponse.Html.Contains(('id="pass-' + $taskAssignedPassId + '"')) -and $taskResponse.Html -match 'Day Entry')
    $taskResponse = Submit-VerificationForm $taskClient "/Visits/Create?passId=$taskAssignedPassId" '/Visits/Create' @{ CustomerPassId = $taskAssignedPassId; TrainerId = '' }
    Assert-Verification 'Newly assigned pass can check in and deduct its visit' ($taskResponse.Status -eq 302 -and (Invoke-VerificationSql $taskDatabase "SELECT remainingvisits FROM customerpasses WHERE customerpassid = $taskAssignedPassId;") -eq '0')
    $taskResponse = Submit-VerificationForm $taskClient '/Customers/Purchase/1026' '/Customers/Purchase/999999' @{ PassTypeId = '102' }
    Assert-Verification 'Unknown customer cannot receive a pass' ($taskResponse.Status -eq 404)
    Invoke-VerificationSql $taskDatabase "INSERT INTO gympasses (passtypeid, passname, price, maxvisits, validitydays) VALUES (104, 'Invalid date limit', 1, 1, 2147483647);" | Out-Null
    $taskResponse = Submit-VerificationForm $taskClient '/Customers/Purchase/1026' '/Customers/Purchase/1026' @{ PassTypeId = '104' }
    Assert-Verification 'Invalid catalog validity cannot overflow the expiry date' ($taskResponse.Status -eq 200 -and (Invoke-VerificationSql $taskDatabase 'SELECT count(*) FROM customerpasses WHERE passtypeid = 104;') -eq '0')
    Invoke-VerificationSql $taskDatabase 'DELETE FROM gympasses WHERE passtypeid = 104;' | Out-Null

    # Leave realistic active, expired, exhausted and recent-visit states for browser verification.
    $taskResponse = Submit-VerificationForm $taskClient '/Visits/Create?passId=301' '/Visits/Create' @{ CustomerPassId = '301'; TrainerId = '201' }
    $taskState.Fixtures['TodayVisitId'] = [int](Invoke-VerificationSql $taskDatabase 'SELECT checkinid FROM checkins WHERE customerpassid = 301 ORDER BY checkindatetime DESC LIMIT 1;')
    $taskState.Checks = @($taskChecks.ToArray())
    $taskState['Passed'] = @($taskChecks | Where-Object Passed).Count
    $taskState['Failed'] = @($taskChecks | Where-Object { -not $_.Passed }).Count
    $taskState | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $taskStatePath -Encoding UTF8
    $taskClient.Dispose()
    Write-Host "Verification completed: $($taskState.Passed) passed, $($taskState.Failed) failed."
    Write-Host "Isolated server remains available at $taskBaseUrl using $taskDatabase."
    Write-Host 'Run scripts/verify-mvc.ps1 -Cleanup after browser verification.'
    if ($taskState.Failed -gt 0) { exit 1 }
} finally {
    foreach ($taskVariable in $taskPreviousEnvironment.Keys) {
        [Environment]::SetEnvironmentVariable($taskVariable, $taskPreviousEnvironment[$taskVariable])
    }
}
