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
                if (string.IsNullOrWhiteSpace(e.text)) errors.Add($"文言が空です: {e.id}");
            }
            foreach (ThTextId id in Enum.GetValues(typeof(ThTextId)))
                if (!seen.Contains(id)) errors.Add($"文言が欠けています: {id}");
            return errors;
        }
    }
}
