# 調査結果 workthree 全動画再試行ベースライン 2026-03-11

最終更新: 2026-03-11

## 1. 目的
- 動画名変更後の母集団で、`workthree` 側の全動画一括試行ベースラインを取り直す。
- 旧 `48件 / 失敗9件` 前提を、現行の実体に合わせて更新する。
- 今後の優先順位表と個別調査 doc の基準をこの結果へ寄せる。

## 2. 実行条件
- branch:
  - `workthree`
- テスト:
  - `DifficultVideoBatchPlaygroundTests.実動画フォルダ配下を_workthreeで一括試行できる`
- 実行時環境変数:
  - `IMM_TEST_DIFFICULT_VIDEO_ROOT=E:\_サムネイル作成困難動画`
- 実行日:
  - 2026-03-11

## 3. 結果概要
- 対象: 25件
- 成功: 10件
- 失敗: 15件

失敗内訳:
- `No frames decoded`: 13件
- `Autogen produced a near-black thumbnail`: 2件

拡張子別:
- 成功
  - `.flv`: 2件
  - `.swf`: 8件
- 失敗
  - `.mp4`: 10件
  - `.mkv`: 3件
  - `.avi`: 1件
  - `.wmv`: 1件

## 4. 重要な読み取り
- 動画名変更後の母集団では、旧 `48件 / 失敗9件` 前提はそのまま使えない。
- 現在の失敗群は `near-black` 2件よりも `No frames decoded` 13件が支配的である。
- したがって、`workthree` の主戦場は
  - true near-black の救済探索
  よりも
  - `No frames decoded` 群の分類整理と勝ち筋探索
  に寄っている。

## 5. 現時点の群分け
- true near-black 群
  - `_steph__094110-vid1.mp4`
  - `【ライブ配信】神回scale_2x_prob-3.mp4`
- no-frames 群
  - `35967.mp4`
  - `na04.mp4`
  - `shiroka8.mp4`
  - `「ラ・ラ・ランド」は少女漫画か！？ 1_2.mp4`
  - `「ラ・ラ・ランド」は少女漫画か！？ 2_2.mp4`
  - `みずがめ座 (2).mp4`
  - `インデックス破壊-093-2-4K.mp4`
  - `真空エラー2_ghq5_temp.mp4`
  - `out1.avi`
  - `古い.wmv`
  - `映像なし_scale_2x_prob-3(1)_scale_2x_prob-3.mkv`
  - `画像1枚ありページ.mkv`
  - `画像1枚あり顔.mkv`

## 6. 次に見る順
1. true near-black 2件
   - 既に失敗形が安定しているため、閾値や `ffmpeg1pass` 逃がし条件の差分を見る
2. `インデックス破壊-093-2-4K.mp4`
   - 旧 `OTD-093-2-4K.mp4` 相当の no-frames 代表として追う
3. `35967.mp4`
   - 中間1枚抜き差分の既知ケースとして継続追跡する
4. `画像1枚あり顔.mkv` / `画像1枚ありページ.mkv`
   - short + repair + fallback の再現率を取る
5. `out1.avi` / `古い.wmv`
   - 旧前提では未重点だった拡張子を、除外候補か救済候補か切り分ける

## 7. 参照
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork_workthree\Tests\IndigoMovieManager_fork.Tests\DifficultVideoBatchPlaygroundTests.cs`
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork_workthree\Tests\IndigoMovieManager_fork.Tests\bin\x64\Debug\net8.0-windows\difficult-video-batch-summary-latest.csv`
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\優先順位表_workthree_失敗15件の検証順_2026-03-11.md`
