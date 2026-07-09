[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = $PSScriptRoot
$project = Join-Path $repoRoot 'L2TribeLauncher.csproj'

$dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
$dotnet = if ($dotnetCommand) {
    $dotnetCommand.Source
}
else {
    @(
        (Join-Path $repoRoot '..\l2-tribe-server\build\tooling\dotnet\dotnet.exe'),
        (Join-Path $repoRoot '..\l2classic-interlude-custom\build\tooling\dotnet\dotnet.exe')
    ) | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
}
if (-not (Test-Path -LiteralPath $dotnet -PathType Leaf)) {
    throw 'No se encontro .NET 8 SDK. Instala dotnet 8 o clona l2-tribe-server/l2classic-interlude-custom como repo hermano.'
}

Write-Host 'Abriendo L2 Tribe Launcher en modo desarrollo...' -ForegroundColor Cyan
Write-Host 'Este modo evita Smart App Control porque no ejecuta el .exe single-file generado.' -ForegroundColor DarkGray
& $dotnet run --project $project --configuration Release
