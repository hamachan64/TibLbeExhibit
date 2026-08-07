# チュートリアル空間の再デザイン・待機中ステージ非表示・BGM追加

## 目的
チュートリアル体験の前後を、企画（G.I.編モチーフ）の世界観に沿った完成度に引き上げる。
1. チュートリアル中に体験者全員を包む円筒VR空間を、添付リファレンス画像（HUNTER×HUNTER
   「グリードアイランド」の具現化装置演出）の雰囲気――紺〜青黒ベースに水色/白の発光ライン、
   縦長の光る窓ストライプ――へ寄せ、天井を高くして圧迫感を減らす。**個々のプレイヤーを
   個別に包む現状の設計をやめ、5〜6人が同時に1つの部屋を共有する演出にする。**
2. 開始待機時間（`SessionState.WaitingForStart`）中、MRステージの遺跡演出・村/森の
   プロップ一式が素通しで見えてしまっている状態を解消し、チュートリアル突入と同時に
   「具現化」される驚きを保つ。
3. チュートリアルBGM・開始SE・本編BGMを追加し、体験の音響面を仕上げる。

## 受け入れ基準
- [ ] `TutorialSpaceDirector` の円筒ルームが半径6m・高さ5mになっている。
- [ ] ルームは**各プレイヤーのカメラ中心ではなく、固定のワールド座標**に生成される
      （全プレイヤーが同じ物理的な1つの部屋の中にいるように見える）。床の高さも
      プレイヤーごとの目線基準ではなく固定の実世界床レベル（Y=0、既存プロップ配置と同じ基準）
      にする。
- [ ] 床・壁・天井が別マテリアル（色味・模様が異なる）で描画される。
- [ ] 壁面に、添付画像を参考にした縦長の発光ウィンドウストライプパターンが、既存の
      回路柄（Truchet迷路）に重ねて表示される。床・天井にはストライプは出ない。
- [ ] 全体の配色が紺〜青黒ベース＋水色/白の発光になっている（現状のアイボリー基調から変更）。
- [ ] Unlit・完全不透明（alpha=1書き込み）・`Cull Off` など、パススルーを確実に隠す
      既存の描画方針は変更しない。
- [ ] `SessionState.WaitingForStart` 中、Environmentシーンの `StagePedestal`（台座・浮遊岩）と
      `GIPokerProps`（木・岩・家・祭壇などの村/遺跡プロップ一式）が視界に入らない
      （PlayModeまたは実機で、待機中に該当オブジェクトが完全に見えないことを確認する）。
- [ ] `SessionState.Tutorial` に入った瞬間（＝ `StagePhase.Awakening`）、上記オブジェクトが
      白フェード演出と重なるタイミングで出現する（新規のポップイン対策演出を追加する必要はない。
      既存の `TransitionDirector` の白フェードで十分に隠れることを確認する）。
- [ ] `SessionState.Tutorial` に遷移した瞬間、`BGM_Tutorial.mp3` がループ再生される。
      音量は各StepのVoiceClip（Step0〜7.mp3）の聞き取りを妨げない小さめの値。
- [ ] チュートリアルのステップ1（Introステップ、Step1.mp3）の読み上げより前に、`Step0.mp3` が
      再生され、その再生完了を待ってからステップ1（Intro演出＋Step1.mp3）が始まる。
- [ ] `SessionState.Tutorial → Playing` に遷移した瞬間、以下の順で音が切り替わる:
      1. `BGM_Tutorial.mp3` を停止（フェードアウト）
      2. `Start.mp3`（開始の掛け声）を1回再生
      3. `Start.mp3` の再生完了後、`BGM_Play.mp3` をループ再生開始（本編BGM）
- [ ] `skipTutorial=true`（デバッグ用直行フロー）時や `Finished` への遷移時には
      `Start.mp3`/`BGM_Play.mp3` の上記シーケンスは発火しない（Tutorialを経て Playing に
      入った場合のみ）。

## 変更対象

### 1. チュートリアル空間デザイン（共有・単一ルーム化）
- `Assets/_Content/Scripts/GIPoker/TutorialSpaceDirector.cs`
  - `radius` を 4f → 6f、`height` を 3f → 5f に変更。
  - `PositionAtPlayer()`（`Camera.main`/`XrOriginLocator` 基準の毎回追従配置）を廃止し、
    固定のワールド座標に一度だけ配置する方式に変更する（`ShowAsync` の中で位置を
    セットする処理自体は残してよいが、値は固定にする）。
    - 床レベル: ワールド Y=0（`GIPokerProps` の配置基準・`Prop_*` の `Position.y=0` と同じ）。
    - X/Z: Environmentシーンの実測データから、プロップが東西の境界（X -2.75〜-7.05）へ
      寄せられ中央（X≈-4.9）が通路として空けられていることが分かっている
      （`GIPokerPropSetup.cs` のレイアウトコメント参照）。デフォルト値として
      `X=-4.9, Z=-0.5`（BigTree_A: (-6.55,0,-1.2) と BigTree_B: (-3.25,0,0.2) の中間、
      入口寄りの開けた場所）を仮置きする。**この値は実際の展示レイアウトに応じて
      現地調整が必要**なため、`[SerializeField] private Vector3 sharedRoomAnchor` として
      調整しやすい形にする。
    - 回転は不要（円筒は軸対称）。
  - `floorBelowEye` フィールドは共有ルームでは使わなくなるため削除する
    （床は固定Yになるため、プレイヤーごとの目線からの相対計算が不要）。
    `defaultEyeHeight` も同様に不要なら削除する（`XrOriginLocator` フォールバックが
    丸ごと不要になるため）。
  - サイズ変更に伴い、`BuildRoomMesh` を側壁・床・天井の **3つのサブメッシュ** に分けて
    生成するよう変更する（`mesh.subMeshCount = 3`、`SetTriangles(wallTris, 0)` /
    `(floorTris, 1)` / `(ceilingTris, 2)`）。
  - `MeshRenderer.sharedMaterials` に、壁用・床用・天井用の3マテリアルインスタンス（すべて
    `GIPoker/Tutorial/RoomWall` シェーダーを使うが、プロパティ値が異なる）を設定する。
  - `_Reveal` のDOTween登場/退場演出は3マテリアルすべてに対して同じ値を設定する
    （配列で保持し、ループで `SetFloat` する）。
  - 練習アイテム（`TutorialDirector.PlaceInFront`）は**変更しない**。共有ルームの中でも
    各プレイヤーが自分のカメラ正面に個別の練習アイテムを持つ現状の設計を維持する
    （5〜6人が同時に同じ場所へ練習アイテムを出すと衝突するため。練習アイテムは
    非同期のままでよい）。
- `Assets/_Content/Shaders/GIPoker/Resources/GIP_TutorialRoomWall.shader`
  - 配色を紺〜青黒ベースに変更（`_BaseColor` デフォルトを暗い紺、`_GlowColor` を水色に）。
  - 縦長の発光ウィンドウストライプ用の新規プロパティを追加し、既存のTruchet迷路発光に
    加算合成する:
    - `_StripeColor`（Color、明るい水色〜白）
    - `_StripeIntensity`（Range 0-1、既定0。壁マテリアルのみ1に設定して有効化。
      床・天井マテリアルは0のままで無効化する）
    - `_StripeDensity`（円周方向のストライプ列密度、m単位）
    - `_StripeWidth`（Range 0-1、各列内でのストライプ幅比率）
    - `_StripeRowDensity`（縦方向の区切り密度。列ごとにhashで点灯/非点灯・明滅位相をランダム化し、
      添付画像のように高さの異なる短冊が不規則に光る見た目にする）
    - 実装方針: `input.uv.x * _StripeDensity` を列IDとして `hash21` で列ごとの点灯有無・輝度を決め、
      `input.uv.y` 側も同様に区切って縦方向の点灯/非点灯セグメントを作る。既存の `emission` に
      `stripe * _StripeIntensity` を加算し、`col` にも `_StripeColor.rgb * stripe` を加算する。
  - マテリアルごとのパラメータ差でも見た目を作れるよう、以下の目安値を出発点にする
    （最終値は見た目確認しながら実装エージェントの裁量で微調整可）:
    - 壁: `_BaseColor`≈(0.03,0.05,0.10) / `_GlowColor`≈(0.55,0.85,1.0) / `_StripeColor`≈(0.85,0.97,1.0) /
      `_StripeIntensity`=1 / `_Tiling`≈1.8 / `_Emission`≈1.6
    - 床: `_BaseColor`≈(0.02,0.03,0.06) / `_GlowColor`≈(0.4,0.7,0.95) / `_StripeIntensity`=0 /
      `_Tiling`≈2.4（細かめのパネル目地）/ `_LineWidth`≈0.04 / `_Emission`≈1.0（控えめ、直視時に眩しくしない）
    - 天井: `_BaseColor`≈(0.04,0.06,0.11) / `_GlowColor`≈(0.5,0.8,1.0) / `_StripeIntensity`=0 /
      `_Tiling`≈1.2（粗め）/ `_Emission`≈0.9（控えめ、見上げても圧迫感を出さない）
  - 完全不透明・Unlit・`ZWrite On`・`Cull Off` の描画方針は変更しない（パススルー遮蔽の生命線）。
- ネットワーク同期について: 部屋の出現/消滅タイミングは、既存の `ContentStart` RPC ＋
  `GameSession` の固定時間ロジックで全クライアントが既に揃っている。位置についても、
  STYLY NetSyncのアプリは全クライアントが共通の空間アンカーでワールド座標系を
  揃えている前提のため、**固定のワールド座標を全クライアントで同じ値にする**だけで
  「物理的に同じ部屋を共有している」状態が実現できる。新規RPC・ネットワークオブジェクト化は
  不要（既存の `TutorialSpaceDirector.Create()` によるローカル生成方式のまま、位置決めロジックだけ
  「自分のカメラ基準」から「固定ワールド座標基準」に変える）。

### 2. 待機中のステージ非表示
- `Assets/_Content/Scripts/GIPoker/StagePedestal.cs`
  - 現状 `Sink()`/`Awake()` は `visualRoot` を地中相当の位置に動かすだけで、
    `SetActive(false)` 等の非表示化をしていない。パススルーMRには実世界床による
    オクルージョンが無いため、沈めても実際には見えてしまう。
  - `Awake()` 時点で `visualRoot.gameObject`（またはその配下のRendererすべて）を
    非アクティブ化してから初期位置を設定する。
  - `Sink()` 完了時（トゥイーンの `OnComplete`）に非アクティブ化する。
  - `Rise()` 開始時、アニメーション開始前に再アクティブ化する（沈んだ状態のまま
    非表示→アクティブ化→迫り上がりトゥイーン、の順で見た目が破綻しないようにする）。
  - `EnterPhase(Reality/Awakening, immediate: true)` 経路（シーンロード直後の初期状態合わせ）でも
    確実に非アクティブになるようにする。
  - `GroundHalo`（`groundHalo` Renderer）は既存どおりアルファフェードで制御されており
    透過マテリアルのため、こちらは変更不要（アルファ0で実質見えない）。
- `Assets/_Content/Scripts/GIPoker/StageDecorationDirector.cs`
  - `GIPokerProps`（Environmentシーン内、木・岩・家・祭壇などのプロップ一式ルート。
    `GIPokerPropSetup.cs` が生成する固定シーンオブジェクト）への参照を
    `[SerializeField]`（未設定時は `GameObject.Find("GIPokerProps")` 等でフォールバック）で追加し、
    `ApplyPhase` 内で `StagePhase.Reality` のときは非アクティブ、それ以外では
    アクティブにする単純なトグルを追加する（immediate/非immediateいずれも即時切り替えでよい。
    `StagePedestal` のような滑らかな迫り上がり演出は不要。理由: `WaitingForStart→Tutorial` の
    遷移時は既存の `TransitionDirector` の白フェードアウト〜フェードインが約1.6秒あり、
    `GameSession.State` の変化（フェード開始とほぼ同時）に連動する本トグルは
    その白フェード中に隠れるため、追加のポップイン対策演出は不要と判断する。加えて、
    Tutorial中はプレイヤーが共有ルームの内部にいるため、ルーム自体の壁がこれらのプロップを
    視覚的に覆い隠す＝二重に隠蔽される）。
  - `GIPokerProps` 配下のオブジェクトが `SetActive(false)` になっても、`StageSpawnAnchor` 等の
    既存コンポーネントの `Awake`/`Start` はシーンロード時に一度実行済みのため、
    非アクティブ化による初期化への悪影響はない（アイテムのスポーン自体は
    `ItemSpawnManager` が別途 `SessionState.Playing` をガードしているため、
    プロップの非表示化とスポーンロジックは独立している）。

### 3. チュートリアルBGM・開始SE・本編BGM
- 音声ファイルのインポート:
  - `Assets/_Content/Sounds/BGM_Tutorial.mp3`, `Assets/_Content/Sounds/Start.mp3` は
    `.meta` が存在せず未インポートのため、uloop経由で `AssetDatabase.ImportAsset` /
    `AssetDatabase.Refresh()` を実行し `.meta` を生成させる（既存Step1〜7.mp3と同等の
    インポート設定に合わせる）。
  - `Assets/_Content/Sounds/Step0.mp3` は `.meta` 済みだが未使用のため、コード側で
    新規に結線する。
  - `Assets/_Content/Sounds/BGM_Play.mp3` は既にインポート済み（`.meta`あり）だが
    コード内で未使用のため、新規に結線する。
  - `Assets/_Content/Sounds/警官のホイッスル2.mp3` は今回のスコープ外（下記「やらないこと」）。
- `Assets/_Content/Scripts/GIPoker/GIPokerAssets.cs`
  - `public static AudioClip TutorialIntroVoiceClip;`（Step0.mp3）
  - `public static AudioClip TutorialBgmClip;`（BGM_Tutorial.mp3）
  - `public static AudioClip SessionStartVoiceClip;`（Start.mp3、掛け声）
  - `public static AudioClip MainBgmClip;`（BGM_Play.mp3、本編BGM）
  を追加（既存の `TutorialStepVoiceClips` と同じ「null許容の共有アセット置き場」パターン）。
- `Assets/_Content/Scripts/GIPoker/GIPokerBootstrap.cs`
  - 上記4つに対応する `[SerializeField] private AudioClip ...;` を追加し、起動時に
    `GIPokerAssets` へ流し込む（既存 `tutorialStepVoiceClips` の結線と同じ書き方、
    `Assets/_Content/Scripts/GIPoker/GIPokerBootstrap.cs:57,70` 付近を参照）。
- Step0.mp3（Introの前振り音声）:
  - `Assets/_Content/Scripts/GIPoker/TutorialDirector.cs` の `BeginStepProgression()` を
    `UniTask` 化し、`tickSubscription` の開始・`OnStepEntered()`（＝ステップ1/Step1.mp3の再生開始）
    より前に `GIPokerAssets.TutorialIntroVoiceClip` を再生し、その完了を待ってから
    ステップ進行を開始する。
    - 再生・完了待ちは `TutorialView` 側に薄いメソッド（例: `PlayPreambleAsync(AudioClip, CancellationToken)`）
      を追加し、既存の `voiceAudioSource`／`IsVoiceFinished` の仕組みを再利用する形で実装する
      （新しいAudioSourceを増やさない）。
    - クリップが `null`（未設定）の場合は待たずに即座に通常のステップ進行へ進む
      （展示で詰まらせない既存方針を踏襲）。
    - `tutorialActive` が途中で `false` になった場合（Cleanup発生時）は、待機中でも
      安全に中断できるようにする（`destroyCancellationToken` 等を使う）。
- 新規クラス `Assets/_Content/Scripts/GIPoker/TutorialAudioController.cs`
  - `GameSession` を受け取り `session.State` を購読する軽量コンポーネント
    （`TransitionDirector`/`StageDirector` と同じ「`previousState` を見て実際の遷移だけ処理する」
    パターンに倣う。ReactivePropertyは購読直後に現在値を1回流すため、初回は
    `previousState` 確定のみで演出しない）。
  - 内部に2D再生（`spatialBlend = 0`）の `AudioSource` を用途別に用意する
    （BGM用に1つ、単発SE用に1つ、の計2つで足りる。`TutorialView.voiceAudioSource` と同じ方式）。
  - `previous != Tutorial かつ next == Tutorial`: `TutorialBgmClip` を `loop = true` で
    再生開始。音量は `[SerializeField] private float tutorialBgmVolume = 0.15f;` 程度
    （Step音声より明確に控えめ）で調整可能にする。
  - `previous == Tutorial かつ next == Playing`: 以下を順に行う（`UniTask` で連結する）。
    1. Tutorial BGMを短時間（0.4〜0.6秒程度、DOTweenで `AudioSource.volume` を0へ）
       フェードアウトして停止する。
    2. `SessionStartVoiceClip`（Start.mp3）を再生し、再生完了を待つ。
    3. `MainBgmClip`（BGM_Play.mp3）を `loop = true` で再生開始する
       （`[SerializeField] private float mainBgmVolume` で音量調整可能にする。既存の
       `BGM_Play.mp3` が置かれていた経緯は不明だが、これも展示のノイズ環境を考慮した値にする）。
  - それ以外の遷移（`skipTutorial` によるWaitingForStart→Playing直行、Finishedへの遷移等）では
    上記シーケンスを発火しない。ただし安全側として、コンポーネント破棄時やセッション終了時に
    鳴りっぱなしのBGMがあれば停止するガードは入れる（連続稼働のLBEでセッションを跨いで
    音が残らないようにする）。
  - `GIPokerBootstrap.cs` から他のディレクター（`TutorialDirector`, `TransitionDirector` 等）と
    同様に生成・`Init(session)` 呼び出しを追加する。

## やらないこと（スコープ外）
- `Assets/_Content/Sounds/警官のホイッスル2.mp3` のインポート・使用（今回の指示に含まれないため。
  ファイルはリポジトリに残るが本タスクでは触らない）。
- `GIPokerProps` の侵蝕度連動の段階的フェード演出（今回は表示/非表示の単純トグルのみ。
  凝った出現エフェクトが必要なら別タスクとする）。
- チュートリアル空間の位置決め以外のネットワーク同期化（練習アイテムなど）。練習アイテムは
  既存どおり非同期・カメラ相対のまま。
- リスキーダイス等、ゲームロジック本体（役・スコア・ゲイン制限等）の変更。
- `StageScoreMonitor`（スコアボード表示）まわりの変更（今回「ステージ」として扱うのは
  `StagePedestal`・`GIPokerProps` のみとする）。
- 共有ルームの固定アンカー座標の最終確定（現地の実際のプレイヤー集合位置に合わせた
  微調整）。今回はコード上デフォルト値を仮置きし、`[SerializeField]` で調整可能にするところまで。

## 制約
- 規約: UniTask / R3 / DOTween、ハンドトラッキング専用、`.unity`/`.prefab`/`.asset` は
  uloop経由で編集。
- シェーダー変更後も、壁面が完全不透明・alpha=1書き込みでパススルーを確実に隠す方針
  （`.claude/agent-memory/` のMR知見）を崩さないこと。
- `GIPokerProps` 非表示化は `SetActive` によるシンプルな実装に留め、`StagePedestal` のような
  DOTweenアニメーションは付けない（スコープ外の項で明記した理由による）。
- 共有ルームの位置は固定ワールド座標のみで実現し、新規のNetSync RPC・ネットワークオブジェクトは
  追加しない（既存の空間アンカー整合性に依拠する）。

## 検証方法
- EditModeテスト: 今回の変更はビジュアル・アセット結線が中心でロジック分岐が少ないため、
  既存のEditModeテストスイートが壊れていないことを確認する。`TutorialAudioController` の
  「previous/next state による分岐（Tutorial→Playingのみ発火）」はロジックとして
  切り出せるため、軽量なEditModeテストを追加する。
- コンパイル確認、`gipoker-verify` スキルの手順に従う。
- PlayMode/目視:
  - エディタ自動開始（`autoStartInEditorDelay`）でWaitingForStart→Tutorial→Playingの流れを再生し、
    各フェーズでStagePedestal/GIPokerPropsの表示状態、ルームの見た目、
    Step0→Step1の音声順序、BGM_Tutorial→Start.mp3→BGM_Playの音声切り替わりタイミングを
    ログ・スクリーンショットで確認する。
  - 円筒ルームのスクリーンショットを、床・壁・天井それぞれ写る角度で撮影し、
    添付リファレンス画像と見比べて雰囲気が近いか確認する。
  - ParrelSyncで2クライアントを起動し、両クライアントで円筒ルームが同じワールド座標
    （同じ実座標）に出現することを確認する。

## 実機確認が必要な項目
- 共有ルームのアンカー座標（デフォルト `X=-4.9, Z=-0.5, Y=0`）が、実際の展示レイアウトで
  5〜6人が集まる場所として適切か。現地で `TutorialSpaceDirector.sharedRoomAnchor` を
  調整する前提。
- パススルーMR実機での円筒空間の見え方（色味・発光の強さ・天井高さ5m・半径6mの体感、
  ストライプ模様の視認性）。エディタのGame Viewでは光量やコントラストの見え方が
  実機と異なる可能性がある。
- BGM_Tutorial.mp3/BGM_Play.mp3とStep音声（Step0〜7.mp3）の実機での音量バランス
  （`tutorialBgmVolume`/`mainBgmVolume` の初期値が適切かは実機のスピーカー/展示会場の
  ノイズ環境で要調整）。
- WaitingForStart→Tutorial遷移時、GIPokerProps/StagePedestalの出現が白フェードで
  完全に隠れているか（PICO実機のフレームレート次第でフラッシュが見える可能性）。
- 実機の複数台構成（PICO複数台）で、共有ルームが本当に同じ物理位置に見えるか
  （空間アンカーのキャリブレーション精度に依存する）。
