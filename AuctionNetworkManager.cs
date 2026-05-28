using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using TMPro;
using NativeWebSocket;

public class AuctionNetworkManager : MonoBehaviour
{
    [Header("UI Canvas Components")]
    public TextMeshProUGUI timerText;
    public TextMeshProUGUI bidStatusText;
    public Button bidButton;

    private WebSocket websocket;
    private string serverHttpUrl = "http://localhost:3000/api/login";
    private string serverWsUrl = "ws://localhost:3000/socket.io/?EIO=4&transport=websocket";
    private string currentUsername;
    private int currentHighestBid = 100;

    [System.Serializable] public class LoginData { public string username; }

    // Helper classes to cleanly parse incoming nested JSON frames from the server
    [System.Serializable]
    public class AuctionTickData
    
    {
        public int timer;
        public int highest_bid;
        public string highest_bidder;
    }

    [System.Serializable]
    public class AuctionConcludedData
    {
        public string winner;
        public int final_price;
        public int remaining_balance;
    }

    void Start()
    {
        currentUsername = "ChallengerX"; // Temporary swap for the Editor client

        // Setup Button UI Listener
        if (bidButton != null)
        {
            bidButton.onClick.AddListener(OnBidButtonClicked);
        }

        StartCoroutine(LoginAndConnect(currentUsername));
    }

    IEnumerator LoginAndConnect(string username)
    {
        LoginData data = new LoginData { username = username };
        string jsonData = JsonUtility.ToJson(data);

        using (UnityWebRequest request = new UnityWebRequest(serverHttpUrl, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonData);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                Debug.Log($"<color=green>✓ HTTP Cache Primed:</color> {request.downloadHandler.text}");
                InitializeWebSocket();
            }
            else
            {
                Debug.LogError($"✗ HTTP Configuration Error: {request.error}");
            }
        }
    }

    async void InitializeWebSocket()
    {
        websocket = new WebSocket(serverWsUrl);

        websocket.OnOpen += () =>
        {
            Debug.Log("<color=cyan>⚡ Physical TCP Connection Established!</color>");
        };

        websocket.OnError += (e) => { Debug.LogError($"✗ WebSocket Network Error: {e}"); };
        websocket.OnClose += (e) => { Debug.Log("<color=red>❌ Connection Closed.</color>"); };

        websocket.OnMessage += (bytes) =>
        {
            string rawMessage = Encoding.UTF8.GetString(bytes);

            if (rawMessage.StartsWith("0"))
            {
                SendWebSocketMessage("40");
            }
            else if (rawMessage.StartsWith("40"))
            {
                Debug.Log("<color=lime>✓ Socket.io Layer Authenticated!</color>");
                string joinFrame = "42[\"join_auction\",{\"username\":\"" + currentUsername + "\"}]";
                SendWebSocketMessage(joinFrame);
            }
            else if (rawMessage.StartsWith("42"))
            {
                ProcessIncomingEvent(rawMessage);
            }
        };

        await websocket.Connect();
    }

    private void ProcessIncomingEvent(string rawFrame)
    {
        // Clean out the Socket.io frame metadata prefix to get raw JSON array strings
        // e.g. 42["auction_tick",{"timer":14,...}] -> {"timer":14,...}
        if (rawFrame.Contains("auction_tick"))
        {
            string cleanJson = ExtractJsonPayload(rawFrame);
            AuctionTickData data = JsonUtility.FromJson<AuctionTickData>(cleanJson);

            currentHighestBid = data.highest_bid;

            // Render directly onto your Unity screen canvas elements
            timerText.text = $"Time Left: <color=red>{data.timer}s</color>";
            bidStatusText.text = $"Current Bid: <color=yellow>{data.highest_bid} Gold</color>\nBy: <b>{data.highest_bidder}</b>";
        }
        else if (rawFrame.Contains("bid_updated"))
        {
            string cleanJson = ExtractJsonPayload(rawFrame);
            AuctionTickData data = JsonUtility.FromJson<AuctionTickData>(cleanJson);

            currentHighestBid = data.highest_bid;

            // Replaced fire emoji with high-contrast colored text tag
            bidStatusText.text = $"<color=orange><b>[NEW HIGH BID]</b></color> <color=yellow>{data.highest_bid} Gold</color>\nBy: <b>{data.highest_bidder}</b>";
        }
        else if (rawFrame.Contains("auction_concluded"))
        {
            string cleanJson = ExtractJsonPayload(rawFrame);
            AuctionConcludedData data = JsonUtility.FromJson<AuctionConcludedData>(cleanJson);

            timerText.text = "<b><color=red>CLOSED</color></b>";

            // Replaced trophy emoji with a bold clean status title tag
            bidStatusText.text = $"<b>WINNER: {data.winner}</b>\nFinal Price: <color=yellow>{data.final_price} Gold</color>";
            if (bidButton != null) bidButton.interactable = false; // Kill button input
        }
    }

    private string ExtractJsonPayload(string rawFrame)
    {
        int startIdx = rawFrame.IndexOf('{');
        int endIdx = rawFrame.LastIndexOf('}');
        if (startIdx != -1 && endIdx != -1)
        {
            return rawFrame.Substring(startIdx, (endIdx - startIdx) + 1);
        }
        return "";
    }

    private void OnBidButtonClicked()
    {
        if (websocket != null && websocket.State == WebSocketState.Open)
        {
            // Take whatever the current high bid is, append 50 gold to top it, and ship it out!
            int myNewBid = currentHighestBid + 50;
            string bidFrame = "42[\"submit_bid\",{\"username\":\"" + currentUsername + "\",\"bidAmount\":" + myNewBid + "}]";

            SendWebSocketMessage(bidFrame);
            Debug.Log($"🚀 Fired live competitive bid for: {myNewBid} Gold");
        }
    }

    private async void SendWebSocketMessage(string message)
    {
        if (websocket != null && websocket.State == WebSocketState.Open)
        {
            await websocket.SendText(message);
        }
    }

    void Update()
    {
#if !UNITY_WEBGL || UNITY_EDITOR
        if (websocket != null) { websocket.DispatchMessageQueue(); }
#endif

        // NEW INPUT SYSTEM COMPATIBLE BACKDOOR: Press R to revive loop
        if (UnityEngine.InputSystem.Keyboard.current != null &&
            UnityEngine.InputSystem.Keyboard.current.rKey.wasPressedThisFrame)
        {
            if (websocket != null && websocket.State == WebSocketState.Open)
            {
                string resetFrame = "42[\"submit_bid\",{\"username\":\"System\",\"bidAmount\":100}]";
                SendWebSocketMessage(resetFrame);
                if (bidButton != null) bidButton.interactable = true;
                Debug.Log("<color=yellow>🛠️ Dev Command Sent: Reviving Game Loop...</color>");
            }
        }
    }
}