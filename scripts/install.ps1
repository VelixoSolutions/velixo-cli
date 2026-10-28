<#
.SYNOPSIS
Installs the Velixo CLI from the latest GitHub Release, or from the version in -Version.

.EXAMPLE
irm https://cli.velixo.com/install.ps1 | iex

.EXAMPLE
& ([scriptblock]::Create((irm https://cli.velixo.com/install.ps1))) -Version 0.1.0
#>
param(
    [string]$Version
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

$repo = 'VelixoSolutions/velixo-cli'

function Fail([string]$Message) {
    Write-Host "error: $Message" -ForegroundColor Red
    throw $Message
}

if ($PSVersionTable.PSVersion.Major -ge 6 -and -not $IsWindows) {
    Fail 'this script is for Windows. On macOS and Linux, use: curl -fsSL https://cli.velixo.com/install.sh | sh'
}

$architecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
switch ($architecture) {
    'X64'   { $rid = 'win-x64' }
    'Arm64' { $rid = 'win-arm64' }
    default { Fail "unsupported processor '$architecture'. The Velixo CLI supports x64 and arm64." }
}

$headers = @{ 'User-Agent' = 'velixo-cli-installer' }

if ([string]::IsNullOrWhiteSpace($Version)) {
    $release = Invoke-RestMethod -Headers $headers -Uri "https://api.github.com/repos/$repo/releases/latest"
    $Version = $release.tag_name
}
$Version = $Version.TrimStart('v')

$asset = "velixo-$Version-$rid.zip"
$base = "https://github.com/$repo/releases/download/v$Version"
$installDir = Join-Path $env:LOCALAPPDATA 'Programs\velixo'
$tmp = Join-Path ([System.IO.Path]::GetTempPath()) ("velixo-install-" + [System.Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tmp | Out-Null

try {
    Write-Host "Installing Velixo CLI $Version for $rid"
    try {
        Invoke-WebRequest -Headers $headers -Uri "$base/$asset" -OutFile (Join-Path $tmp $asset)
        Invoke-WebRequest -Headers $headers -Uri "$base/checksums.txt" -OutFile (Join-Path $tmp 'checksums.txt')
    }
    catch {
        Fail "could not download $base/$asset. Check that version $Version exists and has a file for $rid."
    }

    $expected = $null
    foreach ($line in Get-Content (Join-Path $tmp 'checksums.txt')) {
        if ($line -match '^([0-9a-fA-F]{64})\s+\*?(\S+)$' -and $Matches[2] -eq $asset) {
            $expected = $Matches[1]
            break
        }
    }
    if (-not $expected) { Fail "checksums.txt has no entry for $asset." }

    $actual = (Get-FileHash -Algorithm SHA256 -Path (Join-Path $tmp $asset)).Hash
    if ($expected -ne $actual) {
        Fail "SHA-256 mismatch for $asset. Expected $expected, got $actual. Nothing was installed."
    }

    $extract = Join-Path $tmp 'extract'
    Expand-Archive -Path (Join-Path $tmp $asset) -DestinationPath $extract
    $binary = Join-Path $extract 'velixo.exe'
    if (-not (Test-Path $binary)) { Fail 'the archive does not contain velixo.exe.' }

    New-Item -ItemType Directory -Path $installDir -Force | Out-Null
    Copy-Item -Path $binary -Destination (Join-Path $installDir 'velixo.exe') -Force
    Write-Host "Installed $installDir\velixo.exe"

    $userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
    $entries = @()
    if ($userPath) { $entries = $userPath -split ';' | Where-Object { $_ } }
    if ($entries -notcontains $installDir) {
        $newPath = (($entries + $installDir) -join ';')
        [Environment]::SetEnvironmentVariable('Path', $newPath, 'User')
        Write-Host "Added $installDir to your user PATH. Open a new terminal to use it."
    }
    if (($env:Path -split ';') -notcontains $installDir) {
        $env:Path = "$env:Path;$installDir"
    }

    Write-Host ''
    Write-Host 'Next step: velixo login'
}
finally {
    Remove-Item -Recurse -Force -Path $tmp -ErrorAction SilentlyContinue
}
