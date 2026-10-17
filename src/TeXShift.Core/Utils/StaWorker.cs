using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TeXShift.Core.Utils
{
    /// <summary>Owns an STA message loop and releases its resources before the thread exits.</summary>
    internal sealed class StaWorker : IDisposable
    {
        private readonly object _gate = new object();
        private readonly HashSet<Action> _cancelPending = new HashSet<Action>();
        private readonly Thread _thread;
        private readonly Action _cleanup;
        private readonly TaskCompletionSource<bool> _ready = new TaskCompletionSource<bool>();
        private SynchronizationContext _context;
        private bool _disposed;
        private Exception _shutdownError;

        public StaWorker(string name, Action cleanup)
        {
            _cleanup = cleanup;
            _thread = new Thread(Run) { IsBackground = true, Name = name };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
            _ready.Task.GetAwaiter().GetResult();
        }

        private void Run()
        {
            try
            {
                using (var context = new WindowsFormsSynchronizationContext())
                {
                    _context = context;
                    SynchronizationContext.SetSynchronizationContext(context);
                    _ready.TrySetResult(true);
                    try { Application.Run(); }
                    finally { _cleanup(); }
                }
            }
            catch (Exception ex)
            {
                _shutdownError = ex;
                _ready.TrySetException(ex);
            }
            finally
            {
                lock (_gate)
                {
                    _disposed = true;
                    foreach (var cancel in _cancelPending) cancel();
                    _cancelPending.Clear();
                }
            }
        }

        public Task<T> InvokeAsync<T>(Func<Task<T>> action)
        {
            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            Action cancel = () => completion.TrySetCanceled();
            lock (_gate)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(StaWorker));
                _cancelPending.Add(cancel);
                _context.Post(async _ =>
                {
                    if (completion.Task.IsCompleted) return;
                    try { completion.TrySetResult(await action()); }
                    catch (Exception ex) { completion.TrySetException(ex); }
                    finally { lock (_gate) _cancelPending.Remove(cancel); }
                }, null);
            }
            return completion.Task;
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (!_disposed)
                {
                    _disposed = true;
                    foreach (var cancel in _cancelPending) cancel();
                    _cancelPending.Clear();
                    _context.Post(_ => Application.ExitThread(), null);
                }
            }
            if (Thread.CurrentThread == _thread) return;
            if (!_thread.Join(TimeSpan.FromSeconds(5)))
                throw new TimeoutException($"STA thread {_thread.Name} did not exit.");
            if (_shutdownError != null)
                throw new InvalidOperationException("STA resource cleanup failed.", _shutdownError);
        }
    }
}
