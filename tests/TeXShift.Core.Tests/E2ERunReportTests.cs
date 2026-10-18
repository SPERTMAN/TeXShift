using System;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TeXShift.Tests.E2E.Models;

namespace TeXShift.Core.Tests
{
    [TestClass]
    public class E2ERunReportTests
    {
        [TestMethod]
        public async Task CleanupFailureChangesSuccessAndDoesNotPreventLaterCleanup()
        {
            var run = new E2ERunReport("synthetic", true);
            bool disposed = false;
            Assert.IsFalse(await run.CleanupAsync("Restore", () => throw new InvalidOperationException("restore failed")));
            Assert.IsTrue(await run.CleanupAsync("Dispose", () => { disposed = true; return Task.CompletedTask; }));
            Assert.IsTrue(disposed);
            Assert.AreEqual(6, run.Finish(0));
            Assert.AreEqual("failed", run.CleanupStatus);
            Assert.AreEqual("failed", run.Steps[0].Status);
            StringAssert.Contains(run.Steps[0].Error, "restore failed");
            Assert.AreEqual("passed", run.Steps[1].Status);
        }

        [TestMethod]
        public async Task RetainingPagesStillRequiresSuccessfulSessionDisposal()
        {
            var run = new E2ERunReport("synthetic", false);
            run.Skip("DeleteTestResources", "retained");
            await run.CleanupAsync("Dispose", () => throw new TimeoutException("STA did not exit"));
            Assert.AreEqual(6, run.Finish(0));
            Assert.AreEqual("failed", run.CleanupStatus);
        }

        [TestMethod]
        public async Task CleanupFailurePreservesOriginalFailureCodeAndBothErrors()
        {
            var run = new E2ERunReport("synthetic", true);
            try { await run.StepAsync("Convert", () => throw new InvalidOperationException("conversion failed")); }
            catch (InvalidOperationException) { }
            await run.CleanupAsync("Delete", () => throw new InvalidOperationException("deletion failed"));
            Assert.AreEqual(2, run.Finish(2));
            StringAssert.Contains(run.Steps[0].Error, "conversion failed");
            StringAssert.Contains(run.Steps[1].Error, "deletion failed");
        }

        [TestMethod]
        public void IntentionalRetentionDoesNotFailTheRun()
        {
            var run = new E2ERunReport("synthetic", false);
            run.Skip("DeleteTestResources", "retained");
            Assert.AreEqual(0, run.Finish(0));
            Assert.AreEqual("retained", run.CleanupStatus);
            Assert.AreEqual("skipped", run.Steps[0].Status);
        }
    }
}
