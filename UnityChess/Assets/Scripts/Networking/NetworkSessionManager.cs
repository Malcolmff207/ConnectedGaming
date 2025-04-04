using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using System;
using UnityChess;

/// <summary>
/// Manages network sessions, including session codes, reconnection logic, and session state.
/// </summary>
public class NetworkSessionManager : MonoBehaviour
{
    // Static instance for easy access
    public static NetworkSessionManager Instance { get; private set; }

    // Dictionary to store active sessions by code
    private Dictionary<string, SessionInfo> activeSessions = new Dictionary<string, SessionInfo>();

    // Current session info
    [System.Serializable]
    public class SessionInfo
    {
        public string sessionCode;
        public string ipAddress;
        public ushort port;
        public ulong hostClientId;
        public List<ulong> connectedClients = new List<ulong>();
        public Dictionary<ulong, Side> playerSides = new Dictionary<ulong, Side>();
    }

    // Current session the local player is part of
    public SessionInfo CurrentSession { get; private set; }
    
    // Event fired when disconnected with a reason
    public event Action<string> OnDisconnectedWithReason;
    
    // Maximum reconnection attempts
    private const int MaxReconnectAttempts = 3;
    private int currentReconnectAttempts = 0;
    
    // Reconnect delay in seconds
    private const float ReconnectDelay = 2.0f;
    
    private void Awake()
    {
        // Singleton pattern
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        
        // Keep this object when loading new scenes
        DontDestroyOnLoad(gameObject);
    }
    
    private void Start()
    {
        // Subscribe to network events
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnect;
        }
    }
    
    private void OnDestroy()
    {
        // Unsubscribe from network events
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnect;
        }
    }
    
    /// <summary>
    /// Creates a new session as host
    /// </summary>
    /// <param name="sessionCode">The session code to use</param>
    /// <returns>True if successful, false otherwise</returns>
    public bool CreateSession(string sessionCode)
    {
        // Configure transport
        if (NetworkManager.Singleton.NetworkConfig.NetworkTransport is UnityTransport transport)
        {
            string ipAddress = "127.0.0.1"; // Local host for testing
            ushort port = 7777;             // Default port
            
            transport.ConnectionData.Address = ipAddress;
            transport.ConnectionData.Port = port;
            
            // Create session info
            CurrentSession = new SessionInfo
            {
                sessionCode = sessionCode,
                ipAddress = ipAddress,
                port = port
            };
            
            // Start as host
            if (NetworkManager.Singleton.StartHost())
            {
                // Add to active sessions
                activeSessions[sessionCode] = CurrentSession;
                
                // Set host client ID
                CurrentSession.hostClientId = NetworkManager.Singleton.LocalClientId;
                
                // Add host to connected clients
                CurrentSession.connectedClients.Add(NetworkManager.Singleton.LocalClientId);
                
                // Assign White to host
                CurrentSession.playerSides[NetworkManager.Singleton.LocalClientId] = Side.White;
                
                Debug.Log($"Created session: {sessionCode}");
                return true;
            }
        }
        
        Debug.LogError("Failed to create session");
        return false;
    }
    
    /// <summary>
    /// Joins an existing session as client
    /// </summary>
    /// <param name="sessionCode">The session code to join</param>
    /// <returns>True if connection attempt started, false otherwise</returns>
    public bool JoinSession(string sessionCode)
    {
        // In a full implementation, here you would:
        // 1. Query a matchmaking server for IP and port based on session code
        // 2. Return an error if the session doesn't exist
        
        // For this demo, assume all sessions are on localhost:7777
        string ipAddress = "127.0.0.1";
        ushort port = 7777;
        
        // Configure transport
        if (NetworkManager.Singleton.NetworkConfig.NetworkTransport is UnityTransport transport)
        {
            transport.ConnectionData.Address = ipAddress;
            transport.ConnectionData.Port = port;
            
            // Create session info
            CurrentSession = new SessionInfo
            {
                sessionCode = sessionCode,
                ipAddress = ipAddress,
                port = port
            };
            
            // Start as client
            if (NetworkManager.Singleton.StartClient())
            {
                Debug.Log($"Joining session: {sessionCode}");
                return true;
            }
        }
        
        Debug.LogError("Failed to join session");
        return false;
    }
    
    /// <summary>
    /// Attempts to rejoin the last session
    /// </summary>
    /// <returns>True if reconnection attempt started, false otherwise</returns>
    public bool RejoinSession()
    {
        // Check if we have a previous session
        if (CurrentSession == null)
        {
            Debug.LogWarning("No previous session to rejoin");
            OnDisconnectedWithReason?.Invoke("No previous session to rejoin");
            return false;
        }
        
        // Reset reconnect attempts
        currentReconnectAttempts = 0;
        
        Debug.Log($"Attempting to rejoin session: {CurrentSession.sessionCode} at {CurrentSession.ipAddress}:{CurrentSession.port}");
        
        // Configure transport
        if (NetworkManager.Singleton.NetworkConfig.NetworkTransport is UnityTransport transport)
        {
            transport.ConnectionData.Address = CurrentSession.ipAddress;
            transport.ConnectionData.Port = CurrentSession.port;
            
            // Start as client
            if (NetworkManager.Singleton.StartClient())
            {
                Debug.Log($"Rejoin attempt started for session: {CurrentSession.sessionCode}");
                return true;
            }
            else
            {
                Debug.LogError("Failed to start client for rejoin");
                OnDisconnectedWithReason?.Invoke("Failed to rejoin - could not start client");
                return false;
            }
        }
        
        Debug.LogError("Transport not found or not supported");
        OnDisconnectedWithReason?.Invoke("Failed to rejoin - transport configuration error");
        return false;
    }
    
    /// <summary>
    /// Attempts to reconnect multiple times with increasing delay
    /// </summary>
    private IEnumerator ReconnectCoroutine()
    {
        while (currentReconnectAttempts < MaxReconnectAttempts)
        {
            Debug.Log($"Reconnect attempt {currentReconnectAttempts + 1}/{MaxReconnectAttempts}");
            
            // Configure transport
            if (NetworkManager.Singleton.NetworkConfig.NetworkTransport is UnityTransport transport)
            {
                transport.ConnectionData.Address = CurrentSession.ipAddress;
                transport.ConnectionData.Port = CurrentSession.port;
                
                // Start as client
                if (NetworkManager.Singleton.StartClient())
                {
                    Debug.Log($"Reconnecting to session: {CurrentSession.sessionCode}");
                    yield break; // Exit if connection started
                }
            }
            
            currentReconnectAttempts++;
            
            // Wait before next attempt with exponential backoff
            float delay = ReconnectDelay * (1 << currentReconnectAttempts);
            yield return new WaitForSeconds(delay);
        }
        
        // Failed to reconnect after max attempts
        Debug.LogError("Failed to rejoin session after multiple attempts");
        OnDisconnectedWithReason?.Invoke("Failed to rejoin session after multiple attempts");
        
        // Clear current session
        CurrentSession = null;
    }
    
    /// <summary>
    /// Leaves the current session
    /// </summary>
    public void LeaveSession()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.Shutdown();
        }
        
        // Don't clear CurrentSession to allow rejoining
    }
    
    /// <summary>
    /// Called when a client connects
    /// </summary>
    private void OnClientConnected(ulong clientId)
    {
        if (CurrentSession != null && !CurrentSession.connectedClients.Contains(clientId))
        {
            // Add to connected clients
            CurrentSession.connectedClients.Add(clientId);
            
            // If we're the host, assign Black to the first client that joins
            if (NetworkManager.Singleton.IsHost && clientId != NetworkManager.Singleton.LocalClientId)
            {
                CurrentSession.playerSides[clientId] = Side.Black;
            }
            
            Debug.Log($"Client {clientId} connected to session {CurrentSession.sessionCode}");
        }
    }
    
    /// <summary>
    /// Called when a client disconnects
    /// </summary>
    private void OnClientDisconnect(ulong clientId)
    {
        if (CurrentSession != null)
        {
            // Remove from connected clients
            CurrentSession.connectedClients.Remove(clientId);
            
            if (clientId == NetworkManager.Singleton.LocalClientId)
            {
                // We disconnected
                Debug.Log($"Disconnected from session {CurrentSession.sessionCode}");
                
                // Don't clear CurrentSession to allow rejoining
            }
            else
            {
                // Another player disconnected
                Debug.Log($"Client {clientId} disconnected from session {CurrentSession.sessionCode}");
            }
        }
    }
    
    /// <summary>
    /// Gets a side (White/Black) for a client
    /// </summary>
    public Side GetClientSide(ulong clientId)
    {
        if (CurrentSession != null && CurrentSession.playerSides.TryGetValue(clientId, out Side side))
        {
            return side;
        }
        
        // Default to White for host, Black for others
        return NetworkManager.Singleton.IsHost ? Side.White : Side.Black;
    }
}