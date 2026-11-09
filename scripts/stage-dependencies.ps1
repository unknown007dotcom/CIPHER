<#
    Stages the offline dependency bundle under dependencies\ and (re)generates
    dependencies\manifest.sha256. This is the ONLY step that downloads anything,
    and it is a developer/CI action: install-dependencies.bat and the MSI build
    never download.

    Payloads:
      vosk/vosk-model-small-en-us-0.15   alphacephei model zip (~40 MB)
      vosk/native/win-x64/libvosk.dll    from the restored Vosk NuGet package
      espeak-ng/                         latest eSpeak NG MSI (admin-extracted)
      playwright/browsers/chromium-*     from the local Playwright browser cache
      licenses/                          Apache/GPL notice texts

    Run after `dotnet restore CIPHER.sln` (libvosk + license sources come from
    the NuGet cache) and, for Chromium, after
    `pwsh src/CIPHER.BrowserTest/bin/Release/net10.0/playwright.ps1 install chromium`
    (or set PLAYWRIGHT_BROWSERS_PATH). Re-running refreshes payloads and rewrites
    the manifest.
#>
param(
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
$deps = Join-Path $RepoRoot 'dependencies'
$temp = Join-Path ([IO.Path]::GetTempPath()) ('cipher-stage-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp -Force | Out-Null
Write-Host "Staging offline dependencies into $deps (scratch: $temp)"

try {
    # --- Vosk model -------------------------------------------------------------
    $modelDir = Join-Path $deps 'vosk/vosk-model-small-en-us-0.15'
    if (-not (Test-Path (Join-Path $modelDir 'conf/model.conf'))) {
        $zip = Join-Path $temp 'vosk-model.zip'
        Invoke-WebRequest -Uri 'https://alphacephei.com/vosk/models/vosk-model-small-en-us-0.15.zip' -OutFile $zip
        $extract = Join-Path $temp 'vosk-model'
        Expand-Archive -LiteralPath $zip -DestinationPath $extract -Force
        New-Item -ItemType Directory -Path (Split-Path $modelDir -Parent) -Force | Out-Null
        Copy-Item -Path (Join-Path $extract 'vosk-model-small-en-us-0.15') -Destination $modelDir -Recurse -Force
    }
    Write-Host '[OK] Vosk model'

    # --- libvosk native -----------------------------------------------------------
    $nugetRoot = Join-Path $env:USERPROFILE '.nuget/packages'
    $libvosk = Get-ChildItem -Path (Join-Path $nugetRoot 'vosk') -Recurse -Filter 'libvosk.dll' -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match 'win-x64' } | Select-Object -First 1
    if (-not $libvosk) {
        throw 'libvosk.dll (win-x64) not found in the NuGet cache. Run: dotnet restore CIPHER.sln'
    }
    $nativeDir = Join-Path $deps 'vosk/native/win-x64'
    New-Item -ItemType Directory -Path $nativeDir -Force | Out-Null
    Copy-Item $libvosk.FullName (Join-Path $nativeDir 'libvosk.dll') -Force
    Write-Host '[OK] libvosk.dll'

    # --- eSpeak NG ------------------------------------------------------------------
    $espeakDir = Join-Path $deps 'espeak-ng'
    if (-not (Test-Path (Join-Path $espeakDir 'espeak-ng.exe'))) {
        $headers = @{ 'User-Agent' = 'CIPHER-CI' }
        $release = Invoke-RestMethod -Uri 'https://api.github.com/repos/espeak-ng/espeak-ng/releases/latest' -Headers $headers
        $asset = $release.assets | Where-Object { $_.name -like '*.msi' } | Select-Object -First 1
        if (-not $asset) { throw 'No eSpeak NG MSI asset found in the latest release.' }
        $msi = Join-Path $temp 'espeak-ng.msi'
        Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $msi -Headers $headers
        $extract = Join-Path $temp 'espeak-extract'
        $proc = Start-Process msiexec.exe -ArgumentList "/a `"$msi`" /qn TARGETDIR=`"$extract`"" -Wait -PassThru
        if ($proc.ExitCode -ne 0) { throw "msiexec administrative install failed with exit code $($proc.ExitCode)." }
        $exe = Get-ChildItem -Path $extract -Recurse -Filter 'espeak-ng.exe' | Select-Object -First 1
        if (-not $exe) { throw 'espeak-ng.exe was not found in the extracted MSI.' }

        New-Item -ItemType Directory -Path $espeakDir -Force | Out-Null
        Copy-Item $exe.FullName (Join-Path $espeakDir 'espeak-ng.exe') -Force
        $lib = Join-Path $exe.DirectoryName 'libespeak-ng.dll'
        if (Test-Path $lib) { Copy-Item $lib (Join-Path $espeakDir 'libespeak-ng.dll') -Force }

        # espeak-ng-data may sit next to the exe or one level up (the ESPEAK_DATA_PATH
        # handling in CIPHER.Voice covers both; staging prefers next-to-exe).
        $dataDir = Get-ChildItem -Path $extract -Recurse -Directory -Filter 'espeak-ng-data' | Select-Object -First 1
        if (-not $dataDir) { throw 'espeak-ng-data was not found in the extracted MSI.' }
        Copy-Item $dataDir.FullName (Join-Path $espeakDir 'espeak-ng-data') -Recurse -Force
    }
    Write-Host '[OK] eSpeak NG'

    # --- Playwright Chromium ---------------------------------------------------------
    $browsersDest = Join-Path $deps 'playwright/browsers'
    if (-not (Get-ChildItem -Path $browsersDest -Directory -Filter 'chromium*' -ErrorAction SilentlyContinue)) {
        $source = $env:PLAYWRIGHT_BROWSERS_PATH
        if ([string]::IsNullOrWhiteSpace($source)) {
            $source = Join-Path $env:LOCALAPPDATA 'ms-playwright'
        }
        $chromiumDirs = Get-ChildItem -Path $source -Directory -Filter 'chromium*' -ErrorAction SilentlyContinue
        if (-not $chromiumDirs) {
            throw "No Chromium build found under '$source'. Run: pwsh src/CIPHER.BrowserTest/bin/Release/net10.0/playwright.ps1 install chromium"
        }
        New-Item -ItemType Directory -Path $browsersDest -Force | Out-Null
        foreach ($dir in $chromiumDirs) {
            Copy-Item $dir.FullName (Join-Path $browsersDest $dir.Name) -Recurse -Force
        }
    }
    Write-Host '[OK] Playwright Chromium'

    # --- Licenses ----------------------------------------------------------------------
    $licenseDir = Join-Path $deps 'licenses'
    New-Item -ItemType Directory -Path $licenseDir -Force | Out-Null
    Invoke-WebRequest -Uri 'https://raw.githubusercontent.com/alphacep/vosk-api/master/LICENSE' -OutFile (Join-Path $licenseDir 'LICENSE-VOSK.txt')
    Invoke-WebRequest -Uri 'https://raw.githubusercontent.com/microsoft/playwright/main/LICENSE' -OutFile (Join-Path $licenseDir 'LICENSE-PLAYWRIGHT.txt')
    $gpl = Get-ChildItem -Path $temp -Recurse -Filter 'COPYING*' -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($gpl) {
        Copy-Item $gpl.FullName (Join-Path $licenseDir 'LICENSE-ESPEAK-NG.txt') -Force
    }
    else {
        Invoke-WebRequest -Uri 'https://raw.githubusercontent.com/espeak-ng/espeak-ng/master/COPYING' -OutFile (Join-Path $licenseDir 'LICENSE-ESPEAK-NG.txt')
    }
    Write-Host '[OK] License notices'

    # --- Manifest ----------------------------------------------------------------------
    $manifest = Join-Path $deps 'manifest.sha256'
    $lines = Get-ChildItem -Path $deps -Recurse -File |
        Where-Object { $_.FullName -ne $manifest } |
        Sort-Object FullName |
        ForEach-Object {
            $relative = $_.FullName.Substring($deps.Length).TrimStart('\', '/').Replace('\', '/')
            $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            "$hash  $relative"
        }
    Set-Content -Path $manifest -Value $lines -Encoding ascii
    Write-Host "[OK] manifest.sha256 regenerated ($($lines.Count) files)"
}
finally {
    Remove-Item $temp -Recurse -Force -ErrorAction SilentlyContinue
}
