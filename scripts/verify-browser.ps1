[CmdletBinding()]
param(
    [string]$BaseUrl = 'http://localhost:5290',
    [string[]]$Paths = @('/', '/Account/Login', '/Account/Register'),
    [string]$OutputDirectory,
    [string]$BrowserPath,
    [string]$LoginEmail,
    [string]$LoginPassword
)

# Read-only browser checks: no forms are submitted and no application records are changed.
$ErrorActionPreference = 'Stop'
$taskRepositoryRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$taskArtifactsRoot = [IO.Path]::GetFullPath((Join-Path $taskRepositoryRoot 'artifacts'))
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $taskArtifactsRoot 'browser' }
$taskOutputRoot = [IO.Path]::GetFullPath($OutputDirectory)
$taskArtifactsPrefix = $taskArtifactsRoot.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
if (-not $taskOutputRoot.StartsWith($taskArtifactsPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'OutputDirectory must be a directory under this repository''s artifacts directory.'
}

$taskBaseUri = [Uri]$BaseUrl
if ($taskBaseUri.Scheme -notin @('http', 'https') -or $taskBaseUri.Host -notin @('localhost', '127.0.0.1', '[::1]', '::1')) {
    throw 'BaseUrl must point to a local HTTP or HTTPS development server.'
}
foreach ($taskRoute in $Paths) {
    if (-not $taskRoute.StartsWith('/') -or $taskRoute.StartsWith('//') -or $taskRoute.Contains('\')) {
        throw "Use local paths beginning with a single slash: $taskRoute"
    }
}

if (-not $BrowserPath) {
    $BrowserPath = @(
        'C:\Program Files\Google\Chrome\Application\chrome.exe',
        'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe',
        'C:\Program Files\Microsoft\Edge\Application\msedge.exe'
    ) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if (-not $BrowserPath -or -not (Test-Path -LiteralPath $BrowserPath)) {
    throw 'Chrome or Edge was not found. Supply its executable with -BrowserPath.'
}

New-Item -ItemType Directory -Path $taskOutputRoot -Force | Out-Null
$taskProfilePath = Join-Path $taskOutputRoot ('profile-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskProfilePath -Force | Out-Null
$script:taskSocket = $null
$script:taskCommandId = 0
$script:taskBrowserErrors = @()
$script:taskDocumentResponses = @()
$script:taskNavigationLoaded = $false
$taskBrowserProcess = $null
$taskResults = @()

function Send-CdpPacket {
    param([hashtable]$Packet)
    $taskJson = ConvertTo-Json -InputObject $Packet -Depth 40 -Compress
    $taskBytes = [Text.Encoding]::UTF8.GetBytes($taskJson)
    $taskSegment = [ArraySegment[byte]]::new($taskBytes)
    $taskCancellation = [Threading.CancellationTokenSource]::new(15000)
    try {
        [void]$script:taskSocket.SendAsync($taskSegment, [Net.WebSockets.WebSocketMessageType]::Text,
            $true, $taskCancellation.Token).GetAwaiter().GetResult()
    } finally {
        $taskCancellation.Dispose()
    }
}

function Receive-CdpPacket {
    $taskBuffer = New-Object byte[] 65536
    $taskSegment = [ArraySegment[byte]]::new($taskBuffer)
    $taskStream = [IO.MemoryStream]::new()
    $taskCancellation = [Threading.CancellationTokenSource]::new(15000)
    try {
        do {
            $taskReceived = $script:taskSocket.ReceiveAsync($taskSegment, $taskCancellation.Token).GetAwaiter().GetResult()
            if ($taskReceived.MessageType -eq [Net.WebSockets.WebSocketMessageType]::Close) {
                throw 'The browser closed its debugging connection.'
            }
            $taskStream.Write($taskBuffer, 0, $taskReceived.Count)
        } while (-not $taskReceived.EndOfMessage)
        $taskJson = [Text.Encoding]::UTF8.GetString($taskStream.ToArray())
        return ConvertFrom-Json -InputObject $taskJson
    } finally {
        $taskCancellation.Dispose()
        $taskStream.Dispose()
    }
}

function Invoke-Cdp {
    param([string]$Method, [hashtable]$Parameters = @{})
    $script:taskCommandId++
    $taskCurrentId = $script:taskCommandId
    Send-CdpPacket @{ id = $taskCurrentId; method = $Method; params = $Parameters }
    do {
        $taskPacket = Receive-CdpPacket
        switch ($taskPacket.method) {
            'Page.loadEventFired' {
                $script:taskNavigationLoaded = $true
            }
            'Runtime.exceptionThrown' {
                $script:taskBrowserErrors += [pscustomobject]@{
                    source = 'javascript'; message = $taskPacket.params.exceptionDetails.text
                    details = $taskPacket.params.exceptionDetails.exception.description
                }
            }
            'Runtime.consoleAPICalled' {
                if ($taskPacket.params.type -eq 'error') {
                    $script:taskBrowserErrors += [pscustomobject]@{
                        source = 'console'; message = (($taskPacket.params.args | ForEach-Object {
                            if ($null -ne $_.value) { [string]$_.value } else { $_.description }
                        }) -join ' ')
                    }
                }
            }
            'Log.entryAdded' {
                if ($taskPacket.params.entry.level -eq 'error') {
                    $script:taskBrowserErrors += [pscustomobject]@{
                        source = $taskPacket.params.entry.source
                        message = $taskPacket.params.entry.text; url = $taskPacket.params.entry.url
                    }
                }
            }
            'Network.responseReceived' {
                if ($taskPacket.params.type -eq 'Document') {
                    $script:taskDocumentResponses += [pscustomobject]@{
                        url = $taskPacket.params.response.url; status = $taskPacket.params.response.status
                    }
                }
            }
        }
    } while ($taskPacket.id -ne $taskCurrentId)
    if ($taskPacket.error) {
        throw "CDP $($Method) failed: $($taskPacket.error.message)"
    }
    return $taskPacket.result
}

function Invoke-BrowserJavaScript {
    param([string]$Expression)
    $taskEvaluation = Invoke-Cdp 'Runtime.evaluate' @{
        expression = $Expression; returnByValue = $true; awaitPromise = $true
    }
    if ($taskEvaluation.exceptionDetails) {
        throw "Browser check failed: $($taskEvaluation.exceptionDetails.text)"
    }
    return $taskEvaluation.result.value
}

function Send-BrowserKey {
    param([string]$Key, [int]$Code)
    $taskKeyParameters = @{
        type = 'keyDown'; key = $Key; code = $Key; windowsVirtualKeyCode = $Code; nativeVirtualKeyCode = $Code
    }
    if ($Key -eq 'Enter') {
        $taskKeyParameters.text = "`r"
        $taskKeyParameters.unmodifiedText = "`r"
    }
    Invoke-Cdp 'Input.dispatchKeyEvent' $taskKeyParameters | Out-Null
    Invoke-Cdp 'Input.dispatchKeyEvent' @{
        type = 'keyUp'; key = $Key; code = $Key; windowsVirtualKeyCode = $Code; nativeVirtualKeyCode = $Code
    } | Out-Null
}

try {
    $taskBrowserArguments = @(
        '--headless=new', '--disable-gpu', '--no-first-run', '--no-default-browser-check',
        '--disable-background-networking', '--disable-component-update', '--remote-debugging-port=0',
        '--remote-allow-origins=*', ('--user-data-dir="' + $taskProfilePath + '"'), 'about:blank'
    )
    $taskBrowserProcess = Start-Process -FilePath $BrowserPath -ArgumentList $taskBrowserArguments -WindowStyle Hidden -PassThru
    $taskPortFile = Join-Path $taskProfilePath 'DevToolsActivePort'
    $taskDeadline = [DateTime]::UtcNow.AddSeconds(15)
    while (-not (Test-Path -LiteralPath $taskPortFile)) {
        if ([DateTime]::UtcNow -gt $taskDeadline) { throw 'The headless browser did not expose its debugging port.' }
        Start-Sleep -Milliseconds 100
    }
    $taskDebugPort = Get-Content -LiteralPath $taskPortFile | Select-Object -First 1
    $taskTargets = Invoke-RestMethod -Uri "http://localhost:$taskDebugPort/json/list" -TimeoutSec 5
    $taskTarget = $taskTargets | Where-Object type -eq 'page' | Select-Object -First 1
    if (-not $taskTarget.webSocketDebuggerUrl) { throw 'No browser page target was found.' }
    $script:taskSocket = [Net.WebSockets.ClientWebSocket]::new()
    $taskCancellation = [Threading.CancellationTokenSource]::new(15000)
    try {
        [void]$script:taskSocket.ConnectAsync([Uri]$taskTarget.webSocketDebuggerUrl, $taskCancellation.Token).GetAwaiter().GetResult()
    } finally { $taskCancellation.Dispose() }
    foreach ($taskDomain in @('Page.enable', 'Runtime.enable', 'Log.enable', 'Network.enable')) {
        Invoke-Cdp $taskDomain | Out-Null
    }
    if ($LoginEmail) {
        if (-not $LoginPassword) { throw 'Supply LoginPassword with LoginEmail for an authenticated browser check.' }
        $taskLoginBody = @{ Email = $LoginEmail; Password = $LoginPassword } | ConvertTo-Json -Compress
        Invoke-RestMethod -Uri ([Uri]::new($taskBaseUri, '/auth/login')) -Method Post -ContentType 'application/json' -Body $taskLoginBody -SessionVariable taskBrowserSession | Out-Null
        foreach ($taskCookie in $taskBrowserSession.Cookies.GetCookies($taskBaseUri)) {
            $taskCookieParameters = @{
                name = $taskCookie.Name; value = $taskCookie.Value; url = $taskBaseUri.AbsoluteUri
                path = $taskCookie.Path; httpOnly = $taskCookie.HttpOnly; secure = $taskCookie.Secure
            }
            Invoke-Cdp 'Network.setCookie' $taskCookieParameters | Out-Null
        }
    }

    $taskViewports = @(
        @{ name = 'desktop'; width = 1440; height = 1000; mobile = $false },
        @{ name = 'tablet'; width = 768; height = 1024; mobile = $false },
        @{ name = 'mobile'; width = 390; height = 844; mobile = $true }
    )
    foreach ($taskViewport in $taskViewports) {
        Invoke-Cdp 'Emulation.setDeviceMetricsOverride' @{
            width = $taskViewport.width; height = $taskViewport.height; deviceScaleFactor = 1
            mobile = $taskViewport.mobile; screenWidth = $taskViewport.width; screenHeight = $taskViewport.height
        } | Out-Null
        foreach ($taskRoute in $Paths) {
            $script:taskBrowserErrors = @()
            $script:taskDocumentResponses = @()
            $script:taskNavigationLoaded = $false
            $taskPageUri = [Uri]::new($taskBaseUri, $taskRoute)
            $taskNavigation = Invoke-Cdp 'Page.navigate' @{ url = $taskPageUri.AbsoluteUri }
            if ($taskNavigation.errorText) { throw "Navigation failed: $($taskNavigation.errorText)" }
            $taskDeadline = [DateTime]::UtcNow.AddSeconds(15)
            do {
                $taskDocumentReady = Invoke-BrowserJavaScript "document.readyState === 'complete' && location.href !== 'about:blank'"
                $taskReady = $taskDocumentReady -and ($script:taskNavigationLoaded -or -not $taskNavigation.loaderId)
                if (-not $taskReady) { Start-Sleep -Milliseconds 100 }
            } while (-not $taskReady -and [DateTime]::UtcNow -lt $taskDeadline)
            if (-not $taskReady) { throw "Page loading timed out: $($taskPageUri.AbsoluteUri)" }
            Invoke-BrowserJavaScript 'document.fonts.ready.then(() => true)' | Out-Null
            $taskPage = Invoke-BrowserJavaScript @'
(() => ({
    url: location.href,
    title: document.title,
    heading: document.querySelector('main h1')?.textContent.trim() || null,
    viewportWidth: innerWidth,
    scrollWidth: document.documentElement.scrollWidth,
    horizontalOverflow: document.documentElement.scrollWidth > innerWidth + 1,
    brokenImages: [...document.images].filter(image => image.complete && image.naturalWidth === 0).map(image => image.getAttribute('src')),
    stylesLoaded: [...document.querySelectorAll('link[rel=stylesheet]')].every(link => !!link.sheet),
    menuAvailable: !!document.querySelector('[data-nav-toggle]') && document.querySelector('[data-nav-toggle]').getBoundingClientRect().width > 0
}))()
'@
            $taskMenu = $null
            if ($taskPage.menuAvailable) {
                Invoke-BrowserJavaScript "document.querySelector('[data-nav-toggle]').focus(); true" | Out-Null
                Send-BrowserKey 'Enter' 13
                $taskOpened = Invoke-BrowserJavaScript "document.querySelector('[data-nav-toggle]').getAttribute('aria-expanded') === 'true'"
                Send-BrowserKey 'Tab' 9
                $taskNavFocused = Invoke-BrowserJavaScript "!!document.activeElement?.closest('[data-nav-panel]')"
                Send-BrowserKey 'Escape' 27
                $taskClosed = Invoke-BrowserJavaScript "document.querySelector('[data-nav-toggle]').getAttribute('aria-expanded') === 'false'"
                $taskFocusReturned = Invoke-BrowserJavaScript "document.activeElement === document.querySelector('[data-nav-toggle]')"
                $taskMenu = [pscustomobject]@{
                    keyboardOpens = [bool]$taskOpened; tabReachesNavigation = [bool]$taskNavFocused
                    escapeCloses = [bool]$taskClosed; escapeRestoresFocus = [bool]$taskFocusReturned
                }
            }

            $taskSlug = ($taskRoute.Trim('/') -replace '[^a-zA-Z0-9-]', '-')
            if (-not $taskSlug) { $taskSlug = 'home' }
            $taskScreenshotPath = Join-Path $taskOutputRoot ($taskViewport.name + '-' + $taskSlug + '.png')
            Invoke-BrowserJavaScript 'document.activeElement?.blur(); true' | Out-Null
            $taskMetrics = Invoke-Cdp 'Page.getLayoutMetrics'
            $taskContentHeight = [Math]::Min(20000, [Math]::Max($taskViewport.height, [Math]::Ceiling($taskMetrics.cssContentSize.height)))
            $taskCapture = Invoke-Cdp 'Page.captureScreenshot' @{
                format = 'png'; fromSurface = $true; captureBeyondViewport = $true
                clip = @{ x = 0; y = 0; width = $taskViewport.width; height = $taskContentHeight; scale = 1 }
            }
            [IO.File]::WriteAllBytes($taskScreenshotPath, [Convert]::FromBase64String($taskCapture.data))

            $taskForms = Invoke-BrowserJavaScript @'
(() => [...document.forms].map(form => {
    const validatedFields = [...form.querySelectorAll('[required], [data-val-required]')]
        .filter(field => field.type !== 'hidden' && !field.matches(':disabled'));
    const result = {
        action: new URL(form.action).pathname,
        method: form.method.toUpperCase(),
        antiforgery: !!form.querySelector('input[name="__RequestVerificationToken"]'),
        requiredFields: validatedFields.map(field => field.name),
        emptySubmissionBlocked: null
    };
    if (!validatedFields.length) return result;
    const fields = [...form.querySelectorAll('input:not([type=hidden]):not([type=submit]):not([type=button]), select, textarea')];
    const originals = fields.map(field => ({ field, value: field.value, checked: field.checked }));
    fields.forEach(field => { field.value = ''; if (field.type === 'checkbox' || field.type === 'radio') field.checked = false; });
    const nativeValid = form.checkValidity();
    const hasJQueryValidation = !!window.jQuery?.validator && !!window.jQuery(form).data('validator');
    const jqueryValid = hasJQueryValidation ? window.jQuery(form).valid() : null;
    result.emptySubmissionBlocked = !nativeValid || jqueryValid === false;
    if (hasJQueryValidation) window.jQuery(form).validate().resetForm();
    originals.forEach(({ field, value, checked }) => { field.value = value; if (checked !== undefined) field.checked = checked; });
    return result;
}))()
'@
            # Drain queued console events without introducing any requests.
            Invoke-BrowserJavaScript 'true' | Out-Null
            $taskStatus = $script:taskDocumentResponses | Select-Object -Last 1
            $taskFailures = @()
            if ($taskStatus.status -ge 400 -or -not $taskStatus) { $taskFailures += 'HTTP document error' }
            if (-not $taskPage.heading) { $taskFailures += 'Missing main page heading' }
            if ($taskPage.horizontalOverflow) { $taskFailures += 'Horizontal page overflow' }
            if (-not $taskPage.stylesLoaded) { $taskFailures += 'Stylesheet failed to load' }
            if (@($taskPage.brokenImages).Count) { $taskFailures += 'Broken images' }
            if ($script:taskBrowserErrors.Count) { $taskFailures += 'Browser console errors' }
            if ($taskMenu -and (-not $taskMenu.keyboardOpens -or -not $taskMenu.tabReachesNavigation -or -not $taskMenu.escapeCloses -or -not $taskMenu.escapeRestoresFocus)) {
                $taskFailures += 'Mobile navigation keyboard interaction'
            }
            foreach ($taskForm in @($taskForms)) {
                if ($taskForm.method -eq 'POST' -and -not $taskForm.antiforgery) { $taskFailures += "Missing form antiforgery: $($taskForm.action)" }
                if ($taskForm.emptySubmissionBlocked -eq $false) { $taskFailures += "Empty required form accepted: $($taskForm.action)" }
            }
            $taskResults += [pscustomobject]@{
                viewport = $taskViewport.name; path = $taskRoute; status = $taskStatus.status
                page = $taskPage; menu = $taskMenu; forms = @($taskForms)
                errors = @($script:taskBrowserErrors); screenshot = $taskScreenshotPath
                passed = $taskFailures.Count -eq 0; failures = @($taskFailures)
            }
            Write-Host ("{0,-8} {1,-32} HTTP {2} {3}" -f $taskViewport.name, $taskRoute, $taskStatus.status,
                $(if ($taskFailures.Count) { 'FAIL: ' + ($taskFailures -join '; ') } else { 'PASS' }))
        }
    }
    $taskReportPath = Join-Path $taskOutputRoot 'report.json'
    ConvertTo-Json -InputObject $taskResults -Depth 20 | Set-Content -LiteralPath $taskReportPath -Encoding UTF8
    Write-Host "Report: $taskReportPath"
    if (@($taskResults | Where-Object { -not $_.passed }).Count) { throw 'One or more browser checks failed. See report.json and screenshots.' }
} finally {
    if ($script:taskSocket) {
        if ($script:taskSocket.State -eq [Net.WebSockets.WebSocketState]::Open) {
            try { Invoke-Cdp 'Browser.close' | Out-Null } catch { }
        }
        $script:taskSocket.Dispose()
    }
    if ($taskBrowserProcess) {
        if (-not $taskBrowserProcess.WaitForExit(3000)) {
            # Only stop the isolated browser instance created above.
            Stop-Process -Id $taskBrowserProcess.Id -Force -ErrorAction SilentlyContinue
        }
    }
    $taskResolvedProfile = [IO.Path]::GetFullPath($taskProfilePath)
    if ($taskResolvedProfile.StartsWith($taskOutputRoot.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase)) {
        try { Remove-Item -LiteralPath $taskResolvedProfile -Recurse -Force -ErrorAction Stop } catch {
            Write-Warning "The isolated browser profile remains in artifacts: $taskResolvedProfile"
        }
    }
}
