[CmdletBinding()]
param(
    [switch]$NoLaunch
)

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
    throw 'No se encontro .NET 8 SDK. Instala dotnet 8 o clona l2-tribe-server como repo hermano.'
}

Write-Host 'Compilando preview del launcher...' -ForegroundColor Cyan
& $dotnet build $project --configuration Debug
if ($LASTEXITCODE -ne 0) {
    throw "El preview no compilo (codigo $LASTEXITCODE)"
}

if (-not $NoLaunch) {
    $assembly = Join-Path $repoRoot 'bin\Debug\net8.0-windows\win-x64\L2TribeLauncher.dll'
    if (-not (Test-Path -LiteralPath $assembly -PathType Leaf)) {
        throw "No se encontro el preview compilado: $assembly"
    }
    Write-Host 'Abriendo L2 Tribe Launcher...' -ForegroundColor Green
    # Smart App Control can block unsigned local apphosts; the trusted SDK host
    # runs the exact same preview assembly without weakening Windows security.
    $process = Start-Process -FilePath $dotnet -ArgumentList @("`"$assembly`"") -WorkingDirectory $repoRoot -PassThru
    $process.WaitForExit()
}
