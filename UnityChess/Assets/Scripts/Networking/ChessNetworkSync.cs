using System.Collections;
using Unity.Netcode;
using UnityChess;
using UnityEngine;

/// <summary>
/// Handles networking for the chess game, including game state synchronization and board state management.
/// </summary>
public class ChessNetworkSync : NetworkBehaviour
{
    // Reference to GameManager
    private GameManager gameManager;
    // Reference to BoardManager
    private BoardManager boardManager;

    // Network variable to track current game state
    public NetworkVariable<NetworkFEN> CurrentFEN = new NetworkVariable<NetworkFEN>(
        new NetworkFEN { Value = "" },
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);
    
    // Struct to contain FEN string representation of the board
    public struct NetworkFEN : INetworkSerializable
    {
        public string Value;
        
        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Value);
        }
    }

    private void Awake()
    {
        // Get references to managers - but don't initialize here
        // We'll check and initialize them when needed
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        
        // Subscribe to network FEN changes
        CurrentFEN.OnValueChanged += OnFENChanged;
        
        if (IsServer)
        {
            // Subscribe to game events on the server
            GameManager.NewGameStartedEvent += OnNewGameStarted;
            GameManager.MoveExecutedEvent += OnMoveExecuted;
            
            // Get references and initialize
            EnsureManagerReferences();
            
            // Initialize with current game state
            SyncCurrentGameState();
        }
        else
        {
            // For clients, subscribe to VisualPieceMoved to catch moves
            VisualPiece.VisualPieceMoved += OnVisualPieceMoved;
        }
    }

    public override void OnNetworkDespawn()
    {
        // Unsubscribe from events
        CurrentFEN.OnValueChanged -= OnFENChanged;
        
        if (IsServer)
        {
            GameManager.NewGameStartedEvent -= OnNewGameStarted;
            GameManager.MoveExecutedEvent -= OnMoveExecuted;
        }
        else
        {
            VisualPiece.VisualPieceMoved -= OnVisualPieceMoved;
        }
        
        base.OnNetworkDespawn();
    }

    private void OnNewGameStarted()
    {
        if (IsServer)
        {
            // Sync the initial state when a new game starts
            SyncCurrentGameState();
        }
    }

    private void OnMoveExecuted()
    {
        if (IsServer)
        {
            // Sync the state after a move is executed
            SyncCurrentGameState();
        }
    }

    private void SyncCurrentGameState()
    {
        // Ensure we have references
        if (!EnsureManagerReferences()) return;
        
        // Get the current game state as FEN
        string fenString = gameManager.SerializeGame();
        
        // Update the network variable
        CurrentFEN.Value = new NetworkFEN { Value = fenString };
        
        // Log for debugging
        Debug.Log($"Server synced FEN: {fenString}");
    }

    private void OnFENChanged(NetworkFEN previousValue, NetworkFEN newValue)
    {
        // Only clients need to react to FEN changes
        if (IsServer) return;
        
        // Apply the new FEN to the local game
        Debug.Log($"Client received FEN: {newValue.Value}");
        
        // Ensure we have references before proceeding
        if (!EnsureManagerReferences())
        {
            Debug.LogError("Cannot update from FEN: Manager references are missing");
            return;
        }
        
        // We'll use a coroutine to load the game and then manually update the visual pieces
        StartCoroutine(LoadGameAndUpdateVisuals(newValue.Value));
    }

    private IEnumerator LoadGameAndUpdateVisuals(string fenString)
    {
        // First, ensure manager references
        if (!EnsureManagerReferences())
        {
            Debug.LogError("Cannot load game: Manager references are missing");
            yield break;
        }
        
        // Load the game state from FEN
        try
        {
            gameManager.LoadGame(fenString);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error loading game from FEN: {e.Message}");
            yield break;
        }
        
        // Wait a frame to ensure the game state is updated
        yield return null;
        
        // Now manually update the visual pieces
        try
        {
            UpdateVisualPieces();
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error updating visual pieces after loading game: {e.Message}");
        }
    }
    
    private void UpdateVisualPieces()
    {
        // Ensure we have references before proceeding
        if (!EnsureManagerReferences())
        {
            Debug.LogError("Cannot update visual pieces: Manager references are missing");
            return;
        }
        
        try
        {
            // Clear the board first
            ClearVisualPieces();
            
            // Recreate all pieces based on the current game state
            if (gameManager != null && boardManager != null && gameManager.CurrentPieces != null)
            {
                foreach ((Square square, Piece piece) in gameManager.CurrentPieces)
                {
                    if (square != null && piece != null)
                    {
                        boardManager.CreateAndPlacePieceGO(piece, square);
                    }
                }
            }
            
            // Make sure only the correct side's pieces are enabled
            if (NetworkGameManager.Instance != null)
            {
                boardManager.EnsureOnlyPiecesOfSideAreEnabled(NetworkGameManager.Instance.CurrentTurn.Value);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error updating visual pieces: {e.Message}");
        }
    }
    
    private void ClearVisualPieces()
    {
        try
        {
            // Find all VisualPiece components in the scene
            VisualPiece[] visualPieces = FindObjectsOfType<VisualPiece>();
            
            // Destroy each of them (careful not to destroy while iterating)
            foreach (VisualPiece piece in visualPieces)
            {
                if (piece != null && piece.gameObject != null)
                {
                    Destroy(piece.gameObject);
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error clearing visual pieces: {e.Message}");
        }
    }

    private void OnVisualPieceMoved(Square movedPieceInitialSquare, Transform movedPieceTransform, Transform closestBoardSquareTransform, Piece promotionPiece = null)
    {
        // Null check to prevent errors
        if (movedPieceTransform == null || closestBoardSquareTransform == null)
        {
            Debug.LogWarning("Null transform encountered in OnVisualPieceMoved");
            return;
        }
        
        // Only clients who aren't the server need to send move requests
        if (IsServer) return;
        
        // Ensure we have references
        if (!EnsureManagerReferences()) return;
        
        Side clientSide = Side.Black; // Clients always play as black in this simple implementation
        
        // Make sure NetworkGameManager.Instance is not null
        if (NetworkGameManager.Instance == null)
        {
            Debug.LogError("NetworkGameManager.Instance is null");
            return;
        }
        
        Side currentTurn = NetworkGameManager.Instance.CurrentTurn.Value;
        
        // Check if it's this client's turn
        if (currentTurn != clientSide)
        {
            // Reset the piece if it's not our turn
            if (movedPieceTransform != null && movedPieceTransform.parent != null)
            {
                movedPieceTransform.position = movedPieceTransform.parent.position;
            }
            return;
        }
        
        // If it's our turn, send the move request to the server
        Square endSquare = new Square(closestBoardSquareTransform.name);
        
        RequestMoveServerRpc(
            movedPieceInitialSquare.ToString(),
            endSquare.ToString(),
            promotionPiece != null ? promotionPiece.GetType().Name : "None"
        );
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestMoveServerRpc(string startSquare, string endSquare, string promotionPieceName)
    {
        Debug.Log($"Server received move request: {startSquare} to {endSquare}, promotion: {promotionPieceName}");
        
        // Ensure we have references
        if (!EnsureManagerReferences()) return;
        
        // The server validates and executes the move
        Square start = SquareUtil.StringToSquare(startSquare);
        Square end = SquareUtil.StringToSquare(endSquare);
        
        // Use the GameManager to execute the move directly for better error handling
        try
        {
            // Get the piece at the start square
            Piece piece = gameManager.CurrentBoard[start];
            if (piece == null) return;
            
            // Get the GameObject for the piece
            GameObject pieceGO = boardManager.GetPieceGOAtPosition(start);
            if (pieceGO == null) return;
            
            // Get the destination square
            GameObject endSquareGO = boardManager.GetSquareGOByPosition(end);
            if (endSquareGO == null) return;
            
            // Use reflection to call the OnPieceMoved method safely
            System.Reflection.MethodInfo onPieceMovedMethod = gameManager.GetType().GetMethod(
                "OnPieceMoved", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance
            );
            
            if (onPieceMovedMethod != null)
            {
                // Create promotion piece if needed
                Piece promoPiece = null;
                if (promotionPieceName != "None")
                {
                    Side side = piece.Owner;
                    switch (promotionPieceName)
                    {
                        case "Queen": promoPiece = new Queen(side); break;
                        case "Rook": promoPiece = new Rook(side); break;
                        case "Bishop": promoPiece = new Bishop(side); break;
                        case "Knight": promoPiece = new Knight(side); break;
                    }
                }
                
                // Invoke the method
                onPieceMovedMethod.Invoke(gameManager, new object[] { 
                    start, 
                    pieceGO.transform, 
                    endSquareGO.transform, 
                    promoPiece 
                });
                
                Debug.Log("Server executed the move successfully");
                // The move was successful - MoveExecutedEvent will trigger SyncCurrentGameState()
            }
            else
            {
                Debug.LogError("OnPieceMoved method not found in GameManager");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error executing move: {e.Message}");
        }
    }
    
    /// <summary>
    /// Forces the client to request the latest state from the server
    /// </summary>
    public void ForceRefreshFromServer()
    {
        if (!IsServer)
        {
            Debug.Log("Client is requesting a forced refresh from server");
            RequestCurrentGameStateServerRpc();
        }
    }
    
    [ServerRpc(RequireOwnership = false)]
    private void RequestCurrentGameStateServerRpc()
    {
        Debug.Log("Server received request for current game state");
        if (IsServer)
        {
            // Sync the current state immediately
            SyncCurrentGameState();
        }
    }
    
    // Method for measuring ping - called by NetworkGameManager
    public float GetPing()
    {
        if (NetworkManager.Singleton.IsConnectedClient)
        {
            return NetworkManager.Singleton.NetworkConfig.NetworkTransport.GetCurrentRtt(NetworkManager.ServerClientId);
        }
        return 0;
    }
    
    // Helper method to ensure we have references to required managers
    private bool EnsureManagerReferences()
    {
        // Check and initialize GameManager reference
        if (gameManager == null)
        {
            gameManager = GameManager.Instance;
            if (gameManager == null)
            {
                Debug.LogError("GameManager instance is null!");
                return false;
            }
        }
        
        // Check and initialize BoardManager reference
        if (boardManager == null)
        {
            boardManager = BoardManager.Instance;
            if (boardManager == null)
            {
                Debug.LogError("BoardManager instance is null!");
                return false;
            }
        }
        
        return true;
    }
}