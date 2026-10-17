using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace TeXShift.Core.Services.ForwardConversion
{
    /// <summary>Moves existing native equations through Markdown parsing as opaque inline content.</summary>
    internal sealed class NativeMathContent
    {
        private static readonly Regex Equation = new Regex(@"<!--\[if\s+mathML\]>.*?<!\[endif\]-->",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
        private readonly Dictionary<string, string> _equations = new Dictionary<string, string>();

        public string Protect(string text)
        {
            string prefix = "TEXSHIFTNATIVE" + Guid.NewGuid().ToString("N");
            return Equation.Replace(text, match =>
            {
                string token = prefix + _equations.Count + "END";
                _equations.Add(token, match.Value);
                return token;
            });
        }

        public void Restore(XElement outline)
        {
            var texts = outline.Descendants(outline.Name.Namespace + "T").ToList();
            foreach (var equation in _equations)
            {
                var targets = texts.Where(t => t.Value.Contains(equation.Key)).ToList();
                if (targets.Count != 1)
                    throw new InvalidOperationException("Native equation was lost or duplicated during conversion.");
                targets[0].ReplaceNodes(new XCData(targets[0].Value.Replace(equation.Key, equation.Value)));
            }
        }

        public static bool Contains(string text) => Equation.IsMatch(text ?? string.Empty);
    }
}
