using System.Collections.Generic;
using System.Globalization;
using R3;
using Styly.NetSync;
using UnityEngine;

namespace STYLY.TIB_Template.GIPoker
{
    /// <summary>
    /// カード状態のマルチユーザー同期（STYLY NetSync RPC ベース）。
    ///
    /// 設計:
    /// - アイテムの出現自体は同期しない。ItemSpawnManager が共有シードから
    ///   決定論的に出現させるため、全クライアントで同じ NetworkId のアイテムが
    ///   同じ位置に存在する前提が成立する
    /// - プレイヤー操作による離散的な状態遷移（カード化/掴む/離す/収納/ゲイン/錬金）
    ///   だけを RPC で配信する。各アイテムの State (R3) の遷移を購読して自動配信
    ///   するため、ゲームプレイ側のコードは同期を意識しない
    /// - 受信適用中は applyingRemote フラグで再配信を抑制し、
    ///   自分が送った RPC のエコーは ClientNo で無視する（オフラインループバック対応）
    /// - 1分ルールの消滅（Expired）は配信しない。カード化イベントを受けた各クライアントが
    ///   ローカルタイマーで消滅させる（イベント数の削減と耐障害性のため）
    /// - リスキーダイス大凶のバインダー破壊は「発動者のバインダーのみ」なのでローカル処理。
    ///   大吉のSS出現・全体効果（占い水晶/彷徨うよろい）・錬金は配信する
    /// - スコアは UserVariable.GiScore として共有する（他プレイヤーのスコア表示用の基盤）
    ///
    /// 制限: 掴んでいる間のカードの位置は同期しない（リモートではその場に静止し、
    /// 離した位置 GiRelease で再配置される）。継続的な位置同期が必要になったら
    /// NetSyncObject の導入を検討すること。
    /// </summary>
    [DefaultExecutionOrder(-45)]
    public class CardNetworkSync : MonoBehaviour
    {
        public static CardNetworkSync Instance { get; private set; }

        /// <summary>SS出現などランタイム生成アイテム用のIDオフセット（スケジュールIDと衝突しない領域）</summary>
        private const int DynamicIdBase = 100000;

        private readonly Dictionary<int, CardizableItemBase> itemsById = new();
        private bool applyingRemote;
        private int dynamicIdCounter;

        private CardEffectService effectService;

        private static int SelfClientNo =>
            NetSyncManager.Instance != null ? NetSyncManager.Instance.ClientNo : -999;

        private static bool Online => RpcManager.Instance != null;

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>GIPokerBootstrap から結線する</summary>
        public void Initialize(CardEffectService effects, CardBinder binder)
        {
            effectService = effects;

            if (!Online)
            {
                Debug.LogWarning("[CardNetworkSync] RpcManager が見つからないため同期は無効です（単一シーンテスト中は正常）");
                return;
            }

            SubscribeInbound();

            // 全体効果（占い水晶・彷徨うよろい）のローカル発動を配信
            effects.EffectActivated
                .Subscribe(x =>
                {
                    if (applyingRemote)
                    {
                        return;
                    }
                    if (x.effect == CardEffectType.SlowFloating || x.effect == CardEffectType.PauseCountdown)
                    {
                        Send(Rpc.GiEffect, ((int)x.effect).ToString());
                    }
                })
                .AddTo(this);

            // ダイス結果（リモートでも大吉/大凶のVFXを見せる）
            effects.DiceRolled
                .Subscribe(r =>
                {
                    if (!applyingRemote)
                    {
                        Send(Rpc.GiDice, r.Roll.ToString(), r.IsBless ? "1" : "0", r.IsCurse ? "1" : "0",
                            F(r.Position.x), F(r.Position.y), F(r.Position.z));
                    }
                })
                .AddTo(this);

            // 大吉によるSS出現を配信
            effects.SsSpawned
                .Subscribe(item =>
                {
                    if (applyingRemote || item == null)
                    {
                        return;
                    }
                    var id = DynamicIdBase + SelfClientNo * 100 + dynamicIdCounter++;
                    Register(id, item);
                    var p = item.transform.position;
                    Send(Rpc.GiSpawnSS, id.ToString(), F(p.x), F(p.y), F(p.z));
                })
                .AddTo(this);

            // 自分のスコアを UserVariable として共有
            binder.Score
                .Subscribe(score => VariableManager.Instance?.SetSelf(UserVariable.GiScore, score.TotalScore))
                .AddTo(this);
        }

        /// <summary>
        /// アイテムを同期対象として登録する（ItemSpawnManager / SS出現時に呼ばれる）。
        /// 以後、このアイテムのローカルな状態遷移は自動的に配信される。
        /// </summary>
        public void Register(int networkId, CardizableItemBase item)
        {
            item.NetworkId = networkId;
            itemsById[networkId] = item;

            var previous = item.State.CurrentValue;
            item.State
                .Subscribe(current =>
                {
                    var prev = previous;
                    previous = current;
                    if (prev != current)
                    {
                        OnLocalStateChanged(item, prev, current);
                    }
                })
                .AddTo(item);

            item.DefinitionChanged
                .Subscribe(def =>
                {
                    if (!applyingRemote && def != null)
                    {
                        Send(Rpc.GiConvert, item.NetworkId.ToString(), def.CardId);
                    }
                })
                .AddTo(item);
        }

        // ========== 送信（ローカルの状態遷移 → RPC） ==========

        private void OnLocalStateChanged(CardizableItemBase item, CardizableState prev, CardizableState current)
        {
            if (applyingRemote || !Online || item.NetworkId < 0)
            {
                return;
            }

            var id = item.NetworkId.ToString();
            var p = item.transform.position;
            switch (current)
            {
                case CardizableState.FloatingCard when prev == CardizableState.Item:
                    Send(Rpc.GiCardify, id);
                    break;
                case CardizableState.Held:
                    Send(Rpc.GiGrab, id);
                    break;
                case CardizableState.FloatingCard when prev == CardizableState.Held || prev == CardizableState.Stored:
                    Send(Rpc.GiRelease, id, F(p.x), F(p.y), F(p.z));
                    break;
                case CardizableState.Stored:
                    Send(Rpc.GiStore, id);
                    break;
                case CardizableState.Gained:
                    Send(Rpc.GiGain, id, F(p.x), F(p.y), F(p.z));
                    break;
                case CardizableState.Expired:
                    // 消滅は配信しない（各クライアントのローカルタイマーで発生する）
                    break;
            }
        }

        // ========== 受信（RPC → 状態適用） ==========

        private void SubscribeInbound()
        {
            Subscribe(Rpc.GiCardify, args => WithItem(args[0], item => item.TryCardify()));

            Subscribe(Rpc.GiGrab, args => WithItem(args[0], item => item.TryGrab()));

            Subscribe(Rpc.GiRelease, args => WithItem(args[0], item =>
            {
                var pos = V3(args, 1);
                if (item.State.CurrentValue == CardizableState.Stored)
                {
                    item.ReleaseFromBinder(pos); // 他プレイヤーがバインダーから取り出した
                }
                else
                {
                    item.transform.position = pos;
                    item.Release();
                }
            }));

            Subscribe(Rpc.GiStore, args => WithItem(args[0], item =>
            {
                // 他プレイヤーのバインダーに収納された = この空間からは消える
                item.MarkStored();
            }));

            Subscribe(Rpc.GiGain, args => WithItem(args[0], item =>
            {
                item.transform.position = V3(args, 1);
                item.TryGain();
                // 効果の実行はしない。ゲインした本人が結果（GiConvert/GiEffect/GiDice等）を配信する
            }));

            Subscribe(Rpc.GiConvert, args => WithItem(args[0], item =>
            {
                var def = effectService != null ? effectService.FindAlchemyDefinition(args[1]) : null;
                if (def != null)
                {
                    item.OverrideDefinition(def);
                }
            }));

            Subscribe(Rpc.GiEffect, args =>
            {
                if (effectService != null && int.TryParse(args[0], out var effect))
                {
                    effectService.ApplyEffectRemote((CardEffectType)effect);
                }
            });

            Subscribe(Rpc.GiDice, args =>
            {
                var isBless = args[1] == "1";
                var isCurse = args[2] == "1";
                var pos = V3(args, 3);
                if (isBless)
                {
                    GIPokerAssets.SpawnVfx(GIPokerAssets.DiceBlessVfx, pos, 8f);
                }
                else if (isCurse)
                {
                    GIPokerAssets.SpawnVfx(GIPokerAssets.DiceCurseVfx, pos, 8f);
                }
            });

            Subscribe(Rpc.GiSpawnSS, args =>
            {
                if (effectService == null || !int.TryParse(args[0], out var id))
                {
                    return;
                }
                var item = effectService.SpawnSsCardAt(V3(args, 1), announce: false);
                if (item != null)
                {
                    Register(id, item);
                }
            });
        }

        /// <summary>自分のエコーを除外し、受信適用中フラグを立てて適用する共通ラッパー</summary>
        private void Subscribe(Rpc rpc, System.Action<string[]> apply)
        {
            RpcManager.Instance.AsObservable(rpc)
                .Where(data => data.ClientNo != SelfClientNo && data.Parameters != null)
                .Subscribe(data =>
                {
                    applyingRemote = true;
                    try
                    {
                        apply(data.Parameters);
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogError($"[CardNetworkSync] {rpc} の適用に失敗: {e.Message}");
                    }
                    finally
                    {
                        applyingRemote = false;
                    }
                })
                .AddTo(this);
        }

        private void WithItem(string idArg, System.Action<CardizableItemBase> action)
        {
            if (!int.TryParse(idArg, out var id))
            {
                return;
            }
            if (!itemsById.TryGetValue(id, out var item) || item == null)
            {
                // ローカルでは既に消滅（Expired）している等のレースは無視する
                Debug.Log($"[CardNetworkSync] itemId={id} は存在しないためイベントをスキップ");
                return;
            }
            action(item);
        }

        private static void Send(Rpc rpc, params string[] args)
        {
            RpcManager.Instance?.Send(rpc, args);
        }

        private static string F(float v)
        {
            return v.ToString("F3", CultureInfo.InvariantCulture);
        }

        private static Vector3 V3(string[] args, int startIndex)
        {
            return new Vector3(
                float.Parse(args[startIndex], CultureInfo.InvariantCulture),
                float.Parse(args[startIndex + 1], CultureInfo.InvariantCulture),
                float.Parse(args[startIndex + 2], CultureInfo.InvariantCulture));
        }
    }
}
