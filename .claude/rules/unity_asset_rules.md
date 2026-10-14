# Unityアセット操作ルール

AIエージェントがUnityプロジェクトを破損させる典型パターンを防ぐためのルール。

## 1. シリアライズ済みアセットのテキスト直接編集の禁止
- `.unity`（シーン）/ `.prefab` / `.asset` のYAMLをテキストエディタ的に直接編集しないこと。
- シーン・プレハブ・ScriptableObject の変更は `uloop-execute-dynamic-code` スキル経由で、`SerializedObject` / `AssetDatabase` / `PrefabUtility` などのエディタAPIを用いて行うこと。
- 例外: 変更内容の**読み取り・調査**のためにYAMLを `grep` / `Read` することは問題ない。

## 2. .meta ファイルの整合性維持
- `Assets/` 配下にファイル・フォルダを新規作成／移動／削除した場合は、必ず `AssetDatabase.Refresh()`（`uloop-execute-dynamic-code` 経由）を実行し、`.meta` の生成・追従を確認すること。
- `.meta` を単独で削除・作成しないこと。アセットの削除は `AssetDatabase.DeleteAsset`、移動は `AssetDatabase.MoveAsset` を使う（ペアの `.meta` が自動処理される）。
- `.meta` 内の `guid` を手作業で書き換えないこと。guid が変わるとシーン・プレハブからの参照がすべて切れる。

## 3. 参照（guid / fileID）の取り扱い
- スクリプトやアセットの参照の付け替えは、YAML上の guid / fileID の書き換えではなく、エディタAPI（`SerializedProperty.objectReferenceValue` 等）で行うこと。
- スクリプトファイルの**リネーム**はクラス名との一致が必要（MonoBehaviour）。リネーム時はクラス名も同時に変更し、コンパイルとシーン参照（Missing Script）の確認を行うこと。

## 4. ProjectSettings の変更
- `TagManager.asset`（タグ・レイヤー）や各種 ProjectSettings を変更する場合は、変更理由をコミットメッセージまたは作業報告に明記すること。
- レイヤーの追加（例: `HandBone`）はエディタAPIまたはUnityエディタ上で行い、既存レイヤー番号の並び替え・再利用はしないこと（既存シーンの衝突マトリクスが壊れる）。

## 5. 変更後の検証
- C#・アセットの変更後は `treasure-haul-verify` スキルの手順（コンパイル → EditModeテスト → 必要ならPlayMode確認）に従うこと。
