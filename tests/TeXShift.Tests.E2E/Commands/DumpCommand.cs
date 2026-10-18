using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace TeXShift.Tests.E2E.Commands
{
    internal enum DumpFormat { Xml, One, Pdf }

    internal static class DumpCommand
    {
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        public static async Task<int> RunAsync(bool hierarchy, string pageId, FileInfo output, DumpFormat format)
        {
            bool hasPageId = !string.IsNullOrWhiteSpace(pageId);
            if (hierarchy == hasPageId)
            {
                Console.Error.WriteLine("Specify exactly one of --hierarchy or --page.");
                return CommandHelpers.ExitInvalidArguments;
            }
            if (format != DumpFormat.Xml && (hierarchy || output == null))
            {
                Console.Error.WriteLine("--format one/pdf requires --page and --output.");
                return CommandHelpers.ExitInvalidArguments;
            }

            TestPageManager pageManager = null;

            try
            {
                pageManager = await TestPageManager.CreateAsync().ConfigureAwait(false);
                if (format != DumpFormat.Xml)
                {
                    output.Directory?.Create();
                    var publishFormat = format == DumpFormat.One
                        ? Microsoft.Office.Interop.OneNote.PublishFormat.pfOneNote
                        : Microsoft.Office.Interop.OneNote.PublishFormat.pfPDF;
                    if (!await pageManager.ExportPageAsync(pageId, output.FullName, publishFormat).ConfigureAwait(false))
                        throw new IOException("Page export did not produce a file.");
                    return CommandHelpers.ExitSuccess;
                }
                string xml = hierarchy
                    ? await pageManager.GetHierarchyPagesXmlAsync().ConfigureAwait(false)
                    : await pageManager.GetPageContentBasicXmlAsync(pageId).ConfigureAwait(false);

                if (output == null)
                {
                    Console.Out.Write(xml);
                }
                else
                {
                    output.Directory?.Create();
                    File.WriteAllText(output.FullName, xml, Utf8NoBom);
                }

                return CommandHelpers.ExitSuccess;
            }
            catch (Exception ex)
            {
                CommandHelpers.EmitError($"Dump failed: {ex.Message}", ex);
                return CommandHelpers.ExitOneNoteUnavailable;
            }
            finally
            {
                if (pageManager != null)
                {
                    pageManager.CloseLaunchedOneNoteOnDispose();
                    pageManager.Dispose();
                }
            }
        }
    }
}
