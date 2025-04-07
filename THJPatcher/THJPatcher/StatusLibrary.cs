using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace THJPatcher
{
    internal class StatusLibrary
    {
        private static readonly object _lock = new object(); 
        
        public delegate void ProgressHandler(int value);
        static event ProgressHandler progressChange;
        static int progressValue;

        private static ConcurrentQueue<string> logQueue = new ConcurrentQueue<string>();
        public delegate void LogUpdateHandler();
        static event LogUpdateHandler logUpdateAvailable;

        public delegate void PatchStateHandler(bool isPatching);
        static event PatchStateHandler patchStateChange;

        public static List<string> DequeueLogMessages()
        {
            List<string> messages = new List<string>();
            while (logQueue.TryDequeue(out string message))
            {
                messages.Add(message);
            }
            return messages;
        }

        public static int Progress()
        {
            lock (_lock)
            {
                 return progressValue;
            }
        }

        public static void SetProgress(int value)
        {
            ProgressHandler handler = null;
            lock (_lock)
            {
                progressValue = value;
                handler = progressChange; 
            }
            handler?.Invoke(value); 
        }

        public static void SubscribeProgress(ProgressHandler f)
        {
            lock (_lock) { progressChange += f; }
        }

        public static void Log(string message)
        {
            logQueue.Enqueue(message);

            LogUpdateHandler handler = logUpdateAvailable;
            handler?.Invoke();
        }

        public static void SubscribeLogUpdateAvailable(LogUpdateHandler f)
        {
            lock (_lock) { logUpdateAvailable += f; }
        }

        public static void SetPatchState(bool isPatching)
        {
            PatchStateHandler handler = null;
             lock(_lock)
             {
                handler = patchStateChange;
             }
            handler?.Invoke(isPatching);
        }

        public static void SubscribePatchState(PatchStateHandler f)
        {
            lock (_lock) { patchStateChange += f; }
        }
    }
}
