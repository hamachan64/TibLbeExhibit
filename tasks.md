# 🧪 Unityエディタでのテスト手順書
## Step 1: カード定義アセットの作成
Projectウィンドウで Assets/_Content/ 内に CardDefinitions フォルダを作成
右クリック → Create > GIPoker > Card Definition
例として「花」を作成: cardId: d_flower、displayName: 花、rank: D、effectType: None
テスト用に数種類（D/C/A/Sなど）作っておくと役計算の確認がしやすいです

## Step 2: カード化アイテムのセットアップ（Environmentシーン想定）
テスト用オブジェクト（Cube等）をシーンに配置
CardizableItemBase をアタッチし、definition にStep 1のアセットを設定
同オブジェクトに Collider（Is Trigger = ON） と Rigidbody（Is Kinematic = ON） を追加 ※トリガーイベント発火に必要
handLayerMask に手のコライダー用レイヤー（例: 新規レイヤー HandBone）を設定
（任意）子に「アイテム見た目」「カード見た目」の2つを作り、itemVisualRoot / cardVisualRoot に割り当てると、カード化時に見た目が自動で切り替わります

## Step 3: 手側のセットアップ（UserClientシーン想定）
XR Origin配下の左右の手（XR Handsのハンド）それぞれに空のGameObject（例: RightHandInteractor）を用意
HandPinchTracker をアタッチ: handedness = Right（左手はLeft）、xrOrigin に XR Origin の Transform を設定
同じGameObjectに CardGrabInteractor をアタッチ（trackerは自動取得されます）
カード化トリガー用に、手の指先（人差し指先端ボーン）に小さな SphereCollider ＋ Kinematic Rigidbody を付け、レイヤーをStep 2-4の HandBone に設定

## Step 4: バインダーのセットアップ
バインダー用GameObject（腰や左手前あたりに置くCube等で可）に CardBinder をアタッチ
storeRadius（既定0.2m）はテスト中に調整。storePoint 未設定なら自身の位置が収納中心になります

## Step 5: 動作確認（エディタ再生）
実機なしでもロジック単体は以下で確認できます:

カード化と1分ルール: 再生中にInspectorから CardizableItemBase.TryCardify() 相当を確認したい場合、HandBone レイヤーのコライダー付きオブジェクトをアイテムに接触させる → 浮遊開始、60秒後に縮小消滅すればOK
役計算: 一時スクリプトかテストで PokerHandEvaluator.Evaluate(new[]{ CardRank.D, CardRank.C, CardRank.B, CardRank.A, CardRank.S }) を呼び、TotalScore == 311（基本111＋ストレート200）になることを確認
ピンチ判定: XR Handsが動く実機（PICO/Quest）またはXR Device Simulatorで、カードに指を近づけてつまむ → 追従 → バインダー付近で離す → 消えて CardBinder.Cards に追加されればOK

## 次のステップ候補
「ゲイン」ハンドサインのジェスチャー判定（XR Hands Gestures サンプルが Assets/Samples/XR Hands/1.7.3/Gestures に導入済みなので流用可能）
バインダーUI（Cards の差分購読で描画）とリザルト画面（Score 購読）
カード効果システム（CardEffectType をキーに OnGained から発動）
STYLY NetSyncでのカード状態同期