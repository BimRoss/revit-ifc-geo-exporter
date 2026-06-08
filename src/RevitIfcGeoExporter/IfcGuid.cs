using System;

namespace BimRoss.RevitIfcGeoExporter
{
    /// <summary>
    /// Compresses a 128-bit GUID into the 22-character base64 form used by IFC
    /// (IfcGloballyUniqueId). Algorithm per buildingSMART / Autodesk reference.
    /// </summary>
    internal static class IfcGuid
    {
        private static readonly char[] Base64Chars =
            "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz_$".ToCharArray();

        public static string FromRevitUniqueId(string uniqueId)
        {
            // Revit UniqueId is "<guid>-<episodeId hex>". The leading 36-char GUID
            // is what we want — element identity is stable on it.
            if (string.IsNullOrEmpty(uniqueId)) return ToIfcGuid(Guid.NewGuid());
            var head = uniqueId.Length >= 36 ? uniqueId.Substring(0, 36) : uniqueId;
            if (Guid.TryParse(head, out var g)) return ToIfcGuid(g);
            return ToIfcGuid(Guid.NewGuid());
        }

        public static string ToIfcGuid(Guid guid)
        {
            var b = guid.ToByteArray();
            // .NET serializes the first three fields little-endian; IFC wants big-endian.
            var be = new byte[16];
            be[0] = b[3]; be[1] = b[2]; be[2] = b[1]; be[3] = b[0];
            be[4] = b[5]; be[5] = b[4];
            be[6] = b[7]; be[7] = b[6];
            for (int i = 8; i < 16; i++) be[i] = b[i];

            var num = new uint[6];
            num[0] = (uint)(be[0] >> 2);
            num[1] = (uint)(((be[0] & 0x3) << 16) | (be[1] << 8) | be[2]);
            num[2] = (uint)((be[3] << 16) | (be[4] << 8) | be[5]);
            num[3] = (uint)((be[6] << 16) | (be[7] << 8) | be[8]);
            num[4] = (uint)((be[9] << 16) | (be[10] << 8) | be[11]);
            num[5] = (uint)((be[12] << 16) | (be[13] << 8) | be[14]);
            // be[15] is folded by reducing the final group to 5 chars.

            var sb = new System.Text.StringBuilder(22);
            Append(sb, num[0], 2);
            Append(sb, num[1], 4);
            Append(sb, num[2], 4);
            Append(sb, num[3], 4);
            Append(sb, num[4], 4);
            Append(sb, num[5], 4);
            // pad to 22 with last byte if needed
            if (sb.Length < 22)
            {
                var last = (uint)be[15];
                Append(sb, last, 22 - sb.Length);
            }
            return sb.ToString();
        }

        private static void Append(System.Text.StringBuilder sb, uint value, int width)
        {
            var buf = new char[width];
            for (int i = width - 1; i >= 0; i--)
            {
                buf[i] = Base64Chars[value % 64];
                value /= 64;
            }
            sb.Append(buf);
        }
    }
}
