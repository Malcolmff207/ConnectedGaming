using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Ensures proper player setup for networking, fixing issues with player objects and components
/// </summary>
public class NetworkPlayerSetup : NetworkBehaviour
{
    [SerializeField] private GameObject dlcManagerPrefab;
    
    // Static reference for easy access
    public static NetworkPlayerSetup Instance { get; private set; }
    
    // Called on both server and clients
    private void Awake()
    {
        // Set up singleton pattern
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }
    
    // Called when object is spawned on the network
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        
        // If this is the server, set up player objects
        if (IsServer)
        {
            // Register for network events
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnectedCallback;
        }
        
        Debug.Log($"NetworkPlayerSetup spawned: IsServer={IsServer}, IsHost={IsHost}, IsClient={IsClient}");
    }
    
    public override void OnNetworkDespawn()
    {
        // Clean up event subscriptions
        if (IsServer && NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnectedCallback;
        }
        
        base.OnNetworkDespawn();
    }
    
    // Called when a client connects to the server
    private void OnClientConnectedCallback(ulong clientId)
    {
        Debug.Log($"Client {clientId} connected - ensuring player object setup");
        
        // Make sure the client has a player object
        if (NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out NetworkClient client))
        {
            if (client.PlayerObject == null)
            {
                Debug.LogWarning($"Client {clientId} has no player object! Creating one...");
                CreatePlayerObjectForClient(clientId);
            }
            else
            {
                // Ensure the player object has the necessary components
                EnsurePlayerObjectHasComponents(client.PlayerObject);
            }
            
            // After setting up the player object, trigger profile synchronization
            StartCoroutine(TriggerProfileSyncAfterDelay(clientId, 1.0f));
        }
    }
    
    // Creates a player object for a client if one doesn't exist
    private void CreatePlayerObjectForClient(ulong clientId)
    {
        // This is a simplified example - in a real game you'd spawn a proper player prefab
        GameObject playerObject = new GameObject($"Player_{clientId}");
        NetworkObject networkObject = playerObject.AddComponent<NetworkObject>();
        
        // Add DLC manager component
        EnsurePlayerObjectHasComponents(networkObject);
        
        // Spawn the object and assign it to the client
        networkObject.SpawnAsPlayerObject(clientId);
        
        Debug.Log($"Created and spawned player object for client {clientId}");
    }
    
    // Ensures a player object has necessary components
    private void EnsurePlayerObjectHasComponents(NetworkObject playerObject)
    {
        if (playerObject == null) return;
        
        // Check if DLCManager component exists
        DLCManager dlcManager = playerObject.GetComponent<DLCManager>();
        if (dlcManager == null)
        {
            Debug.Log($"Adding DLCManager to player object {playerObject.name}");
            
            // If we have a prefab, instantiate it as a child
            if (dlcManagerPrefab != null)
            {
                GameObject dlcManagerInstance = Instantiate(dlcManagerPrefab, playerObject.transform);
                dlcManager = dlcManagerInstance.GetComponent<DLCManager>();
                
                if (dlcManager == null)
                {
                    Debug.LogError("DLCManager prefab doesn't have a DLCManager component!");
                    dlcManager = dlcManagerInstance.AddComponent<DLCManager>();
                }
            }
            else
            {
                // Otherwise add the component directly
                dlcManager = playerObject.gameObject.AddComponent<DLCManager>();
            }
        }
    }
    
    // New coroutine to trigger profile sync with a delay to ensure everything is initialized
    private IEnumerator TriggerProfileSyncAfterDelay(ulong clientId, float delay)
    {
        yield return new WaitForSeconds(delay);
        
        Debug.Log($"Triggering profile sync for client {clientId}");
        
        // If we have a GameUIDLCIntegration component in the scene, tell it to request a profile refresh
        GameUIDLCIntegration uiIntegration = FindObjectOfType<GameUIDLCIntegration>();
        if (uiIntegration != null)
        {
            // Access the method via reflection since it might be private
            var refreshMethod = uiIntegration.GetType().GetMethod("RequestAllProfileRefreshes", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                
            if (refreshMethod != null)
            {
                refreshMethod.Invoke(uiIntegration, null);
                Debug.Log("Triggered profile refresh via GameUIDLCIntegration");
            }
            else
            {
                Debug.LogWarning("Could not find RequestAllProfileRefreshes method in GameUIDLCIntegration");
            }
        }
        else
        {
            Debug.LogWarning("Could not find GameUIDLCIntegration to trigger profile refresh");
            
            // Alternative: try to find DLCManager instances and call their sync method directly
            DLCManager[] dlcManagers = FindObjectsOfType<DLCManager>();
            foreach (var manager in dlcManagers)
            {
                // Call the sync method if it exists
                var syncMethod = manager.GetType().GetMethod("SyncProfileWithNetwork", 
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                    
                if (syncMethod != null)
                {
                    syncMethod.Invoke(manager, null);
                    Debug.Log($"Triggered profile sync directly on DLCManager {manager.name}");
                }
            }
        }
        
        // Also notify clients that a new player has connected and they should share their profiles
        TriggerProfileSharingClientRpc(clientId);
    }
    
    // Public method to manually check and fix player objects
    public void VerifyAllPlayerObjects()
    {
        if (!IsServer) return;
        
        foreach (var clientPair in NetworkManager.Singleton.ConnectedClients)
        {
            ulong clientId = clientPair.Key;
            NetworkClient client = clientPair.Value;
            
            if (client.PlayerObject == null)
            {
                Debug.LogWarning($"Client {clientId} has no player object during verification! Creating one...");
                CreatePlayerObjectForClient(clientId);
            }
            else
            {
                EnsurePlayerObjectHasComponents(client.PlayerObject);
            }
        }
        
        Debug.Log("All player objects verified");
    }
    
    // Add these RPC methods for coordinating profile sharing
    [ClientRpc]
    private void TriggerProfileSharingClientRpc(ulong newClientId)
    {
        // Don't trigger for the new client itself
        if (NetworkManager.Singleton.LocalClientId == newClientId)
            return;
            
        Debug.Log($"Received request to share profile with new client {newClientId}");
        
        // Find our DLCManager
        DLCManager dlcManager = FindObjectOfType<DLCManager>();
        if (dlcManager != null)
        {
            // Call the sync method if it exists
            var syncMethod = dlcManager.GetType().GetMethod("SyncProfileWithNetwork", 
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                
            if (syncMethod != null)
            {
                syncMethod.Invoke(dlcManager, null);
                Debug.Log("Shared profile with new client");
            }
        }
    }
}