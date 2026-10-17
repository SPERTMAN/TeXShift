using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TeXShift.Core.OneNote;

namespace TeXShift.Core.Tests
{
    [TestClass]
    public class SelectionScopeTests
    {
        private static readonly XNamespace Ns = OneNoteXml.Namespace;

        [TestMethod]
        public void NestedChildDoesNotSelectTheWholeOutline()
        {
            var outline = Outline("a", Paragraph("parent", Paragraph("child")));
            Assert.IsFalse(Promote(outline.Descendants(Ns + "OE").Last()));
        }

        [TestMethod]
        public void RootSelectionIsTheOutlineScopeUsedByOneNote()
        {
            var outline = Outline("a", Paragraph("parent", Paragraph("child")));
            Assert.IsTrue(Promote(outline.Descendants(Ns + "OE").First()));
        }

        [TestMethod]
        public void CompleteNestedSelectionCanPromote()
        {
            var outline = Outline("a", Paragraph("parent", Paragraph("child")));
            Assert.IsTrue(Promote(outline.Descendants(Ns + "OE").ToArray()));
        }

        [TestMethod]
        public void RootCaretSelectionCanPromoteWithoutSelectingEveryDescendant()
        {
            var outline = Outline("a", Paragraph("parent", Paragraph("child")));
            Assert.IsTrue(Promote(outline.Descendants(Ns + "OE").First()));
        }

        [TestMethod]
        public void SelectionAcrossOutlinesCannotPromoteToTheFirst()
        {
            var a = Outline("a", Paragraph("first"));
            var b = Outline("b", Paragraph("second"));
            Assert.IsFalse(Promote(a.Descendants(Ns + "OE").Concat(b.Descendants(Ns + "OE")).ToArray()));
        }

        private static bool Promote(params XElement[] nodes)
        {
            var read = new ReadResult { IsSuccess = true, Mode = DetectionMode.Selection,
                OriginalXmlNode = nodes[0], OriginalXmlNodes = new List<XElement>(nodes) };
            var type = typeof(ReadResult).Assembly.GetType("TeXShift.Core.Services.ReverseConversion.ReverseSelectionPromoter", true);
            return (bool)type.GetMethod("TryPromoteFullOutlineSelectionToCursor", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { read, false, false, null });
        }

        private static XElement Outline(string id, XElement oe) =>
            new XElement(Ns + "Outline", new XAttribute("objectID", id), new XElement(Ns + "OEChildren", oe));

        private static XElement Paragraph(string text, XElement child = null) =>
            new XElement(Ns + "OE", new XAttribute("objectID", text), new XElement(Ns + "T", text),
                child == null ? null : new XElement(Ns + "OEChildren", child));
    }
}
