# 調査結果 workthree true_near_black 2件固定 2026-03-11

最終更新: 2026-03-11

## 1. 目的
- `workthree` で true near-black とみなしている2件が、安定して `Autogen produced a near-black thumbnail` を返すかを確認する。
- `No frames decoded` 群と切り分け、P1-B の母集団を固定する。

## 2. 対象
- `P1B-01`
- `P1B-02`

実体:
- `P1B-01 = E:\_サムネイル作成困難動画\_steph_myers_-1836566168414388686-20240919_094110-vid1.mp4`
- `P1B-02 = E:\_サムネイル作成困難動画\作成NG\【ライブ配信】神回ですか！？な おP様 配信！！_scale_2x_prob-3.mp4`

## 3. 実行条件
- テスト:
  - `TrueNearBlackPairTests.true_near_black_2件を固定順で比較できる`
- 実行時環境変数:
  - `IMM_TEST_TRUE_NEAR_BLACK_MOVIES`

## 4. 結果
| 動画 | result | error | elapsed_ms |
|---|---|---|---:|
| `P1B-01` | 失敗 | `Autogen produced a near-black thumbnail` | 261 |
| `P1B-02` | 失敗 | `Autogen produced a near-black thumbnail` | 66 |

## 5. 判断
- この2件は `workthree` でも安定して near-black を再現している。
- したがって、P1-B の true near-black 群として固定してよい。
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork\Thumbnail\Engines\引き継ぎ_autogenEOFドレイン対応とベンチ画像出力_f3fd039_2026-03-11.md` で整理された `EOFドレイン` は、超短尺 `No frames decoded` 系の取りこぼし対策である。
- この2件は `No frames decoded` ではなく `Autogen produced a near-black thumbnail` で安定しているため、`EOFドレイン` の主対象ではない。
- 以後はこの2件を
  - 黒判定しきい値
  - 代表フレーム選定
  - `ffmpeg1pass` 逃がし条件
の比較対象とする。

## 5.1 EOFドレイン取り込み後の見方
- `f3fd039` 相当を取り込む目的は、`画像1枚あり顔.mkv` / `画像1枚ありページ.mkv` のような超短尺 `No frames decoded` 群を near-black 調査の母集団から外すことにある。
- そのうえで、なお `Autogen produced a near-black thumbnail` が残る個体だけを true near-black 群として扱う。
- したがって next は「`EOFドレイン` で短尺 no-frames 汚染を減らした後、黒判定の比較をやる」である。

## 6. 優先順位表への影響
- `P1B-01`
- `P1B-02`
の2件は、true near-black 群として P1-B に固定する。
- `P1A-01` ～ `P1A-03` は near-black 群ではなく、`No frames decoded` 群として追う。

## 7. 参照
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork_workthree\Tests\IndigoMovieManager_fork.Tests\TrueNearBlackPairTests.cs`
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork_workthree\Tests\IndigoMovieManager_fork.Tests\bin\x64\Debug\net8.0-windows\true-near-black-pair-summary-latest.csv`
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\優先順位表_workthree_失敗15件の検証順_2026-03-11.md`
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork\Thumbnail\Engines\引き継ぎ_autogenEOFドレイン対応とベンチ画像出力_f3fd039_2026-03-11.md`

## 8. 補足
- 2026-03-11 に `dotnet test --filter FullyQualifiedName~TrueNearBlackPairTests.true_near_black_2件を固定順で比較できる` を再実行したが、NUnit の `Explicit` 扱いで skip になった。
- ただし `true-near-black-pair-summary-latest.csv` の内容は 2件とも `Autogen produced a near-black thumbnail` で一致しており、母集団固定の判断は維持してよい。
