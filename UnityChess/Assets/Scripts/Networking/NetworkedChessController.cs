using Unity.Netcode;
using UnityChess;
using UnityEngine;

/// <summary>
/// Main controller for networked chess game
/// </summary>
public class NetworkedChessController : MonoBehaviour
{
    // Add singleton pattern
    public static NetworkedChessController Instance { get; private set; }
    
    [Header("References")]
    [SerializeField] private NetworkManager networkManager;
    [SerializeField] private GameObject networkUICanvas;
    
    // Added reference to NetworkSessionManager
    private NetworkSessionManager sessionManager;
    
    // References to network components
    private NetworkGameManager networkGameManager;
    private ChessNetworkSync chessNetworkSync;
    
    [Header("Prefabs")]
    [SerializeField] private GameObject networkPrefab;
    
    private void Awake()
    {
        // Set up singleton instance
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        
        // Ensure we have the NetworkManager reference
        if (networkManager == null)
        {
            Debug.LogError("NetworkManager reference is missing!");
        }
        
        // Get or find the session manager
        sessionManager = FindObjectOfType<NetworkSessionManager>();
        if (sessionManager == null)
        {
            Debug.LogError("NetworkSessionManager is missing! Add it to the scene.");
        }
    }
    
    private void Start()
    {
        // Find the components in Start instead of Awake to ensure they're initialized
        networkGameManager = GetComponent<NetworkGameManager>();
        if (networkGameManager == null)
        {
            Debug.LogError("NetworkGameManager component is missing! Make sure it's on the same GameObject.");
            // Try to find it in the scene as fallback
            networkGameManager = FindObjectOfType<NetworkGameManager>();
            if (networkGameManager != null)
            {
                Debug.Log("Found NetworkGameManager in scene - using that instead.");
            }
        }
        
        chessNetworkSync = GetComponent<ChessNetworkSync>();
        if (chessNetworkSync == null)
        {
            Debug.LogError("ChessNetworkSync component is missing! Make sure it's on the same GameObject.");
            // Try to find it in the scene as fallback
            chessNetworkSync = FindObjectOfType<ChessNetworkSync>();
            if (chessNetworkSync != null)
            {
                Debug.Log("Found ChessNetworkSync in scene - using that instead.");
            }
        }
        
        // Subscribe to network events
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
        
        // Set up network manager
        if (networkPrefab != null && NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.NetworkConfig.PlayerPrefab = networkPrefab;
        }
        
        // Subscribe to session manager events
        if (sessionManager != null)
        {
            sessionManager.OnDisconnectedWithReason += OnDisconnectWithReason;
        }
    }
    
    private void OnDestroy()
    {
        // Clean up singleton instance
        if (Instance == this)
        {
            Instance = null;
        }
        
        // Unsubscribe from network events
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }
        
        // Unsubscribe from session manager events
        if (sessionManager != null)
        {
            sessionManager.OnDisconnectedWithReason -= OnDisconnectWithReason;
        }
    }
    
    private void OnClientConnected(ulong clientId)
    {
        Debug.Log($"Client connected: {clientId}");
        
        // If we're the server and a client just connected
        if (NetworkManager.Singleton.IsServer && clientId != NetworkManager.Singleton.LocalClientId)
        {
            Debug.Log("Starting new game for connected client");
            
            // Start a new game or sync the current game state
            GameManager.Instance.StartNewGame();
            
            // Get the client's side from the session manager
            if (sessionManager != null)
            {
                Side clientSide = sessionManager.GetClientSide(clientId);
                Debug.Log($"Assigned side {clientSide} to client {clientId}");
            }
        }
    }
    
    private void OnClientDisconnected(ulong clientId)
    {
        Debug.Log($"Client disconnected: {clientId}");
    }
    
    private void OnDisconnectWithReason(string reason)
    {
        Debug.LogWarning($"Disconnected: {reason}");
        
        // Here you could show a UI message to the user
        // or handle specific disconnect scenarios
    }
    
    // Performance logging method for ping measurement
    public void LogNetworkPerformance()
    {
        if (NetworkManager.Singleton.IsConnectedClient && chessNetworkSync != null)
        {
            float ping = chessNetworkSync.GetPing();
            Debug.Log($"Current ping: {ping:F2} ms");
        }
    }
    
    // Helper method to determine which side this client controls
    public Side GetLocalPlayerSide()
    {
        if (sessionManager != null && NetworkManager.Singleton != null)
        {
            return sessionManager.GetClientSide(NetworkManager.Singleton.LocalClientId);
        }
        
        // Default fallback
        return NetworkManager.Singleton.IsHost ? Side.White : Side.Black;
    }
}