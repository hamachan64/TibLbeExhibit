# 煙VFX再生時のSEフック追加（音源は後日差し替え）

## 目的
`.claude/loops/20260807-card-item-smoke-effect.md` で追加した「バインダー出し入れ／カード化／ゲイン」時の
白煙＋衝撃波VFX（`GIPokerAssets.BinderSmokeVfx`）再生と同時にSE（効果音）を鳴らせるようにする。
ただし対象音源はまだ用意できていないため、今回は**Inspector/Prefabから後日AudioClipを差し込めば
そのまま鳴るようにする配線のみ**を行う（音源が未設定の間は無音のまま、既存動作を一切変えない）。

## 受け入れ基準
- [ ] `GIPokerAssets` に、煙VFXと同時に鳴らすSE用の `AudioClip`（未設定＝null許容）と音量の置き場が増える。
- [ ] `GIPokerBootstrap` の Inspector から、上記AudioClip・音量を設定できる（`[SerializeField]`）。
- [ ] AudioClipが未設定（null）の場合、例外を投げず、SEは鳴らない（既存のVFXのみの動作を維持）。
- [ ] バインダー出し入れ（`BinderView.PlaySmokeVfx`）／カード化・ゲイン（`CardizableItemBase.PlayCardSizedSmokeVfx`）の
      **どちらも同じ煙VFX＋SEの組み合わせ**として鳴らす（3箇所とも同じクリップ・同じ音量設定を共有する）。
- [ ] SE再生はプロジェクトのRSGルール通り、コルーチンを使わない（`AudioSource.PlayClipAtPoint` のような
      ワンショット静的APIか、既存パターンに沿った実装でよい）。
- [ ] コンパイルが通り、既存EditModeテストがグリーンのまま。

## 変更対象
- `Assets/_Content/Scripts/GIPoker/GIPokerAssets.cs`:
  - `public static AudioClip SmokeSfxClip;`（未設定なら無音、コメントに「音源は後日追加予定」と明記）
  - `public static float SmokeSfxVolume = 1f;`
  - null安全なヘルパー `PlaySfxAtPoint(AudioClip clip, Vector3 position, float volume)`
    （`SpawnVfx` と同じ思想：clip null なら何もしない）を追加。
- `Assets/_Content/Scripts/GIPoker/GIPokerBootstrap.cs`:
  - `smokeSfxClip` (`AudioClip`) / `smokeSfxVolume` (`float`, 既定1f) の `SerializeField` を追加し、
    `Awake()` で `GIPokerAssets` に流し込む。
- `Assets/_Content/Scripts/GIPoker/BinderView.cs`:
  - `PlaySmokeVfx()` 内で、VFX再生に加えて `GIPokerAssets.PlaySfxAtPoint(GIPokerAssets.SmokeSfxClip, position, GIPokerAssets.SmokeSfxVolume)` を呼ぶ。
- `Assets/_Content/Scripts/GIPoker/CardizableItemBase.cs`:
  - `PlayCardSizedSmokeVfx()` 内で同様に呼ぶ。

## やらないこと（スコープ外）
- 実際のSE音源ファイルの追加・選定（今回は「後で設定できる状態」を作るだけ）。
- 3D/2D（`spatialBlend`）の細かいチューニング。`AudioSource.PlayClipAtPoint` の既定挙動に委ねる。
- ミュート設定・音量スライダーなどのユーザー向けUI追加。

## 制約
- 新規AudioClipは1種類のみ（バインダー・カード化・ゲインで共有）。個別に差し替えたくなった場合は
  将来的にフィールドを分ければよいが、今回は「同じ煙エフェクトなので同じSEでよい」という前提。
- コルーチン禁止、UnityEvent禁止（既存ルール通り）。

## 検証方法
- コンパイル確認（環境にUnity Editorが無い場合は静的レビューで代替し、その旨を明記する）。
- 可能であればEditModeで「`GIPokerAssets.SmokeSfxClip` が null のとき例外が出ない」ことを確認する軽量テストを検討。
- PlayMode/目視: Unity Editorが使える環境で、適当なテスト用AudioClipを一時的にInspectorへ設定し、
  カード化・ゲイン・バインダー出し入れの3箇所すべてでSEが鳴ることを確認する（本セッションでは未実施の可能性あり）。

## 実機確認が必要な項目
- 実際のSE音源が用意された後、展示環境（ノイズの多い会場）での聞き取りやすさ・音量バランス。
