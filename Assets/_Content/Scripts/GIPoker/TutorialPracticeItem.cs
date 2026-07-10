using UnityEngine;

namespace STYLY.TIB_Template.GIPoker
{
    /// <summary>
    /// チュートリアル専用の練習アイテム（単色の球）。
    ///
    /// 本番アイテム（プレハブ＋ItemSpawnManager 経由）と違い、完全にランタイム生成する:
    /// - CardNetworkSync.Register を呼ばない（NetworkId は -1 のまま）ため、
    ///   カード化・掴む・収納・ゲインの状態遷移が Gi* RPC として配信されることはない
    /// - カード定義もランタイム生成（ランクD・効果なし）なので、ゲインしても
    ///   CardEffectService は何も発動せず、GiEffect 等の配信も起きない
    /// - バインダーに収納された練習カードは Playing 遷移時に TutorialDirector が
    ///   CardBinder.Clear() で撤去するため、本番スコアを汚さない
    ///
    /// 基底クラスの handLayerMask はシリアライズ値（ランタイム生成では 0 = 判定なし）のため、
    /// 素手タッチの判定は同じ GameObject に付ける TutorialTouchTrigger が代行する。
    /// </summary>
    public class TutorialPracticeItem : CardizableItemBase
    {
        private const float VisualDiameter = 0.12f;
        private const float TriggerRadius = 0.09f;

        /// <summary>
        /// 練習アイテムを生成する（生成直後は非アクティブ。位置決め後に SetActive(true) すること）。
        /// </summary>
        public static TutorialPracticeItem Create(string name, Color color)
        {
            var go = new GameObject(name);
            go.SetActive(false);

            // アイテム見た目: 単色の球（基底クラスの Awake がフォールバックレンダラーとして拾う）
            var visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            visual.name = "Visual";
            Destroy(visual.GetComponent<Collider>());
            visual.transform.SetParent(go.transform, false);
            visual.transform.localScale = Vector3.one * VisualDiameter;
            visual.GetComponent<Renderer>().sharedMaterial =
                CardVisualFactory.CreateLitMaterial(color, color * 0.5f);

            // 素手タッチ（TutorialTouchTrigger）とピンチ掴み（CardGrabInteractor の
            // OverlapSphere）の両方が拾うトリガーコライダー
            var trigger = go.AddComponent<SphereCollider>();
            trigger.radius = TriggerRadius;
            trigger.isTrigger = true;

            // 非アクティブな GameObject への AddComponent なので Awake は
            // SetActive(true) まで遅延する（＝見た目の構築が先に完了している）
            var item = go.AddComponent<TutorialPracticeItem>();

            // ランタイム生成のカード定義（既定値: ランクD・効果なし）。
            // 定義が null だとスコア計算（CardInstance.Rank）で例外になるため必須
            var definition = ScriptableObject.CreateInstance<CardDefinition>();
            definition.name = "練習カード"; // DisplayName 未設定時は name がカード名として表示される
            item.OverrideDefinition(definition);

            go.AddComponent<TutorialTouchTrigger>().Initialize(item);
            return item;
        }
    }
}
