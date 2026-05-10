# BambuDry Windows installer

[`BambuDry.iss`](BambuDry.iss) is the [Inno Setup 6](https://jrsoftware.org/isinfo.php)
script that builds the `BambuDry-<version>-Setup.exe` distributed on GitHub
Releases. The GitHub Actions workflow at
[`.github/workflows/build-windows.yml`](../../.github/workflows/build-windows.yml)
runs it on every push.

## What the installer does

- **Per-user install by default** — installs to
  `%LOCALAPPDATA%\Programs\BambuDry` so no admin prompt; user can elect per-machine
  in the wizard if they want.
- Ships a **self-contained** build of BambuDry — the .NET 8 runtime is bundled, so
  end users don't need to install .NET themselves.
- Creates a Start Menu shortcut, an optional desktop shortcut, and an
  optional `Run at login` shortcut (all toggleable on the wizard's Tasks page).
- Cleans up the `HKCU\…\Run\BambuDry` key on uninstall in case the user
  enabled "Launch at login" inside the app (the in-app toggle writes that
  key directly; the installer would otherwise leave it orphaned).

## Building locally

You'll only do this if you want to test installer changes without going
through CI.

```powershell
# 1. Install Inno Setup 6+ from https://jrsoftware.org/isinfo.php
#    Default install location: C:\Program Files (x86)\Inno Setup 6\

# 2. Publish the app (self-contained — bundles .NET runtime)
dotnet publish ..\src\BambuDry.App `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=false `
    -o publish

# 3. Compile the installer
& "C:\Program Files (x86)\Inno Setup 6\iscc.exe" BambuDry.iss

# Output: output\BambuDry-0.1.0-Setup.exe
```

To override the version embedded in the installer:

```powershell
& "C:\Program Files (x86)\Inno Setup 6\iscc.exe" /DMyAppVersion=0.2.0 BambuDry.iss
```

## Code signing

CI signs both `BambuDry.exe` and the installer via [Azure Trusted
Signing](https://learn.microsoft.com/azure/trusted-signing/). When the
required `AZURE_*` secrets aren't populated the workflow still builds
the installer but skips signing — so contributors without the cert can
still produce working binaries locally.
