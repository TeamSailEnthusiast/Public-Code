using System;
using System.Collections.Generic;
using Sfs2X;
using Sfs2X.Entities.Data;
using Sfs2X.Requests;
using UnityEngine;
using UnityEngine.EventSystems;

public class InputHandler : Singleton<InputHandler>
{
    public static Action OnAnyInput;
    public static Action OnLeftClick;
    public static Action<string> OnHotKey;

    [field: SerializeField] public bool _enabled { get; private set; }

    [Space]
    [Header("Tick")]
    [SerializeField] int _tickRate = 30;
    public float TickDelta;
    float _tickTimer;
    float _secondaryTickTimer;
    int _id;

    [Space]
    [Header("Movement")]
    float _vertical;
    float _horizontal;

    [SerializeField] bool _predictLocal = true;
    [SerializeField] bool _sendInputsToServer = true;
    [SerializeField] bool _reconcile = true;

    [SerializeField] int _maxBufferSize = 128;
    [SerializeField] float _maxSquaredError = 0.004f;

    InputData[] _inputBuffer;
    StateData[] _stateBuffer;
    [SerializeField] List<StateData> _lastReceivedAuthStates = new();
    [SerializeField] List<StateData> _authStates = new();

    int _ackedID = -1;
    int _sendingID;
    int _bufferIndex;
    int _rewindID;
    int _rewindIndex;

    Vector3 _playerPos = new Vector3(0f, 0f, 0f);
    Vector3 _camRot;

    [field: Space]
    [field: Header("Attack")]
    [field: SerializeField] public bool AutoAttack { get; private set; }
    float _attackTimer;
    [SerializeField] float _attackCooldown = 0.1f;

    SFSSimplex _simplex;

    // Debug
    public Vector3 GhostPosRot { get; private set; }

    [field: Space]
    [field: Header("Keyboard")]
    string[] _hotkeys = { "B", "C", "H", "I", "K", "P" };
    KeyCode[] _keyCodes;

    [field: SerializeField] public bool IsShiftPressed { get; private set; }
    [field: SerializeField] public bool IsCtrlPressed { get; private set; }

    // [field: Space]
    // [field: Header("Mouse")]
    [property: SerializeField]
    public bool ContextMenuShown
    {
        get
        {
            return (UI_ContextMenu.Instance.gameObject.activeSelf || UI_ContextMenu.Instance.XInputField.gameObject.activeSelf);
        }
        private set { }
    }


    List<RaycastResult> _underPointerResults = new();
    IContextMenu _iContextMenu;
    bool _foundIContextMenu;

    float _mouseDownTime;
    [SerializeField] float _clickThreshold = 0.2f;

    public void Init(SFSSimplex simplex)
    {
        _simplex = simplex;

        TickDelta = 1f / _tickRate;

        _inputBuffer = new InputData[_maxBufferSize];
        _stateBuffer = new StateData[_maxBufferSize];
        for (int i = 0; i < _maxBufferSize; i++)
        {
            _inputBuffer[i] = new InputData();
            _stateBuffer[i] = new StateData();
        }


        _keyCodes = new KeyCode[_hotkeys.Length];
        for (int i = 0; i < _hotkeys.Length; i++)
        {
            _keyCodes[i] = (KeyCode)System.Enum.Parse(typeof(KeyCode), _hotkeys[i]);
        }


        SFSSimplex.OnReceiveAckEvent += OnReceiveAcknowledgement;
        SFSSimplex.OnReceiveStateEvent += OnReceiveAuthState;

        // OnHotKey += OnHotkeyPressed;

        _enabled = true;
    }
    public void ProcessInput(out bool hasMoved)
    {
        hasMoved = false;
        if (!_enabled) return;

        bool hasInputted = false;

        #region Local Input // Not networked

        IsShiftPressed = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        IsCtrlPressed = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);

        PointerEventData pointerEventData = new PointerEventData(EventSystem.current)
        {
            position = Input.mousePosition
        };

        _underPointerResults.Clear();
        EventSystem.current.RaycastAll(pointerEventData, _underPointerResults);

        if (Input.GetMouseButtonDown(0))
        {
            _mouseDownTime = Time.time;

            if (_underPointerResults.Count != 0)
            {
                UI_ContextOption contextOption = _underPointerResults[0].gameObject.GetComponent<UI_ContextOption>();

                UI_ContextMenu contextMenuPanel = _underPointerResults[0].gameObject.GetComponentInParent<UI_ContextMenu>();

                Debug.Log(_underPointerResults[0].gameObject.name);

                if (contextOption == null && contextMenuPanel == null)
                {
                    UI_ContextMenu.Instance.Hide();
                }
                // else _mouseDownTime -= 10f;
            }
            else
            {
                UI_ContextMenu.Instance.Hide();
            }
        }
        if (Input.GetMouseButtonUp(0))
        {
            hasInputted = true;
            Debug.Log("left mouse up");

            if (Time.time - _mouseDownTime < _clickThreshold)
            {
                Debug.Log("left mouse up");

                // OnLeftClick?.Invoke();

                if (_underPointerResults.Count != 0)
                {
                    Debug.Log(_underPointerResults[0].gameObject);

                    _underPointerResults[0].gameObject.GetComponent<IClickable>()?.OnLeftClick();
                }
            }
        }

        if (Input.GetMouseButtonUp(1))
        {
            // Special case: We need to invoke this before we handle opening the contextMenu
            OnAnyInput?.Invoke();

            _foundIContextMenu = false;
            foreach (RaycastResult result in _underPointerResults)
            {
                if (!result.gameObject.TryGetComponent<IContextMenu>(out _iContextMenu))
                    continue;

                UI_ContextMenu.Instance.OpenContextMenu(_iContextMenu);
                _foundIContextMenu = true;
                break;
            }

            if (!_foundIContextMenu)
            {
                if (ContextMenuShown)
                {
                    UI_ContextMenu.Instance.Hide();
                }
                else
                {
                    // Perform special attack
                }
            }
        }

        CharacterManager.LocalCharacter.Particles.AimAtMouse();


        // Hotkeys
        foreach (KeyCode keyCode in _keyCodes)
        {
            if (Input.GetKeyDown(keyCode))
            {
                hasInputted = true;

                OnHotKey?.Invoke(keyCode.ToString());

                if (keyCode == KeyCode.P)
                {
                    AutoAttack = !AutoAttack;
                }
            }
        }


        #endregion

        #region Networked Input

        _tickTimer += Time.deltaTime;
        _secondaryTickTimer = _tickTimer;

        while (_tickTimer >= TickDelta)
        {
            _vertical = Input.GetAxisRaw("Vertical");
            _horizontal = Input.GetAxisRaw("Horizontal");

            if (HasNetworkedInput())
            {
                _bufferIndex = _id % _maxBufferSize;

                InputData input = _inputBuffer[_bufferIndex];
                input.Reset();
                input.ID = _id;

                // Camera
                if (Input.GetKey(KeyCode.E))
                {
                    input.SetRotationInput(1);

                    if (_predictLocal)
                        OrbitCamera.Instance.Rotate(input.Rotation);
                }
                else if (Input.GetKey(KeyCode.Q))
                {
                    input.SetRotationInput(-1);

                    if (_predictLocal)
                        OrbitCamera.Instance.Rotate(input.Rotation);
                }

                // Movement
                if (_vertical != 0 || _horizontal != 0)
                {
                    input.SetMovementInput(_vertical, _horizontal);

                    if (_predictLocal)
                        CharacterManager.LocalCharacter.Locomotion.Move(input);

                    hasMoved = true;
                }

                // Attacking
                bool attacking = false;
                if ((Input.GetMouseButton(0) || AutoAttack) && CharacterManager.LocalCharacter.Equipment.HasWeaponEquipped())
                {
                    if (_attackTimer <= 0f)
                    {
                        attacking = true;
                        hasInputted = true;

                        CharacterManager.LocalCharacter.Particles.Emit();
                        CharacterManager.LocalCharacter.AudioSource.Play();

                        _attackTimer += _attackCooldown;
                    }
                }

                // Character Animation
                CharacterManager.LocalCharacter.Animator.Animate(input.Vertical, input.Horizontal, TickDelta, attacking);

                // Buffer state
                StateData postInputState = _stateBuffer[_bufferIndex];
                postInputState.Reset();
                postInputState.ID = _id;

                postInputState.x = CharacterManager.LocalCharacter.Locomotion.transform.position.x;
                postInputState.z = CharacterManager.LocalCharacter.Locomotion.transform.position.z;
                postInputState.camRot = OrbitCamera.Instance.transform.localEulerAngles.y;

                // Only tick forward if we processed networked input
                _id++;
            }
            else
            {
                CharacterManager.LocalCharacter.Animator.Animate(0f, 0f, TickDelta, false);
            }

            if (_attackTimer > 0f)
            {
                _attackTimer -= TickDelta;
            }

            _secondaryTickTimer -= TickDelta;

            if (_secondaryTickTimer >= TickDelta) continue;

            if (_sendInputsToServer)
                SendInputsToServer();

            if (_reconcile)
                Reconcile();

            _tickTimer = _secondaryTickTimer;
        }
        #endregion

        if (hasInputted) OnAnyInput?.Invoke();
    }

    bool HasNetworkedInput()
    {
        // Movement
        if (_vertical != 0 || _horizontal != 0)
        {
            return true;
        }

        // Camera
        if (Input.GetKey(KeyCode.E))
        {
            return true;
        }
        else if (Input.GetKey(KeyCode.Q))
        {
            return true;
        }

        // Attacking - probably can remove sending attack input to server if we have autoAttack toggled on
        if (_attackTimer <= 0f && (Input.GetMouseButton(0) || AutoAttack))
        {
            return true;
        }

        return false;
    }

    // "i" request msg --->  "i" as key for first _sendingID, "f"+i as key for each input byte Flags
    void SendInputsToServer()
    {
        _sendingID = Mathf.Clamp(_ackedID + 1, 0, _id);

        int length = _id - _sendingID;
        if (length == 0) return;

        // Only send the first inputID, server can infer the rest.
        ISFSObject inputsSFSObj = new SFSObject();
        inputsSFSObj.PutInt("i", _sendingID);

        for (int i = 0; i < length; i++)
        {
            _bufferIndex = _sendingID % _maxBufferSize;
            InputData sendingInput = _inputBuffer[_bufferIndex];

            // Fill SFSObject with byte flags from sendingInput element
            inputsSFSObj.PutByte("f" + i, sendingInput.Flags);

            ++_sendingID;
        }
        _simplex.SendRoomUDP("i", inputsSFSObj);
    }

    void Reconcile()
    {
        if (_lastReceivedAuthStates.Count == 0) return;

        _authStates.AddRange(_lastReceivedAuthStates);
        _lastReceivedAuthStates.Clear();

        foreach (StateData state in _authStates)
        {
            StateData predictedState = _stateBuffer[state.ID % _maxBufferSize];
            if (SquaredDistance(predictedState.x, predictedState.z, state.x, state.z) < _maxSquaredError &&
                predictedState.camRot == state.camRot) continue;

            _playerPos.x = state.x;
            _playerPos.z = state.z;
            CharacterManager.LocalCharacter.Locomotion.transform.position = _playerPos;

            _camRot.y = state.camRot;
            OrbitCamera.Instance.transform.localEulerAngles = _camRot;

            _rewindID = state.ID;
            _rewindIndex = _rewindID % _maxBufferSize;

            StateData rewindedState = _stateBuffer[_rewindIndex];
            rewindedState.x = state.x;
            rewindedState.z = state.z;
            rewindedState.camRot = state.camRot;


            ++_rewindID;
            while (_rewindID < _id)
            {
                _rewindIndex = _rewindID % _maxBufferSize;

                OrbitCamera.Instance.Rotate(_inputBuffer[_rewindIndex].Rotation);
                CharacterManager.LocalCharacter.Locomotion.Move(_inputBuffer[_rewindIndex]);

                // Update state buffer
                rewindedState = _stateBuffer[_rewindIndex];
                rewindedState.x = CharacterManager.LocalCharacter.Locomotion.transform.position.x;
                rewindedState.z = CharacterManager.LocalCharacter.Locomotion.transform.position.z;
                rewindedState.camRot = state.camRot;

                ++_rewindID;
            }
        }
        _authStates.Clear();
    }

    // "a" response msg --> "i" for int id
    /// <summary>
    /// Every time the client sends input to the server, the server will immediately send an acknowledgment back.
    /// This lets the client know to stop sending inputs that the server has already received successfully.
    /// </summary>
    /// <param name="ackedID"></param>
    void OnReceiveAcknowledgement(SFSObject msg)
    {
        _ackedID = Mathf.Max(_ackedID, msg.GetInt("i"));
    }

    // "s" response msg --> "i" for int id, "x" for float x, "z" for float z, "r" for float rotCam
    void OnReceiveAuthState(SFSObject msg)
    {
        StateData receivedState = new StateData(msg.GetInt("i"),
                                                msg.GetFloat("x"),
                                                msg.GetFloat("z"),
                                                msg.GetFloat("r"));

        Debug.Log("Received authState: " + "(" + receivedState.x + ", " + receivedState.z + ")");

        _lastReceivedAuthStates.Add(receivedState);
        _ackedID = Mathf.Max(_ackedID, receivedState.ID);

        GhostPosRot = new Vector3(receivedState.x, receivedState.camRot, receivedState.z);
    }

    float SquaredDistance(float ax, float ay, float bx, float by)
    {
        return ((ax - bx) * (ax - bx)) + ((ay - by) * (ay - by));
    }

    public void SetFireCooldown(float _attackCooldown)
    {
        this._attackCooldown = _attackCooldown;
    }
}


/// <summary>
// /// OBSOLETE. Removes all elements from buffers whose id value is lower than the param id.
// /// </summary>
// /// <param name="id"></param>
// void DiscardOldData(int id)
// {

//     // This could be optimized by finding the correct index and then working our way down to 0
//     // We know that a higher index always has a higher id, and a lower index has a lower id
//     // Maybe like so:

//     // _bufferIndex = id % _maxBufferSize;

//     // for (int i = _bufferIndex; i >= 0; --i)
//     // {
//     //     _inputBuffer[i].ID = -1;
//     // }

//     // Iterate through the input buffer to find elements to remove (old  and unoptimized)
//     // for (int i = 0; i < _maxBufferSize; i++)
//     // {
//     //     if (_inputBuffer[i].ID < id)
//     //     {
//     //         // Set ID to -1 to signal it represents a null value
//     //         _inputBuffer[i].ID = -1;
//     //     }
//     // }
// }

//     public void Init()
//     {
//         TickDelta = ClientManager.Instance.TickDelta;

//         _bufferKeys = new int[_maxBufferSize];
//         for (int i = 0; i < _maxBufferSize; i++)
//         {
//             _bufferKeys[i] = int.MaxValue;
//         }
//     }

//     void Update()
//     {
//         // Movement
//         _vertical = Input.GetAxisRaw("_vertical");
//         _horizontal = Input.GetAxisRaw("_horizontal");

//         if (_tickTimer < TickDelta)
//         {
//             _tickTimer += Time.deltaTime;
//         }

//         if (_vertical != 0 || _horizontal != 0)
//         {
//             while (_tickTimer >= TickDelta)
//             {
//                 _tickTimer -= TickDelta;

//                 BufferInputAndMovePlayer();
//                 SendInputsToServer();
//                 _id++;

//                 Reconcile();
//             }
//         }
//         #region local-only
//         // Camera
//         if (Input.GetKey(KeyCode.E))
//         {
//             OrbitCamera.Instance.Rotate(1);
//         }
//         else if (Input.GetKey(KeyCode.Q))
//         {
//             OrbitCamera.Instance.Rotate(-1);
//         }
//         // Mouse
//         if (_attackTimer <= 0f && (Input.GetMouseButton(0) || AutoFire))
//         {
//             CharacterManager.LocalCharacter.Locomotion.Attack();
//             _audioSource.Play();

//             _attackTimer += _attackCooldown;
//         }
//         if (_attackTimer > 0f)
//         {
//             _attackTimer -= Time.deltaTime;
//         }

//         if (Input.GetKeyDown(KeyCode.T))
//         {
//              CharacterManager.LocalCharacter.Particles.OnWeaponChanged();
//             SetFireCooldown(CharacterStats.Instance.GetCastingCooldownTime());
//         }

//         if (Input.GetKeyDown(KeyCode.I))
//         {
//             AutoFire = !AutoFire;
//         }

//         if (Input.GetKeyDown(KeyCode.Keypad8))
//         {
//             CharacterStats.Instance.AddDexterity(5);
//             SetFireCooldown(CharacterStats.Instance.GetCastingCooldownTime());

//         }
//         if (Input.GetKeyDown(KeyCode.Keypad2))
//         {
//             CharacterStats.Instance.AddDexterity(-5);
//             SetFireCooldown(CharacterStats.Instance.GetCastingCooldownTime());
//         }
//         #endregion
//     }


//     void BufferInputAndMovePlayer()
//     {
//         InputData newInput;
//         StateData preInputState;

//         if (_inputBuffer.Count > _maxBufferSize)
//         {
//             // If our buffers are full, stop allocating memory with new instances, instead reuse the oldest ones.
//             var key = _bufferKeys[_id % _maxBufferSize];

//             // Reset input data and state data of oldest instances found
//             newInput = _inputBuffer[key];
//             newInput.Reset(_id, _vertical, _horizontal);

//             preInputState = _stateBuffer[key];
//             preInputState.Reset(_id,
//                                 CharacterManager.LocalCharacter.Locomotion.transform.position.x,
//                                 CharacterManager.LocalCharacter.Locomotion.transform.position.z);

//             // Remove old input data and state data from the buffers
//             _inputBuffer.Remove(key);
//             _stateBuffer.Remove(key);

//             // Add new input data and state data to the buffers
//             _inputBuffer.Add(_id, newInput);
//             _stateBuffer.Add(_id, preInputState);

//             // Store the new key
//             _bufferKeys[_id % _maxBufferSize] = _id;
//         }
//         else
//         {
//             // Create new input data and state data instances (this allocates memory)
//             newInput = new InputData();
//             newInput.Reset(_id, _vertical, _horizontal);

//             preInputState = new StateData();
//             preInputState.Reset(_id,
//                                 CharacterManager.LocalCharacter.Locomotion.transform.position.x,
//                                 CharacterManager.LocalCharacter.Locomotion.transform.position.z);

//             // Add new input data and state data to the buffers
//             _inputBuffer[_id] = newInput;
//             _stateBuffer[_id] = preInputState;

//             // Store the new key
//             _bufferKeys[_id % _maxBufferSize] = _id;
//         }

//         // Move the player
//         CharacterManager.LocalCharacter.Locomotion.Move(newInput);
//     }

//     void SendInputsToServer()
//     {
//         foreach (var input in _inputBuffer)
//         {

//         }
//     }

//     void Reconcile()
//     {
//         // Replay inputs if necessary
//         state = _lastReceivedAuthState;
//         if (state == null || state.ID == 0) return;

//         var predictedState = _stateBuffer[state.ID];
//         if (SquaredDistance(predictedState.x, predictedState.z, state.x, state.z) < _maxSquaredError) return;

//         _playerPos.x = state.x;
//         _playerPos.z = state.z;
//         CharacterManager.LocalCharacter.Locomotion.transform.position = _playerPos;

//         _rewindID = state.ID;
//         while (_rewindID < _id)
//         {
//             if (_rewindID != _inputBuffer[_rewindID].ID)
//             {
//                 Debug.Log("Mismatch between _rewindID and inputBuffer element _id.");
//                 Debug.Log("_rewindID = " + _rewindID + " inputData.ID = " + _inputBuffer[_rewindID].ID);
//                 continue;
//             }
//             // Update state buffer with player position
//             _stateBuffer[_rewindID].x = CharacterManager.LocalCharacter.Locomotion.transform.position.x;
//             _stateBuffer[_rewindID].z = CharacterManager.LocalCharacter.Locomotion.transform.position.z;

//             CharacterManager.LocalCharacter.Locomotion.Move(_inputBuffer[_rewindID]);

//             ++_rewindID;
//         }
//         state = null;
//     }

//     /// <summary>
//     /// Everytime the client sends input to the server, the server will immediately send an acknowledgement back
//     /// This lets the client know to stop sending inputs that the server has already received successfully.
//     /// </summary>
//     /// <param name="ackedID"></param>
//     void OnReceiveTickAcknowledgement(int ackedID)
//     {
//         DiscardOldData(ackedID);
//     }

//     void OnReceiveAuthState(StateData receivedState)
//     {
//         if (receivedState.ID > _lastReceivedAuthState.ID)
//             _lastReceivedAuthState = receivedState;

//         DiscardOldData(receivedState.ID);
//     }


//     /// <summary>
//     /// Removes all elements from buffers whose id value is lower than the param id.
//     /// </summary>
//     /// <param name="id"></param>
//     void DiscardOldData(int id)
//     {
//         // Iterate through the input buffer to find elements to remove
//         foreach (var entry in _inputBuffer)
//         {
//             if (entry.Key < id)
//             {
//                 _keysToRemove.Add(entry.Key);
//             }
//         }

//         // Remove elements from the buffers based on the keys collected
//         foreach (var key in _keysToRemove)
//         {
//             _inputBuffer.Remove(key);
//             _stateBuffer.Remove(key);
//         }
//         _keysToRemove.Clear();
//     }

//     float SquaredDistance(float ax, float ay, float bx, float by)
//     {
//         return ((ax - bx) * (ax - bx)) + ((ay - by) * (ay - by));
//     }

//     public void SetFireCooldown(float _attackCooldown)
//     {
//         this._attackCooldown = _attackCooldown;
//     }
// }


// void OnTick()
// {
//     // Movement
//     _vertical = Input.GetAxisRaw("_vertical");
//     _horizontal = Input.GetAxisRaw("_horizontal");

//     if (_vertical != 0 || _horizontal != 0)
//     {
//         BufferInputAndMovePlayer();
//         SendInputsToServer();
//         Reconcile();
//     }
//     #region local-only
//     // Camera
//     if (Input.GetKey(KeyCode.E))
//     {
//         OrbitCamera.Instance.Rotate(1);
//     }
//     else if (Input.GetKey(KeyCode.Q))
//     {
//         OrbitCamera.Instance.Rotate(-1);
//     }
//     // Mouse
//     if (_attackTimer <= 0f && (Input.GetMouseButton(0) || AutoFire))
//     {
//         CharacterManager.LocalCharacter.Locomotion.Attack();
//         _audioSource.Play();

//         _attackTimer += _attackCooldown;
//     }
//     if (_attackTimer > 0f)
//     {
//         _attackTimer -= Time.deltaTime;
//     }

//     if (Input.GetKeyDown(KeyCode.T))
//     {
//          CharacterManager.LocalCharacter.Particles.OnWeaponChanged();
//         SetFireCooldown(CharacterStats.Instance.GetCastingCooldownTime());
//     }

//     if (Input.GetKeyDown(KeyCode.I))
//     {
//         AutoFire = !AutoFire;
//     }

//     if (Input.GetKeyDown(KeyCode.Keypad8))
//     {
//         CharacterStats.Instance.AddDexterity(5);
//         SetFireCooldown(CharacterStats.Instance.GetCastingCooldownTime());

//     }
//     if (Input.GetKeyDown(KeyCode.Keypad2))
//     {
//         CharacterStats.Instance.AddDexterity(-5);
//         SetFireCooldown(CharacterStats.Instance.GetCastingCooldownTime());
//     }
//     #endregion
// }



// Send input buffer to server for authoritative simulation
// foreach (var input in _inputBuffer)
// {
//     if (input.ID == 0) continue;

//     // Only send (int ID, byte Flags, float DeltaTime) from each InputData element in the buffer
//     // Maybe we dont even need to send the deltaTime, need to test it
// }

// public class InputHandler : Singleton<InputHandler>
// {
//     [SerializeField] AudioSource _audioSource;
//     [SerializeField] AudioClip _castingSFX;

//     public float _vertical { get; private set; }
//     public float _horizontal { get; private set; }

//     [SerializeField] int _maxBufferSize = 10;
//     [SerializeField] List<InputData> _inputBuffer = new();
//     [SerializeField] Dictionary<int, StateData> _stateBuffer = new();

//     [SerializeField] List<StateData> _authStates = new();

//     public bool AutoFire { get; private set; }
//     float _attackTimer;
//     [SerializeField] float _attackCooldown = 0.1f;
//     Vector3 _playerPos;


//     void Start()
//     {
//         ClientManager.OnTick += OnTick;
//     }

//     void OnTick()
//     {
//         // Movement
//         _vertical = Input.GetAxisRaw("_vertical");
//         _horizontal = Input.GetAxisRaw("_horizontal");

//         if (_vertical != 0 || _horizontal != 0)
//         {
//             InputData newInput;
//             StateData preInputState;
//             if (_inputBuffer.Count < _maxBufferSize)
//             {
//                 newInput = new InputData(_vertical, _horizontal, _id - 1, ClientManager.Instance.TickDelta);
//                 _inputBuffer.Add(newInput);

//                 preInputState = new StateData(_id - 1,
//                                                    CharacterManager.LocalCharacter.Locomotion.transform.position.x,
//                                                    CharacterManager.LocalCharacter.Locomotion.transform.position.z);
//                 _stateBuffer.Add(_id - 1, preInputState);
//             }
//             else
//             {
//                 // Reuse the oldest InputData instance to avoid allocating memory every frame
//                 newInput = _inputBuffer[0];
//                 newInput.Reset(_vertical, _horizontal, _id - 1, ClientManager.Instance.TickDelta);

//                 // Rotate the list to maintain the order of the elements
//                 _inputBuffer.RemoveAt(0);
//                 _inputBuffer.Add(newInput);

//                 // Do the same thing with state buffer
//                 preInputState = _stateBuffer[0];
//                 int oldTickKey = preInputState.ID;

//                 preInputState.Reset(_id - 1,
//                                     CharacterManager.LocalCharacter.Locomotion.transform.position.x,
//                                     CharacterManager.LocalCharacter.Locomotion.transform.position.z);

//                 _stateBuffer.Remove(oldTickKey);
//                 _stateBuffer.Add(preInputState.ID, preInputState);
//             }

//             // Send input buffer to server for authoritative simulation
//             foreach (var input in _inputBuffer)
//             {
//                 // Only send (int ID, byte Flags, float DeltaTime) from each InputData element in the buffer
//                 // Maybe we dont even need to send the deltaTime, need to test it
//             }

//             CharacterManager.LocalCharacter.Locomotion.Move(newInput);

//             // Loop through all received authoritative server states and replay the inputs if necessary
//             for (int i = 0; i < _authStates.Count; i++)
//             {
//                 if (_authStates[i] == null) continue;

//                 var predictedState = _stateBuffer[_authStates[i].ID];
//                 var authState = _authStates[i];

//                 if (SquaredDistance(predictedState.x, predictedState.z, authState.x, authState.z) > 0.01f)
//                 {
//                     _playerPos.x = authState.x;
//                     _playerPos.z = authState.z;
//                     CharacterManager.LocalCharacter.Locomotion.transform.position = _playerPos;

//                     int _rewindID = authState.ID;
//                     while (_rewindID < _id)
//                     {
//                         // I dont understand this first line, looks like we are setting all replaying inputs
//                         // to our last input?? Seems odd.
//                         // this.client_input_buffer[buffer_slot] = inputs;
//                         // Probably need to get the actual input from the local buffer that corresponds to this tick


//                         // this.client_state_buffer[buffer_slot].position = player_rigidbody.position;
//                         // this.client_state_buffer[buffer_slot].rotation = player_rigidbody.rotation;
//                         _stateBuffer[_rewindID].x = CharacterManager.LocalCharacter.Locomotion.transform.position.x;
//                         _stateBuffer[_rewindID].z = CharacterManager.LocalCharacter.Locomotion.transform.position.z;

//                         // this.AddForcesToPlayer(player_rigidbody, inputs);
//                         // Physics.Simulate(Time.fixedDeltaTime);
//                         CharacterManager.LocalCharacter.Locomotion.Move(newInput);

//                         ++_rewindID;
//                     }

//                 }
//                 _authStates[i] = null;
//             }
//         }

//         // Camera
//         if (Input.GetKey(KeyCode.E))
//         {
//             OrbitCamera.Instance.Rotate(1);
//         }
//         else if (Input.GetKey(KeyCode.Q))
//         {
//             OrbitCamera.Instance.Rotate(-1);
//         }
//         // Mouse
//         if (_attackTimer <= 0f && (Input.GetMouseButton(0) || AutoFire))
//         {
//             CharacterManager.LocalCharacter.Locomotion.Attack();
//             _audioSource.Play();

//             _attackTimer += _attackCooldown;
//         }
//         if (_attackTimer > 0f)
//         {
//             _attackTimer -= Time.deltaTime;
//         }

//         if (Input.GetKeyDown(KeyCode.T))
//         {
//              CharacterManager.LocalCharacter.Particles.OnWeaponChanged();
//             SetFireCooldown(CharacterStats.Instance.GetCastingCooldownTime());
//         }

//         if (Input.GetKeyDown(KeyCode.I))
//         {
//             AutoFire = !AutoFire;
//         }

//         if (Input.GetKeyDown(KeyCode.Keypad8))
//         {
//             CharacterStats.Instance.AddDexterity(5);
//             SetFireCooldown(CharacterStats.Instance.GetCastingCooldownTime());

//         }
//         if (Input.GetKeyDown(KeyCode.Keypad2))
//         {
//             CharacterStats.Instance.AddDexterity(-5);
//             SetFireCooldown(CharacterStats.Instance.GetCastingCooldownTime());
//         }
//     }

//     /// <summary>
//     /// Removes all elements whose tick value is lower than or equal to the input tickValue
//     /// </summary>
//     /// <param name="tickValue"></param>
//     void DiscardOldInputs(int tickValue)
//     {
//         // Iterate through the buffer to find elements to remove
//         for (int i = _inputBuffer.Count - 1; i >= 0; i--)
//         {
//             if (_inputBuffer[i].ID <= tickValue)
//             {
//                 _inputBuffer.RemoveRange(0, i + 1);
//                 break;
//             }
//         }
//     }

//     float SquaredDistance(float ax, float ay, float bx, float by)
//     {
//         return ((ax - bx) * (ax - bx)) + ((ay - by) * (ay - by));
//     }
//     // private IEnumerator Shoot()
//     // {
//     //     while (true)
//     //     {
//     //         if (_mayShoot)
//     //         {


//     //             _mayShoot = false;
//     //             yield return new WaitForSeconds(_attackCooldown);
//     //         }
//     //         yield return null;
//     //     }
//     // }

//    
// }
