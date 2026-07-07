# LBE & PICO Enterprise 開発の学習メモ

（Claudeが自律的にここに追記していきます）

## 現在の初期知見
- 本プロジェクトは PICO (PXR_Enterprise) を使用しており、コントローラーではなくハンドトラッキング専用。
- UniTask と R3 をベースに非同期・イベント駆動を構築している。
- Unityメタファイルの破壊を防ぐための Hook (`protect.sh`) が稼働中。
