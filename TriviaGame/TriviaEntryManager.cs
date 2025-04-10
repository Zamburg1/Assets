using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using Shared.Core;
using Shared.Trivia;
using Alphasquad.GamesShared;

public class TriviaEntryManager : EntryManagerBase
{
    private Dictionary<string, int> currentVotes = new Dictionary<string, int>();
    private int[] voteCounts = new int[4];
    private TwitchConnection twitchConnection;
    
    [Header("Gem Pool Settings")]
    [Tooltip("Gems awarded per winner (can be scaled by difficulty)")]
    [SerializeField] private int gemsPerWinner = 100;
    
    private void Awake()
    {
        entries = new Dictionary<string, int>();
        DebugLogger.LogInitialization(this, "TriviaEntryManager initialized");
    }

    void Start()
    {
        twitchConnection = FindAnyObjectByType<TwitchConnection>();
    }
    
    public override int AddEntry(string username)
    {
        username = SanitizeUsername(username);
        
        if (!entries.ContainsKey(username))
        {
            entries[username] = 1;
        }
        else
        {
            entries[username]++;
        }
        
        return entries[username];
    }
    
    public override void ResetEntries()
    {
        entries.Clear();
        ClearVotes();
    }
    
    public void ClearVotes()
    {
        currentVotes.Clear();
        for (int i = 0; i < voteCounts.Length; i++)
        {
            voteCounts[i] = 0;
        }
        DebugLogger.Log(this, "Cleared all votes");
    }
    
    public void RecordVote(string username, int answerIndex)
    {
        if (string.IsNullOrEmpty(username) || answerIndex < 0 || answerIndex >= 4)
            return;
            
        username = SanitizeUsername(username);
        
        // Check if user already voted
        if (currentVotes.TryGetValue(username, out int previousVote))
        {
            // User already voted - do nothing or you could update UI to show they already voted
            return;
        }
        
        // Record the vote
        currentVotes[username] = answerIndex;
        voteCounts[answerIndex]++;
        
        // Track interaction for consistency
        AddEntry(username);
        
        DebugLogger.LogUserInteraction(this, username, $"voted for answer {answerIndex + 1}");
    }
    
    public List<string> CalculateWinners(int correctAnswerIndex)
    {
        if (correctAnswerIndex < 0 || correctAnswerIndex >= 4)
            return new List<string>();
            
        List<string> winners = new List<string>();
        
        foreach (var entry in currentVotes)
        {
            if (entry.Value == correctAnswerIndex)
            {
                winners.Add(entry.Key);
            }
        }
        
        DebugLogger.Log(this, $"Found {winners.Count} winners for correct answer {correctAnswerIndex + 1}");
        return winners;
    }
    
    public int[] GetAllVoteCounts()
    {
        return voteCounts;
    }
    
    public int GetVotesForAnswer(int answerIndex)
    {
        if (answerIndex < 0 || answerIndex >= voteCounts.Length)
            return 0;
            
        return voteCounts[answerIndex];
    }
    
    public int GetTotalVotes()
    {
        return currentVotes.Count;
    }
    
    public override int GetUniqueParticipants()
    {
        return entries.Count;
    }
}