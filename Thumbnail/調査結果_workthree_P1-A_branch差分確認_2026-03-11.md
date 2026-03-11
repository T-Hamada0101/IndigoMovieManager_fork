# 調査結果 workthree P1-A branch差分確認 2026-03-11

最終更新: 2026-03-11

## 1. 目的
- `near-black` 想定から `No frames decoded` へ変化していた P1-A の3件について、`service` と `ffmpeg.exe` 中間1枚抜きの差分を確認する。
- branch 差分なのか、実フレーム取得不能なのかを切り分ける。

## 2. 対象
- `P1A-01`
- `P1A-02`
- `P1A-03`

## 3. 実行条件
- テスト:
  - `P1ABranchDiffBatchTests.P1A対象3件をbranch差分観点で比較できる`
- 実行時環境変数:
  - `IMM_TEST_P1A_MOVIES`
- 比較観点:
  - `ThumbnailCreationService.CreateThumbAsync(...)`
  - `ffprobe` で duration 取得
  - duration の中央秒で `ffmpeg.exe -frames:v 1`

## 4. 結果
| 動画 | service | service error | midpoint | ffmpeg direct | exit code | 判断 |
|---|---|---|---:|---|---:|---|
| `P1A-01` | 失敗 | `No frames decoded` | 240.474 | 失敗 | -22 | branch 差分だけでは説明しにくい |
| `P1A-02` | 失敗 | `No frames decoded` | 2289.917 | 失敗 | -22 | 同上 |
| `P1A-03` | 失敗 | `No frames decoded` | 2299.133 | 失敗 | -22 | 同上 |

## 5. 読み取り
- 3件とも `service` だけでなく、`ffmpeg.exe` の中間1枚抜きも失敗した。
- したがって、この3件については「workthree branch だけ route が違うため near-black から no-frames へ変わった」とは言い切れない。
- 少なくとも「中間位置でフレームを簡単に1枚抜ける動画」ではなかった。

## 6. 現時点の判断
- P1-A は「branch 差分確認」から一段進み、`No frames decoded` 実フレーム不能寄りの候補として扱う。
- ただし、これだけで最終的に救済不能と決め打ちはしない。
- 次に確認すべきなのは:
  1. `ffmpeg.exe` の seek 位置依存か
  2. repair 後入力で変わるか
  3. `near-black` 群に混ぜるべきでなく、`No frames decoded` 群へ移すべきか

## 7. 優先順位表への影響
- `P1A-01` ～ `P1A-03` は、現時点では P1-A の「branch 差分確認対象」から「No frames decoded 深掘り候補」へ寄せる。
- 真の near-black 救済探索は `P1B-01` と `P1B-02` を先に見る方が効率的。

## 8. 参照
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork_workthree\Tests\IndigoMovieManager_fork.Tests\P1ABranchDiffBatchTests.cs`
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork_workthree\Tests\IndigoMovieManager_fork.Tests\bin\x64\Debug\net8.0-windows\p1a-branch-diff-summary-latest.csv`
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\優先順位表_workthree_失敗15件の検証順_2026-03-11.md`
