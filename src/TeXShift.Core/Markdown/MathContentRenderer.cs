using System.Globalization;
using System.Threading.Tasks;
using System.Xml.Linq;
using TeXShift.Core.Configuration;
using TeXShift.Core.Math;
using TeXShift.Core.Utils;

namespace TeXShift.Core.Markdown
{
    public sealed class MathContentRenderer
    {
        private readonly IMathEngine _engine;
        private readonly MathSettings _settings;

        public MathContentRenderer(IMathEngine engine, MathSettings settings = null)
        {
            _engine = engine;
            _settings = settings ?? new MathSettings();
        }

        public async Task<string> RenderInlineAsync(string latex, bool displayMode)
        {
            if (_engine == null) return SourceText(latex, displayMode);
            var expression = await _engine.ParseAsync(latex, displayMode).ConfigureAwait(false);
            // OneNote merges a leading numeric run into preceding body text unless the math font
            // is established by a real run. The trailing anchor preserves inline layout.
            return "<span style='font-family:Cambria Math' lang='x-none'>\u200B</span>" +
                Wrap(OneNoteNativeMathRenderer.Render(expression, 11)) + "<span lang='x-none'>\u200B</span>";
        }

        public async Task<XElement> RenderDisplayAsync(string latex, XNamespace ns)
        {
            var oe = new XElement(ns + "OE", new XAttribute("alignment", "center"),
                new XAttribute("spaceBefore", "8.8"), new XAttribute("spaceAfter", "8.8"),
                new XAttribute("style", "font-family:'Cambria Math';font-size:" + _settings.DisplayFontSize.ToString("0.###", CultureInfo.InvariantCulture) + "pt"));
            if (_engine == null)
                oe.Add(new XElement(ns + "T", new XCData(SourceText(latex, true))));
            else
            {
                var expression = await _engine.ParseAsync(latex, true).ConfigureAwait(false);
                if (_settings.DisplayAsImage)
                {
                    var image = await _engine.RenderImageAsync(expression, _settings.DisplayFontSize).ConfigureAwait(false);
                    oe.Add(new XElement(ns + "Image", new XAttribute("format", "png"),
                        new XAttribute("alt", MathImageSource.Encode(latex)),
                        new XElement(ns + "Size", new XAttribute("width", image.WidthPt), new XAttribute("height", image.HeightPt)),
                        new XElement(ns + "Data", image.Base64Png)));
                }
                else
                    oe.Add(new XElement(ns + "T", new XCData(Wrap(OneNoteNativeMathRenderer.Render(expression, _settings.DisplayFontSize)))));
            }
            return oe;
        }

        private static string Wrap(string mathml) => "<!--[if mathML]>" + mathml + "<![endif]-->";
        private static string SourceText(string latex, bool display) =>
            (display ? "$$" : "$") + OneNoteHtmlTextEscaper.Escape(latex) + (display ? "$$" : "$");
    }
}
