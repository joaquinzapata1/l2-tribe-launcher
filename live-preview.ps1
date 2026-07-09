[CmdletBinding()]
param(
    [int]$PollMilliseconds = 350,
    [int]$DebounceMilliseconds = 450,
    [int]$MaxRefreshes = 0
)

$ErrorActionPreference = 'Stop'
$repoRoot = $PSScriptRoot
$project = Join-Path $repoRoot 'L2TribeLauncher.csproj'
$createdNew = $false
$mutex = [System.Threading.Mutex]::new(
    $true,
    'Local\L2TribeLauncherLivePreview',
    [ref]$createdNew)
if (-not $createdNew) {
    $mutex.Dispose()
    throw 'Ya hay un Live Preview Launcher abierto. Usa la ventana existente.'
}
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

$previewProcess = $null

function Get-SourceSnapshot {
    $files = @(
        Get-ChildItem -LiteralPath $repoRoot -File |
            Where-Object { $_.Extension -in @('.cs', '.csproj', '.manifest') }
        Get-ChildItem -LiteralPath (Join-Path $repoRoot 'Assets') -File -Recurse |
            Where-Object { $_.Extension -in @('.png', '.jpg', '.jpeg', '.ico', '.ttf', '.otf') }
    ) | Sort-Object FullName

    return ($files | ForEach-Object {
        "$($_.FullName)|$($_.Length)|$($_.LastWriteTimeUtc.Ticks)"
    }) -join "`n"
}

function Stop-Preview {
    if ($script:previewProcess -and -not $script:previewProcess.HasExited) {
        $script:previewProcess.CloseMainWindow() | Out-Null
        if (-not $script:previewProcess.WaitForExit(1500)) {
            Stop-Process -Id $script:previewProcess.Id -Force -ErrorAction SilentlyContinue
            $script:previewProcess.WaitForExit()
        }
    }
    $script:previewProcess = $null
}

function Build-And-Launch {
    param([string]$Reason)

    Stop-Preview
    Write-Host ''
    Write-Host "[$(Get-Date -Format 'HH:mm:ss')] $Reason" -ForegroundColor Cyan

    $script:previewProcess = Start-Process `
        -FilePath $dotnet `
        -ArgumentList @('run', '--project', $project, '--configuration', 'Release') `
        -WorkingDirectory $repoRoot `
        -PassThru
    Write-Host 'Preview actualizado en modo desarrollo. Guarda un archivo para refrescar.' -ForegroundColor Green
    return $true
}

try {
    $Host.UI.RawUI.WindowTitle = 'L2 Tribe Launcher - Live Preview'
    Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" |
        Where-Object { $_.CommandLine -like "*L2TribeLauncher.csproj*" } |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    if (-not (Build-And-Launch 'Iniciando live preview...')) {
        Write-Host 'Esperando el proximo guardado...' -ForegroundColor Yellow
    }
    $snapshot = Get-SourceSnapshot
    $refreshes = 0
    Write-Host 'Watcher activo. Cerra esta consola o presiona Ctrl+C para terminar.'

    while ($true) {
        Start-Sleep -Milliseconds $PollMilliseconds
        $current = Get-SourceSnapshot
        if ($current -eq $snapshot) {
            continue
        }

        Start-Sleep -Milliseconds $DebounceMilliseconds
        $snapshot = Get-SourceSnapshot
        Build-And-Launch 'Cambios detectados; recompilando...' | Out-Null
        $refreshes++
        if ($MaxRefreshes -gt 0 -and $refreshes -ge $MaxRefreshes) {
            break
        }
    }
}
finally {
    Stop-Preview
    $mutex.ReleaseMutex()
    $mutex.Dispose()
}
