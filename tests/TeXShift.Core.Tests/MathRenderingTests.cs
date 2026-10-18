using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TeXShift.Core.Configuration;
using TeXShift.Core.Markdown;
using TeXShift.Core.Math;
using TeXShift.Core.OneNote;
using TeXShift.Core.OneNoteToMarkdown;

namespace TeXShift.Core.Tests
{
    [TestClass]
    public class MathRenderingTests
    {
        private static readonly XNamespace Ns = OneNoteXml.Namespace;
        private const string Mml = "http://www.w3.org/1998/Math/MathML";

        [TestMethod]
        public async Task DisplayEquationHasNoTextAnchorAndInlineEquationHasOne()
        {
            var renderer = new MathContentRenderer(new Engine("<mi>x</mi>"));
            var display = await renderer.RenderDisplayAsync("x", Ns);
            StringAssert.EndsWith(display.Element(Ns + "T").Value, "<![endif]-->");
            StringAssert.EndsWith(await renderer.RenderInlineAsync("x", false), "<span lang='x-none'>\u200B</span>");
        }

        [TestMethod]
        public async Task NativeAdaptationPreservesStandardTreeAndConvertsRelativeSizesWithinScope()
        {
            var engine = new Engine("<mstyle mathsize='2em'><mi>a</mi><mstyle mathsize='50%'><mi>b</mi></mstyle></mstyle><mi>c</mi>");
            var before = engine.Math.ToString();
            var renderer = new MathContentRenderer(engine, new MathSettings { DisplayFontSize = 12 });
            string result = (await renderer.RenderDisplayAsync("a+b", Ns)).Element(Ns + "T").Value;
            Assert.AreEqual("12pt", (string)ExtractMath(result).Attribute("mathsize"));
            StringAssert.Contains(result, "mathsize='24pt'");
            StringAssert.Contains(result, "mathsize='12pt'");
            Assert.AreEqual(before, engine.Math.ToString());
        }

        [TestMethod]
        public async Task TextStyleMovesDefaultLimitsAndRetainsExplicitLimits()
        {
            var renderer = new MathContentRenderer(new Engine(
                "<mstyle displaystyle='false'><munderover><mo>∑</mo><mi>i</mi><mi>n</mi></munderover>" +
                "<munderover><mo movablelimits='false'>∑</mo><mi>j</mi><mi>n</mi></munderover></mstyle>"));
            var math = ExtractMath((await renderer.RenderDisplayAsync("", Ns)).Element(Ns + "T").Value);
            Assert.AreEqual(1, math.Descendants(XName.Get("msubsup", Mml)).Count());
            Assert.AreEqual(1, math.Descendants(XName.Get("munderover", Mml)).Count());
            Assert.IsTrue(math.Descendants(XName.Get("mo", Mml)).All(e => (string)e.Attribute("stretchy") == "false"));
        }

        [TestMethod]
        public async Task NestedFencesPreserveOrdinaryCommasAndTextAttributes()
        {
            var renderer = new MathContentRenderer(new Engine(
                "<mrow><mo stretchy='true'>(</mo><mrow><mo>[</mo><mfrac><mi>a</mi><mi>b</mi></mfrac><mo>]</mo></mrow><mo>)</mo></mrow>" +
                "<mrow><mo>(</mo><mi>a</mi><mo>,</mo><mi>b</mi><mo>)</mo></mrow><mtext>A &amp; B</mtext>"));
            var math = ExtractMath((await renderer.RenderDisplayAsync("", Ns)).Element(Ns + "T").Value);
            Assert.AreEqual(2, math.Descendants(XName.Get("mfenced", Mml)).Count());
            Assert.IsTrue(math.Value.Contains("a,b"));
            Assert.IsTrue(math.Value.Contains("A\u00a0&\u00a0B"));
            Assert.IsFalse(math.Descendants(XName.Get("mtext", Mml)).Any());
        }

        [TestMethod]
        public async Task ImageRetainsSourceAfterSurroundingOutlineMetadataIsLost()
        {
            var engine = new Engine("<mi>x</mi>");
            var renderer = new MathContentRenderer(engine, new MathSettings { DisplayAsImage = true });
            const string latex = "\\scriptstyle x_{abc}";
            var oe = await renderer.RenderDisplayAsync(latex, Ns);
            Assert.IsNotNull(oe.Element(Ns + "Image"));
            var outline = new XElement(Ns + "Outline", new XElement(Ns + "OEChildren", oe));
            var result = await new OneNoteToMarkdownConverter(new OneNoteStyleConfig()).ConvertToMarkdownAsync(outline);
            StringAssert.Contains(result.Markdown, "$$\n" + latex + "\n$$");
            Assert.AreEqual(0, result.Warnings.Count);
            await renderer.RenderInlineAsync("x", false);
            Assert.AreEqual(1, engine.ImageCalls);
        }

        [TestMethod]
        public async Task CompactDisplayUsesInlineMathToAllowCentering()
        {
            var engine = new Engine("<mo>∑</mo><mi>x</mi>");
            var renderer = new MathContentRenderer(engine, new MathSettings
            {
                DisplayMode = MathDisplayMode.NativeCompact,
                DisplayFontSize = 14
            });

            var oe = await renderer.RenderDisplayAsync("\\sum x", Ns);
            var text = oe.Element(Ns + "T").Value;

            Assert.IsTrue(text.StartsWith("<span", StringComparison.Ordinal));
            StringAssert.Contains(text, "font-size:14pt");
            Assert.IsTrue(engine.LastDisplayMode == false);
        }

        [TestMethod]
        public void ExistingSettingsCanEnableImagesWithoutSpecifyingSize()
        {
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes("{\"Math\":{\"DisplayAsImage\":true}}")))
            {
                var settings = (AppSettings)new DataContractJsonSerializer(typeof(AppSettings)).ReadObject(stream);
                Assert.AreEqual(11.0, settings.Math.DisplayFontSize);
                Assert.AreEqual(MathDisplayMode.Image, settings.Math.GetDisplayMode());
            }
        }

        [TestMethod]
        public void DisplayModeSettingsRoundTripAsMutuallyExclusiveMode()
        {
            var settings = new AppSettings
            {
                Math = new MathSettings
                {
                    DisplayMode = MathDisplayMode.NativeCompact,
                    DisplayFontSize = 14
                }
            };

            using (var stream = new MemoryStream())
            {
                var serializer = new DataContractJsonSerializer(typeof(AppSettings));
                serializer.WriteObject(stream, settings);
                stream.Position = 0;
                var restored = (AppSettings)serializer.ReadObject(stream);

                Assert.AreEqual(MathDisplayMode.NativeCompact, restored.Math.GetDisplayMode());
                Assert.AreEqual(14.0, restored.Math.DisplayFontSize);
            }
        }

        private static XElement ExtractMath(string html) => XElement.Parse(html.Substring(16, html.IndexOf("<![endif]-->", StringComparison.Ordinal) - 16));

        private sealed class Engine : IMathEngine
        {
            public XElement Math { get; }
            public int ImageCalls { get; private set; }
            public bool? LastDisplayMode { get; private set; }
            public Engine(string content) { Math = XElement.Parse("<math xmlns='" + Mml + "'>" + content + "</math>"); }
            public Task<MathExpression> ParseAsync(string latex, bool displayMode)
            {
                LastDisplayMode = displayMode;
                return Task.FromResult(new MathExpression(latex, displayMode, Math));
            }
            public Task<MathImage> RenderImageAsync(MathExpression expression, double fontSizePt)
            {
                ImageCalls++;
                return Task.FromResult(new MathImage { Base64Png = "AA==", WidthPt = 20, HeightPt = 10 });
            }
            public void Dispose() { }
        }
    }
}
