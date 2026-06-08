# IFC fixtures

CI calls `python tests/validate_fixtures.py` which validates every `.ifc` in this
directory against the IFC4 schema using `ifcopenshell.validate`.

To add a fixture:

1. Open a known Revit model.
2. Select a representative subset (a wall, a slab, a door, an instanced family,
   one element on each storey).
3. Run **Export Selection to IFC** → save into this directory as
   `sample_<short_description>.ifc`.
4. Commit. CI re-validates it on every PR.

Empty directory means "no fixtures yet" — CI passes with a TODO log line until
the first fixture lands.
