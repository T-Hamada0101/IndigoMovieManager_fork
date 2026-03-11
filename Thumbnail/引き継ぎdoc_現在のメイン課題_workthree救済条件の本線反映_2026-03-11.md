# 引き継ぎdoc: 現在のメイン課題 workthree救済条件の本線反映 2026-03-11

## 1. 現在のメイン課題
- 現在の本線メイン課題は、`workthree` 側で研究する難動画救済条件を、本線へ安全に戻せる形で反映すること。
- 重点は「探索」ではなく、「受け皿整備」と「最小反映位置の確定」。

## 2. 現在地
- `FailureDb` の土台実装は完了済み。
- `HangSuspected`、`Leased / Running / Hang` の観測、失敗タブ表示は本線へ反映済み。
- `FailureDb.ExtraJson` の受け皿キーは先行整理済み。
- `workthree` 側では、失敗 9 件の優先順位表まで作成済み。
- 本線は、`workthree` の実験結果を受けて最小実装へ落とす段階。

## 3. 直近の重要コミット
- `b7fca2b`
  - サムネ失敗調査基盤と workthree 受け皿を一括反映
- `ffabe9e`
  - ローカル固有情報の除去と調査結果を反映

## 4. 本線側で既に揃っている受け皿
- `FailureDb`
- `FailureKind`
- `HangSuspected`
- `FailureDb.ExtraJson`
- `サムネ失敗` タブ
- `trace_thumbnail_runtime.py`
- 関連計画書、設計メモ、実機確認チェックリスト

## 5. workthree 側から受けたい情報
- 対象動画群
- 成功した条件
- 失敗時との差分
- 再現率
- 本番導入位置の候補
  - `preflight`
  - `retry policy`
  - `repair workflow`
  - `finalizer` 前
- 既存 `FailureKind` で足りるか

## 6. 直近の優先対象
- `P1`
  - `near-black` 5件グループ
  - `画像1枚あり顔.mkv`
  - `画像1枚ありページ.mkv`
- `P2`
  - `ライブ配信真空エラー2_ghq5_temp.mp4`
  - `OTD-093-2-4K.mp4`
  - `ラ・ラ・ランド 1/2, 2/2`
- `P3`
  - 救済不能の明確化候補

## 6.1 2026-03-11 受領済みの短文化結論
- 先に本線へ戻す候補は 2 系統。
  - `35967.mp4 型`
  - `画像1枚あり顔.mkv 型`
- `35967.mp4 型`
  - 条件: `autogen / service` は `No frames decoded`、長尺、`ffmpeg midpoint` は成功
  - 入れ先: `retry policy`
  - 差し込み位置: `ThumbnailEngineExecutionCoordinator.ApplyPostExecutionFallbacksAsync(...)`
- `画像1枚あり顔.mkv 型`
  - 条件: `autogen / service` は `No frames decoded`、超短尺、極小 seek `0.001 / 0.01` だけ成功
  - 入れ先: `ffmpeg1pass` の短尺 fallback
  - 差し込み位置: `FfmpegOnePassThumbnailGenerationEngine`
- 今回まだ入れないもの
  - bitrate 閾値の決め打ち
  - `FailureKind` 新設
  - `画像1枚ありページ.mkv` の救済条件

## 7. 次にやること
1. `workthree` 側の掘り下げ結果を受領する。
2. `result_signature`、`recovery_route`、`decision_basis` を見て本線の導入位置を決める。
3. `FailureKind` を増やすか、既存分類へ寄せるか判断する。
4. 最小実装 + 回帰テスト + 関連 doc 更新まで同一コミット系列で入れる。

## 8. 今やらないこと
- 本線側で難動画救済の試行錯誤を二重に行うこと
- 動画名ベタ判定
- `workthree` 側の playground ロジックをそのまま本番へ持ち込むこと

## 9. 関連資料
- `Thumbnail/現状把握_workthree_失敗動画検証と本線反映方針_2026-03-11.md`
- `Thumbnail/連絡用doc_workthree救済条件の受け皿整理_FailureDbExtraJson_2026-03-11.md`
- `Thumbnail/設計整理_FailureDbExtraJson先行反映範囲_2026-03-11.md`
- `Thumbnail/設計メモ_FailureKind_失敗分類と回復方針案_2026-03-09.md`
- `Thumbnail/Implementation Plan_workthree救済条件の本線受け取りとFailureDbExtraJson標準化_2026-03-11.md`
- `Thumbnail/Implementation Plan_Queue実行状態分離とHangSuspected_実装計画兼タスクリスト_2026-03-10.md`
- `Thumbnail/Implementation Plan_サムネイルWorker完全責務移譲_長期計画_2026-03-08.md`
- `C:/Users/na6ce/source/repos/IndigoMovieManager_fork_workthree/Thumbnail/連絡用doc_workthree_本線向け短文化_35967型と顔型_2026-03-11.md`
- `Thumbnail/連絡用doc_サムネイルスレッド再構築計画向け_別ツリー成果要約_2026-03-11.md`
- `Thumbnail/優先順位表_サムネイルスレッド再構築計画向け_難読動画論点_2026-03-11.md`

## 10. 一言要約
- 今の本線課題は、`workthree` の成功条件を受けて、`FailureDb` と `FailureKind` を軸に最小実装へ戻すこと。
