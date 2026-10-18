using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace TeXShift.Core.OneNote
{
    /// <summary>Moves the editor selection into the replacement submitted in the same update.</summary>
    internal static class OneNoteWriteSelection
    {
        private static readonly XNamespace Ns = OneNoteXml.Namespace;

        public static void PlaceCaret(IReadOnlyList<XElement> outlines)
        {
            foreach (var node in outlines.SelectMany(outline => outline.DescendantsAndSelf()))
                node.Attribute("selected")?.Remove();

            // The writer selects the existing outline before replacing its paragraphs.
            // Place the new editing position in generated content, ahead of preserved siblings.
            var paragraphs = outlines.SelectMany(outline => outline.Descendants(Ns + "OE")).ToList();
            var target = paragraphs.FirstOrDefault(oe => oe.Attribute("objectID") == null && IsEditable(oe))
                ?? paragraphs.FirstOrDefault(IsEditable);
            if (target == null)
            {
                var children = outlines[0].Element(Ns + "OEChildren");
                if (children == null)
                {
                    children = new XElement(Ns + "OEChildren");
                    outlines[0].Add(children);
                }
                target = new XElement(Ns + "OE", new XElement(Ns + "T", new XCData(string.Empty)));
                children.Add(target);
            }

            var text = target.Element(Ns + "T");
            if (text != null)
            {
                // An empty selected text run represents a caret, not a highlighted text range.
                if (text.Value.Length > 0)
                {
                    var caret = new XElement(Ns + "T", new XCData(string.Empty));
                    text.AddBeforeSelf(caret);
                    text = caret;
                }
                text.SetAttributeValue("selected", "all");
            }
            else
            {
                target.Elements().First(element => element.Name == Ns + "Image" || element.Name == Ns + "InsertedFile")
                    .SetAttributeValue("selected", "all");
            }
        }

        private static bool IsEditable(XElement oe) => oe.Element(Ns + "T") != null
            || oe.Element(Ns + "Image") != null || oe.Element(Ns + "InsertedFile") != null;
    }
}
