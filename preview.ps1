[CmdletBinding()]
param(
    [switch]$NoLaunch
)

$ErrorActionPreference = 'Stop'
$repoRoot = $PSScriptRoot
$project = Join-Path $repoRoot 'L2InterludeUpdater.csproj'
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

Write-Host 'Compilando preview del launcher...' -ForegroundColor Cyan
& $dotnet build $project --configuration Debug
if ($LASTEXITCODE -ne 0) {
    throw "El preview no compilo (codigo $LASTEXITCODE)"
}

if (-not $NoLaunch) {
    $executable = Join-Path $repoRoot 'bin\Debug\net8.0-windows\win-x64\InterludeLauncher.exe'
    if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
        throw "No se encontro el preview compilado: $executable"
    }
    Write-Host 'Abriendo L2 Hamburgo Launcher...' -ForegroundColor Green
    $process = Start-Process -FilePath $executable -WorkingDirectory $repoRoot -PassThru
    $process.WaitForExit()
}
