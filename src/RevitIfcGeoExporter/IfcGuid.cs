using System;
using System.Text;

namespace BimRoss.RevitIfcGeoExporter
{
    /// <summary>
    /// Canonical buildingSMART IFC GUID compression — 16 bytes ⇄ 22 base64 chars.
    ///
    /// Encodes byte[0] in chars 0–1 (12-bit slot, top 4 bits unused), then five
    /// 3-byte groups in 4-char slots each: 1 + 5×3 = 16 bytes; 2 + 5×4 = 22 chars.
    /// </summary>
    internal static class IfcGuid
    {
        private static readonly char[] Base64Chars =
            "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz_$".ToCharArray();

        public static string FromRevitUniqueId(string uniqueId)
        {
            if (string.IsNullOrEmpty(uniqueId)) return ToIfcGuid(Guid.NewGuid());
            // Revit UniqueId is "<36-char guid>-<8-char episode hex>".
            var head = uniqueId.Length >= 36 ? uniqueId.Substring(0, 36) : uniqueId;
            return Guid.TryParse(head, out var g) ? ToIfcGuid(g) : ToIfcGuid(Guid.NewGuid());
        }

        public static string FromComposite(string a, string b)
        {
            // Deterministic GUID for federated (link, element) identity. Uses MD5
            // because we want a stable, dependency-free 128-bit digest — not a
            // cryptographic guarantee.
            using (var md5 = System.Security.Cryptography.MD5.Create())
            {
                var bytes = md5.ComputeHash(Encoding.UTF8.GetBytes(a + "|" + b));
                return ToIfcGuid(new Guid(bytes));
            }
        }

        public static string ToIfcGuid(Guid guid)
        {
            var le = guid.ToByteArray();
            // .NET's first three fields are little-endian on disk; IFC wants
            // big-endian byte order for the wire form.
            var b = new byte[]
            {
                le[3], le[2], le[1], le[0],
                le[5], le[4],
                le[7], le[6],
                le[8], le[9], le[10], le[11], le[12], le[13], le[14], le[15],
            };

            var sb = new StringBuilder(22);
            AppendBase64(sb, b[0], 2);
            for (int i = 1; i < 16; i += 3)
            {
                uint v = ((uint)b[i] << 16) | ((uint)b[i + 1] << 8) | b[i + 2];
                AppendBase64(sb, v, 4);
            }
            return sb.ToString();
        }

        public static Guid FromIfcGuid(string ifc)
        {
            if (ifc == null || ifc.Length != 22)
                throw new ArgumentException("IFC GUID must be exactly 22 chars.", nameof(ifc));

            var b = new byte[16];
            uint v = DecodeBlock(ifc, 0, 2);
            b[0] = (byte)v;
            for (int g = 0; g < 5; g++)
            {
                v = DecodeBlock(ifc, 2 + g * 4, 4);
                b[1 + g * 3] = (byte)((v >> 16) & 0xFF);
                b[2 + g * 3] = (byte)((v >> 8) & 0xFF);
                b[3 + g * 3] = (byte)(v & 0xFF);
            }

            var le = new byte[]
            {
                b[3], b[2], b[1], b[0],
                b[5], b[4],
                b[7], b[6],
                b[8], b[9], b[10], b[11], b[12], b[13], b[14], b[15],
            };
            return new Guid(le);
        }

        private static void AppendBase64(StringBuilder sb, uint value, int width)
        {
            var buf = new char[width];
            for (int i = width - 1; i >= 0; i--)
            {
                buf[i] = Base64Chars[value & 0x3F];
                value >>= 6;
            }
            sb.Append(buf);
        }

        private static uint DecodeBlock(string s, int start, int width)
        {
            uint v = 0;
            for (int i = 0; i < width; i++)
            {
                v = (v << 6) | CharToVal(s[start + i]);
            }
            return v;
        }

        private static uint CharToVal(char c)
        {
            if (c >= '0' && c <= '9') return (uint)(c - '0');
            if (c >= 'A' && c <= 'Z') return (uint)(10 + (c - 'A'));
            if (c >= 'a' && c <= 'z') return (uint)(36 + (c - 'a'));
            if (c == '_') return 62;
            if (c == '$') return 63;
            throw new ArgumentException("Invalid IFC GUID character: " + c);
        }
    }
}
