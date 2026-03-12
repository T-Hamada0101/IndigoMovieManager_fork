# Implementation Plan_FfmpegAutoGen_改善プラン_2026-03-11

## 1. 今回の対象
- 対象リポジトリは `IndigoMovieManager_fork`
- 今回は次の 3 点だけに絞る
  - `EAGAIN` 修正
  - 1枚目初回失敗時の `短尺極小 seek 型` への切り替え
  - 1枚でも取得できたらサムネイルを作成する方針の明文化

## 2. 目的
- `autogen` 自体の取りこぼしを減らす
- `No frames decoded` のうち、`autogen` 実装起因で落としている分を減らす
- 修正範囲を小さく保ち、まずは本体の成功率を上げる

## 3. 背景

### 3.1 現在の問題
- `avcodec_receive_frame(...) == EAGAIN` で、その場の取得試行を早く打ち切っている
- 短尺動画では、通常の自動算出秒より `0.001` や `0.01` のような先頭極小 seek だけ成功する群がある
- `SaveCombinedThumbnail(...)` は `1枚以上` あれば保存できるが、`autogen` 側の取得戦略はその前提を十分活かしていない

### 3.2 今回の前提
- 破損動画の repair 分岐や `partial file` 判定は今回は扱わない
- 初期化遅延化や `CanHandle` 改善も今回は入れない
- まずは `autogen` 本体の最小修正で成功率を上げる

## 4. 実装方針

### 4.1 `EAGAIN` 修正
- 対象:
  - `FfmpegAutoGenThumbnailGenerationEngine.CreateInternal`
  - `FfmpegAutoGenThumbnailGenerationEngine.CreateBookmarkInternal`
- 方針:
  - `avcodec_receive_frame(...)` が `EAGAIN` を返したら、その秒の取得を失敗扱いで終わらせない
  - 次の packet 供給へ戻す
  - `avcodec_send_packet(...)` / `avcodec_receive_frame(...)` の往復を、FFmpeg の期待どおり継続する
- 狙い:
  - packet 供給不足の一時状態を `No frames decoded` に誤変換しない

### 4.2 1枚目初回失敗時の `短尺極小 seek 型` 切り替え
- 対象:
  - `CreateInternal` の capture 秒列処理
- 発動条件:
  - 最初の capture 秒で 1 枚も取れなかった
  - まだ `bitmaps.Count == 0`
  - 動画が短尺である
- 追加する fallback 秒候補:
  - `0.001`
  - `0.01`
  - `0.016`
  - 必要なら `0.033`
- 方針:
  - 通常の1枚目が取れなかったときだけ、短尺動画に限って先頭極小 seek 候補を浅く試す
  - 既存の通常秒列を全面置換しない
  - 極小 seek 候補は少数に限定し、無駄な総当たりにしない
- 狙い:
  - `画像1枚あり顔.mkv` 型の「通常秒は失敗するが先頭極小 seek だけ成功」を拾う

### 4.3 1枚でも取得したら作成
- 現状確認:
  - `ThumbnailImageUtility.SaveCombinedThumbnail(...)` は `frames.Count >= 1` で保存可能
  - 足りないコマは黒背景のまま残る
- 今回の方針:
  - `autogen` 側でも `1枚以上取れたら成功扱い` を前提に処理を組む
  - 先頭1枚目が失敗しても、その後または fallback 秒で 1 枚でも取れたら保存へ進む
  - 「全予定秒が揃わないと失敗」の発想で分岐を増やさない
- 狙い:
  - `完全成功でなくてもサムネイルとして使える` ケースを救う

### 4.4 近傍の非黒優先 + latest bright fallback
- 更新日:
  - 2026-03-11
- 変更概要:
  - 短尺・少パネル時だけ、`要求秒に整合する非黒フレーム` を最優先にする
  - 近傍が黒しかない時は、取得済みの `latest bright` を最後の救済候補として採用する
- 方針:
  - 通常動画へは広げない
  - 既存の `短尺` 判定を流用し、回帰面積を増やしすぎない
  - `暗いけど要求秒に近い` より `見える1枚` を優先したい短尺難読動画だけ対象にする
- 狙い:
  - 短尺難読動画で、黒コマ即採用による取りこぼしを減らす
  - `毎回 latest bright` にはせず、通常動画の代表性は維持する

## 5. 実装箇所
- [FfmpegAutoGenThumbnailGenerationEngine.cs](./FfmpegAutoGenThumbnailGenerationEngine.cs)
  - デコードループ修正
  - 短尺極小 seek fallback 追加
  - `1枚以上取得で成功` の意図が読めるように整理
- [ThumbnailImageUtility.cs](../ThumbnailImageUtility.cs)
  - 変更不要の想定
  - `SaveCombinedThumbnail(...)` の既存仕様を前提利用

## 6. 実装順
1. `EAGAIN` 修正を入れる
2. 最初の capture 秒だけに限定した短尺極小 seek fallback を追加する
3. `bitmaps.Count > 0` なら成功、という意図がコード上で明確になるよう整理する

## 7. テスト観点

### 7.1 単体観点
- `EAGAIN` を返しても即 `No frames decoded` にならないこと
- 最初の capture 秒失敗後に、短尺時だけ極小 seek 候補へ進むこと
- 長尺動画では極小 seek fallback が不用意に走らないこと
- 1枚だけ取得できた場合でも保存成功になること

### 7.2 実動画観点
- `画像1枚あり顔.mkv`
  - 極小 seek fallback で回復できること
- `画像1枚ありページ.mkv`
  - 同じ fallback を入れても安易に成功扱いにならないこと
- `35967.mp4`
  - 今回は本命対象外
  - ただし `EAGAIN` 修正だけで改善するかは確認する価値がある

## 8. 注意点
- 今回は `repair` 連携を混ぜない
- `partial file` 判定も今回の範囲外
- 短尺極小 seek fallback は「最初の1枚失敗時のみ」に限定して広げすぎない
- 長尺動画へ同じ fallback を広げるのは別検討にする

## 9. 完了条件
- `EAGAIN` 修正が入っている
- 短尺動画で、1枚目初回失敗時のみ極小 seek 候補へ切り替わる
- 1枚でも取得できたケースで `autogen success` になる
- 短尺・少パネル時だけ、近傍の非黒優先 + latest bright fallback が働く
- 既存の長尺動画群を不必要に極小 seek へ流さない

## 10. ひとことで言うと
- 今回は `autogen` の基礎的な取りこぼしだけを減らす
- `EAGAIN` を正し、短尺の先頭極小 seek を最小限だけ足し、1枚でも取れたらサムネイル化する
