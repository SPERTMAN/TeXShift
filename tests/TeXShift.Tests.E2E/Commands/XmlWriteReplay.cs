using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using TeXShift.Core.OneNote;
using OneNoteInterop = Microsoft.Office.Interop.OneNote;

namespace TeXShift.Tests.E2E.Commands
{
    internal static class XmlWriteReplay
    {
        public static async Task ExecuteAsync(TestPageManager manager, string pageId, string template, string output)
        {
            XNamespace ns = OneNoteXml.Namespace;
            string before = await ReadSelectionAsync(manager, pageId).ConfigureAwait(false);
            var page = XDocument.Parse(before).Root;
            var outline = page.Elements(ns + "Outline").FirstOrDefault(element => element.Attribute("selected") != null)
                ?? page.Element(ns + "Outline")
                ?? throw new InvalidOperationException("The test page has no outline.");
            var paragraphs = outline.Descendants(ns + "OE").ToList();
            string xml = template.Replace("{{pageId}}", pageId)
                .Replace("{{outlineId}}", (string)outline.Attribute("objectID"));
            xml = Regex.Replace(xml, @"\{\{oe:(\d+)\}\}", match =>
            {
                int index = int.Parse(match.Groups[1].Value);
                if (index >= paragraphs.Count) throw new ArgumentException("Paragraph token is outside the source outline: " + match.Value);
                return (string)paragraphs[index].Attribute("objectID");
            });
            var changes = XElement.Parse(xml);
            if (changes.Name != ns + "Page" || (string)changes.Attribute("ID") != pageId)
                throw new ArgumentException("Changes must target the temporary page using {{pageId}}.");
            if (xml.Contains("{{")) throw new ArgumentException("Unresolved token in changes XML.");

            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
            File.WriteAllText(Path.Combine(output, $"F02_Original_XML_{stamp}.xml"), before);
            File.WriteAllText(Path.Combine(output, $"F03_Page_Changes_{stamp}.xml"), xml);
            await manager.OneNoteDispatcher.InvokeAsync(app =>
                app.UpdatePageContent(xml, DateTime.MinValue, OneNoteInterop.XMLSchema.xs2013, true)).ConfigureAwait(false);
            File.WriteAllText(Path.Combine(output, $"F04_Final_Page_XML_Basic_{stamp}.xml"),
                await manager.GetPageContentBasicXmlAsync(pageId).ConfigureAwait(false));
            File.WriteAllText(Path.Combine(output, $"F08_Final_Selection_{stamp}.xml"),
                await ReadSelectionAsync(manager, pageId).ConfigureAwait(false));
        }

        private static Task<string> ReadSelectionAsync(TestPageManager manager, string pageId) =>
            manager.OneNoteDispatcher.InvokeAsync(app =>
            {
                app.GetPageContent(pageId, out string xml, OneNoteInterop.PageInfo.piSelection, OneNoteInterop.XMLSchema.xs2013);
                return xml;
            });
    }
}
