# revit-ifc-geo-exporter

A small Revit add-in (C#, .NET Framework 4.8) that exports the **current selection** to a minimal **IFC4** file. v0.2.0 expands the export from a single-storey tessellated dump into a properly structured IFC with real element types, materials, property sets, georeferencing, family-instance reuse, and linked-model support.

Built for fast handoff to downstream viewers / web pipelines / clash workflows when you don't want the full weight (or quirks) of Autodesk's in-box IFC exporter.

## What v0.2.0 emits

- Spatial structure: `IfcProject` → `IfcSite` → `IfcBuilding` → **one `IfcBuildingStorey` per Revit Level** (#1).
- Per-element IFC type from a Revit Category → IFC mapping: `IfcWall`, `IfcSlab`, `IfcRoof`, `IfcColumn`, `IfcBeam`, `IfcDoor`, `IfcWindow`, `IfcStair`, `IfcRamp`, `IfcCovering`, `IfcRailing`, `IfcSpace`, `IfcDuctSegment`, `IfcPipeSegment`, `IfcLightFixture`, … — anything unmapped falls back to `IfcBuildingElementProxy` (#2).
- **Family-instance reuse** via `IfcRepresentationMap` + `IfcMappedItem` — a model with 500 identical chairs writes the geometry once, not 500× (#3). Mirrored instances fall back to flat tessellation.
- **Materials & colors** via `IfcStyledItem` + `IfcSurfaceStyleRendering` + `IfcColourRgb` (#4). Element-level dominant material; per-face material is a follow-up.
- **Property sets**: standard `Pset_<Type>Common` populated with Reference (Mark), Description, FireRating, plus a custom `Pset_BimRoss_RevitParameters` for shared parameters with values (#5). Identical (Pset, values) groups dedup into one `IfcPropertySet` shared via `IfcRelDefinesByProperties`.
- **Georeferencing** via `IfcMapConversion` + `IfcProjectedCRS` driven by Revit's project location (#6). CRS name defaults to "Local" — EPSG resolution is a follow-up.
- **Linked-model support** (#7): selection references that resolve to a `RevitLinkInstance` get their link transform composed in, get a `Pset_BimRoss_Source` property tagging the source link, and get a stable composite GUID so federated re-exports diff cleanly.
- Tessellated geometry as `IfcTriangulatedFaceSet` over `IfcCartesianPointList3D`.
- Stable 22-char IFC GUIDs from Revit `UniqueId`, now canonically encoded (#8 — full 16 bytes, round-trippable with IfcOpenShell).
- Vertex dedup via injective tuple key — no more hash collisions on distinct positions (#9).
- Configurable `ViewDetailLevel` + face triangulation tolerance through `ExportOptions` (#11). UI dialog persistence is a follow-up; defaults reproduce v0.1.0 behavior.

## Build

Requires Visual Studio 2022 (or .NET SDK 8+ on Windows for command-line builds) and a local Revit install for the API DLLs. Default target is **Revit 2024**.

```powershell
cd src
dotnet build RevitIfcGeoExporter.sln -c Release
# Retarget another Revit version:
dotnet build -c Release -p:RevitVersion=2025
# Non-standard install:
dotnet build -c Release -p:RevitInstallDir="D:\Autodesk\Revit 2024"
```

Output: `src/RevitIfcGeoExporter/bin/Release/RevitIfcGeoExporter.dll` + `.addin` manifest.

## Tests

The pure (Revit-free) parts have a standalone xUnit test project that builds on any OS:

```bash
cd tests/RevitIfcGeoExporter.Tests
dotnet test
```

`IfcGuid` is covered; the Revit-dependent code is exercised by the IFC-fixture validator instead.

## CI

`.github/workflows/build.yml` runs three jobs:

- **unit-tests** — Linux, .NET 8, runs the xUnit tests.
- **validate-fixtures** — Linux, Python 3.12, validates every `.ifc` under `tests/fixtures/` with `ifcopenshell.validate`.
- **build-addin** — Windows, matrix over Revit 2023/2024/2025, builds the add-in and uploads a per-version artifact.

Note: the `build-addin` job currently expects a Revit reference-assembly NuGet (e.g. `Nice3point.Revit.Api.RevitAPI`) to be wired into the csproj for the cloud builds to work — see #10 for the package-reference cutover. Until that lands, only the local build path works.

## Install

1. Copy `RevitIfcGeoExporter.dll` and `RevitIfcGeoExporter.addin` to:
   ```
   %APPDATA%\Autodesk\Revit\Addins\<RevitVersion>\
   ```
2. Edit the `.addin` and make `<Assembly>` an absolute path to the DLL if the DLL isn't sitting next to the manifest.
3. Restart Revit. The command appears under **Add-Ins → External Tools → Export Selection to IFC**.

## Use

1. Select one or more elements in any view. References inside linked models are picked up automatically.
2. Run **Export Selection to IFC** from Add-Ins → External Tools.
3. Choose a save location.
4. Open the resulting `.ifc` in any IFC viewer (Solibri, BIMcollab Zoom, BIMvision, usBIM.viewer, xeokit).

## Layout

```
src/
├── RevitIfcGeoExporter.sln
└── RevitIfcGeoExporter/
    ├── RevitIfcGeoExporter.csproj
    ├── RevitIfcGeoExporter.addin       # Revit add-in manifest
    ├── ExportSelectionCommand.cs       # IExternalCommand entry point + orchestrator
    ├── ExportOptions.cs                # User-tunable knobs
    ├── ExportTypes.cs                  # Pure DTOs consumed by IfcWriter
    ├── CategoryMap.cs                  # Revit Category → IFC entity + PredefinedType
    ├── GeometryWalker.cs               # Solid/Mesh walk → triangulation → meters
    ├── MaterialCache.cs                # Resolve & cache dominant material per element
    ├── SymbolCache.cs                  # Per-FamilySymbol mesh cache for IfcMappedItem reuse
    ├── PsetMapping.cs                  # Revit params → Pset_*Common + custom Psets
    ├── IfcWriter.cs                    # IFC4 STEP P21 emitter
    └── IfcGuid.cs                      # Canonical 128-bit GUID ⇄ 22-char IFC base64
tests/
├── RevitIfcGeoExporter.Tests/          # xUnit (links IfcGuid.cs)
├── validate_fixtures.py                # ifcopenshell-driven schema check
└── fixtures/                           # checked-in sample IFC files (grow over time)
.github/workflows/build.yml             # unit-tests + validate-fixtures + per-Revit build
```

## License

MIT — see `LICENSE`.
