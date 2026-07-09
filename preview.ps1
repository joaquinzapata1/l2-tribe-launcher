[CmdletBinding()]
param(
    [switch]$NoLaunch
)

$ErrorActionPreference = 'Stop'
$repoRoot = $PSScriptRoot
$buildScript = Join-Path $repoRoot 'build.ps1'
$previewOutputDir = Join-Path $repoRoot 'build\preview-win-x64'
$publishedExecutable = Join-Path $previewOutputDir 'L2TribeLauncher.exe'
$trustedPreviewDir = Join-Path $env:LOCALAPPDATA 'L2TribeLauncher\preview'
$executable = Join-Path $trustedPreviewDir 'L2TribeLauncher.exe'

Write-Host 'Publicando preview single-file del launcher...' -ForegroundColor Cyan
& $buildScript -OutputDir $previewOutputDir
New-Item -ItemType Directory -Path $trustedPreviewDir -Force | Out-Null
$existingPreviewProcesses = @(
    Get-CimInstance Win32_Process -Filter "Name='L2TribeLauncher.exe'" |
        Where-Object { $_.ExecutablePath -eq $executable }
)
foreach ($process in $existingPreviewProcesses) {
    Stop-Process -Id $process.ProcessId -Force -ErrorAction SilentlyContinue
}
foreach ($process in $existingPreviewProcesses) {
    Wait-Process -Id $process.ProcessId -Timeout 5 -ErrorAction SilentlyContinue
}
for ($attempt = 1; $attempt -le 20; $attempt++) {
    try {
        Copy-Item -LiteralPath $publishedExecutable -Destination $executable -Force
        break
    }
    catch {
        if ($attempt -eq 20) {
            throw
        }
        Start-Sleep -Milliseconds 250
    }
}

if (-not $NoLaunch) {
    if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
        throw "No se encontro el preview compilado: $executable"
    }
    Write-Host 'Abriendo L2 Tribe Launcher...' -ForegroundColor Green
    # The single-file publish avoids loading L2TribeLauncher.dll, which can be
    # blocked by Windows Application Control on some machines. Running the
    # preview copy from LocalAppData also avoids repo/build path reputation hits.
    $process = Start-Process -FilePath $executable -WorkingDirectory $trustedPreviewDir -PassThru
    $process.WaitForExit()
}
