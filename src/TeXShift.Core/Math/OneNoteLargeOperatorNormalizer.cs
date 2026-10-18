using System.Linq;
using System.Xml.Linq;

namespace TeXShift.Core.Math
{
    /// <summary>
    /// Gives adjacent large operators explicit operand rows for OneNote's MathML importer.
    /// </summary>
    internal static class OneNoteLargeOperatorNormalizer
    {
        private const string MathNamespace = "http://www.w3.org/1998/Math/MathML";
        internal const string LargeOperators = "∏∐∑∫∬∭∮∯∰∱∲∳⋀⋁⋂⋃⨀⨁⨂⨃⨄⨅⨆⨉⨊⨋⨌⨍⨎⨏⨐⨑⨒⨓⨔⨕⨖⨗⨘⨙⨚⨛⨜";

        internal static void Normalize(XElement math)
        {
            // Only inferred rows have a sequence of operands. Script, fraction, root-index,
            // and table-row children occupy fixed slots and must never be combined.
            foreach (var row in math.DescendantsAndSelf().Where(IsInferredRow).Reverse().ToArray())
            {
                var children = row.Elements().ToArray();
                for (int start = 0; start < children.Length - 1; start++)
                {
                    if (!IsLargeOperator(children[start]) || !IsLargeOperator(children[start + 1]))
                    {
                        continue;
                    }

                    int end = start + 1;
                    while (end + 1 < children.Length && IsLargeOperator(children[end + 1]))
                    {
                        end++;
                    }

                    // OneNote consumes the next element as an operator's operand. Preserve
                    // that boundary (including an existing explicit group), then nest the
                    // chain from right to left. Grouping only the operator leaves an empty
                    // operand slot; grouping the entire suffix swallows unrelated terms.
                    XElement operand = end + 1 < children.Length ? children[end + 1] : null;
                    for (int index = end; index > start; index--)
                    {
                        var op = children[index];
                        var group = new XElement(row.Name.Namespace + "mrow");
                        op.ReplaceWith(group);
                        group.Add(op);
                        if (operand != null)
                        {
                            operand.Remove();
                            group.Add(operand);
                        }
                        operand = group;
                    }

                    start = end;
                }
            }
        }

        private static bool IsInferredRow(XElement element)
        {
            if (!IsMathElement(element)) return false;
            switch (element.Name.LocalName)
            {
                case "math":
                case "mrow":
                case "mstyle":
                case "mtd":
                case "msqrt":
                case "menclose":
                case "mpadded":
                case "mphantom":
                case "merror":
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsLargeOperator(XElement element)
        {
            if (!IsMathElement(element)) return false;
            switch (element.Name.LocalName)
            {
                case "mo":
                    if ((string)element.Attribute("largeop") == "false") return false;
                    return (string)element.Attribute("largeop") == "true" ||
                           (element.Value.Length == 1 && LargeOperators.IndexOf(element.Value[0]) >= 0);
                case "msub":
                case "msup":
                case "msubsup":
                case "munder":
                case "mover":
                case "munderover":
                    var basis = element.Elements().FirstOrDefault();
                    return basis != null && IsLargeOperator(basis);
                default:
                    return false;
            }
        }

        private static bool IsMathElement(XElement element)
        {
            return element.Name.NamespaceName.Length == 0 || element.Name.NamespaceName == MathNamespace;
        }
    }
}
