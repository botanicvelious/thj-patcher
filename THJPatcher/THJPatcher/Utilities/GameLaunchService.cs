using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace THJPatcher.Utilities
{
    /// <summary>
    /// Service responsible for launching the game and managing game processes
    /// </summary>
    public class GameLaunchService
    {
        private readonly Action<string> _logAction;
        private readonly bool _isSilentMode;
        private Process _gameProcess;

        /// <summary>
        /// Initializes a new instance of the GameLaunchService
        /// </summary>
        /// <param name="logAction">Action to log messages</param>
        /// <param name="isSilentMode">Whether the application is running in silent mode</param>
        public GameLaunchService(Action<string> logAction, bool isSilentMode)
        {
            _logAction = logAction ?? (message => { /* No logging if null */ });
            _isSilentMode = isSilentMode;
        }

        /// <summary>
        /// Launches the EverQuest game
        /// </summary>
        /// <returns>A value indicating whether the game was successfully launched</returns>
        public bool LaunchGame()
        {
            try
            {
                _logAction("Starting EverQuest...");
                
                // Use the existing utility method to start EverQuest
                _gameProcess = UtilityLibrary.StartEverquest();
                
                if (_gameProcess != null)
                {
                    if (_isSilentMode)
                    {
                        Console.WriteLine("Starting EverQuest...");
                        Console.Out.Flush();

                        // Ensure the process is properly detached
                        _gameProcess.EnableRaisingEvents = false;
                        _gameProcess.StartInfo.UseShellExecute = true;
                        _gameProcess.StartInfo.RedirectStandardOutput = false;
                        _gameProcess.StartInfo.RedirectStandardError = false;
                        _gameProcess.StartInfo.CreateNoWindow = false;

                        // Give the process time to fully start
                        Thread.Sleep(2000);
                    }
                    
                    return true;
                }
                else
                {
                    LogError("Failed to start EverQuest");
                    return false;
                }
            }
            catch (Exception ex)
            {
                LogError($"An error occurred while trying to start EverQuest: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Checks if EverQuest is already running
        /// </summary>
        /// <returns>True if EverQuest is running, false otherwise</returns>
        public bool IsGameRunning()
        {
            try
            {
                Process[] processes = Process.GetProcessesByName("eqgame");
                return processes.Length > 0;
            }
            catch (Exception ex)
            {
                _logAction($"Error checking if game is running: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Gets the path to the EverQuest executable
        /// </summary>
        /// <returns>The full path to eqgame.exe</returns>
        public string GetGameExecutablePath()
        {
            string eqPath = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName);
            return Path.Combine(eqPath, "eqgame.exe");
        }

        /// <summary>
        /// Waits for the game process to exit
        /// </summary>
        /// <param name="timeout">Maximum time to wait in milliseconds</param>
        /// <returns>True if the process exited, false if it timed out</returns>
        public async Task<bool> WaitForGameExitAsync(int timeout = 30000)
        {
            if (_gameProcess == null) return true;
            
            try
            {
                return await Task.Run(() => 
                {
                    return _gameProcess.WaitForExit(timeout);
                });
            }
            catch (Exception ex)
            {
                _logAction($"Error waiting for game exit: {ex.Message}");
                return false;
            }
        }

        private void LogError(string message)
        {
            if (_isSilentMode)
            {
                Console.WriteLine($"[ERROR] {message}");
                Console.Out.Flush();
                Thread.Sleep(2000); // Longer delay for errors
            }
            
            _logAction(message);
        }
    }
} 