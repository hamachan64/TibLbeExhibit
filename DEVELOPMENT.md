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
  PICOデバイス向けのVR対応パッケージ。
- **PXR_Enterprise**:
  展示用途として、ジェスチャー認識のオフやシステムリセットの無効化など、デバイスの特殊な制御を行うために使用されています。
- **Meta OpenXR (`com.unity.xr.meta-openxr`)**:
  Meta Quest向けの対応パッケージ。
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
| **Environment** | `Assets/_Content/Scenes/Environment.unity` | 3Dステージ、共通の干渉オブジェクト（岩、木、カードの浮遊演出など） |
| **UserClient** | `Assets/_Content/Scenes/UserClient.unity` | 体験者用XR Rig、バインダーUI、ハンドトラッキング操作ロジック |
| **OperatorClient** | `Assets/_Content/Scenes/OperatorClient.unity` | 運営用PC向けUI、システム制御、制限時間の管理・開始トリガー |

## ディレクトリ構成
- `Assets/_Content/Scripts/`: 主なロジック・スクリプト類
  - `UserClient.cs`: 体験者向けの機能やPICOエンタープライズの初期化・制御
  - `OperatorClient.cs`: オペレーター（運用者）向けの機能
  - `VRArea.cs`, `ViewManager.cs`: エリア管理やビュー（UI）の管理

## 開発・実装上のルール
- **言語・命名規則**: C#の標準的な命名規則に従います。クラス・メソッドはPascalCase、変数はcamelCaseを推奨します。
- **非同期処理の統一**: コルーチンではなく、極力 `UniTask` を使用してください。
- **イベント処理の統一**: UnityEventや標準のC#イベント（`event Action`）よりも、`R3` の `Observable` を用いたリアクティブな設計を推奨します。
- **アニメーション制御**: オブジェクトの移動やUIのフェードなどの演出処理は、Updateメソッド内で計算するのではなく、極力 `DOTween` を使用してください。
- **展示デバイスの制御**: デバイスに依存する処理（PICO Enterprise API等）を呼び出す場合は、他プラットフォーム（EditorやMeta Quest）でエラーにならないよう、適切なプラットフォーム分岐やコンパイルディレクティブを考慮してください。
