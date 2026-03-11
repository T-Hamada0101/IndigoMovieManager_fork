# 調査結果 workthree near_black群ベースライン 2026-03-11

最終更新: 2026-03-11

## 1. 目的
- `workthree` branch 上で、near-black 優先群 5件の現行ベースラインを固定する。
- 本線の一括試行結果との差分を把握し、どこから検証を始めるべきかを明確にする。

## 2. 実行条件
- branch:
  - `workthree`
- 実行テスト:
  - `NearBlackBatchPlaygroundTests.near_black候補5件を現行branch条件で一括比較できる`
- 実行日:
  - 2026-03-11

## 3. 結果概要
- 対象: 5件
- 成功: 0件
- 失敗: 5件

失敗内訳:
- `Autogen produced a near-black thumbnail`: 2件
- `No frames decoded`: 3件

## 4. 動画別結果
| 動画 | 結果 | エラー | 所見 |
|---|---|---|---|
| `P1B-01` | 失敗 | `Autogen produced a near-black thumbnail` | 期待どおり near-black 系 |
| `P1A-01` | 失敗 | `No frames decoded` | 本線一括試行時の near-black とは異なる。branch 差分の影響候補 |
| `P1A-02` | 失敗 | `No frames decoded` | 本線一括試行時の near-black とは異なる。branch 差分の影響候補 |
| `P1A-03` | 失敗 | `No frames decoded` | 本線一括試行時の near-black とは異なる。branch 差分の影響候補 |
| `P1B-02` | 失敗 | `Autogen produced a near-black thumbnail` | 期待どおり near-black 系 |

## 5. 本線との差分
- 本線の 2026-03-11 一括試行では、この群は near-black 5件として整理していた。
- `workthree` baseline では 3件が `No frames decoded` に落ちている。
- したがって、この branch で near-black 群を研究する前に、まず「branch 差分で engine route / policy / repair 条件が変わっていないか」を確認する必要がある。

## 6. 次の検証順
1. `P1A-01`
   - 本線との差分が明確で、単体確認しやすい
2. `P1A-02`
3. `P1A-03`
4. `P1B-01`
5. `P1B-02`

## 7. 判断
- `workthree` では near-black 群の研究をそのまま進める前に、まず branch 差分で `No frames decoded` 化している 3件を優先確認すべき。
- つまり、P1 の内訳は次の2段に分ける。
  - P1-A: 本線との差分確認 (`P1A-01` ～ `P1A-03`)
  - P1-B: 真の near-black 救済条件探索 (`P1B-01`, `P1B-02`)

## 8. 参照
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork_workthree\Tests\IndigoMovieManager_fork.Tests\NearBlackBatchPlaygroundTests.cs`
- 実行時に必要:
  - `IMM_TEST_NEAR_BLACK_MOVIES`
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork_workthree\Tests\IndigoMovieManager_fork.Tests\bin\x64\Debug\net8.0-windows\near-black-batch-summary-latest.csv`
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\優先順位表_workthree_失敗15件の検証順_2026-03-11.md`
