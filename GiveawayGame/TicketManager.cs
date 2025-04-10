using Shared.Core;
using System;
using UnityEngine;

public class TicketManager : TicketManagerBase
{
    private const string GIVEAWAY_TICKETS_KEY = "GiveawayTickets";
    
    // Override the tickets save key to be specific to the giveaway game
    protected override string TicketsSaveKey => GIVEAWAY_TICKETS_KEY;
    
    // Override the no tickets message to be specific to the giveaway game
    protected override void SendNoTicketsMessage(string username)
    {
        if (twitchConnection != null)
        {
            twitchConnection.SendChatMessage($"@{username}, you don't have any tickets to join the giveaway. {GetTimeUntilReset()}.");
        }
    }
    
    // Override process tickets command to be specific to the giveaway
    public override void ProcessTicketsCommand(string username)
    {
        if (twitchConnection == null) return;
        
        int tickets = GetRemainingTickets(username);
        
        twitchConnection.SendChatMessage($"@{username}, you have {tickets} entry tickets remaining today. {GetTimeUntilReset()}.");
    }
    
    // Override announce winner to use "gems" instead of generic reward
    public override void AnnounceWinner(string username, int reward)
    {
        if (twitchConnection == null) return;
        
        twitchConnection.SendChatMessage($"@{username} has won {reward} gems, congratulations!");
        
        // If they won, let them know their ticket balance
        int remainingTickets = GetRemainingTickets(username);
        twitchConnection.SendChatMessage($"@{username}, you have {remainingTickets} entry tickets remaining for today. {GetTimeUntilReset()}.");
    }
} 