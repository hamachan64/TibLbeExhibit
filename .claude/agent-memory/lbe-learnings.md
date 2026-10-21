# LBE & PICO Enterprise 開発の学習メモ

（Claudeが自律的にここに追記していきます）

## 現在の初期知見
- 本プロジェクトは PICO (PXR_Enterprise) を使用しており、コントローラーではなくハンドトラッキング専用。
- UniTask と R3 をベースに非同期・イベント駆動を構築している。
- Unityメタファイルの破壊を防ぐための Hook (`protect.sh`) が稼働中。

## TMP日本語フォントの豆腐（□）問題（2026-07-10 解決済み）
- 症状: PlayModeで「体験」「見習」など一部の漢字だけ□で表示される（かなや頻出漢字は正常）。
- 原因: `Noto Sans JP SDF` は Dynamic モードだが **1024x1024 の単一アトラス＋pointSize 90 で約130グリフが上限**。
  マルチアトラス（isMultiAtlasTexturesEnabled）が無効だと満杯以降の新規グリフが追加できず豆腐になる。
- 対策（適用済み）: マルチアトラス有効化＋アトラス 2048x2048 化＋ `ClearFontAssetData(true)` で再構築。
  変更はエディタAPI（SerializedObject / uloop-execute-dynamic-code）経由で行うこと。
- 注意: UIに新しい漢字を追加したら PlayMode で一度表示して豆腐が出ないか確認する。
  実機（PICO）ビルドでは動的グリフ生成のためソースフォント（.ttf）が同梱される必要がある → 実機での表示確認は必須。

## ゲームフロー状態機械（2026-07-10 拡張）
- SessionState: WaitingForStart（タイトル表示）→ Tutorial（固定60秒）→ Playing（5分）→ Finished（リザルト）。
- チュートリアルが固定時間なのは ContentStart RPC が全クライアント同報のため、
  Playing 開始時刻（＝シード付きスポーン開始）を全員で揃える必要があるから。進行度で短縮してはいけない。
- 新しい状態を足す場合、購読側は「特定状態かどうか」だけで判定する（switchの網羅に依存しない）と壊れない。
- 練習アイテム等を CardNetworkSync に同期させたくない場合は Register() を呼ばなければよい（NetworkId=-1 のまま）。

## サブエージェント運用（2026-07-10）
- git worktree 内のエージェントは Unity コンパイル不可。API実在確認は「該当ファイルを開いてシグネチャ確認」を義務付けると
  統合時のコンパイルが一発で通る（今回3PR・+1892行でエラー0件）。
- SendMessage でエージェントを transcript 再開すると worktree を離れてメインツリーで作業することがある。
  再開時は作業ディレクトリとブランチを必ず確認すること。

## MRパススルー用シェーダー（2026-07-12 Issue #7）
- パススルー合成の premultiplied / straight 規約差に依存しないためには、
  「黒(0,0,0)＋alpha蓄積（Blend の第2指定 `One OneMinusSrcAlpha`）」の影、
  「加算＋alpha も加算（`One One, One One`）」の発光、のように framebuffer alpha を必ず書くのが安全。
- radial dissolve は侵蝕度 0 のとき中心（dist=0）でフロンティア=0 となり縁の発光バンドが点として残る。
  `clip(erosion - ε)` ＋ 微小域のフェード係数を必ず入れること。
- グローバル値（`Shader.SetGlobalFloat`）は `CBUFFER_START(UnityPerMaterial)` に入れてはいけない
  （入れると SRP Batcher がマテリアル値として扱いグローバル更新が反映されない）。
- depth-only オクルーダーは URP の `DepthOnly` パス（`ColorMask R` / `return positionCS.z`）も
  併設し、Depth Texture 要求時の整合を取る。ShadowCaster パスは持たせない（現実メッシュの二重影防止）。

## TIB_20260429（会場スキャン）のオクルーダー化と空間アンカー不在（2026-07-12）
- TIB_20260429 に `ColorMask 0` の深度オンリーオクルーダーを適用すると、実機パススルーでは
  「現実の柱・壁の裏に回った仮想物を正しく隠す」ために機能するが、**Unityエディタ単体再生では
  色を一切描かないため背景色（グレー等）がそのまま透けて見える**。バグではなく仕様。
  エディタで元のテクスチャを見たい場合は `#if UNITY_EDITOR` プレビュー分岐の追加を検討する
  （本プロジェクトでは未実装、必要になったら追加）。
- **重要な未解決前提**: 本プロジェクトには「実機の現実空間とEnvironmentシーン（TIB_20260429含む）を
  位置合わせする空間アンカー/キャリブレーションの仕組みが存在しない**（CLAUDE.md には
  「空間アンカーなどの透明な基準ベース」という設計意図の記述はあるが未実装）。
  そのためオクルーダーが実機で正しく機能するかは未検証。PICOのパススルーがプラットフォーム側で
  深度オクルージョンを自動提供するかどうかも未確認。実機確認で位置ズレ・見た目の破綻が出た場合は
  TIB_20260429 のオクルーダーを `SetActive(false)` にする方針で合意済み（2026-07-12時点）。
  この判断はどちらの前提が真か実機で確認してから最終決定する。
- `Apply Occluder To TIB Mesh` のような「配下の全MeshRendererを機械的に対象とする」メニューは、
  会場ジオメトリではない配置基準マーカー（例: CenterPosition）も巻き込みうる。
  対象選定は名前パターンや専用タグで絞るか、適用後に非ジオメトリオブジェクトが混入していないか
  目視確認すること。

## TutorialView演出リッチ化とTMP新規漢字の回避策（2026-07-13 Issue #15）
- worktree内でUnityコンパイルができない状況でも「新しい漢字を追加しない」制約は守れる。
  やり方: `TitleView.cs` / `BinderView.cs` / `ResultView.cs` / `ResultGrade.cs` /
  変更前の `TutorialSequence.cs`・`TutorialView.cs` など、既に実機/PlayModeで描画実績のある View から
  `grep -ohoP '"[^"]*[぀-ヿ㐀-鿿][^"]*"'` で日本語リテラルを洗い出し、そこに含まれる漢字だけで
  新しい文言を組み立てれば、TMPアトラスへの新規グリフ追加ゼロを机上で保証できる。
  PR本文には「使用した漢字はすべて既存UI由来（新規グリフなし）」と明記すると豆腐リスクの
  説明責任を果たせる。**出典リストには編集対象ファイル自身の変更前文言も忘れずに含めること**
  （Issue #15 では「秒」「編」の出典が変更前 TutorialView のカウントダウン文言
  "本編開始まで {…}秒" であり、他Viewには存在しなかった）。
- ランタイム生成Viewで `CardVisualFactory.CreateLitMaterial`（毎回 new Material）を使う場合、
  生成マテリアルをリストに集約して `OnDestroy` で一括 `Destroy()` すること。
  LBEの連続稼働ではセッション毎にViewが再生成されるため、破棄漏れはセッションまたぎの
  漸増リークになる（Materialにも `DOKill()` 拡張があるので破棄前にTweenを止められる）。
- `Material.DOColor(Color, string/int propertyId, float duration)` や `Material.DOFade` /
  `Material.DOFloat` はこのプロジェクトの Demigiant/DOTween 同梱ソース（Modules配下の .cs）を
  grep しても定義が見つからない（コンパイル済み DOTween 本体DLL側にショートカット拡張が
  含まれているため）。実在確認は `Assets/Plugins/Demigiant/DOTween/DOTween.XML` を
  `grep -n "DOColor(UnityEngine.Material"` のように検索すると確実（既存コードの
  `StagePedestal.cs` / `BorderCircle.cs` が実例）。
- `TextMeshPro`（3D版）には `DOFade` 拡張が無い（UGUIのGraphic/Image限定）。
  代わりに `DOTween.To(() => tmp.alpha, a => tmp.alpha = a, target, duration)` を使う
  （`ResultView.FadeText` が既存パターン）。同様に色遷移も
  `DOTween.To(() => tmp.color, c => tmp.color = c, target, duration)` で代替できる
  （DOTween本体の `DOTween.To(DOGetter<Color>, DOSetter<Color>, Color, float)` オーバーロードは
  Modules非依存でコア機能として存在する）。

## UniTask × DOTween 連携の罠（2026-07-13 PR #16）
- 本プロジェクトの DOTween は DLL プラグイン（Assets/Plugins/Demigiant）であり UPM ではないため、
  UniTask 側の `UNITASK_DOTWEEN_SUPPORT` シンボルが定義されず `Tween.ToUniTask()` 等の
  `DOTweenAsyncExtensions` は**コンパイル不可**。ProjectSettings へのシンボル追加は worktree から
  行わない方針のため、Tween 完了待ちは `UniTask.WaitUntil(() => tween == null || !tween.IsActive())`
  のような Tween 非依存のポーリングで書く（キャンセル時は Tween を Kill してから例外伝播）。
- ランタイム生成マテリアルが `Shader.Find` で取得するシェーダーは、どのアセットからも参照されないと
  実機ビルドで stripping され null になる。`Resources` フォルダ配下に置いて同梱を保証すること
  （GraphicsSettings の Always Included は YAML 編集禁止のため使わない）。
- HLSL の `smoothstep(min, max, x)` は min > max の降順引数が undefined。PICO 等の GPU ドライバ差で
  見た目が破綻しうるため、必ず昇順＋`1.0h -` 反転の形で書く。

## プレイエリア座標系とスポーン配置のズレ（2026-07-13 docs/Tib.md対応）
- **重要**: GIPokerステージ一式（GIPoker_ItemSpawner / GIPoker_Stage配下のSpawnAnchors・ErosionPatches・
  LeyLines・ShadowCatcher / GIPokerStageDecoration配下の台座・BorderCircle・AmbientMotes）は当初
  **原点(0,0,0)付近の約4×4mクラスタ**として作られていた。一方、実際の利用可能プレイエリアは
  VRArea / Area_Floor で定義される **ワールド中心 X≈-4.9, Z≈-6.4、床Y=0、約4×18m の細長い回廊**
  （Area_Floor bounds: X[-6.98,-2.84], Z[-15.86,2.98]）。両者が重ならず、原点付近に置いたアイテムは
  エリア外に出現して「歩いても出会えない」状態になっていた。
- 対策として全ステージ要素を回廊内（幅方向は安全マージンを取り X∈[-6.3,-3.6]、Z=+1.3〜-14.3）へ
  18m全長に分散再配置した（docs/Tib.md「広い距離を歩けることを活かす」方針）。
  ItemSpawnManager のフォールバック箱 spawnAreaSize も (3,0,17) に拡大、position を回廊中心へ。
- **今後アイテム/装飾を追加・配置する際は、原点ではなくこの回廊フレーム（中心 -4.9,0,-6.4 / 4×18m）
  を基準にすること。** VRArea.MeshRenderer を一時 enable すると Scene で領域を可視化できる（Tib.md §今回の体験エリア）。
- StageSpawnAnchor の Anchor_01..04 は isElevated=true で4つの台座（RuinPillar×2/FloatingRock×2）の
  真上に乗っている。台座を動かす時は対応する elevated アンカーも一緒に動かすこと
  （Y: RuinPillar=1.10 / FloatingRock=1.04、2026-07-15に下記の高さバグ修正で値変更）。
  Anchor_05..30 は床アンカー(isElevated=false)。

## PICO実機ビルドでMRパススルー無効・ハンドトラッキング不動作（2026-08-01）
- 症状: PICO実機に`UserClient`をビルドすると、フルカラーパススルー（MR）が有効にならず、
  ハンドトラッキングも認識されない。
- 原因: `Assets/XR/Settings/OpenXRPackageSettings.asset` のAndroidビルドターゲット向け機能セットが、
  **Initial commit時点から一貫して「Meta Quest向け」のまま**だった
  （`MetaQuestFeature Android`のみ`m_enabled: 1`、`PICOFeature Android`＝PICO Support本体・
  `PassthroughFeature Android`＝`XR_FB_passthrough`・`HandTracking Android`＝
  `XR_EXT_hand_tracking`は全て`m_enabled: 0`）。本プロジェクトはPICO＋ハンドトラッキング専用のはずだが、
  対応するOpenXR機能が有効化されていなかったため、パススルー起動コード
  （STYLY XR Rig同梱の`PassthroughManager.EnablePassthroughAPI()`、`StylyXrRig.cs`から自動呼出）は
  存在するが`PassthroughFeature.isExtensionEnable`がfalseで実質no-opだった。
  `UserClient.cs`自体にはパススルー関連コードはなく、Enterprise初期化・Recenter・ジェスチャー無効化のみ。
- 検知方法: `git blame`で該当機能の`m_enabled`値がいつから0だったかを遡って確認（直近の
  `chore: Android向けXRビルド設定を更新`コミットはOpenXR Loader登録に伴うfileID再シリアライズ
  （diffが巨大化する）が主目的で、機能の有効/無効自体は変えていなかった）。
- 対策: `uloop-execute-dynamic-code`経由で`OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android)`
  → `settings.GetFeatures<T>()`（`T`=`Unity.XR.OpenXR.Features.PICOSupport.PICOFeature` /
  `PassthroughFeature` / `UnityEngine.XR.Hands.OpenXR.HandTracking`）でEditor API経由で
  `enabled = true`に変更（YAML直接編集は`.claude/rules/unity_asset_rules.md`で禁止）。
  `OpenXRFeature.nameUi`は`internal`でアクセス不可、ログには`GetType().Name`を使うこと。
  `MetaQuestFeature Android`など他機能には触れずピンポイントで3行のみ変更できた
  （`git diff`で`m_enabled: 0→1`が3ヶ所のみであることを確認）。
- **未解決の関連疑い**: 「ゲームが開始しない」症状は今回のハンドトラッキング無効化が絡む可能性が
  あるが未確定。もう一つの候補として、実機ビルドが`Assets/Settings/Build Profiles/UserClient.asset`
  （`UserClient.unity`+`Environment.unity`を正しく含む）経由ではなく旧来のFile > Build Settings
  （`ProjectSettings/EditorBuildSettings.asset`には`Environment.unity`のみ登録）経由で行われた場合、
  `UserClient.unity`自体がビルドから漏れ、両症状を同時に説明できる。実機再ビルド時は
  Build Profilesウィンドウで`UserClient`プロファイルが選択されているか必ず確認すること。

## uloop-execute-dynamic-code内のAssetDatabase.SaveAssets()が無関係アセットを巻き込む（2026-08-01）
- 症状: 上記OpenXR機能修正のスクリプト実行後、意図していない
  `M_Prop_Lantern.mat` / `M_Prop_Water.mat` の `m_ValidKeywords` から `_EMISSION` が消える差分が発生した。
  両マテリアルとも `_EmissionColor` は non-black（Lantern: {2, 1.5, 0.7}, Water: {0.05, 0.15, 0.2}）で
  `_EMISSION` があるのが正しい状態であり、これはEditor側の何らかの自動再検証（URPマテリアルの
  バージョンアップグレード処理等）でdirty化されていたものと推測される。
- 原因: 修正スクリプトが `AssetDatabase.SaveAssets()`（引数なし・プロジェクト全体対象）を呼んでおり、
  その時点でEditor内にdirty状態で残っていた無関係アセットまで一緒に保存してしまった。
  対象を絞った `EditorUtility.SetDirty(settings)` は正しいが、保存呼び出しが全体スコープだった。
- **今後のルール**: `uloop-execute-dynamic-code`でアセットを保存する際は、
  `AssetDatabase.SaveAssets()`ではなく対象アセットのパスを指定した
  `AssetDatabase.SaveAssetIfDirty(AssetDatabase.GetAssetPath(target))`（Unity 2021.3+で利用可）を使い、
  意図した1アセットだけを保存対象にすること。修正後は必ず `git status` で意図しないファイルが
  一緒に変更されていないか確認してからコミットする。
- **2026-08-03再発・原因の再整理**: `AssetDatabase.SaveAssets()`を使わず`EditorSceneManager.SaveScene()`
  （シーン単体の保存）だけを呼んだ場合でも、`M_Prop_Lantern.mat`/`M_Prop_Water.mat`の
  `_EMISSION`キーワード消失が再発した。つまり原因は「保存APIの呼び方（全体 vs 個別）」ではなく、
  **この2マテリアルがEditor起動時から常にdirty状態のまま放置されている**こと自体にあり、
  Unityの何らかの保存トリガー（シーン保存を含む）に巻き込まれて毎回書き出されてしまう。
  `SaveAssetIfDirty`で保存対象を絞ってもこの2ファイルの発火は防げないため、
  **uloopでシーン/アセットを保存する操作をした後は毎回 `git diff --stat` でこの2ファイルが
  紛れ込んでいないか確認し、混入していたら `git checkout --` で個別に差し戻す**運用を徹底すること
  （今回のPRでは2回連続で発生・2回とも差し戻し済み）。根本修正（なぜ常にdirtyなのか）は未調査。

## 実機ビルドは成功するのにハンドトラッキングが全く反応しない（2026-08-01 調査済み・未修正）
- 症状: OpenXRビルドエラー（PICO Touch Interaction Profile必須違反）を`HandInteractionProfile`有効化で
  解消し実機ビルド・インストールに成功した後、ハンドが一切トラッキングされず（ピンチ等のジェスチャーに
  完全に無反応）。
- 静的設定は全て正常だったことを確認済み（＝原因ではない）:
  - `OpenXRPackageSettings.asset`: Android向け`HandTracking`(XR_EXT_hand_tracking)・`PICOFeature`・
    `HandInteractionProfile`はいずれも`m_enabled: 1`。
  - `Assets/Resources/PICOProjectSetting.asset`: `isHandTracking: 1`。
  - `Assets/XR/XRGeneralSettingsPerBuildTarget.asset`: AndroidのLoaderは`OpenXRLoader`が正しく設定。
  - ビルド済みAPK（`Build/Build.apk`）の`AndroidManifest.xml`を`unzip -p ... | python3 (utf-16le decode)`で
    直接確認し、`com.picovr.permission.HAND_TRACKING`パーミッションと`handtracking`/`controller`の
    meta-dataが実際に注入されていることを確認済み（マニフェスト注入自体は成功している）。
  - `HandPinchTracker.cs`（`GIPokerBootstrap`から生成）は`SubsystemManager.GetSubsystems<XRHandSubsystem>`を
    ポーリングする実装で、コード上の明らかな欠陥は見当たらない。
- **真因（実機adbで確定）**: `com.picovr.permission.HAND_TRACKING`は`protectionLevel: dangerous`の
  **Android実行時（ランタイム）パーミッション**であり、マニフェストに宣言されていても自動では付与されない。
  実機接続中に
  `adb shell dumpsys package <applicationId>` を実行したところ、
  `requested permissions`には載っているが `runtime permissions: com.picovr.permission.HAND_TRACKING: granted=false`
  だった（同じdangerous permissionである`READ_MEDIA_IMAGES`等は付与済み＝OS側の個別許可管理が機能している）。
  加えて `grep -rln "RequestPermission\|HasUserAuthorizedPermission\|AndroidRuntimePermissions" Assets/_Content/Scripts`
  は0件で、**アプリ内でこのパーミッションをランタイムに要求するコードが存在しない**ことも確認済み。
  → OSがハンドトラッキング（カメラ由来のプライバシー系機能）へのアクセスを許可していないため、
  OpenXRランタイムが関節データを配信せず、`XRHandSubsystem`の`isTracked`が常にfalseになっていたと推測される。
- 検知に使ったコマンド（実機adb必須。`adb devices`でオンラインか確認してから）:
  ```
  adb shell pm list permissions -f | grep -A4 "com.picovr.permission.HAND_TRACKING"  # protectionLevel確認
  adb shell dumpsys package <applicationId> | grep -B2 -A2 "HAND_TRACKING"           # granted状態確認
  ```
- **未実施（次回の修正候補）**: `UnityEngine.Android.Permission.RequestUserPermission("com.picovr.permission.HAND_TRACKING")`
  等でアプリ起動時に明示的にランタイム許可を要求するコードを追加する必要がある（Unity標準API・カスタム
  パーミッション文字列でも動作する想定）。あるいはPICO SDK側に専用のヘルパーAPIがないか
  `Unity.XR.PICO.TOBSupport` / OpenXR PICOサポートパッケージを確認すること。追加後は必ず実機で
  `dumpsys package`のgranted状態を再確認してから「直った」と判断すること（ビルド成功≠権限付与）。
- 副次的に発見した未確定の疑い（今回の主因ではないが要確認）: `ProjectSettings/EditorBuildSettings.asset`
  （File > Build Settings）には`Environment.unity`のみ登録され`UserClient.unity`が無い。正しいシーン構成は
  `Assets/Settings/Build Profiles/UserClient.asset`（`UserClient.unity`+`Environment.unity`両方enabled）
  経由でのビルドのはず。今回の実機ビルドは`adb`ログ上`com.hamachan.tib`プロセスが実際に起動していたため
  致命的ではなさそうだが、Build Profileが正しく選択されているかは実機ビルドのたびに確認すること。
- **恒久対応（2026-08-01 実装済み）**: `UserClient.cs`の`Awake()`で`UnityEngine.Android.Permission`を使い
  `com.picovr.permission.HAND_TRACKING`をランタイムリクエストするコードを追加（`#if UNITY_ANDROID`ガード、
  `HasUserAuthorizedPermission`で既許可ならスキップ）。`PermissionCallbacks.PermissionDeniedAndDontAskAgain`は
  Unity側でobsolete（"Unreliable. Query ShouldShowRequestPermissionRationale and use PermissionDenied event."）
  なので使わないこと。
  - 検証: `adb shell pm grant <applicationId> com.picovr.permission.HAND_TRACKING` →
    `adb shell am force-stop <applicationId>` → 再起動、で`dumpsys package`上`granted=true`が
    プロセス再起動後も維持されることを確認済み（＝許可は再起動すれば有効化される可能性が高い）。
    ただし「初回起動時にダイアログが出た直後、OpenXRセッションが既に作成済みの状態でも動的に
    ハンドトラッキングが有効化されるか」は未検証。もし初回ダイアログ許可直後は反応せず、
    アプリの完全再起動（force-stop→再起動）が必要な場合は、展示設営時に
    「初回起動→権限ダイアログ許可→アプリ再起動」を運用手順に含めること。
  - コンパイルは`uloop-compile --force-recompile`でエラー0件・警告0件を確認済み。実機での
    ハンドトラッキング動作自体（ヘッドセット装着しての目視確認）はまだ未実施。

## EditModeテストでR3の型（ReadOnlyReactiveProperty等）を使うとCS0012になる（2026-08-01 解決済み）
- 症状: `TibLbeExhibit.Tests.EditMode` 配下の新規テストで `ReadOnlyReactiveProperty<T>.CurrentValue` 等を
  使うと `error CS0012: ... 'R3, Version=1.3.0.0...' を参照してください` でコンパイルエラーになる。
  Tests asmdef の `references` に `"R3.Unity"` を追加しても直らない。
- 原因: `R3.Unity.asmdef` は `autoReferenced: true` だが、Tests asmdef 側が
  `"overrideReferences": true` を設定しているため、Unity の「自動参照されるプリコンパイル済みDLL」の
  暗黙追加が無効化され、`R3.Unity` を references に足しても中身（実体は `R3.dll`）までは
  引き込まれない。`TibLbeExhibit.Content.asmdef` は `overrideReferences: false` なので
  `R3.Unity` 参照だけで動いており、比較すると原因が分かりにくい。
- 対策: `TibLbeExhibit.Tests.EditMode.asmdef` の `precompiledReferences` に
  `"R3.dll"`, `"Microsoft.Bcl.TimeProvider.dll"`, `"Microsoft.Bcl.AsyncInterfaces.dll"`
  （`R3.Unity.asmdef` 自身の precompiledReferences と同じ内容）を追加すると解決する。
  DOTween（`Assets/Plugins/Demigiant` の素のDLL、asmdef無し）は逆に "auto-referenced プラグインDLL"
  として `overrideReferences: true` でも自動では入らない可能性があるため、DOTweenの型をテストで
  直接使う場合も同様の確認が必要（今回はテスト側でDOTween型を直接参照しなかったため未検証）。
- 副次的な学び: `uloop-execute-dynamic-code` で `AssetDatabase.Refresh()` を呼んだり
  スクリプトを再コンパイルしたりすると、変更していないシーン（例: OperatorClient.unity /
  UserClient.unity）が isDirty になることがある。実際にYAML差分があるか `git diff` で必ず確認し、
  意図しない差分が無ければ保存して `run-tests` の「未保存の変更あり」ブロックを解除してよい。

## CFXR（Cartoon FX Remaster）のVFXが配線済みなのに何も描画されない（2026-08-02 解決済み）
- 症状: `GIPokerAssets.SpawnVfx` で正しいprefab参照を渡してInstantiateしているのに、
  シーン上に何も見えない。エラーもマゼンタ（シェーダー欠損の警告色）も出ない、完全な無描画。
  バインダー出し入れ（Lサインジェスチャー）の煙VFXで発生。カード化・ゲイン・消滅・リスキーダイス
  大吉/大凶など、他のCFXR依存VFXでも理屈上は同じ条件で起こり得る。
- 原因: `.cfxrshader`（`Assets/JMO Assets/Cartoon FX Remaster/CFXR Assets/Shaders/*.cfxrshader`）は
  カスタム `ScriptedImporter`（`CFXR_ShaderImporter.cs`）で、**インポート実行時点の
  `GraphicsSettings.currentRenderPipeline` を見てURP用/Built-in用のどちらか一方だけ**を焼き込み、
  結果を `.meta` の `detectedRenderPipeline` にキャッシュする（既定 `renderPipelineDetection: Auto`）。
  このマシンでは本プロジェクトのURP整備前にCFXRを取り込んだ形跡があり、`detectedRenderPipeline`が
  `Built-In Render Pipeline` のまま古いバイクで固まっていた。Built-in用ビルドには
  `LightMode=UniversalForward` パスが存在しないため、URPのForward Rendererは**エラーを出さず
  黙って描画をスキップする**（マゼンタにすらならない）。
- 重要: `Assets/JMO Assets/` は `.gitignore:119` で**フォルダごと完全に除外**されている。
  つまりこの `.meta` キャッシュはマシンローカルの状態であり、**gitで修正を配布できない**。
  新規クローンでゼロからインポートする分には、その時点で既にURPが `ProjectSettings`
  （こちらはgit管理下）に設定されているため `Auto` 検出で正しく焼かれるはずだが、
  「URP整備前にインポート済み → その後キャッシュが再評価されないまま残る」という
  履歴的な事故は他のマシンでも再現し得る。
- 対策（再発時）:
  1. 対象の `.cfxrshader.meta` を開き `detectedRenderPipeline` を確認する
     （`Universal Render Pipeline` であるべき）。
  2. `Built-In Render Pipeline` のままなら、`Assets/JMO Assets/Cartoon FX Remaster/CFXR Assets/Shaders/`
     をProjectウィンドウで選択し右クリック→Reimport（`renderPipelineDetection: Auto` のままなら
     現在のURP設定を正しく再検出して焼き直される）。
  3. 併せて `CardifyVfx` / `GainVfx` / `ExpireVfx` / `DiceBlessVfx` / `DiceCurseVfx` など、
     他のCFXR依存VFXも同じマシンで一度は目視確認しておくとよい（同じキャッシュ汚染の影響を
     受けている可能性があるため）。

## STYLY NetSync: アバタープレファブ未設定（ステルスモード）でも接続イベントは取れる（2026-08-03 PR #25調査）
- 「複数プレイヤーの接続一覧・スコアを集計する運営PC側スクリプト」を作る際、`NetSyncManager`に
  `_localAvatarPrefab`/`_remoteAvatarPrefab`を設定しない（＝ステルスモード。`OperatorClient.unity`が
  この状態）でも、`OnAvatarConnected`/`OnAvatarDisconnected`は正常に発火することを
  `MessageProcessor.cs`のソースで確認済み（`ProcessMessageQueue`内、"Announce connection
  regardless of avatar prefab" とコメントあり）。`AvatarManager.SpawnRemoteAvatar`は
  `remoteAvatarPrefab == null`で即returnするが、接続イベント自体はそれとは独立した経路
  （`_knownConnectedClients`ベース）で飛ぶため心配不要。
- 既に接続済みのクライアント一覧を後から取得するAPIとして
  `NetSyncManager.GetAliveClients(bool includeStealthClients = false)`（`int[]`を返す）が存在する。
  `LeaderboardTracker`のようなトラッカーを`Start()`で初期化する場合、`OnAvatarConnected`の購読を
  張るより前に既に接続済みだったクライアント（例: 運営PCアプリを日中に再起動したケース）は
  二度とイベントを受け取れず集計から漏れるバグになりやすい。**購読を張る前に必ず
  `GetAliveClients(includeStealthClients: true)`で現在の接続済み一覧を取得し、初期状態をシードする
  こと。** これはcode-reviewerエージェントによる指摘で発覚した（実装時点では見落とされていた）。
- `VariableManagerBase<TGlobal,TUser>.AsObservableOnChanged(TUserVariable)`は
  `Observable<UserVariableData<string>>`（型変換版は`<T>`ジェネリック）を返すR3ネイティブAPIで、
  `NetSyncManager.OnClientVariableChanged`（UnityEvent）を独自にラップし直す必要はない
  （`Library/PackageCache/com.styly.styly-netsync.utility@.../VariableManagerBase.cs`に実装済み）。
  ただし「変更時のみ」流れる仕様で、購読開始時点の現在値は流れない点に注意
  （初期値が要る場合は`Get<T>(variable, clientNo, default)`で明示的に補完する）。

## `STYLY Default Avatar A.prefab`のBodyTransformSolverが既にドリフトして無効化されている（2026-08-03 発見・未修正）
- `BodyTransformSolver`（`Library/.../Util Scripts/BodyTransformSolver.cs`）は現在のクラス定義で
  `netSyncAvatar`（`NetSyncAvatar`参照）フィールドを必須とし、`LateUpdate`冒頭
  `if (netSyncAvatar == null) return;`で即座に何もしなくなる。
  しかし`Assets/_Content/Prefabs/STYLY Default Avatar A.prefab`のシリアライズ済みYAMLには
  この`netSyncAvatar`フィールドが存在せず、代わりに現在のクラスには存在しない`head:`という
  古いフィールド名が残っていた（パッケージ側でフィールド名が`head`→`netSyncAvatar`+`groundCenter`に
  変更された過去のリファクタの影響で、プレファブ側が追従できていなかった）。
  つまり**このコンポーネントは今回発見する以前から実質的に無効（アバターのBodyメッシュが
  頭部に追従しない）だった可能性が高い**。`uloop-execute-dynamic-code`でプレファブに別の
  コンポーネントを追加してPrefabUtility経由で保存すると、Unityが現在のクラス定義に合わせて
  再シリアライズし、`netSyncAvatar: {fileID: 0}`（null）として可視化される。
- 対応: 今回のPR（#25、スコアモニター機能追加）ではスコープ外として据え置いた。
  次にこのプレファブを触る機会があれば、`netSyncAvatar`フィールドに同一GameObjectの
  `NetSyncAvatar`コンポーネントを設定し直し、Bodyメッシュが頭部に追従するか実機/PlayModeで
  確認すること。

## MRパススルーで「地中に沈める」だけでは非表示にならない（2026-08-06 PR #31で修正）
- 症状: `StagePedestal`（台座・浮遊岩）は`SessionState.WaitingForStart`中、`visualRoot`を
  地面より下に移動させる（`Sink()`）だけで非表示化していたつもりだったが、実際には
  待機中もずっと見えてしまっていた。
- 原因: パススルーMRには実世界の床による深度オクルージョンが無いため、Unity座標上で
  「地中」に位置していても、視錐台に入っていれば単純にレンダリングされてしまう
  （TIB_20260429オクルーダーの話とは別問題。あちらは深度オンリーオクルーダーの実機挙動、
  こちらはそもそもオクルーダーが存在しない場所で「位置を動かすだけ」の隠蔽が無効という話）。
- 教訓: **MRパススルー環境でオブジェクトを「隠す」場合、位置移動（沈める/遠ざける）だけでは
  不十分。必ず`SetActive(false)`かRenderer無効化などレンダリング自体を止める手段を併用すること。**
  半透明マテリアル側（`BorderCircle`のようにアルファ0までフェードする実装）は元々alpha=0で
  実質見えなくなるため問題にならない。問題になるのは不透明ジオメトリを「動かして隠す」設計。
- 同じ理由で、`GIPokerProps`（村/遺跡プロップ一式・木/岩/家/祭壇など）は元々どのフェーズにも
  連動しておらず常時アクティブだったため、待機中ずっと素通しで見えていた
  （バグというより「未実装」。`StageDecorationDirector`にフェーズ連動の`SetActive`トグルを追加して解決）。

## 固定ワールド座標だけで「複数プレイヤーが物理的に同じ場所を共有している」演出が作れる（2026-08-06 PR #31）
- チュートリアル用の円筒VR空間（`TutorialSpaceDirector`）を、従来の「各プレイヤーのカメラ中心に
  個別配置」から「全プレイヤーが1つの部屋を共有」に変更する際、新規のNetSync RPC・
  ネットワークオブジェクト化は一切不要だった。
- 理由: STYLY NetSyncは全クライアントのワールド座標系を共通の空間アンカーで揃える前提の
  アプリケーション（アバター位置がクライアント間で正しく同期して見えること自体がその証拠）。
  そのため、**各クライアントがローカルに生成するオブジェクトでも、同じ固定ワールド座標
  （ハードコードされた`Vector3`）に置きさえすれば、全員から見て「同じ物理的な場所」に
  存在しているように見える**。表示/非表示のタイミングも、既存の`ContentStart` RPC＋
  `GameSession`の固定時間ロジックで全クライアント間で既に揃っている。
- 応用: 今後「全員で共有する演出物」を作る要件が出た場合、まず「本当に位置同期RPCが必要か、
  それとも固定ワールド座標＋既存の状態同期タイミングだけで足りるか」を先に検討するとよい。
  後者で足りるケースは実装コストが大幅に小さい。
- 注意点: 床の高さ（Y座標）は「プレイヤーの目線から相対的に決める」実装（旧`PositionAtPlayer`の
  `floorBelowEye`）から「固定の実世界床レベル（Y=0、他のステージ要素と同じ基準）」に変更する
  必要があった。個別プレイヤー向けの演出を共有演出に転用する際は、個人差（身長など）に依存する
  相対配置ロジックが紛れ込んでいないか確認すること。

## 同一Unity Editorを複数セッション/エージェントが並行操作することがある（2026-08-06 PR #31）
- 症状: `unity-implementer`エージェントが単独でuloop経由の実装作業をしていたはずが、
  作業完了報告時、自分が触っていない複数ファイル（`BinderView.cs`/`PlayerNameTagView.cs`/
  `StageScoreMonitorView.cs`のフォントサイズ変更、新規サウンド`Fichish.mp3`/`Opening.mp3`）が
  同じUnity Editorのワーキングツリー上に出現していた。
- 原因（推測）: uloopはUnityエディタプロセス自体を操作するため、**別のClaude Codeセッション
  （ユーザー自身の別ウィンドウ、または別エージェント）が同じリポジトリの同じEditorインスタンスに
  対して並行してuloopコマンドを実行すると、両者の変更が同じワーキングツリー上で混ざる**。
  gitはブランチ単位で分離できるが、Unity Editorのプロセス・ワーキングツリーは1つしかないため
  ブランチを切っていても分離されない。
- 対応（CLAUDE.md既存ルールの再確認）: 無関係な未コミット変更を見つけても`git stash`や
  上書きはせず、`git diff --stat`で自分が触ったファイルとそれ以外を仕分けし、
  コミット時は自分のタスクに関係するファイルだけを明示的に`git add`すること。
  シーンファイル（`.unity`）のように無関係な変更と同じファイルに混在した場合は、
  `git diff`でハンク単位に自分の変更だけが含まれているか必ず確認してからステージする
  （今回は幸い`Environment.unity`/`UserClient.unity`とも自分の変更のみのクリーンな差分だった）。
- **今後の教訓**: 複数エージェント/セッションが同時に同一Unityプロジェクトを触る可能性がある
  運用では、作業開始前に「今このUnity Editorを他に触っている人/エージェントがいないか」を
  ユーザーに確認する、または`unity-implementer`エージェントへの指示に「作業開始時と完了時に
  `git status`を比較し、自分が触っていないファイルの変更があれば報告すること」を明記すると
  早期検知できる。

## Treasure Haul 用の構成整理で分かったこと（2026-10-15 repo/develop 作成時）
- **Unity 6 のビルドプロファイルがシーン一覧を上書きする**: `Assets/Settings/Build Profiles/*.asset` が
  `overrideGlobalScenes=true` だと、`EditorBuildSettings.scenes` の読み書きは**アクティブなプロファイル**に対して行われ、
  `ProjectSettings/EditorBuildSettings.asset` は使われない。`AdditiveSceneLoader` は名前で `LoadSceneAsync` するため、
  プロファイル未登録のシーンは `null` が返って NullReferenceException になる。
  新シーンを足すときはプロファイル（`BuildProfile.scenes`）に登録し、`BuildProfile.SetActiveBuildProfile` で切り替える。
- **アセンブリ分割はシーン参照を壊さない**: シーン/プレハブはスクリプトを GUID で参照する。YAML 内の
  `m_EditorClassIdentifier: <アセンブリ名>::<型名>` はエディタ用メタ情報なので、asmdef 名を変えても実害はない。
  ファイル移動・改名は `AssetDatabase.MoveAsset` / `RenameAsset` で行えば GUID が保たれ、git でも rename として記録される。
- **ガード（guard.py）の誤検知**: perl/sed のコマンド文字列に `.unity` などの拡張子が含まれると、
  対象が Markdown でもブロックされる。シーン名を含む文書の置換は Edit ツールで行う。
  また `uloop execute-dynamic-code --code-file` にスクラッチパッドのパスを渡すと guard が中身を読めずブロックするので、`--code` でインライン実行する。
- **シェル環境の注意（macOS / zsh）**: BSD sed は `\b` を解釈しない（単語境界の置換は `perl -pi -e`）。
  zsh では `echo ==` がコマンド展開エラーになり、`--include=*.cs` のような未クォートのグロブは「no matches found」で失敗する。
- **EditMode テストは全件成功が基準**: 以前は前作（GIPoker）のテストが24件失敗していた（GIPoker の調整後にテストが追従していなかった）。
  `repo/develop` では GIPoker を保守しないため、2026-10-18 に GIPoker のテスト15ファイルを削除し、テストの asmdef から GIPoker への参照を外した。
  `LSignPoseEvaluatorTests` は Core のテストなので残した。
  なお Assets 配下のファイル削除は、guard.py（シェルでの削除コマンド）と uloop の安全機能（`AssetDatabase.DeleteAsset`）の両方で止まる。削除が必要なときは人に頼む（ユーザーが自分のターミナルで `git rm` する）。

## エディタでの試作（Issue #36、2026-10-18）で分かったこと

### XRI 3.3.1「XR Interaction Simulator」サンプル
- 取り込んだだけでは、手の形のうち Grab / Thumb / Open / Fist の4つが動かない。プレハブの入力参照が、別サンプル「XR Device Simulator」の `.inputactions`（guid `b72ab2a4…`）を指しているため。
  - 回避: サンプル本体は編集せず、シーン上のインスタンスで4つの参照を `XR Interaction Hand Controls.inputactions`（Hands/Grab・Thumb・Open・Fist）に付け替える（`TH_Proto_Hands` はこの状態）。XR Device Simulator サンプルも取り込めば直る見込み（未検証）。
- 操作: Game View にキー入力が必要（`uloop simulate-keyboard` の KeyDown/KeyUp で届く）。コントローラー⇔手の切り替えは `[` と `]` の同時押し（Tab ではない）。手の形は単独キーで右手・Shift で左手、O=Open、P=Fist（押すたびにトグル）、M=Pinch、N=Poke。
- 手の関節は `XRDeviceSimulatorHandsProvider` が `XRHandSubsystem` として渡すので、実機と同じコードで読める。

### ParrelSync のクローンを AI から動かす
- `uloop launch <クローン>` で GUI 起動すると、ParrelSync の「Asset modifications saving detected and blocked」モーダル（ボタンは ok のみ）で止まり、uloop がつながらない（2回再現）。人が ok を押すか、`-batchmode -executeMethod`（`ProtoBatchRunner.PlayNet`）で起動する。batchmode では uloop・Game View が使えないので、クローンの操作は RPC で命令を送る形にした。
- このマシンは Library が約18GB（Bee 約11GB）・ディスク空き約17GB。`CreateCloneFromCurrent` で全コピーすると満杯になりうる。Library は Bee を除いて `cp -c -R`（APFS クローン）でコピーした。
- `NetSyncObject` の ObjectId の自動割り当ては、dynamic code での編集では発動しなかった（パッケージと同じ GlobalObjectId の FNV で `SerializedObject` から書いた）。

### STYLY NetSync 0.17.4 の実測（同じ Mac、localhost）
- RPC: 1クライアント 30回/秒（スライディング窓）を超えると捨てる（`Rate limited: dropped`）。`ConfigureRpcLimit` で上げられ、100回/秒でも欠損なし。順番の入れ替わり・重複はなし。自分の broadcast は自分にも届く。
- RPC の引数は JSON で 65,535 バイトまで（`(ushort)` で長さを持つ）。超えると**エラーも出ずに受信側の args が null** になる。設計上は 32KB 程度を上限にする。
- サーバーが落ちても `IsReady` / `HasServerConnection` は true のまま（切断に気づかない）。再起動後1秒以内に自動再接続するが、**ClientNo が入れ替わる**。停止中に送った RPC は溜まって、再接続後にまとめて届く。サーバーのグローバル変数は消える（クライアントの手元のキャッシュには残る）。
- グローバル変数: 後から入った端末も読める。書いた直後の Get は null（サーバー往復後に反映）。1024 文字を超えると `SetGlobalVariable` が false。
- DeviceId: `NetSyncManager.Instance.DeviceId`。相手のは `GetDeviceIdByClientNo` / `GetClientNoByDeviceId`。
- `NetSyncObject` の位置同期は、相手の画面で約 200〜300ms 遅れ、動き始めは約1秒遅れて数秒かけて縮む（内部の補間バッファ。外から変える設定なし）。RPC の片道は 4〜12ms。
- `RequestOwnership` は先着の調停をしない（他人が持っていても奪える。同時要求は後勝ち）。持ち主の決定はホストが行い、承認された1人だけが要求する。切り替えは約65ms。クライアントが終了すると持ち主は 0 に戻る。

## 基盤（Issue #37、2026-10-18）で分かったこと

### 進行の同期（ホスト＝運営PC、端末は状況報告 ThResync で登録）
- **運営PCだけ再起動すると、端末が報告し直すきっかけが無い**: 待機中は通し番号も RoundId も同じなので、通し番号の不一致では気づけない。`ThState` / `ThHeartbeat` に運営PCの起動値（hostEpoch）を載せ、端末は「今の起動値に対して自分が報告したか」を持つ（起動値ごとに必ず1回報告）。「起動値が変わったら報告」だけだと、運営PCより先に起動した端末が、他の端末への返信の `ThState` を受けただけで報告し損ねる（レビューで2回直した）。
- **ClientNo はイベントなしに入れ替わる**: サーバー再起動で端末どうしの ClientNo が入れ替わっても、接続・切断イベントは出ない（生きている番号の集合が変わらないため）。端末は自分の ClientNo が変わったら報告し直し、ホストは受け取るたびに `GetDeviceIdByClientNo` で送り手を引き直す。
- **`OnAvatarConnected` はステルスの運営PC（ParrelSync のクローンを含む）でも発火する**。端末の登録は、端末が送る報告（`ThResync`）だけで行う。
- **版が合わない端末と無限往復しうる**: 端末の「採用したら報告」とホストの「版が違えば ThParams」が、往復時間ごとに回り続けて RPC の上限を食い尽くす。端末・ホストの両方で〔再送間隔〕の制限に通す。
- 純粋クラス（`SessionHostCore` / `SessionClientCore`）に送信の口（`IThTransport`）を注入し、メモリ上の偽の通信でホスト1＋端末N を EditMode テストでつなぐと、取りこぼし・再起動・入れ替わりを数秒で再現できる。

### エディタ・ツール
- **NetSync のサーバー自動探索が、この Mac の localhost サーバーを見つけられないことがある**（Discovery timeout）。結合確認では、Play 中にリフレクションで `_serverAddress = "localhost"` にして `StartNetworking` を呼び直した（シーンは変えない）。
- **`persistentDataPath` に置いた一時ファイル（短縮パラメータ JSON など）は、guard.py が `rm` も `File.Delete` も止める**。結合確認の後片付けは人に頼む。短縮値で確かめたいときは、ファイルを置かずに済む方法（テストや dynamic code での上書き）を先に考える。
- 実装エージェントが結合確認の後に Play を止め忘れることがある。テストの前に `EditorApplication.isPlaying` を確かめ、何のシーンかを見てから止める。
- 既存の `LeaderboardTracker`（Core）は、NetSync が Ready にならないまま Play を止めると `EventManager.OnReadyAsObservable` の後片付けで NullReferenceException を出す（`NetSyncManager.Instance` が先に消えるため）。今回の変更とは無関係の既存の問題。

### GitHub Issue の本文をスクリプトで書き換えるとき（2026-10-18）
- `gh issue view --jq .body > file` → 加工 → `gh issue edit --body-file file` を for ループで回したところ、1件だけ取得が TLS タイムアウトで失敗し、**空のファイルで本文を上書きした**（#42）。`gh` が失敗しても、ループの次のコマンドは動く。
- 復旧: GraphQL の `issue.userContentEdits { editedAt diff }` に、過去の版の本文が丸ごと残っている。上書き直前の版を取り出して戻した。
- 対策: 取得した本文の長さを確かめてから更新する（空・極端に短いなら止める）。加工は「取得に成功したときだけ」動くよう、コマンドを `&&` でつなぐ。

## 配置・S を持つ・回収（Issue #38、2026-10-19）で分かったこと
- **取りやめ→再申請の間に `ThFreed` が挟まる**: ホストは「承認(申請1) → 取りやめ受理の ThFreed → 承認(申請2)」の順に返す。端末が `ThFreed` で無条件に「つかめる」に戻すと、承認待ちの申請2 まで消え、その後の承認(申請2)を「古い承認」と誤判定して取りやめを返してしまう。承認待ち（自分の申請中）の間の `ThFreed` は世代と置き位置だけ更新する。テストは間のメッセージも含めた実際の順番で流すこと（レビューで発見）。
- **Rigidbody の Interpolate と kinematic の transform 直書きは混ぜない**: 補間が transform を上書きするので、他端末の補間・DOTween の飛行は `interpolation=None`、物理で動かす追従・落下のときだけ Interpolate にする。
- **`Rigidbody.SweepTest` は、同じ Rigidbody に付いたトリガー（大きめの持ち手）を掃引に含めない**（実測。トリガー有無で距離が同じ）。持ち手を別 Rigidbody に分ける必要はない。
- **人とぶつからない処理でアバターのレイヤーを変えない**: `HandBone` は指で触れる判定用。アバターを移すと体で触れる判定が発火し、描画のレイヤーも変わる。`Physics.IgnoreCollision` でお宝ごとに外し、新しいアバターは `OnAvatarConnected` で拾う。
- **値札は近いと視界をふさぐ**: 手に持ったお宝の上の値札が目の前で巨大になった。一定距離より近いときは距離に比例して縮める。MR の手元 UI はスクリーンショットで必ず見る。
- **XR Interaction Simulator の手**: `[`+`]` で手のモードに入れないことがあった。`SimulatedDeviceLifecycleManager.SwitchDeviceMode()` とシミュレーター内部の手の状態をリフレクションで動かした。`Hands Smoothing Post Processor` は収束が遅いので確認時だけ無効化。Play のたびに `Assets/Samples/.../Unity_Hand_Medium.mat` が書き換わるので、コミット前に戻す。
- **MonoBehaviour はファイル名とクラス名を一致させる**: 2つ目のクラスを同じファイルに置いたら、シーンで Missing Script になった。

## 減額と表示（Issue #39、2026-10-19）で分かったこと
- **追従で障害物の 2mm 手前に止めても `OnCollisionEnter` は出る**（エディタで実測。床に押し付けて衝撃 1.2〜1.35m/s）。`ClampBySweep` から接触を取る代わりの方法は要らなかった。床のようにコライダーが重なっている所では、同じ瞬間に Enter が2件出る。ホストの組み合わせでの重複除去（0.3秒）でまとまる。
- **受信時刻で速度を出すと跳ね上がる**: `ThPoses` が同じフレームにまとめて届くと、受信時刻の差が数十マイクロ秒になり、2点の差から数百 m/s が出る（そっと触れただけで減額される）。速度は 0.1 秒ほどの幅の最古〜最新から出し、分母の下限を〔位置の送信間隔〕にして、〔追従速度の上限〕で切る。
- **ホストが覚える免除は、動いている状態のときだけ受ける**: 判定役は置かれた後も「最後の持ち主」として残る。そのため、遅れて届いた `exempt=true` で、置かれたお宝の免除が立ちっぱなしになり得る。Held / Falling 等のときだけ覚え、落としたときにも戻す。
- **DOTween の Sequence の中では入れ子の Tween の `SetDelay` が効かない**。開始時刻は `Insert(時刻, tween)` で指定する。
- **PlayMode でエディタを一時停止すると、ホストの Heartbeat が途絶えて端末が Waiting に落ちる**。画面を止めて撮りたいときは `Time.timeScale = 0` にする。

## M（両手で持つ。Issue #40、2026-10-20）で分かったこと
- **新しい RPC を足したら、`SessionHost` / `SessionClient` の RPC 購読の一覧にも足す**。`SessionHostCore` / `SessionClientCore` の `OnRpc` に分岐を足しただけでは届かない（`ThLift` / `ThLifted` で漏れた）。偽の通信でつなぐ EditMode テストでは購読の一覧を通らないので見つからず、エディタで ParrelSync のクローンと実際に通信して初めて見つかった。
- **エディタで両手を動かすときは、XR Interaction Simulator ではなく `HandGripTracker` の状態（握り・解除・手のひらの位置）をリフレクションで直接書く**と、2本の手を離して狙う・握る操作を時間どおりに再現できる。ただし `GripStateMachine`（しきい値・離す判定）は通らない。
  - 握ったままの手に、もう一度「握る」を書いてもイベントは出ない（`ReactiveProperty` は同じ値では通知しない）。先に「開く」を書いてから「握る」。
  - dynamic code では `Object` が `UnityEngine.Object` と `System.Object` で曖昧になるので、完全修飾で書く。
- **予約が認められた後に否認・取りやめで戻すときは、端末側の持ち主（`OwnerClientNo`）も -1 に戻す**。戻さないと、自分の予約の跡が残って、そのお宝を二度とつかめなくなる。
- **作業中に `TH_DiscoverGlow.mat` の `_Color` が `_BaseColor` と同じ値に書き換わることがある**（コードからは触っていない。URP がマテリアルを保存し直すときに古い `_Color` を合わせているとみられる。原因は未確認）。今回の作業と関係なければ、コミット前に戻す。

## 見失い・手の飛び・そっと置く・持ち主の切断（Issue #41、2026-10-20）で分かったこと
- **見失い待ちの間も、手（`HandGrabber.Held`）とお宝の結び付きを外さない**。外すと、待っている間にその手のビームが出て別の物をつかむ。M では、片手が「握って戻る → また見失う」と `Released(Lost)` がもう一度出るので、解除の処理で先に `Held = null` にしていると、その手だけ M から外れる（レビューで発見）。戻ったときに両手の `Held` を付け直す。
- **見失いから戻ったら、握りの判定を握っている状態へ戻す**（`GripStateMachine.Resume`）。「離す側のしきい値以上なら持ち続ける」のに判定が「握っていない」のままだと、後で手を開いても `Released` が出ず、お宝が手から離れなくなる。
- **戻った直後は指の関節が不安定**なので、「開いて戻った」を1フレームの強さで決めない（〔離す判定〕の間続いたら）。
- **手の飛びの判定は、見失っても前の位置を忘れない**。〔見失いの猶予〕（0.1秒）より短い見失いは `Released` にならないので、戻った最初のフレームを比べないと、離れた位置への一瞬の移動が補正なしの追従になる。
- **自分のお宝の免除は衝突の報告にも載せる**（`ThDamage.exemptA`）。`ThPoses` の `exempt`（1/30秒ごと）より先に衝突の報告がホストに届くことがある。ホストは送り手が a の判定役のときだけ信じる。
- **持ち主が切断したときの降ろす先は運営PCが決めて配る**。運営PCも Environment を読み込んでいるので Raycast できる。各端末で決めると、端末ごとに降ろす先がずれて `ThItems` の置き位置と食い違う。

## オープニング・保留・本編開始・結果（Issue #42、2026-10-21）で分かったこと
- **JsonUtility はメッセージの入れ子クラスに null を出せない**（読むと既定値のオブジェクトになる）。「無い」を表したいときは `present` のような bool を持たせる（`ThResultMsg`）。
- **保留のように「状態は同じで中身だけ変わる」ときも、状態の通し番号を +1 して `ThState` を送る**。端末は通し番号が合わない Heartbeat では時間を合わせず `ThResync` を出すので、`ThState` を取りこぼしても追いつける。Heartbeat の時間合わせだけで直そうとすると、取りこぼした間に残り秒がずれたままになる。
- **Heartbeat の時間合わせで残り秒は少し戻る**ことがある。残り秒から字幕などの「進む物」を決めるときは、戻さない（前の値との最大を取る）。戻すと同じナレーションが2回鳴る。
- **シーンの YAML の差分で、オブジェクトが消えたように見えることがある**（uloop で編集して保存すると、ブロックの並びが変わる）。差分の `-` だけで判断せず、新しいファイルに `m_Name:` が残っているかを数えて確かめる。
- 頭について来る面は XRI の `LazyFollow`（`UnityEngine.XR.Interaction.Toolkit.UI`）で作れる。`TibLbeExhibit.TreasureHaul.asmdef` に `Unity.XR.Interaction.Toolkit` の参照が要る。エディタの Game View は縦長で水平の視野が狭いので、面の幅の見え方は実機で確かめる。
- 毎フレーム `ThTextTable.Format` を呼ぶと、`params object[]` とボクシングで GC が出続ける。表示に関わる値が前のフレームと同じなら組み立てを飛ばす。

## 光る持ち手の廃止・高い壁でビームを止める（PR #59、2026-10-21）で分かったこと
- **太さなしの Raycast は、始点を内側に含むコライダーに当たらない**。仮想の壁は体で通り抜けられるので、手のひらが壁の中や壁の向こうにあると、手のひらから撃つ壁の判定が素通りする。`CheckSphere(手のひら)` と、肩から手のひらへの `Linecast` で別に確かめる（`HandGrabber.IsHandBehindWall`）。
- **SphereCast の `hit.distance` は球の中心が進んだ距離**で、触れた点より最大で半径ぶん手前の値になる。太さなしの壁の距離と比べると、薄い壁の裏に接した物が壁越しに選ばれる。候補ごとに、`ClosestPoint` で求めた点までの `Linecast` で壁を確かめる。
- **同じ Rigidbody に付けた子のトリガーは、`SweepTest(QueryTriggerInteraction.Ignore)` にも質量・慣性にも入らない**。つかむ範囲を実行時に子のトリガーとして足しても、持っている物の追従や衝突には影響しない。
- **背面法の輪郭は、角張った低ポリゴンのモデルを法線で押し出すと角に隙間が出る**。「メッシュの中心 → 頂点」の向きで押し出すと隙間が出にくい。ただし肉の薄い器では、内側の面が外へ出て筋が見えることがある。
- **uloop-execute-dynamic-code では `AssetDatabase.DeleteAsset` が `DangerousApiCall` として拒否される**。アセットを消す必要があるときは、人に Unity 上で消してもらう（迂回しない）。
- uloop で編集していると、関係のないマテリアル（例: `M_Prop_Lantern.mat` の `_EMISSION`）が再インポートで書き換わることがある。コミット前に `git status` で範囲外の変更を見つけ、`git checkout --` で戻す。

## 練習・色分け（Issue #43、2026-10-21）で分かったこと
- **STYLY のアバターのシェーダー（`Shader Graphs/Avatar`）には色の項目が無い**（`_thickness`・`_power` だけ。色は固定値と頂点カラー）。`MaterialPropertyBlock` だけでは塗れない。塗るときは同じ計算を再現した自作シェーダー（`TIB/AvatarTint`）へ塗る間だけ差し替える。**不透明の URP Lit に替えてはいけない**: 見た目が STYLY のアバターから変わり、MR では本物の人を覆う。なお元のアバターも、既定の `_thickness`=3 では中央が不透明（縁だけ透ける）。
- 手書きの URP シェーダーで元の Shader Graph を再現するときは、ShadowCaster パス（元が影を落とすなら）、ステレオの記述（`UNITY_VERTEX_OUTPUT_STEREO` ほか）、`UnityPerMaterial` の CBUFFER、透明なら `_SURFACE_TYPE_TRANSPARENT`、Unity 6 の柔らかい影のキーワード（`_SHADOWS_SOFT_LOW/MEDIUM/HIGH`）、`_BlendModePreserveSpecular` の扱いまで合わせる（独立レビューで指摘された）。
- `STYLY Default Avatar A 1` は `BodyTransformSolver.netSyncAvatar` が設定済みで、体は頭について動く（前の版 `A` の不具合は `A 1` では起きない）。
- 練習のように「Tutorial だけの表」と「本編の表」を分けると、切断時の後片付け（`ForceRestItemsOf`）や Result の取りこぼし（Tutorial → Result に飛ぶ）が片方の表にしか効かない漏れが出やすい。状態ごとに「どの表が生きているか」を必ず確かめる。
- 案内の段階を「持っているか」で決めるときは、手を開いた後の落下中・回収の承認待ちを「持っている」の続きとして扱わないと、成功の直前に1段目の案内がちらつく。
- uloop の `run-tests` は、コンパイル直後だと Domain Reload 中で失敗する（JSON でなく「Unity is reloading」を返す）。少し待って再実行する。
- 実装エージェントが ParrelSync 用に作ったクローン（`TibLbeExhibit_clone_1`）は、guard により `rm` で消せない。後片付けは人に頼む。
