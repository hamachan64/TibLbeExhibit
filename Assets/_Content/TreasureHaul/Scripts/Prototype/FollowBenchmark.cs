using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace STYLY.TIB_Template.TreasureHaul.Prototype
{
    /// <summary>
    /// 試作(S-4): 3つの軌道 x 3通りの Fixed Timestep で追従を自動計測し、CSV に書く。
    /// 目標は FixedUpdate ごとに「物理時間」で動かすので、描画フレームレートに結果が左右されない。
    /// 測定中だけ Time.fixedDeltaTime を変え、終わったら（途中で止めても）元に戻す。
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class FollowBenchmark : MonoBehaviour
    {
        const float PressSpeed = 1.0f;       // 押し付け軌道の目標の速さ (m/s)
        const float FastPressSpeed = 3.0f;   // 速い手の押し付け (m/s)
        const float PushDepth = 0.4f;        // 目標を障害物の中へ入れる深さ (m)

        [SerializeField] ProtoVelocityFollower _follower;
        [SerializeField] Transform _target;
        [SerializeField] Collider[] _obstacles;
        [SerializeField] Transform _wall;
        [SerializeField] Transform _box;
        [SerializeField] bool _autoRun = true;
        [Tooltip("空なら Application.persistentDataPath")]
        [SerializeField] string _outputDirectory = "";

        public sealed class Result
        {
            public float FixedDelta; public string Label; public string Method; public string Trajectory; public int Steps;
            public double LagSum, LagMax, AngSum, AngMax; public int LagCount;
            public float PenetrationMax; public int Reversals; public int PressingSteps; public int InsideSteps;
            public double NormalDistSum, NormalDistSqSum; public int NormalDistCount;
        }

        sealed class Run
        {
            public string Trajectory;
            public float Duration;
            public Func<float, Pose> Path;
            public Pose Start;
        }

        readonly List<Result> _results = new List<Result>();
        Run _run;
        Result _cur;
        int _stepIndex, _stepTotal;
        bool _runDone;
        Pose _lastTarget;
        Vector3 _prevPos;
        float _prevNormalSpeed;
        bool _hasPrev;
        Collider _followerCol;
        float _originalFixedDelta;
        bool _holdAtEnd;

        public IReadOnlyList<Result> Results => _results;
        public string LastCsvPath { get; private set; }

        void Awake()
        {
            _originalFixedDelta = Time.fixedDeltaTime;
            _followerCol = _follower.GetComponent<Collider>();
        }

        async UniTaskVoid Start()
        {
            if (_autoRun) await RunAllAsync();
        }

        void OnDestroy() { Time.fixedDeltaTime = _originalFixedDelta; }

        public async UniTask RunAllAsync()
        {
            var ct = destroyCancellationToken;
            _results.Clear();
            try
            {
                bool originalSweep = _follower.SweepStop;
                try
                {
                    foreach (bool sweep in new[] { false, true })
                    {
                        _follower.SweepStop = sweep;
                        foreach (var cfg in new[] { (0.02f, "0.02"), (1f / 72f, "1/72"), (1f / 90f, "1/90") })
                        {
                            Time.fixedDeltaTime = cfg.Item1;
                            foreach (var run in BuildRuns())
                                await ExecuteAsync(run, cfg.Item1, cfg.Item2, ct);
                        }
                    }
                }
                finally { _follower.SweepStop = originalSweep; }
                LastCsvPath = WriteCsv();
                Debug.Log($"[FollowBenchmark] done. rows={_results.Count} csv={LastCsvPath}");
            }
            finally
            {
                Time.fixedDeltaTime = _originalFixedDelta;
            }
        }

        /// <summary>1つの軌道だけを流し、終わっても目標を動かしたままにせず最終姿勢で止める（スクリーンショット用）。</summary>
        public async UniTask RunHoldAsync(int runIndex, float fixedDelta)
        {
            var ct = destroyCancellationToken;
            _holdAtEnd = true;
            try
            {
                Time.fixedDeltaTime = fixedDelta;
                await ExecuteAsync(BuildRuns()[runIndex], fixedDelta, "hold", ct);
            }
            finally
            {
                _holdAtEnd = false;
                Time.fixedDeltaTime = _originalFixedDelta;
            }
        }

        async UniTask ExecuteAsync(Run run, float dt, string label, System.Threading.CancellationToken ct)
        {
            // 初期姿勢へ戻して、数ステップ落ち着かせる
            var rb = _follower.Body;
            _target.SetPositionAndRotation(run.Start.position, run.Start.rotation);
            rb.position = run.Start.position; rb.rotation = run.Start.rotation;
            rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero;
            Physics.SyncTransforms();
            _run = null;
            await UniTask.WaitForFixedUpdate(ct);
            await UniTask.WaitForFixedUpdate(ct);

            _cur = new Result { FixedDelta = dt, Label = label, Trajectory = run.Trajectory, Method = _follower.SweepStop ? "sweep" : "baseline" };
            _stepTotal = Mathf.RoundToInt(run.Duration / dt);
            _stepIndex = 0; _hasPrev = false; _runDone = false;
            _prevPos = rb.position; _prevNormalSpeed = 0f;
            _lastTarget = run.Start;
            _follower.ConsumeContact();
            _run = run;
            await UniTask.WaitUntil(() => _runDone, PlayerLoopTiming.Update, ct);
            if (_cur.Label != "hold") _results.Add(_cur);
            Debug.Log($"[FollowBenchmark] {_cur.Method} {label} {run.Trajectory}: lagMean={Mean(_cur.LagSum, _cur.LagCount) * 1000:F2}mm lagMax={_cur.LagMax * 1000:F2}mm pen={_cur.PenetrationMax * 1000:F2}mm nrev={_cur.Reversals} jitter={NormalStd(_cur) * 1000:F3}mm inside={_cur.InsideSteps} pressSteps={_cur.PressingSteps}/{_cur.Steps}");
        }

        void FixedUpdate()
        {
            if (_run == null || _runDone) return;
            var rb = _follower.Body;

            // 1. 直前の物理ステップの結果を測る（目標は前ステップのもの）
            if (_stepIndex > 0) Measure(rb);

            if (_stepIndex >= _stepTotal)
            {
                _runDone = true;
                if (!_holdAtEnd) _run = null;
                return;
            }

            // 2. 目標を1ステップ進める（追従側はこの後に自分の FixedUpdate で速度を決める）
            float t = (_stepIndex + 1) * Time.fixedDeltaTime;
            _lastTarget = _run.Path(t);
            _target.SetPositionAndRotation(_lastTarget.position, _lastTarget.rotation);
            _stepIndex++;
        }

        void Measure(Rigidbody rb)
        {
            _follower.ConsumeContact();
            float dt = Time.fixedDeltaTime;
            float dist = Vector3.Distance(rb.position, _lastTarget.position);
            float ang = Quaternion.Angle(rb.rotation, _lastTarget.rotation);

            // 目標が障害物の外にある間だけ「遅れ」に数える（目標の姿勢に追従体のコライダーを置いて、重なるかで判定）
            bool targetFree = true;
            foreach (var o in _obstacles)
            {
                if (Physics.ComputePenetration(_followerCol, _lastTarget.position, _lastTarget.rotation, o, o.transform.position, o.transform.rotation, out _, out _))
                { targetFree = false; break; }
            }
            if (targetFree)
            {
                _cur.LagSum += dist; _cur.AngSum += ang; _cur.LagCount++;
                if (dist > _cur.LagMax) _cur.LagMax = dist;
                if (ang > _cur.AngMax) _cur.AngMax = ang;
            }

            // めり込み（最大）と、最も近い障害物の接触面の向き
            Collider nearest = null; float nearestDist = float.MaxValue; Vector3 nearestPoint = Vector3.zero; bool inside = false;
            foreach (var o in _obstacles)
            {
                if (Physics.ComputePenetration(_followerCol, rb.position, rb.rotation, o, o.transform.position, o.transform.rotation, out _, out float d)
                    && d > _cur.PenetrationMax)
                    _cur.PenetrationMax = d;
                Vector3 cp = o.ClosestPoint(rb.position);
                float cd = Vector3.Distance(cp, rb.position);
                if (cd < nearestDist) { nearestDist = cd; nearest = o; nearestPoint = cp; }
                if (cd < 1e-6f) inside = true;   // 中心が障害物の中 = すり抜け（壁を厚くしてあるので通常は起きない）
            }
            if (inside) _cur.InsideSteps++;

            // ガタつき: 押し付け中（目標が障害物の中）に、接触面の法線方向の速度の符号が反転した回数と、法線方向の位置の揺れ幅。
            // 法線 = 最も近い障害物の最近点 → 追従体の中心 の向き（壁なら壁の法線、角なら角から中心への向き）
            bool pressing = !targetFree;
            if (pressing && nearest != null && nearestDist > 1e-6f && _hasPrev)
            {
                Vector3 n = (rb.position - nearestPoint) / nearestDist;
                float vn = Vector3.Dot(rb.position - _prevPos, n) / dt;
                const float minSpeed = 0.02f;   // m/s。これ未満は「止まっている」として符号を見ない
                if (Mathf.Abs(vn) > minSpeed)
                {
                    if (Mathf.Abs(_prevNormalSpeed) > minSpeed && Mathf.Sign(vn) != Mathf.Sign(_prevNormalSpeed)) _cur.Reversals++;
                    _prevNormalSpeed = vn;
                }
                _cur.NormalDistSum += nearestDist; _cur.NormalDistSqSum += (double)nearestDist * nearestDist; _cur.NormalDistCount++;
            }
            if (pressing) _cur.PressingSteps++;
            _prevPos = rb.position; _hasPrev = true;
            _cur.Steps++;
        }

        List<Run> BuildRuns()
        {
            var runs = new List<Run>();

            // ① 何もない所を往復（ゆっくり / 速い。速い方は追従速度の上限を超える）
            Pose Free(float t, float period, float amp)
            {
                float s = Mathf.Sin(2f * Mathf.PI * t / period);
                return new Pose(new Vector3(amp * s, 0.5f, -6f), Quaternion.Euler(0f, 40f * s, 0f));
            }
            runs.Add(new Run { Trajectory = "1a_free_slow", Duration = 4f, Path = t => Free(t, 2f, 1.5f), Start = Free(0, 2f, 1.5f) });
            runs.Add(new Run { Trajectory = "1b_free_fast", Duration = 3f, Path = t => Free(t, 0.6f, 1.5f), Start = Free(0, 0.6f, 1.5f) });

            // 押し付け軌道は、ふつうの速さ(1m/s)と速い手(3m/s)の2通り
            foreach (var spd in new[] { (PressSpeed, "", 5f), (FastPressSpeed, "_fast", 3f) })
            {
                float speed = spd.Item1; string suffix = spd.Item2; float dur = spd.Item3;

                // ② 壁に斜めに押し付けて沿わせる（壁の手前面から PushDepth だけ中へ入れたまま、壁に沿って進む）
                float wallFace = _wall.position.x - _wall.localScale.x * 0.5f;
                var s2 = new Vector3(wallFace - 1.6f, 0.5f, -2f);
                float v2 = speed / Mathf.Sqrt(2f);
                Pose Wall(float t)
                {
                    float x = Mathf.Min(s2.x + v2 * t, wallFace + PushDepth);
                    var p = new Vector3(x, 0.5f, Mathf.Min(s2.z + v2 * t, 3.5f));
                    return new Pose(p, Quaternion.Euler(0f, 30f * Mathf.Min(t / 3f, 1f), 0f));
                }
                runs.Add(new Run { Trajectory = "2_wall_oblique" + suffix, Duration = dur, Path = Wall, Start = Wall(0) });

                // ③ 箱の角に押し付ける（角へ向かって斜めに入り、PushDepth 中で止めて押し続ける）
                Vector3 corner = new Vector3(_box.position.x + _box.localScale.x * 0.5f, 0.5f, _box.position.z - _box.localScale.z * 0.5f);
                Vector3 dir = new Vector3(-1f, 0f, 1f).normalized;
                Vector3 side = new Vector3(1f, 0f, 1f).normalized * 0.02f; // 角の真正面だと対称で決まらないので少しずらす
                Vector3 s3 = corner - dir * 2f + side;
                Pose Corner(float t)
                {
                    float dist = Mathf.Min(speed * t, 2f + PushDepth);
                    return new Pose(s3 + dir * dist, Quaternion.identity);
                }
                runs.Add(new Run { Trajectory = "3_box_corner" + suffix, Duration = dur, Path = Corner, Start = Corner(0) });
            }
            return runs;
        }

        string WriteCsv()
        {
            var sb = new StringBuilder();
            sb.AppendLine("method,fixedDeltaLabel,fixedDeltaSec,trajectory,steps,lagMeanMm,lagMaxMm,angLagMeanDeg,angLagMaxDeg,penetrationMaxMm,normalReversals,normalJitterStdMm,pressingSteps,insideSteps");
            var ci = CultureInfo.InvariantCulture;
            foreach (var r in _results)
            {
                sb.AppendLine(string.Join(",", r.Method, r.Label, r.FixedDelta.ToString("F5", ci), r.Trajectory, r.Steps.ToString(ci),
                    (Mean(r.LagSum, r.LagCount) * 1000).ToString("F2", ci), (r.LagMax * 1000).ToString("F2", ci),
                    Mean(r.AngSum, r.LagCount).ToString("F3", ci), r.AngMax.ToString("F3", ci),
                    (r.PenetrationMax * 1000).ToString("F2", ci), r.Reversals.ToString(ci), (NormalStd(r) * 1000).ToString("F3", ci), r.PressingSteps.ToString(ci), r.InsideSteps.ToString(ci)));
            }
            string dir = Environment.GetEnvironmentVariable("TH_PROTO_OUT");
            if (string.IsNullOrEmpty(dir)) dir = string.IsNullOrEmpty(_outputDirectory) ? Application.persistentDataPath : _outputDirectory;
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, $"follow_benchmark_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
            File.WriteAllText(path, sb.ToString());
            return path;
        }

        static double NormalStd(Result r)
        {
            if (r.NormalDistCount < 2) return 0.0;
            double m = r.NormalDistSum / r.NormalDistCount;
            return Math.Sqrt(Math.Max(0.0, r.NormalDistSqSum / r.NormalDistCount - m * m));
        }

        static double Mean(double sum, int n) => n > 0 ? sum / n : 0.0;
    }
}
