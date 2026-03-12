# 連絡用doc workthree 本線引き継ぎ 難読動画成果要約 2026-03-11

## 1. 結論
- `workthree` 側で、本線へ返す価値が高い条件は 2 本に絞れた。
- 優先順は次。
  1. `35967型`
  2. `画像1枚あり顔` 型
- true near-black 2件は、まだ「本線へ条件反映」より「比較調査継続」の段階。

## 2. 本線へ返す第1候補
- 名称:
  - `35967型`
- 主条件:
  - `autogen / service = No frames decoded`
  - 長尺
  - `ffmpeg midpoint success`
- 補助条件:
  - bitrate が極端に低くない
- 入れ先候補:
  - `retry policy`
- 意味:
  - `autogen` 本体改善ではなく、`ffmpeg1pass` 救済へ落とす一般条件候補

## 3. 本線へ返す第2候補
- 名称:
  - `画像1枚あり顔` 型
- 主条件:
  - 超短尺
  - `autogen / service = No frames decoded`
  - `ffmpeg` の極小 seek `0.001 / 0.01` のみ成功
- 入れ先候補:
  - `FfmpegOnePassThumbnailGenerationEngine` の短尺 fallback 候補調整
- 注意:
  - `画像1枚ありページ.mkv` には誤適用しない

## 4. まだ本線へ返さないもの
- true near-black 2件
  - `Autogen produced a near-black thumbnail`
  - `P-04` の「古い暗フレーム捨て」観点で比較継続
- `画像1枚ありページ.mkv`
  - `顔型` と同条件では救えない
  - 別救済候補か除外候補かは保留
- `インデックス破壊-093-2-4K.mp4`
  - `ffmpeg midpoint fail`
  - `35967型` ではない
  - repair 側論点

## 5. `f3fd039` の扱い
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork\Thumbnail\Engines\引き継ぎ_autogenEOFドレイン対応とベンチ画像出力_f3fd039_2026-03-11.md` は取り込み価値あり。
- ただし主対象は near-black ではなく、超短尺 `No frames decoded` 群。
- 取り込み後の再実行順は次で固定。
  1. `画像1枚あり顔.mkv`
  2. `画像1枚ありページ.mkv`
  3. `NearBlackBatchPlaygroundTests`
  4. `TrueNearBlackPairTests`

## 6. 本線側で次にやること
1. `35967型` を `retry policy` 差し込み候補として受け取る
2. `顔型` を `ffmpeg1pass` 短尺 fallback 候補調整として受け取る
3. `f3fd039` 取り込み後に、short no-frames 群と true near-black 群を再比較する

## 7. 参照
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\連絡用doc_workthree_35967型判定基準と本線反映候補_2026-03-11.md`
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\連絡用doc_workthree_画像1枚あり顔_極小seek成功条件_2026-03-11.md`
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\連絡用doc_workthree_本線向け短文化_35967型と顔型_2026-03-11.md`
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\作業メモ_workthree_f3fd039取り込み後のnear_black再実行順_2026-03-11.md`
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork\Thumbnail\調査結果_難読動画成功パターン集_2026-03-11.md`
