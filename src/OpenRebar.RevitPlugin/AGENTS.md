# Revit plugin

Revit API types compile only under `#if REVIT_SDK`. This repository does not reference the Revit SDK, so the checked-in build is the stub.

Ship two builds by the process runtime, not by the Revit year:

- `RevitNet8` (`net8.0-windows`) when `Environment.Version.Major` is below 10.
- `RevitNet10` (`net10.0-windows`) when the major version is 10 or higher.

The manifests are `addin/RevitNet8/OpenRebar.addin` and `addin/RevitNet10/OpenRebar.addin`. One `.addin` file cannot choose the DLL. Shared code must still build for `net8.0`.
