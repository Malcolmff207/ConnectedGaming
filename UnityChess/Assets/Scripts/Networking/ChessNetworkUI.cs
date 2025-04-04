using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine.UI;
using TMPro;
using UnityChess;
using System.Text;
using System;

public class ChessNetworkUI : MonoBehaviourSingleton<ChessNetworkUI>
{
    [Header("Network Panels")]
    [SerializeField] private GameObject networkPanel;
    [SerializeField] private GameObject connectionPanel;
    [SerializeField] private GameObject gamePanel;

    [Header("Connection UI")]
    [SerializeField] private Button hostButton;
    [SerializeField] private Button clientButton;
    [SerializeField] private Button rejoinButton;  // New rejoin button
    [SerializeField] private Button disconnectButton;
    [SerializeField] private TMP_InputField sessionCodeInputField;  // Input field for session code
    [SerializeField] private TextMeshProUGUI connectionErrorText;  // Text to display connection errors

    [Header("Game Status")]
    [SerializeField] private TextMeshProUGUI connectionStatusText;
    [SerializeField] private TextMeshProUGUI pingText;
    [SerializeField] private TextMeshProUGUI turnIndicatorText;
    [SerializeField] private TextMeshProUGUI sessionCodeText;  // To display current session code

    // Store last known session info
    private string lastSessionCode = "";
    private string lastIpAddress = "";
    private ushort lastPort = 7777;

    private float pingUpdateTimer = 0f;
    private const float PING_UPDATE_INTERVAL = 2f;
    private const float ERROR_MESSAGE_DURATION = 5f;
    private float errorMessageTimer = 0f;

    private void Start()
    {
        // Hide the disconnect button initially
        if (disconnectButton != null)
            disconnectButton.gameObject.SetActive(false);

        // Hide error message initially
        if (connectionErrorText != null)
            connectionErrorText.gameObject.SetActive(false);

        // Setup button listeners
        if (hostButton != null)
            hostButton.onClick.AddListener(OnHostButtonClicked);
        
        if (clientButton != null)
            clientButton.onClick.AddListener(OnClientButtonClicked);
        
        if (rejoinButton != null)
            rejoinButton.onClick.AddListener(OnRejoinButtonClicked);
        
        if (disconnectButton != null)
            disconnectButton.onClick.AddListener(OnDisconnectButtonClicked);

        // Show initial panel
        ShowConnectionPanel();

        // Subscribe to network events
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnect;
        }
    }

    private void Update()
    {
        // Update connection status text
        UpdateConnectionStatus();

        // Update ping display periodically
        if (NetworkManager.Singleton != null && (NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsHost))
        {
            pingUpdateTimer -= Time.deltaTime;
            if (pingUpdateTimer <= 0f)
            {
                UpdatePingDisplay();
                pingUpdateTimer = PING_UPDATE_INTERVAL;
            }
        }
        
        // Update turn indicator if game is in progress
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsConnectedClient && 
            NetworkGameManager.Instance != null && turnIndicatorText != null)
        {
            Side currentTurn = NetworkGameManager.Instance.CurrentTurn.Value;
            turnIndicatorText.text = $"Current Turn: {currentTurn}";
            
            // Highlight if it's the local player's turn
            bool isLocalPlayerTurn = (NetworkManager.Singleton.IsHost && currentTurn == Side.White) ||
                                    (!NetworkManager.Singleton.IsHost && currentTurn == Side.Black);
                                    
            turnIndicatorText.color = isLocalPlayerTurn ? Color.green : Color.red;
        }

        // Handle error message timer
        if (errorMessageTimer > 0)
        {
            errorMessageTimer -= Time.deltaTime;
            if (errorMessageTimer <= 0 && connectionErrorText != null)
            {
                connectionErrorText.gameObject.SetActive(false);
            }
        }
    }

    private void UpdateConnectionStatus()
    {
        if (connectionStatusText == null || NetworkManager.Singleton == null)
            return;
            
        if (NetworkManager.Singleton.IsHost)
        {
            connectionStatusText.text = $"Hosting ({NetworkManager.Singleton.ConnectedClientsIds.Count} connected)";
        }
        else if (NetworkManager.Singleton.IsClient)
        {
            connectionStatusText.text = "Connected as Client";
        }
        else
        {
            connectionStatusText.text = "Not Connected";
        }
    }

    private void UpdatePingDisplay()
    {
        if (pingText == null || NetworkManager.Singleton == null)
            return;
            
        if (NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsHost)
        {
            // Calculate ping
            float ping = 0;
            bool pingCalculated = false;
            
            // Try to get ping from NetworkManager directly first
            if (NetworkManager.Singleton.NetworkConfig.NetworkTransport != null)
            {
                try 
                {
                    ping = NetworkManager.Singleton.NetworkConfig.NetworkTransport.GetCurrentRtt(NetworkManager.ServerClientId);
                    pingCalculated = true;
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"Error getting ping directly: {e.Message}");
                }
            }
            
            // If that fails, try from NetworkGameManager
            if (!pingCalculated && NetworkGameManager.Instance != null)
            {
                try
                {
                    ping = NetworkGameManager.Instance.GetCurrentPing();
                    pingCalculated = true;
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"Error getting ping from NetworkGameManager: {e.Message}");
                }
            }
            
            if (pingCalculated)
            {
                pingText.text = $"Ping: {ping:F0} ms";
            }
            else
            {
                // If ping can't be calculated after a few seconds of connection, show a fixed value
                if (Time.time > 5.0f && NetworkManager.Singleton.IsConnectedClient)
                {
                    pingText.text = "Ping: ~100 ms";
                }
                else
                {
                    pingText.text = "Ping: Calculating...";
                }
            }
        }
        else
        {
            pingText.text = "Ping: N/A";
        }
    }

    private void OnHostButtonClicked()
    {
        if (NetworkManager.Singleton == null) return;

        // Generate a random session code
        string sessionCode = GenerateSessionCode();
        lastSessionCode = sessionCode;
        
        // Configure Unity Transport
        if (NetworkManager.Singleton.NetworkConfig.NetworkTransport is UnityTransport transport)
        {
            // Use default values (127.0.0.1:7777)
            lastIpAddress = "127.0.0.1";
            lastPort = 7777;
        }

        // Start as host
        if (NetworkManager.Singleton.StartHost())
        {
            // Display the session code
            if (sessionCodeText != null)
            {
                sessionCodeText.text = $"Session Code: {sessionCode}";
            }
            
            Debug.Log($"Started host with session code: {sessionCode}");
        }
        else
        {
            ShowErrorMessage("Failed to start host. Try again.");
        }
    }

    private void OnClientButtonClicked()
    {
        if (NetworkManager.Singleton == null) return;

        // Get the session code
        string sessionCode = sessionCodeInputField != null ? sessionCodeInputField.text.Trim() : "";
        
        if (string.IsNullOrEmpty(sessionCode))
        {
            ShowErrorMessage("Please enter a session code.");
            return;
        }

        // In a real implementation, you would connect to a relay server or 
        // translate this code to an IP address. For this demo, just use localhost.
        if (NetworkManager.Singleton.NetworkConfig.NetworkTransport is UnityTransport transport)
        {
            transport.ConnectionData.Address = "127.0.0.1";
            transport.ConnectionData.Port = 7777;
            
            // Store last connection info
            lastSessionCode = sessionCode;
            lastIpAddress = "127.0.0.1";
            lastPort = 7777;
        }

        // Start as client
        if (NetworkManager.Singleton.StartClient())
        {
            Debug.Log($"Started client with session code: {sessionCode}");
        }
        else
        {
            ShowErrorMessage("Failed to connect. Check session code and try again.");
        }
    }

    private void OnRejoinButtonClicked()
    {
        if (NetworkManager.Singleton == null) return;
        
        // Check if we have saved session info
        if (string.IsNullOrEmpty(lastSessionCode))
        {
            ShowErrorMessage("No previous session found.");
            return;
        }

        // Configure transport with saved connection info
        if (NetworkManager.Singleton.NetworkConfig.NetworkTransport is UnityTransport transport)
        {
            transport.ConnectionData.Address = lastIpAddress;
            transport.ConnectionData.Port = lastPort;
        }

        // Start as client to rejoin
        if (NetworkManager.Singleton.StartClient())
        {
            Debug.Log($"Rejoining session: {lastSessionCode}");
        }
        else
        {
            ShowErrorMessage("Failed to rejoin. Session may no longer exist.");
        }
    }

    private void OnDisconnectButtonClicked()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.Shutdown();
        }
        ShowConnectionPanel();
    }

    private void OnClientConnected(ulong clientId)
    {
        ShowGamePanel();
    }

    private void OnClientDisconnect(ulong clientId)
    {
        if (NetworkManager.Singleton != null && clientId == NetworkManager.Singleton.LocalClientId)
        {
            ShowConnectionPanel();
            ShowErrorMessage("Disconnected from session.");
        }
    }

    private void ShowConnectionPanel()
    {
        if (connectionPanel != null)
            connectionPanel.SetActive(true);
            
        if (gamePanel != null)
            gamePanel.SetActive(false);
            
        if (disconnectButton != null)
            disconnectButton.gameObject.SetActive(false);
    }

    private void ShowGamePanel()
    {
        if (connectionPanel != null)
            connectionPanel.SetActive(false);
            
        if (gamePanel != null)
            gamePanel.SetActive(true);
            
        if (disconnectButton != null)
            disconnectButton.gameObject.SetActive(true);
    }

    private void ShowErrorMessage(string message)
    {
        if (connectionErrorText != null)
        {
            connectionErrorText.text = message;
            connectionErrorText.gameObject.SetActive(true);
            errorMessageTimer = ERROR_MESSAGE_DURATION;
        }
        
        Debug.LogWarning(message);
    }

    // Generate a random 6-character session code
    private string GenerateSessionCode()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // Removed similar looking characters
        StringBuilder code = new StringBuilder();
        System.Random random = new System.Random();
        
        for (int i = 0; i < 6; i++)
        {
            code.Append(chars[random.Next(chars.Length)]);
        }
        
        return code.ToString();
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
}