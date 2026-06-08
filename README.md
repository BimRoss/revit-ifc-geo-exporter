# revit-ifc-geo-exporter

A small Revit add-in (C#, .NET Framework 4.8) that exports the **current selection** to a minimal **IFC4** file as **`IfcTriangulatedFaceSet`** geometry, wrapped in `IfcBuildingElementProxy`. Preserves Revit `UniqueId` as a stable IFC GUID and copies the source Category onto the proxy's object-type field.

Built for fast handoff to downstream viewers / web pipelines / clash workflows when you don't want the full weight (or quirks) of Autodesk's in-box IFC exporter.

## Why a v1 like this?

The official `Autodesk/revit-ifc` exporter is excellent for whole-model BIM handoff, but the AEC community routinely rolls smaller, geometry-only exports when they need:

- **Selection-scoped** exports for QTO, clash, or partial handoff.
- **Tessellated geometry** (mesh, not BRep) for web viewers, glTF/USD pipelines, three.js / xeokit / Speckle-style flows.
- **Predictable file size and stable GUIDs** for diffing across exports.

This v1 nails that minimum useful slice in one dependency-free assembly.

## Status — v0.1.0 (demo)

- Walks Revit `GeometryElement` → `Solid.Face.Triangulate()` and direct `Mesh` objects, recursing into `GeometryInstance`.
- Deduplicates vertices, drops degenerate triangles.
- Converts Revit feet → IFC meters.
- Emits hand-rolled IFC4 STEP P21: `IfcProject` → `IfcSite` → `IfcBuilding` → `IfcBuildingStorey` → `IfcBuildingElementProxy`+, each with `IfcCartesianPointList3D` + `IfcTriangulatedFaceSet` under a `Body` representation.
- Encodes Revit `UniqueId` → 22-char IFC GUID (stable across exports).

### Known gaps (v1 is a demo)
- No `IfcMappedItem` reuse — instanced families re-tessellate per occurrence.
- No materials, colors, or property sets beyond Name/Category.
- No georeferencing (`IfcMapConversion`); world placement is identity.
- Single `Default Storey`; doesn't respect Revit levels.
- No linked-model transform application yet (single `Document` only).

## Build

Requires Visual Studio 2022 (or .NET SDK 6+ for command-line builds) and a local Revit install for the API DLLs. Default target is **Revit 2024**.

```powershell
cd src
dotnet build RevitIfcGeoExporter.sln -c Release
# To target another Revit version:
dotnet build RevitIfcGeoExporter.sln -c Release -p:RevitVersion=2025
# If Revit is installed somewhere non-standard:
dotnet build -c Release -p:RevitInstallDir="D:\Autodesk\Revit 2024"
```

Output: `src/RevitIfcGeoExporter/bin/Release/RevitIfcGeoExporter.dll` + `.addin` manifest.

## Install

1. Copy `RevitIfcGeoExporter.dll` and `RevitIfcGeoExporter.addin` to:
   ```
   %APPDATA%\Autodesk\Revit\Addins\<RevitVersion>\
   ```
2. Edit the `.addin` and make `<Assembly>` an absolute path to the DLL if you didn't put the DLL in the same folder.
3. Restart Revit. The command appears under **Add-Ins → External Tools → Export Selection to IFC**.

## Use

1. Select one or more elements in a 3D view.
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
    ├── ExportSelectionCommand.cs       # IExternalCommand entry point
    ├── GeometryWalker.cs               # Geometry walk + triangulation + dedupe
    ├── IfcWriter.cs                    # Minimal IFC4 STEP P21 emitter
    └── IfcGuid.cs                      # 128-bit GUID → 22-char IFC base64
```

## License

MIT — see `LICENSE`.
