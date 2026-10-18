using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TeXShift.Core.OneNote;

namespace TeXShift.Core.Tests
{
    [TestClass]
    public class OneNoteWriteSelectionTests
    {
        private static readonly XNamespace Ns = OneNoteXml.Namespace;

        [TestMethod]
        public void ReplacementGetsAnEmptyCaretWithoutChangingTextOrPreservedSibling()
        {
            var preserved = new XElement(Ns + "OE", new XAttribute("objectID", "keep"), Text("unchanged"));
            var replacement = new XElement(Ns + "OE", Text("<b>new</b>"));
            var outline = Outline(preserved, replacement);
            string before = preserved.ToString();
            Apply(outline);
            Apply(outline);
            Assert.AreEqual(before, preserved.ToString());
            Assert.AreEqual(2, replacement.Elements(Ns + "T").Count());
            Assert.AreEqual(string.Empty, replacement.Element(Ns + "T").Value);
            Assert.AreEqual("all", (string)replacement.Element(Ns + "T").Attribute("selected"));
            Assert.AreEqual("<b>new</b>", replacement.Elements(Ns + "T").Last().Value);
        }

        [TestMethod]
        public void TableCellReceivesCaretAndOldSelectionIsCleared()
        {
            var original = new XElement(Ns + "OE", new XAttribute("objectID", "keep"), Text("original"));
            original.Element(Ns + "T").SetAttributeValue("selected", "all");
            var cell = new XElement(Ns + "OE", Text("cell"));
            var table = new XElement(Ns + "OE", new XElement(Ns + "Table",
                new XElement(Ns + "Row", new XElement(Ns + "Cell", new XElement(Ns + "OEChildren", cell)))));
            var outline = Outline(original, table);
            Apply(outline);
            Assert.IsNull(original.Element(Ns + "T").Attribute("selected"));
            Assert.AreEqual("all", (string)cell.Element(Ns + "T").Attribute("selected"));
            Assert.AreEqual(1, outline.Descendants().Attributes("selected").Count());
        }

        [TestMethod]
        public void ImageSelectionDoesNotIntroduceAnInvalidTextSibling()
        {
            var image = new XElement(Ns + "Image", new XElement(Ns + "Data", "synthetic"));
            var paragraph = new XElement(Ns + "OE", image);
            Apply(Outline(paragraph));
            Assert.AreEqual("all", (string)image.Attribute("selected"));
            Assert.AreEqual(1, paragraph.Elements().Count());
        }

        [TestMethod]
        public void EmptyResultStillProvidesAValidEditingPosition()
        {
            var outline = Outline();
            Apply(outline);
            Assert.AreEqual(1, outline.Descendants(Ns + "OE").Count());
            Assert.AreEqual("all", (string)outline.Descendants(Ns + "T").Single().Attribute("selected"));
        }

        private static XElement Text(string value) => new XElement(Ns + "T", new XCData(value));
        private static XElement Outline(params XElement[] paragraphs) => new XElement(Ns + "Outline", new XElement(Ns + "OEChildren", paragraphs));
        private static void Apply(XElement outline)
        {
            var type = typeof(OneNotePageWriter).Assembly.GetType("TeXShift.Core.OneNote.OneNoteWriteSelection", true);
            type.GetMethod("PlaceCaret", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { new[] { outline } });
        }
    }
}
