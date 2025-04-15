using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityChess;
using Unity.Netcode;
using Firebase;
using Firebase.Database;
using Firebase.Extensions;

/// <summary>
/// Manages saving and restoring game states to/from Firebase
/// Simplified version that doesn't require UI interaction
/// </summary>
public class GameStateManager : MonoBehaviour
{
    // Singleton instance
    public static GameStateManager Instance { get; private set; }
    
    // Firebase references
    private DatabaseReference databaseReference;
    private bool firebaseInitialized = false;
    
    // Auto-save settings
    [SerializeField] private bool enableAutoSave = true;
    [SerializeField] private float autoSaveInterval = 60f; // Save every minute by default
    private string lastSavedState = "";
    
    // Cached saved games
    private List<SavedGameInfo> savedGames = new List<SavedGameInfo>();
    
    // Latest saved/loaded game info for analytics
    public string LastSaveId { get; private set; }
    public string LastSaveName { get; private set; }
    public string LastLoadId { get; private set; }
    public string LastLoadName { get; private set; }
    
    [Serializable]
    public class SavedGameInfo
    {
        public string Id;
        public string Name;
        public string Date;
        public string FEN;
        public string PlayerWhite;
        public string PlayerBlack;
        public int MoveCount;
    }
    
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
    }
    
    private void Start()
    {
        // Initialize Firebase
        InitializeFirebase();
        
        // Start the auto-save coroutine if enabled
        if (enableAutoSave)
        {
            StartCoroutine(AutoSaveCoroutine());
        }
        
        // Subscribe to game events - safely
        try
        {
            GameManager.GameEndedEvent += OnGameEnded;
        }
        catch (Exception)
        {
            Debug.LogWarning("Could not subscribe to GameEndedEvent");
        }
    }
    
    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
        
        // Unsubscribe from events - safely
        try
        {
            GameManager.GameEndedEvent -= OnGameEnded;
        }
        catch (Exception)
        {
            Debug.LogWarning("Could not unsubscribe from GameEndedEvent");
        }
    }
    
    private void InitializeFirebase()
    {
        try
        {
            // Check dependencies
            FirebaseApp.CheckAndFixDependenciesAsync().ContinueWithOnMainThread(task => 
            {
                if (task.Exception != null)
                {
                    Debug.LogError($"Failed to initialize Firebase: {task.Exception}");
                    return;
                }
                
                // Initialize Firebase Database with explicit URL
                FirebaseDatabase database = FirebaseDatabase.GetInstance("https://connectedgaming-18bcb-default-rtdb.europe-west1.firebasedatabase.app/");
                databaseReference = database.RootReference;
                
                firebaseInitialized = true;
                Debug.Log("Firebase Database initialized successfully!");
                
                // Load saved games list in the background
                StartCoroutine(LoadSavedGamesCoroutine());
            });
        }
        catch (Exception e)
        {
            Debug.LogError($"Firebase initialization error: {e.Message}");
        }
    }
    
    private IEnumerator AutoSaveCoroutine()
    {
        yield return new WaitForSeconds(10f); // Initial delay to let game initialize
        
        while (true)
        {
            // Only save if there's an active game
            if (GameManager.Instance != null && GameManager.Instance.CurrentBoard != null)
            {
                // Get current game state
                string currentState = GameManager.Instance.SerializeGame();
                
                // Only save if state has changed
                if (currentState != lastSavedState && !string.IsNullOrEmpty(currentState))
                {
                    string saveName = "AutoSave_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    SaveGameToFirebase(saveName, currentState);
                    lastSavedState = currentState;
                }
            }
            
            yield return new WaitForSeconds(autoSaveInterval);
        }
    }
    
    // Called when a game ends
    private void OnGameEnded()
    {
        // Save the final game state
        if (GameManager.Instance != null)
        {
            string finalState = GameManager.Instance.SerializeGame();
            string saveName = "Completed_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
            SaveGameToFirebase(saveName, finalState);
        }
    }
    
    // Public method to manually save the current game
    public void SaveCurrentGame(string saveName = "")
    {
        if (string.IsNullOrEmpty(saveName))
        {
            saveName = "Manual_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
        }
        
        if (GameManager.Instance != null)
        {
            string gameState = GameManager.Instance.SerializeGame();
            SaveGameToFirebase(saveName, gameState);
        }
    }
    
    // Internal method to save game to Firebase
    private void SaveGameToFirebase(string saveName, string fenString)
    {
        if (!firebaseInitialized || databaseReference == null)
        {
            Debug.LogWarning("Firebase not initialized. Can't save game.");
            return;
        }
        
        StartCoroutine(SaveGameCoroutine(saveName, fenString));
    }
    
    private IEnumerator SaveGameCoroutine(string saveName, string fenString)
    {
        Debug.Log($"Saving game: {saveName}...");
        
        // Generate a unique ID
        string saveId = Guid.NewGuid().ToString();
        
        // Store for analytics reference
        LastSaveId = saveId;
        LastSaveName = saveName;
        
        // Save to PlayerPrefs for analytics tracking
        PlayerPrefs.SetString("LastSaveId", saveId);
        PlayerPrefs.SetString("LastSaveName", saveName);
        
        // Get move count
        int moveCount = 0;
        if (GameManager.Instance != null && GameManager.Instance.HalfMoveTimeline != null)
        {
            moveCount = GameManager.Instance.HalfMoveTimeline.Count;
        }
        
        // Create save data
        Dictionary<string, object> saveData = new Dictionary<string, object>
        {
            { "id", saveId },
            { "name", saveName },
            { "date", DateTime.UtcNow.ToString("o") },
            { "fen", fenString },
            { "moveCount", moveCount },
            { "playerWhite", PlayerPrefs.GetString("PlayerName", "Player") },
            { "playerBlack", "Opponent" }
        };
        
        // Get user ID
        string userId = GetCurrentUserId();
        
        // Save to Firebase
        var saveTask = databaseReference.Child("savedGames").Child(userId).Child(saveId).SetValueAsync(saveData);
        
        while (!saveTask.IsCompleted)
        {
            yield return null;
        }
        
        if (saveTask.Exception != null)
        {
            Debug.LogError($"Failed to save game: {saveTask.Exception}");
            yield break;
        }
        
        Debug.Log($"Game saved successfully: {saveName}");
        
        // Update saved games list
        SavedGameInfo savedGame = new SavedGameInfo
        {
            Id = saveId,
            Name = saveName,
            Date = DateTime.UtcNow.ToString("o"),
            FEN = fenString,
            MoveCount = moveCount,
            PlayerWhite = PlayerPrefs.GetString("PlayerName", "Player"),
            PlayerBlack = "Opponent"
        };
        
        savedGames.Add(savedGame);
    }
    
    // Public method to load a saved game by ID
    public void LoadSavedGame(string saveId)
    {
        if (!firebaseInitialized || databaseReference == null)
        {
            Debug.LogWarning("Firebase not initialized. Can't load game.");
            return;
        }
        
        StartCoroutine(LoadSavedGameCoroutine(saveId));
    }
    
    private IEnumerator LoadSavedGameCoroutine(string saveId)
    {
        Debug.Log($"Loading game with ID: {saveId}...");
        
        // Get user ID
        string userId = GetCurrentUserId();
        
        // Query Firebase for the saved game
        var queryTask = databaseReference.Child("savedGames").Child(userId).Child(saveId).GetValueAsync();
        
        while (!queryTask.IsCompleted)
        {
            yield return null;
        }
        
        if (queryTask.Exception != null)
        {
            Debug.LogError($"Failed to query saved game: {queryTask.Exception}");
            yield break;
        }
        
        DataSnapshot snapshot = queryTask.Result;
        
        if (!snapshot.Exists)
        {
            Debug.LogWarning($"Saved game with ID {saveId} not found.");
            yield break;
        }
        
        // Extract game data
        string fenString = snapshot.Child("fen").Value?.ToString();
        string saveName = snapshot.Child("name").Value?.ToString();
        
        if (string.IsNullOrEmpty(fenString))
        {
            Debug.LogError("Invalid saved game data: missing FEN string.");
            yield break;
        }
        
        // Store for analytics reference
        LastLoadId = saveId;
        LastLoadName = saveName;
        
        // Save to PlayerPrefs for analytics tracking
        PlayerPrefs.SetString("LastLoadId", saveId);
        PlayerPrefs.SetString("LastLoadName", saveName);
        
        // Load the game
        LoadGame(fenString);
        
        Debug.Log($"Game '{saveName}' loaded successfully!");
    }
    
    // Internal method to load a game from FEN string
    public void LoadGame(string fenString)
    {
        if (GameManager.Instance != null)
        {
            // Check if we're in a networked game
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsConnectedClient)
            {
                // In a networked game, the host needs to load the game for all players
                if (NetworkManager.Singleton.IsHost || NetworkManager.Singleton.IsServer)
                {
                    LoadGameForAllClients(fenString);
                }
                else
                {
                    // Clients should request the host to load the game
                    RequestLoadGame(fenString);
                }
            }
            else
            {
                // In a local game, just load it directly
                GameManager.Instance.LoadGame(fenString);
            }
        }
    }
    
    // Public method to get the list of saved games
    public List<SavedGameInfo> GetSavedGames()
    {
        return new List<SavedGameInfo>(savedGames);
    }
    
    // Load the list of saved games from Firebase
    private IEnumerator LoadSavedGamesCoroutine()
    {
        Debug.Log("Loading saved games list...");
        
        // Clear the list
        savedGames.Clear();
        
        // Get user ID
        string userId = GetCurrentUserId();
        
        // Query saved games
        var queryTask = databaseReference.Child("savedGames").Child(userId).GetValueAsync();
        
        while (!queryTask.IsCompleted)
        {
            yield return null;
        }
        
        if (queryTask.Exception != null)
        {
            Debug.LogError($"Failed to query saved games: {queryTask.Exception}");
            yield break;
        }
        
        DataSnapshot snapshot = queryTask.Result;
        
        if (!snapshot.Exists)
        {
            Debug.Log("No saved games found.");
            yield break;
        }
        
        // Process each saved game
        foreach (DataSnapshot childSnapshot in snapshot.Children)
        {
            try
            {
                SavedGameInfo savedGame = new SavedGameInfo
                {
                    Id = childSnapshot.Child("id").Value?.ToString(),
                    Name = childSnapshot.Child("name").Value?.ToString(),
                    Date = childSnapshot.Child("date").Value?.ToString(),
                    FEN = childSnapshot.Child("fen").Value?.ToString(),
                    PlayerWhite = childSnapshot.Child("playerWhite").Value?.ToString(),
                    PlayerBlack = childSnapshot.Child("playerBlack").Value?.ToString(),
                    MoveCount = Convert.ToInt32(childSnapshot.Child("moveCount").Value)
                };
                
                savedGames.Add(savedGame);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Error processing saved game: {e.Message}");
            }
        }
        
        Debug.Log($"Loaded {savedGames.Count} saved games.");
    }
    
    // Delete a saved game by ID
    public void DeleteSavedGame(string saveId)
    {
        if (!firebaseInitialized || databaseReference == null)
        {
            Debug.LogWarning("Firebase not initialized. Can't delete game.");
            return;
        }
        
        StartCoroutine(DeleteSavedGameCoroutine(saveId));
    }
    
    private IEnumerator DeleteSavedGameCoroutine(string saveId)
    {
        Debug.Log($"Deleting saved game with ID: {saveId}...");
        
        // Get user ID
        string userId = GetCurrentUserId();
        
        // Delete from Firebase
        var deleteTask = databaseReference.Child("savedGames").Child(userId).Child(saveId).RemoveValueAsync();
        
        while (!deleteTask.IsCompleted)
        {
            yield return null;
        }
        
        if (deleteTask.Exception != null)
        {
            Debug.LogError($"Failed to delete saved game: {deleteTask.Exception}");
            yield break;
        }
        
        // Remove from cached list
        savedGames.RemoveAll(g => g.Id == saveId);
        
        Debug.Log("Saved game deleted successfully!");
    }
    
    // Helper methods for networked game loading
    private void RequestLoadGame(string fenString)
    {
        Debug.Log($"Client would request server to load game: {fenString.Substring(0, Math.Min(20, fenString.Length))}...");
        // In a real implementation, this would be a ServerRpc
    }
    
    private void LoadGameForAllClients(string fenString)
    {
        Debug.Log($"Server would broadcast game state to all clients: {fenString.Substring(0, Math.Min(20, fenString.Length))}...");
        // In a real implementation, this would be a ClientRpc
        
        // For now, just load it locally
        GameManager.Instance.LoadGame(fenString);
    }
    
    // Utility method to get current user ID
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