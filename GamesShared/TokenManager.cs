using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Manages authentication tokens for Twitch and StreamElements
/// Handles token refresh and persistence
/// </summary>
public class TokenManager : MonoBehaviour
{
    [Serializable]
    private class TokenData
    {
        public string twitchClientId;
        public string twitchClientSecret;
        public string twitchAccessToken;
        public string twitchRefreshToken;
        public DateTime twitchTokenExpiry;
        // New StreamElements OAuth fields
        public string streamElementsClientId;
        public string streamElementsClientSecret;
        public string streamElementsAccessToken;
        public string streamElementsRefreshToken;
        public DateTime streamElementsTokenExpiry;
        public string streamElementsChannelId;
        // JWT token (legacy auth)
        public string streamElementsJwtToken;
    }

    [Header("Twitch Authentication")]
    [SerializeField, Tooltip("Your Twitch Application Client ID")]
    private string twitchClientId;
    
    [SerializeField, Tooltip("Your Twitch Application Client Secret")]
    private string twitchClientSecret;

    [Header("Twitch Authorization")]
    [SerializeField, Tooltip("Authorization URL to navigate to in a browser")]
    private string authorizationUrl;
    
    [SerializeField, Tooltip("Authorization code received from redirect")]
    private string authorizationCode;
    
    [SerializeField, Tooltip("Generate authorization URL based on client ID")]
    private bool generateTwitchAuthUrl = false;
    
    [SerializeField, Tooltip("Exchange auth code for tokens")]
    private bool exchangeTwitchAuthCode = false;
    
    private string tokenStoragePath;
    private bool isInitialized = false;
    
    [Header("StreamElements Authentication")]
    [SerializeField, Tooltip("Your StreamElements Client ID")]
    private string streamElementsClientId;
    
    [SerializeField, Tooltip("Your StreamElements Client Secret")]
    private string streamElementsClientSecret;
    
    [SerializeField, Tooltip("Your StreamElements Channel ID")]
    private string streamElementsChannelId;
    
    // JWT token for StreamElements (legacy authentication method)
    [SerializeField, Tooltip("Your StreamElements JWT token (legacy auth)")]
    private string streamElementsJwtToken;
    
    // OAuth tokens for StreamElements (replacing JWT)
    private string streamElementsAccessToken;
    private string streamElementsRefreshToken;
    private DateTime streamElementsTokenExpiry;
    
    [Header("StreamElements Authorization")]
    [SerializeField, Tooltip("Authorization URL for StreamElements")]
    private string streamElementsAuthUrl;
    
    [SerializeField, Tooltip("StreamElements authorization code received from redirect")]
    private string streamElementsAuthCode;
    
    [SerializeField, Tooltip("Generate StreamElements authorization URL")]
    private bool generateStreamElementsAuthUrl = false;
    
    [SerializeField, Tooltip("Exchange StreamElements auth code for tokens")]
    private bool exchangeStreamElementsAuthCode = false;
    
    private string twitchAccessToken;
    private string twitchRefreshToken;
    private DateTime twitchTokenExpiry;

    [Header("Automatic Token Refresh")]
    // Add token expiration tracking
    [SerializeField] private float tokenRefreshSafetyMarginMinutes = 60f; // Refresh tokens 1 hour before expiry
    
    // Track token expiration times
    private DateTime twitchTokenExpiryTime;
    private DateTime streamElementsTokenExpiryTime;
    
    // Standard Twitch token lifetime is 4 hours, but we'll use a default in case we can't determine it
    private float defaultTwitchTokenLifetimeHours = 4f;
    
    // Standard StreamElements JWT lifetime is 30 days, but we'll use a default in case we can't determine it
    private float defaultStreamElementsTokenLifetimeHours = 24 * 30f; // 30 days
    
    void Awake()
    {
        tokenStoragePath = Path.Combine(Application.persistentDataPath, "tokens.json");
        LoadTokens();
        
        if (!string.IsNullOrEmpty(twitchAccessToken) && 
            !string.IsNullOrEmpty(twitchRefreshToken) &&
            twitchTokenExpiry > DateTime.UtcNow.AddMinutes(10))
        {
            isInitialized = true;
            Debug.Log("TokenManager: Tokens loaded successfully");
        }
        else
        {
            Debug.LogWarning("TokenManager: Tokens not found or expired. Setup required.");
        }
        
        // Start token refresh coroutine
        StartCoroutine(TokenRefreshRoutine());
    }
    
    void OnValidate()
    {
        // Generate the authorization URL when requested for Twitch
        if (generateTwitchAuthUrl && !string.IsNullOrEmpty(twitchClientId))
        {
            generateTwitchAuthUrl = false;
            string scopes = "chat:read+chat:edit+channel:moderate+channel:read:redemptions";
            authorizationUrl = $"https://id.twitch.tv/oauth2/authorize?response_type=code&client_id={twitchClientId}&redirect_uri=https://zamburg1.github.io/twitch-auth-redirect/&scope={scopes}&state={Guid.NewGuid()}";
            Debug.Log($"Authorization URL generated. Open this in a browser: {authorizationUrl}");
        }
        
        // Exchange the authorization code for tokens when requested for Twitch
        if (exchangeTwitchAuthCode && !string.IsNullOrEmpty(authorizationCode))
        {
            exchangeTwitchAuthCode = false;
            StartCoroutine(ExchangeCodeForTokens());
        }
        
        // Generate the authorization URL when requested for StreamElements
        if (generateStreamElementsAuthUrl && !string.IsNullOrEmpty(streamElementsClientId))
        {
            generateStreamElementsAuthUrl = false;
            string scopes = "channel:read+points:write";
            string redirectUri = "https://zamburg1.github.io/twitch-auth-redirect/";
            string state = Guid.NewGuid().ToString();
            streamElementsAuthUrl = $"https://api.streamelements.com/oauth2/authorize?client_id={streamElementsClientId}&redirect_uri={redirectUri}&response_type=code&scope={scopes}&state={state}";
            Debug.Log($"StreamElements Authorization URL generated. Open this in a browser: {streamElementsAuthUrl}");
        }
        
        // Exchange the authorization code for tokens when requested for StreamElements
        if (exchangeStreamElementsAuthCode && !string.IsNullOrEmpty(streamElementsAuthCode))
        {
            exchangeStreamElementsAuthCode = false;
            StartCoroutine(ExchangeStreamElementsCodeForTokens());
        }
    }
    
    private IEnumerator ExchangeCodeForTokens()
    {
        string url = "https://id.twitch.tv/oauth2/token";
        WWWForm form = new WWWForm();
        form.AddField("client_id", twitchClientId);
        form.AddField("client_secret", twitchClientSecret);
        form.AddField("code", authorizationCode);
        form.AddField("grant_type", "authorization_code");
        form.AddField("redirect_uri", "https://zamburg1.github.io/twitch-auth-redirect/");

        using (UnityWebRequest request = UnityWebRequest.Post(url, form))
        {
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"Failed to exchange code for tokens: {request.error}");
                yield break;
            }

            string response = request.downloadHandler.text;
            TokenResponse tokenResponse = JsonUtility.FromJson<TokenResponse>(response);
            
            if (tokenResponse != null)
            {
                twitchAccessToken = tokenResponse.access_token;
                twitchRefreshToken = tokenResponse.refresh_token;
                twitchTokenExpiry = DateTime.UtcNow.AddSeconds(tokenResponse.expires_in);
                
                SaveTokens();
                isInitialized = true;
                
                Debug.Log("Successfully obtained Twitch tokens!");
                
                // Clear the authorization code for security
                authorizationCode = "";
            }
        }
    }
    
    private IEnumerator ExchangeStreamElementsCodeForTokens()
    {
        string url = "https://api.streamelements.com/oauth2/token";
        WWWForm form = new WWWForm();
        form.AddField("client_id", streamElementsClientId);
        form.AddField("client_secret", streamElementsClientSecret);
        form.AddField("code", streamElementsAuthCode);
        form.AddField("grant_type", "authorization_code");
        form.AddField("redirect_uri", "https://zamburg1.github.io/twitch-auth-redirect/");

        using (UnityWebRequest request = UnityWebRequest.Post(url, form))
        {
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"Failed to exchange code for StreamElements tokens: {request.error}");
                yield break;
            }

            string response = request.downloadHandler.text;
            StreamElementsTokenResponse tokenResponse = JsonUtility.FromJson<StreamElementsTokenResponse>(response);
            
            if (tokenResponse != null)
            {
                streamElementsAccessToken = tokenResponse.access_token;
                streamElementsRefreshToken = tokenResponse.refresh_token;
                streamElementsTokenExpiry = DateTime.UtcNow.AddSeconds(tokenResponse.expires_in);
                
                SaveTokens();
                
                Debug.Log("Successfully obtained StreamElements tokens!");
                
                // Clear the authorization code for security
                streamElementsAuthCode = "";
            }
        }
    }
    
    [Serializable]
    private class TokenResponse
    {
        public string access_token;
        public string refresh_token;
        public int expires_in;
        public string[] scope;
        public string token_type;
    }
    
    [Serializable]
    private class StreamElementsTokenResponse
    {
        public string access_token;
        public string refresh_token;
        public int expires_in;
        public string token_type;
    }
    
    private void LoadTokens()
    {
        if (File.Exists(tokenStoragePath))
        {
            try
            {
                string json = File.ReadAllText(tokenStoragePath);
                TokenData data = JsonUtility.FromJson<TokenData>(json);
                
                twitchClientId = data.twitchClientId;
                twitchClientSecret = data.twitchClientSecret;
                twitchAccessToken = data.twitchAccessToken;
                twitchRefreshToken = data.twitchRefreshToken;
                twitchTokenExpiry = data.twitchTokenExpiry;
                streamElementsClientId = data.streamElementsClientId;
                streamElementsClientSecret = data.streamElementsClientSecret;
                streamElementsAccessToken = data.streamElementsAccessToken;
                streamElementsRefreshToken = data.streamElementsRefreshToken;
                streamElementsTokenExpiry = data.streamElementsTokenExpiry;
                streamElementsChannelId = data.streamElementsChannelId;
                streamElementsJwtToken = data.streamElementsJwtToken;
            }
            catch (Exception e)
            {
                Debug.LogError($"Error loading tokens: {e.Message}");
            }
        }
    }
    
    private void SaveTokens()
    {
        try
        {
            TokenData data = new TokenData
            {
                twitchClientId = twitchClientId,
                twitchClientSecret = twitchClientSecret,
                twitchAccessToken = twitchAccessToken,
                twitchRefreshToken = twitchRefreshToken,
                twitchTokenExpiry = twitchTokenExpiry,
                streamElementsClientId = streamElementsClientId,
                streamElementsClientSecret = streamElementsClientSecret,
                streamElementsAccessToken = streamElementsAccessToken,
                streamElementsRefreshToken = streamElementsRefreshToken,
                streamElementsTokenExpiry = streamElementsTokenExpiry,
                streamElementsChannelId = streamElementsChannelId,
                streamElementsJwtToken = streamElementsJwtToken
            };
            
            string json = JsonUtility.ToJson(data);
            File.WriteAllText(tokenStoragePath, json);
            
            Debug.Log("Tokens saved successfully");
        }
        catch (Exception e)
        {
            Debug.LogError($"Error saving tokens: {e.Message}");
        }
    }
    
    private IEnumerator TokenRefreshRoutine()
    {
        while (true)
        {
            // Wait for initial setup to complete
            if (!isInitialized)
            {
                yield return new WaitForSeconds(30);
                continue;
            }
            
            // Check if token needs refreshing (with a buffer of 1 hour)
            if (DateTime.UtcNow.AddHours(1) > twitchTokenExpiry)
            {
                yield return RefreshTwitchToken();
            }
            
            // Wait for 30 minutes before checking again
            yield return new WaitForSeconds(1800);
        }
    }
    
    private IEnumerator RefreshTwitchToken()
    {
        Debug.Log("Refreshing Twitch token...");
        
        string url = "https://id.twitch.tv/oauth2/token";
        WWWForm form = new WWWForm();
        form.AddField("client_id", twitchClientId);
        form.AddField("client_secret", twitchClientSecret);
        form.AddField("refresh_token", twitchRefreshToken);
        form.AddField("grant_type", "refresh_token");

        using (UnityWebRequest request = UnityWebRequest.Post(url, form))
        {
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"Failed to refresh token: {request.error}");
                yield break;
            }

            string response = request.downloadHandler.text;
            TokenResponse tokenResponse = JsonUtility.FromJson<TokenResponse>(response);
            
            if (tokenResponse != null)
            {
                twitchAccessToken = tokenResponse.access_token;
                twitchRefreshToken = tokenResponse.refresh_token;
                twitchTokenExpiry = DateTime.UtcNow.AddSeconds(tokenResponse.expires_in);
                
                SaveTokens();
                
                Debug.Log("Successfully refreshed Twitch token!");
            }
        }
    }
    
    // Methods to access tokens from other components
    public string GetTwitchAccessToken()
    {
        // Ensure token is valid before returning
        if (DateTime.UtcNow > twitchTokenExpiry)
        {
            Debug.LogWarning("Token expired! Triggering immediate refresh.");
            // We can't yield here, so we'll return the current token and trust the refresh routine
            StartCoroutine(RefreshTwitchToken());
        }
        return twitchAccessToken;
    }
    
    public string GetTwitchClientId()
    {
        return twitchClientId;
    }
    
    public string GetStreamElementsClientId()
    {
        return streamElementsClientId;
    }
    
    public string GetStreamElementsClientSecret()
    {
        return streamElementsClientSecret;
    }
    
    public string GetStreamElementsAccessToken()
    {
        return streamElementsAccessToken;
    }
    
    public string GetStreamElementsRefreshToken()
    {
        return streamElementsRefreshToken;
    }
    
    public string GetStreamElementsChannelId()
    {
        return streamElementsChannelId;
    }
    
    // Format for environment variables
    public string GetTwitchOAuthFormatted()
    {
        string token = GetTwitchAccessToken();
        if (!token.StartsWith("oauth:"))
        {
            token = "oauth:" + token;
        }
        return token;
    }

    private void Start()
    {
        InitializeTokenExpiryTimes();
        
        // Start token refresh check
        StartCoroutine(CheckTokenRefreshRoutine());
    }
    
    private void InitializeTokenExpiryTimes()
    {
        // Initialize with default expiry times from now
        twitchTokenExpiryTime = DateTime.UtcNow.AddHours(defaultTwitchTokenLifetimeHours);
        streamElementsTokenExpiryTime = DateTime.UtcNow.AddHours(defaultStreamElementsTokenLifetimeHours);
        
        // Try to load saved expiry times if available
        LoadTokenExpiryTimes();
        
        Debug.Log($"Token expiry times initialized - Twitch: {twitchTokenExpiryTime}, StreamElements: {streamElementsTokenExpiryTime}");
    }
    
    /// <summary>
    /// Check if tokens need refresh periodically
    /// </summary>
    private IEnumerator CheckTokenRefreshRoutine()
    {
        while (true)
        {
            // Check tokens every 15 minutes
            yield return new WaitForSeconds(15 * 60);
            
            CheckAndRefreshTokens();
        }
    }
    
    /// <summary>
    /// Check if any tokens need refreshing and handle accordingly
    /// </summary>
    public void CheckAndRefreshTokens()
    {
        DateTime now = DateTime.UtcNow;
        TimeSpan safetyMargin = TimeSpan.FromMinutes(tokenRefreshSafetyMarginMinutes);
        
        // Check Twitch token
        if (now + safetyMargin >= twitchTokenExpiryTime)
        {
            Debug.LogWarning($"Twitch token approaching expiry (expires {twitchTokenExpiryTime}). Attempting refresh.");
            ManualRefreshTwitchToken();
        }
        
        // Check StreamElements token
        if (now + safetyMargin >= streamElementsTokenExpiryTime)
        {
            Debug.LogWarning($"StreamElements token approaching expiry (expires {streamElementsTokenExpiryTime}). Attempting refresh.");
            RefreshStreamElementsToken();
        }
    }
    
    /// <summary>
    /// Refresh the StreamElements token
    /// </summary>
    private void RefreshStreamElementsToken()
    {
        // Try automatic refresh first if we have refresh token
        if (!string.IsNullOrEmpty(streamElementsRefreshToken))
        {
            StartCoroutine(RefreshStreamElementsTokenAutomatically());
        }
        else
        {
            // Fall back to manual notification if we don't have a refresh token
            Debug.LogWarning("IMPORTANT: StreamElements OAuth token needs refreshing. Please update in the Inspector.");
            
            // Schedule a UI notification
            StartCoroutine(ShowTokenRefreshNotification("StreamElements"));
        }
    }
    
    /// <summary>
    /// Attempt to automatically refresh the StreamElements token using the refresh token
    /// </summary>
    private IEnumerator RefreshStreamElementsTokenAutomatically()
    {
        Debug.Log("Attempting automatic StreamElements token refresh...");
        
        string url = "https://api.streamelements.com/oauth2/token";
        WWWForm form = new WWWForm();
        form.AddField("client_id", streamElementsClientId);
        form.AddField("client_secret", streamElementsClientSecret);
        form.AddField("refresh_token", streamElementsRefreshToken);
        form.AddField("grant_type", "refresh_token");
        form.AddField("redirect_uri", "https://zamburg1.github.io/twitch-auth-redirect/");

        // Track number of retry attempts
        int retryAttempt = 0;
        int maxRetries = 3;
        float initialRetryDelay = 2f;
        bool success = false;
        
        while (!success && retryAttempt <= maxRetries)
        {
            if (retryAttempt > 0)
            {
                // Apply exponential backoff
                float delayTime = initialRetryDelay * Mathf.Pow(2, retryAttempt - 1);
                Debug.Log($"Retrying StreamElements token refresh in {delayTime:F1} seconds (attempt {retryAttempt}/{maxRetries})");
                yield return new WaitForSeconds(delayTime);
            }
            
            retryAttempt++;
            
            using (UnityWebRequest request = UnityWebRequest.Post(url, form))
            {
                yield return request.SendWebRequest();
                
                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"Failed to automatically refresh StreamElements token (attempt {retryAttempt}/{maxRetries}): {request.error}");
                    
                    // Check if we should try again
                    if (retryAttempt <= maxRetries)
                    {
                        continue;
                    }
                    else
                    {
                        // Fall back to manual notification after all retries fail
                        StartCoroutine(ShowTokenRefreshNotification("StreamElements"));
                        yield break;
                    }
                }
                
                string response = request.downloadHandler.text;
                
                try {
                    StreamElementsTokenResponse tokenResponse = JsonUtility.FromJson<StreamElementsTokenResponse>(response);
                    
                    if (tokenResponse != null && !string.IsNullOrEmpty(tokenResponse.access_token))
                    {
                        // Successfully refreshed
                        streamElementsAccessToken = tokenResponse.access_token;
                        
                        // Update refresh token if provided
                        if (!string.IsNullOrEmpty(tokenResponse.refresh_token))
                        {
                            streamElementsRefreshToken = tokenResponse.refresh_token;
                        }
                        
                        // Update expiry
                        streamElementsTokenExpiry = DateTime.UtcNow.AddSeconds(tokenResponse.expires_in);
                        
                        // Store in environment variable
                        Environment.SetEnvironmentVariable("STREAMELEMENTS_ACCESS_TOKEN", streamElementsAccessToken);
                        Environment.SetEnvironmentVariable("STREAMELEMENTS_REFRESH_TOKEN", streamElementsRefreshToken);
                        
                        // Save tokens
                        SaveTokens();
                        SaveTokenExpiryTimes();
                        
                        Debug.Log("Successfully refreshed StreamElements token automatically!");
                        success = true;
                    }
                    else
                    {
                        throw new Exception("Invalid token response format");
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Error parsing StreamElements token refresh response: {ex.Message}");
                    
                    // Check if we should try again
                    if (retryAttempt <= maxRetries)
                    {
                        continue;
                    }
                    else
                    {
                        // Fall back to manual notification after all retries fail
                        StartCoroutine(ShowTokenRefreshNotification("StreamElements"));
                        yield break;
                    }
                }
            }
        }
    }
    
    /// <summary>
    /// Manual refresh for the Twitch OAuth token when automatic refresh isn't possible
    /// </summary>
    private void ManualRefreshTwitchToken()
    {
        // Try automatic refresh first if we have refresh token
        if (!string.IsNullOrEmpty(twitchRefreshToken))
        {
            StartCoroutine(RefreshTwitchTokenAutomatically());
        }
        else
        {
            // Fall back to manual notification if we don't have a refresh token
            Debug.LogWarning("IMPORTANT: Twitch OAuth token needs refreshing. Please update in the Inspector.");
            
            // Schedule a UI notification
            StartCoroutine(ShowTokenRefreshNotification("Twitch"));
        }
    }
    
    /// <summary>
    /// Attempt to automatically refresh the Twitch token using the refresh token
    /// </summary>
    private IEnumerator RefreshTwitchTokenAutomatically()
    {
        Debug.Log("Attempting automatic Twitch token refresh...");
        
        string url = "https://id.twitch.tv/oauth2/token";
        WWWForm form = new WWWForm();
        form.AddField("client_id", twitchClientId);
        form.AddField("client_secret", twitchClientSecret);
        form.AddField("refresh_token", twitchRefreshToken);
        form.AddField("grant_type", "refresh_token");

        using (UnityWebRequest request = UnityWebRequest.Post(url, form))
        {
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"Failed to automatically refresh Twitch token: {request.error}");
                
                // Fall back to manual notification
                StartCoroutine(ShowTokenRefreshNotification("Twitch"));
                yield break;
            }

            string response = request.downloadHandler.text;
            
            try {
                TokenResponse tokenResponse = JsonUtility.FromJson<TokenResponse>(response);
                
                if (tokenResponse != null && !string.IsNullOrEmpty(tokenResponse.access_token))
                {
                    // Successfully refreshed
                    twitchAccessToken = tokenResponse.access_token;
                    
                    // Update refresh token if provided
                    if (!string.IsNullOrEmpty(tokenResponse.refresh_token))
                    {
                        twitchRefreshToken = tokenResponse.refresh_token;
                    }
                    
                    // Update expiry
                    twitchTokenExpiryTime = DateTime.UtcNow.AddSeconds(tokenResponse.expires_in);
                    twitchTokenExpiry = twitchTokenExpiryTime; // Update both expiry tracking variables
                    
                    // Store in environment variable
                    Environment.SetEnvironmentVariable("TWITCH_OAUTH_TOKEN", twitchAccessToken);
                    
                    // Save tokens
                    SaveTokens();
                    SaveTokenExpiryTimes();
                    
                    Debug.Log("Successfully refreshed Twitch token automatically!");
                }
                else
                {
                    throw new Exception("Invalid token response format");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error parsing token refresh response: {ex.Message}");
                StartCoroutine(ShowTokenRefreshNotification("Twitch"));
            }
        }
    }
    
    /// <summary>
    /// Show a notification to the user that a token needs refreshing
    /// </summary>
    private IEnumerator ShowTokenRefreshNotification(string tokenType)
    {
        // Display notification UI for 5 minutes, every 30 minutes
        for (int i = 0; i < 10; i++) // Will show for 5 hours total
        {
            Debug.LogWarning($"⚠️ IMPORTANT: {tokenType} token needs manual refresh! Please update in the Inspector. ⚠️");
            
            // Wait 30 minutes before showing again
            yield return new WaitForSeconds(30 * 60);
        }
    }
    
    /// <summary>
    /// Set a new Twitch OAuth token and update its expiry time
    /// </summary>
    public void SetTwitchOAuth(string newToken, float lifetimeHours = 0)
    {
        if (string.IsNullOrEmpty(newToken)) return;
        
        // Store the token in environment variable
        Environment.SetEnvironmentVariable("TWITCH_OAUTH_TOKEN", newToken);
        
        // Update expiry time
        float tokenLifetime = lifetimeHours > 0 ? lifetimeHours : defaultTwitchTokenLifetimeHours;
        twitchTokenExpiryTime = DateTime.UtcNow.AddHours(tokenLifetime);
        
        // Save expiry times
        SaveTokenExpiryTimes();
        
        Debug.Log($"Updated Twitch OAuth token. Expires: {twitchTokenExpiryTime}");
    }
    
    /// <summary>
    /// Set a new StreamElements OAuth token and update its expiry time
    /// </summary>
    public void SetStreamElementsOAuth(string newAccessToken, string newRefreshToken, float lifetimeHours = 0)
    {
        if (string.IsNullOrEmpty(newAccessToken) || string.IsNullOrEmpty(newRefreshToken)) return;
        
        // Store the tokens in environment variables
        Environment.SetEnvironmentVariable("STREAMELEMENTS_ACCESS_TOKEN", newAccessToken);
        Environment.SetEnvironmentVariable("STREAMELEMENTS_REFRESH_TOKEN", newRefreshToken);
        
        // Update expiry time
        float tokenLifetime = lifetimeHours > 0 ? lifetimeHours : defaultStreamElementsTokenLifetimeHours;
        streamElementsTokenExpiryTime = DateTime.UtcNow.AddHours(tokenLifetime);
        
        // Save expiry times
        SaveTokenExpiryTimes();
        
        Debug.Log($"Updated StreamElements OAuth token. Expires: {streamElementsTokenExpiryTime}");
    }
    
    /// <summary>
    /// Save token expiry times to persistent storage
    /// </summary>
    private void SaveTokenExpiryTimes()
    {
        try
        {
            string data = JsonUtility.ToJson(new TokenExpiryData {
                TwitchExpiryTicks = twitchTokenExpiryTime.Ticks,
                StreamElementsExpiryTicks = streamElementsTokenExpiryTime.Ticks
            });
            
            string filePath = Path.Combine(Application.persistentDataPath, "token_expiry.json");
            File.WriteAllText(filePath, data);
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error saving token expiry times: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Load token expiry times from persistent storage
    /// </summary>
    private void LoadTokenExpiryTimes()
    {
        try
        {
            string filePath = Path.Combine(Application.persistentDataPath, "token_expiry.json");
            if (File.Exists(filePath))
            {
                string data = File.ReadAllText(filePath);
                TokenExpiryData expiryData = JsonUtility.FromJson<TokenExpiryData>(data);
                
                if (expiryData != null)
                {
                    twitchTokenExpiryTime = new DateTime(expiryData.TwitchExpiryTicks);
                    streamElementsTokenExpiryTime = new DateTime(expiryData.StreamElementsExpiryTicks);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error loading token expiry times: {ex.Message}");
        }
    }
    
    [Serializable]
    private class TokenExpiryData
    {
        public long TwitchExpiryTicks;
        public long StreamElementsExpiryTicks;
    }

    /// <summary>
    /// Get the StreamElements JWT token (legacy authentication)
    /// </summary>
    public string GetStreamElementsJwtToken()
    {
        return streamElementsJwtToken;
    }
    
    /// <summary>
    /// Set a new StreamElements JWT token (legacy authentication)
    /// </summary>
    /// <param name="newToken">The new JWT token</param>
    public void SetStreamElementsJwtToken(string newToken)
    {
        if (string.IsNullOrEmpty(newToken)) return;
        
        streamElementsJwtToken = newToken;
        
        // Store in environment variable for persistence and for other components to access
        Environment.SetEnvironmentVariable("STREAMELEMENTS_JWT_TOKEN", newToken);
        
        SaveTokens();
        
        Debug.Log("StreamElements JWT token updated");
    }
} 