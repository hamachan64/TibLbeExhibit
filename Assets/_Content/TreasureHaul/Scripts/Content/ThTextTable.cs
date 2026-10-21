using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace STYLY.TIB_Template.TreasureHaul
{
    /// <summary>
    /// 文言の ID。<b>末尾にだけ足す</b>（並べ替え・途中への挿入・削除をしない。アセットは数値で ID を持つため）。
    /// </summary>
    public enum ThTextId
    {
        // 運営UI
        StateWaiting,
        StateOpening,
        StateTutorial,
        StatePlaying,
        StateResult,
        OpRemaining,
        OpRoundId,
        OpDevices,
        OpParamsVersion,
        OpWarning,
        OpStart,
        OpAbort,
        OpAbortArmed,
        OpReloadParams,
        // 体験者向け
        GoalSentence,
        GripBeam,
        PracticeGrip,
        PracticeDamageNote,
        TagYours,
        HintTwoHands,
        /// <summary>使わない（2026-10-21、荷台の案内はやめた）。列挙からは消せないので残す</summary>
        HintGoHere,
        PriceUnknown,
        PriceFormat,
        PracticeSuccessFormat,
        DamageFormat,
        CollectFormat,
        CollectDamagedFormat,
        /// <summary>使わない（2026-10-21、結果画面は回収額・目標額・クリアだけにした）。列挙からは消せないので残す</summary>
        ResultLossFormat,
        // 追加要素
        StatueNotice,
        HintLargeTwoPeople,
        // #38
        /// <summary>荷台の表示板。回収した合計額（書式「{0:N0}G」）</summary>
        BoardTotalFormat,
        // #42（オープニング・保留・本編開始・結果）
        /// <summary>オープニングの字幕 1〜5（TREASURE_HAUL_NARRATION.md の op01〜op05 の案。U-5 は未決）</summary>
        OpeningLine1,
        OpeningLine2,
        OpeningLine3,
        OpeningLine4,
        OpeningLine5,
        /// <summary>本編の帯（書式「残り {0}:{1:00} ／ 目標 {2:N0}G」。分・秒・目標額）</summary>
        HudPlayingFormat,
        /// <summary>目標額に届いた瞬間に大きく出す</summary>
        TargetReached,
        /// <summary>結果（書式「{0:N0}G ／ 目標 {1:N0}G」。回収額・目標額）</summary>
        ResultAmountFormat,
        ResultClear,
        /// <summary>結果（書式「あと {0:N0}G でクリア」。足りない額）</summary>
        ResultFailFormat,
        /// <summary>待機画面の小さなクレジット欄（N-9）</summary>
        Credits,
        // 運営UI
        OpHold,
        OpHoldRelease,
        /// <summary>保留中の表示（書式「保留中（あと {0} 秒で自動解除）」）</summary>
        OpHoldingFormat,
        OpBeginPlay,
        /// <summary>合計額と目標額（書式「合計 {0:N0}G ／ 目標 {1:N0}G」）</summary>
        OpTotalFormat,
        // #43（練習）
        /// <summary>練習の案内: 持てた後（持った瞬間の値札は今のまま）</summary>
        PracticeCarry,
        /// <summary>練習の案内: 持っているお宝が納品場所の範囲に入ったら</summary>
        PracticeRelease,
        // #44（端末一覧・見学・位置合わせ確認・記録 CSV）。アセットに無い間は <see cref="ThTextTable"/> の組み込みの既定の文言を使う
        OpDeviceListTitle,
        /// <summary>端末番号（書式「端末 {0}」）</summary>
        OpDeviceNumberFormat,
        /// <summary>未登録の端末（書式「未登録 {0}」。DeviceId の末尾 6 文字）</summary>
        OpDeviceUnregisteredFormat,
        OpDeviceConnected,
        OpDeviceDisconnected,
        OpAlignConfirmed,
        OpAlignUnconfirmed,
        OpHandBoth,
        OpHandOne,
        OpHandNone,
        /// <summary>手の認識・電池が届いていない（または古い）</summary>
        OpStatusUnknown,
        /// <summary>電池（書式「{0}%」）</summary>
        OpBatteryFormat,
        OpBatteryUnknown,
        OpPracticeDone,
        OpPracticeNotDone,
        OpSpectatorTag,
        OpParticipantTag,
        OpSetSpectator,
        OpClearSpectator,
        OpConfirmAlignment,
        OpUnconfirmAlignment,
        OpWriteDeviceTemplate,
        OpNoDevices,
        /// <summary>rounds CSV の見出し（カンマ区切りで 15 列）</summary>
        CsvRoundsHeader,
        /// <summary>players CSV の見出し（カンマ区切りで 11 列）</summary>
        CsvPlayersHeader,
        CsvCompleted,
        CsvAborted,
        CsvYes,
        CsvNo,
    }

    [Serializable]
    public class ThTextEntry
    {
        public ThTextId id;
        [TextArea(1, 3)] public string text = "";

        public ThTextEntry() { }
        public ThTextEntry(ThTextId id, string text) { this.id = id; this.text = text; }
    }

    /// <summary>文言の一覧（ID → 文言）。見た目・音・文言は ThContentSet からまとめて参照する。</summary>
    [CreateAssetMenu(fileName = "ThTextTable", menuName = "Treasure Haul/Text Table")]
    public sealed class ThTextTable : ScriptableObject
    {
        public List<ThTextEntry> entries = DefaultEntries();

        /// <summary>
        /// #44 で足した ID の組み込みの既定の文言。アセット（ThTextTable.asset）に項目が無い・空のときに使う（アセットの値が優先）。
        /// アセットへ移したら、ここから消してよい。
        /// </summary>
        static readonly Dictionary<ThTextId, string> Fallbacks = new Dictionary<ThTextId, string>
        {
            { ThTextId.OpDeviceListTitle, "端末一覧" },
            { ThTextId.OpDeviceNumberFormat, "端末 {0}" },
            { ThTextId.OpDeviceUnregisteredFormat, "未登録 {0}" },
            { ThTextId.OpDeviceConnected, "接続" },
            { ThTextId.OpDeviceDisconnected, "切断" },
            { ThTextId.OpAlignConfirmed, "位置合わせ 確認済み" },
            { ThTextId.OpAlignUnconfirmed, "位置合わせ 未確認" },
            { ThTextId.OpHandBoth, "両手" },
            { ThTextId.OpHandOne, "片手" },
            { ThTextId.OpHandNone, "なし" },
            { ThTextId.OpStatusUnknown, "不明" },
            { ThTextId.OpBatteryFormat, "{0}%" },
            { ThTextId.OpBatteryUnknown, "不明" },
            { ThTextId.OpPracticeDone, "練習 済" },
            { ThTextId.OpPracticeNotDone, "練習 未" },
            { ThTextId.OpSpectatorTag, "見学" },
            { ThTextId.OpParticipantTag, "参加" },
            { ThTextId.OpSetSpectator, "見学にする" },
            { ThTextId.OpClearSpectator, "参加に戻す" },
            { ThTextId.OpConfirmAlignment, "位置合わせを確認" },
            { ThTextId.OpUnconfirmAlignment, "確認を外す" },
            { ThTextId.OpWriteDeviceTemplate, "接続中の端末を書き出す" },
            { ThTextId.OpNoDevices, "端末がありません" },
            { ThTextId.CsvRoundsHeader, "開始時刻,RoundId,終わり方,中断した状態,人数,モード,追加要素,持ち方,パラメータ版,保留の有無,延長秒,回収額,目標額,目標達成,損害額" },
            { ThTextId.CsvPlayersHeader, "RoundId,端末番号,DeviceId,色番号,練習完了秒,初めて持った秒,初めて回収した秒,回収した数,回収額,減額の回数,減額の額" },
            { ThTextId.CsvCompleted, "完了" },
            { ThTextId.CsvAborted, "中断" },
            { ThTextId.CsvYes, "はい" },
            { ThTextId.CsvNo, "いいえ" },
        };

        /// <summary>組み込みの既定の文言（予備フォントの文字集めに使う。ThFontExtraMenu）</summary>
        public static IEnumerable<string> FallbackTexts => Fallbacks.Values;

        Dictionary<ThTextId, string> _map;
        readonly HashSet<ThTextId> _warned = new HashSet<ThTextId>();

        /// <summary>
        /// 全 ID を並べた空の表（文言は入れない）。文言の置き場所はアセット（ThTextTable.asset）の 1 か所だけ。
        /// 新しくアセットを作ったときは ID だけの表になり、Validate が空の文言を警告する。
        /// </summary>
        public static List<ThTextEntry> DefaultEntries()
        {
            var list = new List<ThTextEntry>();
            foreach (ThTextId id in Enum.GetValues(typeof(ThTextId))) list.Add(new ThTextEntry(id, ""));
            return list;
        }

        void OnEnable() { _map = null; }
        void OnValidate() { _map = null; }

        void Build()
        {
            _map = new Dictionary<ThTextId, string>();
            foreach (var e in entries) if (e != null && !_map.ContainsKey(e.id)) _map[e.id] = e.text;
        }

        public bool Has(ThTextId id)
        {
            if (_map == null) Build();
            return _map.ContainsKey(id);
        }

        /// <summary>文言を返す。無い ID は ID 名を返し、警告を 1 回だけ出す。</summary>
        public string Get(ThTextId id)
        {
            if (_map == null) Build();
            if (_map.TryGetValue(id, out var s) && !string.IsNullOrEmpty(s)) return s;
            if (Fallbacks.TryGetValue(id, out var fallback)) return fallback;
            if (_warned.Add(id)) Debug.LogWarning($"[ThTextTable] 文言がありません: {id}");
            return id.ToString();
        }

        /// <summary>string.Format で埋める（金額は {0:N0} で 1,200 のように桁区切り。culture に依らず「,」）。</summary>
        public string Format(ThTextId id, params object[] args)
        {
            var fmt = Get(id);
            try { return string.Format(CultureInfo.InvariantCulture, fmt, args); }
            catch (FormatException)
            {
                if (_warned.Add(id)) Debug.LogWarning($"[ThTextTable] 書式が不正です: {id} = {fmt}");
                return fmt;
            }
        }

        /// <summary>重複した ID と、文言の欠け・空を返す（空なら問題なし）。</summary>
        public List<string> Validate()
        {
            var errors = new List<string>();
            var seen = new HashSet<ThTextId>();
            foreach (var e in entries)
            {
                if (e == null) { errors.Add("空の要素があります"); continue; }
                if (!seen.Add(e.id)) errors.Add($"ID が重複しています: {e.id}");
                if (string.IsNullOrWhiteSpace(e.text) && !Fallbacks.ContainsKey(e.id)) errors.Add($"文言が空です: {e.id}");
            }
            foreach (ThTextId id in Enum.GetValues(typeof(ThTextId)))
                if (!seen.Contains(id) && !Fallbacks.ContainsKey(id)) errors.Add($"文言が欠けています: {id}");
            return errors;
        }
    }
}
