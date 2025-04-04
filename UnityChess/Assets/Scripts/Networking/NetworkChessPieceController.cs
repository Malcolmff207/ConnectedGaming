using UnityEngine;
using UnityChess;
using Unity.Netcode;

/// <summary>
/// Controls which chess pieces are interactable based on the current player and turn.
/// This script should be added to your NetworkGameController GameObject.
/// </summary>
public class NetworkChessPieceController : MonoBehaviour
{
    // Reference to the networked chess controller
    private NetworkedChessController networkedChessController;
    
    private void Awake()
    {
        // Find the reference in Awake, before Start
        networkedChessController = FindObjectOfType<NetworkedChessController>();
        if (networkedChessController == null)
        {
            Debug.LogError("NetworkedChessController not found in scene!");
        }
    }
    
    private void Start()
    {
        // Subscribe to move executed event to update piece interactability
        GameManager.MoveExecutedEvent += UpdatePieceInteractability;
        
        // Initial update
        UpdatePieceInteractability();
    }

    private void OnDestroy()
    {
        // Unsubscribe from events
        GameManager.MoveExecutedEvent -= UpdatePieceInteractability;
    }
    
    private void Update()
    {
        // Continuously check and enforce correct piece interactability
        // This ensures that the right pieces are enabled/disabled as turns change
        if (Time.frameCount % 30 == 0) // Only check every 30 frames for performance
        {
            UpdatePieceInteractability();
        }
    }

    public void UpdatePieceInteractability()
    {
        // Only proceed if we have network connection and game manager
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsConnectedClient || 
            NetworkGameManager.Instance == null || BoardManager.Instance == null)
        {
            return;
        }

        // Determine which side this client controls
        Side localPlayerSide;
        
        // Get the side from NetworkedChessController if available
        if (networkedChessController != null)
        {
            localPlayerSide = networkedChessController.GetLocalPlayerSide();
        }
        else
        {
            // Fallback to the old method
            localPlayerSide = NetworkManager.Singleton.IsHost ? Side.White : Side.Black;
        }
        
        // Get the current turn from the network game manager
        Side currentTurn = NetworkGameManager.Instance.CurrentTurn.Value;
        
        // Find all visual pieces in the scene
        VisualPiece[] allPieces = FindObjectsOfType<VisualPiece>();
        
        foreach (VisualPiece piece in allPieces)
        {
            if (piece == null) continue;
            
            // A piece should only be enabled if:
            // 1. It belongs to the local player
            // 2. It's the local player's turn
            bool shouldBeEnabled = piece.PieceColor == localPlayerSide && currentTurn == localPlayerSide;
            
            // Set the piece's enabled state
            piece.enabled = shouldBeEnabled;
            
            // Debug logging
            if (piece.PieceColor == localPlayerSide && piece.enabled != shouldBeEnabled)
            {
                Debug.Log($"Setting {piece.PieceColor} piece {piece.name} enabled to {shouldBeEnabled}");
            }
        }
        
        Debug.Log($"Updated piece interactability - Local: {localPlayerSide}, Turn: {currentTurn}");
    }
}