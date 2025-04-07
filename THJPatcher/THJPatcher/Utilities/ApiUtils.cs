#nullable enable
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using THJPatcher.Models; // Assuming ChangelogResponse etc. are here

namespace THJPatcher.Utilities
{
    internal static class ApiUtils
    {
        // Fetches changelog data from the API.
        // Returns the deserialized ChangelogResponse on success, null otherwise.
        internal static async Task<ChangelogResponse?> FetchChangelogDataAsync(
            string patcherToken,
            string changelogEndpoint,
            string allChangelogsEndpoint,
            bool fetchAll,
            string currentMessageId,
            bool isDebugMode,
            Action<string> log)
        {
            if (string.IsNullOrEmpty(patcherToken))
            {
                log("[ERROR] Unable to authenticate with changelog API - Token missing");
                log("Continuing....");
                return null;
            }

            string url;
            if (fetchAll)
            {
                url = allChangelogsEndpoint;
                if (isDebugMode) log("[DEBUG] Fetching all changelogs");
            }
            else
            {
                url = $"{changelogEndpoint}{currentMessageId}";
                if (isDebugMode) log($"[DEBUG] Fetching changelogs since: {currentMessageId}");
            }

            if (isDebugMode) log($"[DEBUG] Using URL: {url}");

            try
            {
                using (var client = new HttpClient())
                {
                    // Set a timeout of 5 seconds for the changelog API request
                    client.Timeout = TimeSpan.FromSeconds(5);
                    client.DefaultRequestHeaders.Add("x-patcher-token", patcherToken);

                    if (isDebugMode) log($"[DEBUG] Calling changelog API: {url}");

                    var httpResponse = await client.GetAsync(url);

                    if (httpResponse.StatusCode == System.Net.HttpStatusCode.Forbidden)
                    {
                        log("[ERROR] Authentication failed with changelog API");
                        log("Continuing....");
                        return null;
                    }

                    if (httpResponse.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        log("[ERROR] Changelog API endpoint not found");
                        log("Continuing....");
                        return null;
                    }

                    if (!httpResponse.IsSuccessStatusCode)
                    {
                        log("[ERROR] Failed to connect to changelog API");
                        log("Continuing....");
                        return null;
                    }

                    var response = await httpResponse.Content.ReadAsStringAsync();
                    if (isDebugMode) log($"[DEBUG] API Response: {response}");

                    if (!string.IsNullOrEmpty(response))
                    {
                        var options = new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true,
                            AllowTrailingCommas = true
                        };

                        var changelogResponse = JsonSerializer.Deserialize<ChangelogResponse>(response, options);
                        if (changelogResponse?.Status == "success")
                        {
                            return changelogResponse;
                        }
                        else
                        {
                            log("[Warning] Changelog API did not return success status.");
                        }
                    }
                    else
                    {
                        log("[Warning] Changelog API returned empty response.");
                    }
                }
            }
            catch (TaskCanceledException)
            {
                // Handle timeout specifically
                log("[INFO] Changelog API request timed out after 5 seconds");
                log("Continuing without changelog updates...");
            }
            catch (JsonException jsonEx)
            {
                 if (isDebugMode) log($"[DEBUG] Failed to deserialize changelog JSON: {jsonEx.Message}");
                 log("[ERROR] Failed to process changelog data from API.");
                 log("Continuing....");
            }
            catch (HttpRequestException httpEx)
            {
                 if (isDebugMode) log($"[DEBUG] Changelog HTTP request failed: {httpEx.Message}");
                 log("[ERROR] Failed to connect to changelog API.");
                 log("Continuing....");
            }
            catch (Exception ex)
            {
                if (isDebugMode) log($"[DEBUG] Failed to check changelogs: {ex.Message}");
                log("[ERROR] Failed to connect to changelog API");
                log("Continuing....");
            }

            return null; // Return null if any error occurs or response is not successful
        }

        // Fetches server status data from the API.
        // Returns the deserialized ServerStatus on success, null otherwise.
        internal static async Task<ServerStatus?> GetServerStatusAsync(
            string patcherToken,
            bool isDebugMode,
            Action<string> log)
        {
            if (string.IsNullOrEmpty(patcherToken))
            {
                log("[ERROR] Unable to authenticate with server status API - Token missing");
                log("Continuing....");
                return null;
            }

            if (isDebugMode) log("[DEBUG] Checking server status...");

            try
            {
                using (var client = new HttpClient())
                {
                    // Set a timeout of 3 seconds for server status API requests
                    client.Timeout = TimeSpan.FromSeconds(3);
                    client.DefaultRequestHeaders.Add("x-patcher-token", patcherToken);

                    // Check server status
                    var response = await client.GetStringAsync("http://thj-patcher-gsgvaxf0ehcegjdu.eastus2-01.azurewebsites.net/serverstatus");
                    if (isDebugMode) log($"[DEBUG] Server status response: {response}");

                    var status = JsonSerializer.Deserialize<ServerStatus>(response);
                    return status; // Return regardless of Found status, let caller decide
                }
            }
            catch (TaskCanceledException)
            {
                if (isDebugMode) log("[DEBUG] Server status API request timed out after 3 seconds");
                // Silently fail
            }
            catch (JsonException jsonEx)
            {
                 if (isDebugMode) log($"[DEBUG] Failed to deserialize server status JSON: {jsonEx.Message}");
                 // Silently fail
            }
            catch (HttpRequestException httpEx)
            {
                 if (isDebugMode) log($"[DEBUG] Server status HTTP request failed: {httpEx.Message}");
                 // Silently fail
            }
            catch (Exception ex)
            {
                if (isDebugMode) log($"[DEBUG] Server status check error: {ex.Message}");
                // Silently fail
            }

            return null;
        }

        // Fetches EXP bonus data from the API.
        // Returns the deserialized ExpBonusStatus on success, null otherwise.
        internal static async Task<ExpBonusStatus?> GetExpBonusAsync(
            string patcherToken,
            bool isDebugMode,
            Action<string> log)
        {
            if (string.IsNullOrEmpty(patcherToken))
            {
                // Assume token check was done by caller (e.g., GetServerStatusAsync)
                // or log error if called directly without token.
                log("[ERROR] Unable to authenticate with EXP bonus API - Token missing");
                return null;
            }

            if (isDebugMode) log("[DEBUG] Checking exp bonus...");

            try
            {
                using (var client = new HttpClient())
                {
                    // Set a timeout of 3 seconds for exp bonus API requests
                    client.Timeout = TimeSpan.FromSeconds(3);
                    client.DefaultRequestHeaders.Add("x-patcher-token", patcherToken);

                    var expResponse = await client.GetStringAsync("http://thj-patcher-gsgvaxf0ehcegjdu.eastus2-01.azurewebsites.net/expbonus");
                    if (isDebugMode) log($"[DEBUG] Exp bonus response: {expResponse}");

                    var expStatus = JsonSerializer.Deserialize<ExpBonusStatus>(expResponse);
                    return expStatus; // Return regardless of Status/Found, let caller decide
                }
            }
             catch (TaskCanceledException)
            {
                if (isDebugMode) log("[DEBUG] EXP bonus API request timed out after 3 seconds");
                // Silently fail
            }
            catch (JsonException jsonEx)
            {
                 if (isDebugMode) log($"[DEBUG] Failed to deserialize EXP bonus JSON: {jsonEx.Message}");
                 // Silently fail
            }
            catch (HttpRequestException httpEx)
            {
                 if (isDebugMode) log($"[DEBUG] EXP bonus HTTP request failed: {httpEx.Message}");
                 // Silently fail
            }
            catch (Exception ex)
            {
                if (isDebugMode) log($"[DEBUG] EXP bonus check error: {ex.Message}");
                // Silently fail
            }
            return null;
        }
    }
} 