[CmdletBinding()]
param([string]$BrowserPath, [string]$StateFile = 'artifacts/mvc-verification/state.json')

# Run only against the isolated database/server created by verify-mvc.ps1.
$ErrorActionPreference = 'Stop'
$taskRepository = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$taskArtifactsRoot = [IO.Path]::GetFullPath((Join-Path $taskRepository 'artifacts')).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
$taskStateFile = [IO.Path]::GetFullPath($(if ([IO.Path]::IsPathRooted($StateFile)) { $StateFile } else { Join-Path $taskRepository $StateFile }))
if (-not $taskStateFile.StartsWith($taskArtifactsRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'StateFile must resolve to a file under this repository''s artifacts directory.'
}
if (-not (Test-Path -LiteralPath $taskStateFile)) { throw 'Run verify-mvc.ps1 first to create the isolated test server.' }
$taskState = Get-Content -LiteralPath $taskStateFile -Raw | ConvertFrom-Json
if ($taskState.CleanedUp -or $taskState.Database -notmatch '^fitnessapp_ui_verify_[a-f0-9]{12}$') {
    throw 'The isolated verification server is not active.'
}
$taskServer = Get-CimInstance Win32_Process -Filter "ProcessId = $($taskState.ProcessId)" -ErrorAction SilentlyContinue
if (-not $taskServer -or $taskServer.CommandLine -notlike "*$($taskState.ServerAssembly)*" -or $taskServer.CommandLine -notlike "*$($taskState.BaseUrl)*") {
    throw 'The recorded process does not own the isolated verification server.'
}
$taskBaseUri = [Uri]$taskState.BaseUrl
if ($taskBaseUri.Host -notin @('127.0.0.1', 'localhost') -or $taskBaseUri.Scheme -ne 'http') {
    throw 'Expected a local HTTP verification server.'
}
if (-not $BrowserPath) {
    $BrowserPath = @('C:\Program Files\Google\Chrome\Application\chrome.exe',
        'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe') |
        Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if (-not $BrowserPath) { throw 'Supply a Chrome or Edge executable using -BrowserPath.' }
$taskOutput = Join-Path $taskRepository 'artifacts/browser-profile-picture'
New-Item -ItemType Directory -Path $taskOutput -Force | Out-Null
$taskProfile = Join-Path $taskOutput ('profile-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskProfile | Out-Null
$script:taskSocket = $null
$script:taskCommand = 0
$script:taskConsoleErrors = @()
$script:taskPageLoaded = $false
$taskBrowser = $null
$taskSession = $null
$taskChecks = [Collections.Generic.List[object]]::new()

function Invoke-PictureCdp([string]$Method, [hashtable]$Parameters = @{}) {
    $script:taskCommand++
    $taskId = $script:taskCommand
    $taskPacket = @{ id = $taskId; method = $Method; params = $Parameters } | ConvertTo-Json -Depth 30 -Compress
    $taskBytes = [Text.Encoding]::UTF8.GetBytes($taskPacket)
    $taskTimeout = [Threading.CancellationTokenSource]::new(30000)
    try {
        $script:taskSocket.SendAsync([ArraySegment[byte]]::new($taskBytes), [Net.WebSockets.WebSocketMessageType]::Text,
            $true, $taskTimeout.Token).GetAwaiter().GetResult() | Out-Null
        do {
            $taskBuffer = New-Object byte[] 65536
            $taskStream = [IO.MemoryStream]::new()
            try {
                do {
                    $taskRead = $script:taskSocket.ReceiveAsync([ArraySegment[byte]]::new($taskBuffer), $taskTimeout.Token).GetAwaiter().GetResult()
                    if ($taskRead.MessageType -eq [Net.WebSockets.WebSocketMessageType]::Close) { throw 'Browser connection closed.' }
                    $taskStream.Write($taskBuffer, 0, $taskRead.Count)
                } while (-not $taskRead.EndOfMessage)
                $taskReply = [Text.Encoding]::UTF8.GetString($taskStream.ToArray()) | ConvertFrom-Json
            } finally { $taskStream.Dispose() }
            if ($taskReply.method -eq 'Runtime.exceptionThrown') { $script:taskConsoleErrors += $taskReply.params.exceptionDetails.text }
            if ($taskReply.method -eq 'Runtime.consoleAPICalled' -and $taskReply.params.type -eq 'error') { $script:taskConsoleErrors += 'console.error' }
            if ($taskReply.method -eq 'Page.loadEventFired') { $script:taskPageLoaded = $true }
        } while ($taskReply.id -ne $taskId)
        if ($taskReply.error) { throw "Browser command failed: $($taskReply.error.message)" }
        return $taskReply.result
    } finally { $taskTimeout.Dispose() }
}

function Invoke-PictureJavaScript([string]$Expression) {
    $taskValue = Invoke-PictureCdp 'Runtime.evaluate' @{ expression = $Expression; returnByValue = $true; awaitPromise = $true }
    if ($taskValue.exceptionDetails) { throw "Browser script failed: $($taskValue.exceptionDetails.text)" }
    return $taskValue.result.value
}

function Assert-PictureBrowser([string]$Name, [bool]$Passed) {
    $taskChecks.Add([pscustomobject]@{ Name = $Name; Passed = $Passed })
    if (-not $Passed) { throw "FAIL $Name" }
    Write-Host "PASS $Name"
}

function Wait-PictureBrowser([string]$Condition) {
    $taskWaitScript = @'
(async () => {
    const deadline = Date.now() + 15000;
    while (!(CONDITION)) {
        if (Date.now() > deadline) throw new Error('Picture UI condition timed out');
        await new Promise(resolve => setTimeout(resolve, 50));
    }
    return true;
})()
'@
    Invoke-PictureJavaScript ($taskWaitScript.Replace('CONDITION', $Condition)) | Out-Null
}

function Wait-PicturePage {
    $taskDeadline = [DateTime]::UtcNow.AddSeconds(15)
    do {
        $taskReady = Invoke-PictureJavaScript "document.readyState === 'complete' && !!document.querySelector('[data-profile-picture]')"
        if ($script:taskPageLoaded -and $taskReady) { return }
        Start-Sleep -Milliseconds 50
    } while ([DateTime]::UtcNow -lt $taskDeadline)
    throw 'The profile page did not finish loading.'
}

try {
    # All account/image writes go to the guarded isolated verification database.
    $taskRegistration = @{
        Email = ('picture-browser-' + [Guid]::NewGuid().ToString('N') + '@verification.invalid')
        Username = 'Picture Browser Verification'; Password = ('Picture-Test-' + [Guid]::NewGuid().ToString('N') + '!')
        Dateofbirth = '1990-01-01'
    } | ConvertTo-Json -Compress
    Invoke-RestMethod -Uri ([Uri]::new($taskBaseUri, '/auth/register')) -Method Post -ContentType 'application/json' -Body $taskRegistration -SessionVariable taskSession | Out-Null
    $taskBrowser = Start-Process -FilePath $BrowserPath -ArgumentList @('--headless=new', '--disable-gpu', '--no-first-run',
        '--no-default-browser-check', '--disable-background-networking', '--disable-component-update', '--remote-debugging-port=0',
        '--remote-allow-origins=*', ('--user-data-dir="' + $taskProfile + '"'), 'about:blank') -WindowStyle Hidden -PassThru
    $taskPortFile = Join-Path $taskProfile 'DevToolsActivePort'
    $taskDeadline = [DateTime]::UtcNow.AddSeconds(15)
    while (-not (Test-Path -LiteralPath $taskPortFile)) {
        if ([DateTime]::UtcNow -gt $taskDeadline) { throw 'Browser startup timed out.' }
        Start-Sleep -Milliseconds 100
    }
    $taskDebugPort = Get-Content -LiteralPath $taskPortFile | Select-Object -First 1
    $taskTargets = Invoke-RestMethod -Uri "http://localhost:$taskDebugPort/json/list"
    $taskTarget = $taskTargets | Where-Object type -eq 'page' | Select-Object -First 1
    if (-not $taskTarget.webSocketDebuggerUrl) { throw 'No headless browser page target was found.' }
    $script:taskSocket = [Net.WebSockets.ClientWebSocket]::new()
    $taskConnectTimeout = [Threading.CancellationTokenSource]::new(15000)
    try { $script:taskSocket.ConnectAsync([Uri]$taskTarget.webSocketDebuggerUrl, $taskConnectTimeout.Token).GetAwaiter().GetResult() | Out-Null }
    finally { $taskConnectTimeout.Dispose() }
    foreach ($taskDomain in @('Page.enable', 'Runtime.enable', 'Network.enable')) { Invoke-PictureCdp $taskDomain | Out-Null }
    foreach ($taskCookie in $taskSession.Cookies.GetCookies($taskBaseUri)) {
        Invoke-PictureCdp 'Network.setCookie' @{
            name = $taskCookie.Name; value = $taskCookie.Value; url = $taskBaseUri.AbsoluteUri
            path = $taskCookie.Path; httpOnly = $taskCookie.HttpOnly; secure = $taskCookie.Secure
        } | Out-Null
    }
    foreach ($taskViewport in @(@{ Name = 'desktop'; Width = 1440; Height = 1000 }, @{ Name = 'mobile'; Width = 390; Height = 844 })) {
        Invoke-PictureCdp 'Emulation.setDeviceMetricsOverride' @{
            width = $taskViewport.Width; height = $taskViewport.Height; deviceScaleFactor = 1; mobile = $false
        } | Out-Null
        $script:taskPageLoaded = $false
        Invoke-PictureCdp 'Page.navigate' @{ url = ([Uri]::new($taskBaseUri, '/Member')).AbsoluteUri } | Out-Null
        Wait-PicturePage
        $taskPrefix = $taskViewport.Name
        Assert-PictureBrowser "$taskPrefix default avatar loads" (Invoke-PictureJavaScript "document.querySelector('[data-picture-image]').naturalWidth > 0 && document.querySelector('[data-picture-remove]').hidden")
        Assert-PictureBrowser "$taskPrefix antiforgery token present" (Invoke-PictureJavaScript "!!document.querySelector('[data-profile-picture] input[name=__RequestVerificationToken]').value")
        Invoke-PictureJavaScript @'
(() => {
    window.pictureTest = {
        choose(file) {
            const data = new DataTransfer(); data.items.add(file);
            const input = document.querySelector('[data-picture-file]'); input.files = data.files;
            input.dispatchEvent(new Event('change', { bubbles: true }));
        },
        async selectImage(color) {
            const canvas = document.createElement('canvas'); canvas.width = 1000; canvas.height = 500;
            canvas.getContext('2d').fillStyle = color; canvas.getContext('2d').fillRect(0, 0, 1000, 500);
            const blob = await new Promise(resolve => canvas.toBlob(resolve, 'image/png'));
            this.choose(new File([blob], 'profile.png', { type: 'image/png' })); return true;
        }
    };
    pictureTest.choose(new File(['invalid'], 'picture.txt', {type: 'text/plain'}));
    return true;
})()
'@ | Out-Null
        Assert-PictureBrowser "$taskPrefix invalid type message" (Invoke-PictureJavaScript "document.querySelector('[data-picture-message]').textContent.includes('JPEG') && document.querySelector('[data-picture-upload]').hidden")
        Invoke-PictureJavaScript "pictureTest.choose(new File([new Uint8Array(5 * 1024 * 1024 + 1)], 'large.png', { type: 'image/png' })); true" | Out-Null
        Assert-PictureBrowser "$taskPrefix oversize message" (Invoke-PictureJavaScript "document.querySelector('[data-picture-message]').textContent.includes('5 MB')")
        Invoke-PictureJavaScript "pictureTest.selectImage('#047857')" | Out-Null
        Wait-PictureBrowser "document.querySelector('[data-picture-image]').naturalWidth === 1000"
        Assert-PictureBrowser "$taskPrefix preview before upload" (Invoke-PictureJavaScript "document.querySelector('[data-picture-image]').src.startsWith('blob:') && !document.querySelector('[data-picture-upload]').hidden")
        Invoke-PictureJavaScript "document.querySelector('[data-picture-cancel]').click(); true" | Out-Null
        Assert-PictureBrowser "$taskPrefix cancel restores avatar" (Invoke-PictureJavaScript "!document.querySelector('[data-picture-image]').src.startsWith('blob:') && document.querySelector('[data-picture-upload]').hidden")
        Invoke-PictureJavaScript "pictureTest.selectImage('#047857')" | Out-Null
        Invoke-PictureJavaScript "document.querySelector('[data-picture-upload]').click(); true" | Out-Null
        Wait-PictureBrowser "document.querySelector('[data-profile-picture]').getAttribute('aria-busy') === 'false' && document.querySelector('[data-picture-message]').textContent.includes('updated')"
        Wait-PictureBrowser "document.querySelector('[data-picture-image]').complete && document.querySelector('[data-picture-image]').naturalWidth === 512"
        Assert-PictureBrowser "$taskPrefix optimized image displays" (Invoke-PictureJavaScript "document.querySelector('[data-picture-image]').naturalHeight === 256 && !document.querySelector('[data-picture-remove]').hidden")
        $taskScreenshot = Invoke-PictureCdp 'Page.captureScreenshot' @{ format = 'png'; captureBeyondViewport = $true }
        [IO.File]::WriteAllBytes((Join-Path $taskOutput ($taskPrefix + '-uploaded.png')), [Convert]::FromBase64String($taskScreenshot.data))
        Invoke-PictureJavaScript "pictureTest.selectImage('#be123c')" | Out-Null
        Invoke-PictureJavaScript "document.querySelector('[data-picture-upload]').click(); true" | Out-Null
        Wait-PictureBrowser "document.querySelector('[data-profile-picture]').getAttribute('aria-busy') === 'false' && document.querySelector('[data-picture-message]').textContent.includes('updated')"
        Assert-PictureBrowser "$taskPrefix replacement succeeds" (Invoke-PictureJavaScript "!document.querySelector('[data-picture-remove]').hidden")
        $script:taskPageLoaded = $false
        Invoke-PictureCdp 'Page.reload' | Out-Null
        Wait-PicturePage
        Wait-PictureBrowser "document.querySelector('[data-picture-image]').naturalWidth === 512"
        Assert-PictureBrowser "$taskPrefix upload survives reload" (Invoke-PictureJavaScript "document.querySelector('[data-profile-picture]').dataset.hasPicture === 'true'")
        Invoke-PictureJavaScript "document.querySelector('[data-picture-remove]').click(); true" | Out-Null
        Wait-PictureBrowser "document.querySelector('[data-profile-picture]').getAttribute('aria-busy') === 'false' && document.querySelector('[data-picture-message]').textContent.includes('removed')"
        Assert-PictureBrowser "$taskPrefix removal restores default" (Invoke-PictureJavaScript "document.querySelector('[data-picture-remove]').hidden && document.querySelector('[data-picture-image]').getAttribute('src').includes('default-avatar.svg')")
        $script:taskPageLoaded = $false
        Invoke-PictureCdp 'Page.reload' | Out-Null
        Wait-PicturePage
        Assert-PictureBrowser "$taskPrefix deletion survives reload" (Invoke-PictureJavaScript "document.querySelector('[data-profile-picture]').dataset.hasPicture === 'false' && document.querySelector('[data-picture-remove]').hidden")
        Assert-PictureBrowser "$taskPrefix no horizontal overflow" (Invoke-PictureJavaScript "document.documentElement.scrollWidth <= innerWidth + 1")
    }
    Assert-PictureBrowser 'No JavaScript exceptions or console errors' ($script:taskConsoleErrors.Count -eq 0)
} finally {
    $taskChecks | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $taskOutput 'report.json') -Encoding UTF8
    if ($script:taskSocket) {
        if ($script:taskSocket.State -eq [Net.WebSockets.WebSocketState]::Open) {
            try { Invoke-PictureCdp 'Browser.close' | Out-Null } catch { }
        }
        $script:taskSocket.Dispose()
    }
    if ($taskBrowser -and -not $taskBrowser.WaitForExit(3000)) { Stop-Process -Id $taskBrowser.Id -Force -ErrorAction SilentlyContinue }
    if ($taskSession) {
        try { Invoke-RestMethod -Uri ([Uri]::new($taskBaseUri, '/auth/session')) -Method Delete -WebSession $taskSession | Out-Null } catch { }
    }
    $taskResolvedProfile = [IO.Path]::GetFullPath($taskProfile)
    $taskResolvedOutput = [IO.Path]::GetFullPath($taskOutput).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    if ($taskResolvedProfile.StartsWith($taskResolvedOutput, [StringComparison]::OrdinalIgnoreCase)) {
        try { Remove-Item -LiteralPath $taskResolvedProfile -Recurse -Force -ErrorAction Stop } catch { Write-Warning 'The temporary browser profile remains under artifacts/browser-profile-picture.' }
    }
}
Write-Host "Profile picture browser verification passed: $($taskChecks.Count) checks. Report: $taskOutput/report.json"
