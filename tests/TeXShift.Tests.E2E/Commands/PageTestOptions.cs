using System.IO;

namespace TeXShift.Tests.E2E.Commands
{
    internal sealed class PageTestOptions
    {
        public FileInfo Input { get; set; }

        public string Markdown { get; set; }

        public FileInfo Page { get; set; }

        public FileInfo Changes { get; set; }

        public string Caret { get; set; }
        public string Outline { get; set; }

        public string[] Select { get; set; }

        public string Mode { get; set; } = "com";

        public DirectoryInfo Output { get; set; }

        public bool Cleanup { get; set; } = true;
    }
}
