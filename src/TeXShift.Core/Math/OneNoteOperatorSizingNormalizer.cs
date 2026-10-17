using System.Linq;
using System.Xml.Linq;

namespace TeXShift.Core.Math
{
    /// <summary>Prevents Office from recursively growing nested n-ary operators.</summary>
    internal static class OneNoteOperatorSizingNormalizer
    {
        private static readonly string FixedSizeOperators = "∏∐∑⋀⋁⋂⋃⨀⨁⨂⨃⨄⨅⨆⨉";

        internal static string Normalize(string mathml)
        {
            var math = XElement.Parse(mathml, LoadOptions.PreserveWhitespace);
            bool changed = false;
            foreach (var op in math.Descendants().Where(e => IsMathElement(e) && e.Name.LocalName == "mo"))
            {
                // Office defaults sums and products to grow with their operand. Nested
                // operators then enlarge each other. Integrals keep their import workaround.
                if (op.Value.Length == 1 && FixedSizeOperators.Contains(op.Value)
                    && op.Attribute("stretchy") == null)
                {
                    op.SetAttributeValue("stretchy", "false");
                    changed = true;
                }
            }
            return changed ? math.ToString(SaveOptions.DisableFormatting) : mathml;
        }

        private static bool IsMathElement(XElement element)
        {
            return element.Name.NamespaceName.Length == 0
                || element.Name.NamespaceName == "http://www.w3.org/1998/Math/MathML";
        }
    }
}
