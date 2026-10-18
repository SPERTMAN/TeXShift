using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using TeXShift.Core.Utils;

namespace TeXShift.Tests.E2E
{
    internal sealed class RibbonCommandObservation
    {
        private readonly string _command;
        private readonly string _pageId;
        private readonly DateTime _since;
        private readonly Dictionary<string, XElement> _events = new Dictionary<string, XElement>();
        private string _id;
        public int? ProcessId { get; private set; }
        public bool Completed { get; private set; }
        public bool Success { get; private set; }

        public RibbonCommandObservation(string command, string pageId, DateTime since)
        { _command = command; _pageId = pageId; _since = since; }

        public void ReadLogs()
        {
            foreach (var name in new[] { "runtime.old.log", "runtime.log" })
            {
                var path = Path.Combine(TeXShiftPaths.RuntimeLogFolder, name);
                try
                {
                    using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (var reader = new StreamReader(stream))
                    {
                        string line;
                        while ((line = reader.ReadLine()) != null) Accept(line);
                    }
                }
                catch (FileNotFoundException) { }
                catch (DirectoryNotFoundException) { }
            }
        }

        internal void Accept(string line)
        {
            var offset = line.IndexOf("<AddInCommand ", StringComparison.Ordinal);
            if (offset < 0) return;
            XElement entry;
            try { entry = XElement.Parse(line.Substring(offset)); }
            catch (System.Xml.XmlException) { return; } // The writer may still be appending this line.
            if ((string)entry.Attribute("command") != _command ||
                !DateTime.TryParse((string)entry.Attribute("utc"), CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var timestamp) || timestamp < _since) return;
            var id = (string)entry.Attribute("id");
            var status = (string)entry.Attribute("status");
            if (status == "started" && _id == null)
            {
                _id = id;
                ProcessId = (int?)entry.Attribute("pid");
            }
            if (id == null || id != _id) return;
            _events[status] = entry;
            if (status != "succeeded" && status != "failed") return;
            Completed = true;
            Success = status == "succeeded" && (string)entry.Attribute("pageId") == _pageId;
        }

        public void Save(string path) => new XElement("RibbonCommand",
            new XAttribute("expectedPageId", _pageId), new XAttribute("completed", Completed),
            new XAttribute("success", Success), _events.Values.ToArray()).Save(path);
    }
}
