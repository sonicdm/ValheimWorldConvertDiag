ValheimWorldConvertDiag - Windows Docker host build notes

This revision fixes two issues from the first package:
1. Valheim 1.0's Unity assemblies require netstandard2.1.
2. BaseUnityPlugin ultimately needs UnityEngine.dll in addition to UnityEngine.CoreModule.dll.
3. build.ps1 now stops if dotnet build fails instead of printing a misleading "Built" path.

For Allan's bind mounts:
  H:\games\Valheim Server\Docker\data   -> /opt/valheim
  H:\games\Valheim Server\Docker\config -> /config

Put these four files into this project's lib folder:
  BepInEx.dll
  0Harmony.dll
  UnityEngine.dll
  UnityEngine.CoreModule.dll

Typical PowerShell discovery commands:

$Valheim = 'H:\games\Valheim Server\Docker\data'
Get-ChildItem $Valheim -Recurse -Filter BepInEx.dll -ErrorAction SilentlyContinue | Select-Object -Expand FullName
Get-ChildItem $Valheim -Recurse -Filter 0Harmony.dll -ErrorAction SilentlyContinue | Select-Object -Expand FullName
Get-ChildItem $Valheim -Recurse -Filter UnityEngine.dll -ErrorAction SilentlyContinue | Select-Object -Expand FullName
Get-ChildItem $Valheim -Recurse -Filter UnityEngine.CoreModule.dll -ErrorAction SilentlyContinue | Select-Object -Expand FullName

Copy the matching files into .\lib\, then run:
  .\build.ps1

Expected output:
  .\bin\Release\netstandard2.1\ValheimWorldConvertDiag.dll

Install to:
  H:\games\Valheim Server\Docker\config\bepinex\plugins\ValheimWorldConvertDiag.dll

V1.1 LOCATOR UPDATE
-------------------
This version also watches ZDO reads for the legacy "items" field. If Valheim's
converter accesses the inventory through the normal ZDO getter methods, the
failure report will include a "Source ZDO candidate" line with fields such as
m_uid, position, sector, and prefab hash. This is diagnostic only.
