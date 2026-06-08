"""Validate every IFC fixture under tests/fixtures/ with ifcopenshell.

Used by CI (.github/workflows/build.yml) to catch schema regressions in the
emitter. If no fixtures are present, exits with code 0 and a TODO message —
the fixture set is grown by hand by exporting from a known Revit selection.
"""
import os
import sys

try:
    import ifcopenshell
    import ifcopenshell.validate
except ImportError:
    print("ifcopenshell not installed — run `pip install ifcopenshell`")
    sys.exit(2)

FIXTURE_DIR = os.path.join(os.path.dirname(__file__), "fixtures")


def main() -> int:
    if not os.path.isdir(FIXTURE_DIR):
        print(f"no fixture dir at {FIXTURE_DIR} — nothing to validate")
        return 0

    fixtures = [f for f in os.listdir(FIXTURE_DIR) if f.endswith(".ifc")]
    if not fixtures:
        print("no .ifc fixtures present yet — see tests/fixtures/README.md")
        return 0

    failed = []
    for name in sorted(fixtures):
        path = os.path.join(FIXTURE_DIR, name)
        print(f"validating {name} …")
        try:
            model = ifcopenshell.open(path)
        except Exception as ex:
            print(f"  FAIL parse: {ex}")
            failed.append(name)
            continue

        projects = model.by_type("IfcProject")
        if len(projects) != 1:
            print(f"  FAIL: expected exactly 1 IfcProject, found {len(projects)}")
            failed.append(name)
            continue

        try:
            errors = ifcopenshell.validate.validate(model, json=False)
        except Exception as ex:
            print(f"  FAIL validator: {ex}")
            failed.append(name)
            continue

        if errors:
            print(f"  FAIL: {len(errors)} schema issue(s)")
            for err in errors[:5]:
                print(f"    {err}")
            failed.append(name)
        else:
            print(f"  ok ({len(model.by_type('IfcProduct'))} products)")

    if failed:
        print(f"\n{len(failed)} fixture(s) failed: {failed}")
        return 1
    print("\nall fixtures valid")
    return 0


if __name__ == "__main__":
    sys.exit(main())
