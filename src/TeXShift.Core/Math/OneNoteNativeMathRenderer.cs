using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace TeXShift.Core.Math
{
    internal static class OneNoteNativeMathRenderer
    {
        public static string Render(MathExpression expression, double fontSizePt)
            => Render(expression, fontSizePt, false);

        public static string Render(MathExpression expression, double fontSizePt, bool forceRootSize)
        {
            var math = expression.CopyMathML();
            if ((expression.DisplayMode || forceRootSize) && math.Attribute("mathsize") == null)
                math.SetAttributeValue("mathsize", fontSizePt.ToString("0.###", CultureInfo.InvariantCulture) + "pt");
            foreach (var entry in MathStyleResolver.Resolve(math, fontSizePt, expression.DisplayMode))
                ApplyStyle(entry.Key, entry.Value);
            foreach (var attribute in math.DescendantsAndSelf().Attributes()
                .Where(a => a.Name.LocalName.StartsWith("data-mjx-", StringComparison.Ordinal)).ToArray())
                attribute.Remove();

            foreach (var node in math.Descendants().Reverse().ToArray())
            {
                var ns = node.Name.Namespace;
                switch (node.Name.LocalName)
                {
                    case "annotation":
                    case "annotation-xml": node.Remove(); break;
                    case "semantics": node.ReplaceWith(node.Nodes().ToArray()); break;
                    case "mspace":
                        node.ReplaceWith(new XElement(ns + "mo", Space((string)node.Attribute("width"))));
                        break;
                    case "mtext":
                        node.ReplaceWith(new XElement(ns + "mrow", node.Value.Select(c =>
                            char.IsLetter(c) ? new XElement(ns + "mi", new XAttribute("mathvariant", "normal"), c) :
                            new XElement(ns + (char.IsDigit(c) ? "mn" : "mo"), char.IsWhiteSpace(c) ? '\u00a0' : c))));
                        break;
                    case "mrow": ConvertFences(node); break;
                    case "mi":
                    case "mo":
                        // OneNote reparses multi-letter identifiers when updating a page.
                        if (!node.HasAttributes && node.Value.Length > 1 && node.Value.All(c => c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z'))
                            node.ReplaceWith(new XElement(ns + "mrow", node.Value.Select(c => new XElement(ns + "mi", c))));
                        break;
                }
            }

            foreach (var op in math.Descendants().Where(e => e.Name.LocalName == "mo").ToArray())
            {
                if (op.Value == "\u2061" && op.ElementsAfterSelf().FirstOrDefault()?.Value == "(")
                { op.Remove(); continue; }
                if (op.Value.Length == 1 && "()[]{}".Contains(op.Value))
                    op.SetAttributeValue("fence", "false");
                if (op.Value.Length == 1 && OneNoteLargeOperatorNormalizer.LargeOperators.Contains(op.Value)
                    && op.Attribute("stretchy") == null)
                    op.SetAttributeValue("stretchy", "false");
            }
            OneNoteLargeOperatorNormalizer.Normalize(math);

            // Single quotes avoid OneNote's conditional-comment attribute-quote artifacts.
            var buffer = new StringBuilder();
            using (var writer = new XmlTextWriter(new StringWriter(buffer, CultureInfo.InvariantCulture)) { QuoteChar = '\'' })
                math.WriteTo(writer);
            return buffer.ToString();
        }

        private static void ApplyStyle(XElement node, MathStyleResolver.Style style)
        {
            if (node.Attribute("mathsize") != null)
                node.SetAttributeValue("mathsize", style.FontSizePt.ToString("0.###", CultureInfo.InvariantCulture) + "pt");
            if (style.Display && style.ScriptLevel == 0) return;
            if (node.Name.LocalName == "mo" && node.Value.Length == 1
                && OneNoteLargeOperatorNormalizer.LargeOperators.Contains(node.Value) && node.Attribute("largeop") == null)
                node.SetAttributeValue("largeop", "false");

            // Office ignores movablelimits inherited from the operator dictionary.
            string kind = node.Name.LocalName;
            if (kind != "munder" && kind != "mover" && kind != "munderover") return;
            var basis = node.Elements().First();
            string movable = (string)basis.Attribute("movablelimits");
            bool movableByDefault = basis.Name.LocalName == "mo" && basis.Value.Length == 1
                && "∏∐∑⋀⋁⋂⋃⨀⨁⨂⨃⨄⨅⨆⨉".Contains(basis.Value);
            if (movable == "true" || movable == null && movableByDefault)
                node.Name = node.Name.Namespace + (kind == "munder" ? "msub" : kind == "mover" ? "msup" : "msubsup");
        }

        private static void ConvertFences(XElement row)
        {
            var children = row.Elements().ToArray();
            if (children.Length < 3) return;
            var first = children.First();
            var last = children.Last();
            if (first.Name.LocalName != "mo" || last.Name.LocalName != "mo") return;
            int pair = Array.IndexOf(new[] { "(", "[", "{", "|" }, first.Value);
            if (pair < 0 || (last.Value != new[] { ")", "]", "}", "|" }[pair] && last.Value != "")) return;
            if ((string)first.Attribute("stretchy") == "false" || (string)last.Attribute("stretchy") == "false") return;
            var tall = new[] { "mtable", "mfrac", "msqrt", "mroot", "munder", "mover", "munderover", "mfenced" };
            if (!children.Skip(1).Take(children.Length - 2).SelectMany(e => e.DescendantsAndSelf()).Any(e => tall.Contains(e.Name.LocalName))) return;
            row.ReplaceWith(new XElement(row.Name.Namespace + "mfenced", row.Attributes(), new XAttribute("open", first.Value),
                new XAttribute("close", last.Value), new XAttribute("separators", ""),
                new XElement(row.Name.Namespace + "mrow", children.Skip(1).Take(children.Length - 2))));
        }

        private static string Space(string width)
        {
            if (width == null || !width.EndsWith("em", StringComparison.Ordinal)
                || !double.TryParse(width.Substring(0, width.Length - 2), NumberStyles.Float, CultureInfo.InvariantCulture, out double em)
                || em <= 0) return "";
            var spaces = new StringBuilder();
            while (em >= 1) { spaces.Append('\u2003'); em -= 1; }
            if (em >= .8) spaces.Append('\u2003');
            else if (em >= .25) spaces.Append('\u2004');
            else if (em >= .2) spaces.Append('\u205f');
            else if (em > 0) spaces.Append('\u2009');
            return spaces.ToString();
        }
    }
}
