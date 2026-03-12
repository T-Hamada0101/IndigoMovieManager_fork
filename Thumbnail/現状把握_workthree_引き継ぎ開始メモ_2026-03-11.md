# 現状把握 workthree 引き継ぎ開始メモ 2026-03-11

最終更新: 2026-03-11

## 1. この branch の役割
- `workthree` は、難動画の実動画検証を進めるための branch とする。
- ここでは本線受け皿の整備ではなく、失敗動画の再現、救済条件の探索、再現率確認を優先する。
- 本線へ戻す時は、動画名ではなく一般条件として戻す。

## 2. 2026-03-11 時点の状態
- branch: `workthree`
- worktree path:
  - `C:\Users\{username}\source\repos\IndigoMovieManager_fork_workthree`
- 作業ツリーは現時点でクリーンではない。
- ただし、`workthree` 側には本線で先行整備した次の資産がまだ入っていない。
  - `FailureDb` 系の受け皿
  - `workthree` 受領用 doc 群
  - `AutogenRepairPlaygroundTests`
  - `FfmpegShortClipRecoveryPlaygroundTests`
  - `DifficultVideoBatchPlaygroundTests` は持ち込み済み

## 3. 本線との差分認識
- 本線側では、`workthree -> FailureDb.ExtraJson -> サムネ失敗タブ -> 計画書流し先` まで受け皿が整っている。
- `workthree` 側は、まだ難動画研究を始めるための計測基盤が不足している。
- したがって、この branch での最初の実務は「研究用 doc と試験ハーネスを最小単位で持ち込むこと」である。

## 4. ここで欲しい成果物
- 動画ごとの失敗理由
- 成功した条件
- 再現率
- 本番導入位置
- 既存 `FailureKind` で足りるか

## 5. 直近の優先順
1. workthree 側へ現状把握 doc を追加する。
2. 全動画一括試行ハーネスを持ち込み、母集団を固定する。
3. 失敗動画ごとの個別 playground を持ち込み、成功条件を試す。
4. 成功した条件を一般化し、優先順位表へ落とす。
5. 本線側へ伝達 doc を渡す。

## 6. 直ちにやらないこと
- 本線向け UI 受け皿の追加
- `FailureKind` の本線実装変更
- Queue / Worker の責務変更
- 動画名ベタ判定の本番導入

## 7. 次に持ち込む候補
- `Tests/IndigoMovieManager_fork.Tests/AutogenRepairPlaygroundTests.cs`
- `Tests/IndigoMovieManager_fork.Tests/FfmpegShortClipRecoveryPlaygroundTests.cs`
- `Thumbnail/現状把握_workthree_失敗動画検証と本線反映方針_2026-03-11.md`
- `Thumbnail/優先順位表_workthree_失敗15件の検証順_2026-03-11.md`
- `Thumbnail/調査結果_workthree_全動画再試行ベースライン_2026-03-11.md`

## 8. 完了条件
- `workthree` 側だけで、失敗動画の再現と救済条件の比較を進められる。
- 本線へ戻す時に必要な最小情報を、この branch 上で整理できる。

## 9. 関連
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork\Thumbnail\現状把握_workthree_失敗動画検証と本線反映方針_2026-03-11.md`
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork\Thumbnail\Implementation Plan_workthree救済条件の本線受け取りとFailureDbExtraJson標準化_2026-03-11.md`
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork\Thumbnail\連絡用_workthree救済条件の受け皿整理_FailureDbExtraJson_2026-03-11.md`
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\優先順位表_workthree_失敗15件の検証順_2026-03-11.md`
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\調査結果_workthree_全動画再試行ベースライン_2026-03-11.md`
