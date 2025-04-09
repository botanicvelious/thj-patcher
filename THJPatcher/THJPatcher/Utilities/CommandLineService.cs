using System;
using System.Linq;

namespace THJPatcher.Utilities
{
    /// <summary>
    /// Service to handle command line arguments
    /// </summary>
    public class CommandLineService
    {
        private readonly string[] _args;
        private readonly Action<string> _logAction;
        
        /// <summary>
        /// Gets whether silent mode is enabled
        /// </summary>
        public bool IsSilentMode { get; private set; }
        
        /// <summary>
        /// Gets whether auto-confirm is enabled
        /// </summary>
        public bool IsAutoConfirm { get; private set; }
        
        /// <summary>
        /// Gets whether debug mode is enabled
        /// </summary>
        public bool IsDebugMode { get; private set; }
        
        /// <summary>
        /// Creates a new instance of the CommandLineService
        /// </summary>
        /// <param name="args">Command line arguments</param>
        /// <param name="logAction">Action to log messages</param>
        public CommandLineService(string[] args, Action<string> logAction)
        {
            _args = args ?? Environment.GetCommandLineArgs();
            _logAction = logAction;
            
            ParseArguments();
        }
        
        /// <summary>
        /// Parses command line arguments
        /// </summary>
        private void ParseArguments()
        {
            foreach (var arg in _args)
            {
                var lowerArg = arg.ToLower();
                switch (lowerArg)
                {
                    case "--silent":
                    case "-silent":
                        IsSilentMode = true;
                        break;
                    case "--confirm":
                    case "-confirm":
                        IsAutoConfirm = true;
                        break;
                    case "--debug":
                    case "-debug":
                        IsDebugMode = true;
                        break;
                }
            }
            
            // Log debug mode if enabled
            if (IsDebugMode)
            {
                _logAction?.Invoke("[DEBUG] Debug mode enabled");
            }
            
            // Double-check for debug flag specifically
            if (_args.Any(a => a.Equals("-debug", StringComparison.OrdinalIgnoreCase)))
            {
                IsDebugMode = true;
                _logAction?.Invoke("[DEBUG] Debug mode enabled (alternate check)");
            }
        }
        
        /// <summary>
        /// Gets whether a specific argument is present
        /// </summary>
        /// <param name="argName">The argument name to check for</param>
        /// <returns>True if the argument is present, otherwise false</returns>
        public bool HasArgument(string argName)
        {
            return _args.Any(arg => arg.Equals(argName, StringComparison.OrdinalIgnoreCase) ||
                                    arg.Equals($"-{argName}", StringComparison.OrdinalIgnoreCase) ||
                                    arg.Equals($"--{argName}", StringComparison.OrdinalIgnoreCase));
        }
        
        /// <summary>
        /// Gets the value of a specific argument
        /// </summary>
        /// <param name="argName">The argument name to get the value for</param>
        /// <returns>The argument value or null if not found</returns>
        public string GetArgumentValue(string argName)
        {
            for (int i = 0; i < _args.Length - 1; i++)
            {
                var arg = _args[i];
                if (arg.Equals(argName, StringComparison.OrdinalIgnoreCase) ||
                    arg.Equals($"-{argName}", StringComparison.OrdinalIgnoreCase) ||
                    arg.Equals($"--{argName}", StringComparison.OrdinalIgnoreCase))
                {
                    return _args[i + 1];
                }
            }
            
            return null;
        }
    }
} 