using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace STYLY.TIB_Template.TreasureHaul
{
    /// <summary>
    /// 運営UIの最小（F-OPS-2/3 の一部）。SessionHost の状態を表示し、「開始」「中断」「パラメータ再読み込み」を押せる。
    /// 判断は SessionHostCore / AbortConfirmGate にあり、ここは表示と配線だけ。
    /// </summary>
    public sealed class OperatorView : MonoBehaviour
    {
        [SerializeField] SessionHost _host;
        [SerializeField] TMP_Text _stateText;
        [SerializeField] TMP_Text _timeText;
        [SerializeField] TMP_Text _roundText;
        [SerializeField] TMP_Text _devicesText;
        [SerializeField] TMP_Text _versionText;
        [SerializeField] TMP_Text _warningText;
        [SerializeField] Button _startButton;
        [SerializeField] Button _abortButton;
        [SerializeField] TMP_Text _abortLabel;
        [SerializeField] Button _reloadButton;

        const string AbortLabelNormal = "中断";
        const string AbortLabelArmed = "もう一度押すと中断";

        static readonly Dictionary<RoundState, string> StateLabels = new Dictionary<RoundState, string>
        {
            { RoundState.Waiting, "待機" },
            { RoundState.Opening, "オープニング" },
            { RoundState.Tutorial, "練習" },
            { RoundState.Playing, "本編" },
            { RoundState.Result, "結果" },
        };

        AbortConfirmGate _abortGate;

        static double Now => Time.realtimeSinceStartupAsDouble;

        void Start()
        {
            var core = _host.Core;
            _abortGate = new AbortConfirmGate(core.Params.operation.abortConfirmSeconds);

            core.State.Subscribe(s =>
            {
                _stateText.text = StateLabels[s];
                if (s == RoundState.Waiting) _abortGate.Reset();
            }).AddTo(this);
            core.RemainingSeconds.Select(r => Mathf.CeilToInt(r)).DistinctUntilChanged()
                .Subscribe(sec => _timeText.text = $"残り {sec} 秒").AddTo(this);
            core.RoundId.Subscribe(id => _roundText.text = $"RoundId {id}").AddTo(this);
            core.ConnectedCount.CombineLatest(core.EligibleCount, (c, e) => (c, e))
                .Subscribe(t => _devicesText.text = $"端末 接続中 {t.c} / 参加できる {t.e}").AddTo(this);
            core.ParamsVersionText.Subscribe(v =>
            {
                _versionText.text = $"パラメータ版 {v}";
                _abortGate.SetWindow(core.Params.operation.abortConfirmSeconds);   // 再読み込み後の値に合わせる
            }).AddTo(this);
            core.Warnings.Subscribe(w => _warningText.text = w.Length == 0 ? "" : "警告\n" + string.Join("\n", w)).AddTo(this);

            // 押せる条件
            core.State.CombineLatest(core.EligibleCount, (s, e) => s == RoundState.Waiting && e >= 1)
                .Subscribe(v => _startButton.interactable = v).AddTo(this);
            core.State.Subscribe(s =>
            {
                _abortButton.interactable = s != RoundState.Waiting;
                _reloadButton.interactable = s == RoundState.Waiting;
            }).AddTo(this);

            _startButton.OnClickAsObservable().Subscribe(_ => _host.RequestStart()).AddTo(this);
            _reloadButton.OnClickAsObservable().Subscribe(_ => _host.RequestReloadParams()).AddTo(this);
            _abortButton.OnClickAsObservable().Subscribe(_ =>
            {
                if (_abortGate.Press(Now)) _host.RequestAbort();
            }).AddTo(this);

            UpdateAbortLabelLoop(destroyCancellationToken).Forget();
        }

        async UniTaskVoid UpdateAbortLabelLoop(System.Threading.CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    _abortLabel.text = _abortGate.IsArmed(Now) ? AbortLabelArmed : AbortLabelNormal;
                    await UniTask.Delay(TimeSpan.FromMilliseconds(100), cancellationToken: ct);
                }
            }
            catch (OperationCanceledException) { }
        }
    }
}
