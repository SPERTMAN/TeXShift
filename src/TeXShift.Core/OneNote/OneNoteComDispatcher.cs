using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using TeXShift.Core.Utils;
using OneNoteInterop = Microsoft.Office.Interop.OneNote;

namespace TeXShift.Core.OneNote
{
    // Named delegates keep embedded interop types out of generic arguments across assemblies.
    public delegate T OneNoteFunction<T>(OneNoteInterop.Application application);
    public delegate void OneNoteAction(OneNoteInterop.Application application);

    /// <summary>Runs OneNote COM calls on one STA with an explicitly marshaled application interface.</summary>
    public sealed class OneNoteComDispatcher : IDisposable
    {
        private static readonly Guid ApplicationIid = typeof(OneNoteInterop.Application).GUID;
        private readonly StaWorker _worker;
        private IntPtr _interfaceStream;
        private OneNoteInterop.Application _application;

        public OneNoteComDispatcher(OneNoteInterop.Application application)
        {
            if (application == null) throw new ArgumentNullException(nameof(application));

            var iid = ApplicationIid;
            var hr = CoMarshalInterThreadInterfaceInStream(
                ref iid,
                application,
                out _interfaceStream);
            Marshal.ThrowExceptionForHR(hr);
            _worker = new StaWorker("TeXShift_OneNote_COM_STA", ReleaseApplication);
        }

        /// <summary>Activates OneNote on the owned STA. The caller manages the OneNote window.</summary>
        public static OneNoteComDispatcher Connect()
        {
            var dispatcher = new OneNoteComDispatcher();
            try
            {
                dispatcher._worker.InvokeAsync(() =>
                {
                    var appType = Type.GetTypeFromProgID("OneNote.Application")
                        ?? throw new InvalidOperationException("OneNote.Application is not registered.");
                    dispatcher._application = (OneNoteInterop.Application)Activator.CreateInstance(appType);
                    return Task.FromResult(true);
                }).GetAwaiter().GetResult();
                return dispatcher;
            }
            catch
            {
                dispatcher.Dispose();
                throw;
            }
        }

        private OneNoteComDispatcher()
        {
            _worker = new StaWorker("TeXShift_OneNote_COM_STA", ReleaseApplication);
        }

        /// <summary>Runs a call on the STA. Return managed data; COM references must stay inside the callback.</summary>
        public Task<T> InvokeAsync<T>(OneNoteFunction<T> action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            return _worker.InvokeAsync(() => Task.FromResult(action(GetApplication())));
        }

        public Task InvokeAsync(OneNoteAction action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            return _worker.InvokeAsync(() =>
            {
                action(GetApplication());
                return Task.FromResult(true);
            });
        }

        public void Dispose()
        {
            _worker.Dispose();
        }

        private OneNoteInterop.Application GetApplication()
        {
            if (_application != null) return _application;

            var stream = _interfaceStream;
            _interfaceStream = IntPtr.Zero;
            if (stream == IntPtr.Zero)
                throw new InvalidOperationException("The OneNote COM interface stream is unavailable.");

            var iid = ApplicationIid;
            var hr = CoGetInterfaceAndReleaseStream(stream, ref iid, out var interfacePointer);
            Marshal.ThrowExceptionForHR(hr);
            try
            {
                _application = (OneNoteInterop.Application)Marshal.GetObjectForIUnknown(interfacePointer);
                return _application;
            }
            finally
            {
                Marshal.Release(interfacePointer);
            }
        }

        private void ReleaseApplication()
        {
            // Consume an unused marshal stream before releasing its application reference.
            if (_interfaceStream != IntPtr.Zero) GetApplication();
            if (_application != null && Marshal.IsComObject(_application))
            {
                Marshal.ReleaseComObject(_application);
                _application = null;
            }
        }

        [DllImport("ole32.dll")]
        private static extern int CoMarshalInterThreadInterfaceInStream(
            ref Guid riid,
            [MarshalAs(UnmanagedType.Interface)] object pUnk,
            out IntPtr ppStm);

        [DllImport("ole32.dll")]
        private static extern int CoGetInterfaceAndReleaseStream(
            IntPtr pStm,
            ref Guid riid,
            out IntPtr ppv);
    }
}
