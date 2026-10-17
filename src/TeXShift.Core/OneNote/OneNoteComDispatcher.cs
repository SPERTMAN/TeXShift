using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using TeXShift.Core.Utils;
using OneNoteInterop = Microsoft.Office.Interop.OneNote;

namespace TeXShift.Core.OneNote
{
    /// <summary>Runs OneNote COM calls on one STA with an explicitly marshaled application interface.</summary>
    internal sealed class OneNoteComDispatcher : IDisposable
    {
        private static readonly Guid ApplicationIid = typeof(OneNoteInterop.Application).GUID;
        private readonly StaWorker _worker;
        private IntPtr _interfaceStream;
        private OneNoteInterop.Application _application;

        internal OneNoteComDispatcher(OneNoteInterop.Application application)
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

        internal Task<T> InvokeAsync<T>(Func<OneNoteInterop.Application, T> action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            return _worker.InvokeAsync(() => Task.FromResult(action(GetApplication())));
        }

        internal Task InvokeAsync(Action<OneNoteInterop.Application> action)
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
