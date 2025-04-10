using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Collections;
using System.Threading.Tasks;
using Shared.Chat;

public class TwitchConnection : MonoBehaviour
{
    // Enhanced message data with badges
    public class TwitchMessageData
    {
        public string Username { get; set; }
        public string Message { get; set; }
        public List<string> Badges { get; set; } = new List<string>();
        public string Color { get; set; } = "#FFFFFF"; // Default color
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>();
        
        // Role checks
        public bool IsModerator => Badges.Contains("moderator");
        public bool IsSubscriber => Badges.Contains("subscriber");
        public bool IsBroadcaster => Badges.Contains("broadcaster");
        public bool IsVIP => Badges.Contains("vip");
        public bool IsArtist => Badges.Contains("artist");
        public bool IsCheerer => Badges.Contains("bits") || Badges.Contains("bits-leader");
        
        // Get appropriate hex color based on role hierarchy
        public string GetRoleColor(TwitchConnection connection)
        {
            // Role hierarchy (highest to lowest)
            if (IsBroadcaster) return connection.broadcasterColor;
            if (IsModerator) return connection.moderatorColor;
            if (IsVIP) return connection.vipColor;
            if (IsArtist) return connection.artistColor;
            if (IsSubscriber) return connection.subscriberColor;
            if (IsCheerer) return connection.cheererColor;
            
            // Get color from tags if present, otherwise use default color
            return string.IsNullOrEmpty(Color) ? connection.defaultUserColor : Color;
        }
        
        public override string ToString()
        {
            return $"{Username}{(Badges.Count > 0 ? $" [{string.Join(", ", Badges)}]" : "")}";
        }
    }
    
    // Updated delegate for chat message events with badge support
    public delegate void ChatMessageHandlerWithBadges(TwitchMessageData messageData);
    public event ChatMessageHandlerWithBadges OnMessageReceivedWithBadges;
    
    // Legacy delegate for backward compatibility
    public delegate void ChatMessageHandler(string username, string message);
    public event ChatMessageHandler OnMessageReceived;
    
    // Delegate for handling messages with metadata
    public delegate void MessageReceivedHandler(string username, string message, Dictionary<string, string> tags);
    
    [Header("Connection Settings")]
    [SerializeField] private string twitchServer = "irc.chat.twitch.tv";
    [SerializeField] private int twitchPort = 6667;
    [SerializeField, Tooltip("How long to wait between reconnection attempts (seconds)")] 
    private float initialReconnectDelay = 5f;
    [SerializeField, Tooltip("Maximum reconnection delay after backoff (seconds)")]
    private float maxReconnectDelay = 60f;
    [SerializeField, Tooltip("Maximum number of reconnection attempts")]
    private int maxReconnectAttempts = 20;
    [SerializeField, Tooltip("Connection timeout in seconds")]
    private float connectionTimeout = 10f;
    
    [Header("Role Colors")]
    [SerializeField, Tooltip("Color for the broadcaster (channel owner)")] 
    public string broadcasterColor = "#FF0000";
    [SerializeField, Tooltip("Color for channel moderators")] 
    public string moderatorColor = "#00FF00";
    [SerializeField, Tooltip("Color for VIP users")] 
    public string vipColor = "#FF00FF";
    [SerializeField, Tooltip("Color for artists")] 
    public string artistColor = "#FFA500";
    [SerializeField, Tooltip("Color for subscribers")] 
    public string subscriberColor = "#8A2BE2";
    [SerializeField, Tooltip("Color for users who have donated bits")] 
    public string cheererColor = "#0000FF";
    [SerializeField, Tooltip("Default color for regular users")]
    public string defaultUserColor = "#FFFFFF";
    
    [Header("Command Settings")]
    [Tooltip("TCP connection requires regular PING messages to keep the connection alive - Twitch recommends 5 minutes (300 seconds)")]
    private float pingInterval = 300f;
    
    [Header("Debug Options")]
    [SerializeField, Tooltip("Show debug messages in console")]
    private bool showDebugMessages = true;
    
    private string username;
    private string oauthToken;
    private string channelName;
    
    private TcpClient twitchClient;
    private StreamReader reader;
    private StreamWriter writer;
    private Thread readerThread;
    private CancellationTokenSource cancellationTokenSource;
    private bool isConnected = false;
    private bool shouldReconnect = true;
    
    private GiveawayController controller;
    private TokenManager tokenManager;
    
    // Reference to the chat command processor
    private ChatCommandProcessor commandProcessor;
    
    private void Awake()
    {
        // Get reference to the command processor
        commandProcessor = ChatCommandProcessor.Instance;
        if (commandProcessor == null)
        {
            Debug.LogWarning("ChatCommandProcessor not found. Command processing will be limited.");
        }
    }
    
    public void Initialize(GiveawayController controller)
    {
        this.controller = controller;
        
        // Find TokenManager
        tokenManager = FindAnyObjectByType<TokenManager>();
        if (tokenManager == null)
        {
            LogError("TokenManager not found. Please add it to the scene.");
            return;
        }
        
        // Try to load credentials
        LoadCredentials();
        
        // Connect to Twitch
        if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(oauthToken) && !string.IsNullOrEmpty(channelName))
        {
            ConnectToTwitch();
        }
        else
        {
            LogError("Twitch credentials missing. Check TokenManager setup.");
        }
    }
    
    private void LoadCredentials()
    {
        // First check environment variables (for backward compatibility)
        username = Environment.GetEnvironmentVariable("TWITCH_USERNAME");
        oauthToken = Environment.GetEnvironmentVariable("TWITCH_OAUTH_TOKEN");
        channelName = Environment.GetEnvironmentVariable("TWITCH_CHANNEL_NAME");
        
        // Then check TokenManager if environment variables are not set
        if (tokenManager != null)
        {
            // Use environment variables if available, otherwise use TokenManager
            if (string.IsNullOrEmpty(oauthToken))
            {
                oauthToken = tokenManager.GetTwitchOAuthFormatted();
            }
            
            // If username and channel name aren't set through environment variables,
            // you might need to add them to TokenManager or set them directly here
            // For now, we'll assume they're set through environment variables
        }
        
        // Make sure oauth token has correct format
        if (!string.IsNullOrEmpty(oauthToken) && !oauthToken.StartsWith("oauth:"))
        {
            oauthToken = "oauth:" + oauthToken;
        }
    }
    
    /// <summary>
    /// Establishes connection to Twitch IRC
    /// </summary>
    private bool ConnectToTwitch()
    {
        try
        {
            LogMessage("Connecting to Twitch chat...");
            
            // Create connection
            twitchClient = new TcpClient();
            twitchClient.ReceiveTimeout = (int)(connectionTimeout * 1000);
            twitchClient.SendTimeout = (int)(connectionTimeout * 1000);
            
            // Attempt connection with timeout
            var connectResult = twitchClient.BeginConnect(twitchServer, twitchPort, null, null);
            var success = connectResult.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(connectionTimeout));
            
            if (!success)
            {
                LogError("Connection attempt timed out");
                twitchClient.Close();
                return false;
            }
            
            // Finish the connection process
            try {
                twitchClient.EndConnect(connectResult);
            }
            catch (Exception e) {
                LogError("Connection failed: " + e.Message);
                return false;
            }
            
            reader = new StreamReader(twitchClient.GetStream());
            writer = new StreamWriter(twitchClient.GetStream()) { AutoFlush = true };
            
            // Request capabilities for tags (includes badges)
            writer.WriteLine("CAP REQ :twitch.tv/tags twitch.tv/commands");
            
            // Authenticate
            writer.WriteLine("PASS " + oauthToken);
            writer.WriteLine("NICK " + username);
            writer.WriteLine("JOIN #" + channelName);
            
            // Create new cancellation token for this connection
            if (cancellationTokenSource != null)
            {
                cancellationTokenSource.Dispose();
            }
            cancellationTokenSource = new CancellationTokenSource();
            
            // Start reader thread with the cancellation token
            StartReaderThread();
            
            // Schedule regular pings to keep the connection alive
            InvokeRepeating("SendPing", pingInterval, pingInterval);
            
            LogMessage("Connected to Twitch chat successfully!");
            
            return true;
        }
        catch (Exception e)
        {
            LogError("Error connecting to Twitch: " + e.Message);
            CleanupConnection();
            return false;
        }
    }
    
    private void StartReaderThread()
    {
        if (readerThread != null && readerThread.IsAlive)
        {
            LogMessage("Stopping existing reader thread...");
            StopReaderThread();
        }
        
        // Create a new cancellation token source
        cancellationTokenSource = new CancellationTokenSource();
        
        // Start the thread
        readerThread = new Thread(() => ReadMessages(cancellationTokenSource.Token));
        readerThread.IsBackground = true; // Make sure it's a background thread
        readerThread.Start();
        
        LogMessage("Message reader thread started");
    }
    
    private void StopReaderThread()
    {
        try
        {
            // Signal cancellation
            if (cancellationTokenSource != null && !cancellationTokenSource.IsCancellationRequested)
            {
                cancellationTokenSource.Cancel();
            }
            
            // Wait for the thread to exit with a timeout
            if (readerThread != null && readerThread.IsAlive)
            {
                LogMessage("Waiting for reader thread to exit...");
                bool joined = readerThread.Join(3000); // Wait up to 3 seconds
                
                if (!joined)
                {
                    LogError("Reader thread did not exit gracefully, may be blocked.");
                    // We don't Abort since it's deprecated and unsafe
                }
            }
        }
        catch (Exception ex)
        {
            LogError($"Error stopping reader thread: {ex.Message}");
        }
        
        cancellationTokenSource?.Dispose();
        cancellationTokenSource = null;
    }
    
    private void ReadMessages(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && twitchClient != null && twitchClient.Connected)
            {
                // Check if client is still valid
                if (reader == null || !twitchClient.Connected)
                {
                    LogError("Reader or client became invalid, exiting read loop");
                    break;
                }
                
                // Attempt to read line
                try
                {
                    string message = reader.ReadLine();
                    
                    // Exit if cancelled or null message (connection closed)
                    if (cancellationToken.IsCancellationRequested || message == null)
                    {
                        LogMessage("End of stream or cancellation detected, exiting read loop");
                        break;
                    }
                    
                    // Process message on the main thread
                    MainThreadDispatcher.Enqueue(() => ProcessMessage(message));
                }
                catch (IOException ex)
                {
                    // This can happen when the socket is closed from another thread
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        LogError($"Read error: {ex.Message}");
                        
                        // Try to reconnect if we should
                        if (shouldReconnect)
                        {
                            MainThreadDispatcher.Enqueue(AttemptReconnect);
                        }
                    }
                    break;
                }
                catch (ObjectDisposedException)
                {
                    // Reader was disposed
                    LogMessage("Reader was disposed, exiting read loop");
                    break;
                }
                catch (Exception ex)
                {
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        LogError($"Unexpected read error: {ex.Message}");
                    }
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            LogError($"Fatal reader thread error: {ex.Message}");
        }
        finally
        {
            LogMessage("Reader thread exiting");
        }
    }
    
    private void AttemptReconnect()
    {
        StartCoroutine(ReconnectWithBackoff());
    }
    
    /// <summary>
    /// Handles reconnecting to Twitch IRC with exponential backoff
    /// </summary>
    private IEnumerator ReconnectWithBackoff()
    {
        // Stop the reader thread gracefully
        StopReaderThread();
        
        if (twitchClient != null)
        {
            twitchClient.Close();
            twitchClient = null;
        }
        
        CancelInvoke("SendPing"); // Stop sending pings until reconnected
        
        int attempts = 0;
        float currentDelay = initialReconnectDelay;
        
        while (shouldReconnect && attempts < maxReconnectAttempts)
        {
            attempts++;
            LogMessage($"Attempting to reconnect to Twitch (attempt {attempts}/{maxReconnectAttempts})...");
            
            // Wait before retry
            yield return new WaitForSeconds(currentDelay);
            
            // Try to reconnect
            if (ConnectToTwitch())
            {
                LogMessage("Reconnection successful.");
                yield break; // Exit coroutine if successful
            }
            
            // Increase delay using exponential backoff with a max limit
            currentDelay = Mathf.Min(currentDelay * 1.5f, maxReconnectDelay);
        }
        
        if (attempts >= maxReconnectAttempts)
        {
            LogError("Max reconnection attempts reached. Please restart the application.");
        }
    }
    
    /// <summary>
    /// Schedules a reconnection attempt 
    /// </summary>
    private void ScheduleReconnect()
    {
        isConnected = false;
        StartCoroutine(ReconnectWithBackoff());
    }
    
    /// <summary>
    /// Processes incoming chat messages
    /// </summary>
    private void ProcessMessage(string message)
    {
        // If it's a PING message, respond with PONG
        if (message.StartsWith("PING"))
        {
            SendPong(message);
            return;
        }
        
        // Not all messages are chat messages, but we only care about those for commands
        if (!message.Contains("PRIVMSG"))
        {
            return; // Exit early if not a chat message
        }

        try
        {
            // Create a message data object to hold all the information
            TwitchMessageData messageData = new TwitchMessageData();
            Dictionary<string, string> tags = new Dictionary<string, string>();
            
            // Extract username
            int exclamationIndex = message.IndexOf('!');
            if (exclamationIndex <= 0) return;
            
            // Check for tags/badges (starts with @)
            if (message.StartsWith("@"))
            {
                // Extract badge information from tags
                int spaceAfterTags = message.IndexOf(' ');
                if (spaceAfterTags > 0) 
                {
                    string tagSection = message.Substring(0, spaceAfterTags);
                    messageData.Badges = ExtractBadges(tagSection);
                    messageData.Color = ExtractColor(tagSection);
                    
                    // Parse tags into dictionary
                    string[] tagParts = tagSection.Substring(1).Split(';');
                    foreach (string tagPart in tagParts)
                    {
                        string[] keyValue = tagPart.Split(new char[] {'='}, 2);
                        if (keyValue.Length == 2)
                        {
                            tags[keyValue[0]] = keyValue[1];
                        }
                    }
                    
                    // Store tags for command processing
                    messageData.Tags = tags;
                    
                    // Extract username from the rest of the message
                    string afterTags = message.Substring(spaceAfterTags + 1);
                    exclamationIndex = afterTags.IndexOf('!');
                    if (exclamationIndex > 0)
                    {
                        messageData.Username = afterTags.Substring(1, exclamationIndex - 1);
                    }
                }
            }
            else
            {
                // Traditional username extraction for messages without tags
                messageData.Username = message.Substring(1, exclamationIndex - 1);
            }
            
            // If we couldn't extract a username, exit
            if (string.IsNullOrEmpty(messageData.Username)) return;

            // Extract message content
            int messageStart = message.IndexOf(':', message.IndexOf("PRIVMSG")) + 1;
            if (messageStart < 0 || messageStart >= message.Length) return;
            messageData.Message = message.Substring(messageStart);

            if (showDebugMessages)
            {
                LogMessage($"Processing Twitch message - User: {messageData}");
            }
            
            // Trigger event handlers for message events
            OnMessageReceivedWithBadges?.Invoke(messageData);
            OnMessageReceived?.Invoke(messageData.Username, messageData.Message);

            // Forward to ChatCommandProcessor for command handling
            if (commandProcessor != null && messageData.Message.StartsWith("!"))
            {
                bool handled = commandProcessor.ProcessChatMessage(
                    messageData.Username, 
                    messageData.Message, 
                    messageData.Tags
                );
                
                if (handled)
                {
                    // Command was handled by processor, we're done
                    return;
                }
            }

            // Process standard game messages for backward compatibility
            
            // Process trivia answers first (since this is primarily a trivia bot)
            var triviaController = FindAnyObjectByType<TriviaController>();
            if (triviaController != null)
            {
                triviaController.ProcessChatMessage(messageData.Username, messageData.Message);
            }

            // Process tickets command
            if (messageData.Message.ToLower().Trim() == "!tickets")
            {
                var ticketManager = FindAnyObjectByType<TicketManager>();
                if (ticketManager != null)
                {
                    ticketManager.ProcessTicketsCommand(messageData.Username);
                }
                return;
            }

            // Process giveaway entry if applicable
            if (messageData.Message.ToLower().Contains("!alphasquad"))
            {
                controller?.ProcessChatEntry(messageData.Username);
            }
        }
        catch (Exception e)
        {
            LogError($"Error processing chat message: {e.Message}\nMessage was: {message}");
        }
    }
    
    /// <summary>
    /// Extract badges from the Twitch message tags
    /// </summary>
    private List<string> ExtractBadges(string tagSection)
    {
        List<string> badges = new List<string>();
        
        // Find badges parameter
        int badgesStart = tagSection.IndexOf("badges=");
        if (badgesStart >= 0)
        {
            // Extract from badges= to the next semicolon or end
            int badgesEnd = tagSection.IndexOf(';', badgesStart);
            string badgesPart = badgesEnd > badgesStart ? 
                tagSection.Substring(badgesStart + 7, badgesEnd - badgesStart - 7) : 
                tagSection.Substring(badgesStart + 7);
                
            // Process badge list (format: badge/version,badge/version)
            if (!string.IsNullOrEmpty(badgesPart) && badgesPart != "")
            {
                string[] badgePairs = badgesPart.Split(',');
                foreach (string badgePair in badgePairs)
                {
                    // Extract just the badge name (before the /)
                    int versionSeparator = badgePair.IndexOf('/');
                    if (versionSeparator > 0)
                    {
                        badges.Add(badgePair.Substring(0, versionSeparator));
                    }
                    else
                    {
                        badges.Add(badgePair);
                    }
                }
            }
        }
        
        return badges;
    }
    
    /// <summary>
    /// Extract user color from the Twitch message tags
    /// </summary>
    private string ExtractColor(string tagSection)
    {
        // Find color parameter
        int colorStart = tagSection.IndexOf("color=");
        if (colorStart >= 0)
        {
            // Extract from color= to the next semicolon or end
            int colorEnd = tagSection.IndexOf(';', colorStart);
            string colorPart = colorEnd > colorStart ? 
                tagSection.Substring(colorStart + 6, colorEnd - colorStart - 6) : 
                tagSection.Substring(colorStart + 6);
                
            // If the user has set a color, it will be in the format #RRGGBB
            if (!string.IsNullOrEmpty(colorPart) && colorPart != "")
            {
                return colorPart;
            }
        }
        
        // Default to white if no color is found
        return defaultUserColor;
    }
    
    /// <summary>
    /// Sends a PING to maintain the Twitch connection
    /// </summary>
    private void SendPing()
    {
        if (isConnected && writer != null)
        {
            try
            {
                writer.WriteLine("PING :tmi.twitch.tv");
            }
            catch (Exception e)
            {
                LogError("Error sending PING: " + e.Message);
                isConnected = false;
                ScheduleReconnect();
            }
        }
    }
    
    /// <summary>
    /// Responds to Twitch server PING with PONG
    /// </summary>
    private void SendPong(string pingMessage)
    {
        if (writer != null)
        {
            try
            {
                writer.WriteLine("PONG :tmi.twitch.tv");
            }
            catch (Exception e)
            {
                LogError("Error sending PONG: " + e.Message);
                isConnected = false;
                UnityMainThreadDispatcher.Instance().Enqueue(() =>
                {
                    ScheduleReconnect();
                });
            }
        }
    }
    
    private void OnDisable()
    {
        // Disconnect when disabled
        DisconnectFromTwitch(reconnect: false);
    }
    
    private void OnDestroy()
    {
        // Ensure we clean up all resources
        DisconnectFromTwitch(reconnect: false);
        
        // Clear all event subscribers to prevent memory leaks
        OnMessageReceivedWithBadges = null;
        OnMessageReceived = null;
    }

    public void ProcessMessageForTesting(string message)
    {
        ProcessMessage(message);
    }
    
    /// <summary>
    /// Registers a chat command with the ChatCommandProcessor
    /// </summary>
    public void RegisterChatCommandHandler(string commandName, Action<string, string, Dictionary<string, string>> handler)
    {
        if (commandProcessor == null)
        {
            Debug.LogWarning("Cannot register command: ChatCommandProcessor not available");
            return;
        }
        
        if (string.IsNullOrEmpty(commandName) || handler == null)
            return;
            
        // Convert old-style handler to new format
        CommandHandler wrappedHandler = (username, args, rawMessage, tags, sourceObject) => {
            handler(username, rawMessage, tags);
            return true;
        };
        
        // Register with the command processor
        commandProcessor.RegisterCommand(
            commandName,
            wrappedHandler,
            $"Command registered by TwitchConnection",
            $"Usage: !{commandName}"
        );
        
        LogMessage($"Registered command handler for: {commandName}");
    }
    
    /// <summary>
    /// Send points command to StreamElements
    /// </summary>
    public void SendPointsCommand(string username, int points)
    {
        if (writer != null && isConnected)
        {
            try
            {
                string command = $"PRIVMSG #{channelName} :!addpoints {username} {points}";
                writer.WriteLine(command);
                LogMessage($"Sent points command: {command}");
            }
            catch (Exception e)
            {
                LogError($"Error sending points command: {e.Message}");
            }
        }
    }

    public void SendChatMessage(string message)
    {
        if (writer != null && isConnected)
        {
            try
            {
                writer.WriteLine($"PRIVMSG #{channelName} :{message}");
                LogMessage($"Sent chat message: {message}");
            }
            catch (Exception e)
            {
                LogError($"Error sending chat message: {e.Message}");
            }
        }
        else
        {
            LogWarning("Cannot send chat message - not connected to chat");
        }
    }

    // Utility method for consistent debug logging
    private void LogMessage(string message)
    {
        if (showDebugMessages)
        {
            Debug.Log($"[TwitchConnection] {message}");
        }
    }
    
    // Utility method for consistent error logging
    private void LogError(string message)
    {
        Debug.LogError($"[TwitchConnection] {message}");
    }

    private void LogWarning(string message)
    {
        Debug.LogWarning($"[TwitchConnection] {message}");
    }
    
    /// <summary>
    /// Cleans up connection resources
    /// </summary>
    private void CleanupConnection()
    {
        // Cancel any running operations
        CancelInvoke("SendPing");
        
        // Dispose of cancellation token
        if (cancellationTokenSource != null)
        {
            try
            {
                cancellationTokenSource.Dispose();
                cancellationTokenSource = null;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error disposing cancellation token: {ex.Message}");
            }
        }
        
        // Close streams
        if (reader != null)
        {
            try
        {
            reader.Dispose();
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error disposing reader: {ex.Message}");
            }
            finally
            {
            reader = null;
            }
        }
        
        if (writer != null)
        {
            try
        {
            writer.Dispose();
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error disposing writer: {ex.Message}");
            }
            finally
            {
            writer = null;
            }
        }
        
        // Close client
        if (twitchClient != null)
        {
            try
        {
            if (twitchClient.Connected)
                twitchClient.Close();
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error closing tcp client: {ex.Message}");
            }
            finally
            {
            twitchClient = null;
            }
        }
    }

    /// <summary>
    /// Disconnects from Twitch chat and cleans up resources
    /// </summary>
    public void DisconnectFromTwitch(bool reconnect = true)
    {
        shouldReconnect = reconnect;
        
        try
        {
            // Cancel the read thread
            if (cancellationTokenSource != null && !cancellationTokenSource.IsCancellationRequested)
            {
                cancellationTokenSource.Cancel();
                cancellationTokenSource = null;
            }
            
            // Stop the reader thread
            if (readerThread != null && readerThread.IsAlive)
            {
                readerThread.Join(TimeSpan.FromSeconds(2));
                readerThread = null;
            }
            
            // Close the writer
            if (writer != null)
            {
                try { writer.Close(); } catch { }
                writer = null;
            }
            
            // Close the reader
            if (reader != null)
            {
                try { reader.Close(); } catch { }
                reader = null;
            }
            
            // Close the client
            if (twitchClient != null)
            {
                try 
                { 
                    if (twitchClient.Connected)
                    {
                        twitchClient.Close();
                    }
                } 
                catch { }
                twitchClient = null;
            }
            
            isConnected = false;
            LogMessage("Disconnected from Twitch chat.");
        }
        catch (Exception ex)
        {
            LogError($"Error during disconnect: {ex.Message}");
        }
    }
}