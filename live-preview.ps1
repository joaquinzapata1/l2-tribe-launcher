[CmdletBinding()]
param(
    [int]$PollMilliseconds = 350,
    [int]$DebounceMilliseconds = 450,
    [int]$MaxRefreshes = 0
)

$ErrorActionPreference = 'Stop'
$repoRoot = $PSScriptRoot
$createdNew = $false
$mutex = [System.Threading.Mutex]::new(
    $true,
    'Local\L2TribeLauncherLivePreview',
    [ref]$createdNew)
if (-not $createdNew) {
    $mutex.Dispose()
    throw 'Ya hay un Live Preview Launcher abierto. Usa la ventana existente.'
}
$buildScript = Join-Path $repoRoot 'build.ps1'
$previewOutputDir = Join-Path $repoRoot 'build\preview-win-x64'
$publishedExecutable = Join-Path $previewOutputDir 'L2TribeLauncher.exe'
$trustedPreviewDir = Join-Path $env:LOCALAPPDATA 'L2TribeLauncher\live-preview'
$executable = Join-Path $trustedPreviewDir 'L2TribeLauncher.exe'

$previewProcess = $null

function Get-SourceSnapshot {
    $files = @(
        Get-ChildItem -LiteralPath $repoRoot -File |
            Where-Object { $_.Extension -in @('.cs', '.csproj', '.manifest') }
        Get-ChildItem -LiteralPath (Join-Path $repoRoot 'Assets') -File -Recurse |
            Where-Object { $_.Extension -in @('.png', '.jpg', '.jpeg', '.ico') }
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
    try {
        & $buildScript -OutputDir $previewOutputDir
    }
    catch {
        Write-Host 'El build fallo. Corregi el error y guarda de nuevo.' -ForegroundColor Red
        Write-Host $_.Exception.Message -ForegroundColor Red
        return $false
    }
    New-Item -ItemType Directory -Path $trustedPreviewDir -Force | Out-Null
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
    if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
        throw "No se pudo preparar el preview compilado: $executable"
    }

    $script:previewProcess = Start-Process `
        -FilePath $executable `
        -WorkingDirectory $trustedPreviewDir `
        -PassThru
    Write-Host 'Preview actualizado. Guarda un archivo para refrescar.' -ForegroundColor Green
    return $true
}

try {
    $Host.UI.RawUI.WindowTitle = 'L2 Tribe Launcher - Live Preview'
    Get-CimInstance Win32_Process -Filter "Name='L2TribeLauncher.exe'" |
        Where-Object { $_.ExecutablePath -eq $executable } |
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
