[CmdletBinding()]
param(
    [switch]$Watch,
    [switch]$NoMinify,
    [switch]$InstallOnly
)

$ErrorActionPreference = 'Stop'
$taskRepositoryRoot = Split-Path -Parent $PSScriptRoot
$taskRelease = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'tailwind-release.json') -Raw | ConvertFrom-Json

if ($env:OS -eq 'Windows_NT') {
    # The official Windows build runs on x64 and on ARM64 through Windows emulation.
    $taskPlatform = 'windows-x64'
} else {
    $taskArchitecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant()
    if ([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::OSX)) {
        $taskPlatform = "macos-$taskArchitecture"
    } else {
        $taskPlatform = "linux-$taskArchitecture"
    }
}

$taskAsset = $taskRelease.assets.PSObject.Properties[$taskPlatform].Value
if ($null -eq $taskAsset) {
    throw "No pinned Tailwind CLI is configured for '$taskPlatform'. See scripts/tailwind-release.json."
}

$taskToolDirectory = Join-Path $taskRepositoryRoot ".tools/tailwind/$($taskRelease.version)"
$taskExecutable = Join-Path $taskToolDirectory $taskAsset.file
New-Item -ItemType Directory -Path $taskToolDirectory -Force | Out-Null

if (-not (Test-Path -LiteralPath $taskExecutable)) {
    $taskDownload = "$taskExecutable.download"
    $taskDownloadUrl = "https://github.com/tailwindlabs/tailwindcss/releases/download/v$($taskRelease.version)/$($taskAsset.file)"
    Write-Host "Downloading Tailwind CSS $($taskRelease.version) for $taskPlatform..."
    try {
        [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
        $taskWebClient = New-Object System.Net.WebClient
        try {
            $taskWebClient.DownloadFile($taskDownloadUrl, $taskDownload)
        } finally {
            $taskWebClient.Dispose()
        }

        $taskDownloadedHash = (Get-FileHash -LiteralPath $taskDownload -Algorithm SHA256).Hash
        if ($taskDownloadedHash -ne $taskAsset.sha256) {
            throw 'The downloaded Tailwind executable did not match the pinned SHA256 checksum.'
        }

        Move-Item -LiteralPath $taskDownload -Destination $taskExecutable
    } finally {
        if (Test-Path -LiteralPath $taskDownload) {
            Remove-Item -LiteralPath $taskDownload -Force
        }
    }
}

if ((Get-FileHash -LiteralPath $taskExecutable -Algorithm SHA256).Hash -ne $taskAsset.sha256) {
    throw "The cached Tailwind executable failed checksum verification. Remove '$taskExecutable' and run this script again."
}

if ($env:OS -ne 'Windows_NT') {
    & chmod +x $taskExecutable
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not make the Tailwind CLI executable.'
    }
}

if ($InstallOnly) {
    Write-Host "Tailwind CSS $($taskRelease.version) is ready."
    return
}

$taskInput = Join-Path $taskRepositoryRoot 'FitnessApp/Styles/tailwind.css'
$taskOutput = Join-Path $taskRepositoryRoot 'FitnessApp/wwwroot/css/tailwind.css'
$taskArguments = @('--input', $taskInput, '--output', $taskOutput)
if ($Watch) {
    $taskArguments += '--watch=always'
} elseif (-not $NoMinify) {
    $taskArguments += '--minify'
}

& $taskExecutable @taskArguments
if ($LASTEXITCODE -ne 0) {
    throw "Tailwind CSS compilation failed (exit code $LASTEXITCODE)."
}
