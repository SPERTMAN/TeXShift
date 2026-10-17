using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using TeXShift.Core.Abstractions;
using TeXShift.Core.OneNote;
using TeXShift.Core.OneNoteMeta;
using TeXShift.Core.Utils;

namespace TeXShift.Core.Services.ForwardConversion
{
    /// <summary>
    /// Converts text runs within the original OneNote structure. Native objects and
    /// paragraphs outside the scope retain their identity and content.
    /// </summary>
    public sealed class ForwardContentConverter
    {
        private static readonly XNamespace Ns = OneNoteXml.Namespace;
        private readonly Func<double?, IMarkdownConverter> _createConverter;

        public ForwardContentConverter(Func<double?, IMarkdownConverter> createConverter)
        {
            _createConverter = createConverter ?? throw new ArgumentNullException(nameof(createConverter));
        }

        public async Task<IReadOnlyList<XElement>> ConvertAsync(ReadResult read)
        {
            var selected = new HashSet<string>(read.TargetObjectIds, StringComparer.Ordinal);
            bool all = read.Mode == DetectionMode.Cursor;
            var sources = all
                ? new[] { read.OriginalXmlNode }
                : read.OriginalXmlNodes.Select(n => n.Ancestors(Ns + "Outline").FirstOrDefault()).Distinct().ToArray();
            var result = new List<XElement>();
            foreach (var source in sources)
            {
                if (source == null || source.Name != Ns + "Outline")
                    throw new InvalidOperationException("Cannot locate source outline for conversion.");
                double width;
                double? sourceWidth = double.TryParse((string)source.Element(Ns + "Size")?.Attribute("width"),
                    System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out width)
                    ? width : read.SourceOutlineWidth;
                var converter = _createConverter(sourceWidth);
                XElement updated;
                if (all && source.Descendants(Ns + "OE").All(IsPlainText)
                    && !NativeMathContent.Contains(read.ExtractedText))
                {
                    // Keep the original Markdown document pipeline for plain text, including
                    // fenced blocks, tables, references, and exact reverse-conversion metadata.
                    updated = await converter.ConvertToOneNoteXmlAsync(read.ExtractedText).ConfigureAwait(false);
                    updated.SetAttributeValue("objectID", (string)source.Attribute("objectID"));
                    foreach (var layout in source.Elements().Where(e => e.Name == Ns + "Position" || e.Name == Ns + "Size").Reverse())
                        updated.AddFirst(new XElement(layout));
                }
                else
                {
                    updated = new XElement(source);
                    await ConvertChildrenAsync(updated.Element(Ns + "OEChildren"), selected, all, converter).ConfigureAwait(false);
                    // A partial or mixed conversion has no single faithful Markdown source.
                    // Reverse conversion must inspect the retained native content as well.
                    TeXShiftMetaWriter.RemoveExistingTeXShiftMeta(updated, Ns);
                }
                foreach (var node in updated.DescendantsAndSelf()) node.Attribute("selected")?.Remove();
                result.Add(updated);
            }
            return result;
        }

        private async Task ConvertChildrenAsync(XElement container, HashSet<string> selected, bool all, IMarkdownConverter converter)
        {
            if (container == null) return;
            var children = container.Elements(Ns + "OE").ToList();
            for (int i = 0; i < children.Count; i++)
            {
                var oe = children[i];
                bool convert = all || selected.Contains((string)oe.Attribute("objectID"));
                if (convert && IsPlainText(oe))
                {
                    var run = new List<XElement> { oe };
                    while (i + 1 < children.Count && IsPlainText(children[i + 1])
                        && (all || selected.Contains((string)children[i + 1].Attribute("objectID"))))
                        run.Add(children[++i]);
                    string text = string.Join("\n", run.Select(ReadText));
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        var replacement = await ConvertTextAsync(text, converter).ConfigureAwait(false);
                        oe.ReplaceWith(replacement);
                        foreach (var other in run.Skip(1)) other.Remove();
                    }
                    continue;
                }

                // Retain table/image/ink payloads. Table cells can contain independently
                // selected text, so visit their own paragraph containers in place.
                foreach (var cells in oe.Elements(Ns + "Table").SelectMany(t => t.Descendants(Ns + "Cell")))
                    await ConvertChildrenAsync(cells.Element(Ns + "OEChildren"), selected, all, converter).ConfigureAwait(false);
                await ConvertChildrenAsync(oe.Element(Ns + "OEChildren"), selected, all, converter).ConfigureAwait(false);
                if (!convert || !oe.Elements(Ns + "T").Any()) continue;
                string ownText = ReadText(oe);
                if (string.IsNullOrWhiteSpace(ownText)) continue;
                var converted = await ConvertTextAsync(ownText, converter).ConfigureAwait(false);
                if (converted.Count == 0) continue;

                // Original paragraph properties and list markers apply to its first result.
                // Generated Markdown list markers win when both describe the same paragraph.
                var first = converted[0];
                foreach (var attribute in oe.Attributes().Where(a => a.Name.LocalName != "objectID" && a.Name.LocalName != "selected"))
                    first.SetAttributeValue(attribute.Name, attribute.Value);
                foreach (var property in oe.Elements().Where(e => e.Name != Ns + "T" && e.Name != Ns + "OEChildren"))
                    if (!first.Elements(property.Name).Any()) first.AddFirst(new XElement(property));
                var nested = oe.Element(Ns + "OEChildren");
                if (nested != null)
                {
                    var last = converted.Last();
                    var existing = last.Element(Ns + "OEChildren");
                    if (existing == null) last.Add(new XElement(nested));
                    else existing.Add(nested.Elements().Select(e => new XElement(e)));
                }
                oe.ReplaceWith(converted);
            }
        }

        private static bool IsPlainText(XElement oe) => oe.Elements().All(e => e.Name == Ns + "T" || e.Name == Ns + "Meta");

        private static string ReadText(XElement oe)
        {
            return string.Concat(oe.Elements(Ns + "T").Select(t => t.Value));
        }

        private static string NormalizeText(string text)
        {
            text = OneNoteAutoLinkNormalizer.Normalize(text);
            text = Regex.Replace(text, @"<br\b[^>]*>\r?\n?", "\n", RegexOptions.IgnoreCase);
            return WebUtility.HtmlDecode(text);
        }

        private static async Task<List<XElement>> ConvertTextAsync(string text, IMarkdownConverter converter)
        {
            var native = new NativeMathContent();
            var outline = await converter.ConvertToOneNoteXmlAsync(NormalizeText(native.Protect(text))).ConfigureAwait(false);
            native.Restore(outline);
            return outline.Element(Ns + "OEChildren").Elements(Ns + "OE").Select(e => new XElement(e)).ToList();
        }
    }
}
