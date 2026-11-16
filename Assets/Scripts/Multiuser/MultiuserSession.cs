using System;
using System.Collections.Generic;
using System.IO;
using Fusion;
using Fusion.Photon.Realtime;
using UnityEngine;

// Multiuser entry point. Creates itself when the main scene starts (nothing to add to the scene),
// joins a Photon Fusion shared-mode session and keeps the app state of all users in step:
//   MultiuserTransformSync - SegmentedVolumeObject and the grabbable objects under it
//   MultiuserUiSync        - button presses and slider values
//   MultiuserTfSync        - transfer function points and zoom scope
//   MultiuserAxisSync      - axes placed with the UniTool
//
// Each part produces small messages. They are packed into byte chunks and sent to everybody through
// one reliable, ordered RPC (MultiuserRpc). Only state is sent, never volume data: both PCs need the
// same datasets in StreamingAssets/Datasets.
//
// Settings: StreamingAssets/multiuser.json (optional).
public class MultiuserSession : MonoBehaviour
{
    public const byte MsgHello = 1;
    public const byte MsgSnapshotBegin = 2;
    public const byte MsgSnapshotEnd = 3;
    public const byte MsgTransform = 10;
    public const byte MsgClick = 11;
    public const byte MsgSlider = 12;
    public const byte MsgButtonState = 13;
    public const byte MsgActive = 14;
    public const byte MsgTf = 20;
    public const byte MsgAxes = 21;

    [Serializable]
    public class Config
    {
        public bool enabled = true;
        public string sessionName = "LERCO";
        // Photon region both PCs connect to; empty = let Photon pick the best one
        public string region = "eu";
        // more objects to keep in step besides SegmentedVolumeObject: "Name" or "Name/Child"
        public string[] extraSyncedObjects = new string[0];
    }

    enum State { Offline, Connecting, Joining, Live }

    struct Inbound
    {
        public int sender;
        public byte type;
        public byte[] payload;
    }

    class Partial
    {
        public int total;
        public MemoryStream buffer;
    }

    const int MaxChunkBytes = 400;          // a Fusion RPC carries at most 512 bytes
    const float PollInterval = 0.05f;       // 20 updates per second while something changes
    const float ScanInterval = 1f;
    const float ReconnectDelay = 5f;
    const float SettleTime = 1f;            // wait this long after joining before asking for the state
    const float SnapshotTimeout = 10f;

    public static MultiuserSession Instance { get; private set; }

    // Shared objects are exchanged relative to this transform. It sits at the world origin for now;
    // aligning the two headsets later only means moving it.
    public static Transform SharedOrigin { get { return Instance != null ? Instance._sharedOrigin : null; } }

    public Config Settings { get; private set; }
    public VolumeSolution Solution { get; private set; }
    public bool CanSend { get { return _state == State.Live; } }

    Transform _sharedOrigin;
    NetworkRunner _runner;
    State _state = State.Offline;

    MultiuserTransformSync _transforms;
    MultiuserUiSync _ui;
    MultiuserTfSync _tf;
    MultiuserAxisSync _axes;

    readonly List<byte[]> _outbox = new List<byte[]>();
    readonly Queue<Inbound> _inbox = new Queue<Inbound>();
    readonly Dictionary<int, Partial> _partials = new Dictionary<int, Partial>();
    readonly MemoryStream _writeStream = new MemoryStream();
    BinaryWriter _writer;

    float _nextConnect;
    float _nextPoll;
    float _nextScan;
    float _joinedAt;
    float _helloAt;
    bool _helloSent;
    bool _inSnapshot;
    bool _snapshotRequested;
    bool _wasBusy;
    bool _loadStartedRemotely;
    int _lastOthers = -1;
    int _lowestJoiningPeer = -1;

    // ------------------------------------------------------------------ start-up

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Instance != null) return;

        // only the main app scene has a VolumeSolution; sample scenes stay untouched
        VolumeSolution solution = FindObjectOfType<VolumeSolution>(true);
        if (solution == null) return;

        Config config = LoadConfig();
        if (!config.enabled)
        {
            Debug.Log("[Multiuser] disabled in multiuser.json");
            return;
        }

        GameObject go = new GameObject("MultiuserSession");
        DontDestroyOnLoad(go);
        MultiuserSession session = go.AddComponent<MultiuserSession>();
        session.Settings = config;
        session.Solution = solution;
    }

    static Config LoadConfig()
    {
        Config config = new Config();
        try
        {
            string path = Path.Combine(Application.streamingAssetsPath, "multiuser.json");
            if (File.Exists(path)) JsonUtility.FromJsonOverwrite(File.ReadAllText(path), config);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Multiuser] could not read multiuser.json, using defaults: " + e.Message);
        }
        if (string.IsNullOrEmpty(config.sessionName)) config.sessionName = "LERCO";
        return config;
    }

    void Awake()
    {
        Instance = this;
        _writer = new BinaryWriter(_writeStream);

        GameObject origin = new GameObject("SharedOrigin");
        origin.transform.SetParent(transform, false);
        _sharedOrigin = origin.transform;
    }

    void Start()
    {
        Application.runInBackground = true;

        _transforms = new MultiuserTransformSync(this);
        _ui = new MultiuserUiSync(this);
        _tf = new MultiuserTfSync(this);
        _axes = new MultiuserAxisSync(this);

        MultiuserRpc.ChunkReceived += OnChunk;
    }

    void OnDestroy()
    {
        MultiuserRpc.ChunkReceived -= OnChunk;
        if (_runner != null) _runner.Shutdown();
        if (Instance == this) Instance = null;
    }

    // ------------------------------------------------------------------ connection

    async void Connect()
    {
        _state = State.Connecting;

        GameObject go = new GameObject("MultiuserRunner");
        NetworkRunner runner = go.AddComponent<NetworkRunner>();
        runner.ProvideInput = false;
        _runner = runner;

        try
        {
            StartGameArgs args = new StartGameArgs
            {
                GameMode = GameMode.Shared,
                SessionName = Settings.sessionName,
            };
            if (!string.IsNullOrEmpty(Settings.region))
            {
                // both PCs must end up in the same region to meet in the same session
                FusionAppSettings appSettings = PhotonAppSettings.Global.AppSettings.GetCopy();
                appSettings.FixedRegion = Settings.region;
                args.CustomPhotonAppSettings = appSettings;
            }

            StartGameResult result = await runner.StartGame(args);

            if (this == null || _runner != runner) return;
            if (!result.Ok)
            {
                Debug.LogWarning("[Multiuser] could not join session '" + Settings.sessionName + "': " + result.ShutdownReason + " " + result.ErrorMessage);
                Disconnected();
                return;
            }
        }
        catch (Exception e)
        {
            if (this == null || _runner != runner) return;
            Debug.LogWarning("[Multiuser] could not join session '" + Settings.sessionName + "': " + e.Message);
            Disconnected();
            return;
        }

        Debug.Log("[Multiuser] joined session '" + Settings.sessionName + "' as player " + LocalId);
        _state = State.Joining;
        _joinedAt = Time.unscaledTime;
        _helloSent = false;
        _inSnapshot = false;
        _lastOthers = -1;
        _lowestJoiningPeer = -1;
    }

    void Disconnected()
    {
        if (_state == State.Joining || _state == State.Live) Debug.LogWarning("[Multiuser] connection lost, retrying");

        NetworkRunner runner = _runner;
        _runner = null;
        if (runner != null)
        {
            if (!runner.IsShutdown) runner.Shutdown();
            Destroy(runner.gameObject);
        }

        _state = State.Offline;
        _nextConnect = Time.unscaledTime + ReconnectDelay;
        _outbox.Clear();
        _inbox.Clear();
        _partials.Clear();
        _snapshotRequested = false;
    }

    int LocalId { get { return _runner != null ? _runner.LocalPlayer.PlayerId : -1; } }

    int CountOthers()
    {
        int local = LocalId;
        int others = 0;
        foreach (PlayerRef player in _runner.ActivePlayers)
        {
            if (player.PlayerId != local) others++;
        }
        return others;
    }

    // a dataset is loading: nothing is applied or sent until the volume is ready
    bool Busy { get { return Solution != null && Solution.IsLoadingDataset; } }

    // ------------------------------------------------------------------ main loop

    void LateUpdate()
    {
        float now = Time.unscaledTime;

        if (_state == State.Offline)
        {
            if (now >= _nextConnect) Connect();
            return;
        }
        if (_state == State.Connecting) return;

        if (_runner == null || _runner.IsShutdown)
        {
            Disconnected();
            return;
        }
        if (!_runner.IsRunning) return;

        bool busy = Busy;
        if (_wasBusy && !busy)
        {
            // a load the other user started changed this app the same way it changed theirs: nothing to report
            if (_loadStartedRemotely && _state == State.Live) RebaselineAll();
            _loadStartedRemotely = false;
        }
        _wasBusy = busy;

        if (now >= _nextScan)
        {
            _nextScan = now + ScanInterval;
            RescanAll();
            ReportPlayers();
        }

        if (_state == State.Joining) UpdateJoining(now);

        if (_state == State.Live && !busy)
        {
            // local changes always go out before incoming ones are applied, so none are overwritten unseen
            if (now >= _nextPoll || _inbox.Count > 0)
            {
                _nextPoll = now + PollInterval;
                PollAll();
            }
            if (_snapshotRequested)
            {
                _snapshotRequested = false;
                SendSnapshot();
            }
        }

        Flush();
        if (!busy) ProcessInbox();
        _transforms.Tick(Time.unscaledDeltaTime);
    }

    void RescanAll()
    {
        _ui.Rescan();
        _transforms.Rescan();
    }

    void PollAll()
    {
        _ui.Poll();
        _transforms.Poll();
        _tf.Poll();
        _axes.Poll();
    }

    // take the current state as "already known to everybody"
    void RebaselineAll()
    {
        _ui.Rebaseline();
        _transforms.Rebaseline();
        _tf.Rebaseline();
        _axes.Rebaseline();
    }

    void ReportPlayers()
    {
        int others = CountOthers();
        if (others == _lastOthers) return;
        _lastOthers = others;
        Debug.Log("[Multiuser] other users in session: " + others);

        // forget half-received data of users who left
        if (_partials.Count > 0)
        {
            List<int> gone = new List<int>();
            foreach (int sender in _partials.Keys)
            {
                bool present = false;
                foreach (PlayerRef player in _runner.ActivePlayers)
                {
                    if (player.PlayerId == sender) present = true;
                }
                if (!present) gone.Add(sender);
            }
            foreach (int sender in gone) _partials.Remove(sender);
        }
    }

    // ------------------------------------------------------------------ joining: get the current state first

    void UpdateJoining(float now)
    {
        if (!_helloSent)
        {
            if (now - _joinedAt < SettleTime) return;
            if (CountOthers() == 0)
            {
                GoLive("first user in the session");
                return;
            }
            Enqueue(MsgHello, null);
            _helloSent = true;
            _helloAt = now;
            return;
        }

        if (CountOthers() == 0)
        {
            GoLive("the other user left");
        }
        else if (!_inSnapshot && _lowestJoiningPeer >= 0 && LocalId < _lowestJoiningPeer)
        {
            // both apps were started together and wait for each other: the lower player number goes first
            GoLive("started together with the other user");
            _snapshotRequested = true;
        }
        else if (!_inSnapshot && now - _helloAt > SnapshotTimeout)
        {
            Debug.LogWarning("[Multiuser] nobody sent the current state, continuing without it");
            GoLive("timeout");
        }
    }

    void GoLive(string reason)
    {
        _state = State.Live;
        _inSnapshot = false;
        RescanAll();
        RebaselineAll();
        Debug.Log("[Multiuser] live (" + reason + ")");
    }

    void SendSnapshot()
    {
        Enqueue(MsgSnapshotBegin, null);
        _ui.SendRadioStates();      // dataset first: the receiver loads it before applying the rest
        _tf.SendFull();             // zoom scope has to be known before the zoom toggle is replayed
        _ui.SendFull();
        _transforms.SendFull();
        _axes.SendFull();
        _tf.SendFull();             // final point positions, after the zoom toggle
        Enqueue(MsgSnapshotEnd, null);
        Debug.Log("[Multiuser] sent the current state to a user who joined");
    }

    // ------------------------------------------------------------------ sending

    // used by the sync parts; only goes out while this app is a full member of the session
    public void Send(byte type, Action<BinaryWriter> write)
    {
        if (!CanSend) return;
        Enqueue(type, write);
    }

    void Enqueue(byte type, Action<BinaryWriter> write)
    {
        _writeStream.SetLength(0);
        _writer.Write(type);
        _writer.Write((ushort)0);
        if (write != null) write(_writer);
        _writer.Flush();

        byte[] message = _writeStream.ToArray();
        int length = message.Length - 3;
        if (length > ushort.MaxValue)
        {
            Debug.LogWarning("[Multiuser] message of type " + type + " is too large, dropped");
            return;
        }
        message[1] = (byte)(length & 0xFF);
        message[2] = (byte)(length >> 8);
        _outbox.Add(message);
    }

    // Chunk layout: first byte 0 = whole messages follow, 1 = first piece of one large message
    // (followed by its total length), 2 = next piece of that message.
    void Flush()
    {
        if (_outbox.Count == 0) return;

        List<byte> chunk = new List<byte>(MaxChunkBytes) { 0 };
        foreach (byte[] message in _outbox)
        {
            if (message.Length + 1 > MaxChunkBytes)
            {
                if (chunk.Count > 1)
                {
                    SendChunk(chunk.ToArray());
                    chunk.Clear();
                    chunk.Add(0);
                }
                SendSplit(message);
                continue;
            }
            if (chunk.Count + message.Length > MaxChunkBytes)
            {
                SendChunk(chunk.ToArray());
                chunk.Clear();
                chunk.Add(0);
            }
            chunk.AddRange(message);
        }
        if (chunk.Count > 1) SendChunk(chunk.ToArray());
        _outbox.Clear();
    }

    void SendSplit(byte[] message)
    {
        int offset = 0;
        bool first = true;
        while (offset < message.Length)
        {
            int header = first ? 5 : 1;
            int count = Math.Min(MaxChunkBytes - header, message.Length - offset);
            byte[] piece = new byte[header + count];
            piece[0] = (byte)(first ? 1 : 2);
            if (first) Buffer.BlockCopy(BitConverter.GetBytes(message.Length), 0, piece, 1, 4);
            Buffer.BlockCopy(message, offset, piece, header, count);
            SendChunk(piece);
            offset += count;
            first = false;
        }
    }

    void SendChunk(byte[] data)
    {
        try
        {
            MultiuserRpc.Rpc_Chunk(_runner, LocalId, data);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Multiuser] send failed: " + e.Message);
        }
    }

    // ------------------------------------------------------------------ receiving

    void OnChunk(int sender, byte[] data)
    {
        if (_runner == null || data == null || data.Length == 0) return;
        if (sender == LocalId) return;

        byte kind = data[0];
        if (kind == 0)
        {
            _partials.Remove(sender);
            ParseMessages(sender, data, 1, data.Length - 1);
        }
        else if (kind == 1)
        {
            if (data.Length < 5) return;
            Partial partial = new Partial { total = BitConverter.ToInt32(data, 1), buffer = new MemoryStream() };
            partial.buffer.Write(data, 5, data.Length - 5);
            _partials[sender] = partial;
            CompletePartial(sender, partial);
        }
        else if (kind == 2)
        {
            Partial partial;
            if (!_partials.TryGetValue(sender, out partial)) return;
            partial.buffer.Write(data, 1, data.Length - 1);
            CompletePartial(sender, partial);
        }
    }

    void CompletePartial(int sender, Partial partial)
    {
        if (partial.buffer.Length < partial.total) return;
        _partials.Remove(sender);
        byte[] all = partial.buffer.ToArray();
        ParseMessages(sender, all, 0, all.Length);
    }

    void ParseMessages(int sender, byte[] data, int offset, int count)
    {
        int end = offset + count;
        while (offset + 3 <= end)
        {
            byte type = data[offset];
            int length = data[offset + 1] | (data[offset + 2] << 8);
            offset += 3;
            if (offset + length > end) break;

            byte[] payload = new byte[length];
            Buffer.BlockCopy(data, offset, payload, 0, length);
            offset += length;
            _inbox.Enqueue(new Inbound { sender = sender, type = type, payload = payload });
        }
    }

    void ProcessInbox()
    {
        bool applied = false;
        int budget = 500;
        while (_inbox.Count > 0 && budget-- > 0)
        {
            Inbound message = _inbox.Dequeue();
            try
            {
                if (Dispatch(message)) applied = true;
            }
            catch (Exception e)
            {
                // one control that fails to apply must not stop everything behind it
                Debug.LogWarning("[Multiuser] could not apply message " + message.type + ": " + e);
            }

            if (Busy)
            {
                // that message started a dataset load; the rest waits until the volume is ready
                _loadStartedRemotely = true;
                _wasBusy = true;
                break;
            }
        }

        // whatever just changed came from the other user, so it must not be reported back as a local change
        if (applied && _state == State.Live && !Busy) RebaselineAll();
    }

    bool Dispatch(Inbound message)
    {
        switch (message.type)
        {
            case MsgHello:
                // with two users the other one always answers; with more, the session's master does
                if (_state == State.Live && (_runner.IsSharedModeMasterClient || CountOthers() == 1)) _snapshotRequested = true;
                // the sender is still joining too; remembered for the tie-break in UpdateJoining
                if (_state == State.Joining && (_lowestJoiningPeer < 0 || message.sender < _lowestJoiningPeer)) _lowestJoiningPeer = message.sender;
                return false;

            case MsgSnapshotBegin:
                if (_state == State.Joining && _helloSent) _inSnapshot = true;
                return false;

            case MsgSnapshotEnd:
                if (_state == State.Joining && _inSnapshot) GoLive("received the current state");
                return false;
        }

        // until the state arrives, ordinary traffic is ignored: the state that follows already contains it
        if (_state == State.Joining && !_inSnapshot) return false;

        using (MemoryStream stream = new MemoryStream(message.payload))
        using (BinaryReader reader = new BinaryReader(stream))
        {
            switch (message.type)
            {
                case MsgTransform: _transforms.Apply(reader, _state == State.Joining); break;
                case MsgClick: _ui.ApplyClick(reader); break;
                case MsgSlider: _ui.ApplySlider(reader); break;
                case MsgButtonState: _ui.ApplyButtonState(reader); break;
                case MsgActive: _ui.ApplyActive(reader); break;
                case MsgTf: _tf.Apply(reader); break;
                case MsgAxes: _axes.Apply(reader); break;
                default: return false;
            }
        }
        return true;
    }
}
