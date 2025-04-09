using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using WpfApplication = System.Windows.Application;
using WpfColor = System.Windows.Media.Color;

namespace THJPatcher.Utilities
{
    /// <summary>
    /// Service that handles checking server status and experience bonus
    /// </summary>
    public class ServerStatusService
    {
        private readonly bool _isDebugMode;
        private readonly Action<string> _logAction;
        
        public string PlayerCount { get; private set; } = "0";
        public string ExpBoostPercentage { get; private set; } = "0";
        public bool HasServerData { get; private set; }
        public bool HasExpData { get; private set; }

        public ServerStatusService(bool isDebugMode, Action<string> logAction)
        {
            _isDebugMode = isDebugMode;
            _logAction = logAction ?? (message => { /* No logging if null */ });
        }
        
        /// <summary>
        /// Fetches server status data from the API
        /// </summary>
        public async Task<bool> RefreshStatusAsync()
        {
            string token = Constants.PATCHER_TOKEN;
            if (string.IsNullOrEmpty(token))
            {
                _logAction("[ERROR] Unable to authenticate with server status API - Token missing");
                _logAction("Continuing....");
                return false;
            }

            // Reset status
            PlayerCount = "0";
            ExpBoostPercentage = "0";
            HasServerData = false;
            HasExpData = false;
            
            bool success = false;
            
            try
            {
                // Fetch server status (player count)
                var status = await ApiUtils.GetServerStatusAsync(token, _isDebugMode, _logAction);
                if (status?.Found == true && status.Server != null)
                {
                    PlayerCount = status.Server.PlayersOnline.ToString();
                    HasServerData = true;
                    success = true;
                }
                
                // Fetch experience bonus status
                var expStatus = await ApiUtils.GetExpBonusAsync(token, _isDebugMode, _logAction);
                if (expStatus?.Status == "success" && expStatus.Found)
                {
                    ExpBoostPercentage = ParseExpBoostPercentage(expStatus.ExpBoost);
                    HasExpData = true;
                    success = true;
                }
            }
            catch (Exception ex)
            {
                if (_isDebugMode) _logAction($"[DEBUG] Error fetching server status: {ex.Message}");
                success = false;
            }
            
            return success;
        }
        
        /// <summary>
        /// Updates UI elements with the server status data
        /// </summary>
        public void UpdateUI(TextBlock playerCountBlock, TextBlock expBonusBlock)
        {
            try
            {
                if (playerCountBlock != null && HasServerData)
                {
                    WpfApplication.Current.Dispatcher.Invoke(() =>
                    {
                        playerCountBlock.Inlines.Clear();
                        playerCountBlock.Inlines.Add(new Run("Players Online:"));
                        playerCountBlock.Inlines.Add(new LineBreak());
                        playerCountBlock.Inlines.Add(new Run(PlayerCount) { Foreground = new SolidColorBrush(WpfColor.FromRgb(212, 184, 106)) });
                    });
                }
                
                if (expBonusBlock != null && HasExpData)
                {
                    WpfApplication.Current.Dispatcher.Invoke(() =>
                    {
                        expBonusBlock.Inlines.Clear();
                        expBonusBlock.Inlines.Add(new Run("Experience:"));
                        expBonusBlock.Inlines.Add(new LineBreak());
                        expBonusBlock.Inlines.Add(new Run($"{ExpBoostPercentage}% Bonus") { Foreground = new SolidColorBrush(WpfColor.FromRgb(212, 184, 106)) });
                    });
                }
            }
            catch (Exception ex)
            {
                if (_isDebugMode) _logAction($"[DEBUG] Error updating UI with server status: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Parses experience boost percentage from API response
        /// </summary>
        private string ParseExpBoostPercentage(string expBoostText)
        {
            if (string.IsNullOrEmpty(expBoostText))
                return "0";
                
            try 
            {
                var parts = expBoostText.Split('%');
                if (parts != null && parts.Length > 0)
                {
                    var valuePart = parts[0].Split(':').LastOrDefault()?.Trim();
                    if (!string.IsNullOrEmpty(valuePart) && !valuePart.Equals("Off", StringComparison.OrdinalIgnoreCase))
                    {
                        return valuePart;
                    }
                }
            }
            catch(Exception ex)
            {
                if (_isDebugMode) _logAction($"[DEBUG] Error parsing ExpBoost string '{expBoostText}': {ex.Message}");
                return "?"; // Indicate parsing error
            }
            
            return "0";
        }
    }
} 