using UnityEngine;
using UnityChess;

/// <summary>
/// This script adds a component to the GameManager GameObject to add null checks 
/// to the OnPieceMoved method using MonoBehaviour's OnEnable
/// </summary>
[RequireComponent(typeof(GameManager))]
public class GameManagerNullCheckFix : MonoBehaviour
{
    private GameManager gameManager;
    private System.Reflection.MethodInfo originalOnPieceMovedMethod;
    
    // Delegate type that matches the signature of OnPieceMoved
    private delegate void OnPieceMovedDelegate(Square square, Transform pieceTransform, Transform squareTransform, Piece promotionPiece);
    private OnPieceMovedDelegate wrappedOnPieceMoved;
    
    private void Awake()
    {
        gameManager = GetComponent<GameManager>();
        
        // Get the original method info
        originalOnPieceMovedMethod = gameManager.GetType().GetMethod(
            "OnPieceMoved", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance
        );
        
        if (originalOnPieceMovedMethod == null)
        {
            Debug.LogError("Could not find OnPieceMoved method in GameManager");
            return;
        }
        
        // Subscribe our safe wrapper to the VisualPieceMoved event
        VisualPiece.VisualPieceMoved += SafeOnPieceMoved;
        
        Debug.Log("GameManagerNullCheckFix installed");
    }
    
    private void OnDestroy()
    {
        // Unsubscribe our wrapper when this component is destroyed
        VisualPiece.VisualPieceMoved -= SafeOnPieceMoved;
    }
    
    private void SafeOnPieceMoved(Square square, Transform pieceTransform, Transform squareTransform, Piece promotionPiece)
    {
        // Check for null transforms before invoking the original method
        if (pieceTransform == null)
        {
            Debug.LogWarning("Null piece transform in SafeOnPieceMoved - ignoring move");
            return;
        }
        
        if (squareTransform == null)
        {
            Debug.LogWarning("Null square transform in SafeOnPieceMoved - ignoring move");
            return;
        }
        
        try
        {
            // Invoke the original method with our parameters
            originalOnPieceMovedMethod.Invoke(gameManager, new object[] { square, pieceTransform, squareTransform, promotionPiece });
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error in SafeOnPieceMoved: {e.Message}");
        }
    }
}