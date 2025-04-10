using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Shared.Chat
{
    /// <summary>
    /// Command parameter definition for chat commands
    /// </summary>
    public class CommandParameter
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public bool IsRequired { get; set; }
        public string DefaultValue { get; set; }
        
        public CommandParameter(string name, string description, bool isRequired = true, string defaultValue = null)
        {
            Name = name;
            Description = description;
            IsRequired = isRequired;
            DefaultValue = defaultValue;
        }
    }

    /// <summary>
    /// Unified command handler delegate that supports both parsed args and raw messages
    /// </summary>
    /// <param name="username">Username of the sender</param>
    /// <param name="args">Parsed command arguments</param>
    /// <param name="message">Original raw message</param>
    /// <param name="tags">Message metadata tags</param>
    /// <param name="sourceObject">GameObject that triggered the command (optional)</param>
    /// <returns>True if command was handled successfully</returns>
    public delegate bool CommandHandler(string username, string[] args, string message, Dictionary<string, string> tags, GameObject sourceObject);

    /// <summary>
    /// Centralized processor for chat commands across all games
    /// </summary>
    public class ChatCommandProcessor : MonoBehaviour
    {
        private static ChatCommandProcessor _instance;
        private static readonly object _lock = new object();
        private static bool _isQuitting = false;
        
        // Command registry
        private Dictionary<string, CommandInfo> registeredCommands = new Dictionary<string, CommandInfo>(StringComparer.OrdinalIgnoreCase);
        
        // Command prefix
        [SerializeField] private string commandPrefix = "!";
        
        // Precompiled regex patterns
        private static readonly Regex CommandRegex = new Regex(@"^!(\w+)(?:\s+(.*))?$", RegexOptions.Compiled);
        
        // Cached string builder for processing
        private readonly StringBuilder stringBuilder = new StringBuilder(256);
        
        // Pool of string arrays for args to avoid GC allocations
        private readonly Queue<string[]> argsPool = new Queue<string[]>();
        private const int MAX_POOL_SIZE = 20;
        private const int DEFAULT_ARGS_SIZE = 8;
        
        // Logging
        [SerializeField] private bool logCommandRegistrations = true;
        [SerializeField] private bool logCommandExecutions = true;
        [SerializeField] private bool verboseLogging = false;
        
        /// <summary>
        /// Data container for registered commands
        /// </summary>
        private class CommandInfo
        {
            public string Name { get; set; }
            public string Description { get; set; }
            public string UsageExample { get; set; }
            public CommandHandler Callback { get; set; }
            public GameObject RegisteredBy { get; set; }
            public bool RequiresArgs { get; set; }
            public string[] AllowedRoles { get; set; }
        }
        
        public static ChatCommandProcessor Instance
        {
            get
            {
                if (_isQuitting)
                    return null;
                    
                lock (_lock)
                {
                    if (_instance == null)
                    {
                        _instance = FindObjectOfType<ChatCommandProcessor>();
                        
                        if (_instance == null)
                        {
                            GameObject go = new GameObject("ChatCommandProcessor");
                            _instance = go.AddComponent<ChatCommandProcessor>();
                            DontDestroyOnLoad(go);
                        }
                    }
                    return _instance;
                }
            }
        }
        
        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            
            _instance = this;
            DontDestroyOnLoad(gameObject);
            
            // Pre-populate the args pool
            for (int i = 0; i < 10; i++)
            {
                argsPool.Enqueue(new string[DEFAULT_ARGS_SIZE]);
            }
            
            if (verboseLogging)
            {
                Debug.Log("ChatCommandProcessor initialized");
            }
        }
        
        private void OnDestroy()
        {
            // Clean up all registered commands to prevent memory leaks
            registeredCommands.Clear();
        }
        
        private void OnApplicationQuit()
        {
            _isQuitting = true;
        }
        
        /// <summary>
        /// Register a new chat command
        /// </summary>
        /// <param name="commandName">Command name without the ! prefix</param>
        /// <param name="callback">Method to call when command is triggered</param>
        /// <param name="description">Short description of the command</param>
        /// <param name="usageExample">Example of how to use the command</param>
        /// <param name="requiresArgs">Whether the command requires arguments</param>
        /// <param name="registeredBy">GameObject that registered the command</param>
        /// <param name="allowedRoles">Optional list of roles that can use this command, null/empty means anyone</param>
        /// <returns>True if registration was successful</returns>
        public bool RegisterCommand(string commandName, CommandHandler callback, 
                               string description = "", string usageExample = "", bool requiresArgs = false,
                               GameObject registeredBy = null, string[] allowedRoles = null)
        {
            if (string.IsNullOrEmpty(commandName))
            {
                Debug.LogError("ChatCommandProcessor: Cannot register command with empty name");
                return false;
            }
            
            if (callback == null)
            {
                Debug.LogError($"ChatCommandProcessor: Cannot register command {commandName} with null callback");
                return false;
            }
            
            // Remove prefix if someone accidentally included it
            if (commandName.StartsWith(commandPrefix))
            {
                commandName = commandName.Substring(commandPrefix.Length);
            }
            
            // Check if command already exists
            if (registeredCommands.ContainsKey(commandName))
            {
                if (verboseLogging || logCommandRegistrations)
                {
                    Debug.LogWarning($"ChatCommandProcessor: Command {commandName} is already registered. Overwriting.");
                }
                // Clean up existing command to prevent memory leaks
                registeredCommands.Remove(commandName);
            }
            
            // Register the command
            registeredCommands[commandName] = new CommandInfo
            {
                Name = commandName,
                Description = description,
                UsageExample = usageExample,
                Callback = callback,
                RegisteredBy = registeredBy,
                RequiresArgs = requiresArgs,
                AllowedRoles = allowedRoles
            };
            
            if (logCommandRegistrations)
            {
                Debug.Log($"ChatCommandProcessor: Registered command {commandPrefix}{commandName}");
            }
            
            return true;
        }
        
        /// <summary>
        /// Unregister a chat command
        /// </summary>
        /// <param name="commandName">Command name without the ! prefix</param>
        /// <returns>True if unregistration was successful</returns>
        public bool UnregisterCommand(string commandName)
        {
            if (string.IsNullOrEmpty(commandName))
            {
                return false;
            }
            
            // Remove prefix if someone accidentally included it
            if (commandName.StartsWith(commandPrefix))
            {
                commandName = commandName.Substring(commandPrefix.Length);
            }
            
            // Try to remove the command
            if (registeredCommands.TryGetValue(commandName, out _))
            {
                registeredCommands.Remove(commandName);
                
                if (logCommandRegistrations)
                {
                    Debug.Log($"ChatCommandProcessor: Unregistered command {commandPrefix}{commandName}");
                }
                
                return true;
            }
            
            return false;
        }
        
        /// <summary>
        /// Unregister all commands registered by a specific GameObject
        /// </summary>
        /// <param name="registeredBy">GameObject that registered the commands</param>
        /// <returns>Number of commands unregistered</returns>
        public int UnregisterAllCommandsFrom(GameObject registeredBy)
        {
            if (registeredBy == null)
            {
                return 0;
            }
            
            int count = 0;
            List<string> commandsToRemove = new List<string>();
            
            // Find all commands registered by this GameObject
            foreach (var kvp in registeredCommands)
            {
                if (kvp.Value.RegisteredBy == registeredBy)
                {
                    commandsToRemove.Add(kvp.Key);
                    count++;
                }
            }
            
            // Remove the commands
            foreach (string command in commandsToRemove)
            {
                registeredCommands.Remove(command);
                
                if (logCommandRegistrations)
                {
                    Debug.Log($"ChatCommandProcessor: Unregistered command {commandPrefix}{command} from {registeredBy.name}");
                }
            }
            
            return count;
        }
        
        /// <summary>
        /// Check if a command is registered
        /// </summary>
        /// <param name="commandName">Command name without the ! prefix</param>
        /// <returns>True if the command is registered</returns>
        public bool IsCommandRegistered(string commandName)
        {
            if (string.IsNullOrEmpty(commandName))
            {
                return false;
            }
            
            // Remove prefix if someone accidentally included it
            if (commandName.StartsWith(commandPrefix))
            {
                commandName = commandName.Substring(commandPrefix.Length);
            }
            
            return registeredCommands.ContainsKey(commandName);
        }
        
        /// <summary>
        /// Process a chat message and execute any commands it contains
        /// </summary>
        /// <param name="username">Username of the message sender</param>
        /// <param name="message">Chat message content</param>
        /// <param name="tags">Optional metadata tags from the message</param>
        /// <param name="senderObject">Optional GameObject associated with the sender</param>
        /// <returns>True if a command was executed</returns>
        public bool ProcessChatMessage(string username, string message, Dictionary<string, string> tags = null, GameObject senderObject = null)
        {
            if (string.IsNullOrEmpty(message))
            {
                return false;
            }
            
            // Quick check to avoid regex for non-command messages
            if (!message.StartsWith(commandPrefix))
            {
                return false;
            }
            
            // Parse the command using regex
            Match match = CommandRegex.Match(message);
            if (!match.Success)
            {
                return false;
            }
            
            string commandName = match.Groups[1].Value.ToLowerInvariant();
            string argsString = match.Groups.Count > 2 ? match.Groups[2].Value : string.Empty;
            
            // Look up the command
            if (!registeredCommands.TryGetValue(commandName, out CommandInfo commandInfo))
            {
                // Command not found
                if (verboseLogging)
                {
                    Debug.Log($"ChatCommandProcessor: Command {commandPrefix}{commandName} not found");
                }
                return false;
            }
            
            // Check roles if required
            if (tags != null && commandInfo.AllowedRoles != null && commandInfo.AllowedRoles.Length > 0)
            {
                bool hasRole = false;
                
                if (tags.TryGetValue("badges", out string badgesValue) && !string.IsNullOrEmpty(badgesValue))
                {
                    foreach (string role in commandInfo.AllowedRoles)
                    {
                        // Special case for "Any" role
                        if (role.Equals("Any", StringComparison.OrdinalIgnoreCase))
                        {
                            hasRole = true;
                            break;
                        }
                        
                        if (badgesValue.Contains(role.ToLowerInvariant()))
                        {
                            hasRole = true;
                            break;
                        }
                    }
                }
                
                if (!hasRole)
                {
                    if (verboseLogging)
                    {
                        Debug.Log($"ChatCommandProcessor: User {username} lacks required role for command {commandPrefix}{commandName}");
                    }
                    return false;
                }
            }
            
            // Parse arguments
            string[] args = GetArgsFromPool();
            int argCount = 0;
            
            if (!string.IsNullOrEmpty(argsString))
            {
                argCount = ParseArgs(argsString, args);
            }
            
            // Check if command requires args but none were provided
            if (commandInfo.RequiresArgs && argCount == 0)
            {
                // Could notify the user that arguments are required
                if (verboseLogging)
                {
                    Debug.Log($"ChatCommandProcessor: Command {commandPrefix}{commandName} requires arguments but none were provided");
                }
                ReturnArgsToPool(args);
                return false;
            }
            
            try
            {
                // Log command execution if enabled
                if (logCommandExecutions)
                {
                    Debug.Log($"ChatCommandProcessor: Executing command {commandPrefix}{commandName} from {username}");
                }
                
                // Execute the command callback with both parsed args and original message
                bool result = commandInfo.Callback?.Invoke(username, args, message, tags, senderObject) ?? false;
                return result;
            }
            catch (Exception e)
            {
                Debug.LogError($"ChatCommandProcessor: Error executing command {commandPrefix}{commandName}: {e.Message}\n{e.StackTrace}");
                return false;
            }
            finally
            {
                // Return the args array to the pool
                ReturnArgsToPool(args);
            }
        }
        
        /// <summary>
        /// Get a string array from the pool or create a new one if the pool is empty
        /// </summary>
        private string[] GetArgsFromPool()
        {
            if (argsPool.Count > 0)
            {
                return argsPool.Dequeue();
            }
            
            return new string[DEFAULT_ARGS_SIZE];
        }
        
        /// <summary>
        /// Return a string array to the pool
        /// </summary>
        private void ReturnArgsToPool(string[] args)
        {
            if (args == null || argsPool.Count >= MAX_POOL_SIZE)
            {
                return;
            }
            
            // Clear the array to prevent memory leaks
            Array.Clear(args, 0, args.Length);
            argsPool.Enqueue(args);
        }
        
        /// <summary>
        /// Parse command arguments, handling quoted strings
        /// </summary>
        /// <param name="argsString">String containing command arguments</param>
        /// <param name="args">Array to store parsed arguments</param>
        /// <returns>Number of arguments parsed</returns>
        private int ParseArgs(string argsString, string[] args)
        {
            if (string.IsNullOrEmpty(argsString) || args == null || args.Length == 0)
            {
                return 0;
            }
            
            int argCount = 0;
            bool inQuotes = false;
            
            // Clear the StringBuilder
            stringBuilder.Clear();
            
            // Process the string character by character
            for (int i = 0; i < argsString.Length; i++)
            {
                char c = argsString[i];
                
                if (c == '"')
                {
                    // Toggle quote mode
                    inQuotes = !inQuotes;
                }
                else if (c == ' ' && !inQuotes)
                {
                    // Space outside of quotes - end of argument
                    if (stringBuilder.Length > 0)
                    {
                        if (argCount < args.Length)
                        {
                            args[argCount++] = stringBuilder.ToString();
                        }
                        stringBuilder.Clear();
                    }
                }
                else
                {
                    // Add character to current argument
                    stringBuilder.Append(c);
                }
            }
            
            // Add the last argument if there is one
            if (stringBuilder.Length > 0 && argCount < args.Length)
            {
                args[argCount++] = stringBuilder.ToString();
            }
            
            return argCount;
        }
        
        /// <summary>
        /// Get a list of all registered commands with their descriptions
        /// </summary>
        /// <returns>Dictionary of command names and descriptions</returns>
        public Dictionary<string, string> GetRegisteredCommandsInfo()
        {
            Dictionary<string, string> result = new Dictionary<string, string>();
            
            foreach (var kvp in registeredCommands)
            {
                result.Add(commandPrefix + kvp.Key, kvp.Value.Description);
            }
            
            return result;
        }
        
        /// <summary>
        /// Clear all registered commands
        /// </summary>
        public void ClearCommands()
        {
            registeredCommands.Clear();
            if (logCommandRegistrations)
            {
                Debug.Log("ChatCommandProcessor: Cleared all commands");
            }
        }
        
        /// <summary>
        /// Get the command prefix
        /// </summary>
        public string GetCommandPrefix()
        {
            return commandPrefix;
        }
        
        /// <summary>
        /// Set the command prefix
        /// </summary>
        public void SetCommandPrefix(string prefix)
        {
            if (!string.IsNullOrEmpty(prefix))
            {
                commandPrefix = prefix;
                if (logCommandRegistrations)
                {
                    Debug.Log($"ChatCommandProcessor: Command prefix set to '{prefix}'");
                }
            }
        }
    }
} 