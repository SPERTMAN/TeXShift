using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TeXShift.AddIn.Localization;
using TeXShift.Core.Errors;
using TeXShift.Core.Logging;
using TeXShift.Core.Mermaid;

namespace TeXShift.AddIn.UI.WPF.ViewModels
{
    /// <summary>
    /// Renders a small fixed Mermaid sample with the selected theme through the shared Mermaid
    /// service, so the preview matches what conversion produces.
    /// Members must be called on the owning WPF dispatcher thread.
    /// </summary>
    public sealed class MermaidThemePreviewViewModel : ViewModelBase, IDisposable
    {
        private const string SampleDiagram =
            "flowchart LR\n" +
            "    A[Markdown] --> B{TeXShift}\n" +
            "    B -->|Math| C[OneNote]\n" +
            "    B -->|Mermaid| C\n";

        private readonly IMermaidService _mermaidService;
        private string _requestedTheme;
        private string _displayedTheme;
        private bool _isActive;
        private bool _isRendering;
        private bool _isDisposed;

        public MermaidThemePreviewViewModel(IMermaidService mermaidService)
        {
            _mermaidService = mermaidService ?? throw new ArgumentNullException(nameof(mermaidService));
        }

        private ImageSource _image;
        public ImageSource Image
        {
            get => _image;
            private set => SetProperty(ref _image, value);
        }

        private bool _isLoading;
        /// <summary>
        /// Gets whether a render is pending and no previous image is shown.
        /// </summary>
        public bool IsLoading
        {
            get => _isLoading;
            private set => SetProperty(ref _isLoading, value);
        }

        private string _statusText;
        public string StatusText
        {
            get => _statusText;
            private set => SetProperty(ref _statusText, value);
        }

        public string AccessibleName => UIResources.GetString("Settings_MermaidPreview_Name");

        /// <summary>
        /// Sets the theme to preview. Renders immediately once the preview is active.
        /// </summary>
        public void SetTheme(string theme)
        {
            _requestedTheme = theme;
            RenderIfNeeded();
        }

        /// <summary>
        /// Starts rendering when the preview first becomes visible, so the Mermaid engine
        /// is not initialized for users who never open the Mermaid settings.
        /// </summary>
        public void Activate()
        {
            _isActive = true;
            RenderIfNeeded();
        }

        /// <summary>
        /// Drops any pending result synchronously. Nothing here may depend on the dispatcher
        /// running again: the settings thread shuts it down as soon as the window closes.
        /// The shared Mermaid service finishes an in-flight request on its own STA thread.
        /// </summary>
        public void Dispose()
        {
            _isDisposed = true;
        }

        private void RenderIfNeeded()
        {
            if (!_isActive || _isRendering || _isDisposed || IsDisplayingRequestedTheme())
                return;

            _ = RenderLatestThemeAsync();
        }

        private bool IsDisplayingRequestedTheme()
        {
            return string.Equals(_displayedTheme, _requestedTheme, StringComparison.Ordinal);
        }

        /// <summary>
        /// Keeps at most one request in flight and renders only the latest theme afterwards,
        /// so rapid switching neither queues stale renders nor shows an outdated theme.
        /// </summary>
        private async Task RenderLatestThemeAsync()
        {
            _isRendering = true;
            if (Image == null)
            {
                StatusText = null;
                IsLoading = true;
            }

            while (!IsDisplayingRequestedTheme())
            {
                var theme = _requestedTheme;
                string errorText = null;
                ImageSource image = null;
                try
                {
                    // Resume on the dispatcher; the service never touches this thread.
                    var result = await _mermaidService.RenderToImageAsync(
                        SampleDiagram,
                        new MermaidRenderOptions { Theme = theme });

                    if (result != null && result.Success && !string.IsNullOrEmpty(result.Base64PngData))
                    {
                        image = DecodePng(result.Base64PngData);
                    }
                    else
                    {
                        RuntimeLog.Write($"Mermaid theme preview failed for '{theme}': {result?.ErrorMessage}");
                        errorText = UIResources.GetString("Settings_MermaidPreview_Failed");
                    }
                }
                catch (Exception ex)
                {
                    RuntimeLog.Write($"Mermaid theme preview failed for '{theme}': {ex}");
                    errorText = ex is TeXShiftException
                        ? ErrorMessages.GetUserFriendlyMessage(ex)
                        : UIResources.GetString("Settings_MermaidPreview_Failed");
                }

                if (_isDisposed)
                    return;

                // A newer theme was requested while rendering; render that one instead.
                if (!string.Equals(theme, _requestedTheme, StringComparison.Ordinal))
                    continue;

                Image = image;
                StatusText = errorText;
                _displayedTheme = theme;
            }

            IsLoading = false;
            _isRendering = false;
        }

        private static ImageSource DecodePng(string base64PngData)
        {
            var bitmap = new BitmapImage();
            using (var stream = new MemoryStream(Convert.FromBase64String(base64PngData)))
            {
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
            }

            bitmap.Freeze();
            return bitmap;
        }
    }
}
