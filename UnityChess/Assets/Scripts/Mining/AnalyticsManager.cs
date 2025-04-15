using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Firebase;
using Firebase.Analytics;
using Firebase.Extensions;
using Firebase.Database;
using UnityChess;
using Unity.Netcode;

/// <summary>
/// Manages Firebase Analytics integration and event logging for data mining purposes
/// </summary>
public class AnalyticsManager : MonoBehaviour
{
    // Singleton instance
    public static AnalyticsManager Instance { get; private set; }
    
    // Firebase references
    private DatabaseReference databaseReference;
    private bool isInitialized = false;
    
    // Event tracking counters
    private int matchesStarted = 0;
    private int matchesCompleted = 0;
    private Dictionary<string, int> openingMoveCount = new Dictionary<string, int>();
    private Dictionary<string, int> popularPiecesCount = new Dictionary<string, int>();
    
    // Event tracking counters for session
    private Dictionary<string, int> sessionStats = new Dictionary<string, int>();
    
    private void Awake()
    {
        // Setup singleton instance
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        
        // Initialize Firebase Analytics
        InitializeFirebase();
        
        // Initialize session stats
        ResetSessionStats();
    }
    
    private void Start()
    {
        // Subscribe to game events
        GameManager.NewGameStartedEvent += OnGameStarted;
        GameManager.GameEndedEvent += OnGameEnded;
        GameManager.MoveExecutedEvent += OnMoveExecuted;
        
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
        }
    }
    
    private void OnDestroy()
    {
        // Unsubscribe from events
        GameManager.NewGameStartedEvent -= OnGameStarted;
        GameManager.GameEndedEvent -= OnGameEnded;
        GameManager.MoveExecutedEvent -= OnMoveExecuted;
        
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }
        
        if (Instance == this)
        {
            Instance = null;
        }
    }
    
    private void ResetSessionStats()
    {
        sessionStats.Clear();
        sessionStats["games_played"] = 0;
        sessionStats["white_wins"] = 0;
        sessionStats["black_wins"] = 0;
        sessionStats["draws"] = 0;
        sessionStats["total_moves"] = 0;
        sessionStats["captures"] = 0;
        sessionStats["checks"] = 0;
        sessionStats["checkmates"] = 0;
    }
    
    private void InitializeFirebase()
    {
        // Check dependencies
        FirebaseApp.CheckAndFixDependenciesAsync().ContinueWithOnMainThread(task => 
        {
            if (task.Exception != null)
            {
                Debug.LogError($"Failed to initialize Firebase: {task.Exception}");
                return;
            }
            
            DependencyStatus dependencyStatus = task.Result;
            if (dependencyStatus == DependencyStatus.Available)
            {
                try
                {
                    // Initialize Firebase Analytics
                    FirebaseAnalytics.SetAnalyticsCollectionEnabled(true);
                    
                    // Initialize Database for analytics storage
                    FirebaseDatabase database = FirebaseDatabase.GetInstance("https://connectedgaming-18bcb-default-rtdb.europe-west1.firebasedatabase.app/");
                    
                    databaseReference = database.RootReference;
                    
                    isInitialized = true;
                    Debug.Log("Firebase Analytics initialized successfully!");
                    
                    // Log app open event
                    FirebaseAnalytics.LogEvent(FirebaseAnalytics.EventAppOpen);
                }
                catch (Exception e)
                {
                    Debug.LogError($"Error initializing Firebase components: {e.Message}");
                }
            }
            else
            {
                Debug.LogError($"Could not resolve all Firebase dependencies: {dependencyStatus}");
            }
        });
    }
    
    // Event handlers
    private void OnGameStarted()
    {
        matchesStarted++;
        sessionStats["games_played"]++;
        
        if (!isInitialized) return;
        
        string matchId = Guid.NewGuid().ToString();
        
        // Log game start event to Firebase Analytics
        Parameter[] parameters = {
            new Parameter("match_id", matchId),
            new Parameter("starting_side", GameManager.Instance.StartingSide.ToString()),
            new Parameter("network_game", (NetworkManager.Singleton != null && NetworkManager.Singleton.IsConnectedClient).ToString())
        };
        
        FirebaseAnalytics.LogEvent("game_started", parameters);
        
        // Store match data in Firebase Database
        StoreMatchStartData(matchId);
    }
    
    private void OnGameEnded()
    {
        matchesCompleted++;
        
        if (!isInitialized) return;
        
        // Get game result information
        bool hasLatestMove = GameManager.Instance.HalfMoveTimeline.TryGetCurrent(out HalfMove latestHalfMove);
        string result = "unknown";
        
        if (hasLatestMove)
        {
            if (latestHalfMove.CausedCheckmate)
            {
                result = $"{latestHalfMove.Piece.Owner}_win";
                // Update session stats
                if (latestHalfMove.Piece.Owner == Side.White)
                    sessionStats["white_wins"]++;
                else
                    sessionStats["black_wins"]++;
            }
            else if (latestHalfMove.CausedStalemate)
            {
                result = "draw";
                sessionStats["draws"]++;
            }
        }
        
        int moveCount = GameManager.Instance.HalfMoveTimeline.Count;
        
        // Log game end event to Firebase Analytics
        Parameter[] parameters = {
            new Parameter("result", result),
            new Parameter("move_count", moveCount),
            new Parameter("duration_seconds", Time.timeSinceLevelLoad)
        };
        
        FirebaseAnalytics.LogEvent("game_completed", parameters);
        
        // Store match result data in Firebase Database
        StoreMatchResultData(result, moveCount);
    }
    
    private void OnMoveExecuted()
    {
        // Update session stats
        sessionStats["total_moves"]++;
        
        if (!isInitialized) return;
        
        // Get the latest move
        bool hasLatestMove = GameManager.Instance.HalfMoveTimeline.TryGetCurrent(out HalfMove latestHalfMove);
        if (!hasLatestMove)
            return;
            
        // Update session stats for important move types
        if (latestHalfMove.CausedCheck)
            sessionStats["checks"]++;
        if (latestHalfMove.CausedCheckmate)
            sessionStats["checkmates"]++;
        
        // For first few moves, track opening statistics
        if (GameManager.Instance.HalfMoveTimeline.Count <= 10)
        {
            string moveName = latestHalfMove.ToAlgebraicNotation();
            
            // Store opening move in database
            StoreOpeningMoveData(moveName, GameManager.Instance.HalfMoveTimeline.Count);
            
            // Update local statistics
            if (openingMoveCount.ContainsKey(moveName))
                openingMoveCount[moveName]++;
            else
                openingMoveCount[moveName] = 1;
        }
        
        // Track piece usage statistics
        string pieceType = latestHalfMove.Piece.GetType().Name;
        if (popularPiecesCount.ContainsKey(pieceType))
            popularPiecesCount[pieceType]++;
        else
            popularPiecesCount[pieceType] = 1;
        
        // Store move data in Firebase
        StoreMoveData(latestHalfMove);
    }
    
    private void OnClientConnected(ulong clientId)
    {
        if (!isInitialized) return;
        
        // Log client connection event
        Parameter[] parameters = {
            new Parameter("client_id", clientId.ToString()),
            new Parameter("is_host", NetworkManager.Singleton.IsHost.ToString())
        };
        
        FirebaseAnalytics.LogEvent("player_connected", parameters);
    }
    
    private void OnClientDisconnected(ulong clientId)
    {
        if (!isInitialized) return;
        
        // Log client disconnection event
        Parameter[] parameters = {
            new Parameter("client_id", clientId.ToString()),
            new Parameter("game_in_progress", (GameManager.Instance.HalfMoveTimeline.Count > 0).ToString())
        };
        
        FirebaseAnalytics.LogEvent("player_disconnected", parameters);
    }
    
    // Database storage methods
    private void StoreMatchStartData(string matchId)
    {
        if (databaseReference == null) return;
        
        string userId = GetCurrentUserId();
        
        Dictionary<string, object> matchData = new Dictionary<string, object>
        {
            { "start_time", DateTime.UtcNow.ToString("o") },
            { "user_id", userId },
            { "starting_side", GameManager.Instance.StartingSide.ToString() },
            { "is_network_game", NetworkManager.Singleton != null && NetworkManager.Singleton.IsConnectedClient }
        };
        
        databaseReference.Child("matches").Child(matchId).SetValueAsync(matchData)
            .ContinueWithOnMainThread(task => 
            {
                if (task.Exception != null)
                {
                    Debug.LogError($"Failed to store match start data: {task.Exception}");
                }
            });
            
        // Store match ID in player prefs for tracking the current match
        PlayerPrefs.SetString("CurrentMatchId", matchId);
    }
    
    private void StoreMatchResultData(string result, int moveCount)
    {
        if (databaseReference == null) return;
        
        string matchId = PlayerPrefs.GetString("CurrentMatchId", Guid.NewGuid().ToString());
        
        Dictionary<string, object> resultData = new Dictionary<string, object>
        {
            { "end_time", DateTime.UtcNow.ToString("o") },
            { "result", result },
            { "move_count", moveCount },
            { "duration_seconds", Time.timeSinceLevelLoad }
        };
        
        databaseReference.Child("matches").Child(matchId).UpdateChildrenAsync(resultData)
            .ContinueWithOnMainThread(task => 
            {
                if (task.Exception != null)
                {
                    Debug.LogError($"Failed to store match result data: {task.Exception}");
                }
            });
    }
    
    private void StoreOpeningMoveData(string moveName, int moveNumber)
    {
        if (databaseReference == null) return;
        
        string matchId = PlayerPrefs.GetString("CurrentMatchId", "unknown");
        
        Dictionary<string, object> moveData = new Dictionary<string, object>
        {
            { "move_name", moveName },
            { "move_number", moveNumber },
            { "timestamp", DateTime.UtcNow.ToString("o") }
        };
        
        databaseReference.Child("opening_moves").Child(matchId).Child(moveNumber.ToString()).SetValueAsync(moveData)
            .ContinueWithOnMainThread(task => 
            {
                if (task.Exception != null)
                {
                    Debug.LogError($"Failed to store opening move data: {task.Exception}");
                }
            });
            
        // Also increment the global counter for this opening move
        databaseReference.Child("opening_stats").Child(moveName).RunTransaction(data =>
        {
            int currentCount = 0;
            if (data.Value != null)
            {
                currentCount = Convert.ToInt32(data.Value);
            }
            data.Value = currentCount + 1;
            return TransactionResult.Success(data);
        });
    }
    
    private void StoreMoveData(HalfMove move)
    {
        if (databaseReference == null) return;
        
        string matchId = PlayerPrefs.GetString("CurrentMatchId", "unknown");
        int moveIndex = GameManager.Instance.HalfMoveTimeline.HeadIndex;
        
        // Get values from the HalfMove object based on available properties
        Dictionary<string, object> moveData = new Dictionary<string, object>
        {
            { "piece_type", move.Piece.GetType().Name },
            { "piece_owner", move.Piece.Owner.ToString() },
            { "algebraic_notation", move.ToAlgebraicNotation() },
            { "is_check", move.CausedCheck },
            { "is_checkmate", move.CausedCheckmate },
            { "is_stalemate", move.CausedStalemate },
            { "timestamp", DateTime.UtcNow.ToString("o") }
        };
        
        databaseReference.Child("moves").Child(matchId).Child(moveIndex.ToString()).SetValueAsync(moveData)
            .ContinueWithOnMainThread(task => 
            {
                if (task.Exception != null)
                {
                    Debug.LogError($"Failed to store move data: {task.Exception}");
                }
            });
    }
    
    // Public methods for logging events
    public void LogDLCPurchase(string profileId, string profileName, int price)
    {
        if (!isInitialized) return;
        
        // Log purchase event to Firebase Analytics
        Parameter[] parameters = {
            new Parameter("item_id", profileId),
            new Parameter("item_name", profileName),
            new Parameter("price", price),
            new Parameter("currency", "credits"),
            new Parameter("value", (double)price)
        };
        
        FirebaseAnalytics.LogEvent(FirebaseAnalytics.EventPurchase, parameters);
    }
    
    public void LogGameStateSaved(string saveId, string saveName)
    {
        if (!isInitialized) return;
        
        // Log save game event to Firebase Analytics
        Parameter[] parameters = {
            new Parameter("save_id", saveId),
            new Parameter("save_name", saveName),
            new Parameter("moves_count", GameManager.Instance.HalfMoveTimeline.Count)
        };
        
        FirebaseAnalytics.LogEvent("game_state_saved", parameters);
        
        // Store save event in Firebase Database
        if (databaseReference != null)
        {
            string userId = GetCurrentUserId();
            
            Dictionary<string, object> saveData = new Dictionary<string, object>
            {
                { "save_id", saveId },
                { "save_name", saveName },
                { "timestamp", DateTime.UtcNow.ToString("o") },
                { "user_id", userId },
                { "move_count", GameManager.Instance.HalfMoveTimeline.Count }
            };
            
            databaseReference.Child("save_events").Child(userId).Child(saveId).SetValueAsync(saveData);
        }
    }
    
    public void LogGameStateLoaded(string saveId, string saveName)
    {
        if (!isInitialized) return;
        
        // Log load game event to Firebase Analytics
        Parameter[] parameters = {
            new Parameter("save_id", saveId),
            new Parameter("save_name", saveName)
        };
        
        FirebaseAnalytics.LogEvent("game_state_loaded", parameters);
        
        // Store load event in Firebase Database
        if (databaseReference != null)
        {
            string userId = GetCurrentUserId();
            
            Dictionary<string, object> loadData = new Dictionary<string, object>
            {
                { "save_id", saveId },
                { "save_name", saveName },
                { "timestamp", DateTime.UtcNow.ToString("o") },
                { "user_id", userId }
            };
            
            databaseReference.Child("load_events").Child(userId).Push().SetValueAsync(loadData);
        }
    }
    
    // Public methods for querying analytics data
    public void GetMostPopularOpeningMoves(int count, Action<Dictionary<string, int>> callback)
    {
        if (databaseReference == null)
        {
            callback?.Invoke(new Dictionary<string, int>());
            return;
        }
        
        databaseReference.Child("opening_stats").OrderByValue().LimitToLast(count)
            .GetValueAsync().ContinueWithOnMainThread(task => 
            {
                if (task.IsFaulted || task.IsCanceled)
                {
                    Debug.LogError($"Failed to query opening stats: {task.Exception}");
                    callback?.Invoke(new Dictionary<string, int>());
                    return;
                }
                
                Dictionary<string, int> result = new Dictionary<string, int>();
                foreach (var child in task.Result.Children)
                {
                    string moveName = child.Key;
                    int count = Convert.ToInt32(child.Value);
                    result[moveName] = count;
                }
                
                callback?.Invoke(result);
            });
    }
    
    public void GetTopPurchasedDLCItems(int count, Action<Dictionary<string, int>> callback)
    {
        if (databaseReference == null)
        {
            callback?.Invoke(new Dictionary<string, int>());
            return;
        }
        
        // Query the purchases node to get most purchased items
        databaseReference.Child("purchases").GetValueAsync().ContinueWithOnMainThread(task => 
        {
            if (task.IsFaulted || task.IsCanceled)
            {
                Debug.LogError($"Failed to query purchase stats: {task.Exception}");
                callback?.Invoke(new Dictionary<string, int>());
                return;
            }
            
            Dictionary<string, int> purchaseCounts = new Dictionary<string, int>();
            
            // Aggregate purchase counts by profile ID
            foreach (var userNode in task.Result.Children)
            {
                foreach (var purchaseNode in userNode.Children)
                {
                    string profileId = purchaseNode.Child("profileId").Value?.ToString();
                    if (!string.IsNullOrEmpty(profileId))
                    {
                        if (purchaseCounts.ContainsKey(profileId))
                            purchaseCounts[profileId]++;
                        else
                            purchaseCounts[profileId] = 1;
                    }
                }
            }
            
            // Sort and limit to requested count
            var sortedItems = purchaseCounts.OrderByDescending(pair => pair.Value).Take(count)
                              .ToDictionary(pair => pair.Key, pair => pair.Value);
                              
            callback?.Invoke(sortedItems);
        });
    }
    
    public void GetWinLossStatistics(Action<Dictionary<string, int>> callback)
    {
        if (databaseReference == null)
        {
            // Return session stats if Firebase is not available
            callback?.Invoke(sessionStats);
            return;
        }
        
        databaseReference.Child("matches").GetValueAsync().ContinueWithOnMainThread(task => 
        {
            if (task.IsFaulted || task.IsCanceled)
            {
                Debug.LogError($"Failed to query match stats: {task.Exception}");
                callback?.Invoke(sessionStats);
                return;
            }
            
            Dictionary<string, int> stats = new Dictionary<string, int>
            {
                { "total_matches", 0 },
                { "white_wins", 0 },
                { "black_wins", 0 },
                { "draws", 0 }
            };
            
            foreach (var matchNode in task.Result.Children)
            {
                stats["total_matches"]++;
                
                string result = matchNode.Child("result").Value?.ToString();
                if (result == "White_win")
                    stats["white_wins"]++;
                else if (result == "Black_win")
                    stats["black_wins"]++;
                else if (result == "draw")
                    stats["draws"]++;
            }
            
            // Merge with session stats for most up-to-date information
            stats["total_matches"] += sessionStats["games_played"];
            stats["white_wins"] += sessionStats["white_wins"];
            stats["black_wins"] += sessionStats["black_wins"];
            stats["draws"] += sessionStats["draws"];
            
            callback?.Invoke(stats);
        });
    }
    
    public void GetMostUsedPieces(Action<Dictionary<string, int>> callback)
    {
        if (databaseReference == null)
        {
            // Return local statistics if Firebase is not available
            callback?.Invoke(popularPiecesCount);
            return;
        }
        
        databaseReference.Child("moves").GetValueAsync().ContinueWithOnMainThread(task => 
        {
            if (task.IsFaulted || task.IsCanceled)
            {
                Debug.LogError($"Failed to query move stats: {task.Exception}");
                callback?.Invoke(popularPiecesCount);
                return;
            }
            
            Dictionary<string, int> pieceCounts = new Dictionary<string, int>();
            
            // Aggregate piece usage counts
            foreach (var matchNode in task.Result.Children)
            {
                foreach (var moveNode in matchNode.Children)
                {
                    string pieceType = moveNode.Child("piece_type").Value?.ToString();
                    if (!string.IsNullOrEmpty(pieceType))
                    {
                        if (pieceCounts.ContainsKey(pieceType))
                            pieceCounts[pieceType]++;
                        else
                            pieceCounts[pieceType] = 1;
                    }
                }
            }
            
            // Merge with local data for most up-to-date information
            foreach (var kvp in popularPiecesCount)
            {
                if (pieceCounts.ContainsKey(kvp.Key))
                    pieceCounts[kvp.Key] += kvp.Value;
                else
                    pieceCounts[kvp.Key] = kvp.Value;
            }
            
            callback?.Invoke(pieceCounts);
        });
    }
    
    public Dictionary<string, int> GetCurrentSessionStats()
    {
        return new Dictionary<string, int>(sessionStats);
    }
    
    // Utility methods
    private string GetCurrentUserId()
    {
        // If in a network game, use the client ID
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsConnectedClient)
        {
            return NetworkManager.Singleton.LocalClientId.ToString();
        }
        
        // Otherwise use device ID or a persistent user ID stored in PlayerPrefs
        string userId = PlayerPrefs.GetString("UserId", "");
        if (string.IsNullOrEmpty(userId))
        {
            userId = SystemInfo.deviceUniqueIdentifier;
            PlayerPrefs.SetString("UserId", userId);
        }
        
        return userId;
    }
}