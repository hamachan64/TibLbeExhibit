# 非同期処理およびリアクティブプログラミングのルール

## 1. 非同期処理 (UniTask)
- 本プロジェクトでは、Unity標準の `Coroutine` は使用しません。
- 代わりにすべて `UniTask` (`com.cysharp.unitask`) を使用してください。
- メソッドの戻り値は `async UniTask` または `async UniTask<T>` を使用し、待機には `await` を使用します。

## 2. イベント・状態管理 (R3)
- `UnityEvent` や標準のC#イベント（`event Action` 等）の使用は極力避けてください。
- リアクティブな設計を実現するため、`R3` (`com.cysharp.r3`) を使用します。
- 状態の監視には `Observable<T>` や `ReactiveProperty<T>` を活用し、イベント駆動のアーキテクチャを構築してください。
