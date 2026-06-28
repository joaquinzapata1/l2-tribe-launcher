# Interlude Launcher

Windows installer, updater and launcher for the complete Grand Crusade client.

## Features

- installs the complete client into an empty folder
- asks the player to choose the install folder on a new installation
- checks the latest `client-v*` GitHub Release
- downloads up to four immutable content packages concurrently
- installs a local `client-manifest.json` for development
- updates only files that changed between manifests
- repairs missing/corrupt managed files by SHA-256
- preserves mutable player configuration
- validates every package and extracted file
- backs up replaced/deleted client files
- rolls back if applying the patch fails
- allows an active check, download or installation to be cancelled safely
- records the installed patch version
- launches `system-e/l2.exe`

## Build

From the repository root:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-client-updater.ps1
```

Output:

```text
build/updater/win-x64/InterludeLauncher.exe
build/updater/win-x64/InterludeLauncher.exe.sha256
```

The release build is a self-contained single-file Windows executable. It does
not require .NET to be installed on the player's machine.

## Public distribution and signing

The generated development EXE is not signed. Before publishing it to players,
sign it with an RSA code-signing certificate issued by a trusted provider and
timestamp the signature. Smart App Control may block a new unsigned executable.

Microsoft guidance:

- <https://learn.microsoft.com/windows/apps/develop/smart-app-control/code-signing-for-smart-app-control>
- <https://learn.microsoft.com/dotnet/framework/tools/signtool-exe>

## Test mode

The full-client installer can be exercised without the UI:

```powershell
InterludeLauncher.exe `
  --install-content C:\path\client-manifest.json `
  --client C:\Games\Interlude `
  --repair `
  --log C:\path\launcher-test.log
```

The legacy patch installer remains available for development:

```powershell
InterludeLauncher.exe `
  --apply-package C:\path\patch-0.1.0.zip `
  --client C:\path\test-client `
  --sha256 <sha256> `
  --log C:\path\updater-test.log
```
