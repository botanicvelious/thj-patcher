using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace THJPatcher
{
    internal class StatusLibrary
    {
        readonly static Mutex mux = new Mutex();
        
        public delegate void ProgressHandler(int value);
        static event ProgressHandler progressChange;
        static int progressValue;

        public delegate void LogAddHandler(string message);
        static event LogAddHandler logAddChange;

        public delegate void PatchStateHandler(bool isPatching);
        static event PatchStateHandler patchStateChange;

        public static int Progress()
        {
            mux.WaitOne();
            int value = progressValue;
            mux.ReleaseMutex();
            return value;
        }

        public static void SetProgress(int value)
        {
            mux.WaitOne();
            progressValue = value;
            var handler = progressChange;
            mux.ReleaseMutex();

            if (handler != null)
            {
                Task.Run(() => handler.Invoke(value));
            }
        }

        public static void SubscribeProgress(ProgressHandler f)
        {
            mux.WaitOne();
            progressChange += f;
            mux.ReleaseMutex();
        }

        public static void Log(string message)
        {
            mux.WaitOne();
            var handler = logAddChange;
            mux.ReleaseMutex();

            if (handler != null)
            {
                Task.Run(() => handler.Invoke(message));
            }
        }

        public static void SubscribeLogAdd(LogAddHandler f)
        {
            mux.WaitOne();
            logAddChange += f;
            mux.ReleaseMutex();
        }

        public static void SetPatchState(bool isPatching)
        {
            mux.WaitOne();
            var handler = patchStateChange;
            mux.ReleaseMutex();

            if (handler != null)
            {
                Task.Run(() => handler.Invoke(isPatching));
            }
        }

        public static void SubscribePatchState(PatchStateHandler f)
        {
            mux.WaitOne();
            patchStateChange += f;
            mux.ReleaseMutex();
        }
    }
}
