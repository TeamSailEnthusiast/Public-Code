using System;

using UnityEngine;

using Sfs2X;
using Sfs2X.Core;
using Sfs2X.Util;
using Sfs2X.Logging;
using Sfs2X.Requests;
using Sfs2X.Entities.Data;
using LeafGrid;
using Sfs2X.Entities;
using System.Collections.Generic;

public enum ConnectionState : byte
{
    DISCONNECTED,
    CONNECTED
}

public class SFSSimplex : Singleton<SFSSimplex>
{
    public static Action OnReadyEvent;
    public static Action<SFSObject> OnReceiveAckEvent;
    public static Action<SFSObject> OnReceiveStateEvent;

    public static ConnectionState ConnState { get; private set; }


    [Header("Guest Connection Settings")]

    [Tooltip("IP address or domain name of the SmartFoxServer instance; if encryption is enabled, a domain name must be entered")]
    [SerializeField] string _guestHost = "127.0.0.1";

    [Tooltip("TCP listening port of the SmartFoxServer instance, used for TCP socket connection in all builds except WebGL")]
    [SerializeField] int _guestTCPPort = 9933;

    [Tooltip("HTTP listening port of the SmartFoxServer instance, used for WebSocket (WS) connections in WebGL build")]
    [SerializeField] int _guestHttpPort = 8080;

    [Tooltip("HTTPS listening port of the SmartFoxServer instance, used for WebSocket Secure (WSS) connections in WebGL build and connection encryption in all other builds")]
    [SerializeField] int _guestHttpsPort = 8443;

    [Tooltip("Use SmartFoxServer's HTTP tunneling (BlueBox) if TCP socket connection can't be established; not available in WebGL builds")]
    [SerializeField] bool _guestUseHttpTunnel = false;

    [Tooltip("Enable SmartFoxServer protocol encryption; 'host' must be a domain name and an SSL certificate must have been deployed")]
    [SerializeField] bool _guestEncrypt = false;

    [Tooltip("Name of the SmartFoxServer Zone to join")]
    [SerializeField] string _guestZone = "MainZone";

    [Tooltip("Display SmartFoxServer client debug messages")]
    [SerializeField] bool _guestDebug = false;

    [Tooltip("_sfs-side SmartFoxServer logging level")]
    [SerializeField] LogLevel _logLevel = LogLevel.INFO;

    static SmartFox _sfs;

    public void Connect()
    {
        Disconnect();

        Debug.Log("Connecting");
        Application.runInBackground = true;

        if (_sfs != null)
        {
            _sfs.RemoveAllEventListeners();
        }

        _sfs = new SmartFox();

        // Add event listeners
        _sfs.AddEventListener(SFSEvent.CONNECTION, OnConnection);
        _sfs.AddEventListener(SFSEvent.CONNECTION_LOST, OnConnectionLost);

        _sfs.AddEventListener(SFSEvent.LOGIN, OnLogin);
        _sfs.AddEventListener(SFSEvent.LOGIN_ERROR, OnLoginError);

        _sfs.AddEventListener(SFSEvent.UDP_INIT, OnUdpInit);

        // SFS_SignUp.RegisterListeners();

        // Configure internal SFS2X logger
        _sfs.Logger.EnableConsoleTrace = true;
        _sfs.Logger.LoggingLevel = _logLevel;

        // Set connection parameters
        ConfigData cfg = new()
        {
            Host = _guestHost,
            Port = _guestTCPPort,

            HttpPort = _guestHttpPort,
            HttpsPort = _guestHttpsPort,

            UdpHost = _guestHost,
            UdpPort = _guestTCPPort,

            Zone = _guestZone,

            Debug = _guestDebug
        };

        cfg.BlueBox.IsActive = _guestUseHttpTunnel;
        cfg.BlueBox.UseHttps = _guestEncrypt;

        _sfs.Connect(cfg);
    }

    public void Disconnect()
    {
        if (_sfs != null && _sfs.IsConnected)
            _sfs.Disconnect();
    }
    void Update()
    {
        if (_sfs == null) return;

        _sfs.ProcessEvents();
    }

    public void SendRoomUDP(string cmd, ISFSObject sfsObj)
    {
        _sfs.Send(new ExtensionRequest(cmd, sfsObj, _sfs.LastJoinedRoom, true));
    }

    void OnApplicationQuit()
    {
        // if (_sfs != null && _sfs.IsConnected)
        //     _sfs.EnableLagMonitor(false);

        Disconnect();
    }


    //----------------------------------------------------------
    // SmartFoxServer event listeners
    //----------------------------------------------------------
    void OnExtensionResponse(BaseEvent evt)
    {
        switch ((string)evt.Params["cmd"])
        {
            // Server received input - we received ack
            case "a":
                {
                    OnReceiveAckEvent?.Invoke((SFSObject)evt.Params["params"]);
                }
                break;
            // Server processed input - we received character state
            case "s":
                {
                    OnReceiveStateEvent?.Invoke((SFSObject)evt.Params["params"]);
                }
                break;
            // Server processed remote user input - we received remote user input
            case "r":
                {
                    OnRemoteUserState((SFSObject)evt.Params["params"]);
                }
                break;
        }
    }
    public void OnRemoteUserState(SFSObject stateParams)
    {
        Debug.Log("received remote user input");

        int id = stateParams.GetInt("i");
        float x = stateParams.GetFloat("x");
        float z = stateParams.GetFloat("z");

        CharacterManager.Instance.UpdateAOICharacter(id, x, z);
    }
    void OnProximityListUpdate(BaseEvent evt)
    {
        CharacterManager.Instance.ManageAOICharacters((List<User>)evt.Params["addedUsers"],
                                                      (List<User>)evt.Params["removedUsers"]);

        DynamicObjectLoader.Instance.UpdateMMOItems((List<IMMOItem>)evt.Params["addedItems"],
                                                    (List<IMMOItem>)evt.Params["removedItems"]);
    }
    void OnConnection(BaseEvent evt)
    {
        // Check if the conenction was established or not
        if ((bool)evt.Params["success"])
        {
            Debug.Log("Connection established successfully");
            Debug.Log("SFS2X API version: " + _sfs.Version);
            Debug.Log("Connection mode is: " + _sfs.ConnectionMode);

            ConnState = ConnectionState.CONNECTED;
            _sfs.Send(new LoginRequest("guest" + UnityEngine.Random.Range(0, 1000000).ToString()));

            // ConnState = ConnectionState.CONNECTED;
            // if (_guestEncrypt)
            // {
            //     Debug.Log("Initializing encryption...");

            //     // Initialize encryption
            //     _sfs.InitCrypto();
            // }
            // else if (PermState == PermissionState.NONE)
            // {
            //     // Login as Guest first otherwise server wont accept our requests
            //     _sfs.Send(new LoginRequest(""));
            // }
        }
        else
        {
            string reason = (string)evt.Params["reason"];

            Debug.Log("Connection to SmartFoxServer lost; reason is: " + reason);

            // Show error message
            // DDoLCanvas.Instance.EnableErrorPopUp("Disconnected", "Connection failed; is the server running?");
        }
    }

    void OnConnectionLost(BaseEvent evt)
    {
        // Show error message
        string reason = (string)evt.Params["reason"];

        Debug.Log("Connection to SmartFoxServer lost; reason is: " + reason);
        ConnState = ConnectionState.DISCONNECTED;

        // if (reason != ClientDisconnectionReason.MANUAL)
        // {
        // Show error message
        // string connLostMsg = "You have been disconnected, reason: ";
        // ErrorType errorType = ErrorType.UNDEFINED;

        // if (reason == ClientDisconnectionReason.IDLE)
        // {
        //     connLostMsg += "Idle Timeout";
        //     errorType = ErrorType.GUEST_LOGOUT_IDLE;
        // }
        // else if (reason == ClientDisconnectionReason.KICK)
        // {
        //     connLostMsg += "the server has closed down.";
        //     errorType = ErrorType.GUEST_LOGOUT_SERVER_CLOSED;
        // }
        // else
        // {
        //     connLostMsg += "reason is unknown.";
        // }
        // DDoLCanvas.Instance.EnableErrorPopUp("Disconnected", connLostMsg, errorType);
        // }

        // ConnState = ConnectionState.DISCONNECTED;
        // AuthState = AuthenticationState.NONE;
        // PermState = PermissionState.NONE;
    }
    void OnLogin(BaseEvent evt)
    {
        Debug.Log("Login successful");

        _sfs.InitUDP();


        // if (AuthState == AuthenticationState.REQUEST_SIGN_UP)
        // {
        //     UI_Auth.Instance.SendRegisterData();
        //     // SendRegisterDataEvent?.Invoke();
        // }

        // else if (AuthState == AuthenticationState.REQUEST_SERVERS)
        // {
        //     _sfsServers.RequestServers();
        // }

        // else if (AuthState == AuthenticationState.REQUEST_JOIN_SERVER)
        // {
        //     _sfsServers.RequestJoinServer(_selectedServer.Zone);
        // }

        // else if (AuthState == AuthenticationState.REQUEST_ACTIVATE)
        // {
        //     // SendActivationDataEvent?.Invoke();
        //     UI_AccountActivation.Instance.SendActivationData();
        // }

        // AuthState = AuthenticationState.LOGGED_IN;
        // PermState = PermissionState.GUEST;
    }

    void OnLoginError(BaseEvent evt)
    {
        Debug.Log("Login failed");

        // NOTE: this causes a CONNECTION_LOST event with reason "manual", which in turn removes all SFS listeners
        Disconnect();

        // ResetSplashSceneEvent?.Invoke();
    }

    void OnUdpInit(BaseEvent evt)
    {
        if ((bool)evt.Params["success"])
        {
            Debug.Log("UDP ready!");

            _sfs.AddEventListener(SFSEvent.EXTENSION_RESPONSE, OnExtensionResponse);
            _sfs.AddEventListener(SFSEvent.PROXIMITY_LIST_UPDATE, OnProximityListUpdate);

            // Switch button to Play Button
            OnReadyEvent?.Invoke();
        }
        else
        {
            Debug.Log("UDP initialization failed: " + (string)evt.Params["errorMessage"]);

            // NOTE: this causes a CONNECTION_LOST event with reason "manual", which in turn removes all SFS listeners
            Disconnect();

            // ResetSplashSceneEvent?.Invoke();
        }
    }
}
