using System.Xml.Linq;

namespace TeXShift.Core.Math
{
    public sealed class MathExpression
    {
        private readonly XElement _mathml;
        public string Source { get; }
        public bool DisplayMode { get; }

        public MathExpression(string source, bool displayMode, XElement mathml)
        {
            Source = source;
            DisplayMode = displayMode;
            _mathml = new XElement(mathml);
        }

        public XElement CopyMathML() => new XElement(_mathml);
    }

    public sealed class MathImage
    {
        public string Base64Png { get; set; }
        public double WidthPt { get; set; }
        public double HeightPt { get; set; }
    }
}
