namespace TeXShift.Tests.E2E.Models
{
    internal sealed class OutputFiles
    {
        public string Markdown { get; set; }
        public string OriginalXml { get; set; }
        public string ConvertedXml { get; set; }
        public string PageChangesXml { get; set; }
        public string FinalSelectionXml { get; set; }
        public string FinalXml { get; set; }
        public string FinalXmlFull { get; set; }
        public string Pdf { get; set; }
        public string Perf { get; set; }
        public string ReversedMarkdown { get; set; }
        public string SelectionXml { get; set; }
        public string RibbonCommand { get; set; }
        public string UiSnapshot { get; set; }
        public string ErrorDialog { get; set; }
    }
}
