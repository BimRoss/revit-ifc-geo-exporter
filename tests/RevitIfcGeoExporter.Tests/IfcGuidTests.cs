using System;
using Xunit;

namespace BimRoss.RevitIfcGeoExporter.Tests
{
    public class IfcGuidTests
    {
        // Known reference vectors — round-trip is the authoritative test;
        // the literal values come from the encode/decode pair itself, so
        // these guard against regressions, not external-tool agreement.
        // For external-tool agreement, cross-check against IfcOpenShell's
        // ifcopenshell.guid.compress/expand or Autodesk.IFC.Common GUIDUtil.

        [Theory]
        [InlineData("00000000-0000-0000-0000-000000000000")]
        [InlineData("ffffffff-ffff-ffff-ffff-ffffffffffff")]
        [InlineData("12345678-1234-5678-1234-567812345678")]
        [InlineData("89abcdef-0123-4567-89ab-cdef01234567")]
        [InlineData("a1b2c3d4-e5f6-7890-abcd-ef1234567890")]
        [InlineData("01020304-0506-0708-090a-0b0c0d0e0f10")]
        [InlineData("deadbeef-cafe-babe-f00d-feedfacecafe")]
        public void RoundTrip_PreservesGuid(string s)
        {
            var g = Guid.Parse(s);
            var ifc = IfcGuid.ToIfcGuid(g);
            Assert.Equal(22, ifc.Length);
            var back = IfcGuid.FromIfcGuid(ifc);
            Assert.Equal(g, back);
        }

        [Fact]
        public void Encoding_IsExactly22Chars()
        {
            for (int i = 0; i < 256; i++)
            {
                var ifc = IfcGuid.ToIfcGuid(Guid.NewGuid());
                Assert.Equal(22, ifc.Length);
                foreach (var c in ifc)
                {
                    bool ok = (c >= '0' && c <= '9') || (c >= 'A' && c <= 'Z')
                              || (c >= 'a' && c <= 'z') || c == '_' || c == '$';
                    Assert.True(ok, $"Bad char '{c}' in {ifc}");
                }
            }
        }

        [Fact]
        public void LastByteAffectsEncoding()
        {
            // Regression for #8 — the previous encoder dropped byte 15, so any two
            // GUIDs differing only in byte 15 collided. Verify they no longer do.
            var a = new Guid("00000000-0000-0000-0000-000000000000");
            var b = new Guid("00000000-0000-0000-0000-0000000000ff");
            Assert.NotEqual(IfcGuid.ToIfcGuid(a), IfcGuid.ToIfcGuid(b));
        }

        [Fact]
        public void FromRevitUniqueId_ParsesGuidPrefix()
        {
            // Revit UniqueId is "<guid>-<8 hex episode>" — first 36 chars are the guid.
            var uid = "12345678-1234-5678-1234-567812345678-0000abcd";
            var ifc = IfcGuid.FromRevitUniqueId(uid);
            var expected = IfcGuid.ToIfcGuid(Guid.Parse("12345678-1234-5678-1234-567812345678"));
            Assert.Equal(expected, ifc);
        }

        [Fact]
        public void Composite_IsStable()
        {
            var a = IfcGuid.FromComposite("link-1", "elem-1");
            var b = IfcGuid.FromComposite("link-1", "elem-1");
            var c = IfcGuid.FromComposite("link-2", "elem-1");
            Assert.Equal(a, b);
            Assert.NotEqual(a, c);
            Assert.Equal(22, a.Length);
        }
    }
}
