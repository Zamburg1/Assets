using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System.Collections;

// Suppress "assigned but not used" warnings for inspector debug fields
#pragma warning disable 0414

public class MockTwitchChat : MonoBehaviour
{
    [Header("Auto Generation Settings")]
    public float minMessageDelay = 0.5f;
    public float maxMessageDelay = 2.0f;
    [Tooltip("Key used to toggle both test modes at once")]
    public KeyCode toggleTestModeKey = KeyCode.Space;
    
    [System.Serializable]
    public class TestUsername
    {
        public string username;

        public TestUsername(string name)
        {
            username = name;
        }
    }
    
    [Header("Test User Settings")]
    [SerializeField, Tooltip("Click the + button to add a unique test username")]
    private List<TestUsername> mockUsernamesList = new List<TestUsername>
    {
        new TestUsername("test_alphasquad_user1"),
        new TestUsername("test_alphasquad_user2"),
        new TestUsername("test_alphasquad_user3"),
        new TestUsername("test_alphasquad_viewer1"),
        new TestUsername("test_alphasquad_viewer2"),
        new TestUsername("test_alphasquad_tester1"),
        new TestUsername("test_alphasquad_tester2"),
        new TestUsername("test_alphasquad_bot1"),
        new TestUsername("test_alphasquad_bot2"),
        new TestUsername("test_alphasquad_fan1")
    };
    
    // Legacy property for compatibility with existing code
    private List<string> mockUsernames
    {
        get
        {
            return mockUsernamesList.Select(u => u.username).ToList();
        }
    }
    
    [Header("Debug Information")]
    [SerializeField] private bool showDebugInfo = true;
    [SerializeField, ReadOnly] private string currentMode = "Inactive";
    [SerializeField, ReadOnly] private int remainingUsers = 0;
    [SerializeField, ReadOnly] private bool isTestingActive = false;
    
    [Tooltip("Reference to TriviaController to detect question start/end events")]
    public TriviaController triviaController;
    
    [Tooltip("Reference to LootController to detect round start/end events")]
    public LootController lootController;
    
    [SerializeField, Tooltip("Reference to TwitchConnection (auto-filled if null)")]
    private TwitchConnection twitchConnection;
    private bool isGeneratingAlphasquad = false;
    private bool isGeneratingTriviaAnswers = false;
    private bool isGeneratingLootSelections = false;
    private List<string> remainingTriviaUsers = new List<string>();
    private List<string> remainingLootUsers = new List<string>();
    private int lastQuestionId = -1;
    private int lastLootRoundId = -1;
    
    // Tracked coroutines for proper cleanup
    private Dictionary<string, Coroutine> activeCoroutines = new Dictionary<string, Coroutine>();
    
    // Track if we're in the process of cleaning up
    private bool isCleaningUp = false;
    
    void Awake()
    {
        // Try to find TwitchConnection in the scene if not assigned
        if (twitchConnection == null)
        {
            twitchConnection = FindAnyObjectByType<TwitchConnection>();
        }
    }
    
    void Start()
    {
        UpdateDebugInfo();
            
        // Subscribe to trivia events if possible
        if (triviaController != null)
        {
            triviaController.OnNewQuestion += HandleNewQuestion;
            triviaController.OnQuestionEnd += HandleQuestionEnd;
        }
        
        // Subscribe to loot game events if possible
        if (lootController != null)
        {
            lootController.OnNewRound += HandleNewLootRound;
            lootController.OnRoundEnd += HandleLootRoundEnd;
        }
    }
    
    void OnEnable()
    {
        isCleaningUp = false;
    }
    
    void OnDisable()
    {
        isCleaningUp = true;
        StopAllGenerationCoroutines();
    }
    
    void OnDestroy()
    {
        isCleaningUp = true;
        if (triviaController != null)
        {
            triviaController.OnNewQuestion -= HandleNewQuestion;
            triviaController.OnQuestionEnd -= HandleQuestionEnd;
        }
        
        if (lootController != null)
        {
            lootController.OnNewRound -= HandleNewLootRound;
            lootController.OnRoundEnd -= HandleLootRoundEnd;
        }
        
        StopAllGenerationCoroutines();
    }
    
    void Update()
    {
        // Toggle both test modes with the same key
        if (Input.GetKeyDown(toggleTestModeKey))
        {
            // Toggle both modes together
            isTestingActive = !isTestingActive;
            isGeneratingAlphasquad = isTestingActive;
            isGeneratingTriviaAnswers = isTestingActive;
            isGeneratingLootSelections = isTestingActive;
            
            if (isTestingActive)
            {
                ResetTriviaUsers();
                ResetLootUsers();
                StartAutoGeneration();
                if (showDebugInfo) Debug.Log("All test modes enabled");
            }
            else
            {
                StopAutoGeneration();
                if (showDebugInfo) Debug.Log("All test modes disabled");
            }
            
            UpdateDebugInfo();
        }
    }
    
    // Start the auto-generation coroutine
    private void StartAutoGeneration()
    {
        // Only start if we're not cleaning up
        if (isCleaningUp) return;
        
        // Stop existing coroutine if running
        StopAutoGeneration();
        
        // Start a new coroutine and track it
        if (gameObject.activeInHierarchy)
        {
            StartAndTrackCoroutine("autoGenerate", AutoGenerateMessagesCoroutine());
        }
    }
    
    // Stop the auto-generation coroutine
    private void StopAutoGeneration()
    {
        StopTrackedCoroutine("autoGenerate");
    }
    
    // Stop all running coroutines and clear tracking
    private void StopAllGenerationCoroutines()
    {
        if (this == null || !this.gameObject) return;
        
        try
        {
            // Create a copy of the keys to avoid collection modified exception
            List<string> coroutineKeys = new List<string>(activeCoroutines.Keys);
            foreach (var key in coroutineKeys)
            {
                StopTrackedCoroutine(key);
            }
            
            // Additional safety: Explicitly stop the main coroutines by name if they exist
            StopTrackedCoroutine("autoGenerate");
            
            // Clear the dictionary after stopping all coroutines
            activeCoroutines.Clear();
            
            // Additional safety: stop all coroutines on this MonoBehaviour
            StopAllCoroutines();
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[MockTwitchChat] Error stopping coroutines: {e.Message}");
        }
        
        // Reset state variables
        remainingTriviaUsers.Clear();
        remainingLootUsers.Clear();
        isGeneratingAlphasquad = false;
        isGeneratingTriviaAnswers = false;
        isGeneratingLootSelections = false;
        isTestingActive = false;
        
        // Update the UI to reflect the changes
        UpdateDebugInfo();
    }
    
    // Start and track a coroutine by name
    private void StartAndTrackCoroutine(string name, IEnumerator routine)
    {
        if (isCleaningUp || !this.gameObject || !this.enabled || !this.gameObject.activeInHierarchy)
        {
            return; // Don't start new coroutines during cleanup
        }
        
        StopTrackedCoroutine(name);
        try
        {
            // Only start coroutines if the GameObject is active
            if (gameObject.activeInHierarchy)
            {
                activeCoroutines[name] = StartCoroutine(routine);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[MockTwitchChat] Error starting coroutine {name}: {e.Message}");
        }
    }
    
    // Stop a tracked coroutine by name
    private void StopTrackedCoroutine(string name)
    {
        if (activeCoroutines.TryGetValue(name, out Coroutine coroutine))
        {
            if (coroutine != null && this != null && this.gameObject)
            {
                try
                {
                    StopCoroutine(coroutine);
                }
                catch (System.Exception e)
                {
                    // Log but don't rethrow - this is likely during scene transitions
                    Debug.LogWarning($"[MockTwitchChat] Error stopping coroutine {name}: {e.Message}");
                }
            }
            activeCoroutines.Remove(name);
        }
    }
    
    // Coroutine for auto-generating messages
    private IEnumerator AutoGenerateMessagesCoroutine()
    {
        while (!isCleaningUp && (isGeneratingAlphasquad || isGeneratingTriviaAnswers || isGeneratingLootSelections))
        {
            // Check if we're still in a valid state
            if (!this || !this.gameObject || !this.enabled || !this.gameObject.activeInHierarchy)
            {
                yield break; // Exit if not active
            }
            
            float delay = Random.Range(minMessageDelay, maxMessageDelay);
            
            // Simulate a Trivia game answer if in trivia mode and we have users remaining
            if (isGeneratingTriviaAnswers && remainingTriviaUsers.Count > 0)
            {
                SimulateTriviaAnswer();
            }
            
            // Simulate a Loot game chest selection if in loot mode and we have users remaining
            if (isGeneratingLootSelections && remainingLootUsers.Count > 0)
            {
                SimulateChestSelection();
            }
            
            // Generate messages based on active modes
            if (isGeneratingAlphasquad)
            {
                // Generate !alphasquad messages
                GenerateAlphasquadMessage();
            }
            
            yield return new WaitForSeconds(delay);
        }
    }
    
    // Generate random !alphasquad messages
    private void GenerateAlphasquadMessage()
    {
        if (mockUsernames == null || mockUsernames.Count == 0) return;
        
        string randomUsername = mockUsernames[Random.Range(0, mockUsernames.Count)];
        string mockMessage = $":{randomUsername}!{randomUsername}@{randomUsername}.tmi.twitch.tv PRIVMSG #channel :!alphasquad";
        
        if (twitchConnection != null)
        {
            twitchConnection.ProcessMessageForTesting(mockMessage);
            if (showDebugInfo) Debug.Log($"Generated !alphasquad message from {randomUsername}");
        }
    }
    
    // Generate trivia answers from test users
    private void GenerateTriviaAnswer()
    {
        if (remainingTriviaUsers.Count == 0) return;
        
        int userIndex = Random.Range(0, remainingTriviaUsers.Count);
        string username = remainingTriviaUsers[userIndex];
        remainingTriviaUsers.RemoveAt(userIndex);
        UpdateDebugInfo();
        
        // Sometimes simulate a user trying to answer twice
        if (Random.value < 0.2f) // 20% chance
        {
            SimulateSpecificAnswer(username, Random.Range(1, 5));
            SimulateSpecificAnswer(username, Random.Range(1, 5));
        }
        else
        {
            SimulateSpecificAnswer(username, Random.Range(1, 5));
        }
        
        if (showDebugInfo) Debug.Log($"Test user {username} submitted their answer. {remainingTriviaUsers.Count} users remaining.");
    }
    
    // Simulates a specific answer from a specific user
    private void SimulateSpecificAnswer(string username, int answerNumber)
    {
        string message = $":{username}!user@twitch.tv PRIVMSG #channel :!answer{answerNumber}";
        if (twitchConnection != null)
        {
            twitchConnection.ProcessMessageForTesting(message);
        }
    }
    
    // Reset list of users for trivia testing
    private void ResetTriviaUsers()
    {
        remainingTriviaUsers.Clear();
        if (mockUsernames != null && mockUsernames.Count > 0)
        {
        remainingTriviaUsers.AddRange(mockUsernames);
        }
        UpdateDebugInfo();
    }
    
    // Handle new trivia question events
    private void HandleNewQuestion(int questionId)
    {
        if (isGeneratingTriviaAnswers && questionId != lastQuestionId)
        {
            if (showDebugInfo) Debug.Log($"New question detected (ID: {questionId}). Resetting test users.");
            lastQuestionId = questionId;
            ResetTriviaUsers();
        }
    }
    
    // Handle trivia question end events
    private void HandleQuestionEnd(int questionId)
    {
        if (isGeneratingTriviaAnswers)
        {
            if (showDebugInfo) Debug.Log($"Question ended (ID: {questionId}). Clearing remaining test users.");
            remainingTriviaUsers.Clear();
            UpdateDebugInfo();
        }
    }
    
    // Reset list of users for loot game testing
    private void ResetLootUsers()
    {
        remainingLootUsers.Clear();
        if (mockUsernames != null && mockUsernames.Count > 0)
        {
            remainingLootUsers.AddRange(mockUsernames);
        }
        UpdateDebugInfo();
    }
    
    // Simulate a random chest selection from a test user
    private void SimulateChestSelection()
    {
        // Exit if no users left
        if (remainingLootUsers.Count == 0) return;
        
        // Pick a random user from the remaining list
        int userIndex = Random.Range(0, remainingLootUsers.Count);
        string username = remainingLootUsers[userIndex];
        
        // Remove the user so they only vote once
        remainingLootUsers.RemoveAt(userIndex);
        UpdateDebugInfo();
        
        // Sometimes simulate a user trying to select multiple chests
        if (Random.value < 0.2f) // 20% chance
        {
            SimulateSpecificChestSelection(username, Random.Range(1, 9));
            SimulateSpecificChestSelection(username, Random.Range(1, 9));
        }
        else
        {
            SimulateSpecificChestSelection(username, Random.Range(1, 9));
        }
        
        if (showDebugInfo) Debug.Log($"Test user {username} selected a chest. {remainingLootUsers.Count} users remaining.");
    }
    
    // Simulates a specific chest selection from a specific user
    private void SimulateSpecificChestSelection(string username, int chestNumber)
    {
        string message = $":{username}!user@twitch.tv PRIVMSG #channel :!chest{chestNumber}";
        if (twitchConnection != null)
        {
            twitchConnection.ProcessMessageForTesting(message);
        }
    }
    
    // Handle new loot round events
    private void HandleNewLootRound(int roundId)
    {
        if (isGeneratingLootSelections && roundId != lastLootRoundId)
        {
            if (showDebugInfo) Debug.Log($"New loot round detected (ID: {roundId}). Resetting test users.");
            lastLootRoundId = roundId;
            ResetLootUsers();
        }
    }
    
    // Handle loot round end events
    private void HandleLootRoundEnd(int roundId)
    {
        if (isGeneratingLootSelections)
        {
            if (showDebugInfo) Debug.Log($"Loot round ended (ID: {roundId}). Clearing remaining test users.");
            remainingLootUsers.Clear();
            UpdateDebugInfo();
        }
    }
    
    // Update debug information for the inspector
    private void UpdateDebugInfo()
    {
        // Determine current mode status text
        if (isGeneratingAlphasquad && isGeneratingTriviaAnswers && isGeneratingLootSelections)
        {
            currentMode = "All Modes Active";
        }
        else if (isGeneratingAlphasquad && isGeneratingTriviaAnswers)
        {
            currentMode = "Alphasquad & Trivia Modes";
        }
        else if (isGeneratingAlphasquad && isGeneratingLootSelections)
        {
            currentMode = "Alphasquad & Loot Modes";
        }
        else if (isGeneratingTriviaAnswers && isGeneratingLootSelections)
        {
            currentMode = "Trivia & Loot Modes";
        }
        else if (isGeneratingAlphasquad)
        {
            currentMode = "Alphasquad Mode Only";
        }
        else if (isGeneratingTriviaAnswers)
        {
            currentMode = "Trivia Mode Only";
        }
        else if (isGeneratingLootSelections)
        {
            currentMode = "Loot Mode Only";
        }
        else
        {
            currentMode = "Inactive";
        }
        
        // Update number of remaining users for trivia and loot
        remainingUsers = remainingTriviaUsers.Count + remainingLootUsers.Count;
        
        // Update testing status flag
        isTestingActive = isGeneratingAlphasquad || isGeneratingTriviaAnswers || isGeneratingLootSelections;
    }
    
    void OnValidate()
    {
        // Ensure values are within reasonable ranges
        minMessageDelay = Mathf.Max(0.1f, minMessageDelay);
        maxMessageDelay = Mathf.Max(minMessageDelay + 0.1f, maxMessageDelay);
        
        // Create unique usernames for any new entries
        EnsureUniqueUsernames();
    }
    
    private void EnsureUniqueUsernames()
    {
        // Check if any entries have default or duplicate values
        HashSet<string> existingNames = new HashSet<string>();
        bool changed = false;
        
        for (int i = 0; i < mockUsernamesList.Count; i++)
        {
            // Skip null entries
            if (mockUsernamesList[i] == null)
            {
                mockUsernamesList[i] = new TestUsername(GenerateUniqueUsername(existingNames));
                changed = true;
                continue;
            }
            
            string username = mockUsernamesList[i].username;
            
            // Check for empty, default, or duplicate names
            if (string.IsNullOrWhiteSpace(username) || 
                username == "New Username" || 
                username == "StreamFan1" ||
                existingNames.Contains(username))
            {
                // Generate a new unique name
                mockUsernamesList[i].username = GenerateUniqueUsername(existingNames);
                changed = true;
            }
            
            // Add to the set of existing names
            existingNames.Add(mockUsernamesList[i].username);
        }
        
        if (changed)
        {
            Debug.Log("Updated test usernames to ensure uniqueness");
        }
    }
    
    // Generate a unique username that doesn't exist in the list yet
    private string GenerateUniqueUsername(HashSet<string> existingNames)
    {
        string[] prefixes = { "Viewer", "Chat", "Stream", "Test", "Mock", "Fan", "Twitch", "User" };
        string[] suffixes = { "Bot", "Friend", "Pro", "Guru", "Master", "Noob", "Champion", "Expert" };
        
        string username;
        int attempts = 0;
        
        do
        {
            // Prevent infinite loops
            if (attempts > 100) 
            {
                return $"TestUser{Random.Range(1000, 9999)}";
            }
            
            // Generate a random username
            if (Random.value < 0.5f)
            {
                // Format: PrefixSuffixNumber
                string prefix = prefixes[Random.Range(0, prefixes.Length)];
                string suffix = suffixes[Random.Range(0, suffixes.Length)];
                int number = Random.Range(1, 999);
                username = $"{prefix}{suffix}{number}";
            }
            else
            {
                // Format: PrefixNumber
                string prefix = prefixes[Random.Range(0, prefixes.Length)];
                int number = Random.Range(1, 999);
                username = $"{prefix}{number}";
            }
            
            attempts++;
        } while (existingNames.Contains(username));
        
        return username;
    }
}

// Helper attribute to make fields read-only in the inspector
public class ReadOnlyAttribute : PropertyAttribute { }