// ====== UnityMainThreadDispatcher.cs ======
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

public class UnityMainThreadDispatcher : MonoBehaviour
{
    private static UnityMainThreadDispatcher _instance;
    private static readonly object Lock = new object();
    private readonly Queue<Action> _executionQueue = new Queue<Action>();
    
    [Tooltip("Log warnings when queue size exceeds this threshold")]
    private int warningThreshold = 500;
    
    [Tooltip("Last time a warning was logged about queue size")]
    private float lastQueueWarningTime = 0f;
    
    [Tooltip("Minimum time between queue size warnings")]
    private float queueWarningInterval = 5f;
    
    private static bool _isQuitting = false;
    
    public static UnityMainThreadDispatcher Instance()
    {
        if (_isQuitting)
            return null;
            
        if (_instance == null)
        {
            lock (Lock)
            {
                if (_instance == null)
                {
                    // Create GameObject with component if it doesn't exist
                    var go = new GameObject("UnityMainThreadDispatcher");
                    _instance = go.AddComponent<UnityMainThreadDispatcher>();
                    DontDestroyOnLoad(go);
                }
            }
        }
        return _instance;
    }
    
    void Awake()
    {
        if (_instance == null)
        {
            _instance = this;
            DontDestroyOnLoad(this.gameObject);
        }
        else if (_instance != this)
        {
            Destroy(gameObject);
        }
    }
    
    void OnDestroy()
    {
        if (_instance == this)
        {
            // Clear the queue to prevent memory leaks
            lock (_executionQueue)
            {
                _executionQueue.Clear();
            }
            
            _instance = null;
        }
    }
    
    void OnApplicationQuit()
    {
        _isQuitting = true;
    }
    
    void Update()
    {
        // Execute all queued actions
        lock (_executionQueue)
        {
            // Log warning if queue is getting large
            int queueSize = _executionQueue.Count;
            if (queueSize > warningThreshold && Time.realtimeSinceStartup - lastQueueWarningTime > queueWarningInterval)
            {
                Debug.LogWarning($"MainThreadDispatcher queue size is large: {queueSize} actions queued");
                lastQueueWarningTime = Time.realtimeSinceStartup;
            }
            
            // Process the queue (up to a reasonable number per frame to prevent frame drops)
            int actionsThisFrame = Mathf.Min(queueSize, 100);
            for (int i = 0; i < actionsThisFrame; i++)
            {
                if (_executionQueue.Count > 0)
                {
                    try
                    {
                        _executionQueue.Dequeue().Invoke();
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"Error executing action on main thread: {e.Message}\n{e.StackTrace}");
                    }
                }
                else
                {
                    break;
                }
            }
        }
    }
    
    public void Enqueue(Action action)
    {
        if (action == null)
            return;
            
        lock (_executionQueue)
        {
            _executionQueue.Enqueue(action);
        }
    }
    
    public void ExecuteCoroutine(IEnumerator action)
    {
        if (action == null)
            return;
            
        lock (_executionQueue)
        {
            _executionQueue.Enqueue(() =>
            {
                StartCoroutine(action);
            });
        }
    }
    
    public int GetQueueSize()
    {
        lock (_executionQueue)
        {
            return _executionQueue.Count;
        }
    }
    
    public void ClearQueue()
    {
        lock (_executionQueue)
        {
            int count = _executionQueue.Count;
            _executionQueue.Clear();
            
            if (count > 0)
            {
                Debug.Log($"MainThreadDispatcher queue cleared, removed {count} pending actions");
            }
        }
    }
}