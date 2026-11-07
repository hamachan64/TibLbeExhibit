# 開発情報 (Development Guide)

本ドキュメントでは、本プロジェクトの開発に用いる主要なライブラリ、アーキテクチャ、およびコーディング規則について説明します。

## 主要ライブラリ・フレームワーク

プロジェクト（`Packages/manifest.json` に記載）では以下の重要なライブラリが利用されています。

### 1. 非同期処理・リアクティブプログラミング
- **UniTask (`com.cysharp.unitask`)**: 
  Unityのコルーチンに代わる、高速でアロケーションフリーな非同期処理フレームワークです。非同期メソッドは基本的に `async UniTask` を使用します。
- **R3 (`com.cysharp.r3`)**: 
  UniRxの後継となる次世代のC#リアクティブプログラミングライブラリです。イベント駆動な処理や状態の監視（`Observable`）にはこちらを使用します。

### 2. ネットワーク・同期
- **STYLY NetSync (`com.styly.styly-netsync` / `utility`)**:
  複数ユーザー間での体験同期（位置や状態など）を管理するためのSTYLYのパッケージです。
- **Multiplayer Center (`com.unity.multiplayer.center`)**:
  Unity公式のマルチプレイヤー関連パッケージ。

### 3. XRプラットフォーム
- **PICO Unity OpenXR SDK**:
  PICOデバイス向けのVR/MR対応パッケージ。フルカラーパススルー（Seethrough）の設定に利用します。
- **PXR_Enterprise**:
  展示用途として、ジェスチャー認識のオフやシステムリセットの無効化など、デバイスの特殊な制御を行うために使用されています。
- **Meta OpenXR (`com.unity.xr.meta-openxr`)**:
  Meta Quest向けの対応パッケージ（Passthrough機能含む）。
- **XR Interaction Toolkit (`XRI`)**:
  XR環境下でのインタラクション（つかむ、UI操作など）に使用されます。

### 4. アニメーション・演出
- **DOTween (`Assets/Plugins/Demigiant`)**: 
  オブジェクトの浮遊やUIの展開など、コードベースのアニメーション制御（Tween）に利用します。

### 5. その他・ユーティリティ
- **Addressables (`com.unity.addressables`)**: アセット管理・非同期ロード用。
- **ParrelSync (`com.veriorpies.parrelsync`)**: 
  エディタ上で複数クライアントを同時に立ち上げてテストするためのツール。ローカルでのマルチプレイヤー同期の検証に使用します。

## シーン構成と役割分担
プロジェクトは以下の3つのシーンに分割されており、`AdditiveSceneLoader.cs` によってマルチシーンとして結合されます。

| シーン名 | 保存先 | 主な役割 |
| :--- | :--- | :--- |
| **TreasureHaulEnvironment** | `Assets/_Content/TreasureHaul/Scenes/` | 共有空間（環境光、Shadow Catcher、境界、スコアモニター、ゲームの舞台） |
| **TreasureHaulUserClient** | `Assets/_Content/TreasureHaul/Scenes/` | 体験者用XR Rig、ハンドトラッキング操作ロジック、`GameSession` |
| **TreasureHaulOperatorClient** | `Assets/_Content/TreasureHaul/Scenes/` | 運営用PC向けUI、システム制御、開始トリガー、リーダーボード |

- 追加ロードするシーンは名前で解決されるため、**アクティブなビルドプロファイルのシーン一覧に登録されている必要がある**（Unity 6 のビルドプロファイルがグローバルのシーン一覧を上書きする）。
  Treasure Haul 用は `Assets/Settings/Build Profiles/TreasureHaulUserClient.asset` / `TreasureHaulOperatorClient(mac).asset`。
- `Assets/_Content/Scenes/` の Environment / UserClient / OperatorClient は前作（具現化ポーカー）用。変更しない。

## ディレクトリ構成
- `Assets/_Content/Scripts/`: 共通基盤（`TibLbeExhibit.Core`）
  - `UserClient.cs`: 体験者向けの機能やPICOエンタープライズの初期化・制御
  - `OperatorClient.cs`: オペレーター（運用者）向けの機能
  - `VRArea.cs`, `ViewManager.cs`: エリア管理やビュー（UI）の管理
  - `System/`: RPC・共有変数・シーン追加ロード・リーダーボード
  - `System/XR/`: 手の判定（指先の接触、ピンチ、ハンドサイン）
  - `System/Session/`: セッション進行（`GameSession`）
  - `System/View/`: 画面フェードなど共通の表示
  - `GIPoker/`: 前作のゲーム固有コード（`TibLbeExhibit.GIPoker`）。新作からは参照しない
- `Assets/_Content/TreasureHaul/`: 新作のゲーム固有コード・シーン（`TibLbeExhibit.TreasureHaul`、Core のみ参照）
- `Assets/_Content/Shaders/Common/`, `Assets/_Content/Materials/Common/`: コンテンツ共通のシェーダー・マテリアル（シェーダー名は `TIB/*`）

## 開発・実装上のルール
- **言語・命名規則**: C#の標準的な命名規則に従います。クラス・メソッドはPascalCase、変数はcamelCaseを推奨します。
- **非同期処理の統一**: コルーチンではなく、極力 `UniTask` を使用してください。
- **イベント処理の統一**: UnityEventや標準のC#イベント（`event Action`）よりも、`R3` の `Observable` を用いたリアクティブな設計を推奨します。
- **アニメーション制御**: オブジェクトの移動やUIのフェードなどの演出処理は、Updateメソッド内で計算するのではなく、極力 `DOTween` を使用してください。
- **展示デバイスの制御**: デバイスに依存する処理（PICO Enterprise API等）を呼び出す場合は、他プラットフォーム（EditorやMeta Quest）でエラーにならないよう、適切なプラットフォーム分岐やコンパイルディレクティブを考慮してください。

## git に入れていない素材
リポジトリは公開なので、次の画像は git に入れず、作業する Mac にだけ置く（2026-11-03 のユーザー判断）。`.gitignore` で除いている。

| 素材 | 置き場所 | 使い道 |
| :--- | :--- | :--- |
| タイトルロゴの元画像・キービジュアル | `Artwork/TreasureHaul/`（`TreasureHaul_title_logo.png`、`TreasureHaul_キービジュアル.jpg`） | 元データ。キービジュアルはアプリに入れない |
| タイトルロゴのテクスチャ | `Assets/_Content/TreasureHaul/Textures/TH_TitleLogo.png`（`Textures/` フォルダごと git 管理外） | 待機中の空間のロゴ・開始の合図の正面のロゴ（`ThTitleLogo`） |

- 新しい環境（クローンし直した・hacha さんのプロジェクトへ移す等）では、ロゴの元画像を手で受け渡し、`TH_TitleLogo.png` としてコピーしてから uloop で `AssetDatabase.Refresh()` する。インポート設定は sRGB・Alpha Is Transparency・ミップマップあり・最大 2048・Android は ASTC 6x6。
  `.meta` も git に無いので guid が新しくなる。材質 `Assets/_Content/TreasureHaul/Materials/TH_TitleLogo.mat` の Base Map に、エディタ API で結線し直す。
- 画像が無いときは、`ThTitleLogo` が警告を出してロゴを出さない（白い四角は出ない）。ビルドの前に、ロゴが出ることを PlayMode で確かめる。

## AppConfig（現場での基準位置の調整）
hacha さん（STYLY）の Inter BEE 用テンプレートの仕組み（`Assets/_Content/Scripts/System/AppConfig.cs` / `AppConfigLoader.cs`。`TreasureHaulUserClient` の `UserClient` に付いている）。リビルドせずに、端末ごとの基準位置を設定ファイル `AppConfig.toml` で直せる。

- **置き場所**: エディタはリポジトリの `ExternalResources/AppConfig.toml`。実機（PICO）は `/sdcard/Android/data/com.hamachan.tib/files/AppConfig.toml`（ファイルが無ければ起動時に初期値で作られる。初期値は全部 0）。
- **キー**（Recenter した地点＝起動時の頭の位置・向きから見た値）:
  - `CenterPositionX` / `CenterPositionY` / `CenterPositionZ`: アプリの中心（ワールド原点）の位置（m）。X は右が正、Y は上が正、Z は前が正。
  - `CenterRotationY`: アプリの中心の向き（Y 軸回転、度。右回りが正）。
  - `AppConfigLoader` が、この中心がワールド原点・正面に来るよう、XR Rig を逆向きにずらす（コンテンツ側の座標は変えない）。
- **本プロジェクトのエディタ値**: `ExternalResources/AppConfig.toml` は git に入れてある。エディタで遊ぶとき、練習の列の後ろ（ワールド (8.45, 0, 2.4)）から納品場所（+X）を向いて始まる値（`CenterPositionX = 2.4`、`CenterPositionZ = -8.45`、`CenterRotationY = -90.0`）。実機には使わない。
- **実機での直し方**: `adb push AppConfig.toml /sdcard/Android/data/com.hamachan.tib/files/` → アプリを再起動。アンインストールすると消える（`adb install -r` なら残る）。
- **テンプレートとの違い**: エディタ用の置き場所を、本プロジェクトは Unity プロジェクトがリポジトリ直下にあるため 1 段浅く（`../ExternalResources`）直している。hacha さんのプロジェクトへ移すときは、テンプレート側の `AppConfigLoader.cs` をそのまま使う（guid は同じ）。
- **ParrelSync の複製**: 複製側にも `ExternalResources/` が無ければ、初期値（全部 0）で作られる。その場合は会場の角（ワールド原点）から始まる。
