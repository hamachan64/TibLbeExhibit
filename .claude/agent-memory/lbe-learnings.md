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

## 台座FBX刷新(ca13976)でアンカー高さが台座天板より浮いていたバグ（2026-07-15）
- `ca13976`（遺跡台座・浮遊岩をBlender製FBXモデルに刷新）で `VisualRoot` 配下のメッシュが
  プリミティブ組み立て→FBXへ差し替わり実測の天板高さが変わったが、
  `GIPokerStageDecorationSetup.cs` の `BuildRuinPillarPrefab`/`BuildFloatingRockPrefab` 内の
  `AnchorPoint.localPosition.y` は旧モデル基準の値（1.15 / 1.1）のまま据え置かれていた。
  結果、カード出現位置が台座の実際の天板より **RuinPillar約12.4cm・FloatingRock約13.3cm 上に浮く**
  見た目のズレが発生していた（コード中のコメントには新モデルの正しい高さ「天板上面≈y1.03」
  「草地上面+0.16(=0.96)」と明記されていたにもかかわらず、数値側が追従していなかった）。
- 検知方法: `VisualRoot`配下の全Rendererの合成bounds（world）とAnchorPointのworldPos.yを
  実機/PlayModeでなくエディタ上で`uloop-execute-dynamic-code`により直接比較すると、
  FBX差し替え後のズレはコンパイルエラーにもテストにも出ないため座標の実測比較でしか発見できない。
- 修正: `AnchorPoint.localPosition.y` を RuinPillar=1.10（天板1.03+浮遊余白0.07）、
  FloatingRock=1.04（草地上面0.96+浮遊余白0.08）に変更し、
  メニュー「GIPoker/Stage/Rebuild Pedestal Prefabs」でプレハブへ反映（シーンには触れない）。
  シーン側の Anchor_01/02(FloatingRock用)・Anchor_03/04(RuinPillar用) の worldPos.y も
  同じ値へ手動で追従させる必要がある（両者は別系統のオブジェクトで自動連動しないため）。
- **教訓**: 台座やアイテムの見た目モデルを差し替えるPRでは、「アンカー/接地点などモデル形状に
  依存する固定オフセット値」を横断的に検索し、コメントに書かれた新しい実測値と実際の設定値が
  一致しているか diff で確認すること。ビルドが通ってもテストが通っても、この種のズレは検出されない。

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

## ゲイン後のオブジェクトは元々「二度と掴めない」仕様だった（2026-08-01 仕様変更・実装済み）
- 発端: ユーザーから「ゲイン後のオブジェクト持てなくない？」と指摘。調査したところ
  `CardizableItemBase.CanBeGrabbed => state.Value == CardizableState.FloatingCard` のみで、
  `TryGain()`後の`Gained`状態には戻り経路がなく、`CardGrabInteractor`のピンチ掴み候補から
  恒久的に除外される実装だった（意図か考慮漏れか不明だったため設計判断をユーザーに確認）。
- `CardEffectService.cs`を確認した限り、剣の錬金やリスキーダイス等の効果は全て**ゲインの瞬間に
  一括発動**する実装（例: 剣はゲイン位置の半径2.5m以内のDランクを即座に錬金）で、ゲイン後に
  オブジェクトを持ち運んで使う効果は現状存在しない。なので「掴めない」ことによる効果面の不具合は無かった。
- ユーザーの要望: 「再び掴めるようにして。一度つかんだらそのあとは重力働くようにして」
  → ゲイン後は再グラブ可能にし、離したら物理的に落下する通常オブジェクトにする、という仕様に変更。
- 実装（`CardizableState`に`GainedHeld`を追加する形）:
  - `CanBeGrabbed`に`Gained`状態も追加。
  - `TryGrab()`: `Gained`から掴んだ場合は`GainedHeld`に遷移し、Rigidbodyを
    `isKinematic=true, useGravity=false`にして手の動きに追従できるようにする
    （`FollowPinchPose`のTransform直接操作と物理エンジンを衝突させないため）。
  - `Release()`: `GainedHeld`から離した場合は`Gained`に戻しつつ`isKinematic=false, useGravity=true`
    にして重力に委ね、素手タッチ/掴み判定用だった`Collider.isTrigger`も`false`に切り替えて
    床等と実際に衝突する物理オブジェクトにする（`DropWithGravity()`）。
  - `CardGrabInteractor.FollowPinchPose()`の「保持継続判定」を`Held`だけでなく`GainedHeld`も
    許容するよう修正（これをしないとゲイン後に掴んでも手に追従しない）。
- **既存コードとの整合性（変更不要だったことを確認済み）**:
  - `CardBinder.TryStore()`は`Held`/`FloatingCard`以外を弾くガードが既にあるため、
    `GainedHeld`なオブジェクトをバインダー圏内で離してもスコア用カードとして誤収納されない
    （＝ゲイン済みは二度とスコアにならない、というG.I.のルールと自動的に整合する）。
  - `GainGestureDetector.UpdateTear()`は`tearingCard.State.CurrentValue != Held`で毎フレーム
    ガードしているため、`GainedHeld`なオブジェクトを両手で引き裂こうとしても`Held`ではない
    ことで自動的にキャンセルされる（＝ゲイン済みの再ゲインは自然に不可能なまま）。
  - `CardNetworkSync.cs`の`OnLocalStateChanged`は`case CardizableState.Gained:`が
    prev状態を問わず`Rpc.GiGain`を送るため、`GainedHeld→Gained`の再遷移でも自動的に
    位置だけ再送される。リモート側の`TryGain()`は既に`Gained`状態のため呼んでも無音で失敗するのみ
    （二重ゲイン効果は発生しない）。ただし**重力で落下していくアニメーション自体はネットワーク
    同期されない**（既存の「掴んでいる間の位置は同期しない」制限と同じ）ため、リモートから見ると
    ドロップ直後の位置に静止して見える点は既知の限界として許容。
- 実機（ヘッドセット）での重力落下の見た目・地面への接地は未確認。落下先の環境コライダーとの
  当たり判定（レイヤー衝突マトリクス等）に問題があれば床を貫通する可能性があるため、実機確認時は
  ゲイン→再度掴む→離す→床に着地するか、を必ず目視確認すること。
