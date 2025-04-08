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
        
        // Dictionary to track recently logged messages with timestamps
        private static ConcurrentDictionary<string, DateTime> _recentLogMessages = new ConcurrentDictionary<string, DateTime>();
        
        // Time window for deduplication (2 seconds)
        private static readonly TimeSpan _logDeduplicationWindow = TimeSpan.FromSeconds(2);
        
        // Maximum entries to keep in the recent messages cache to prevent memory leaks
        private static readonly int _maxRecentMessages = 100;

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
            // Check if this exact message was logged recently
            DateTime now = DateTime.Now;
            bool shouldLog = true;
            
            if (_recentLogMessages.TryGetValue(message, out DateTime lastTime))
            {
                if (now - lastTime < _logDeduplicationWindow)
                {
                    // Skip logging duplicate message within time window
                    shouldLog = false;
                }
            }
            
            if (shouldLog)
            {
                // Update last time this message was seen
                _recentLogMessages[message] = now;
                
                // Clean up old entries occasionally to prevent memory leaks
                if (_recentLogMessages.Count > _maxRecentMessages)
                {
                    // Remove entries older than the deduplication window
                    foreach (var key in _recentLogMessages.Keys.ToList())
                    {
                        if (_recentLogMessages.TryGetValue(key, out DateTime timestamp) &&
                            now - timestamp > _logDeduplicationWindow)
                        {
                            _recentLogMessages.TryRemove(key, out _);
                        }
                    }
                }
                
                // Add message to queue
                logQueue.Enqueue(message);
                
                // Notify handlers
                LogUpdateHandler handler = logUpdateAvailable;
                handler?.Invoke();
            }
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
