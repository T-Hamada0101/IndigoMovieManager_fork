# 連絡用doc: workthree 救済条件の受け皿整理 `FailureDb.ExtraJson` 2026-03-11

## 1. 目的
- `workthree` で見つかった救済条件を、本線へ戻す前に記録できる受け皿を固定する。
- 本線側で先に `FailureDb.ExtraJson` の観測項目を整理し、戻し先の器不足を防ぐ。
- 個別動画の成功メモではなく、一般条件として比較・選別できる最小情報を揃える。

## 2. この文書の前提
- `workthree` は「失敗動画を実動画で検証し、成功パターンを本線へ戻す」ための専用ラインである。
- 本線側は探索を重複せず、`FailureDb` / 失敗タブ / 設計メモ / 実装計画の受け皿を整える役とする。
- `FailureDb` の土台実装と失敗タブ切替は完了済みである。

## 3. 本線側で今すぐ受けたい情報
- どの `engine` で成功したか
- どの `seek` 条件で成功したか
- `repair` が必要だったか
- `preflight` や事前判定で分岐したか
- 失敗時との差分が何か
- その条件が再現したか

## 4. `FailureDb.ExtraJson` に残したい共通項目

### 4.1 必須候補
- `failure_kind_source`
  - `queue` / `engine` / `playground`
- `material_duration_sec`
- `thumb_sec`
- `engine_attempted`
- `engine_succeeded`
- `seek_strategy`
  - 例: `original`, `midpoint`, `duration-override`, `manual`
- `seek_sec`
- `repair_attempted`
- `repair_succeeded`
- `preflight_branch`
  - 例: `none`, `flash`, `drm`, `unsupported-codec`, `short-clip-still-like`
- `result_signature`
  - 例: `no-frames-decoded`, `near-black`, `eof`, `timeout`
- `repro_confirmed`
  - `true/false`

### 4.2 あると有用な候補
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
  - 例: `retry`, `repair`, `ffmpeg1pass`, `manual-only`
- `decision_basis`
  - 短い文字列でよい

## 5. workthree から戻る時の最小フォーマット
- 動画ごとに最低限これだけ欲しい。

```json
{
  "movie_path": "E:\\sample\\35967.mp4",
  "failure_signature": "no-frames-decoded",
  "failed_engine": "autogen",
  "successful_engine": "ffmpeg1pass",
  "seek_strategy": "midpoint",
  "seek_sec": 1200,
  "repair_attempted": false,
  "repair_succeeded": false,
  "repro_confirmed": true,
  "proposed_integration_point": "retry-policy",
  "proposed_failure_kind": "TransientDecodeFailure",
  "notes": "長尺 autogen no-frames だが ffmpeg1pass で回復"
}
```

## 6. 本線側での使い道
- `FailureDb.ExtraJson`
  - 実測比較データの保存先
- `設計メモ_FailureKind_失敗分類と回復方針案_2026-03-09.md`
  - `FailureKind` と回復方針の更新先
- `Implementation Plan_サムネイルWorker完全責務移譲_長期計画_2026-03-08.md`
  - 本線ロードマップへの反映先
- `Implementation Plan_Queue実行状態分離とHangSuspected_実装計画兼タスクリスト_2026-03-10.md`
  - Queue / FailureDb / UI の接続タスク整理先

## 7. 本線側で今はやらないこと
- 動画名ベタ判定
- workthree の playground ロジックをそのまま本番へ持ち込むこと
- 失敗 9 件の個別救済を、本線側で先回り実装すること

## 8. 戻し方の基準
- 2件以上の実例、または代表 1 件で再現確認済み
- 条件がファイル名依存でない
- 本番導入位置が書ける
- 既存成功ケースを壊さない回帰テストを付けられる

## 9. 受領後に本線側で直ちにやること
1. `FailureKind` と `result_signature` の対応を整理する
2. `preflight / retry-policy / repair / finalizer` のどこへ入れるかを決める
3. `FailureDb.ExtraJson` へ残すキーを確定する
4. 必要なら失敗タブへ列追加する
5. 回帰テストか手動確認項目へ落とす

## 10. 関連
- [現状把握_workthree_失敗動画検証と本線反映方針_2026-03-11.md](./現状把握_workthree_失敗動画検証と本線反映方針_2026-03-11.md)
- [設計メモ_FailureKind_失敗分類と回復方針案_2026-03-09.md](./設計メモ_FailureKind_失敗分類と回復方針案_2026-03-09.md)
- [連絡用doc_サムネ失敗専用DB先行実装_完了連絡_2026-03-10.md](./連絡用doc_サムネ失敗専用DB先行実装_完了連絡_2026-03-10.md)
- [Implementation Plan_Queue実行状態分離とHangSuspected_実装計画兼タスクリスト_2026-03-10.md](./Implementation Plan_Queue実行状態分離とHangSuspected_実装計画兼タスクリスト_2026-03-10.md)
