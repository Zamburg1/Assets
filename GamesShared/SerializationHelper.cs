using System.Collections.Generic;
using UnityEngine;
using System;
using System.Linq;
using System.IO;
using System.Text;
using System.Threading.Tasks;
#if UNITY_2018_4_OR_NEWER
using Newtonsoft.Json;
#endif

namespace Shared.Core
{
    /// <summary>
    /// Utility class for common serialization tasks across all games
    /// </summary>
    public static class SerializationHelper
    {
        /// <summary>
        /// Configuration for batching large data
        /// </summary>
        private const int DEFAULT_BATCH_SIZE = 1000;
        private const int MAX_BATCH_SIZE = 10000;
        
        /// <summary>
        /// Reusable StringBuilder to reduce allocations
        /// </summary>
        private static readonly StringBuilder _stringBuilder = new StringBuilder(8192);
        
#if UNITY_2018_4_OR_NEWER
        /// <summary>
        /// Reusable serialization settings to avoid creating new ones each time
        /// </summary>
        private static readonly JsonSerializerSettings _defaultSettings = new JsonSerializerSettings
        {
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
            NullValueHandling = NullValueHandling.Ignore,
            Formatting = Formatting.None
        };
        
        private static readonly JsonSerializerSettings _prettySettings = new JsonSerializerSettings
        {
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
            NullValueHandling = NullValueHandling.Ignore,
            Formatting = Formatting.Indented
        };
#endif
        
        /// <summary>
        /// Serializes a Dictionary to JSON using an intermediate representation
        /// </summary>
        /// <typeparam name="TKey">Dictionary key type</typeparam>
        /// <typeparam name="TValue">Dictionary value type</typeparam>
        /// <param name="dictionary">Dictionary to serialize</param>
        /// <returns>JSON string representation</returns>
        public static string SerializeDictionary<TKey, TValue>(Dictionary<TKey, TValue> dictionary)
        {
            if (dictionary == null)
                return "{}";
                
            try
            {
                // Create a serializable wrapper
                var entries = dictionary.Select(kvp => 
                    new SerializableKeyValuePair<TKey, TValue> { Key = kvp.Key, Value = kvp.Value })
                    .ToArray();
                    
                var wrapper = new SerializableDictionary<TKey, TValue> { Entries = entries };
                return JsonUtility.ToJson(wrapper);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error serializing dictionary: {ex.Message}");
                return "{}";
            }
        }
        
        /// <summary>
        /// Deserializes a Dictionary from JSON
        /// </summary>
        /// <typeparam name="TKey">Dictionary key type</typeparam>
        /// <typeparam name="TValue">Dictionary value type</typeparam>
        /// <param name="json">JSON string to deserialize</param>
        /// <returns>Deserialized Dictionary</returns>
        public static Dictionary<TKey, TValue> DeserializeDictionary<TKey, TValue>(string json)
        {
            try
            {
                if (string.IsNullOrEmpty(json))
                    return new Dictionary<TKey, TValue>();
                    
                var wrapper = JsonUtility.FromJson<SerializableDictionary<TKey, TValue>>(json);
                
                if (wrapper == null || wrapper.Entries == null)
                    return new Dictionary<TKey, TValue>();
                    
                // Convert back to dictionary with optimized capacity
                var result = new Dictionary<TKey, TValue>(wrapper.Entries.Length);
                foreach (var entry in wrapper.Entries)
                {
                    // Avoid duplicate keys which could happen if the JSON is manually edited
                    if (!result.ContainsKey(entry.Key))
                    {
                        result.Add(entry.Key, entry.Value);
                    }
                }
                
                return result;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error deserializing dictionary: {ex.Message}");
                return new Dictionary<TKey, TValue>();
            }
        }
        
        /// <summary>
        /// Helper method to save a dictionary to PlayerPrefs
        /// </summary>
        /// <typeparam name="TKey">Dictionary key type</typeparam>
        /// <typeparam name="TValue">Dictionary value type</typeparam>
        /// <param name="dictionary">Dictionary to save</param>
        /// <param name="key">PlayerPrefs key</param>
        public static void SaveDictionaryToPrefs<TKey, TValue>(Dictionary<TKey, TValue> dictionary, string key)
        {
            try
            {
                string json = SerializeDictionary(dictionary);
                PlayerPrefs.SetString(key, json);
                PlayerPrefs.Save();
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error saving dictionary to PlayerPrefs: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Helper method to load a dictionary from PlayerPrefs
        /// </summary>
        /// <typeparam name="TKey">Dictionary key type</typeparam>
        /// <typeparam name="TValue">Dictionary value type</typeparam>
        /// <param name="key">PlayerPrefs key</param>
        /// <returns>Loaded dictionary or empty if not found</returns>
        public static Dictionary<TKey, TValue> LoadDictionaryFromPrefs<TKey, TValue>(string key)
        {
            try
            {
                if (!PlayerPrefs.HasKey(key))
                    return new Dictionary<TKey, TValue>();
                    
                string json = PlayerPrefs.GetString(key);
                return DeserializeDictionary<TKey, TValue>(json);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error loading dictionary from PlayerPrefs: {ex.Message}");
                return new Dictionary<TKey, TValue>();
            }
        }
        
        /// <summary>
        /// Serialize object to JSON string with optional pretty printing
        /// </summary>
        public static string ToJson<T>(T obj, bool prettyPrint = false)
        {
            if (obj == null) return "{}";
            
            try
            {
#if UNITY_2018_4_OR_NEWER
                return JsonConvert.SerializeObject(obj, 
                    prettyPrint ? _prettySettings : _defaultSettings);
#else
                return JsonUtility.ToJson(obj, prettyPrint);
#endif
            }
            catch (Exception ex)
            {
                Debug.LogError($"Serialization error: {ex.Message}");
                return "{}";
            }
        }
        
        /// <summary>
        /// Deserialize JSON string to object
        /// </summary>
        public static T FromJson<T>(string json)
        {
            if (string.IsNullOrEmpty(json)) return default;
            
            try
            {
#if UNITY_2018_4_OR_NEWER
                return JsonConvert.DeserializeObject<T>(json, _defaultSettings);
#else
                return JsonUtility.FromJson<T>(json);
#endif
            }
            catch (Exception ex)
            {
                Debug.LogError($"Deserialization error: {ex.Message}\nJSON: {json.Substring(0, Math.Min(100, json.Length))}...");
                return default;
            }
        }
        
        /// <summary>
        /// Save object to file as JSON
        /// </summary>
        public static bool SaveToFile<T>(T obj, string filePath, bool prettyPrint = false)
        {
            try
            {
                string json = ToJson(obj, prettyPrint);
                File.WriteAllText(filePath, json);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error saving to file {filePath}: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Load object from JSON file
        /// </summary>
        public static T LoadFromFile<T>(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    Debug.LogWarning($"File not found: {filePath}");
                    return default;
                }
                
                string json = File.ReadAllText(filePath);
                return FromJson<T>(json);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error loading from file {filePath}: {ex.Message}");
                return default;
            }
        }
        
        /// <summary>
        /// Serialize a large collection to JSON asynchronously in batches to prevent memory spikes
        /// </summary>
        /// <typeparam name="T">Collection item type</typeparam>
        /// <param name="collection">The collection to serialize</param>
        /// <param name="batchSize">Number of items per batch (default: 1000)</param>
        /// <param name="prettyPrint">Whether to use pretty printing</param>
        /// <returns>JSON string of the entire collection</returns>
        public static async Task<string> SerializeLargeCollectionAsync<T>(
            IEnumerable<T> collection, 
            int batchSize = DEFAULT_BATCH_SIZE,
            bool prettyPrint = false)
        {
            if (collection == null) return "[]";
            
            // Validate batch size
            batchSize = Mathf.Clamp(batchSize, 1, MAX_BATCH_SIZE);
            
            try
            {
                _stringBuilder.Clear();
                _stringBuilder.Append('[');
                
                int itemCount = 0;
                int batchCount = 0;
                bool isFirst = true;
                List<T> batch = new List<T>(batchSize);
                
                // Process in batches
                foreach (var item in collection)
                {
                    batch.Add(item);
                    itemCount++;
                    
                    // When batch is full, serialize it
                    if (batch.Count >= batchSize)
                    {
                        await SerializeBatchAsync(batch, isFirst);
                        isFirst = false;
                        batch.Clear();
                        batchCount++;
                        
                        // Allow the GC to collect between batches
                        await Task.Yield();
                    }
                }
                
                // Process any remaining items
                if (batch.Count > 0)
                {
                    await SerializeBatchAsync(batch, isFirst);
                    batchCount++;
                }
                
                _stringBuilder.Append(']');
                
                Debug.Log($"Serialized {itemCount} items in {batchCount} batches");
                return _stringBuilder.ToString();
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error serializing large collection: {ex.Message}");
                return "[]";
            }
            
            // Helper method to serialize a batch
            async Task SerializeBatchAsync(List<T> batchItems, bool first)
            {
                // Process each item individually to avoid creating another large collection
                for (int i = 0; i < batchItems.Count; i++)
                {
                    if (!first || i > 0)
                    {
                        _stringBuilder.Append(',');
                    }
                    
                    string itemJson = ToJson(batchItems[i], prettyPrint);
                    _stringBuilder.Append(itemJson);
                    
                    // Every 100 items, yield to prevent UI freezing
                    if (i % 100 == 0 && i > 0)
                    {
                        await Task.Yield();
                    }
                }
            }
        }
        
        /// <summary>
        /// Save a large collection to a file as JSON in batches to prevent memory spikes
        /// </summary>
        public static async Task<bool> SaveLargeCollectionToFileAsync<T>(
            IEnumerable<T> collection, 
            string filePath, 
            int batchSize = DEFAULT_BATCH_SIZE,
            bool prettyPrint = false)
        {
            try
            {
                // Ensure directory exists
                string directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                
                // Create temp file path
                string tempFilePath = filePath + ".temp";
                
                // Use a StreamWriter for efficient file writing
                using (StreamWriter writer = new StreamWriter(tempFilePath, false, Encoding.UTF8))
                {
                    await writer.WriteAsync('[');
                    
                    int itemCount = 0;
                    int batchCount = 0;
                    bool isFirst = true;
                    List<T> batch = new List<T>(batchSize);
                    
                    // Process in batches
                    foreach (var item in collection)
                    {
                        batch.Add(item);
                        itemCount++;
                        
                        // When batch is full, write it
                        if (batch.Count >= batchSize)
                        {
                            await WriteBatchAsync(writer, batch, isFirst);
                            isFirst = false;
                            batch.Clear();
                            batchCount++;
                            
                            // Allow the GC to collect between batches
                            await Task.Yield();
                        }
                    }
                    
                    // Process any remaining items
                    if (batch.Count > 0)
                    {
                        await WriteBatchAsync(writer, batch, isFirst);
                        batchCount++;
                    }
                    
                    await writer.WriteAsync(']');
                    
                    Debug.Log($"Wrote {itemCount} items in {batchCount} batches to {filePath}");
                }
                
                // Replace the original file with the temp file
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
                File.Move(tempFilePath, filePath);
                
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error saving large collection to file {filePath}: {ex.Message}");
                return false;
            }
            
            // Helper method to write a batch
            async Task WriteBatchAsync(StreamWriter writer, List<T> batchItems, bool first)
            {
                for (int i = 0; i < batchItems.Count; i++)
                {
                    if (!first || i > 0)
                    {
                        await writer.WriteAsync(',');
                    }
                    
                    string itemJson = ToJson(batchItems[i], prettyPrint);
                    await writer.WriteAsync(itemJson);
                    
                    // Every 100 items, yield to prevent UI freezing
                    if (i % 100 == 0 && i > 0)
                    {
                        await Task.Yield();
                    }
                }
            }
        }
    }
    
    /// <summary>
    /// Serializable wrapper for dictionaries
    /// </summary>
    [Serializable]
    public class SerializableDictionary<TKey, TValue>
    {
        public SerializableKeyValuePair<TKey, TValue>[] Entries;
    }
    
    /// <summary>
    /// Serializable key-value pair
    /// </summary>
    [Serializable]
    public class SerializableKeyValuePair<TKey, TValue>
    {
        public TKey Key;
        public TValue Value;
    }
} 