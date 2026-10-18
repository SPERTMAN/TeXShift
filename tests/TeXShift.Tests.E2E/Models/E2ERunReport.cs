using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace TeXShift.Tests.E2E.Models
{
    internal sealed class E2ERunReport
    {
        public const int CleanupFailureExitCode = 6;

        private readonly Stopwatch _timer = Stopwatch.StartNew();
        private bool _cleanupFailed;

        public E2ERunReport(string testName, bool cleanupRequested)
        {
            TestName = testName;
            CleanupRequested = cleanupRequested;
        }

        public DateTime StartedUtc { get; } = DateTime.UtcNow;
        public DateTime FinishedUtc { get; private set; }
        public string Operation { get; set; } = "convert";
        public string Driver { get; set; }
        public string TestName { get; }
        public string StartMode { get; set; } = "unavailable";
        public int? OneNotePid { get; set; }
        public string OriginalPageId { get; set; }
        public string PageId { get; set; }
        public bool CleanupRequested { get; }
        public string CleanupStatus { get; private set; }
        public int ExitCode { get; private set; }
        public long DurationMs => _timer.ElapsedMilliseconds;
        public List<E2EStep> Steps { get; } = new List<E2EStep>();

        public async Task StepAsync(string name, Func<Task> action)
        {
            var step = new E2EStep { Name = name, Status = "passed" };
            var timer = Stopwatch.StartNew();
            Steps.Add(step);
            try { await action().ConfigureAwait(false); }
            catch (Exception ex)
            {
                step.Status = "failed";
                step.Error = ex.ToString();
                throw;
            }
            finally { step.DurationMs = timer.ElapsedMilliseconds; }
        }

        public async Task<bool> CleanupAsync(string name, Func<Task> action)
        {
            try
            {
                await StepAsync(name, action).ConfigureAwait(false);
                return true;
            }
            catch (Exception ex)
            {
                _cleanupFailed = true;
                Console.Error.WriteLine($"E2E 收尾失败: {name}\n{ex}");
                return false;
            }
        }

        public void Skip(string name, string reason)
        {
            Steps.Add(new E2EStep { Name = name, Status = "skipped", Detail = reason });
        }

        public int Finish(int exitCode)
        {
            _timer.Stop();
            FinishedUtc = DateTime.UtcNow;
            CleanupStatus = _cleanupFailed ? "failed" : CleanupRequested ? "passed" : "retained";
            ExitCode = exitCode == 0 && _cleanupFailed
                ? CleanupFailureExitCode : exitCode;
            return ExitCode;
        }
    }

    internal sealed class E2EStep
    {
        public string Name { get; set; }
        public string Status { get; set; }
        public long DurationMs { get; set; }
        public string Detail { get; set; }
        public string Error { get; set; }
    }
}
