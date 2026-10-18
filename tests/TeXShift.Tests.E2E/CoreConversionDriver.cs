using System;
using System.Threading.Tasks;
using TeXShift.Core.OneNote;
using TeXShift.Core.Services;

namespace TeXShift.Tests.E2E
{
    /// <summary>Runs the Core pipeline on the prepared OneNote page, using default settings.</summary>
    internal static class CoreConversionDriver
    {
        public static async Task InvokeAsync(string command, OneNoteComDispatcher dispatcher, string output)
        {
            using (var services = new ServiceContainer())
            {
                if (command == "reverse")
                {
                    var result = await services.CreateReverseConversionOrchestrator(dispatcher).ExecuteAsync(
                        new ReverseConversionOptions { WriteDebugFiles = true, OutputDirectory = output }).ConfigureAwait(false);
                    if (!result.Success)
                        throw result.Error ?? new InvalidOperationException(result.ReadResult?.ErrorMessage ?? "Reverse conversion failed.");
                }
                else
                {
                    var result = await services.CreateConversionOrchestrator(dispatcher).ExecuteAsync(
                        new ConversionOptions { WriteDebugFiles = true, OutputDirectory = output }).ConfigureAwait(false);
                    if (!result.Success)
                        throw result.Error ?? new InvalidOperationException(result.ReadResult?.ErrorMessage ?? "Conversion failed.");
                }
            }
        }
    }
}
