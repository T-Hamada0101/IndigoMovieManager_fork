# Implementation Plan: workthree救済条件の本線受け取りとFailureDbExtraJson標準化 2026-03-11

## 0. 目的
- `workthree` で確定した救済条件を、本線へ安全に戻せる受け皿を先に整える。
- `FailureDb.ExtraJson` を、比較可能な観測情報の保存先として標準化する。
- 本線側では探索を重複せず、受領後すぐに `FailureKind`・導入位置・回帰テストへ落とせる状態を作る。

## 1. 前提
- `FailureDb` の土台実装は完了済み。
- `HangSuspected`、失敗タブ表示、観測スクリプト、関連設計メモはすでに存在する。
- `workthree` は実動画で失敗動画を再現し、成功条件を一般化する検証ラインとする。
- 本線では動画名判定を行わず、失敗パターンと成功条件の一般条件だけを取り込む。

## 2. 本線側で受け取る最小情報
- 動画ごとの失敗理由
- 成功した条件
- 再現率
- 本番導入位置
- 既存 `FailureKind` で足りるか

## 3. `FailureDb.ExtraJson` の標準キー方針
### 3.1 必須キー
- `failure_kind_source`
- `material_duration_sec`
- `thumb_sec`
- `engine_attempted`
- `engine_succeeded`
- `seek_strategy`
- `seek_sec`
- `repair_attempted`
- `repair_succeeded`
- `preflight_branch`
- `result_signature`
- `repro_confirmed`

### 3.2 任意キー
- `source_video_stream_count`
- `source_audio_stream_count`
- `source_has_video_stream`
- `movie_bitrate`
- `movie_resolution`
- `movie_fps`
- `frame_probe_position`
- `black_ratio`
- `placeholder_candidate`
- `recovery_route`
- `decision_basis`

### 3.3 JSON 運用ルール
- キー名は英小文字スネークケースで固定する。
- 値が不明な場合はキーを省略し、空文字を濫用しない。
- `movie_path` のような主キー相当は列側を優先し、`ExtraJson` へ重複保持しない。
- `workthree` 固有のメモは `notes` へ逃がしてよいが、本線判定に必須な情報は独立キーへ分離する。

## 4. 本番導入位置の切り分け方針
### 4.1 `preflight`
- DRM / SWF / 非対応コーデックのように、事前判定で確定できるもの。
- `preflight_branch` と `result_signature` の組み合わせで固定しやすい。

### 4.2 `retry policy`
- `TransientDecodeFailure` のように、同じ失敗でも `seek` や `engine` 切替で回復余地があるもの。
- 例: 長尺 `autogen` `No frames decoded` -> `ffmpeg1pass` 救済。

### 4.3 `repair workflow`
- `IndexCorruption` / `ContainerMetadataBroken` のように、repair の有無が結果を変えるもの。
- `repair_attempted` / `repair_succeeded` を必ず記録する。

### 4.4 `finalizer` 前
- `near-black` や `placeholder_candidate` のように、失敗固定の直前で扱いを変えるもの。
- `FinalFail` 固定前に追加観測や別 route を差し込む余地があるかを判定する。

## 5. `FailureKind` 判定方針
- 既存 `FailureKind` で説明できる場合は新設しない。
- 優先して当てはめる対象:
  - `TransientDecodeFailure`
  - `ContainerMetadataBroken`
  - `ShortClipStillLike`
  - `HangSuspected`
- 新設を検討する条件:
  - 既存分類では回復方針が分岐し過ぎる
  - `result_signature` と導入位置の対応が曖昧になる
  - UI / FailureDb / policy で同じ意味に保てない

## 6. 直ちに着手するタスク
| ID | 状態 | タスク | 変更先 | 完了条件 |
|---|---|---|---|---|
| WT-INT-001 | 着手 | `FailureDb.ExtraJson` 標準キー一覧を文書固定 | 本書, 関連設計メモ | 必須/任意キーが合意できる |
| WT-INT-002 | 着手 | `FailureKind` 観点の受領条件を設計メモへ反映 | `設計メモ_FailureKind_失敗分類と回復方針案_2026-03-09.md` | 既存分類で足りるかの判断軸が固定される |
| WT-INT-003 | 完了 | `ThumbnailQueueProcessor` の `ExtraJson` 生成箇所を棚卸し | `src/IndigoMovieManager.Thumbnail.Queue/ThumbnailQueueProcessor.cs` | 既存キーと不足キーの差分が出る |
| WT-INT-004 | 完了 | 失敗タブで必要な追加列を棚卸し | `Thumbnail/MainWindow.ThumbnailFailedTab.cs`, `ModelViews/ThumbnailFailedRecordViewModel.cs` | UI追加の有無を判断できる |
| WT-INT-005 | 完了 | 受領後に更新する計画書の流し先を固定 | 長期計画, HangSuspected 計画, 対策文書 | どの文書へ何を追記するかが決まる |
| WT-INT-006 | 完了 | `result_signature` / `recovery_route` の失敗タブ絞り込みを追加 | `MainWindow.xaml`, `Thumbnail/MainWindow.ThumbnailFailedTab.cs` | 受領後の比較対象をUIで即抽出できる |

## 7. 今はやらないこと
- `workthree` の playground ロジックをそのまま本番へ持ち込むこと
- 失敗9件の個別救済を、本線側で先回り実装すること
- 動画名ベタ判定

## 8. 受領後の最短フロー
1. `FailureDb.ExtraJson` のキー差分を確認する
2. `result_signature` と `FailureKind` の対応を決める
3. 導入位置を `preflight / retry policy / repair / finalizer` から1つ選ぶ
4. 最小実装と回帰テストへ落とす
5. 必要なら失敗タブ表示と観測スクリプトを追随更新する

## 9. 関連資料
- `Tests/IndigoMovieManager_fork.Tests/Fixtures/workthree_failuredb_minimal.json`
- `Thumbnail/現状把握_workthree受領後の計画書流し先整理_2026-03-11.md`
- `Thumbnail/現状把握_workthree_失敗動画検証と本線反映方針_2026-03-11.md`
- `Thumbnail/設計メモ_FailureKind_失敗分類と回復方針案_2026-03-09.md`
- `Thumbnail/連絡用doc_workthree救済条件の受け皿整理_FailureDbExtraJson_2026-03-11.md`
- `Thumbnail/Implementation Plan_Queue実行状態分離とHangSuspected_実装計画兼タスクリスト_2026-03-10.md`
- `Thumbnail/Implementation Plan_サムネイルWorker完全責務移譲_長期計画_2026-03-08.md`
