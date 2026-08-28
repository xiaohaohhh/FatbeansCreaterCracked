# FatbeansCreater local launcher

This repository contains the local launcher used by the V1.0.7 package. The
launcher loads `FatbeansCreater.core.exe` from its own directory, applies the
runtime changes during startup, and then invokes the original application
entry point in the same process.

## Repository layout

- `launcher/local_unlock_launcher.cs`: launcher source code.
- `launcher/FatbeansLocalUnlock.exe`: compiled x64 launcher.
- `launcher/FatbeansLocalUnlock.exe.config`: .NET Framework configuration.
- The complete V1.0.7 application package is attached to the GitHub Release.

## Package layout

Place the launcher beside `FatbeansCreater.core.exe`, `FatbeansCreater.exe.config`,
and the application dependency directories. The launcher uses the core file
as its default target. The original core file is kept separately as a recovery
copy in the local installation directory.

## Build

```powershell
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /target:winexe /platform:x64 /optimize+ /out:FatbeansLocalUnlock.exe local_unlock_launcher.cs
```

