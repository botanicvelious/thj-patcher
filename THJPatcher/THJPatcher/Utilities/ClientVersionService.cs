using System;
using System.Collections.Generic;
using THJPatcher.Models;

namespace THJPatcher.Utilities
{
    /// <summary>
    /// Service to manage EverQuest client version information
    /// </summary>
    public class ClientVersionService
    {
        private Dictionary<VersionTypes, ClientVersion> _clientVersions = new Dictionary<VersionTypes, ClientVersion>();
        private readonly Action<string> _logAction;
        private readonly bool _isDebugMode;
        
        /// <summary>
        /// Gets the currently supported client versions
        /// </summary>
        public List<VersionTypes> SupportedClients { get; } = new List<VersionTypes> {
            VersionTypes.Rain_Of_Fear,
            VersionTypes.Rain_Of_Fear_2
        };
        
        /// <summary>
        /// Gets the client versions dictionary
        /// </summary>
        public Dictionary<VersionTypes, ClientVersion> ClientVersions => _clientVersions;
        
        /// <summary>
        /// Creates a new instance of the ClientVersionService
        /// </summary>
        /// <param name="logAction">Action to log messages</param>
        /// <param name="isDebugMode">Whether debug mode is enabled</param>
        public ClientVersionService(Action<string> logAction, bool isDebugMode)
        {
            _logAction = logAction;
            _isDebugMode = isDebugMode;
            
            BuildClientVersions();
        }
        
        /// <summary>
        /// Builds the client versions dictionary
        /// </summary>
        private void BuildClientVersions()
        {
            _clientVersions.Clear();
            _clientVersions.Add(VersionTypes.Titanium, new ClientVersion("Titanium", "titanium"));
            _clientVersions.Add(VersionTypes.Secrets_Of_Feydwer, new ClientVersion("Secrets Of Feydwer", "sof"));
            _clientVersions.Add(VersionTypes.Seeds_Of_Destruction, new ClientVersion("Seeds of Destruction", "sod"));
            _clientVersions.Add(VersionTypes.Rain_Of_Fear, new ClientVersion("Rain of Fear", "rof"));
            _clientVersions.Add(VersionTypes.Rain_Of_Fear_2, new ClientVersion("Rain of Fear 2", "rof2"));
            _clientVersions.Add(VersionTypes.Underfoot, new ClientVersion("Underfoot", "underfoot"));
            _clientVersions.Add(VersionTypes.Broken_Mirror, new ClientVersion("Broken Mirror", "brokenmirror"));
            
            if (_isDebugMode)
            {
                _logAction("[DEBUG] Client versions initialized");
            }
        }
        
        /// <summary>
        /// Gets the client version info for a specific version type
        /// </summary>
        /// <param name="versionType">The version type to get info for</param>
        /// <returns>The client version info or null if not found</returns>
        public ClientVersion GetClientVersion(VersionTypes versionType)
        {
            if (_clientVersions.ContainsKey(versionType))
            {
                return _clientVersions[versionType];
            }
            
            return null;
        }
        
        /// <summary>
        /// Gets the default client version for the patcher
        /// </summary>
        /// <returns>The default client version type</returns>
        public VersionTypes GetDefaultClientVersion()
        {
            // We only support RoF2 now, so return that
            return VersionTypes.Rain_Of_Fear_2;
        }
        
        /// <summary>
        /// Attempts to detect the installed client version
        /// </summary>
        /// <returns>The detected client version or default if detection fails</returns>
        public VersionTypes DetectClientVersion()
        {
            // Currently we only support RoF2, so detection is unnecessary
            // This method could be expanded in the future to examine files or versions
            if (_isDebugMode)
            {
                _logAction("[DEBUG] Client version detection: Using default RoF2");
            }
            
            return GetDefaultClientVersion();
        }
    }
} 