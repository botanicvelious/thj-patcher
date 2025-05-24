using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Net.Http;
using System.Threading;
using YamlDotNet.Core.Tokens;
using System.Runtime.InteropServices.ComTypes;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Diagnostics;

namespace THJPatcher
{
    /* General Utility Methods */
    public static class UtilityLibrary
    {
        //System code to figure out if we have e-cores etc
        [StructLayout(LayoutKind.Sequential)]
        struct GROUP_AFFINITY
        {
            public ulong Mask;
            public ushort Group;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
            public ushort[] Reserved;
        }

        enum LOGICAL_PROCESSOR_RELATIONSHIP
        {
            RelationProcessorCore = 0,
            RelationAll = 0xFFFF
        }
        [StructLayout(LayoutKind.Sequential)]
        struct PROCESSOR_RELATIONSHIP
        {
            public byte Flags;
            public byte EfficiencyClass;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
            public byte[] Reserved;
            public ushort GroupCount;
            public IntPtr GroupMask; // points to GROUP_AFFINITY[]
        }

        [StructLayout(LayoutKind.Sequential)]
        struct SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX
        {
            public LOGICAL_PROCESSOR_RELATIONSHIP Relationship;
            public int Size;
            // Followed by union struct — we handle it manually
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GetLogicalProcessorInformationEx(
            LOGICAL_PROCESSOR_RELATIONSHIP RelationshipType,
            IntPtr Buffer,
            ref int ReturnedLength
        );

        // Win32 constants for window management
        private const int SW_RESTORE = 9;
        private const int SW_SHOW = 5;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_SHOWWINDOW = 0x0040;

        // Win32 API imports
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        //Download a file to current directory
        public static async Task<string> DownloadFile(CancellationTokenSource cts, string url, string outFile)
        {

            try
            {
                var client = new HttpClient();
                var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                response.EnsureSuccessStatusCode();
                using (var stream = await response.Content.ReadAsStreamAsync())
                {
                    var outPath = outFile.Replace("/", "\\");
                    if (outFile.Contains("\\"))
                    { //Make directory if needed.
                        string dir = System.IO.Path.GetDirectoryName(Application.ExecutablePath) + "\\" + outFile.Substring(0, outFile.LastIndexOf("\\"));
                        Directory.CreateDirectory(dir);
                    }
                    outPath = System.IO.Path.GetDirectoryName(Application.ExecutablePath) + "\\" + outFile;

                    using (var w = File.Create(outPath))
                    {
                        await stream.CopyToAsync(w, 81920, cts.Token);
                    }
                }
            }
            catch (ArgumentNullException e)
            {
                return "ArgumentNullExpception: " + e.Message;
            }
            catch (HttpRequestException e)
            {
                return "HttpRequestException: " + e.Message;
            }
            catch (Exception e)
            {
                return "Exception: " + e.Message;
            }
            return "";
        }

        // Download will grab a remote URL's file and return the data as a byte array
        public static async Task<byte[]> Download(CancellationTokenSource cts, string url)
        {
            var client = new HttpClient();
            var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            response.EnsureSuccessStatusCode();
            using (var stream = await response.Content.ReadAsStreamAsync())
            {
                using (var w = new MemoryStream())
                {
                    await stream.CopyToAsync(w, 81920, cts.Token);
                    return w.ToArray();
                }
            }
        }

        public static string GetMD5(string filename)
        {
            using (var md5 = MD5.Create())
            {
                using (var stream = File.OpenRead(filename))
                {
                    var hash = md5.ComputeHash(stream);

                    StringBuilder sb = new StringBuilder();

                    for (int i = 0; i < hash.Length; i++)
                    {
                        sb.Append(hash[i].ToString("X2"));
                    }

                    return sb.ToString();
                }
            }
        }

        public static System.Diagnostics.Process StartEverquest()
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = System.IO.Path.GetDirectoryName(Application.ExecutablePath) + "\\eqgame.exe",
                Arguments = "patchme",
                WorkingDirectory = System.IO.Path.GetDirectoryName(Application.ExecutablePath),
                UseShellExecute = true,
                RedirectStandardOutput = false,
                RedirectStandardError = false,
                CreateNoWindow = false,
                LoadUserProfile = true,
                WindowStyle = ProcessWindowStyle.Normal
            };

            var process = System.Diagnostics.Process.Start(startInfo);

            // Wait for the window to be created (up to 10 seconds)
            for (int i = 0; i < 20 && process.MainWindowHandle == IntPtr.Zero; i++)
            {
                Thread.Sleep(500);
                process.Refresh();
            }

            if (process.MainWindowHandle != IntPtr.Zero)
            {
                // Ensure window is not minimized
                if (IsIconic(process.MainWindowHandle))
                {
                    ShowWindow(process.MainWindowHandle, SW_RESTORE);
                }
                // Force window to show properly
                ShowWindow(process.MainWindowHandle, SW_SHOW);
                SetWindowPos(process.MainWindowHandle, IntPtr.Zero, 0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);

                // Hide the patcher application window
                /* IntPtr patcherWindowHandle = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
                if (patcherWindowHandle != IntPtr.Zero)
                {
                    ShowWindow(patcherWindowHandle, 0);
                }*/
            }

            // Apply CPU affinity if enabled
            if (IniLibrary.instance.EnableCpuAffinity == "true")
            {
                int length = 0;
                GetLogicalProcessorInformationEx(LOGICAL_PROCESSOR_RELATIONSHIP.RelationProcessorCore, IntPtr.Zero, ref length);
                IntPtr ptr = Marshal.AllocHGlobal(length);

                if (!GetLogicalProcessorInformationEx(LOGICAL_PROCESSOR_RELATIONSHIP.RelationProcessorCore, ptr, ref length))
                {
                    Console.WriteLine("Failed to get processor info.");
                }

                IntPtr current = ptr;
                IntPtr end = IntPtr.Add(ptr, length);

                List<int> pCores = new List<int>();
                List<int> eCores = new List<int>();
                int coreIndex = 0;

                while (current.ToInt64() < end.ToInt64())
                {
                    int structSize = Marshal.ReadInt32(current, 4);

                    // Skip header to reach PROCESSOR_RELATIONSHIP
                    IntPtr processorPtr = IntPtr.Add(current, Marshal.OffsetOf<SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX>("Size").ToInt32() + 4);
                    PROCESSOR_RELATIONSHIP processor = Marshal.PtrToStructure<PROCESSOR_RELATIONSHIP>(processorPtr);

                    // Interpret EfficiencyClass
                    if (processor.EfficiencyClass == 0)
                    {
                        if (processor.Flags == 1)
                        {
                            pCores.Add(coreIndex);
                            coreIndex++;
                            pCores.Add(coreIndex);
                        }
                        else
                        {
                            pCores.Add(coreIndex);
                        }
                    }
                    else
                    {
                        if (processor.Flags == 1)
                        {
                            eCores.Add(coreIndex);
                            coreIndex++;
                            eCores.Add(coreIndex);
                        }
                        else
                        {
                            eCores.Add(coreIndex);
                        }
                    }

                    coreIndex++;
                    current = IntPtr.Add(current, structSize);
                }

                Marshal.FreeHGlobal(ptr);

                // if ecores exist replace pCores with eCores as pCores are 1 if eCores exist but 0 if they dont...
                if (eCores.Count > 0)
                {
                    pCores = eCores;
                }

                Console.WriteLine("Performance Cores (P-Cores):");
                foreach (var core in pCores)
                    Console.WriteLine($"Core {core}");

                Console.WriteLine("\nEfficiency Cores (E-Cores):");
                foreach (var core in eCores)
                    Console.WriteLine($"Core {core}");

                int cores = 0;

                foreach (int i in pCores)
                {
                    cores = cores + ((cores + 1));
                    Console.WriteLine(cores);
                }

                process.ProcessorAffinity = (IntPtr)cores;
                process.PriorityClass = ProcessPriorityClass.High;

                // Wait 60 seconds to assign threads (Removed for now)
                //Thread.Sleep(60000);

                // Set threads to be spread out and not switch cores
                int index = 0;
                foreach (ProcessThread eqthreads in process.Threads)
                {
                    eqthreads.IdealProcessor = index;
                    if (index != pCores.Count)
                    {
                        index++;
                    }
                    else
                    {
                        index = 0;
                    }

                }

            }

            return process;
        }

        //Pass the working directory (or later, you can pass another directory) and it returns a hash if the file is found
        public static string GetEverquestExecutableHash(string path)
        {
            var di = new System.IO.DirectoryInfo(path);
            var files = di.GetFiles("eqgame.exe");
            if (files == null || files.Length == 0)
            {
                return "";
            }
            return UtilityLibrary.GetMD5(files[0].FullName);
        }

        // Returns true only if the path is a relative and does not contain ..
        public static bool IsPathChild(string path)
        {
            // get the absolute path
            var absPath = Path.GetFullPath(path);
            var basePath = Path.GetDirectoryName(Application.ExecutablePath);
            // check if absPath contains basePath
            if (!absPath.Contains(basePath))
            {
                return false;
            }
            if (path.Contains("..\\"))
            {
                return false;
            }
            return true;
        }

        // MoveFileEx flags for scheduling file operations
        [Flags]
        public enum MoveFileFlags
        {
            MOVEFILE_REPLACE_EXISTING = 0x00000001,
            MOVEFILE_COPY_ALLOWED = 0x00000002,
            MOVEFILE_DELAY_UNTIL_REBOOT = 0x00000004,
            MOVEFILE_WRITE_THROUGH = 0x00000008
        }

        // Import the MoveFileEx function from kernel32.dll
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool MoveFileEx(string lpExistingFileName, string lpNewFileName, MoveFileFlags dwFlags);

        // Wrapper method for MoveFileEx with a different name to avoid conflict
        public static bool ScheduleFileOperation(string existingFile, string newFile, MoveFileFlags flags)
        {
            return MoveFileEx(existingFile, newFile, flags);
        }
    }
}
