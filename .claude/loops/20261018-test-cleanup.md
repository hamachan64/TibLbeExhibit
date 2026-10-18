# 前作（GIPoker）のテストを repo/develop から削除する

## 目的
`repo/develop` では GIPoker を保守しない（前作は `develop` / `master` 系統で扱い、移行時にも置いていく）。GIPoker のテストは24件が常に失敗しており、毎回「24件から増えていないか」を見比べる必要があって、本当の回帰を見落としやすい。GIPoker のテストを外し、**EditMode テストは全件成功を基準**にする。

## 受け入れ基準
- [x] GIPoker のテスト15ファイル（と .meta）が削除されている（Assets の削除はガードで止まるため、ユーザーが自分のターミナルで `git rm` した）
- [x] `LSignPoseEvaluatorTests`（Core の `LSignPoseEvaluator` のテスト）は残し、名前空間を `STYLY.TIB_Template.Tests` にする
- [x] テストの asmdef から `TibLbeExhibit.GIPoker` への参照を外す
- [x] コンパイル エラー0、EditMode テストが全件成功
- [x] 検証スキル・実装／PlayMode 検証エージェント・学習メモの「既知の失敗24件」を「全件成功が基準」に書き換える

## やらないこと
- GIPoker の本体コード、`develop` / `master` のテストには触らない

## 結果
- コンパイル: エラー0 / 警告0
- EditMode テスト: 42件すべて成功（以前は 270件中 246件成功・24件失敗）
