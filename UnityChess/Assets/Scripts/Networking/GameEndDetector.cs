using UnityEngine;
using Unity.Netcode;
using UnityChess;
using System.Collections;
using TMPro;
using System;

/// <summary>
/// Handles detection and UI display of game end conditions (checkmate/stalemate)
/// Works with the basic NetworkGameManager without modifying its code
/// </summary>
public class GameEndDetector : NetworkBehaviour
{
    // UI references
    [Header("UI References")]
    [SerializeField] private TextMeshProUGUI turnIndicatorText;
    [SerializeField] private Color whiteTurnColor = new Color(0, 0.8f, 0, 1); // Green
    [SerializeField] private Color blackTurnColor = new Color(0.8f, 0, 0, 1); // Red
    [SerializeField] private Color drawColor = Color.yellow;
    
    // Network variable to track game end state
    public struct GameEndState : INetworkSerializable, IEquatable<GameEndState>
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
        
        // Implement IEquatable interface
        public bool Equals(GameEndState other)
        {
            return IsGameOver == other.IsGameOver && 
                   WinnerSide == other.WinnerSide && 
                   EndReason == other.EndReason;
        }
        
        // Override Object.Equals
        public override bool Equals(object obj)
        {
            return obj is GameEndState state && Equals(state);
        }
        
        // Override GetHashCode
        public override int GetHashCode()
        {
            return HashCode.Combine(IsGameOver, WinnerSide, EndReason);
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
    
    // Network variable to track game end state across the network
    private NetworkVariable<GameEndState> gameEndState = new NetworkVariable<GameEndState>(
        new GameEndState { IsGameOver = false, WinnerSide = Side.None, EndReason = EndGameReason.None },
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);
    
    // Reference to NetworkGameManager
    private NetworkGameManager networkGameManager;
    
    // Reference to GameManager
    private GameManager gameManager;
    
    // Flag to avoid multiple triggers
    private bool gameEndHandled = false;
    
    // Track if the message has been displayed
    private bool messageDisplayed = false;

    private void Awake()
    {
        // Find the NetworkGameManager in the scene
        networkGameManager = GetComponent<NetworkGameManager>();
        if (networkGameManager == null)
        {
            networkGameManager = FindObjectOfType<NetworkGameManager>();
            if (networkGameManager == null)
            {
                Debug.LogError("GameEndDetector: Could not find NetworkGameManager component");
                enabled = false;
                return;
            }
        }
        
        // Get reference to GameManager
        gameManager = GameManager.Instance;
        
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
        
        // Subscribe to events
        GameManager.GameEndedEvent += OnGameEndedEvent;
        GameManager.MoveExecutedEvent += OnMoveExecutedEvent;
        
        // Subscribe to network variable change
        gameEndState.OnValueChanged += OnGameEndStateChanged;
        
        Debug.Log("GameEndDetector: Initialized and subscribed to events");
        
        // Reset tracking flags when network object spawns
        gameEndHandled = false;
        messageDisplayed = false;
    }
    
    public override void OnNetworkDespawn()
    {
        // Unsubscribe from events
        GameManager.GameEndedEvent -= OnGameEndedEvent;
        GameManager.MoveExecutedEvent -= OnMoveExecutedEvent;
        gameEndState.OnValueChanged -= OnGameEndStateChanged;
        
        base.OnNetworkDespawn();
    }
    
    // Update is called once per frame
    private void Update()
    {
        // Check if the game has ended according to network variable but we haven't handled it locally
        if (gameEndState.Value.IsGameOver && !messageDisplayed)
        {
            // Display the end game message
            DisplayGameEndMessage(gameEndState.Value);
            
            // Disable all pieces to prevent further moves
            if (BoardManager.Instance != null)
            {
                BoardManager.Instance.SetActiveAllPieces(false);
            }
            
            // Mark as displayed
            messageDisplayed = true;
            gameEndHandled = true;
            
            Debug.Log("Game end message displayed in Update");
        }
    }
    
    private void OnGameEndedEvent()
    {
        // Only server/host should handle this
        if (NetworkManager.Singleton == null || 
            (!NetworkManager.Singleton.IsServer && !NetworkManager.Singleton.IsHost))
        {
            return;
        }
        
        Debug.Log("GameEndDetector: GameEndedEvent received");
        
        // If we've already handled the game end, don't do it again
        if (gameEndHandled) return;
        
        // Get the latest half-move to determine the game end condition
        if (gameManager != null && gameManager.HalfMoveTimeline.TryGetCurrent(out HalfMove latestHalfMove))
        {
            if (latestHalfMove.CausedCheckmate)
            {
                Debug.Log($"GameEndDetector: Checkmate detected! Winner: {latestHalfMove.Piece.Owner}");
                SetGameEndState(latestHalfMove.Piece.Owner, EndGameReason.Checkmate);
            }
            else if (latestHalfMove.CausedStalemate)
            {
                Debug.Log("GameEndDetector: Stalemate detected!");
                SetGameEndState(Side.None, EndGameReason.Stalemate);
            }
        }
    }
    
    private void OnMoveExecutedEvent()
    {
        // Only run on server/host
        if (NetworkManager.Singleton == null || 
            (!NetworkManager.Singleton.IsServer && !NetworkManager.Singleton.IsHost))
        {
            return;
        }
        
        // If game has already ended, don't check again
        if (gameEndHandled) return;
        
        // Check for checkmate/stalemate with the current half-move first
        if (gameManager.HalfMoveTimeline.TryGetCurrent(out HalfMove latestHalfMove))
        {
            if (latestHalfMove.CausedCheckmate)
            {
                Debug.Log($"GameEndDetector: Move execution check found checkmate! Winner: {latestHalfMove.Piece.Owner}");
                SetGameEndState(latestHalfMove.Piece.Owner, EndGameReason.Checkmate);
                return;
            }
            else if (latestHalfMove.CausedStalemate)
            {
                Debug.Log("GameEndDetector: Move execution check found stalemate!");
                SetGameEndState(Side.None, EndGameReason.Stalemate);
                return;
            }
        }
        
        // If the initial check didn't catch it, start a delayed check
        StartCoroutine(CheckGameEndConditionsWithDelay(0.2f));
    }
    
    private IEnumerator CheckGameEndConditionsWithDelay(float delay)
    {
        // Wait to ensure game state is fully updated
        yield return new WaitForSeconds(delay);
        
        // If game has already ended, don't check again
        if (gameEndHandled) yield break;
        
        // If GameManager is missing, can't continue
        if (gameManager == null) yield break;
        
        // Get latest half-move and check if it caused checkmate/stalemate
        if (gameManager.HalfMoveTimeline.TryGetCurrent(out HalfMove latestHalfMove))
        {
            if (latestHalfMove.CausedCheckmate)
            {
                Debug.Log($"GameEndDetector: Delayed check found checkmate! Winner: {latestHalfMove.Piece.Owner}");
                SetGameEndState(latestHalfMove.Piece.Owner, EndGameReason.Checkmate);
                yield break;
            }
            else if (latestHalfMove.CausedStalemate)
            {
                Debug.Log("GameEndDetector: Delayed check found stalemate!");
                SetGameEndState(Side.None, EndGameReason.Stalemate);
                yield break;
            }
        }
        
        // If the half-move flags didn't catch it, check manually
        Board currentBoard = gameManager.CurrentBoard;
        Side sideToMove = gameManager.SideToMove;
        
        // Check if the side to move has any legal moves
        bool hasLegalMoves = false;
        for (int file = 1; file <= 8; file++)
        {
            for (int rank = 1; rank <= 8; rank++)
            {
                Square square = new Square(file, rank);
                Piece piece = currentBoard[square];
                
                if (piece != null && piece.Owner == sideToMove && gameManager.HasLegalMoves(piece))
                {
                    hasLegalMoves = true;
                    break;
                }
            }
            if (hasLegalMoves) break;
        }
        
        // If no legal moves, determine if it's checkmate or stalemate
        if (!hasLegalMoves)
        {
            bool isInCheck = Rules.IsPlayerInCheck(currentBoard, sideToMove);
            
            if (isInCheck)
            {
                Debug.Log($"GameEndDetector: Manual check found checkmate! Winner: {sideToMove.Complement()}");
                SetGameEndState(sideToMove.Complement(), EndGameReason.Checkmate);
            }
            else
            {
                Debug.Log("GameEndDetector: Manual check found stalemate!");
                SetGameEndState(Side.None, EndGameReason.Stalemate);
            }
        }
    }
    
    // Handle game end state changes
    private void OnGameEndStateChanged(GameEndState previousValue, GameEndState newValue)
    {
        if (newValue.IsGameOver)
        {
            Debug.Log($"Game end state changed: Winner={newValue.WinnerSide}, Reason={newValue.EndReason}");
            
            // Update the UI to show game end message
            DisplayGameEndMessage(newValue);
            
            // Mark as handled
            messageDisplayed = true;
            
            // Disable piece movement
            if (BoardManager.Instance != null)
            {
                BoardManager.Instance.SetActiveAllPieces(false);
            }
        }
    }
    
    private void SetGameEndState(Side winnerSide, EndGameReason endReason)
    {
        // Only server/host can set game end state
        if (!IsServer && !IsHost) return;
        
        // Mark as handled to prevent multiple triggers
        gameEndHandled = true;
        
        Debug.Log($"GameEndDetector: Setting game over. Winner: {winnerSide}, Reason: {endReason}");
        
        // Update the network variable
        gameEndState.Value = new GameEndState
        {
            IsGameOver = true,
            WinnerSide = winnerSide,
            EndReason = endReason
        };
        
        // Disable all pieces
        if (BoardManager.Instance != null)
        {
            BoardManager.Instance.SetActiveAllPieces(false);
        }
        
        // Send RPC to ensure all clients display the message
        DisplayGameEndMessageClientRpc(winnerSide.ToString(), (int)endReason);
    }
    
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
        
        // Create a game end state from the parameters
        GameEndState state = new GameEndState
        {
            IsGameOver = true,
            WinnerSide = winner,
            EndReason = endReason
        };
        
        // Display the message
        DisplayGameEndMessage(state);
        
        // Mark as handled and displayed
        gameEndHandled = true;
        messageDisplayed = true;
        
        // Force disable all pieces
        if (BoardManager.Instance != null)
        {
            BoardManager.Instance.SetActiveAllPieces(false);
        }
    }
    
    // Display game end message
    private void DisplayGameEndMessage(GameEndState state)
    {
        if (turnIndicatorText == null)
        {
            Debug.LogError("TurnIndicator is null! Cannot display game end message.");
            
            // Try to find the turn indicator as fallback
            turnIndicatorText = GameObject.Find("TurnIndicator")?.GetComponent<TextMeshProUGUI>();
            if (turnIndicatorText == null)
            {
                Debug.LogError("Still couldn't find TurnIndicator!");
                return;
            }
        }
        
        string message = "";
        Color messageColor;
        
        switch (state.EndReason)
        {
            case EndGameReason.Checkmate:
                message = $"CHECKMATE! {state.WinnerSide} wins!";
                messageColor = state.WinnerSide == Side.White ? whiteTurnColor : blackTurnColor;
                break;
                
            case EndGameReason.Stalemate:
                message = "STALEMATE! Game ends in a draw.";
                messageColor = drawColor;
                break;
                
            case EndGameReason.Resignation:
                message = $"{state.WinnerSide} wins by resignation!";
                messageColor = state.WinnerSide == Side.White ? whiteTurnColor : blackTurnColor;
                break;
                
            case EndGameReason.Timeout:
                message = $"{state.WinnerSide} wins on time!";
                messageColor = state.WinnerSide == Side.White ? whiteTurnColor : blackTurnColor;
                break;
                
            case EndGameReason.Draw:
                message = "DRAW agreed by both players.";
                messageColor = drawColor;
                break;
                
            default:
                message = "Game Over!";
                messageColor = drawColor;
                break;
        }
        
        Debug.Log($"Displaying end game message: {message}");
        
        // Set the text and make it bold
        turnIndicatorText.text = message;
        turnIndicatorText.color = messageColor;
        turnIndicatorText.fontStyle = FontStyles.Bold;
        
        // Make sure the text is visible and prominent
        turnIndicatorText.gameObject.SetActive(true);
        turnIndicatorText.fontSize = turnIndicatorText.fontSize * 1.5f; // Make it bigger
        
        // Update UI layer to ensure visibility
        Canvas canvas = turnIndicatorText.GetComponentInParent<Canvas>();
        if (canvas != null)
        {
            canvas.sortingOrder = 10; // Ensure it's on top
        }
    }
    
    // Public method for other components to offer a draw
    public void OfferDraw()
    {
        if (IsServer || IsHost)
        {
            // For simplicity, just accept the draw right away
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
        SetGameEndState(Side.None, EndGameReason.Draw);
    }
    
    // Public method for other components to resign
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
}