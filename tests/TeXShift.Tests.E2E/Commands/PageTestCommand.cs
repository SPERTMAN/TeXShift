using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using TeXShift.Core.Services;
using TeXShift.Tests.E2E.Models;

namespace TeXShift.Tests.E2E.Commands
{
    internal static class PageTestCommand
    {
        public static async Task<int> RunAsync(PageTestOptions options)
        {
            FileInfo input = options.Input;
            FileInfo page = options.Page;
            DirectoryInfo output = options.Output;
            if (output == null)
            {
                return CommandHelpers.EmitArgumentError("输出目录不能为空。", null, null, 0);
            }

            bool hasMarkdown = !string.IsNullOrWhiteSpace(options.Markdown);
            int sourceCount = (input != null ? 1 : 0) + (hasMarkdown ? 1 : 0) + (page != null ? 1 : 0);
            if (sourceCount > 1)
            {
                return CommandHelpers.EmitArgumentError("请仅提供 --input、--markdown 或 --page 其一。", null, output, 0);
            }

            if (sourceCount == 0)
            {
                return CommandHelpers.EmitArgumentError("请提供 --input、--markdown 或 --page。", null, output, 0);
            }

            string mode = string.IsNullOrWhiteSpace(options.Mode)
                ? "com"
                : options.Mode.Trim().ToLowerInvariant();
            if (mode != "com" && mode != "ui")
            {
                return CommandHelpers.EmitArgumentError("--mode 只能是 com 或 ui。", null, output, 0);
            }

            bool hasCaret = !string.IsNullOrEmpty(options.Caret);
            bool hasSelect = options.Select != null && options.Select.Length > 0;
            bool hasOutline = !string.IsNullOrEmpty(options.Outline);
            if ((hasCaret ? 1 : 0) + (hasSelect ? 1 : 0) + (hasOutline ? 1 : 0) > 1)
            {
                return CommandHelpers.EmitArgumentError("请仅提供 --caret、--select 或 --outline 其一。", null, output, 0);
            }

            PageSelection selection = hasOutline ? PageSelection.Outline(options.Outline) : hasCaret
                ? PageSelection.Caret(options.Caret)
                : hasSelect
                    ? PageSelection.Paragraphs(options.Select[0], options.Select[options.Select.Length - 1])
                    : PageSelection.Default;

            string markdownContent = null;
            if (input != null)
            {
                if (!input.Exists)
                {
                    return CommandHelpers.EmitArgumentError($"Markdown 文件不存在: {input.FullName}", null, output, 0);
                }
                markdownContent = File.ReadAllText(input.FullName);
            }
            else if (page != null)
            {
                if (!page.Exists)
                {
                    return CommandHelpers.EmitArgumentError($"页面文件不存在: {page.FullName}", null, output, 0);
                }
            }
            else
            {
                markdownContent = options.Markdown;
            }

            FileInfo sourceFile = input ?? page;
            string changesTemplate = null;
            if (options.Changes != null)
            {
                if (!options.Changes.Exists)
                    return CommandHelpers.EmitArgumentError($"XML 文件不存在: {options.Changes.FullName}", null, output, 0);
                changesTemplate = File.ReadAllText(options.Changes.FullName);
            }
            string testName = sourceFile != null
                ? Path.GetFileNameWithoutExtension(sourceFile.Name)
                : $"inline_{DateTime.Now:yyyyMMdd_HHmmss}";

            if (!output.Exists)
            {
                output.Create();
            }

            var run = new E2ERunReport(mode, testName, options.Cleanup);
            run.Operation = changesTemplate == null ? "convert" : "write-xml";
            var cliResult = new CliResult { Mode = mode, TestName = testName, OutputDirectory = output.FullName };
            int exitCode = CommandHelpers.ExitSuccess;
            TestPageManager pageManager = null;
            ServiceContainer serviceContainer = null;
            IntPtr previousForegroundWindow = IntPtr.Zero;
            string pdfPath = null;

            try
            {
                await run.StepAsync("AttachOrLaunchOneNote", async () =>
                {
                    pageManager = await TestPageManager.CreateAsync().ConfigureAwait(false);
                    run.StartMode = pageManager.LaunchedOneNoteProcess ? "launched" : "attached";
                    run.OneNotePid = pageManager.OneNoteProcessId;
                }).ConfigureAwait(false);
                serviceContainer = new ServiceContainer();

                await run.StepAsync("SaveCurrentPage", async () =>
                {
                    await pageManager.SaveCurrentPageAsync().ConfigureAwait(false);
                    run.OriginalPageId = pageManager.OriginalPageId;
                }).ConfigureAwait(false);

                // Foreground mode includes page preparation and selection, not just conversion.
                if (mode == "ui")
                    await run.StepAsync("ActivateOneNote", () =>
                    {
                        previousForegroundWindow = TestPageManager.ActivateOneNoteWindow();
                        return Task.CompletedTask;
                    }).ConfigureAwait(false);

                await run.StepAsync("CreateTestPage", async () =>
                {
                    run.PageId = page != null
                        ? await pageManager.CreateTestPageFromFileAsync(page.FullName, selection).ConfigureAwait(false)
                        : await pageManager.CreateTestPageAsync(testName, markdownContent, selection).ConfigureAwait(false);
                }).ConfigureAwait(false);

                if (changesTemplate != null)
                {
                    await run.StepAsync("WriteXml", () => XmlWriteReplay.ExecuteAsync(
                        pageManager, run.PageId, changesTemplate, output.FullName)).ConfigureAwait(false);
                }
                else await run.StepAsync("Convert", async () =>
                {
                    var orchestrator = serviceContainer.CreateConversionOrchestrator(pageManager.OneNoteDispatcher);
                    var result = await orchestrator.ExecuteAsync(new ConversionOptions
                    {
                        WriteDebugFiles = true,
                        ExportPdf = true,
                        OutputDirectory = output.FullName
                    }).ConfigureAwait(false);
                    pdfPath = result?.PdfPath;
                    if (result == null || !result.Success)
                    {
                        exitCode = IsPdfExportFailure(result) ? CommandHelpers.ExitPdfFailed : CommandHelpers.ExitConversionFailed;
                        throw result?.Error ?? new InvalidOperationException(result?.ReadResult?.ErrorMessage ?? "转换失败。");
                    }
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (exitCode == CommandHelpers.ExitSuccess)
                    exitCode = ex is COMException ? CommandHelpers.ExitOneNoteUnavailable : CommandHelpers.ExitConversionFailed;
                cliResult.Error = ex.Message;
                CommandHelpers.EmitError("E2E 执行失败。", ex);
            }
            finally
            {
                if (pageManager != null)
                {
                    run.PageId = run.PageId ?? pageManager.TestPageId;
                    bool restored = await run.CleanupAsync("RestoreOriginalPage", pageManager.RestoreOriginalPageAsync).ConfigureAwait(false);
                    if (options.Cleanup && restored)
                        await run.CleanupAsync("DeleteTestResources", pageManager.CleanupTestResourcesAsync).ConfigureAwait(false);
                    else
                        run.Skip("DeleteTestResources", options.Cleanup ? "restore-failed" : "retained");
                }
                if (serviceContainer != null)
                    await run.CleanupAsync("DisposeServices", () => { serviceContainer.Dispose(); return Task.CompletedTask; }).ConfigureAwait(false);
                if (pageManager != null)
                    await run.CleanupAsync("DisposeOneNote", () => { pageManager.Dispose(); return Task.CompletedTask; }).ConfigureAwait(false);
                if (previousForegroundWindow != IntPtr.Zero)
                    await run.CleanupAsync("RestoreFocus", () => { TestPageManager.FocusWindow(previousForegroundWindow); return Task.CompletedTask; }).ConfigureAwait(false);
            }

            exitCode = run.Finish(exitCode);
            cliResult.Status = exitCode == CommandHelpers.ExitSuccess ? "success" : "failure";
            cliResult.CleanupStatus = run.CleanupStatus;
            cliResult.DurationMs = run.DurationMs;
            if (exitCode == CommandHelpers.ExitCleanupFailed)
                cliResult.Error = "转换完成，但 E2E 收尾失败。";
            cliResult.Files = CommandHelpers.CollectOutputFiles(output, run.StartedUtc, pdfPath);
            try { cliResult.DiagnosticsFile = CommandHelpers.WriteJsonFile(output, "F07_E2E_Run", run); }
            catch (Exception ex)
            {
                CommandHelpers.EmitError("写入 E2E 诊断文件失败。", ex);
                cliResult.Status = "failure";
                cliResult.Error = string.Join(" ", cliResult.Error, ex.Message).Trim();
                if (exitCode == CommandHelpers.ExitSuccess) exitCode = CommandHelpers.ExitDiagnosticsFailed;
            }
            CommandHelpers.EmitResult(cliResult);
            return exitCode;
        }

        private static bool IsPdfExportFailure(ConversionResult result)
        {
            return result?.Error is IOException ||
                result?.Error?.Message.IndexOf("PDF export", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
