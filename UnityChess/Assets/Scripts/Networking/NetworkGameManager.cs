using Unity.Netcode;
using UnityChess;
using UnityEngine;

public class NetworkGameManager : NetworkBehaviour
{
    // Network variables to track game state
    public NetworkVariable<Side> CurrentTurn = new NetworkVariable<Side>(Side.White, 
        NetworkVariableReadPermission.Everyone, 
        NetworkVariableWritePermission.Server);
    
    public NetworkVariable<bool> GameInProgress = new NetworkVariable<bool>(false, 
        NetworkVariableReadPermission.Everyone, 
        NetworkVariableWritePermission.Server);

    // Reference to local GameManager
    private GameManager gameManager;
    
    // Reference to SessionManager
    private NetworkSessionManager sessionManager;
    
    // Static reference for easy access
    public static NetworkGameManager Instance { get; private set; }

    private void Awake()
    {
        // Set up singleton instance
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        
        gameManager = GameManager.Instance;
        
        // Find the session manager
        sessionManager = FindObjectOfType<NetworkSessionManager>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (IsServer)
        {
            // Subscribe to events before initializing the game state
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            
            // If we're starting fresh (not continuing from a disconnection)
            if (GameInProgress.Value == false)
            {
                // Initialize game state on server
                CurrentTurn.Value = Side.White;
                GameInProgress.Value = true;
            }
        }

        // Subscribe to events
        VisualPiece.VisualPieceMoved += OnVisualPieceMoved;
        GameManager.MoveExecutedEvent += OnMoveExecuted;
    }

    public override void OnNetworkDespawn()
    {
        // Unsubscribe from events
        VisualPiece.VisualPieceMoved -= OnVisualPieceMoved;
        GameManager.MoveExecutedEvent -= OnMoveExecuted;
        
        if (IsServer)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
        }
        
        if (Instance == this)
        {
            Instance = null;
        }
        
        base.OnNetworkDespawn();
    }

    private void OnVisualPieceMoved(Square movedPieceInitialSquare, Transform movedPieceTransform, Transform closestBoardSquareTransform, Piece promotionPiece = null)
    {
        // Null check to prevent errors
        if (movedPieceTransform == null || closestBoardSquareTransform == null)
        {
            Debug.LogWarning("Null transform encountered in OnVisualPieceMoved");
            return;
        }
        
        // Get the actual piece from the board
        Piece piece = gameManager.CurrentBoard[movedPieceInitialSquare];
        if (piece == null)
        {
            Debug.LogWarning("No piece found at " + movedPieceInitialSquare);
            return;
        }
        
        // Only process moves if it's this client's turn AND the piece color matches
        Side localPlayerSide = IsHost ? Side.White : Side.Black;
        
        // Double check: verify it's this player's turn AND the piece belongs to this player
        if (CurrentTurn.Value != localPlayerSide || piece.Owner != localPlayerSide)
        {
            Debug.LogWarning($"Rejecting move: Turn={CurrentTurn.Value}, Piece owner={piece.Owner}, Local player={localPlayerSide}");
            
            // If it's not our turn or not our piece, reset the piece position and return
            if (movedPieceTransform != null && movedPieceTransform.parent != null)
            {
                movedPieceTransform.position = movedPieceTransform.parent.position;
            }
            return;
        }

        // If we're here, it's our turn and our piece, so we'll let the game continue
        
        // The rest of the method remains the same...
        if (IsServer || IsHost)
        {
            // We'll handle this in the MoveExecutedEvent
        }
        else
        {
            // If we're the client, send the move to the server
            Square endSquare = new Square(closestBoardSquareTransform.name);
            SendMoveToServerServerRpc(
                movedPieceInitialSquare.ToString(), 
                endSquare.ToString(),
                promotionPiece != null ? promotionPiece.GetType().Name : "None"
            );
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void SendMoveToServerServerRpc(string startSquare, string endSquare, string promotionPieceType)
    {
        // The server receives the move from the client and broadcasts it to all clients
        BroadcastMoveClientRpc(startSquare, endSquare, promotionPieceType);
    }

    [ClientRpc]
    private void BroadcastMoveClientRpc(string startSquare, string endSquare, string promotionPieceType)
    {
        // Don't apply to the client that initiated the move
        if (IsServer || IsHost)
        {
            // Toggle turn on the server
            CurrentTurn.Value = CurrentTurn.Value == Side.White ? Side.Black : Side.White;
            return;
        }
        
        // On clients, visually execute the move
        Square start = SquareUtil.StringToSquare(startSquare);
        Square end = SquareUtil.StringToSquare(endSquare);
        
        // Get the piece GameObject and its transform
        GameObject pieceGO = BoardManager.Instance.GetPieceGOAtPosition(start);
        if (pieceGO == null) return;
        
        Transform pieceTransform = pieceGO.transform;
        Transform endSquareTransform = BoardManager.Instance.GetSquareGOByPosition(end).transform;
        
        // Create the appropriate promotion piece if needed
        Piece promotionPiece = null;
        if (promotionPieceType != "None")
        {
            Side side = gameManager.CurrentBoard[start].Owner;
            switch (promotionPieceType)
            {
                case "Queen": promotionPiece = new Queen(side); break;
                case "Rook": promotionPiece = new Rook(side); break;
                case "Bishop": promotionPiece = new Bishop(side); break;
                case "Knight": promotionPiece = new Knight(side); break;
            }
        }
        
        // Use direct call to handle the move since we can't invoke the event
        // This is a simplified version - in real implementation you might need something more complex
        BoardManager.Instance.TryDestroyVisualPiece(end); // Remove any captured piece
        pieceTransform.parent = endSquareTransform;
        pieceTransform.position = endSquareTransform.position;
        
        // Handle turn update
        BoardManager.Instance.EnsureOnlyPiecesOfSideAreEnabled(CurrentTurn.Value);
    }

    private void OnMoveExecuted()
    {
        if (IsServer || IsHost)
        {
            // Toggle the current turn from White to Black or vice versa
            CurrentTurn.Value = CurrentTurn.Value == Side.White ? Side.Black : Side.White;
            
            // No need to sync the latest move here, as we're already doing it in OnVisualPieceMoved
        }
    }
    
    private void OnClientConnected(ulong clientId)
    {
        Debug.Log($"Client connected: {clientId}");
        
        // If we're the server, handle the client connection
        if (IsServer || IsHost)
        {
            // Always consider it a rejoin if a game is in progress
            // This ensures we don't reset the board on reconnection
            bool gameInProgress = GameInProgress.Value;
            
            if (gameInProgress)
            {
                Debug.Log($"Client {clientId} is connecting to in-progress game - sending current state");
                // Force sync the current game state to the client
                SyncCurrentGameStateToClientClientRpc(clientId);
                
                // Also update the current turn
                Side currentTurn = gameManager.SideToMove;
                CurrentTurn.Value = currentTurn;
                Debug.Log($"Updated current turn to {currentTurn} for client");
            }
            else
            {
                Debug.Log("Starting new game for connected client");
                // Start a new game only if no game is in progress
                GameInProgress.Value = true;
                gameManager.StartNewGame();
                
                // Get the client's side from the session manager
                if (sessionManager != null)
                {
                    Side clientSide = sessionManager.GetClientSide(clientId);
                    Debug.Log($"Assigned side {clientSide} to client {clientId}");
                }
            }
        }
    }
    
    [ClientRpc]
    private void SyncCurrentGameStateClientRpc()
    {
        // Don't apply to the server/host
        if (IsServer || IsHost) return;
        
        Debug.Log("Received request to sync game state from server");
        
        // If this is a client, trigger ChessNetworkSync to update
        ChessNetworkSync networkSync = FindObjectOfType<ChessNetworkSync>();
        if (networkSync != null)
        {
            networkSync.ForceRefreshFromServer();
        }
    }

    [ClientRpc]
    private void SyncCurrentGameStateToClientClientRpc(ulong clientId)
    {
        // Only the targeted client processes this
        if (NetworkManager.Singleton.LocalClientId != clientId) return;
        
        Debug.Log("Received targeted request to sync game state from server");
        
        // Trigger ChessNetworkSync to update
        ChessNetworkSync networkSync = FindObjectOfType<ChessNetworkSync>();
        if (networkSync != null)
        {
            networkSync.ForceRefreshFromServer();
        }
    }

    // Method to measure and log ping
    public float GetCurrentPing()
    {
        if (NetworkManager.Singleton.IsConnectedClient)
        {
            return NetworkManager.Singleton.NetworkConfig.NetworkTransport.GetCurrentRtt(NetworkManager.ServerClientId);
        }
        return 0;
    }
}