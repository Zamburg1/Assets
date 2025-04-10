using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using System.Text;
using System.Linq;
using Shared.Trivia;

public class QuestionLoader : MonoBehaviour
{
    [Header("API Settings")]
    [SerializeField, Tooltip("Base URL for Open Trivia DB API")]
    private string apiBaseUrl = "https://opentdb.com/api.php";
    
    [SerializeField, Tooltip("Number of questions to cache (for quick access)")]
    private int maxCacheSize = 10;
    
    [SerializeField, Tooltip("Difficulty level (easy, medium, hard, or leave empty for any)")]
    private string difficulty = "";
    
    [Header("Error Handling")]
    [SerializeField, Tooltip("Initial delay before retrying a failed request (in seconds)")]
    private float initialRetryDelay = 2f;
    
    [SerializeField, Tooltip("Maximum retry delay (in seconds)")]
    private float maxRetryDelay = 30f;
    
    [SerializeField, Tooltip("Backoff factor for each retry attempt")]
    private float retryBackoffFactor = 2f;
    
    [SerializeField, Tooltip("Maximum number of retry attempts")]
    private int maxRetryAttempts = 3;
    
    [SerializeField, Tooltip("Load backup questions if API fails repeatedly")]
    private bool useBackupQuestionsOnFailure = true;
    
    [SerializeField, Tooltip("Maximum time to wait for a question in seconds")]
    private float maxQuestionWaitTime = 5f;
    
    [SerializeField, Tooltip("Minimum number of questions to cache")]
    private int minCacheSize = 3;
    
    [SerializeField, Tooltip("Whether to adjust cache size based on system memory")]
    private bool useAdaptiveCacheSize = true;
    
    // Cache of pre-loaded questions to use when needed
    private Queue<TriviaQuestion> questionCache = new Queue<TriviaQuestion>();
    
    // Base64 encoding requirement to avoid special character issues
    private const string ENCODING = "base64";
    
    // Flag to track if we're currently loading a batch
    private bool isLoadingBatch = false;
    
    // Track number of consecutive failures
    private int consecutiveFailures = 0;
    
    // Cache control
    private bool preloadingEnabled = true;
    
    // Keep track of recently used questions to avoid duplicates
    private HashSet<string> recentlyUsedQuestions = new HashSet<string>();
    private int maxRecentQuestions = 100; // Remember the last 100 questions
    
    // Add this near the top of the class
    private List<Coroutine> activeCoroutines = new List<Coroutine>();
    
    // Add these fields
    private bool isApplicationFocused = true;
    private bool preloadingPaused = false;
    
    // Add this field for reusing StringBuilder instances
    private StringBuilder logBuilder = new StringBuilder(128);
    
    // Add an enum for error types
    private enum ApiErrorType
    {
        None,
        NetworkError,
        ServerError,
        Timeout,
        ParseError,
        Unknown
    }
    
    // Add a helper method for formatting log messages
    private string FormatLogMessage(string format, params object[] args)
    {
        logBuilder.Length = 0;
        logBuilder.AppendFormat(format, args);
        return logBuilder.ToString();
    }
    
    private void Start()
    {
        // Determine the optimal cache size based on system resources
        DetermineOptimalCacheSize();
        
        // Preload an initial batch of questions if caching is enabled
        if (preloadingEnabled && maxCacheSize > 0)
        {
            TrackCoroutine(StartCoroutine(PreloadQuestions()));
        }
    }
    
    /// <summary>
    /// Preload questions to fill the cache
    /// </summary>
    private IEnumerator PreloadQuestions()
    {
        Debug.Log("Starting preload of trivia questions...");
        while (questionCache.Count < maxCacheSize)
        {
            // Don't preload if the application is in the background
            if (!isApplicationFocused)
            {
                preloadingPaused = true;
                yield break;
            }
            
            if (!isLoadingBatch)
            {
                // Fetch one question at a time to avoid API overload
                yield return LoadQuestionBatch(1);
            }
            
            // Wait a bit before checking cache size again
            yield return new WaitForSeconds(1f);
        }
        Debug.Log(FormatLogMessage("Preloaded {0} trivia questions", questionCache.Count));
    }
    
    /// <summary>
    /// Gets a trivia question, either from cache or by loading from API
    /// </summary>
    /// <param name="onQuestionLoaded">Callback that receives the loaded question</param>
    public void LoadQuestion(Action<TriviaQuestion> onQuestionLoaded)
    {
        TrackCoroutine(StartCoroutine(GetQuestionCoroutine(onQuestionLoaded)));
    }
    
    /// <summary>
    /// Gets a single question, using cache if available or loading from API if needed
    /// </summary>
    private IEnumerator GetQuestionCoroutine(Action<TriviaQuestion> onQuestionLoaded)
    {
        // If we have cached questions, use one
        if (questionCache.Count > 0)
        {
            TriviaQuestion question = questionCache.Dequeue();
            
            // Track this question as used
            TrackUsedQuestion(question.Question);
            
            onQuestionLoaded(question);
            
            // If cache is getting low, load more in the background
            if (preloadingEnabled && questionCache.Count < maxCacheSize / 2 && !isLoadingBatch)
            {
                TrackCoroutine(StartCoroutine(LoadQuestionBatch(1)));
            }
            
            yield break;
        }
        
        // Set up timeout tracking
        float startTime = Time.time;
        bool questionLoaded = false;
        
        // Start loading a question
        TrackCoroutine(StartCoroutine(LoadQuestionBatch(1, q => {
            questionLoaded = true;
            onQuestionLoaded(q);
        })));
        
        // Wait for the question to load or timeout
        while (!questionLoaded && Time.time - startTime < maxQuestionWaitTime)
        {
            yield return null;
        }
        
        // If we timed out, use a backup question
        if (!questionLoaded)
        {
            Debug.LogWarning($"Timed out waiting for question after {maxQuestionWaitTime} seconds. Using backup question.");
            onQuestionLoaded(GetBackupQuestion());
        }
        
        // Start refilling the cache in the background if enabled
        if (preloadingEnabled && !isLoadingBatch)
        {
            TrackCoroutine(StartCoroutine(PreloadQuestions()));
        }
    }
    
    /// <summary>
    /// Load a batch of questions to cache
    /// </summary>
    private IEnumerator LoadQuestionBatch(int batchSize = 1, Action<TriviaQuestion> onFirstQuestionLoaded = null)
    {
        if (isLoadingBatch)
        {
            Debug.LogWarning("Attempted to load questions while another batch is loading, ignoring request.");
                yield break;
        }
        
        isLoadingBatch = true;
        
        // Construct URL with appropriate batch size
        string url = ConstructApiUrl(batchSize);
        Debug.Log(FormatLogMessage("Loading {0} trivia question(s) from API...", batchSize));
        
        // Start with initial retry delay
        float currentRetryDelay = initialRetryDelay;
        int attempt = 0;
        bool succeeded = false;
        
        while (!succeeded && attempt < maxRetryAttempts)
        {
            if (attempt > 0)
            {
                Debug.Log(FormatLogMessage("Retry attempt {0}/{1} after {2:F1}s delay", attempt, maxRetryAttempts, currentRetryDelay));
                yield return new WaitForSeconds(currentRetryDelay);
                
                // Apply exponential backoff for next attempt
                currentRetryDelay = Mathf.Min(currentRetryDelay * retryBackoffFactor, maxRetryDelay);
            }
            
            attempt++;
            
            UnityWebRequest webRequest = null;
            
            try
            {
                webRequest = UnityWebRequest.Get(url);
                
                // Set timeout (10 seconds is usually reasonable)
                webRequest.timeout = 10;
                
                // Send the request
                yield return webRequest.SendWebRequest();
                
                if (webRequest.result == UnityWebRequest.Result.Success)
                {
                    string jsonResult = webRequest.downloadHandler.text;
                    List<TriviaQuestion> loadedQuestions = ProcessJsonResult(jsonResult);
                    
                    if (loadedQuestions != null && loadedQuestions.Count > 0)
                    {
                        // If a callback was provided for the first question, handle it separately
                        if (onFirstQuestionLoaded != null && loadedQuestions.Count > 0)
                        {
                            // Send the first loaded question directly without caching it first
                            TriviaQuestion firstQuestion = loadedQuestions[0];
                            onFirstQuestionLoaded(firstQuestion);
                            
                            // We need to remove the first question from our loaded set
                            // so it doesn't also get added to the cache below
                            loadedQuestions.RemoveAt(0);
                        }
                        
                        // Now add remaining questions to cache
                        foreach (var question in loadedQuestions)
                        {
                            if (questionCache.Count < maxCacheSize)
                            {
                                questionCache.Enqueue(question);
                            }
                        }
                        
                        // Reset the failure counter on success
                        consecutiveFailures = 0;
                        succeeded = true;
                        
                        Debug.Log(FormatLogMessage("Successfully loaded {0} trivia question(s)", loadedQuestions.Count));
                    }
                    else
                    {
                        Debug.LogWarning("API returned success but no valid questions were parsed");
                        consecutiveFailures++;
                    }
                }
                else
                {
                    // Determine the type of error
                    ApiErrorType errorType = ApiErrorType.Unknown;
                    
                    if (webRequest.result == UnityWebRequest.Result.ConnectionError)
                    {
                        errorType = ApiErrorType.NetworkError;
                    }
                    else if (webRequest.result == UnityWebRequest.Result.ProtocolError)
                    {
                        errorType = ApiErrorType.ServerError;
                    }
                    else if (webRequest.result == UnityWebRequest.Result.DataProcessingError)
                    {
                        errorType = ApiErrorType.ParseError;
                    }
                    
                    HandleApiError(errorType, webRequest.error, webRequest);
                }
            }
            catch (System.Net.WebException webEx)
            {
                HandleApiError(ApiErrorType.NetworkError, webEx.Message, webRequest);
            }
            catch (TimeoutException timeoutEx)
            {
                HandleApiError(ApiErrorType.Timeout, timeoutEx.Message, webRequest);
            }
            catch (JsonException jsonEx)
            {
                HandleApiError(ApiErrorType.ParseError, jsonEx.Message, webRequest);
            }
            catch (Exception e)
            {
                HandleApiError(ApiErrorType.Unknown, e.Message, webRequest);
            }
            finally
            {
                // Always dispose the web request
                if (webRequest != null)
                {
                    webRequest.Dispose();
                }
            }
        }
        
        // If all attempts failed and we're using backup questions
        if (!succeeded && useBackupQuestionsOnFailure)
        {
            Debug.LogWarning(FormatLogMessage("Failed to load questions after {0} attempts with backoff. Using backup questions.", maxRetryAttempts));
            
            // Load backup questions
            LoadBackupQuestions();
            
            // If a callback was provided for the first question, invoke it with a backup
            if (onFirstQuestionLoaded != null && questionCache.Count > 0)
            {
                TriviaQuestion backupQuestion = questionCache.Dequeue();
                onFirstQuestionLoaded(backupQuestion);
            }
            else if (onFirstQuestionLoaded != null)
            {
                // If we have no cache at all, create a direct backup
                onFirstQuestionLoaded(GetBackupQuestion());
            }
        }
        
        isLoadingBatch = false;
    }
    
    /// <summary>
    /// Process JSON results into TriviaQuestion objects
    /// </summary>
    private List<TriviaQuestion> ProcessJsonResult(string jsonResult)
    {
        List<TriviaQuestion> questions = new List<TriviaQuestion>();
        
        try
        {
            // Parse the JSON response
            OpenTDBResponse response = JsonUtility.FromJson<OpenTDBResponse>(jsonResult);
            
            if (response == null || response.response_code != 0 || response.results == null || response.results.Count == 0)
            {
                Debug.LogError("API returned no results or error code: " + (response != null ? response.response_code.ToString() : "null response"));
                return null;
            }
            
            // Track how many questions we need to process
            int neededQuestions = Mathf.Min(maxCacheSize - questionCache.Count, response.results.Count);
            int processedQuestions = 0;
            
            // Process each question until we have enough
            foreach (OpenTDBQuestion result in response.results)
            {
                // Skip questions that don't have exactly 3 incorrect answers (for a total of 4 options)
                if (result.incorrect_answers.Count != 3)
                {
                    Debug.LogWarning(FormatLogMessage("Skipping question that doesn't have exactly 3 incorrect answers (has {0})", result.incorrect_answers.Count));
                    continue;
                }
                
                try
                {
                    // Convert Base64 strings to normal text
                    string question = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(result.question));
                    
                    // Skip if this is a duplicate question we've seen recently
                    if (IsDuplicateQuestion(question))
                    {
                        Debug.Log(FormatLogMessage("Skipping duplicate question: {0}", question));
                        continue;
                    }
                    
                    string correctAnswer = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(result.correct_answer));
                    string category = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(result.category));
                    string difficultyLevel = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(result.difficulty));
                    
                    // Create a list of answers with the correct one and all incorrect ones
                    List<string> answers = new List<string>();
                    answers.Add(correctAnswer);
                    
                    foreach (string base64Answer in result.incorrect_answers)
                    {
                        string decodedAnswer = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(base64Answer));
                        answers.Add(decodedAnswer);
                    }
                    
                    // Shuffle the answers
                    ShuffleAnswers(answers, out int correctIndex);
                    
                    // Create the question and add to result list
                    TriviaQuestion triviaQuestion = new TriviaQuestion(
                        question, 
                        answers, 
                        correctIndex, 
                        difficultyLevel, 
                        category
                    );
                    
                    questions.Add(triviaQuestion);
                    
                    // Stop processing once we have enough questions
                    processedQuestions++;
                    if (processedQuestions >= neededQuestions)
                    {
                        break;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"Error processing individual question: {e.Message}");
                    // Skip this question but continue processing others
                }
            }
            
            return questions;
        }
        catch (Exception e)
        {
            Debug.LogError($"Error parsing API response: {e.Message}");
            return null;
        }
    }
    
    /// <summary>
    /// Get a backup question when API fails
    /// </summary>
    private TriviaQuestion GetBackupQuestion()
    {
        // Create a simple backup question
        string[] backupCategories = { "General Knowledge", "Entertainment", "Science", "History", "Geography" };
        string category = backupCategories[UnityEngine.Random.Range(0, backupCategories.Length)];
        
        List<string> answers = new List<string>
        {
            "Paris",
            "London",
            "Berlin",
            "Rome"
        };
        
        return new TriviaQuestion(
            "What is the capital of France?",
                answers,
            0, // Paris is correct (index 0)
            "easy",
            "Geography"
        );
    }
    
    /// <summary>
    /// Load a set of backup questions in case the API fails
    /// </summary>
    private void LoadBackupQuestions()
    {
        // Only add backup questions if we need them
        int backupCount = Mathf.Max(1, maxCacheSize - questionCache.Count);
        int originalCount = questionCache.Count;
        
        // Geography backup
        questionCache.Enqueue(new TriviaQuestion(
            "What is the largest planet in our solar system?",
            new List<string> { "Jupiter", "Saturn", "Neptune", "Earth" },
            0,
            "easy",
            "Science"
        ));
        
        if (questionCache.Count < originalCount + backupCount)
        {
            questionCache.Enqueue(new TriviaQuestion(
                "Who painted the Mona Lisa?",
                new List<string> { "Leonardo da Vinci", "Michelangelo", "Pablo Picasso", "Vincent van Gogh" },
                0,
                "medium",
                "Art"
            ));
        }
        
        if (questionCache.Count < originalCount + backupCount)
        {
            questionCache.Enqueue(new TriviaQuestion(
                "Which of these is NOT a programming language?",
                new List<string> { "Bamboo", "Python", "Java", "Ruby" },
                0,
                "medium",
                "Technology"
            ));
        }
        
        if (questionCache.Count < originalCount + backupCount)
        {
            questionCache.Enqueue(new TriviaQuestion(
                "Which element has the chemical symbol 'Au'?",
                new List<string> { "Gold", "Silver", "Aluminum", "Copper" },
                0,
                "medium",
                "Science"
            ));
        }
        
        if (questionCache.Count < originalCount + backupCount)
        {
            questionCache.Enqueue(new TriviaQuestion(
                "What year did the Titanic sink?",
                new List<string> { "1912", "1905", "1921", "1898" },
                0,
                "medium",
                "History"
            ));
        }
        
        Debug.Log(FormatLogMessage("Loaded {0} backup questions into cache", questionCache.Count - originalCount));
    }
    
    /// <summary>
    /// Construct the API URL with appropriate parameters
    /// </summary>
    private string ConstructApiUrl(int amount)
    {
        string url = $"{apiBaseUrl}?amount={amount}&type=multiple&encode={ENCODING}";
        
        // Add difficulty if specified
        if (!string.IsNullOrEmpty(difficulty))
        {
            url += $"&difficulty={difficulty.ToLower()}";
        }
        
        return url;
    }
    
    private void ShuffleAnswers(List<string> answers, out int correctIndex)
    {
        string correctAnswer = answers[0];
        
        // Fisher-Yates shuffle
        for (int i = 0; i < answers.Count - 1; i++)
        {
            int randomIndex = UnityEngine.Random.Range(i, answers.Count);
            string temp = answers[i];
            answers[i] = answers[randomIndex];
            answers[randomIndex] = temp;
        }
        
        // Find new position of correct answer
        correctIndex = answers.IndexOf(correctAnswer);
    }
    
    // Add this new method to check for duplicate questions
    private bool IsDuplicateQuestion(string question)
    {
        return recentlyUsedQuestions.Contains(question);
    }
    
    // Add this method to track a used question
    private void TrackUsedQuestion(string question)
    {
        // Add to recently used set
        recentlyUsedQuestions.Add(question);
        
        // If we've exceeded the max size, remove the oldest entries
        // For simplicity, we'll just clear everything if we hit the limit
        if (recentlyUsedQuestions.Count > maxRecentQuestions)
        {
            Debug.Log(FormatLogMessage("Clearing recently used questions cache (had {0} questions)", recentlyUsedQuestions.Count));
            recentlyUsedQuestions.Clear();
        }
    }
    
    // Add these methods to clean up coroutines
    private void OnDisable()
    {
        StopAllCoroutines();
        activeCoroutines.Clear();
        isLoadingBatch = false;
    }

    private void OnDestroy()
    {
        StopAllCoroutines();
        activeCoroutines.Clear();
        isLoadingBatch = false;
    }

    // Modify the TrackCoroutine method to add cleanup
    private void TrackCoroutine(Coroutine coroutine)
    {
        if (coroutine != null)
        {
            activeCoroutines.Add(coroutine);
            
            // Start a cleanup coroutine to remove it when done
            StartCoroutine(CleanupCoroutineWhenDone(coroutine));
        }
    }
    
    // Add this method to clean up completed coroutines
    private IEnumerator CleanupCoroutineWhenDone(Coroutine coroutine)
    {
        // Wait until the next frame to see if the coroutine is still running
        yield return null;
        
        // Wait until the coroutine is done
        yield return coroutine;
        
        // Remove from the tracking list
        if (activeCoroutines.Contains(coroutine))
        {
            activeCoroutines.Remove(coroutine);
        }
    }
    
    // Add these methods to handle application focus
    private void OnApplicationFocus(bool hasFocus)
    {
        isApplicationFocused = hasFocus;
        
        if (hasFocus && preloadingPaused)
        {
            // Resume preloading
            preloadingPaused = false;
            
            if (preloadingEnabled && questionCache.Count < maxCacheSize && !isLoadingBatch)
            {
                TrackCoroutine(StartCoroutine(PreloadQuestions()));
            }
        }
    }

    private void OnApplicationPause(bool isPaused)
    {
        // Treat pause the same as losing focus
        isApplicationFocused = !isPaused;
        
        if (isPaused && !preloadingPaused)
        {
            // Mark preloading as paused
            preloadingPaused = true;
        }
    }
    
    // Add a method to handle different error types differently
    private void HandleApiError(ApiErrorType errorType, string errorDetails, UnityWebRequest request = null)
    {
        consecutiveFailures++;
        
        switch (errorType)
        {
            case ApiErrorType.NetworkError:
                Debug.LogWarning(FormatLogMessage("Network error: {0}. Will retry with exponential backoff.", errorDetails));
                // Network errors are usually transient - we can retry with normal backoff
                break;
            
            case ApiErrorType.ServerError:
                int statusCode = request != null ? (int)request.responseCode : 0;
                Debug.LogError(FormatLogMessage("Server error ({0}): {1}. Will retry with longer delay.", statusCode, errorDetails));
                // Server errors might need longer backoff
                break;
            
            case ApiErrorType.Timeout:
                Debug.LogWarning(FormatLogMessage("Request timed out: {0}. Will retry.", errorDetails));
                // Timeouts might indicate server load - use normal backoff
                break;
            
            case ApiErrorType.ParseError:
                Debug.LogError(FormatLogMessage("Parse error: {0}. This might indicate an API change.", errorDetails));
                // Parse errors are likely persistent - use backup questions sooner
                consecutiveFailures += 2; // Count this as multiple failures to trigger backup questions faster
                break;
            
            default:
                Debug.LogError(FormatLogMessage("Unknown error: {0}", errorDetails));
                break;
        }
    }
    
    // Add a method to determine appropriate cache size
    private void DetermineOptimalCacheSize()
    {
        if (!useAdaptiveCacheSize)
        {
            return; // Use the inspector-set value
        }
        
        // Start with the default max cache size
        int optimalSize = maxCacheSize;
        
        // Get system memory info (approximate on most platforms)
        long systemMemoryMB = SystemInfo.systemMemorySize;
        
        // Adjust based on available memory
        if (systemMemoryMB < 1024) // Less than 1GB RAM
        {
            // Very low memory device, use minimum cache
            optimalSize = minCacheSize;
        }
        else if (systemMemoryMB < 2048) // Less than 2GB RAM
        {
            // Low memory device, reduce cache size
            optimalSize = Mathf.Max(minCacheSize, maxCacheSize / 2);
        }
        else if (systemMemoryMB < 4096) // Less than 4GB RAM
        {
            // Mid-range device, slight reduction
            optimalSize = Mathf.Max(minCacheSize, maxCacheSize * 3 / 4);
        }
        // Else use the full cache size for high memory devices
        
        // If we've changed the cache size, log it
        if (optimalSize != maxCacheSize)
        {
            maxCacheSize = optimalSize;
            Debug.Log(FormatLogMessage("Adjusted cache size to {0} based on system memory ({1}MB)", 
                maxCacheSize, systemMemoryMB));
        }
    }
    
    [System.Serializable]
    private class OpenTDBResponse
    {
        public int response_code;
        public List<OpenTDBQuestion> results;
    }
    
    [System.Serializable]
    private class OpenTDBQuestion
    {
        public string category;
        public string type;
        public string difficulty;
        public string question;
        public string correct_answer;
        public List<string> incorrect_answers;
    }
}