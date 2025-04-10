using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using System.Text;
using System.Linq;
#if UNITY_2018_4_OR_NEWER
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
#endif
using Shared.Core;

public class StreamElementsAPI : MonoBehaviour
{
    [Header("Authentication Method")]
    [SerializeField, Tooltip("Use JWT token instead of OAuth (temporary until OAuth setup is complete)")]
    private bool useJwtToken = true;
    
    // OAuth token (fetched from TokenManager)
    private string accessToken;
    
    // Reference to TokenManager
    private TokenManager tokenManager;
    
    [Header("Rate Limiting")]
    [SerializeField, Tooltip("Maximum requests per minute to avoid API rate limits")]
    private int maxRequestsPerMinute = 30;
    
    [SerializeField, Tooltip("Error recovery delay when rate limited (seconds)")]
    private float rateLimitRecoveryDelay = 60f;
    
    [Header("Request Handling")]
    [SerializeField, Tooltip("Retry requests on network errors")]
    private bool retryFailedRequests = true;
    
    [Header("API Settings")]
    [SerializeField, Tooltip("Initial time to wait before retry (seconds)")]
    private float initialRetryDelay = 2f;
    
    [SerializeField, Tooltip("Maximum delay between retries (seconds)")]
    private float maxRetryDelay = 60f;
    
    [SerializeField, Tooltip("Factor to increase backoff time by after each retry")]
    private float backoffMultiplier = 1.5f;
    
    [SerializeField, Tooltip("Maximum number of retry attempts")]
    private int maxRetryAttempts = 5;
    
    [SerializeField, Tooltip("Network request timeout in seconds")]
    private float requestTimeout = 10f;
    
    [SerializeField, Tooltip("Time after which a request is considered stale (minutes)")]
    private float requestStaleTime = 10f;
    
    [SerializeField, Tooltip("Interval between queue cleanups (seconds)")]
    private float queueCleanupInterval = 60f;
    
    // API endpoint URLs
    private const string API_BASE = "https://api.streamelements.com/kappa/v2";
    private string POINTS_URL => $"{API_BASE}/points/{GetChannelId()}/";
    
    // Rate limiting tracking
    private Queue<float> requestTimestamps = new Queue<float>();
    private bool isRateLimited = false;
    private float rateLimitEndTime = 0f;
    
    // API request queue for rate limiting
    private Queue<PendingRequest> pendingRequests = new Queue<PendingRequest>();
    private bool isProcessingQueue = false;
    private float lastQueueCleanupTime = 0f;
    
    // Structure to track pending requests
    private class PendingRequest
    {
        public string Username;
        public int Amount;
        public Action<bool> Callback;
        public int RetryCount = 0;
        public float CreationTime;
        
        public PendingRequest(string username, int amount, Action<bool> callback = null)
        {
            Username = username;
            Amount = amount;
            Callback = callback;
            CreationTime = Time.time;
        }

        public bool IsStale(float staleTimeInSeconds)
        {
            return (Time.time - CreationTime) > staleTimeInSeconds;
        }
    }
    
    private void Awake()
    {
        // Find and store TokenManager reference
        tokenManager = FindAnyObjectByType<TokenManager>();
        if (tokenManager == null)
        {
            Debug.LogError("StreamElementsAPI could not find a TokenManager in the scene!");
        }
        else
        {
            // Get OAuth access token if using OAuth
            if (!useJwtToken)
            {
                accessToken = tokenManager.GetStreamElementsAccessToken();
            }
        }
    }
    
    private void Update()
    {
        // Process the request queue if we have pending requests
        if (pendingRequests.Count > 0 && !isProcessingQueue)
        {
            StartCoroutine(ProcessRequestQueue());
        }
        
        // Clean up old timestamps outside the rate limit window
        CleanupOldRequestTimestamps();
        
        // Periodically clean up stale requests from the queue
        if (Time.time - lastQueueCleanupTime > queueCleanupInterval)
        {
            CleanupStaleRequests();
            lastQueueCleanupTime = Time.time;
        }
    }
    
    private void OnDisable()
    {
        // Gracefully handle any pending requests
        if (pendingRequests.Count > 0)
        {
            Debug.LogWarning($"[StreamElementsAPI] {pendingRequests.Count} pending point requests were cancelled on disable.");
            StopAllCoroutines();
        }
    }
    
    /// <summary>
    /// Get the channel ID from TokenManager
    /// </summary>
    private string GetChannelId()
    {
        if (tokenManager == null)
        {
            Debug.LogError("No TokenManager reference!");
            return string.Empty;
        }
        return tokenManager.GetStreamElementsChannelId();
    }
    
    /// <summary>
    /// Get the JWT token from TokenManager
    /// </summary>
    private string GetJwtToken()
    {
        if (tokenManager == null)
        {
            Debug.LogError("No TokenManager reference!");
            return string.Empty;
        }
        return tokenManager.GetStreamElementsJwtToken();
    }
    
    /// <summary>
    /// Award points to a user through StreamElements
    /// </summary>
    /// <param name="username">The Twitch username</param>
    /// <param name="amount">The amount of points to award</param>
    /// <returns>True if the request was queued successfully</returns>
    public bool AwardPoints(string username, int amount, Action<bool> callback = null)
    {
        if (string.IsNullOrEmpty(username) || amount <= 0)
        {
            Debug.LogWarning($"Invalid award request: {username}, {amount}");
            callback?.Invoke(false);
            return false;
        }
        
        // Check if we need to refresh the token first (only if using OAuth)
        if (!useJwtToken)
        {
            CheckAndRefreshToken();
        }
        
        // Add to the pending request queue
        pendingRequests.Enqueue(new PendingRequest(username, amount, callback));
        
        // Return true to indicate the request was queued
        return true;
    }
    
    /// <summary>
    /// Check if the token needs to be refreshed and request refresh if needed
    /// </summary>
    private void CheckAndRefreshToken()
    {
        // Only relevant for OAuth, not JWT
        if (useJwtToken) return;
        
        // Use TokenManager to check tokens
        if (tokenManager != null)
        {
            // Trigger a token check - it will refresh automatically if needed
            tokenManager.CheckAndRefreshTokens();
            
            // Get the latest token
            string latestToken = tokenManager.GetStreamElementsAccessToken();
            if (!string.IsNullOrEmpty(latestToken) && latestToken != accessToken)
            {
                Debug.Log("Updating StreamElements access token in API");
                accessToken = latestToken;
            }
        }
    }
    
    /// <summary>
    /// Clean up stale requests from the queue
    /// </summary>
    private void CleanupStaleRequests()
    {
        if (pendingRequests.Count == 0 || isProcessingQueue)
        {
            return; // Don't clean if empty or currently processing
        }
        
        int initialCount = pendingRequests.Count;
        float staleTimeInSeconds = requestStaleTime * 60f; // Convert minutes to seconds
        
        // Create a new queue and only keep non-stale requests
        Queue<PendingRequest> freshRequests = new Queue<PendingRequest>();
        List<PendingRequest> staleRequests = new List<PendingRequest>();
        
        // Identify stale requests
        while (pendingRequests.Count > 0)
        {
            PendingRequest request = pendingRequests.Dequeue();
            if (request.IsStale(staleTimeInSeconds))
            {
                staleRequests.Add(request);
            }
            else
            {
                freshRequests.Enqueue(request);
            }
        }
        
        // Process stale requests - notify callbacks of failure
        foreach (var staleRequest in staleRequests)
        {
            try
            {
                staleRequest.Callback?.Invoke(false);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[StreamElementsAPI] Error invoking callback for stale request: {ex.Message}");
            }
        }
        
        // Replace the queue with the fresh requests
        pendingRequests = freshRequests;
        
        // Log the cleanup if any requests were removed
        int removedCount = initialCount - pendingRequests.Count;
        if (removedCount > 0)
        {
            Debug.LogWarning($"[StreamElementsAPI] Cleaned up {removedCount} stale requests from queue after {requestStaleTime} minutes.");
        }
    }
    
    /// <summary>
    /// Process the queue of pending requests with rate limiting
    /// </summary>
    private IEnumerator ProcessRequestQueue()
    {
        isProcessingQueue = true;
        
        while (pendingRequests.Count > 0)
        {
            // Check if we're rate limited
            if (isRateLimited)
            {
                if (Time.time < rateLimitEndTime)
                {
                    // Still in the rate limit cooldown period
                    float remainingTime = rateLimitEndTime - Time.time;
                    Debug.LogWarning($"Rate limited. Waiting {remainingTime:F1} seconds before retrying.");
                    yield return new WaitForSeconds(Mathf.Min(5f, remainingTime)); // Wait but check again every 5 seconds at most
                    continue;
                }
                else
                {
                    // Rate limit period is over
                    isRateLimited = false;
                    Debug.Log("Rate limit cooldown completed. Resuming requests.");
                }
            }
            
            // Check if we can make a request without exceeding rate limits
            if (CanMakeRequest())
            {
                var request = pendingRequests.Dequeue();
                
                // Track this request timestamp
                requestTimestamps.Enqueue(Time.time);
                
                // Make the actual API call
                bool success = false;
                Exception apiException = null;
                
                // We'll store the coroutine reference first
                IEnumerator pointsCoroutine = null;
                
                try
                {
                    // Create a coroutine reference without starting it
                    pointsCoroutine = AwardPointsCoroutine(request.Username, request.Amount, (result) => {
                        success = result;
                    });
                }
                catch (Exception ex)
                {
                    apiException = ex;
                    Debug.LogError($"[StreamElementsAPI] Exception during point award: {ex.Message}");
                    success = false;
                }
                
                // Only yield if we successfully created the coroutine
                if (pointsCoroutine != null)
                {
                    yield return pointsCoroutine;
                }
                
                if (!success)
                {
                    // If the request failed but wasn't due to rate limiting
                    if (!isRateLimited && retryFailedRequests && request.RetryCount < maxRetryAttempts)
                    {
                        // Increment retry count and re-queue with exponential backoff
                        request.RetryCount++;
                        float backoffDelay = initialRetryDelay * Mathf.Pow(2, request.RetryCount - 1);
                        Debug.LogWarning($"[StreamElementsAPI] Retrying point award to {request.Username} (attempt {request.RetryCount}/{maxRetryAttempts}) after {backoffDelay:F1}s");
                        
                        // Wait before retrying
                        yield return new WaitForSeconds(backoffDelay);
                        pendingRequests.Enqueue(request);
                    }
                    else if (isRateLimited)
                    {
                        // Rate limited - re-queue without incrementing retry count
                        pendingRequests.Enqueue(request);
                    }
                    else
                    {
                        // Final failure after all retries
                        Debug.LogError($"[StreamElementsAPI] Failed to award points to {request.Username} after {request.RetryCount} retries" + 
                            (apiException != null ? $": {apiException.Message}" : ""));
                        request.Callback?.Invoke(false);
                    }
                }
                else
                {
                    // Success
                    request.Callback?.Invoke(true);
                }
                
                // Small delay between requests
                yield return new WaitForSeconds(0.1f);
            }
            else
            {
                // Wait until we can make another request
                float waitTime = GetTimeUntilNextRequestAllowed();
                yield return new WaitForSeconds(waitTime);
            }
        }
        
        isProcessingQueue = false;
    }
    
    /// <summary>
    /// Actual coroutine to award points to a user
    /// </summary>
    private IEnumerator AwardPointsCoroutine(string username, int amount, Action<bool> callback = null)
    {
        string channelId = GetChannelId();
        string currentToken = useJwtToken ? GetJwtToken() : accessToken;
        
        // Check if we have the required credentials based on auth method
        if (string.IsNullOrEmpty(currentToken) || string.IsNullOrEmpty(channelId))
        {
            Debug.LogError("StreamElements API not configured. Missing " + 
                (useJwtToken ? "JWT token" : "OAuth token") + " or channel ID.");
            callback?.Invoke(false);
            yield break;
        }
        
        // Construct the URL for the API endpoint
        string url = $"{POINTS_URL}{username}/{amount}";
        
        // Create a UnityWebRequest to put the points
        UnityWebRequest request = null;
        bool success = false;
        bool requestCreateFailed = false;
        
        try
        {
            request = new UnityWebRequest(url, "PUT");
            request.downloadHandler = new DownloadHandlerBuffer();
            
            // Add authorization header using the current token
            request.SetRequestHeader("Authorization", "Bearer " + currentToken);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[StreamElementsAPI] Exception creating web request: {ex.Message}");
            requestCreateFailed = true;
        }
        
        // Handle early exit without using yield in catch block
        if (requestCreateFailed)
        {
            callback?.Invoke(false);
            yield break;
        }
        
        // Send the request outside of try/catch
        yield return request.SendWebRequest();
        
        try
        {
            if (request.result == UnityWebRequest.Result.Success)
            {
                Debug.Log($"Successfully awarded {amount} points to {username}");
                success = true;
            }
            else
            {
                // Check for rate limiting response
                if (request.responseCode == 429) // Too Many Requests
                {
                    Debug.LogWarning("StreamElements API rate limit hit. Enforcing cooldown period.");
                    isRateLimited = true;
                    rateLimitEndTime = Time.time + rateLimitRecoveryDelay;
                }
                else
                {
                    Debug.LogError($"Error awarding points: {request.error} - {request.downloadHandler.text}");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[StreamElementsAPI] Exception processing web request results: {ex.Message}");
        }
        finally
        {
            // Always dispose of the request to prevent memory leaks
            if (request != null)
            {
                request.Dispose();
            }
        }
        
        callback?.Invoke(success);
    }
    
    /// <summary>
    /// Check if we can make a new request without exceeding rate limits
    /// </summary>
    private bool CanMakeRequest()
    {
        CleanupOldRequestTimestamps();
        return requestTimestamps.Count < maxRequestsPerMinute;
    }
    
    /// <summary>
    /// Get the time until the next request would be allowed
    /// </summary>
    private float GetTimeUntilNextRequestAllowed()
    {
        if (requestTimestamps.Count == 0 || requestTimestamps.Count < maxRequestsPerMinute)
        {
            return 0f; // Can make a request immediately
        }
        
        // Get the oldest timestamp in the queue
        float oldestTimestamp = requestTimestamps.Peek();
        
        // Calculate how long until it's outside the 1-minute window
        float timeUntilAllowed = (oldestTimestamp + 60f) - Time.time;
        
        return Mathf.Max(0.1f, timeUntilAllowed);
    }
    
    /// <summary>
    /// Remove timestamps older than 1 minute from the tracking queue
    /// </summary>
    private void CleanupOldRequestTimestamps()
    {
        float oneMinuteAgo = Time.time - 60f;
        
        while (requestTimestamps.Count > 0 && requestTimestamps.Peek() < oneMinuteAgo)
        {
            requestTimestamps.Dequeue();
        }
    }
    
    /// <summary>
    /// Switch between JWT and OAuth authentication methods
    /// </summary>
    /// <param name="useJwt">Whether to use JWT (true) or OAuth (false)</param>
    public void SetAuthenticationMethod(bool useJwt)
    {
        useJwtToken = useJwt;
        
        // If switching to OAuth, get the current token
        if (!useJwt && tokenManager != null)
        {
            accessToken = tokenManager.GetStreamElementsAccessToken();
        }
        
        Debug.Log($"StreamElements API now using {(useJwt ? "JWT" : "OAuth")} authentication");
    }
    
    /// <summary>
    /// Make API request with automatic retry and backoff
    /// </summary>
    /// <param name="endpoint">API endpoint (without base URL)</param>
    /// <param name="method">HTTP method (GET, POST, etc.)</param>
    /// <param name="onSuccess">Callback for successful response</param>
    /// <param name="onError">Callback for error response</param>
    /// <param name="bodyData">Optional body data for POST requests</param>
    /// <param name="headers">Optional additional headers</param>
    public void MakeAPIRequest(string endpoint, string method, Action<string> onSuccess, Action<string> onError, 
        Dictionary<string, string> bodyData = null, Dictionary<string, string> headers = null)
    {
        // Use coroutine for retry logic
        StartCoroutine(MakeAPIRequestWithRetry(endpoint, method, onSuccess, onError, bodyData, headers));
    }
    
    /// <summary>
    /// Make API request with automatic retry and exponential backoff
    /// </summary>
    private IEnumerator MakeAPIRequestWithRetry(string endpoint, string method, Action<string> onSuccess, Action<string> onError, 
        Dictionary<string, string> bodyData = null, Dictionary<string, string> headers = null)
    {
        int attempts = 0;
        float currentDelay = initialRetryDelay;
        bool requestSuccessful = false;
        string lastErrorMessage = string.Empty;
        
        while (!requestSuccessful && attempts < maxRetryAttempts)
        {
            attempts++;
            
            // Log retry attempt if not the first one
            if (attempts > 1)
            {
                Debug.Log($"API request retry attempt {attempts}/{maxRetryAttempts} after {currentDelay:F1}s delay");
            }
            
            // Construct full URL
            string url = $"{API_BASE}/{endpoint}";
            UnityWebRequest request = null;
            
            try
            {
                // Create request based on method
                if (method.Equals("GET", StringComparison.OrdinalIgnoreCase))
                {
                    // Append query parameters if provided
                    if (bodyData != null && bodyData.Count > 0)
                    {
                        StringBuilder queryString = new StringBuilder("?");
                        bool first = true;
                        
                        foreach (var kvp in bodyData)
                        {
                            if (!first) queryString.Append("&");
                            queryString.Append(UnityWebRequest.EscapeURL(kvp.Key));
                            queryString.Append("=");
                            queryString.Append(UnityWebRequest.EscapeURL(kvp.Value));
                            first = false;
                        }
                        
                        url += queryString.ToString();
                    }
                    
                    request = UnityWebRequest.Get(url);
                }
                else if (method.Equals("POST", StringComparison.OrdinalIgnoreCase))
                {
                    request = UnityWebRequest.Post(url, bodyData ?? new Dictionary<string, string>());
                    
                    // For JSON content
                    if (bodyData != null && bodyData.ContainsKey("_json"))
                    {
                        string jsonData = bodyData["_json"];
                        byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonData);
                        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                        request.SetRequestHeader("Content-Type", "application/json");
                    }
                }
                else
                {
                    // Other methods (PUT, DELETE, etc) would go here
                    Debug.LogError($"Unsupported method: {method}");
                    if (onError != null) onError($"Unsupported method: {method}");
                    yield break;
                }
                
                // Set timeout
                request.timeout = Mathf.RoundToInt(requestTimeout);
                
                // Add JWT token authorization header if available
                if (!string.IsNullOrEmpty(accessToken))
                {
                    request.SetRequestHeader("Authorization", $"Bearer {accessToken}");
                }
                
                // Add any additional headers
                if (headers != null)
                {
                    foreach (var header in headers)
                    {
                        request.SetRequestHeader(header.Key, header.Value);
                    }
                }
                
                // Send the request
                yield return request.SendWebRequest();
                
                // Parse response
                if (request.result == UnityWebRequest.Result.Success)
                {
                    requestSuccessful = true;
                    string responseText = request.downloadHandler.text;
                    Debug.Log($"API request successful: {url}");
                    
                    if (onSuccess != null)
                    {
                        onSuccess(responseText);
                    }
                }
                else
                {
                    // Request failed
                    string errorMessage = request.error;
                    int statusCode = (int)request.responseCode;
                    
                    // Don't retry for some status codes (like 401/403)
                    if (statusCode == 401 || statusCode == 403 || statusCode == 404)
                    {
                        Debug.LogError($"API request error (will not retry): {url}, Status: {statusCode}, Error: {errorMessage}");
                        
                        if (onError != null)
                        {
                            onError($"Error {statusCode}: {errorMessage}");
                        }
                        
                        yield break;
                    }
                    
                    // Log the error but continue for retry
                    lastErrorMessage = $"Error {statusCode}: {errorMessage}";
                    Debug.LogWarning($"API request attempt {attempts} failed: {url}, Status: {statusCode}, Error: {errorMessage}");
                }
            }
            catch (Exception ex)
            {
                // Log unexpected exceptions
                lastErrorMessage = $"Exception: {ex.Message}";
                Debug.LogWarning($"API request exception on attempt {attempts}: {ex.Message}");
            }
            finally
            {
                // Ensure request is properly disposed to prevent memory leaks
                if (request != null)
                {
                    request.Dispose();
                    request = null;
                }
            }
            
            // If succeeded, exit the retry loop
            if (requestSuccessful) break;
            
            // Wait before next retry with exponential backoff
            if (attempts < maxRetryAttempts)
            {
                yield return new WaitForSeconds(currentDelay);
                currentDelay = Mathf.Min(currentDelay * backoffMultiplier, maxRetryDelay);
            }
        }
        
        // If we exhausted all retries, call the error callback
        if (!requestSuccessful && onError != null)
        {
            Debug.LogError($"API request failed after {maxRetryAttempts} attempts: {endpoint}");
            onError($"Request failed after {maxRetryAttempts} attempts. Last error: {lastErrorMessage}");
        }
    }
}