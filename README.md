# Valheim Legacy World Conversion Diagnostic

A small BepInEx diagnostic plugin for tracking down legacy inventory records that make a Valheim world fail during the 1.0 world conversion process.

This was created after a vanilla dedicated-server world that loaded normally on the previous Valheim version failed in 1.0 with a stack trace ending in:

```text
System.NullReferenceException: Object reference not set to an instance of an object
at ItemDrop+ItemData.IsSameType
at Inventory.AddItem
at Inventory.AddTempItem
at Inventory.LoadOld
at ZDOMan.ConvertInventories

World load failed mid-file. Exiting without save. Check backups!
```

The plugin does **not** repair, skip, delete, or modify inventory data. It only adds diagnostic logging around the legacy inventory conversion path so you can identify the inventory and source ZDO that caused the exception.

## What it logs

When `ZDOMan.ConvertInventories()` fails, the plugin records information including:

- Source ZDO UID
- Source ZDO world coordinates
- Prefab hash
- Inventory dimensions and loaded item count
- Last item name and prefab hash being added
- Stack, durability, quality, variant and inventory slot
- `ItemData.IsSameType()` inputs
- Null `m_shared` data
- Recent items processed in the failing inventory

A useful failure report can look like:

```text
Inventory snapshot: type=Inventory, name="", size=4x4, loadedItemCount=6
Source ZDO candidate: type=ZDO, m_uid=1:183157,
  m_position=(-866.75, 31.46, -8632.98), m_prefab=328745978

Last Inventory.AddItem(string, ...) call:
Inventory.AddItem(name="Wood", stack=38, pos=4,1, ...)
```

In the world this was built for, the reported coordinates led to one disposable chest at an old Ashlands landing camp. The chest contained malformed legacy inventory slot data. Removing that chest on the previous Valheim version, saving the world, and attempting the 1.0 conversion again allowed the entire world to convert successfully.

## Important

**Work on a backup of your world.**

This plugin is intended for diagnostics. It does not make the conversion safe and does not guarantee that the reported ZDO is disposable.

If your world still loads on an older Valheim build, keep an untouched copy before changing anything.

## Requirements

- Valheim dedicated server
- BepInEx 5.x
- .NET SDK for building
- Reference assemblies from the same Valheim/BepInEx installation you will run the plugin against

The project currently targets `netstandard2.1`.

## Building on Windows

Create a `lib` directory beside the project file and copy these assemblies from your Valheim/BepInEx installation into it:

```text
BepInEx.dll
0Harmony.dll
UnityEngine.dll
UnityEngine.CoreModule.dll
```

Then run:

```powershell
.\build.ps1
```

A successful build produces:

```text
bin\Release\netstandard2.1\BabyGotBoarConvertDiag.dll
```

The `lib` DLLs are intentionally ignored by Git and should not be committed.

## Installing

Copy the built DLL into your BepInEx plugins directory. For `lloesche/valheim-server-docker` / community Valheim server Docker setups this is commonly exposed as:

```text
/config/bepinex/plugins/
```

Restart the server and allow the legacy world conversion to fail normally.

The diagnostic log is written to:

```text
/config/bepinex/BabyGotBoarConvertDiag.log
```

Look for:

```text
---------------- CONVERSION FAILURE REPORT ----------------
```

The `Source ZDO candidate` line is particularly useful because it can give you the exact world coordinates of the inventory that caused the converter to fail.

## Using the coordinates

If the old Valheim version can still load the world, enable developer commands on a client and travel to the reported X/Z coordinates. For example:

```text
goto -867 -8633
```

Inspect the object at that location. Do not destroy it unless you understand what it is and have a backup.

## Why this exists

A single malformed legacy inventory was enough to prevent a large, otherwise healthy vanilla world from converting to Valheim 1.0. The old version tolerated the inventory state, while the new converter crashed in `ItemData.IsSameType()`.

This tool makes that failure visible instead of leaving you with only `World load failed mid-file` and hundreds of possible containers to search manually.

## Disclaimer

This is an unofficial diagnostic tool and is not affiliated with Iron Gate AB or Coffee Stain Publishing.
