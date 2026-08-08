# カード化／ゲイン時の煙エフェクト追加

## 目的
アイテムがカード化される瞬間、およびカードがゲイン（アイテム化）される瞬間に、
バインダー出し入れ時と同じ白煙＋衝撃波VFX（`VFX_BinderSummonPoof`）を追加で再生する。
既存の `CardifyVfx` / `GainVfx`（キラキラ系の演出）はそのまま維持し、その上に煙演出を重ねることで
「モノが具現化・脱具現化する」瞬間の説得力を強める。

## 受け入れ基準
- [ ] `CardizableItemBase.OnCardified()` が呼ばれたとき、既存の `CardifyVfx` に加えて
      `GIPokerAssets.BinderSmokeVfx` がカードサイズ相当のスケールでその場に再生される。
- [ ] `CardizableItemBase.OnGained()` が呼ばれたとき、既存の `GainVfx` に加えて
      `GIPokerAssets.BinderSmokeVfx` がカードサイズ相当のスケールでその場に再生される。
- [ ] `GIPokerAssets.BinderSmokeVfx` が未設定（null）でも例外を投げず、既存のカード化・ゲイン処理は
      従来どおり動作する（`GIPokerAssets.SpawnVfx` の null ガードに委ねる）。
- [ ] 新規に SerializeField ／ GIPokerAssets フィールドを増やさない
      （既存の `BinderSmokeVfx` をそのまま流用する。バインダー用と共通の1アセットのまま）。
- [ ] コンパイルが通り、既存EditModeテスト（特に `CardizableItemBaseTests` / `BinderViewTests`）が
      グリーンのまま。

## 変更対象
- `Assets/_Content/Scripts/GIPoker/CardizableItemBase.cs`:
  - `OnCardified()` / `OnGained()` に、`GIPokerAssets.BinderSmokeVfx` を
    カードサイズ用スケールで追加再生する処理を足す。
  - スケール・再生時間は private const として定義する（`BinderView.SmokeVfxLifetime` を参考に、
    カードは小物なので少し短め〜同程度でよい）。
    - スケール目安: カード実寸（`CardVisualFactory` の `CardWidth=0.063f` / `CardHeight=0.088f`）に対し、
      `VFX_BinderSummonPoof` はバインダー実寸（幅約0.45m）基準で作られている。
      比率的に概ね 0.15〜0.2 程度が「カードサイズに合わせて小さくした煙」として妥当。
      実装エージェントは 0.18f 程度を初期値としつつ、`GIPokerAssets.SpawnVfx` の `scale` 引数（既存機能）
      で縮小すること（VFXプレハブ自体は改変しない）。

## やらないこと（スコープ外）
- `VFX_BinderSummonPoof.prefab` 自体の編集（既存のバインダー演出に影響させない）。
- `GIPokerAssets` / `GIPokerBootstrap` への新規SerializeField追加（既存の `BinderSmokeVfx` を流用するため不要）。
- `CardifyVfx` / `GainVfx` の差し替えや削除。
- 1分ルール消滅（`OnExpired`）への煙演出追加（今回の依頼対象外）。
- サウンド（SE）の追加。

## 制約
- 規約: UniTask / R3 / DOTween、ハンドトラッキング専用、.unity/.prefab/.asset は uloop経由で編集
  （今回はプレハブ非改変・C#のみのため実質関係なし）。
- `GIPokerAssets.SpawnVfx` の既存シグネチャ（`scale` 引数）をそのまま使う。新規ヘルパーは作らない。
- コルーチン禁止（本タスクでは非同期処理自体不要）。

## 検証方法
- EditModeテスト: 既存 `CardizableItemBaseTests` がグリーンのままであることを確認
  （VFX再生自体は Object.Instantiate を伴うため新規アサーションは無理に追加しなくてよいが、
  可能であれば「BinderSmokeVfx未設定時に例外が出ない」程度の軽量テストを検討する）。
- コンパイル確認（gipoker-verify）。
- PlayMode/目視: エディタPlayModeでアイテムに触れてカード化し、バインダーに収納後、
  ゲインした際にそれぞれ煙VFXがカードサイズで発生することを確認する
  （可能なら uloop-screenshot で1枚ずつ撮影）。

## 実機確認が必要な項目
- 実機（PICO）でのフルカラーパススルー環境下での煙VFXの視認性・スケール感
  （エディタ確認のみでは光の見え方が実機と異なるため）。
