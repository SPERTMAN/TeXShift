using System;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace TeXShift.Core.Math
{
    internal static class MathStyleResolver
    {
        internal readonly struct Style
        {
            public double FontSizePt { get; }
            public bool Display { get; }
            public int ScriptLevel { get; }
            public Style(double size, bool display, int level) { FontSizePt = size; Display = display; ScriptLevel = level; }
        }

        public static IReadOnlyDictionary<XElement, Style> Resolve(XElement math, double fontSizePt, bool display)
        {
            var styles = new Dictionary<XElement, Style>();
            Visit(math, fontSizePt, display, 0, styles);
            return styles;
        }

        private static void Visit(XElement node, double size, bool display, int level, Dictionary<XElement, Style> styles)
        {
            if (bool.TryParse((string)node.Attribute("displaystyle"), out bool explicitDisplay)) display = explicitDisplay;
            string script = (string)node.Attribute("scriptlevel");
            if (int.TryParse(script, NumberStyles.Integer, CultureInfo.InvariantCulture, out int explicitLevel))
                level = script.StartsWith("+", StringComparison.Ordinal) || script.StartsWith("-", StringComparison.Ordinal)
                    ? level + explicitLevel : explicitLevel;

            var sizeAttribute = node.Attribute("mathsize");
            if (sizeAttribute != null)
            {
                size = ResolveSize(sizeAttribute.Value, size);
            }

            styles[node] = new Style(size, display, level);
            var children = node.Elements().ToArray();
            for (int i = 0; i < children.Length; i++)
            {
                bool childDisplay = display;
                int childLevel = level;
                switch (node.Name.LocalName)
                {
                    case "mfrac": childDisplay = false; if (!display) childLevel++; break;
                    case "msub": case "msup": case "msubsup": case "mmultiscripts":
                        if (i > 0) { childDisplay = false; childLevel++; } break;
                    case "mroot": if (i == 1) { childDisplay = false; childLevel += 2; } break;
                    case "munder": case "mover": case "munderover":
                        if (i > 0)
                        {
                            childDisplay = false;
                            string accent = node.Name.LocalName == "munder" || node.Name.LocalName == "munderover" && i == 1 ? "accentunder" : "accent";
                            if ((string)node.Attribute(accent) != "true") childLevel++;
                        }
                        break;
                }
                Visit(children[i], size, childDisplay, childLevel, styles);
            }
        }

        internal static double ResolveSize(string value, double inherited)
        {
            string[] units = { "pt", "em", "ex", "%", "px" };
            foreach (var unit in units)
                if (value.EndsWith(unit, StringComparison.Ordinal) && double.TryParse(value.Substring(0, value.Length - unit.Length),
                    NumberStyles.Float, CultureInfo.InvariantCulture, out double amount))
                    return amount * (unit == "pt" ? 1 : unit == "px" ? .75 : unit == "em" ? inherited : unit == "ex" ? inherited / 2 : inherited / 100);
            return value == "small" ? inherited * .8 : value == "big" ? inherited * 1.2 : inherited;
        }
    }
}
