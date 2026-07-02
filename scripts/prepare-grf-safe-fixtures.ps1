[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ClassicSource,
    [string]$ProtectedSource
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$fixtureDirectory = Join-Path $repoRoot 'artifacts\grf-safe-fixtures'
$classicPath = (Resolve-Path -LiteralPath $ClassicSource).Path
$classicItem = Get-Item -LiteralPath $classicPath
if ($classicItem.PSIsContainer) { throw 'ClassicSource must be a file.' }

New-Item -ItemType Directory -Path $fixtureDirectory -Force | Out-Null
$fixturePath = Join-Path $fixtureDirectory 'classic-sample.grf'
$classicHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $classicPath).Hash.ToLowerInvariant()
Copy-Item -LiteralPath $classicPath -Destination $fixturePath -Force
$fixtureItem = Get-Item -LiteralPath $fixturePath
$fixtureItem.IsReadOnly = $false
$fixtureHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $fixturePath).Hash.ToLowerInvariant()
if ($fixtureHash -ne $classicHash) { throw 'The copied classic fixture does not match its source hash.' }

$manifest = [ordered]@{
    generatedUtc = [DateTime]::UtcNow.ToString('o')
    classic = [ordered]@{
        path = $classicPath
        length = $classicItem.Length
        sha256 = $classicHash
        fixture = $fixturePath
        fixtureSha256 = $fixtureHash
    }
}

if ($ProtectedSource) {
    $protectedPath = (Resolve-Path -LiteralPath $ProtectedSource).Path
    $protectedItem = Get-Item -LiteralPath $protectedPath
    $header = New-Object byte[] 46
    $stream = [System.IO.File]::Open($protectedPath, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read,
        [System.IO.FileShare]::ReadWrite -bor [System.IO.FileShare]::Delete)
    try {
        $read = $stream.Read($header, 0, $header.Length)
    }
    finally {
        $stream.Dispose()
    }
    if ($read -ne $header.Length) { throw 'The protected GRF header is truncated.' }
    $manifest.protected = [ordered]@{
        path = $protectedPath
        length = $protectedItem.Length
        lastWriteUtc = $protectedItem.LastWriteTimeUtc.ToString('o')
        headerHex = [BitConverter]::ToString($header).Replace('-', '').ToLowerInvariant()
        copied = $false
    }
}

$manifestPath = Join-Path $fixtureDirectory 'source-hashes.json'
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
Write-Output $fixtureDirectory
