[CmdletBinding()]
param(
    [string]$RuntimeIdentifier = 'win-x64',
    [string]$OutputDir
)

$ErrorActionPreference = 'Stop'
$repoRoot = $PSScriptRoot
$project = Join-Path $repoRoot 'L2InterludeUpdater.csproj'
if (-not $OutputDir) {
    $OutputDir = Join-Path $repoRoot "build\$RuntimeIdentifier"
}

$dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
$dotnet = if ($dotnetCommand) {
    $dotnetCommand.Source
}
else {
    Join-Path $repoRoot '..\l2classic-interlude-custom\build\tooling\dotnet\dotnet.exe'
}
if (-not (Test-Path -LiteralPath $dotnet -PathType Leaf)) {
    throw 'No se encontro .NET 8 SDK. Instala dotnet 8 o clona l2classic-interlude-custom como repo hermano.'
}

if (Test-Path -LiteralPath $OutputDir) {
    Remove-Item -LiteralPath $OutputDir -Recurse -Force
}
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

& $dotnet publish $project `
    --configuration Release `
    --runtime $RuntimeIdentifier `
    --self-contained true `
    --output $OutputDir `
    -p:PublishSingleFile=true `
    -p:DebugType=None `
    -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish fallo con codigo $LASTEXITCODE"
}

$executable = Join-Path $OutputDir 'InterludeLauncher.exe'
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw "No se genero el launcher: $executable"
}

$hash = (Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash.ToLowerInvariant()
$hashPath = "$executable.sha256"
"$hash  InterludeLauncher.exe" | Set-Content -LiteralPath $hashPath -Encoding ASCII
$size = [Math]::Round((Get-Item -LiteralPath $executable).Length / 1MB, 2)

Write-Host ''
Write-Host 'L2 Hamburgo Launcher generado.' -ForegroundColor Green
Write-Host "  EXE:     $executable"
Write-Host "  Size:    $size MB"
Write-Host "  SHA-256: $hash"
