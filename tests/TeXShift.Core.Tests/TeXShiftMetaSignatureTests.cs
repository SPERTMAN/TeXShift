using System;
using System.Reflection;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TeXShift.Core.Configuration;
using TeXShift.Core.OneNote;
using TeXShift.Core.OneNoteToMarkdown;

namespace TeXShift.Core.Tests
{
    [TestClass]
    public class TeXShiftMetaSignatureTests
    {
        private static readonly XNamespace OneNoteNamespace = OneNoteXml.Namespace;

        [TestMethod]
        [DataRow("<msup><mi>x</mi><mn>2</mn></msup>", "<msub><mi>x</mi><mn>2</mn></msub>")]
        [DataRow("<mfrac><mi>a</mi><mi>b</mi></mfrac>", "<mi>a</mi><mi>b</mi>")]
        [DataRow("<msup><mi>x</mi><mrow><mi>a</mi><mi>b</mi></mrow></msup>", "<msup><mrow><mi>x</mi><mi>a</mi></mrow><mi>b</mi></msup>")]
        public void FormulaStructureEditsInvalidateSource(string before, string after)
        {
            Assert.AreNotEqual(ComputeSignature(BuildOutline(before)), ComputeSignature(BuildOutline(after)));
        }

        [TestMethod]
        public void OneNoteNaryLimitPlacementDoesNotInvalidateSource()
        {
            const string children = "<mo>∑</mo><mi>i</mi><mi>n</mi>";
            Assert.AreEqual(ComputeSignature(BuildOutline("<munderover>" + children + "</munderover>")),
                ComputeSignature(BuildOutline("<msubsup>" + children + "</msubsup>")));
        }

        [TestMethod]
        public void SquareRootInferredRowMatchesExplicitRow()
        {
            Assert.AreEqual(ComputeSignature(BuildOutline("<msqrt><mi>a</mi><mi>b</mi></msqrt>")),
                ComputeSignature(BuildOutline("<msqrt><mrow><mi>a</mi><mi>b</mi></mrow></msqrt>")));
        }

        [TestMethod]
        public async System.Threading.Tasks.Task LegacySignatureStillRestoresSource()
        {
            var outline = BuildOutline("<msup><mi>x</mi><mn>2</mn></msup>");
            var writer = typeof(OneNoteToMarkdownConverter).Assembly.GetType("TeXShift.Core.OneNoteMeta.TeXShiftMetaWriter");
            var legacy = (string)writer.GetMethod("ComputeLegacySignature", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { outline });
            writer.GetMethod("WriteSourceMeta", new[] { typeof(XElement), typeof(string), typeof(string), typeof(string) })
                .Invoke(null, new object[] { outline, "$$x^{2}$$", "render", legacy });
            foreach (var meta in outline.Elements(OneNoteNamespace + "Meta"))
                if (((string)meta.Attribute("name")).EndsWith("sigVersion", StringComparison.Ordinal))
                    meta.SetAttributeValue("content", "1");
            var result = await new OneNoteToMarkdownConverter(new OneNoteStyleConfig()).ConvertToMarkdownAsync(outline);
            Assert.AreEqual("$$x^{2}$$", result.Markdown.Trim());
            Assert.AreEqual(0, result.Warnings.Count);
        }

        [TestMethod]
        [DataRow("{", "", "<mo>{</mo><mi>x</mi>")]
        [DataRow("", "|", "<mi>x</mi><mo>|</mo>")]
        public void OneSidedMfencedMatchesOneNoteExplicitFenceRewrite(
            string open,
            string close,
            string rewrittenContent)
        {
            var mfenced =
                $"<mfenced open=\"{open}\" close=\"{close}\"><mrow><mi>x</mi></mrow></mfenced>";

            var beforeOneNote = BuildOutline(mfenced);
            var afterOneNote = BuildOutline($"<mrow>{rewrittenContent}</mrow>");

            Assert.AreEqual(
                ComputeSignature(beforeOneNote),
                ComputeSignature(afterOneNote));
        }

        [TestMethod]
        public void MfencedWithoutAttributesUsesDefaultParentheses()
        {
            var beforeOneNote = BuildOutline("<mfenced><mi>x</mi></mfenced>");
            var afterOneNote = BuildOutline("<mrow><mo>(</mo><mi>x</mi><mo>)</mo></mrow>");

            Assert.AreEqual(
                ComputeSignature(beforeOneNote),
                ComputeSignature(afterOneNote));
        }

        private static XElement BuildOutline(string mathMlContent)
        {
            var richText =
                "<!--[if mathML]><math xmlns=\"http://www.w3.org/1998/Math/MathML\">" +
                mathMlContent +
                "</math><![endif]-->";

            return new XElement(
                OneNoteNamespace + "Outline",
                new XElement(
                    OneNoteNamespace + "OEChildren",
                    new XElement(
                        OneNoteNamespace + "OE",
                        new XElement(OneNoteNamespace + "T", new XCData(richText)))));
        }

        private static string ComputeSignature(XElement outline)
        {
            var assembly = typeof(OneNoteToMarkdownConverter).Assembly;
            var writerType = assembly.GetType(
                "TeXShift.Core.OneNoteMeta.TeXShiftMetaWriter",
                throwOnError: true);
            var method = writerType.GetMethod(
                "ComputeSignature",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.IsNotNull(method);
            return (string)method.Invoke(null, new object[] { outline });
        }
    }
}
