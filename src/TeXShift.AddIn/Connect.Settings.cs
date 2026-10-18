using System;
using System.Windows.Forms;
using System.Windows.Interop;
using Microsoft.Office.Core;
using TeXShift.AddIn.Interop;
using TeXShift.AddIn.Localization;
using TeXShift.AddIn.UI.WPF;
using TeXShift.Core.Localization;

namespace TeXShift.AddIn
{
    /// <summary>
    /// Partial class containing settings dialog handling.
    /// </summary>
    public partial class Connect
    {
        #region Settings Button Handler

        /// <summary>
        /// Settings button click handler. Opens the settings dialog.
        /// </summary>
        public void OnSettingsButtonClick(IRibbonControl control)
        {
            try
            {
                if (TryShowInitializationError())
                {
                    return;
                }
                ShowWpfSettingsDialog();
            }
            catch (Exception ex)
            {
                ShowTopMostMessageBox(
                    string.Format(UIResources.GetString("Message_Error_OpenSettings"), ex.Message),
                    Resources.GetString("Dialog_ErrorTitle"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        #endregion

        #region Settings Dialogs

        /// <summary>
        /// Shows the WPF Material Design settings dialog.
        /// </summary>
        private void ShowWpfSettingsDialog()
        {
            var parentHwnd = NativeMethods.GetForegroundWindow();
            var mermaidService = _serviceContainer.MermaidService;
            Core.Configuration.AppSettings updatedSettings = null;
            bool dialogResult = false;
            bool resetToDefaultsRequested = false;
            Exception threadException = null;

            // WPF requires STA thread
            var thread = new System.Threading.Thread(() =>
            {
                try
                {
                    var dialog = new SettingsWindow(_appSettings, mermaidService);

                    // Set WPF window's Owner to OneNote window
                    var helper = new WindowInteropHelper(dialog);
                    helper.Owner = parentHwnd;

                    if (dialog.ShowDialog() == true)
                    {
                        updatedSettings = dialog.GetUpdatedSettings();
                        resetToDefaultsRequested = dialog.ResetToDefaultsRequested;
                        dialogResult = true;
                    }
                }
                catch (Exception ex)
                {
                    threadException = ex;
                }
                finally
                {
                    // Ensure WPF Dispatcher is closed correctly to release resources
                    var dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
                    dispatcher.InvokeShutdown();
                }
            });

            thread.SetApartmentState(System.Threading.ApartmentState.STA);
            thread.Start();
            thread.Join();

            // Force GC to clean up WPF resources
            GC.Collect();
            GC.WaitForPendingFinalizers();

            // Restore focus to OneNote window
            NativeMethods.SetForegroundWindow(parentHwnd);

            if (threadException != null)
                throw threadException;

            if (dialogResult && updatedSettings != null)
            {
                if (resetToDefaultsRequested)
                {
                    try
                    {
                        _settingsManager.ResetToDefaults();
                    }
                    catch (Exception ex)
                    {
                        ShowTopMostMessageBox(
                            string.Format(UIResources.GetString("Message_Error_ResetSettings"), ex.Message),
                            Resources.GetString("Dialog_ErrorTitle"),
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                        return;
                    }
                }

                _settingsManager.Save(updatedSettings);
                _appSettings = updatedSettings;
                LocalizationManager.Initialize(_appSettings?.Language);
                ApplySettingsToStyleConfig();

                // Refresh Ribbon to update button visibility
                _ribbon?.Invalidate();
            }
        }

        #endregion

        #region Settings Application

        /// <summary>
        /// Applies the current AppSettings to the OneNoteStyleConfig singleton.
        /// </summary>
        private void ApplySettingsToStyleConfig()
        {
            _serviceContainer?.ApplySettings(_appSettings ?? new Core.Configuration.AppSettings());
        }

        #endregion
    }
}
