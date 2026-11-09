<#
    Verifies the staged offline dependency bundle under dependencies\:

      1. Required payload files exist (Vosk model config, eSpeak NG executable and
         data sentinel, a Chromium build, license notices).
      2. manifest.sha256 matches every staged file byte-for-byte (SHA-256), and
         every staged file is listed in the manifest.

    Exit code 0 = bundle complete and untampered; 1 = problems were found.
    Used by install-dependencies.bat; performs no downloads.
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$Root
)

$ErrorActionPreference = 'Stop'
$failures = [System.Collections.Generic.List[string]]::new()

# Get-FileHash is unavailable in some Windows PowerShell 5.1 contexts, and this
# script is invoked from .bat via powershell.exe — hash through .NET directly.
function Get-Sha256Hex([string]$Path) {
    $stream = [IO.File]::OpenRead($Path)
    try {
        $sha = [Security.Cryptography.SHA256]::Create()
        try {
            return ([BitConverter]::ToString($sha.ComputeHash($stream))).Replace('-', '').ToLowerInvariant()
        }
        finally {
            $sha.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }
}

function Require-File([string]$RelativePath) {
    if (-not (Test-Path (Join-Path $Root $RelativePath) -PathType Leaf)) {
        $failures.Add("Missing payload file: $RelativePath")
    }
}

# --- 1. Payload presence -------------------------------------------------------
Require-File 'vosk/vosk-model-small-en-us-0.15/conf/model.conf'
Require-File 'espeak-ng/espeak-ng.exe'
Require-File 'espeak-ng/espeak-ng-data/phontab'

$chromium = Get-ChildItem -Path (Join-Path $Root 'playwright/browsers') -Directory -Filter 'chromium*' -ErrorAction SilentlyContinue |
    Where-Object { Test-Path (Join-Path $_.FullName 'chrome-win/chrome.exe') } |
    Select-Object -First 1
if (-not $chromium) {
    $failures.Add('Missing payload file: playwright/browsers/chromium-*/chrome-win/chrome.exe')
}

$licenses = Get-ChildItem -Path (Join-Path $Root 'licenses') -File -ErrorAction SilentlyContinue
if (-not $licenses -or $licenses.Count -eq 0) {
    $failures.Add('Missing payload files: licenses/* (third-party notices)')
}

# --- 2. Manifest integrity ------------------------------------------------------
$manifestPath = Join-Path $Root 'manifest.sha256'
if (-not (Test-Path $manifestPath -PathType Leaf)) {
    $failures.Add('Missing manifest.sha256')
}
else {
    $expected = @{}
    foreach ($line in Get-Content $manifestPath) {
        if ($line -match '^\s*([0-9a-fA-F]{64})\s+\*?(.+?)\s*$') {
            $expected[$Matches[2].Replace('\', '/')] = $Matches[1].ToLowerInvariant()
        }
        elseif ($line.Trim().Length -gt 0 -and -not $line.TrimStart().StartsWith('#')) {
            $failures.Add("Unparseable manifest line: $line")
        }
    }

    foreach ($entry in $expected.GetEnumerator()) {
        $file = Join-Path $Root ($entry.Key.Replace('/', [IO.Path]::DirectorySeparatorChar))
        if (-not (Test-Path $file -PathType Leaf)) {
            $failures.Add("Manifest lists a missing file: $($entry.Key)")
            continue
        }

        $actual = Get-Sha256Hex $file
        if ($actual -ne $entry.Value) {
            $failures.Add("SHA-256 mismatch: $($entry.Key)")
        }
    }

    Get-ChildItem -Path $Root -Recurse -File | ForEach-Object {
        $relative = $_.FullName.Substring($Root.Length).TrimStart('\', '/').Replace('\', '/')
        if ($relative -ne 'manifest.sha256' -and -not $expected.ContainsKey($relative)) {
            $failures.Add("Staged file is not listed in manifest.sha256: $relative")
        }
    }
}

if ($failures.Count -gt 0) {
    foreach ($failure in $failures) {
        Write-Host "[ERROR] $failure"
        $escaped = $failure.Replace('%', '%25').Replace("`r", '%0D').Replace("`n", '%0A')
        Write-Host "::error title=Verify dependencies::$escaped"
    }
    exit 1
}

Write-Host "[OK] Offline dependency bundle verified (payload present, SHA-256 manifest intact)."
exit 0
