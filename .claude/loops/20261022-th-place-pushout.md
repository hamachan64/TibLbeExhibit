# そっと置いたお宝が壁に食い込まないようにする

## 目的
持ったお宝を壁へ押し付け、手と〔離れたら置く距離〕以上離れて「そっと置く」になったとき、お宝が壁に 2.5cm 食い込んで止まる（#46 の PlayMode で確認。最小 X -2.305、壁の内面 -2.28）。
置かれたお宝が壁の中に入ると、見た目が壊れ、ビームが壁で止まって取れなくなることがある。そっと置いた後のお宝は、壁・家具と重ならないようにする。

## 原因
`TreasureClient.PlaceDown` は、降ろす先を `ThPlaceSolver.Solve`（真下への Raycast）で決め、向きを `ThPlaceSolver.Upright`（Y 回転だけ残す）で立て直す。
傾いて持っていたお宝を立て直すと外形が横に広がるが、横の重なりは確かめていない。

## 受け入れ基準
- [ ] 降ろす先（位置と立て直した向き）で、お宝の本体コライダーが会場の物（壁 `ThWall`・家具・瓦礫・納品場所など。`ThPlaceSolver` が除いているレイヤー・トリガー・アバターは対象外）と重なるとき、重なりが無くなるまで**水平に**押し出す。
  `Physics.ComputePenetration` を使い、最大 3 回繰り返す。押し出した後は、その XZ で `Solve` をやり直して高さを決め直す。
- [ ] 押し出しの合計は 0.3m までにする。それでも重なりが残るときは、そのまま置いて `Debug.LogWarning` を出す（壁の向こうへ大きく飛ばさない）。
- [ ] 処理は `ThPlaceSolver` に静的メソッドとして足す（例: `SolveClear(Transform venueRoot, Vector3 localFrom, Quaternion localRot, Collider[] bodyColliders, Transform treasureRoot)`）。`PlaceDown` からはこれを呼ぶ。
- [ ] 持ち主の切断で運営PCが置かせる経路（`SessionHost` の `ThPlaceSolver.Solve`）は変えない（お宝の当たり判定を持たないため）。今回の範囲外として、設計書に一文書く。
- [ ] EditMode テスト: 壁の箱と、45° 傾いたお宝の箱を作り、壁に接して持っていた位置から置くと、置いた先で重なりが 1mm 以下になる。重なりが無いときは位置が `Solve` と同じになる。0.3m の上限が効く。
- [ ] PlayMode: #46 と同じ手順（`ThLoopbackHarness`、4 人・シード 7、西の外壁の奥行 7.5 に押し付けて離す）で、置いた後のお宝の本体コライダーの最小 X が -2.28 以上（壁の内面より内側）。数値をログで残す。

## 変更対象
- `Assets/_Content/TreasureHaul/Scripts/Treasure/ThPlaceSolver.cs`: 押し出しを足す。
- `Assets/_Content/TreasureHaul/Scripts/Treasure/TreasureClient.cs`: `PlaceDown` で使う。
- `Assets/_Content/Tests/EditMode/`: テストを足す。
- `TREASURE_HAUL_DESIGN.md` 4.2 / 4.3 の「そっと置く」の記述に、横の押し出しを一文足す。

## やらないこと（スコープ外）
- 運営PCが置かせる経路（持ち主の切断）。
- 持っている間の壁へのめり込み（1〜3cm。設計書 4.3 の【要検証】）。
- Core の変更。

## 制約
- 規約: UniTask / R3 / DOTween、ハンドトラッキング専用、`.unity`/`.prefab`/`.asset` は uloop と Editor API で編集する。
- 置いた位置は `ThRest` で全員へ送られるので、計算は持ち主の端末だけで行えばよい（決定性は不要）。
- 毎フレームではなく、置くときに 1 回だけ呼ぶ（割り当ては小さく保つ）。

## 検証方法
- EditMode テスト（上記）。
- PlayMode（上記）。

## 実機確認が必要な項目
- なし

## 外部の素材・サービス（権利・費用・セキュリティ）
- なし
