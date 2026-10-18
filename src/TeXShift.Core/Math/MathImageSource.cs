using System;
using System.Text;

namespace TeXShift.Core.Math
{
    internal static class MathImageSource
    {
        private const string Prefix = "TeXShift:math:v1:";
        public static string Encode(string source) => Prefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(source));
        public static bool TryDecode(string alt, out string source)
        {
            source = null;
            if (alt == null || !alt.StartsWith(Prefix, StringComparison.Ordinal)) return false;
            try { source = Encoding.UTF8.GetString(Convert.FromBase64String(alt.Substring(Prefix.Length))); return true; }
            catch (FormatException) { return false; }
        }
    }
}
