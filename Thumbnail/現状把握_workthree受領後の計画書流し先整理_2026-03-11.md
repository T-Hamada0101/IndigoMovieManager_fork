# 現状把握 workthree受領後の計画書流し先整理 2026-03-11

最終更新: 2026-03-11

## 1. 目的
- `workthree` から救済条件が返ってきた時に、どの計画書へ何を反映するかを固定する。
- 本線側で「どこへ書くか」を毎回判断し直さないようにする。
- `FailureDb.ExtraJson`、`FailureKind`、実装計画、手動確認手順の更新先を揃える。

## 2. 前提
- `workthree` から本線へ戻す時の最小情報は次とする。
  - 動画ごとの失敗理由
  - 成功した条件
  - 再現率
  - 本番導入位置
  - 既存 `FailureKind` で足りるか
- 本線では動画名ベタ判定を採用しない。
- 反映対象は「個別動画」ではなく「一般化できた失敗パターンと救済条件」とする。

## 3. 流し先一覧
| 条件 | 主反映先 | 追随更新先 | 反映内容 |
|---|---|---|---|
| `result_signature` と `FailureKind` の対応が増える | `設計メモ_FailureKind_失敗分類と回復方針案_2026-03-09.md` | `設計整理_result_signatureとFailureKind対応表_2026-03-11.md` | 分類根拠、既存分類で足りるか、新設要否 |
| `preflight` で確定できる救済条件 | `Implementation Plan_workthree救済条件の本線受け取りとFailureDbExtraJson標準化_2026-03-11.md` | `サムネイルが作成できない動画対策.md` | `preflight_branch`、`result_signature`、即時分岐条件 |
| `retry policy` で engine / seek 切替する | `Implementation Plan_workthree救済条件の本線受け取りとFailureDbExtraJson標準化_2026-03-11.md` | `設計メモ_FailureKind_失敗分類と回復方針案_2026-03-09.md` | `engine_attempted`、`seek_strategy`、`seek_sec`、`recovery_route` |
| `repair workflow` で回復する | `Implementation Plan_リカバリーレーン_動画インデックス破損判定と修復API_実装計画兼タスクリスト_2026-03-06.md` | `サムネイルが作成できない動画対策.md` | `repair_attempted`、`repair_succeeded`、repair 適用条件 |
| `HangSuspected` と区別して扱う必要がある | `Implementation Plan_Queue実行状態分離とHangSuspected_実装計画兼タスクリスト_2026-03-10.md` | `設計メモ_FailureKind_失敗分類と回復方針案_2026-03-09.md` | 停滞扱いか、通常 decode failure かの切り分け |
| Worker / Queue / UI の責務境界に影響する | `Implementation Plan_サムネイルWorker完全責務移譲_長期計画_2026-03-08.md` | `Implementation Plan_workthree救済条件の本線受け取りとFailureDbExtraJson標準化_2026-03-11.md` | どこで判断するか、UI へ持ち込まない条件 |
| 調査運用と失敗タブの見方が変わる | `サムネイルが作成できない動画対策.md` | `現状把握_FailureDbExtraJsonキー棚卸し_2026-03-11.md` | `ExtraJson` 新キー、失敗タブ確認観点、観測スクリプト手順 |
| 手動確認項目が増える | 各 Implementation Plan の手動確認節 | `サムネイルが作成できない動画対策.md` | 再現動画、期待ログ、期待 `FailureDb` 行 |

## 4. 反映順
1. `FailureDb.ExtraJson` に必要キーが足りるか確認する。
2. `FailureKind` へ当てはめる。
3. 導入位置を `preflight / retry policy / repair / finalizer` から1つ選ぶ。
4. 対応する Implementation Plan へ落とす。
5. 失敗タブ、運用 doc、観測スクリプトの追随要否を決める。

## 5. 本線側での判断ルール
- `preflight` へ入れるのは、追加実行なしで確定できる条件だけにする。
- `retry policy` へ入れるのは、既存 engine と seek 切替で回復余地があるものに限定する。
- `repair workflow` へ入れるのは、入出力 I/O を増やす価値がある再現率のものだけにする。
- `HangSuspected` 計画へ入れるのは、時間経過や heartbeat を見ないと区別できないものだけにする。
- 長期計画へ書くのは、Worker / Queue / UI の責務境界が変わる時だけにする。

## 6. 今やらないこと
- 受領前に各計画書へ仮説を書き散らすこと
- 動画単位の個別条件をそのまま本線計画へ書くこと
- 失敗タブの表示項目を先回りで増やし続けること

## 7. 完了条件
- `WT-INT-005` の「受領後に更新する計画書の流し先を固定」が満たされる。
- 後続エージェントが受領 doc を読んだ時に、更新先を迷わない。

## 8. 関連
- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork\Thumbnail\Implementation Plan_workthree救済条件の本線受け取りとFailureDbExtraJson標準化_2026-03-11.md`
- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork\Thumbnail\現状把握_workthree_失敗動画検証と本線反映方針_2026-03-11.md`
- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork\Thumbnail\設計メモ_FailureKind_失敗分類と回復方針案_2026-03-09.md`
- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork\Thumbnail\連絡用doc_workthree救済条件の受け皿整理_FailureDbExtraJson_2026-03-11.md`
