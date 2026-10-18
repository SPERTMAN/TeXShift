using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TeXShift.Tests.E2E;

namespace TeXShift.Core.Tests
{
    [TestClass]
    public class RibbonCommandObservationTests
    {
        private static readonly DateTime Since = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private static string Entry(string id, string status, string page = "test-page", string command = "convert",
            string utc = "2026-01-01T00:00:01Z") =>
            $"[timestamp] <AddInCommand id=\"{id}\" command=\"{command}\" status=\"{status}\" pageId=\"{page}\" pid=\"42\" utc=\"{utc}\" />";

        [TestMethod]
        public void RequiresCurrentStartAndMatchingCompletion()
        {
            var observation = new RibbonCommandObservation("convert", "test-page", Since);
            observation.Accept(Entry("old", "started", utc: "2025-12-31T23:59:59Z"));
            observation.Accept(Entry("orphan", "succeeded"));
            observation.Accept(Entry("reverse", "started", command: "reverse"));
            observation.Accept("[timestamp] <AddInCommand id=\"partial");
            Assert.IsFalse(observation.Completed);
            Assert.IsNull(observation.ProcessId);
            observation.Accept(Entry("current", "started"));
            observation.Accept(Entry("another", "succeeded"));
            Assert.IsFalse(observation.Completed);
            observation.Accept(Entry("current", "succeeded"));
            Assert.IsTrue(observation.Completed);
            Assert.IsTrue(observation.Success);
            Assert.AreEqual(42, observation.ProcessId);
        }

        [TestMethod]
        public void RejectsSuccessForAnotherPageAndReportsFailure()
        {
            foreach (var status in new[] { "succeeded", "failed" })
            {
                var observation = new RibbonCommandObservation("convert", "test-page", Since);
                observation.Accept(Entry("current", "started"));
                observation.Accept(Entry("current", status, "other-page"));
                Assert.IsTrue(observation.Completed);
                Assert.IsFalse(observation.Success);
            }
        }

        [TestMethod]
        public void ReadingRotatedAndCurrentLogsTwiceKeepsTheOutcome()
        {
            var observation = new RibbonCommandObservation("convert", "test-page", Since);
            for (int pass = 0; pass < 2; pass++)
            {
                observation.Accept(Entry("current", "started"));
                observation.Accept(Entry("current", "succeeded"));
            }
            Assert.IsTrue(observation.Completed);
            Assert.IsTrue(observation.Success);
        }
    }
}
