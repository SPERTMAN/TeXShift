using System.Reflection;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TeXShift.Core.Math;

namespace TeXShift.Core.Tests
{
    [TestClass]
    public class OneNoteNativeMathRendererTests
    {
        private const string ConditionalCommentStart = "<!--[if mathML]>";

        [TestMethod]
        public void NestedSumsAndProductsDoNotGrowWithTheirOperands()
        {
            string result = AdaptToOneNote("<math><mo>∑</mo><mo>∑</mo><mo>∏</mo><mi>x</mi></math>");
            int end = result.IndexOf("<![endif]-->");
            var math = XElement.Parse(result.Substring(ConditionalCommentStart.Length, end - ConditionalCommentStart.Length));
            foreach (var op in math.Descendants("mo"))
                Assert.AreEqual("false", (string)op.Attribute("stretchy"));
        }

        [TestMethod]
        public void IntegralImportAndExplicitStretchingRemainSupported()
        {
            string result = AdaptToOneNote("<math><mo stretchy=\"false\">∫</mo><mi>x</mi><mo stretchy=\"true\">∑</mo><mi>y</mi></math>");
            StringAssert.Contains(result, "<mo stretchy='false'>∫</mo>");
            StringAssert.Contains(result, "<mo stretchy='true'>∑</mo>");
        }

        [TestMethod]
        [DataRow("mo", "∑")]
        [DataRow("munder", "∑")]
        [DataRow("mover", "∑")]
        [DataRow("munderover", "∑")]
        [DataRow("msub", "∏")]
        [DataRow("msup", "∐")]
        [DataRow("msubsup", "∫")]
        [DataRow("munderover", "⋃")]
        public void AdjacentLargeOperatorsRetainTheirScriptsAndOperandBoundary(string kind, string symbol)
        {
            string op = LargeOperator(kind, symbol);
            string input = $"<math>{op}{op}<mi>x</mi><mo>+</mo><mi>y</mi></math>";
            string expected = $"<math>{op}<mrow>{op}<mi>x</mi></mrow><mo>+</mo><mi>y</mi></math>";

            AssertMathEqual(expected, NormalizeOperators(input));
        }

        [TestMethod]
        public void ThreeMixedOperatorsNestFromRightToLeftAndPreserveExplicitOperand()
        {
            string sum = LargeOperator("munderover", "∑");
            string product = LargeOperator("munder", "∏");
            string integral = LargeOperator("msubsup", "∫");
            const string body = "<mrow><mi>x</mi><mo>+</mo><mi>y</mi></mrow>";
            string input = $"<math>{sum}{product}{integral}{body}<mo>=</mo><mi>z</mi></math>";
            string expected = $"<math>{sum}<mrow>{product}<mrow>{integral}{body}</mrow></mrow><mo>=</mo><mi>z</mi></math>";

            string normalized = NormalizeOperators(input);
            AssertMathEqual(expected, normalized);
            Assert.AreEqual(normalized, NormalizeOperators(normalized));
        }

        [TestMethod]
        public void IndependentChainsKeepTheSeparatingOperatorOutsideTheirOperands()
        {
            const string input = "<math><mo>∑</mo><mo>∑</mo><mi>x</mi><mo>+</mo><mo>∏</mo><mo>∏</mo><mi>y</mi></math>";
            const string expected = "<math><mo>∑</mo><mrow><mo>∑</mo><mi>x</mi></mrow><mo>+</mo><mo>∏</mo><mrow><mo>∏</mo><mi>y</mi></mrow></math>";
            AssertMathEqual(expected, NormalizeOperators(input));
        }

        [TestMethod]
        public void ExplicitLargeOperatorAttributesAndDefaultNamespaceArePreserved()
        {
            const string input = "<math xmlns='http://www.w3.org/1998/Math/MathML'><mo largeop='true'>★</mo><mo largeop='true'>★</mo><mi>x</mi></math>";
            const string expected = "<math xmlns='http://www.w3.org/1998/Math/MathML'><mo largeop='true'>★</mo><mrow><mo largeop='true'>★</mo><mi>x</mi></mrow></math>";
            AssertMathEqual(expected, NormalizeOperators(input));
        }

        [TestMethod]
        [DataRow("<mo>∑</mo>", "<munder><mo>∑</mo><mi>i</mi></munder>")]
        [DataRow("<mo>∑</mo>", "<mo>∑</mo>")]
        [DataRow("<munderover><mo>∑</mo><mi>i</mi><mi>N</mi></munderover>", "<munderover><mo>∑</mo><mi>j</mi><mi>M</mi></munderover>")]
        public void OperatorsWithoutOperandsDoNotGainSyntheticTokens(string first, string second)
        {
            AssertMathEqual($"<math>{first}<mrow>{second}</mrow></math>",
                NormalizeOperators($"<math>{first}{second}</math>"));
        }

        [TestMethod]
        [DataRow("math")]
        [DataRow("mrow")]
        [DataRow("mstyle")]
        [DataRow("mtd")]
        [DataRow("msqrt")]
        public void GroupsOperatorsWithinInferredRows(string container)
        {
            string input = $"<{container}><mo>∑</mo><mo>∑</mo><mi>x</mi></{container}>";
            AssertMathEqual($"<{container}><mo>∑</mo><mrow><mo>∑</mo><mi>x</mi></mrow></{container}>",
                NormalizeOperators(input));
        }

        [TestMethod]
        [DataRow("<math><mo>∑</mo><mi>x</mi></math>")]
        [DataRow("<math><mo>∑</mo><mi>x</mi><mo>+</mo><mo>∑</mo><mi>y</mi></math>")]
        [DataRow("<math><mo>∑</mo><mrow><mo>∑</mo><mi>x</mi></mrow></math>")]
        [DataRow("<math><mfrac><mo>∑</mo><mo>∑</mo></mfrac></math>")]
        [DataRow("<math><mroot><mo>∑</mo><mo>∑</mo></mroot></math>")]
        [DataRow("<math><msubsup><mi>x</mi><mo>∑</mo><mo>∑</mo></msubsup></math>")]
        [DataRow("<math><mo>∑</mo><mo largeop='false'>∫</mo><mi>x</mi></math>")]
        [DataRow("<math><munder><mi>lim</mi><mi>x</mi></munder><munder><mi>lim</mi><mi>y</mi></munder></math>")]
        public void UnrelatedOrAlreadyGroupedMathIsUnchanged(string input)
        {
            AssertMathEqual(input, NormalizeOperators(input));
        }

        [TestMethod]
        public void CompactFractionOperatorsRetainFixedArgumentSlots()
        {
            const string input = "<mml:math xmlns:mml='http://www.w3.org/1998/Math/MathML'><mml:mfrac><mml:mrow><mml:mo>&#x2211;</mml:mo><mml:mo>&#x2211;</mml:mo><mml:mi>x</mml:mi></mml:mrow><mml:mi>y</mml:mi></mml:mfrac></mml:math>";
            const string expected = "<mml:math xmlns:mml='http://www.w3.org/1998/Math/MathML' mathsize='11pt'><mml:mfrac><mml:mrow><mml:mo largeop='false' stretchy='false'>∑</mml:mo><mml:mo largeop='false' stretchy='false'>∑</mml:mo><mml:mi>x</mml:mi></mml:mrow><mml:mi>y</mml:mi></mml:mfrac></mml:math>";
            string adapted = AdaptToOneNote(input);
            int end = adapted.IndexOf("<![endif]-->");
            AssertMathEqual(expected, adapted.Substring(ConditionalCommentStart.Length, end - ConditionalCommentStart.Length));
        }

        private static string LargeOperator(string kind, string symbol)
        {
            string op = $"<mo>{symbol}</mo>";
            if (kind == "mo") return op;
            string scripts = kind == "msubsup" || kind == "munderover"
                ? "<mi>i</mi><mi>N</mi>"
                : "<mi>i</mi>";
            return $"<{kind}>{op}{scripts}</{kind}>";
        }

        private static void AssertMathEqual(string expected, string actual)
        {
            Assert.IsTrue(XNode.DeepEquals(XElement.Parse(expected), XElement.Parse(actual)), actual);
        }

        private static string NormalizeOperators(string mathMl)
        {
            var type = typeof(IMathEngine).Assembly.GetType("TeXShift.Core.Math.OneNoteLargeOperatorNormalizer", true);
            var method = type.GetMethod("Normalize", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(method);
            var math = XElement.Parse(mathMl);
            method.Invoke(null, new object[] { math });
            return math.ToString(SaveOptions.DisableFormatting);
        }

        private static string AdaptToOneNote(string mathMl)
        {
            var type = typeof(IMathEngine).Assembly.GetType("TeXShift.Core.Math.OneNoteNativeMathRenderer", true);
            var method = type.GetMethod("Render", BindingFlags.Public | BindingFlags.Static);
            var expression = new MathExpression("", true, XElement.Parse(mathMl));
            return ConditionalCommentStart + (string)method.Invoke(null, new object[] { expression, 11.0 }) + "<![endif]-->";
        }
    }
}
