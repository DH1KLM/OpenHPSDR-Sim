using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Patroclus.Avalonia.ViewModels
{
    // Enter and leave on the communication thread. Requests are balanced when it exits.
    internal sealed class StreamThreadScheduling : IDisposable
    {
        private readonly bool timerRequested;
        private readonly IntPtr multimediaTask;
        private readonly ThreadPriority previousPriority;
        private readonly bool priorityChanged;
        private bool disposed;
        public string Description { get; }

        public StreamThreadScheduling()
        {
            if (!OperatingSystem.IsWindows()) { Description = "OS default"; return; }
            timerRequested = timeBeginPeriod(1) == 0;
            uint taskIndex = 0;
            multimediaTask = AvSetMmThreadCharacteristics("Pro Audio", ref taskIndex);
            bool multimediaPriority = multimediaTask != IntPtr.Zero && AvSetMmThreadPriority(multimediaTask, 2);
            previousPriority = Thread.CurrentThread.Priority;
            if (!multimediaPriority)
            {
                Thread.CurrentThread.Priority = ThreadPriority.AboveNormal;
                priorityChanged = true;
            }
            Description = (multimediaPriority ? "MMCSS Pro Audio" : "Above normal")
                + (timerRequested ? " / 1 ms timer" : " / default timer");
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (multimediaTask != IntPtr.Zero) AvRevertMmThreadCharacteristics(multimediaTask);
            if (priorityChanged) Thread.CurrentThread.Priority = previousPriority;
            if (timerRequested) timeEndPeriod(1);
        }

        [DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint period);
        [DllImport("winmm.dll")] private static extern uint timeEndPeriod(uint period);
        [DllImport("avrt.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr AvSetMmThreadCharacteristics(string taskName, ref uint taskIndex);
        [DllImport("avrt.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AvSetMmThreadPriority(IntPtr task, int priority);
        [DllImport("avrt.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AvRevertMmThreadCharacteristics(IntPtr task);
    }
}
