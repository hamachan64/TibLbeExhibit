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
