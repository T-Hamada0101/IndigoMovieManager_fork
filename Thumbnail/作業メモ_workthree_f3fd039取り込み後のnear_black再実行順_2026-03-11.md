# 作業メモ workthree f3fd039取り込み後のnear_black再実行順 2026-03-11

最終更新: 2026-03-11

## 1. 目的
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork\Thumbnail\Engines\引き継ぎdoc_autogenEOFドレイン対応とベンチ画像出力_f3fd039_2026-03-11.md` を取り込んだ後、`workthree` 側で near-black 調査をどう再開するかを固定する。
- 超短尺 `No frames decoded` 群の取りこぼし改善と、true near-black 群の黒判定調査を混ぜない。

## 2. 前提
- `f3fd039` の主対象は、超短尺動画で
  - `send_packet` は通る
  - `receive_frame` が `EAGAIN`
  - `EOF` 到達
  - `No frames decoded`
になる群である。
- 主な確認対象:
  - `画像1枚あり顔.mkv`
  - `画像1枚ありページ.mkv`
- true near-black 2件は、現時点で `Autogen produced a near-black thumbnail` が安定しているため、論点は別。

## 3. 再実行順
1. `f3fd039` 相当を `workthree` 側へ取り込む
2. short no-frames 群を再実行する
   - 対象:
     - `画像1枚あり顔.mkv`
     - `画像1枚ありページ.mkv`
   - 見る点:
     - `No frames decoded` が解消するか
     - runtime log に `send flush packet` -> `seek hit` が出るか
3. `NearBlackBatchPlaygroundTests` を再実行する
   - 目的:
     - near-black 群の母集団から short no-frames 汚染が減るかを確認する
4. `TrueNearBlackPairTests` を再実行する
   - 目的:
     - `P1B-01` / `P1B-02` が still near-black か確認する
5. still near-black が残る場合だけ、黒判定しきい値と `ffmpeg1pass` 逃がし条件の比較へ進む

## 4. 実行時の見るポイント
- short no-frames 群
  - `bench-autogen-seek`
  - `bench-autogen-shortclip-firstframe-fallback`
  - `send flush packet`
  - `seek hit`
- true near-black 群
  - `Autogen produced a near-black thumbnail`
  - 出力画像の見た目
  - 黒判定しきい値で落ちているのか、代表フレーム選定が悪いのか

## 5. 期待する分岐
- `画像1枚あり顔.mkv` / `画像1枚ありページ.mkv` が成功へ寄る
  - `f3fd039` は有効
  - near-black 調査の母集団を縮小できる
- true near-black 2件も success へ寄る
  - それは `EOFドレイン` の効果ではなく、母集団再評価が必要
  - near-black 固定の前提を再確認する
- true near-black 2件が変わらず near-black
  - その時点で黒判定しきい値と `ffmpeg1pass` 逃がし条件の比較を始める

## 6. 次に更新すべき文書
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\調査結果_workthree_true_near_black_2件固定_2026-03-11.md`
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\優先順位表_workthree_失敗15件の検証順_2026-03-11.md`
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\現状把握_workthree_失敗動画検証と本線反映方針_2026-03-11.md`

## 7. 参照
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork\Thumbnail\Engines\引き継ぎdoc_autogenEOFドレイン対応とベンチ画像出力_f3fd039_2026-03-11.md`
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\調査結果_workthree_true_near_black_2件固定_2026-03-11.md`
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\優先順位表_workthree_失敗15件の検証順_2026-03-11.md`
