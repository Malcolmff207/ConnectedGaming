using Unity.Netcode;
using UnityChess;
using UnityEngine;
using TMPro;

public class NetworkGameManager : NetworkBehaviour
{
    // Network variables to track game state
    public NetworkVariable<Side> CurrentTurn = new NetworkVariable<Side>(Side.White, 
        NetworkVariableReadPermission.Everyone, 
        NetworkVariableWritePermission.Server);
    
    public NetworkVariable<bool> GameInProgress = new NetworkVariable<bool>(false, 
        NetworkVariableReadPermission.Everyone, 
        NetworkVariableWritePermission.Server);

    // Add this NetworkVariable to track game end state
    private NetworkVariable<GameEndState> gameEndState = new NetworkVariable<GameEndState>(
        new GameEndState { IsGameOver = false, WinnerSide = Side.None, EndReason = EndGameReason.None },
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    // Define the struct and enum for game end state
    public struct GameEndState : INetworkSerializable
    {
        public bool IsGameOver;
        public Side WinnerSide;
        public EndGameReason EndReason;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref IsGameOver);
            serializer.SerializeValue(ref WinnerSide);
            serializer.SerializeValue(ref EndReason);
        }
    }

    public enum EndGameReason
    {
        None,
        Checkmate,
        Stalemate,
        Resignation,
        Timeout,
        Draw
    }

    // UI references
    [Header("UI References")]
    [SerializeField] private TextMeshProUGUI turnIndicatorText;
    [SerializeField] private Color whiteTurnColor = new Color(0, 0.8f, 0, 1); // Green
    [SerializeField] private Color blackTurnColor = new Color(0.8f, 0, 0, 1); // Red
    [SerializeField] private Color drawColor = Color.yellow;

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

        // Find turn indicator if not assigned
        if (turnIndicatorText == null)
        {
            turnIndicatorText = GameObject.Find("TurnIndicator")?.GetComponent<TextMeshProUGUI>();
            if (turnIndicatorText == null)
                Debug.LogWarning("TurnIndicator not found! Game end messages won't be displayed properly.");
        }
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
        
        // IMPORTANT: Add explicit subscription to GameEndedEvent for checkmate/stalemate
        GameManager.GameEndedEvent += OnGameEnded;
        
        // Subscribe to turn change event to update UI
        CurrentTurn.OnValueChanged += OnTurnChanged;
        
        // Subscribe to game end state change
        gameEndState.OnValueChanged += OnGameEndStateChanged;
        
        // Initial UI update
        UpdateTurnIndicatorText();
    }

    public override void OnNetworkDespawn()
    {
        // Unsubscribe from events
        VisualPiece.VisualPieceMoved -= OnVisualPieceMoved;
        GameManager.MoveExecutedEvent -= OnMoveExecuted;
        GameManager.GameEndedEvent -= OnGameEnded; // IMPORTANT: Unsubscribe here
        CurrentTurn.OnValueChanged -= OnTurnChanged;
        gameEndState.OnValueChanged -= OnGameEndStateChanged;
        
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

        // If game is over, don't allow any moves
        if (gameEndState.Value.IsGameOver)
        {
            // Reset the piece position and return
            if (movedPieceTransform != null && movedPieceTransform.parent != null)
            {
                movedPieceTransform.position = movedPieceTransform.parent.position;
            }
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
            
            // Check for game end conditions
            CheckGameEndConditions();
        }
        
        // Update the turn indicator text
        UpdateTurnIndicatorText();
    }
    
    // Check for checkmate/stalemate
    private void CheckGameEndConditions()
    {
        if (!IsServer && !IsHost) return;
        
        // Check if game manager is available
        if (GameManager.Instance == null) return;
        
        // Get the latest half-move
        if (GameManager.Instance.HalfMoveTimeline.TryGetCurrent(out HalfMove latestHalfMove))
        {
            if (latestHalfMove.CausedCheckmate)
            {
                // If checkmate occurred, the side that made the move is the winner
                SetGameEndState(latestHalfMove.Piece.Owner, EndGameReason.Checkmate);
                Debug.Log($"Game ended by checkmate: {latestHalfMove.Piece.Owner} wins!");
            }
            else if (latestHalfMove.CausedStalemate)
            {
                // If stalemate occurred, it's a draw
                SetGameEndState(Side.None, EndGameReason.Stalemate);
                Debug.Log("Game ended by stalemate: Draw!");
            }
        }
    }
    
    // Explicit handler for the GameEndedEvent
    private void OnGameEnded()
    {
        // Only server should handle this
        if (!IsServer && !IsHost) return;
        
        Debug.Log("GameEndedEvent received - checking for checkmate/stalemate");
        
        // Get the latest half-move
        if (GameManager.Instance.HalfMoveTimeline.TryGetCurrent(out HalfMove latestHalfMove))
        {
            if (latestHalfMove.CausedCheckmate)
            {
                // Log extensively for debugging
                Debug.Log($"CHECKMATE DETECTED! Winner: {latestHalfMove.Piece.Owner}");
                
                // If checkmate occurred, the side that made the move is the winner
                SetGameEndState(latestHalfMove.Piece.Owner, EndGameReason.Checkmate);
            }
            else if (latestHalfMove.CausedStalemate)
            {
                Debug.Log("STALEMATE DETECTED!");
                
                // If stalemate occurred, it's a draw
                SetGameEndState(Side.None, EndGameReason.Stalemate);
            }
            else
            {
                Debug.Log("Game ended but not by checkmate or stalemate");
            }
        }
        else
        {
            Debug.LogError("Could not get current half-move in OnGameEnded");
        }
    }
    
    // Update the turn indicator text
    private void UpdateTurnIndicatorText()
    {
        if (turnIndicatorText == null)
        {
            // Try to find the turn indicator if not assigned
            turnIndicatorText = GameObject.Find("TurnIndicator")?.GetComponent<TextMeshProUGUI>();
            if (turnIndicatorText == null) return;
        }
        
        // If game is over, show end game message
        if (gameEndState.Value.IsGameOver)
        {
            DisplayGameEndMessage();
            return;
        }
        
        // Otherwise show current turn
        string turnText = $"Current Turn: {CurrentTurn.Value}";
        turnIndicatorText.text = turnText;
        
        // Set color based on turn
        turnIndicatorText.color = CurrentTurn.Value == Side.White ? whiteTurnColor : blackTurnColor;
    }
    
    // Set game end state with enhanced debugging
    private void SetGameEndState(Side winnerSide, EndGameReason endReason)
    {
        if (!IsServer && !IsHost) return;
        
        Debug.Log($"Setting game end state: Winner={winnerSide}, Reason={endReason}");
        
        // Update the network variable
        gameEndState.Value = new GameEndState
        {
            IsGameOver = true,
            WinnerSide = winnerSide,
            EndReason = endReason
        };
        
        // Update the UI
        UpdateTurnIndicatorText();
        
        // Force disable all pieces to prevent further moves
        if (BoardManager.Instance != null)
        {
            BoardManager.Instance.SetActiveAllPieces(false);
        }
        
        // Tell all clients to update their game end displays
        DisplayGameEndMessageClientRpc(
            winnerSide.ToString(),
            (int)endReason
        );
    }
    
    // Add this ClientRpc to force all clients to display the end message
    [ClientRpc]
    private void DisplayGameEndMessageClientRpc(string winnerSide, int endReasonInt)
    {
        Side winner;
        if (winnerSide == "White")
            winner = Side.White;
        else if (winnerSide == "Black")
            winner = Side.Black;
        else
            winner = Side.None;
            
        EndGameReason endReason = (EndGameReason)endReasonInt;
        
        Debug.Log($"Client received game end notification: Winner={winner}, Reason={endReason}");
        
        // Force update the game end state locally
        gameEndState.Value = new GameEndState
        {
            IsGameOver = true,
            WinnerSide = winner,
            EndReason = endReason
        };
        
        // Force update the UI display
        DisplayGameEndMessage();
        
        // Force disable all pieces
        if (BoardManager.Instance != null)
        {
            BoardManager.Instance.SetActiveAllPieces(false);
        }
    }
    
    // Display game end message with enhanced debugging
    private void DisplayGameEndMessage()
    {
        if (turnIndicatorText == null)
        {
            Debug.LogError("TurnIndicator is null! Cannot display game end message.");
            return;
        }
        
        string message = "";
        Color messageColor;
        
        switch (gameEndState.Value.EndReason)
        {
            case EndGameReason.Checkmate:
                message = $"Game Over: {gameEndState.Value.WinnerSide} Wins by Checkmate!";
                messageColor = gameEndState.Value.WinnerSide == Side.White ? whiteTurnColor : blackTurnColor;
                break;
                
            case EndGameReason.Stalemate:
                message = "Game Over: Draw by Stalemate!";
                messageColor = drawColor;
                break;
                
            case EndGameReason.Resignation:
                message = $"Game Over: {gameEndState.Value.WinnerSide} Wins by Resignation!";
                messageColor = gameEndState.Value.WinnerSide == Side.White ? whiteTurnColor : blackTurnColor;
                break;
                
            case EndGameReason.Timeout:
                message = $"Game Over: {gameEndState.Value.WinnerSide} Wins by Timeout!";
                messageColor = gameEndState.Value.WinnerSide == Side.White ? whiteTurnColor : blackTurnColor;
                break;
                
            case EndGameReason.Draw:
                message = "Game Over: Draw!";
                messageColor = drawColor;
                break;
                
            default:
                message = "Game Over!";
                messageColor = drawColor;
                break;
        }
        
        Debug.Log($"Displaying end game message: {message}");
        
        turnIndicatorText.text = message;
        turnIndicatorText.color = messageColor;
    }
    
    // Player can call this to resign the game
    public void ResignGame()
    {
        // Determine which side is resigning based on local player
        Side resigningSide = NetworkManager.Singleton.IsHost ? Side.White : Side.Black;
        
        // Call the server RPC to handle the resignation
        ResignGameServerRpc(resigningSide);
    }

    [ServerRpc(RequireOwnership = false)]
    private void ResignGameServerRpc(Side resigningSide)
    {
        if (!IsServer && !IsHost) return;
        
        // The opposite side wins
        Side winningSide = resigningSide == Side.White ? Side.Black : Side.White;
        
        // Set the game end state
        SetGameEndState(winningSide, EndGameReason.Resignation);
    }
    
    // Handle turn changes
    private void OnTurnChanged(Side previousValue, Side newValue)
    {
        UpdateTurnIndicatorText();
    }

    // Handle game end state changes
    private void OnGameEndStateChanged(GameEndState previousValue, GameEndState newValue)
    {
        if (newValue.IsGameOver)
        {
            // Update the UI to show game end message
            UpdateTurnIndicatorText();
            
            // Disable piece movement
            BoardManager.Instance.SetActiveAllPieces(false);
        }
    }
    
    private void OnClientConnected(ulong clientId)
    {
        Debug.Log($"Client {clientId} connected");
        
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
                
                // If game is over, sync that state too
                if (gameEndState.Value.IsGameOver)
                {
                    // Game end state will be synced automatically via NetworkVariable
                    Debug.Log($"Game is already over, syncing end state to client");
                }
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
    
    // Method to offer a draw to the opponent
    public void OfferDraw()
    {
        if (IsServer || IsHost)
        {
            // For simplicity in this implementation, just accept the draw right away
            // In a real implementation, you'd want to ask the other player
            SetGameEndState(Side.None, EndGameReason.Draw);
        }
        else
        {
            // Send request to server
            RequestDrawServerRpc();
        }
    }
    
    [ServerRpc(RequireOwnership = false)]
    private void RequestDrawServerRpc()
    {
        // For now, just accept draws automatically
        // In a real implementation, you'd want to get confirmation from the other player
        SetGameEndState(Side.None, EndGameReason.Draw);
    }
    
    // Reset the game
    public void ResetGame()
    {
        if (!IsServer && !IsHost) return;
        
        // Reset game state
        gameEndState.Value = new GameEndState 
        { 
            IsGameOver = false, 
            WinnerSide = Side.None, 
            EndReason = EndGameReason.None 
        };
        
        CurrentTurn.Value = Side.White;
        GameInProgress.Value = true;
        
        // Start a new game in the GameManager
        gameManager.StartNewGame();
        
        // Broadcast reset to clients
        ResetGameClientRpc();
    }
    
    [ClientRpc]
    private void ResetGameClientRpc()
    {
        // Update UI
        UpdateTurnIndicatorText();
    }
}