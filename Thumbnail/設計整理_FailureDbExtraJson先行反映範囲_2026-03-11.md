# 設計整理: `FailureDb.ExtraJson` 先行反映範囲 2026-03-11

## 1. 目的
- workthree から戻る救済条件を受ける前に、本線側で `FailureDb.ExtraJson` の先行反映範囲を固定する。
- 今すぐ本線へ入れるキーと、workthree 受領後に埋めるキーを分ける。
- `Queue / FailureDb / 失敗タブ / 設計メモ` の接続面を先に揃える。

## 2. 現状
- `FailureDb` の record 列本体は実装済み。
- `ExtraJson` には現在、主に Queue 本番経路の観測値が入っている。
- 代表的な既存キー:
  - `Reason`
  - `MainDbFullPath`
  - `AttemptCountAfter`
  - `NextStatus`
  - `FailureKindSource`
  - `WasRunning`
  - `MovieExists`
  - `LeaseUntilUtc`
  - `StartedAtUtc`
  - `ExceptionType`
  - `ExceptionMessage`
  - `ResultErrorMessage`
  - `ResultFailureStage`
  - `ResultPolicyDecision`
  - `ResultPlaceholderAction`
  - `ResultPlaceholderKind`
  - `ResultFinalizerAction`
  - `ResultFinalizerDetail`

## 3. 問題
- workthree 側で見つけるのは「成功条件」だが、今の `ExtraJson` は「失敗時点の Queue 観測」に寄っている。
- このままだと、`seek` 条件や `repair` 条件、`engine` 切替条件を本線側で比較しにくい。
- ただし、今ここで実動画探索ロジックまで本線へ入れるのは責務過多になる。

## 4. 設計方針
- 先に本線へ入れるのは「器」だけにする。
- 候補キーは3層に分ける。
  - `A: 今すぐ Queue 本番経路で埋められる`
  - `B: 本番で将来埋めたいが、現時点では値源が弱い`
  - `C: workthree / playground 由来としてのみ先に使う`
- 本線側では、`A` だけ先行反映してよい。
- `B` と `C` は doc と `FailureDb` の許容だけ先に固定し、値投入は後回しにする。

## 5. 層別整理

### 5.1 A: 今すぐ Queue 本番経路で埋められる
- `failure_kind_source`
  - 現行の `FailureKindSource`
- `result_signature`
  - まずは `ExceptionMessage` と `ResultErrorMessage` の正規化短縮版
- `recovery_route`
  - `retry`
  - `hang-recovery`
  - `final-failed`
  - `placeholder`
  - `manual-only`
- `decision_basis`
  - `ResultPolicyDecision` と `ResultFailureStage` の要約
- `repair_attempted`
  - `ResultFailureStage` や `PolicyDecision` から判定可能な範囲
- `preflight_branch`
  - `ResultPlaceholderKind` や `ResultPolicyDecision` から判定可能な範囲

### 5.2 B: 本番で将来埋めたい
- `material_duration_sec`
- `thumb_sec`
- `engine_attempted`
- `engine_succeeded`
- `seek_strategy`
- `seek_sec`
- `repair_succeeded`
- `source_video_stream_count`
- `source_audio_stream_count`
- `source_has_video_stream`
- `movie_bitrate`
- `movie_resolution`
- `movie_fps`
- `frame_probe_position`
- `black_ratio`
- `placeholder_candidate`

理由:
- 値源が `ThumbnailJobMaterialBuilder`、engine 実行結果、probe 結果にまたがる。
- 今の本線で無理に埋めると、逆に責務が分散する。

### 5.3 C: workthree / playground 先行
- `repro_confirmed`
- `failed_engine`
- `successful_engine`
- `proposed_integration_point`
- `proposed_failure_kind`
- `notes`

理由:
- 検証ラインから戻る判断材料であり、本番 runtime が直接持つ値ではない。

## 6. 先行実装ルール
- `ExtraJson` の schema は厳密固定しない。
- ただしキー名はここで固定し、表記ゆれを防ぐ。
- 本線の Queue 経路では `A` のみを追加対象にする。
- `B` は値源が複数層にまたがるため、workthree 側の優先順位表を受けてから導入する。
- `C` は `FailureDb` へ直接書くのではなく、連絡docや playground 出力から本線設計へ転記する。

## 7. 本線での導入位置

### 7.1 `ThumbnailQueueProcessor`
- `A` の主な投入先
- `BuildFailureExtraJson(...)` の追加先候補:
  - `ResultSignature`
  - `RecoveryRoute`
  - `DecisionBasis`
  - `RepairAttempted`
  - `PreflightBranch`

### 7.2 `ThumbnailCreationService` / `ThumbnailCreateResult`
- `B` の値源を将来受ける中継点
- `engine_attempted`
- `seek_strategy`
- `seek_sec`
- `repair_attempted`
- `repair_succeeded`

### 7.3 `ThumbnailJobMaterialBuilder`
- `B` のうち素材情報
- `material_duration_sec`
- `movie_bitrate`
- `movie_resolution`
- `movie_fps`

### 7.4 失敗タブ
- すぐ列へ出すのは `A` だけでよい
- `B` は比較に意味が出た時点で追加

## 8. 優先順位
1. `A` を doc 上で固定
2. workthree 側から優先順位表を受ける
3. `A` のうち有効なものだけ本番 insert に反映
4. `B` のうち複数動画で効いた条件だけ実装

## 9. 今回の結論
- 今すぐ本線へ入れてよいのは `A` だけ
- `B` はまだ設計予約
- `C` は本線実装対象ではなく、workthree からの受領資料用

## 9.1 2026-03-11 実装反映メモ
- `A` のうち、以下は `ThumbnailQueueProcessor.BuildFailureExtraJson(...)` へ先行反映済み。
  - `result_signature`
  - `recovery_route`
  - `decision_basis`
  - `repair_attempted`
  - `preflight_branch`
- 反映は `FailureDb.ExtraJson` の `PascalCase` / `snake_case` 両キーへ揃えている。
- `サムネ失敗` タブ側の復元ロジックも `snake_case` を許容済み。
  - `thumb_panel_pos`
  - `thumb_time_pos`
  - `was_running`
  - `attempt_count_after`
  - `movie_exists`
  - `result_failure_stage`
  - `result_policy_decision`
  - `result_placeholder_action`
  - `result_placeholder_kind`
  - `result_finalizer_action`
  - `result_finalizer_detail`
- 正規化ルールは本線実装で固定済みだが、workthree 側の実測で有意な差分が出た場合はここを更新して追随する。

## 10. 関連
- [連絡用doc_workthree救済条件の受け皿整理_FailureDbExtraJson_2026-03-11.md](/c:/Users/na6ce/source/repos/IndigoMovieManager_fork/Thumbnail/%E9%80%A3%E7%B5%A1%E7%94%A8doc_workthree%E6%95%91%E6%B8%88%E6%9D%A1%E4%BB%B6%E3%81%AE%E5%8F%97%E3%81%91%E7%9A%BF%E6%95%B4%E7%90%86_FailureDbExtraJson_2026-03-11.md)
- [現状把握_workthree_失敗動画検証と本線反映方針_2026-03-11.md](/c:/Users/na6ce/source/repos/IndigoMovieManager_fork/Thumbnail/%E7%8F%BE%E7%8A%B6%E6%8A%8A%E6%8F%A1_workthree_%E5%A4%B1%E6%95%97%E5%8B%95%E7%94%BB%E6%A4%9C%E8%A8%BC%E3%81%A8%E6%9C%AC%E7%B7%9A%E5%8F%8D%E6%98%A0%E6%96%B9%E9%87%9D_2026-03-11.md)
- [設計メモ_FailureKind_失敗分類と回復方針案_2026-03-09.md](/c:/Users/na6ce/source/repos/IndigoMovieManager_fork/Thumbnail/%E8%A8%AD%E8%A8%88%E3%83%A1%E3%83%A2_FailureKind_%E5%A4%B1%E6%95%97%E5%88%86%E9%A1%9E%E3%81%A8%E5%9B%9E%E5%BE%A9%E6%96%B9%E9%87%9D%E6%A1%88_2026-03-09.md)
