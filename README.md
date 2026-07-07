# TibLbeExhibit

このプロジェクトは、ロケーションベースエンターテインメント（LBE）の展示・体験を目的としたUnity MR（複合現実）アプリケーションです。

## 概要
展示会場でのMR（パススルー）体験を提供するためのアプリケーションであり、オペレーター（運営側）とユーザー（体験者側）のマルチクライアント構成をサポートします。PICOなどのスタンドアローンVRヘッドセットに対応し、展示運用に特化したシステム制御が組み込まれています。

## 動作環境
- **Unity バージョン**: 6000.3.10f1
- **レンダリング**: Universal Render Pipeline (URP)
- **ターゲットデバイス**: PICO (PicoXR), Meta Quest 等の OpenXR 対応デバイス

## セットアップ手順
1. 指定されたバージョンのUnityで本プロジェクトを開きます。
2. 必要なパッケージが自動でリストアされます（`Packages/manifest.json`経由）。
3. 本プロジェクトはリポジトリ軽量化のため一部のサードパーティ製アセット（3Dモデル・エフェクト）を含んでいません。開発前に以下のアセットをUnity Asset Store等からインポートし、`Assets/` 直下に配置してください。
   - **Hovl Studio** (VFXアセット) -> `Assets/Hovl Studio` に配置
   - **JMO Assets** (Cartoon FX) -> `Assets/JMO Assets` に配置
   - **JTS_Wakizashi** (脇差3Dモデル) -> `Assets/JTS_Wakizashi` に配置
4. ビルド対象のプラットフォーム（主にAndroid）に合わせて、Platformを切り替えてください。

## ライセンス・注意事項
本プロジェクトは展示・運用向けに構成されており、PICO Enterprise SDKなどの特殊なSDKが含まれています。実機ビルド時は適切な設定を確認してください。
