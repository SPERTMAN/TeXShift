using System;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using TeXShift.Core.Errors;
using TeXShift.Core.Localization;
using TeXShift.Core.Utils;

namespace TeXShift.Core.Math
{
    /// <summary>
    /// Converts LaTeX math expressions to MathML using WebView2 and MathJax.
    /// Uses a dedicated STA thread to ensure WebView2 compatibility.
    /// </summary>
    internal class MathService : IMathService
    {
        private WebView2 _webView;
        private bool _isInitialized;
        private volatile bool _isDisposed;
        private readonly object _lifecycle = new object();
        private Exception _initFailure;
        private readonly SemaphoreSlim _initLock = new SemaphoreSlim(1, 1);

        private StaWorker _worker;

        // Regex to add mml: namespace prefix to MathML elements
        private static readonly Regex MathMLElementRegex = new Regex(
            @"<(/?)(math|mi|mo|mn|ms|mtext|mspace|mglyph|maligngroup|malignmark|" +
            @"mrow|mfrac|msqrt|mroot|mstyle|merror|mpadded|mphantom|mfenced|menclose|" +
            @"msub|msup|msubsup|munder|mover|munderover|mmultiscripts|mtable|mlabeledtr|" +
            @"mtr|mtd|maction|semantics|annotation|annotation-xml)(\s|>|/>)",
            RegexOptions.Compiled);

        public bool IsInitialized => _isInitialized;

        public async Task InitializeAsync()
        {
            if (_isDisposed) throw new ObjectDisposedException(GetType().Name);
            if (_isInitialized) return;
            if (_initFailure != null) throw _initFailure;

            await _initLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_isInitialized) return;
                if (_initFailure != null) throw _initFailure;

                try
                {
                    StaWorker worker;
                    lock (_lifecycle)
                    {
                        if (_isDisposed) throw new ObjectDisposedException(GetType().Name);
                        worker = _worker = new StaWorker("TeXShift_Math_WebView2_STA", DisposeWebView);
                    }
                    await worker.InvokeAsync(async () =>
                    {
                        await InitializeWebView2Async();
                        return true;
                    }).ConfigureAwait(false);
                    lock (_lifecycle)
                    {
                        if (_isDisposed) throw new ObjectDisposedException(GetType().Name);
                        _isInitialized = true;
                    }
                }
                catch (Exception ex)
                {
                    // Cache the failure so subsequent calls fail fast without retrying
                    // expensive STA thread + WebView2 initialization.
                    if (ex is TeXShiftException)
                    {
                        _initFailure = ex;
                        throw;
                    }

                    _initFailure = new MathConversionException(
                        Resources.GetString("Error_MathInitFailed"),
                        ex.Message,
                        ex);
                    throw _initFailure;
                }
            }
            finally
            {
                _initLock.Release();
            }
        }

        private async Task InitializeWebView2Async()
        {
            _webView = new WebView2();
            _webView.Visible = false;

            var userDataFolder = Path.Combine(Path.GetTempPath(), "TeXShift_WebView2");
            Directory.CreateDirectory(userDataFolder);

            CoreWebView2Environment env;
            try
            {
                env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
                await _webView.EnsureCoreWebView2Async(env);
            }
            catch (WebView2RuntimeNotFoundException ex)
            {
                throw new MathConversionException(
                    Resources.GetString("Error_WebView2NotInstalled"),
                    "WebView2 Runtime not found on this system.",
                    ex);
            }

            // Find MathJax folder
            var mathjaxPath = FindMathJaxPath();
            if (string.IsNullOrEmpty(mathjaxPath))
            {
                throw new InvalidOperationException(
                    "MathJax not found. Expected at Lib/mathjax relative to assembly or project root.");
            }

            // Generate loader HTML with file:// URL to MathJax
            var mathjaxFileUrl = "file:///" + Path.Combine(mathjaxPath, "es5", "tex-mml-chtml.js").Replace('\\', '/');
            var html = GetMathJaxLoaderHtml().Replace(
                "https://mathjax.local/es5/tex-mml-chtml.js",
                mathjaxFileUrl);

            // Write loader HTML to temp folder and navigate via file://
            var loaderPath = Path.Combine(userDataFolder, "loader.html");
            File.WriteAllText(loaderPath, html);

            // Wait for navigation to complete
            var navTcs = new TaskCompletionSource<bool>();
            void OnNavigationCompleted(object s, CoreWebView2NavigationCompletedEventArgs e)
            {
                _webView.CoreWebView2.NavigationCompleted -= OnNavigationCompleted;
                if (e.IsSuccess)
                    navTcs.SetResult(true);
                else
                    navTcs.SetException(new Exception($"Navigation failed: {e.WebErrorStatus}"));
            }
            _webView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
            _webView.CoreWebView2.Navigate("file:///" + loaderPath.Replace('\\', '/'));
            await navTcs.Task;

            // Wait for MathJax to be ready
            await WaitForMathJaxReady();
        }

        public async Task<string> LatexToMathMLAsync(string latex, bool displayMode)
        {
            if (!_isInitialized)
            {
                throw new MathConversionException(
                    Resources.GetString("Error_MathInitFailed"),
                    "MathService not initialized. Call InitializeAsync first.");
            }

            if (string.IsNullOrWhiteSpace(latex))
            {
                return string.Empty;
            }

            try
            {
                // Execute on STA thread
                if (_isDisposed) throw new ObjectDisposedException(GetType().Name);
                return await _worker.InvokeAsync(() => ConvertLatexAsync(latex, displayMode)).ConfigureAwait(false);
            }
            catch (Exception ex) when (!(ex is TeXShiftException))
            {
                throw new MathConversionException(
                    Resources.GetString("Error_MathConversionFailed"),
                    ex.Message,
                    ex);
            }
        }

        private async Task<string> ConvertLatexAsync(string latex, bool displayMode)
        {
            // Escape LaTeX for JavaScript string
            var escapedLatex = EscapeForJavaScript(latex);
            var displayArg = displayMode ? "true" : "false";

            // Call MathJax.tex2mml() via WebView2
            var script = $"texToMml('{escapedLatex}', {displayArg})";
            var result = await _webView.CoreWebView2.ExecuteScriptAsync(script).ConfigureAwait(false);

            // Result is a JSON-encoded string, remove quotes
            var mathml = UnescapeJsonString(result);

            // Add mml: namespace prefix for OneNote compatibility
            mathml = AddMmlNamespacePrefix(mathml);

            return mathml;
        }

        public string WrapMathMLForOneNote(string mathml)
        {
            if (string.IsNullOrWhiteSpace(mathml))
            {
                return string.Empty;
            }

            return OneNoteMathMLAdapter.AdaptToOneNote(mathml);
        }

        /// <summary>
        /// Finds the MathJax folder path, checking multiple locations for dev/production environments.
        /// </summary>
        private string FindMathJaxPath()
        {
            var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            // Production: Lib folder next to DLL
            var prodPath = Path.Combine(assemblyDir, "Lib", "mathjax");
            if (Directory.Exists(prodPath))
            {
                return prodPath;
            }

            // Development: Walk up to find project root, then check known locations
            var dir = new DirectoryInfo(assemblyDir);
            while (dir != null)
            {
                // Check direct Lib folder (Add-in project structure)
                var devPath = Path.Combine(dir.FullName, "Lib", "mathjax");
                if (Directory.Exists(devPath))
                {
                    return devPath;
                }

                // Check src/TeXShift.AddIn/Lib (for E2E tests and other projects)
                var addInLibPath = Path.Combine(dir.FullName, "src", "TeXShift.AddIn", "Lib", "mathjax");
                if (Directory.Exists(addInLibPath))
                {
                    return addInLibPath;
                }

                dir = dir.Parent;
            }

            return null;
        }

        private string GetMathJaxLoaderHtml()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = "TeXShift.Core.Resources.Math.mathjax-loader.html";

            using (var stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream != null)
                {
                    using (var reader = new StreamReader(stream))
                    {
                        return reader.ReadToEnd();
                    }
                }
            }

            throw new InvalidOperationException(
                "MathJax loader HTML not found. Expected embedded resource 'TeXShift.Core.Resources.Math.mathjax-loader.html'.");
        }

        private async Task WaitForMathJaxReady()
        {
            var maxWaitMs = 30000; // 30 seconds timeout
            var intervalMs = 100;
            var elapsed = 0;

            while (elapsed < maxWaitMs)
            {
                var result = await _webView.CoreWebView2.ExecuteScriptAsync("isMathJaxReady()");
                if (result == "true")
                {
                    return;
                }

                await Task.Delay(intervalMs);
                elapsed += intervalMs;
            }

            throw new TimeoutException("MathJax failed to initialize within timeout period.");
        }

        private string AddMmlNamespacePrefix(string mathml)
        {
            // Add mml: prefix to all MathML elements
            var result = MathMLElementRegex.Replace(mathml, "<$1mml:$2$3");

            // Update xmlns to use mml: prefix
            result = result.Replace(
                "xmlns=\"http://www.w3.org/1998/Math/MathML\"",
                "xmlns:mml=\"http://www.w3.org/1998/Math/MathML\"");

            return result;
        }

        private string EscapeForJavaScript(string input)
        {
            return input
                .Replace("\\", "\\\\")
                .Replace("'", "\\'")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
        }

        private string UnescapeJsonString(string json)
        {
            if (string.IsNullOrEmpty(json) || json == "null")
            {
                return string.Empty;
            }

            // Remove surrounding quotes
            if (json.StartsWith("\"") && json.EndsWith("\""))
            {
                json = json.Substring(1, json.Length - 2);
            }

            // Unescape JSON string
            return Regex.Unescape(json);
        }

        public void Dispose()
        {
            StaWorker worker;
            lock (_lifecycle)
            {
                _isDisposed = true;
                _isInitialized = false;
                worker = _worker;
            }
            worker?.Dispose();
            // The semaphore can still have initialization waiters; let GC reclaim it.
        }

        private void DisposeWebView()
        {
            _webView?.Dispose();
            _webView = null;
        }

    }
}
