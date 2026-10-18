using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Cysharp.Threading.Tasks;
using R3;
using Styly.NetSync;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace STYLY.TIB_Template.TreasureHaul.Prototype
{
    /// <summary>
    /// 試作(S-2/S-3): NetSync の往復時間・送信上限・大きさ・再接続・グローバル変数・DeviceId・持ち主の切り替え・位置の同期を測る。
    /// エディタとクローンの両方で同じシーンを Play する。どちらも「応答役」として RPC に答え、
    /// 計測は <see cref="Run"/> に文字列の命令を渡して始める（エディタ側から。クローンへは RPC で命令を送る）。
    /// 結果は 1 行ずつ「RESULT ...」としてログとファイルに出す。時刻は同じ Mac の UTC 秒なので、2 台の間で直接引き算できる。
    /// </summary>
    public sealed class ProtoNetProbe : MonoBehaviour
    {
        public static ProtoNetProbe Instance { get; private set; }

        [SerializeField] ProtoNetBox _boxA;
        [SerializeField] ProtoNetBox _boxB;
        [Tooltip("空なら Application.persistentDataPath")]
        [SerializeField] string _outputDirectory = "";

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        const string FnHello = "P_Hello", FnPing = "P_Ping", FnPong = "P_Pong", FnSelf = "P_SelfEcho", FnBurst = "P_Burst",
            FnReportReq = "P_ReportReq", FnReportRes = "P_ReportRes", FnBig = "P_Big", FnBigAck = "P_BigAck",
            FnCmd = "P_Cmd", FnResult = "P_Result", FnHandoff = "P_HandoffRes";

        sealed class BurstRecv
        {
            public int Count, OutOfOrder, Dup, LastSeq = -1, MaxSeq = -1;
            public readonly HashSet<int> Seen = new HashSet<int>();
            public readonly List<double> LatMs = new List<double>();
        }

        NetSyncManager _nm;
        bool _isClone;
        string _role;
        int _peerNo;       // エディタから見たクローンの ClientNo
        int _editorNo;     // クローンから見たエディタの ClientNo
        string _logPath;
        bool _busy;

        readonly Dictionary<int, BurstRecv> _burst = new Dictionary<int, BurstRecv>();
        readonly Dictionary<int, string[]> _reports = new Dictionary<int, string[]>();
        readonly Dictionary<int, long> _pingTicks = new Dictionary<int, long>();
        readonly Dictionary<int, double> _pongMs = new Dictionary<int, double>();
        int _selfEchoCount, _selfEchoSent;
        int _bigAckSeq;
        string[] _lastBigAck;
        int _dropLogCount, _ownRejectedCount;
        readonly Dictionary<int, double[]> _handoffRes = new Dictionary<int, double[]>();

        public int PeerNo => _peerNo;

        void Awake()
        {
            Instance = this;
            Application.runInBackground = true;
            if (Application.isBatchMode)
            {
                // バッチモードは待ち無しで回るので既定は60に抑える。環境変数 TH_PROTO_FPS で変えられる（-1 = 抑えない。往復時間のフレーム待ちの切り分け用）
                int fps = 60;
                var fe = Environment.GetEnvironmentVariable("TH_PROTO_FPS");
                if (!string.IsNullOrEmpty(fe) && int.TryParse(fe, out int f)) fps = f;
                Application.targetFrameRate = fps;
            }
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Start()
        {
            var ct = destroyCancellationToken;
            _nm = NetSyncManager.Instance;
            if (_nm == null) { Debug.LogError("[ProtoNetProbe] NetSyncManager がありません"); return; }

            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? "";
            _isClone = File.Exists(Path.Combine(projectRoot, ".clone"));
            _role = _isClone ? "clone" : "editor";
            // 出力先: 環境変数 TH_PROTO_OUT > シーンの設定 > persistentDataPath（シーンには絶対パスを保存しない）
            string dir = Environment.GetEnvironmentVariable("TH_PROTO_OUT");
            if (string.IsNullOrEmpty(dir)) dir = string.IsNullOrEmpty(_outputDirectory) ? Application.persistentDataPath : _outputDirectory;
            Directory.CreateDirectory(dir);
            _logPath = Path.Combine(dir, $"net_{_role}.log");
            Log($"START role={_role} dataPath={Application.dataPath} fixedDelta={Time.fixedDeltaTime} targetFps={Application.targetFrameRate} vSync={QualitySettings.vSyncCount}");

            // NetSync の UnityEvent は R3 で包んで購読する
            _nm.OnRPCReceived.AsObservable().Subscribe(t => OnRpc(t.Arg0, t.Arg1, t.Arg2)).AddTo(this);
            _nm.OnReady.AsObservable().Subscribe(_ => Log($"EVENT OnReady clientNo={_nm.ClientNo}")).AddTo(this);
            _nm.OnAvatarConnected.AsObservable().Subscribe(n => Log($"EVENT OnAvatarConnected {n}")).AddTo(this);
            _nm.OnAvatarDisconnected.AsObservable().Subscribe(n => Log($"EVENT OnAvatarDisconnected {n}")).AddTo(this);
            _nm.OnGlobalVariableChanged.AsObservable().Subscribe(t => OnGlobalChanged(t.Arg0, t.Arg1, t.Arg2)).AddTo(this);
            Observable.FromEvent<Application.LogCallback, (string msg, string stack, LogType type)>(
                    h => (msg, stack, type) => h((msg, stack, type)),
                    h => Application.logMessageReceived += h,
                    h => Application.logMessageReceived -= h)
                .Subscribe(l =>
                {
                    if (l.msg.StartsWith("[ProtoNet]")) return;   // 自分のログは数えない（自分の Log が自分を呼ぶのを防ぐ）
                    if (l.msg.Contains("Rate limited: dropped")) _dropLogCount++;
                    if (l.msg.Contains("Ownership rejected")) { _ownRejectedCount++; Log("EVENT " + l.msg); }
                }).AddTo(this);
            foreach (var box in new[] { _boxA, _boxB })
            {
                var b = box;
                b.Net.OnOwnershipChanged.AsObservable().Subscribe(t => Log($"EVENT ownership box={b.Label} {t.Arg1}->{t.Arg0}")).AddTo(this);
            }

            WatchAsync(ct).Forget();
        }

        // 接続状態の変化を、時刻つきで記録する（再接続の所要時間を後から計算できる）
        async UniTaskVoid WatchAsync(System.Threading.CancellationToken ct)
        {
            string last = null;
            bool wasReady = false;
            while (!ct.IsCancellationRequested)
            {
                string alive = _nm.HasHandshake ? string.Join("/", _nm.GetAliveClients(true)) : "-";
                string s = $"conn={_nm.HasServerConnection} handshake={_nm.HasHandshake} gvSync={_nm.HasNetworkVariablesSync} ready={_nm.IsReady} clientNo={_nm.ClientNo} alive={alive} server='{_nm.ServerAddress}'";
                if (s != last) { Log("STATE " + s); last = s; }
                if (_nm.IsReady && !wasReady)
                {
                    wasReady = true;
                    OnBecameReady();
                }
                else if (!_nm.IsReady) wasReady = false;
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
        }

        void OnBecameReady()
        {
            Log($"READY clientNo={_nm.ClientNo} deviceId={_nm.DeviceId} room={_nm.RoomId} version={_nm.GetVersion()}");
            LogGlobals("onReady");
            _nm.Rpc(FnHello, new[] { _role, "new" });
        }

        void LogGlobals(string tag)
        {
            var all = _nm.GetAllGlobalVariables();
            Log($"GLOBALS {tag} count={all?.Count ?? -1} " + (all == null ? "" : string.Join(" ", all.Select(kv => $"{kv.Key}(len={kv.Value?.Length})"))));
        }

        // ---------------------------------------------------------------- ログ

        void Log(string s)
        {
            string line = $"{ProtoNetBox.UtcNow().ToString("F3", Inv)} [{_role}] {s}";
            Debug.Log("[ProtoNet] " + line);
            if (!string.IsNullOrEmpty(_logPath)) { try { File.AppendAllText(_logPath, line + "\n"); } catch { } }
        }

        void Result(string s)
        {
            Log("RESULT " + s);
            if (_isClone && _editorNo > 0 && _nm.IsReady) _nm.Rpc(FnResult, new[] { s }, _editorNo);
        }

        static string F(double v) => v.ToString("R", Inv);
        static double D(string s) => double.Parse(s, Inv);

        // ---------------------------------------------------------------- 受信

        void OnRpc(int sender, string fn, string[] a)
        {
            try
            {
                switch (fn)
                {
                    case FnHello:
                        if (sender != _nm.ClientNo)
                        {
                            if (a[0] == "clone") _peerNo = sender; else _editorNo = sender;
                            if (a.Length > 1 && a[1] == "new") _nm.Rpc(FnHello, new[] { _role, "reply" }, sender);
                            Log($"HELLO from={sender} role={a[0]} kind={(a.Length > 1 ? a[1] : "")}");
                        }
                        break;
                    case FnPing: _nm.Rpc(FnPong, new[] { a[0] }, sender); break;
                    case FnPong:
                    {
                        int i = int.Parse(a[0]);
                        if (_pingTicks.TryGetValue(i, out long t0)) _pongMs[i] = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
                        break;
                    }
                    case FnSelf:
                        if (sender == _nm.ClientNo) _selfEchoCount++;
                        else Log($"SELFECHO received from={sender} id={a[0]}");
                        break;
                    case FnBurst:
                    {
                        int id = int.Parse(a[0]); int seq = int.Parse(a[1]); double sendUtc = D(a[2]);
                        if (!_burst.TryGetValue(id, out var r)) _burst[id] = r = new BurstRecv();
                        r.Count++;
                        if (!r.Seen.Add(seq)) r.Dup++;
                        if (seq < r.LastSeq) r.OutOfOrder++;
                        r.LastSeq = seq; if (seq > r.MaxSeq) r.MaxSeq = seq;
                        r.LatMs.Add((ProtoNetBox.UtcNow() - sendUtc) * 1000.0);
                        break;
                    }
                    case FnReportReq:
                    {
                        int id = int.Parse(a[0]);
                        _burst.TryGetValue(id, out var r);
                        r ??= new BurstRecv();
                        _nm.Rpc(FnReportRes, new[] { a[0], r.Count.ToString(), r.OutOfOrder.ToString(), r.MaxSeq.ToString(), r.Dup.ToString(),
                            F(ProtoNetStats.Percentile(r.LatMs, 0.5)), F(ProtoNetStats.Percentile(r.LatMs, 0.95)), F(r.LatMs.Count > 0 ? r.LatMs.Max() : double.NaN) }, sender);
                        break;
                    }
                    case FnReportRes: _reports[int.Parse(a[0])] = a; break;
                    case FnBig:
                        _nm.Rpc(FnBigAck, new[] { a.Length.ToString(), (a.Length > 0 ? a[0].Length : 0).ToString(), (a.Length > 0 ? ProtoNetStats.Fnv1a(a[0]) : 0u).ToString() }, sender);
                        break;
                    case FnBigAck: _lastBigAck = a; _bigAckSeq++; break;
                    case FnCmd:
                        Log($"CMD from={sender} '{a[0]}'");
                        Run(a[0]);
                        break;
                    case FnResult: Log($"PEER-RESULT {a[0]}"); break;
                    case FnHandoff: _handoffRes[int.Parse(a[0])] = new[] { D(a[1]), D(a[2]) }; break;
                }
            }
            catch (Exception e) { Debug.LogError($"[ProtoNetProbe] OnRpc {fn}: {e}"); }
        }

        void OnGlobalChanged(string name, string oldV, string newV)
        {
            if (name == "th_gv_t" && double.TryParse(newV, NumberStyles.Float, Inv, out double t))
                Log($"GVCHANGED name={name} latencyMs={((ProtoNetBox.UtcNow() - t) * 1000.0).ToString("F1", Inv)}");
            else
                Log($"GVCHANGED name={name} oldLen={oldV?.Length ?? -1} newLen={newV?.Length ?? -1}");
        }

        // ---------------------------------------------------------------- 命令

        /// <summary>命令を1行で受けて始める。戻りは待たない（結果はログ・ファイルへ）。</summary>
        public void Run(string line)
        {
            RunAsync(line).Forget();
        }

        void SendCmd(string line)
        {
            if (_peerNo == 0) { Log("SENDCMD no peer yet: " + line); return; }
            _nm.Rpc(FnCmd, new[] { line }, _peerNo);
        }

        async UniTaskVoid RunAsync(string line)
        {
            var ct = destroyCancellationToken;
            var t = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (t.Length == 0) return;
            string Arg(int i, string d) => i < t.Length ? t[i] : d;
            try
            {
                switch (t[0])
                {
                    case "info": Info(); break;
                    case "globals": LogGlobals("manual"); break;
                    case "rtt": await RttAsync(int.Parse(Arg(1, "100")), int.Parse(Arg(2, "50")), ct); break;
                    case "selfecho": await SelfEchoAsync(ct); break;
                    case "rate": await RateAsync(int.Parse(Arg(1, "20")), float.Parse(Arg(2, "5"), Inv), int.Parse(Arg(3, "30")), ct); break;
                    case "size": await SizeAsync(Arg(1, "items,4096,16384,32768,60000,65000,65531,65532,65536,70000"), ct); break;
                    case "gvset": GvSet(Arg(1, "th_gv"), int.Parse(Arg(2, "10"))); break;
                    case "gvget": Result($"gvget name={Arg(1, "th_gv")} len={_nm.GetGlobalVariable(Arg(1, "th_gv"), null)?.Length ?? -1}"); break;
                    case "gvtime": _nm.SetGlobalVariable("th_gv_t", F(ProtoNetBox.UtcNow())); break;
                    case "sendrate": SetSendRate(float.Parse(Arg(1, "10"), Inv)); break;
                    case "reset": ResetBoxes(); break;
                    case "shot": Shot(Arg(1, "shot")); break;
                    case "own": await EnsureOwnAsync(Box(Arg(1, "A")), 3f, ct); break;
                    case "release": Box(Arg(1, "A")).Net.ReleaseOwnership(); break;
                    case "d1": await D1Async(float.Parse(Arg(1, "10"), Inv), float.Parse(Arg(2, "30"), Inv), ct); break;
                    case "d1run": await D1RunAsync(D(Arg(1, "0")), float.Parse(Arg(2, "30"), Inv), ct); break;
                    case "d2": await D2Async(ct); break;
                    case "steal": await StealAsync(ct); break;
                    case "handoff_take": await HandoffTakeAsync(int.Parse(Arg(1, "0")), ct); break;
                    case "race": await RaceAsync(int.Parse(Arg(1, "0")), D(Arg(2, "0")), ct); break;
                    case "d3": await D3Async(Arg(1, "head"), float.Parse(Arg(2, "10"), Inv), ct); break;
                    case "d3run": await D3RunAsync(Arg(1, "head"), D(Arg(2, "0")), float.Parse(Arg(3, "10"), Inv), ct); break;
                    default: Log("unknown command: " + line); break;
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { Debug.LogError($"[ProtoNetProbe] {line}: {e}"); Log($"ERROR {line}: {e.Message}"); }
        }

        /// <summary>メインカメラを RenderTexture に描いて PNG にする（バッチモードのクローンは Game View がないため）。</summary>
        void Shot(string name)
        {
            var cam = Camera.main;
            if (cam == null) { Log("SHOT no camera"); return; }
            var safe = new StringBuilder();
            foreach (char ch in name ?? "") safe.Append(char.IsLetterOrDigit(ch) && ch < 128 || ch == '_' || ch == '-' ? ch : '_');
            string dir = Path.GetDirectoryName(_logPath);
            string file = Path.Combine(dir, $"{safe}_{_role}.png");
            var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
            var prev = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            var old = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = old;
            cam.targetTexture = prev;
            File.WriteAllBytes(file, tex.EncodeToPNG());
            Destroy(tex); rt.Release(); Destroy(rt);
            Log($"SHOT saved {file}");
        }

        ProtoNetBox Box(string label) => label == "B" ? _boxB : _boxA;

        void Info()
        {
            Log($"INFO clientNo={_nm.ClientNo} deviceId='{_nm.DeviceId}' room={_nm.RoomId} offline={_nm.IsOfflineMode} peer={_peerNo} editor={_editorNo} " +
                $"alive=[{string.Join(",", _nm.GetAliveClients(true))}] deviceIdOfPeer='{(_peerNo > 0 ? _nm.GetDeviceIdByClientNo(_peerNo) : "")}' " +
                $"A.id={_boxA.Net.ObjectId} A.owner={_boxA.Net.OwnerClientNo} B.id={_boxB.Net.ObjectId} B.owner={_boxB.Net.OwnerClientNo} fps={(1f / Mathf.Max(Time.unscaledDeltaTime, 1e-4f)).ToString("F0", Inv)} sendRate={_nm.TransformSendRate}");
        }

        // ---------------------------------------------------------------- C: 往復時間

        async UniTask RttAsync(int n, int intervalMs, System.Threading.CancellationToken ct)
        {
            int peer = _isClone ? _editorNo : _peerNo;
            if (peer == 0) { Log("RTT no peer"); return; }
            _pingTicks.Clear(); _pongMs.Clear();
            int others = _nm.GetAliveClients(true).Length - 1;
            for (int i = 0; i < n; i++)
            {
                _pingTicks[i] = Stopwatch.GetTimestamp();
                _nm.Rpc(FnPing, new[] { i.ToString() }, peer);
                await UniTask.Delay(intervalMs, DelayType.Realtime, PlayerLoopTiming.Update, ct);
            }
            await UniTask.Delay(1500, DelayType.Realtime, PlayerLoopTiming.Update, ct);
            var v = _pongMs.Values.ToList();
            Result($"RTT n={n} got={v.Count} intervalMs={intervalMs} aliveOthers={others} medianMs={F2(ProtoNetStats.Percentile(v, 0.5))} p95Ms={F2(ProtoNetStats.Percentile(v, 0.95))} maxMs={F2(v.Count > 0 ? v.Max() : double.NaN)} minMs={F2(v.Count > 0 ? v.Min() : double.NaN)} meanMs={F2(ProtoNetStats.Mean(v))} fps={(1f / Mathf.Max(Time.unscaledDeltaTime, 1e-4f)).ToString("F0", Inv)}");
        }

        static string F2(double v) => double.IsNaN(v) ? "NaN" : v.ToString("F2", Inv);

        async UniTask SelfEchoAsync(System.Threading.CancellationToken ct)
        {
            _selfEchoCount = 0; _selfEchoSent = 5;
            for (int i = 0; i < 5; i++) { _nm.Rpc(FnSelf, new[] { i.ToString() }); await UniTask.Delay(100, DelayType.Realtime, PlayerLoopTiming.Update, ct); }
            await UniTask.Delay(1000, DelayType.Realtime, PlayerLoopTiming.Update, ct);
            Result($"SELFECHO sent={_selfEchoSent} receivedOwn={_selfEchoCount} (broadcast RPC, 自分に返ってくるか)");
        }

        // ---------------------------------------------------------------- C: 送信の上限

        int _burstId;
        (int limit, double window, double cooldown) _rpcLimit = (30, 1.0, 0.5);   // NetSync の既定。自分で変えた分を覚えておき、戻す

        void SetRpcLimit((int limit, double window, double cooldown) v)
        {
            _rpcLimit = v;
            _nm.ConfigureRpcLimit(v.limit, v.window, v.cooldown);
        }

        async UniTask RateAsync(int rate, float secs, int limit, System.Threading.CancellationToken ct)
        {
            int peer = _isClone ? _editorNo : _peerNo;
            if (peer == 0) { Log("RATE no peer"); return; }
            int id = ++_burstId + (int)(ProtoNetBox.UtcNow() % 100000) * 100;
            var prevLimit = _rpcLimit;
            int dropBefore = _dropLogCount;
            int total = Mathf.RoundToInt(rate * secs);
            var sw = Stopwatch.StartNew();
            int sent = 0;
            double sendSecs;
            int dropLogs;
            try
            {
                SetRpcLimit((limit, 1.0, 0.0));   // 警告の間引き(0.5秒)を切って、落とした数を1件ずつ数える
                while (sent < total)
                {
                    int due = Mathf.Min(total, (int)(sw.Elapsed.TotalSeconds * rate) + 1);
                    while (sent < due) { _nm.Rpc(FnBurst, new[] { id.ToString(), sent.ToString(), F(ProtoNetBox.UtcNow()) }, peer); sent++; }
                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                }
                sendSecs = sw.Elapsed.TotalSeconds;
                await UniTask.Delay(1500, DelayType.Realtime, PlayerLoopTiming.Update, ct);
                dropLogs = _dropLogCount - dropBefore;
            }
            finally
            {
                SetRpcLimit(prevLimit);   // 例外・キャンセルでも、変更前の値に戻す
            }
            _reports.Remove(id);
            _nm.Rpc(FnReportReq, new[] { id.ToString() }, peer);
            double t0 = Time.realtimeSinceStartupAsDouble;
            while (!_reports.ContainsKey(id) && Time.realtimeSinceStartupAsDouble - t0 < 3.0) await UniTask.Yield(PlayerLoopTiming.Update, ct);
            if (!_reports.TryGetValue(id, out var r)) { Result($"RATE rate={rate} limit={limit} sent={total} NO REPORT (peer did not answer)"); return; }
            int got = int.Parse(r[1]);
            Result($"RATE rate={rate}/s limit={limit}/s secs={secs} attemptedToSend={total} sendSecs={sendSecs.ToString("F2", Inv)} dropLogs={dropLogs} arrived={got} lost={total - got} outOfOrder={r[2]} maxSeq={r[3]} dup={r[4]} oneWayP50Ms={F2(D(r[5]))} oneWayP95Ms={F2(D(r[6]))} oneWayMaxMs={F2(D(r[7]))}");
        }

        // ---------------------------------------------------------------- C: 大きさ

        static string BuildItems(int count)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < count; i++)
            {
                if (i > 0) sb.Append(';');
                // id|状態|持ち主|世代|現在の価値|ぶつけた回数|値段公開|回収/消滅|x|y|z
                sb.Append(i).Append("|Held|").Append(3).Append('|').Append(12 + i).Append('|').Append(1480 - i * 7).Append('|').Append(i % 4).Append("|1|0|")
                  .Append((-3.214f + i).ToString("F3", Inv)).Append('|').Append(0.523f.ToString("F3", Inv)).Append('|').Append((7.891f - i * 0.1f).ToString("F3", Inv));
            }
            return sb.ToString();
        }

        static string BuildPayload(int len)
        {
            var c = new char[len];
            for (int i = 0; i < len; i++) c[i] = (char)('a' + (i * 7 + i / 26) % 26);
            return new string(c);
        }

        async UniTask SizeAsync(string spec, System.Threading.CancellationToken ct)
        {
            int peer = _isClone ? _editorNo : _peerNo;
            if (peer == 0) { Log("SIZE no peer"); return; }
            foreach (var tok in spec.Split(','))
            {
                string payload = tok == "items" ? BuildItems(23) : BuildPayload(int.Parse(tok));
                uint hash = ProtoNetStats.Fnv1a(payload);
                int seqBefore = _bigAckSeq;
                var sw = Stopwatch.StartNew();
                _nm.Rpc(FnBig, new[] { payload }, peer);
                while (_bigAckSeq == seqBefore && sw.Elapsed.TotalSeconds < 4.0) await UniTask.Yield(PlayerLoopTiming.Update, ct);
                string verdict;
                if (_bigAckSeq == seqBefore) verdict = "NO_ACK";
                else
                {
                    var a = _lastBigAck;
                    verdict = (a[1] == payload.Length.ToString() && a[2] == hash.ToString()) ? "OK" : $"MISMATCH(recvArgs={a[0]} recvLen={a[1]})";
                }
                int jsonBytes = Encoding.UTF8.GetByteCount(payload) + 4;  // ["..."]
                Result($"SIZE label={tok} chars={payload.Length} approxJsonBytes={jsonBytes} verdict={verdict} ms={sw.Elapsed.TotalMilliseconds.ToString("F1", Inv)}");
                await UniTask.Delay(1500, DelayType.Realtime, PlayerLoopTiming.Update, ct);
            }
            Log($"SIZE done; alive={string.Join("/", _nm.GetAliveClients(true))} conn={_nm.HasServerConnection}");
        }

        // ---------------------------------------------------------------- C: グローバル変数

        void GvSet(string name, int len)
        {
            string v = BuildPayload(len);
            bool ok = _nm.SetGlobalVariable(name, v);
            string back = _nm.GetGlobalVariable(name, null);
            Result($"GVSET name={name} requestedLen={len} returned={ok} readBackLen={back?.Length ?? -1} (64文字名/1024文字値の上限は BinarySerializer)");
        }

        // ---------------------------------------------------------------- S-2: 共通

        void SetSendRate(float r)
        {
            _nm.TransformSendRate = r;
            // 受け側の補間は NetSyncObject が有効になった時の送信頻度で初期化されるので、入れ直す
            foreach (var b in new[] { _boxA, _boxB }) { b.Net.enabled = false; b.Net.enabled = true; }
            Log($"SENDRATE set {_nm.TransformSendRate}");
        }

        void ResetBoxes()
        {
            foreach (var b in new[] { _boxA, _boxB })
            {
                b.StopKind();
                if (b.Net.IsOwnedByMe) b.Net.ReleaseOwnership();
                b.TeleportLocal(b.RestPosition);
            }
            Log("RESET boxes");
        }

        async UniTask<bool> EnsureOwnAsync(ProtoNetBox box, float timeout, System.Threading.CancellationToken ct)
        {
            if (box.Net.IsOwnedByMe) return true;
            double t0 = Time.realtimeSinceStartupAsDouble, lastReq = -10;
            while (Time.realtimeSinceStartupAsDouble - t0 < timeout)
            {
                if (box.Net.IsOwnedByMe) { Log($"OWN box={box.Label} got in {((Time.realtimeSinceStartupAsDouble - t0) * 1000).ToString("F0", Inv)}ms"); return true; }
                if (Time.realtimeSinceStartupAsDouble - lastReq > 1.0) { box.Net.RequestOwnership(); lastReq = Time.realtimeSinceStartupAsDouble; }
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
            Log($"OWN box={box.Label} FAILED owner={box.Net.OwnerClientNo}");
            return false;
        }

        async UniTask WaitUtcAsync(double utc, System.Threading.CancellationToken ct)
        {
            while (ProtoNetBox.UtcNow() < utc) await UniTask.Yield(PlayerLoopTiming.Update, ct);
        }

        ProtoNetBox MineBox() => _boxA.Net.IsOwnedByMe ? _boxA : (_boxB.Net.IsOwnedByMe ? _boxB : null);

        // ---------------------------------------------------------------- D1: 位置の遅れ・なめらかさ

        async UniTask D1Async(float rate, float secs, System.Threading.CancellationToken ct)
        {
            if (_peerNo == 0) { Log("D1 no peer"); return; }
            if (secs <= SettleSecs) { Log($"ERROR D1 secs={secs} は立ち上がり除外({SettleSecs}秒)以下。もっと長く指定してください"); return; }
            SendCmd("reset"); ResetBoxes();
            await UniTask.Delay(800, DelayType.Realtime, PlayerLoopTiming.Update, ct);
            SendCmd($"sendrate {F(rate)}"); SetSendRate(rate);
            await UniTask.Delay(500, DelayType.Realtime, PlayerLoopTiming.Update, ct);
            await EnsureOwnAsync(_boxA, 3f, ct);
            SendCmd("own B");
            await UniTask.Delay(1500, DelayType.Realtime, PlayerLoopTiming.Update, ct);
            double u0 = ProtoNetBox.UtcNow() + 1.5;
            SendCmd($"d1run {F(u0)} {F(secs)}");
            await D1RunAsync(u0, secs, ct);
        }

        const double SettleSecs = 8.0;

        static double LagAt(IReadOnlyList<(double u, Vector3 p)> s, double u0, double t, Vector3 center)
        {
            for (int i = 0; i < s.Count; i++)
            {
                if (s[i].u - u0 < t) continue;
                double ph = Math.Atan2(s[i].p.z - center.z, s[i].p.x - center.x); if (ph < 0) ph += 2 * Math.PI;
                return ProtoNetStats.LagSeconds(ProtoNetStats.ExpectedPhase(s[i].u, ProtoNetBox.CirclePeriod), ph, ProtoNetBox.CirclePeriod) * 1000.0;
            }
            return double.NaN;
        }

        async UniTask D1RunAsync(double u0, float secs, System.Threading.CancellationToken ct)
        {
            if (secs <= SettleSecs) { Log($"ERROR D1RUN secs={secs} は立ち上がり除外({SettleSecs}秒)以下"); return; }
            var mine = MineBox();
            if (mine == null) { Log("D1RUN not owner of any box"); return; }
            var other = mine == _boxA ? _boxB : _boxA;
            await WaitUtcAsync(u0, ct);
            mine.StartKind(ProtoNetBox.Kind.Circle, false, u0);
            other.BeginRecord();
            await UniTask.Delay(TimeSpan.FromSeconds(secs), DelayType.Realtime, PlayerLoopTiming.Update, ct);
            mine.StopKind();
            other.EndRecord();

            var s = other.Samples;
            var center = ProtoNetBox.CircleCenter(other.Index);
            var lag = new List<double>(); var spd = new List<double>(); double maxStall = 0, stallStart = -1; int stalls = 0; var dts = new List<double>();
            int skip = 0;
            var csv = new StringBuilder("t,lagMs,x,z\n");
            for (int i = 0; i < s.Count; i++)
            {
                double obsPhase = Math.Atan2(s[i].p.z - center.z, s[i].p.x - center.x);
                if (obsPhase < 0) obsPhase += 2 * Math.PI;
                double lagMs = ProtoNetStats.LagSeconds(ProtoNetStats.ExpectedPhase(s[i].u, ProtoNetBox.CirclePeriod), obsPhase, ProtoNetBox.CirclePeriod) * 1000.0;
                csv.Append((s[i].u - u0).ToString("F4", Inv)).Append(',').Append(lagMs.ToString("F1", Inv)).Append(',').Append(s[i].p.x.ToString("F4", Inv)).Append(',').Append(s[i].p.z.ToString("F4", Inv)).Append('\n');
                if (s[i].u < u0 + SettleSecs) { skip = i + 1; continue; }   // 立ち上がり（補間が落ち着くまでの数秒）は除く。csv には全部残る
                lag.Add(lagMs);
            }
            try { File.WriteAllText(Path.Combine(Path.GetDirectoryName(_logPath), $"d1_{_role}_rate{_nm.TransformSendRate:F0}.csv"), csv.ToString()); } catch { }
            for (int i = Math.Max(1, skip); i < s.Count; i++)
            {
                double dt = s[i].u - s[i - 1].u; if (dt <= 0) continue;
                double d = Vector3.Distance(s[i].p, s[i - 1].p);
                dts.Add(dt * 1000.0);
                spd.Add(d / dt);
                if (d < 1e-5) { stalls++; if (stallStart < 0) stallStart = s[i - 1].u; maxStall = Math.Max(maxStall, s[i].u - stallStart); }
                else stallStart = -1;
            }
            double meanSpd = ProtoNetStats.Mean(spd), cv = ProtoNetStats.StdDev(spd) / meanSpd;
            double expectSpd = 2 * Math.PI * ProtoNetBox.CircleRadius / ProtoNetBox.CirclePeriod;
            Result($"D1 viewing={other.Label} sendRate={_nm.TransformSendRate} secs={secs} frames={lag.Count} fps={F2(lag.Count / Math.Max(1e-3, secs - SettleSecs))} settleSecs={SettleSecs} lagAt1s={F2(LagAt(s, u0, 1.0, center))} lagAt3s={F2(LagAt(s, u0, 3.0, center))} lagAt5s={F2(LagAt(s, u0, 5.0, center))} " +
                   $"lagMeanMs={F2(ProtoNetStats.Mean(lag))} lagP50Ms={F2(ProtoNetStats.Percentile(lag, 0.5))} lagP95Ms={F2(ProtoNetStats.Percentile(lag, 0.95))} lagMaxMs={F2(lag.Count > 0 ? lag.Max() : double.NaN)} lagMinMs={F2(lag.Count > 0 ? lag.Min() : double.NaN)} lagStdMs={F2(ProtoNetStats.StdDev(lag))} " +
                   $"speedMean={F2(meanSpd)}(expect {F2(expectSpd)}) speedCV={F2(cv)} speedMax={F2(spd.Count > 0 ? spd.Max() : double.NaN)} noMoveFrames={stalls} longestNoMoveMs={F2(maxStall * 1000)} frameDtP99Ms={F2(ProtoNetStats.Percentile(dts, 0.99))}");
        }

        // ---------------------------------------------------------------- D2: 持ち主の切り替え

        async UniTask D2Async(System.Threading.CancellationToken ct)
        {
            if (_peerNo == 0) { Log("D2 no peer"); return; }
            SendCmd("reset"); ResetBoxes();
            await UniTask.Delay(1500, DelayType.Realtime, PlayerLoopTiming.Update, ct);

            // 1) 要求 → 持ち主になるまで / 解放 → 持ち主なしになるまで（自分の画面で測る）
            var acq = new List<double>(); var rel = new List<double>();
            for (int i = 0; i < 10; i++)
            {
                var sw = Stopwatch.StartNew();
                _boxA.Net.RequestOwnership();
                while (!_boxA.Net.IsOwnedByMe && sw.Elapsed.TotalSeconds < 3) await UniTask.Yield(PlayerLoopTiming.Update, ct);
                acq.Add(_boxA.Net.IsOwnedByMe ? sw.Elapsed.TotalMilliseconds : double.NaN);
                await UniTask.Delay(300, DelayType.Realtime, PlayerLoopTiming.Update, ct);
                sw.Restart();
                _boxA.Net.ReleaseOwnership();
                while (_boxA.Net.OwnerClientNo != 0 && sw.Elapsed.TotalSeconds < 3) await UniTask.Yield(PlayerLoopTiming.Update, ct);
                rel.Add(_boxA.Net.OwnerClientNo == 0 ? sw.Elapsed.TotalMilliseconds : double.NaN);
                await UniTask.Delay(300, DelayType.Realtime, PlayerLoopTiming.Update, ct);
            }
            var a2 = acq.Where(x => !double.IsNaN(x)).ToList(); var r2 = rel.Where(x => !double.IsNaN(x)).ToList();
            Result($"D2-acquire n=10 ok={a2.Count} requestToOwnerMs median={F2(ProtoNetStats.Percentile(a2, 0.5))} max={F2(a2.Count > 0 ? a2.Max() : double.NaN)} min={F2(a2.Count > 0 ? a2.Min() : double.NaN)} | releaseToFreeMs ok={r2.Count} median={F2(ProtoNetStats.Percentile(r2, 0.5))} max={F2(r2.Count > 0 ? r2.Max() : double.NaN)} min={F2(r2.Count > 0 ? r2.Min() : double.NaN)}");

            // 2) 他人が持っている物を要求したらどうなるか
            await EnsureOwnAsync(_boxA, 3f, ct);
            int rejBefore = _ownRejectedCount;
            SendCmd("steal");
            await UniTask.Delay(3000, DelayType.Realtime, PlayerLoopTiming.Update, ct);
            Result($"D2-steal editorOwnerAfter={_boxA.Net.OwnerClientNo} (editor={_nm.ClientNo}, peer={_peerNo}) editorSawRejected={_ownRejectedCount - rejBefore}");
            _boxA.Net.ReleaseOwnership();
            await UniTask.Delay(800, DelayType.Realtime, PlayerLoopTiming.Update, ct);

            // 3) 解放 → 相手が要求（承認後の持ち主切り替えに近い流れ）
            var seenFree = new List<double>(); var owned = new List<double>();
            for (int i = 0; i < 10; i++)
            {
                await EnsureOwnAsync(_boxA, 3f, ct);
                SendCmd($"handoff_take {i}");
                await UniTask.Delay(500, DelayType.Realtime, PlayerLoopTiming.Update, ct);
                double tRel = ProtoNetBox.UtcNow();
                _boxA.Net.ReleaseOwnership();
                double w0 = Time.realtimeSinceStartupAsDouble;
                while (!_handoffRes.ContainsKey(i) && Time.realtimeSinceStartupAsDouble - w0 < 4) await UniTask.Yield(PlayerLoopTiming.Update, ct);
                if (_handoffRes.TryGetValue(i, out var hr))
                {
                    seenFree.Add((hr[0] - tRel) * 1000.0); owned.Add((hr[1] - tRel) * 1000.0);
                    Log($"HANDOFF i={i} releaseCall->peerSawFree={((hr[0] - tRel) * 1000).ToString("F0", Inv)}ms releaseCall->peerOwner={((hr[1] - tRel) * 1000).ToString("F0", Inv)}ms");
                }
                else Log($"HANDOFF i={i} no result");
                await UniTask.Delay(1500, DelayType.Realtime, PlayerLoopTiming.Update, ct);
                if (_boxA.Net.OwnerClientNo != 0) await UniTask.Delay(1500, DelayType.Realtime, PlayerLoopTiming.Update, ct);
            }
            Result($"D2-handoff n=10 ok={owned.Count} releaseToPeerSawFreeMs median={F2(ProtoNetStats.Percentile(seenFree, 0.5))} max={F2(seenFree.Count > 0 ? seenFree.Max() : double.NaN)} | releaseToPeerOwnerMs median={F2(ProtoNetStats.Percentile(owned, 0.5))} max={F2(owned.Count > 0 ? owned.Max() : double.NaN)} min={F2(owned.Count > 0 ? owned.Min() : double.NaN)}");

            // 4) 2台が同時に要求
            int editorWins = 0, peerWins = 0, none = 0, split = 0;
            for (int i = 0; i < 10; i++)
            {
                ResetOwnershipLocalWait();
                await UniTask.Delay(800, DelayType.Realtime, PlayerLoopTiming.Update, ct);
                double u0 = ProtoNetBox.UtcNow() + 1.0;
                SendCmd($"race {i} {F(u0)}");
                await RaceAsync(i, u0, ct);
                int mineOwner = _boxA.Net.OwnerClientNo;
                string peerView = _handoffRes.TryGetValue(1000 + i, out var pv) ? ((int)pv[0]).ToString() : "?";
                if (mineOwner == _nm.ClientNo) editorWins++; else if (mineOwner == _peerNo) peerWins++; else none++;
                if (peerView != "?" && peerView != mineOwner.ToString()) split++;
                Log($"RACE-SUMMARY i={i} editorView={mineOwner} peerView={peerView}");
                if (_boxA.Net.IsOwnedByMe) _boxA.Net.ReleaseOwnership();
                await UniTask.Delay(1200, DelayType.Realtime, PlayerLoopTiming.Update, ct);
            }
            Result($"D2-race n=10 editorWins={editorWins} peerWins={peerWins} nobody={none} viewsDisagree={split}");
        }

        void ResetOwnershipLocalWait()
        {
            if (_boxA.Net.IsOwnedByMe) _boxA.Net.ReleaseOwnership();
        }

        async UniTask StealAsync(System.Threading.CancellationToken ct)
        {
            int rej = _ownRejectedCount;
            double t0 = ProtoNetBox.UtcNow();
            _boxA.Net.RequestOwnership();
            var owners = new List<string>();
            for (int i = 0; i < 6; i++)
            {
                await UniTask.Delay(500, DelayType.Realtime, PlayerLoopTiming.Update, ct);
                owners.Add(_boxA.Net.OwnerClientNo.ToString());
            }
            Result($"D2-steal(peer) requestWhileOwnedByEditor ownerEvery500ms=[{string.Join(",", owners)}] mine={_boxA.Net.IsOwnedByMe} rejectedWarnings={_ownRejectedCount - rej}");
            if (_boxA.Net.IsOwnedByMe) _boxA.Net.ReleaseOwnership();
        }

        async UniTask HandoffTakeAsync(int i, System.Threading.CancellationToken ct)
        {
            double w0 = Time.realtimeSinceStartupAsDouble;
            // 持ち主が付いている間は待ち、持ち主なし(0)になった瞬間に要求する
            while (_boxA.Net.OwnerClientNo == 0 && Time.realtimeSinceStartupAsDouble - w0 < 3) await UniTask.Yield(PlayerLoopTiming.Update, ct);
            while (_boxA.Net.OwnerClientNo != 0 && Time.realtimeSinceStartupAsDouble - w0 < 10) await UniTask.Yield(PlayerLoopTiming.Update, ct);
            double seen = ProtoNetBox.UtcNow();
            _boxA.Net.RequestOwnership();
            while (!_boxA.Net.IsOwnedByMe && Time.realtimeSinceStartupAsDouble - w0 < 12) await UniTask.Yield(PlayerLoopTiming.Update, ct);
            double owned = ProtoNetBox.UtcNow();
            if (_editorNo > 0) _nm.Rpc(FnHandoff, new[] { i.ToString(), F(seen), F(owned) }, _editorNo);
            await UniTask.Delay(800, DelayType.Realtime, PlayerLoopTiming.Update, ct);
            if (_boxA.Net.IsOwnedByMe) _boxA.Net.ReleaseOwnership();
        }

        async UniTask RaceAsync(int i, double u0, System.Threading.CancellationToken ct)
        {
            await WaitUtcAsync(u0, ct);
            _boxA.Net.RequestOwnership();
            await UniTask.Delay(1500, DelayType.Realtime, PlayerLoopTiming.Update, ct);
            if (_isClone && _editorNo > 0) _nm.Rpc(FnHandoff, new[] { (1000 + i).ToString(), F(_boxA.Net.OwnerClientNo), "0" }, _editorNo);
            Log($"RACE i={i} finalOwner={_boxA.Net.OwnerClientNo} mine={_boxA.Net.IsOwnedByMe}");
        }

        // ---------------------------------------------------------------- D3: 持っている箱どうしの衝突

        async UniTask D3Async(string kind, float secs, System.Threading.CancellationToken ct)
        {
            if (_peerNo == 0) { Log("D3 no peer"); return; }
            SendCmd("reset"); ResetBoxes();
            await UniTask.Delay(1200, DelayType.Realtime, PlayerLoopTiming.Update, ct);
            await EnsureOwnAsync(_boxA, 3f, ct);
            SendCmd("own B");
            await UniTask.Delay(1500, DelayType.Realtime, PlayerLoopTiming.Update, ct);
            double u0 = ProtoNetBox.UtcNow() + 1.5;
            SendCmd($"d3run {kind} {F(u0)} {F(secs)}");
            await D3RunAsync(kind, u0, secs, ct);
        }

        async UniTask D3RunAsync(string kindName, double u0, float secs, System.Threading.CancellationToken ct)
        {
            var mine = MineBox();
            if (mine == null) { Log("D3RUN not owner"); return; }
            var kind = kindName == "cross" ? ProtoNetBox.Kind.Cross : ProtoNetBox.Kind.Head;
            await WaitUtcAsync(u0, ct);
            mine.StartKind(kind, true, u0);
            var colA = _boxA.GetComponent<Collider>(); var colB = _boxB.GetComponent<Collider>();
            double endAt = Time.realtimeSinceStartupAsDouble + secs;
            double maxPen = 0, sumPen = 0; int overlapFrames = 0, frames = 0; float maxDev = 0; double peakAt = 0;
            while (Time.realtimeSinceStartupAsDouble < endAt)
            {
                await UniTask.Yield(PlayerLoopTiming.PostLateUpdate, ct);
                frames++;
                float pen = Pen(colA, colB);
                if (pen > 0.001f) { overlapFrames++; sumPen += pen; }
                if (pen > maxPen) { maxPen = pen; peakAt = ProtoNetBox.UtcNow() - u0; }
                maxDev = Mathf.Max(maxDev, mine.TargetDistance);
            }
            float finalPen = Pen(colA, colB);
            Shot($"d3_{kindName}");   // 押し付けたままの最終状態（この端末の画面）
            Vector3 pa = _boxA.transform.position, pb = _boxB.transform.position;
            mine.StopKind();
            Result($"D3 kind={kindName} sendRate={_nm.TransformSendRate} frames={frames} maxPenMm={(maxPen * 1000).ToString("F1", Inv)} (at t+{peakAt.ToString("F2", Inv)}s) meanPenWhenOverlapMm={(overlapFrames > 0 ? sumPen / overlapFrames * 1000 : 0).ToString("F1", Inv)} overlapFramesPct={(100.0 * overlapFrames / Math.Max(1, frames)).ToString("F1", Inv)} " +
                   $"finalPenMm={(finalPen * 1000).ToString("F1", Inv)} finalA=({pa.x.ToString("F3", Inv)},{pa.z.ToString("F3", Inv)}) finalB=({pb.x.ToString("F3", Inv)},{pb.z.ToString("F3", Inv)}) ownBox={mine.Label} ownMaxDeviationFromTargetMm={(maxDev * 1000).ToString("F0", Inv)}");
        }

        static float Pen(Collider a, Collider b)
        {
            return Physics.ComputePenetration(a, a.transform.position, a.transform.rotation, b, b.transform.position, b.transform.rotation, out _, out float d) ? d : 0f;
        }
    }
}
