# 調査結果 workthree 優先順自動実行 2026-03-11

最終更新: 2026-03-11

## 1. 目的
- `workthree` の優先順どおりに、代表4件を自動で順番に実行する。
- `service`、`ffmpeg` 中間1枚抜き、repair 前後の差分を、次の実装判断に使える形で固定する。

## 2. 実行対象
- `P1-Index`
  - `インデックス破壊-093-2-4K.mp4`
- `P1-35967`
  - `35967.mp4`
- `P2-Face`
  - `画像1枚あり顔.mkv`
- `P2-Page`
  - `画像1枚ありページ.mkv`

## 3. 実行条件
- テスト:
  - `WorkthreePrioritySequenceTests.優先順で代表動画を自動実行できる`
- 比較観点:
  - `ThumbnailCreationService.CreateThumbAsync(...)`
  - `ffprobe` で duration 取得
  - duration 中央秒で `ffmpeg.exe -frames:v 1`
  - short 群だけ追加で極小 seek 群を確認
  - `PrepareWorkingMovieAsync(...)` による repair 事前適用可否

## 4. 結果
| label | service | midpoint ffmpeg | repair applied | 読み取り |
|---|---|---|---|---|
| `P1-Index` | `No frames decoded` | 失敗 | なし | index 破損名だが、この入口では repair 事前適用なし。`ffmpeg` 中央も失敗 |
| `P1-35967` | `No frames decoded` | 成功 | なし | `autogen` 失敗 / `ffmpeg` 中央成功の差分を再確認 |
| `P2-Face` | `No frames decoded` | 失敗 | なし | 極小 seek `0.001` / `0.01` だけ `ffmpeg` 成功 |
| `P2-Page` | `No frames decoded` | 失敗 | なし | 極小 seek 群も全滅 |

## 5. 動画別所見

### 5.1 `P1-Index`
- duration:
  - `480.947`
- midpoint:
  - `240.474`
- `service`:
  - `No frames decoded`
- `ffmpeg.exe` 中央1枚抜き:
  - 失敗
  - exit code `-22`
- repair:
  - `PrepareWorkingMovieAsync(...)` では未適用

判断:
- この個体は、少なくとも現行入口では
  - `repair workflow` の事前 probe
  - `ffmpeg` 中央1枚抜き
  の両方で簡単には救えていない。
- `index-no-frames` 群として、repair 条件そのものの見直し候補。

### 5.2 `P1-35967`
- duration:
  - `2872.529`
- size:
  - `757,788,401 bytes`
  - `722.683 MB`
- estimated bitrate:
  - `2110.4 kbps`
- midpoint:
  - `1436.265`
- `service`:
  - `No frames decoded`
- `ffmpeg.exe` 中央1枚抜き:
  - 成功
  - exit code `0`
- repair:
  - 未適用

判断:
- `35967.mp4` は引き続き
  - `autogen` では取れない
  - `ffmpeg` 中央では取れる
  差分を持つ代表ケース。
- 本線へ戻す一般条件候補として最優先維持。

`35967.mp4 型` の現時点定義:
- 主条件
  - `service` / `autogen` 側は `No frames decoded`
  - `ffmpeg.exe` の中間1枚抜きは成功
  - 長尺動画である
- 補助条件
  - duration に対して推定 bitrate が極端に低くない
  - 代表ケース `35967.mp4` は `2110.4 kbps` で、時間のわりに小さすぎる動画ではない

注意:
- bitrate は補助条件であり、これだけで `35967.mp4 型` と判定しない。
- `インデックス破壊-093-2-4K.mp4` のように bitrate が十分高くても、`ffmpeg` 中間1枚抜きに失敗する個体は別群として扱う。

### 5.3 `P2-Face`
- duration:
  - `0.069`
- `service`:
  - `No frames decoded`
- midpoint:
  - `0.1`
  - `ffmpeg` 失敗
- 極小 seek:
  - `0.001:ok`
  - `0.01:ok`
  - `0.05:ng`
  - `0.1:ng`
  - `0.25:ng`
  - `0.5:ng`
- repair:
  - 未適用

判断:
- この個体は「短尺全滅」ではない。
- `ffmpeg` では極小 seek に限定して取れる。
- したがって
  - 短尺専用 seek 候補をさらに先頭寄りへ寄せる
  - `0.001` / `0.01` を優先的に試す
  方向は有効。

### 5.4 `P2-Page`
- duration:
  - `0.033`
- `service`:
  - `No frames decoded`
- midpoint:
  - `0.1`
  - `ffmpeg` 失敗
- 極小 seek:
  - 全滅
- repair:
  - 未適用

判断:
- `P2-Face` と同系統に見えて、実際は救済条件が一致していない。
- 短尺群を1条件で一般化する前に、`Face` と `Page` を分離して扱う必要がある。

## 6. 次の判断
1. `35967.mp4`
   - 本線へ戻す一般条件候補として継続
   - `autogen no-frames + ffmpeg midpoint success` 型
   - bitrate は補助条件として `極端に低くないこと` を使える
2. `画像1枚あり顔.mkv`
   - 短尺 seek 救済候補として継続
   - `0.001 / 0.01` を軸に追加比較
3. `画像1枚ありページ.mkv`
   - 同系統扱いを外し、別個体として再調査
4. `インデックス破壊-093-2-4K.mp4`
   - repair の発火条件か probe 条件を見直さない限り、現状は進展なし

## 7. 参照
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork_workthree\Tests\IndigoMovieManager_fork.Tests\WorkthreePrioritySequenceTests.cs`
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork_workthree\Tests\IndigoMovieManager_fork.Tests\bin\x64\Debug\net8.0-windows\workthree-priority-sequence-summary-latest.csv`
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\優先順位表_workthree_失敗15件の検証順_2026-03-11.md`
