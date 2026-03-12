# ManualCheck: Queue実行状態分離とHangSuspected 実機確認チェックリスト 2026-03-11

## 1. 目的
- `Leased / Running / HangSuspected` の状態分離が実機で追えることを確認する。
- 難動画で停滞した時に、本体 / 運転席 / QueueDB / FailureDb の観測結果が揃うことを確認する。
- `HangSuspected` 初回 recovery 戻しと、失敗タブの専用DB表示が現行仕様どおり動くことを確認する。

## 2. 事前準備
- Debug ビルド済みの `IndigoMovieManager_fork.exe` を用意する。
- 対象 MainDB を 1 つ決める。
- 難動画サンプルを用意する。
  - 推奨: `<difficult-video-root>`
- 必要なら normal lane の早期退避秒数を設定する。
  - 既定: `10秒`
  - 上書き環境変数: `IMM_THUMB_NORMAL_LANE_TIMEOUT_SEC`
  - 例: PowerShell セッションだけ `15秒` にする場合は `$env:IMM_THUMB_NORMAL_LANE_TIMEOUT_SEC='15'`
  - 補助スクリプト: `Thumbnail\Test\run_normal_lane_timeout_manual.ps1 -TimeoutSec 15 -LaunchApp`
- 必要なら `thumbnail-progress-viewer` を起動できる状態にする。
- `FailureDb` の保存先を確認しておく。
  - `%LOCALAPPDATA%\IndigoMovieManager_fork\thumbnail-failure-db\*.failure-debug.imm`

## 3. 確認観点
- 本体サムネイルタブに `leased / running / hang` が表示される。
- 外側運転席に `queued / leased / running / hang` が表示される。
- `trace_thumbnail_runtime.py` で `queued / leased / running / hang` を採れる。
- normal lane timeout 時に通常 job が救済へ自動移送される。
- `HangSuspected` 初回は recovery レーンへ戻る。
- `サムネ失敗` タブ active 時に FailureDb の内容を表示する。

## 4. 手順

### 4.1 基本表示確認
1. 本体を起動し、対象 MainDB を開く。
2. サムネイルタブを開く。
3. `leased / running / hang` 行が表示されることを確認する。
4. 運転席を開く。
5. `queued / leased / running / hang` 行が表示されることを確認する。
6. 運転席の `CoordinatorReason` に `lease=` と `hang=` が出ることを確認する。

### 4.2 難動画投入確認
1. 難動画を複数件登録し、サムネイル生成を開始する。
2. 運転席の `queued / leased / running / hang` が変化することを確認する。
3. 本体タブの `leased / running / hang` が同じ方向に変化することを確認する。
4. `leased` だけ増えて `running` が 0 のまま固定しないことを確認する。

### 4.3 停滞観測確認
1. 別ターミナルで下記を実行する。

```powershell
python scripts\trace_thumbnail_runtime.py --main-db "<対象MainDBフルパス>" --duration 60 --interval 2
```

2. 出力ログに以下が含まれることを確認する。
  - `control state=... q=... lease=... run=... hang=...`
  - `queue totals queued=... leased=... running=... hang=...`
  - `queue oldest_leased=...`
  - `queue oldest_running=...`
3. `StartedAtUtc` がある QueueDB では、`leased` と `running` が分かれて出ることを確認する。

### 4.4 normal lane 早期退避確認
1. 停滞しやすい難動画を 1 件以上投入する。
2. `debug-runtime.log` を確認し、normal 側で下記が出ることを確認する。
  - `thumbnail-timeout`
  - `normal lane timeout handoff`
3. 続けて下記が出ることを確認する。
  - `thumbnail-recovery`
  - `recovery scheduled by force-reset` または `recovery scheduled by enqueue`
4. その後の Queue 再取得で recovery レーンへ戻ることを確認する。
5. `IMM_THUMB_NORMAL_LANE_TIMEOUT_SEC` を `10 -> 15 -> 20` と変えた時に、timeout 発火頻度が変わることを確認する。
6. recovery で `ffmpeg1pass` が `exit success` でも出力無しだった場合、下記の並びで `opencv` へ落ちることを確認する。
  - `ffmpeg1pass-output`
  - `engine failed: category=error id=ffmpeg1pass`
  - `engine fallback: category=fallback from=ffmpeg1pass, to=opencv`
  - `execution flow end ... success=True`
  - `repair success`

### 4.5 HangSuspected 回復確認
1. 難動画のうち停滞しやすいものを対象にする。
2. `HangSuspected` が発生したら、FailureDb を確認する。
3. 初回の記録で下記を確認する。
  - `FailureKind = HangSuspected`
  - `Reason = hang-recovery-scheduled`
  - `QueueStatus = Pending`
  - `ExtraJson.AttemptCountAfter = 2`
4. その後の Queue 再取得で recovery レーンへ戻ることを確認する。
5. 同一動画で recovery 再発した場合は、`Reason = final-failed` と `QueueStatus = Failed` になることを確認する。

### 4.6 サムネ失敗タブ確認
1. `サムネ失敗` タブを active にする。
2. FailureDb 由来の一覧へ切り替わることを確認する。
3. 下記の列が表示されることを確認する。
  - `FailureKind`
  - `KindSource`
  - `WasRunning`
  - `StartedAtUtc`
  - `AttemptAfter`
  - `MovieExists`
  - `ResultStage`
  - `PolicyDecision`
  - `PlaceholderAction`
  - `FinalizerAction`
4. `HangSuspected` 行の `Reason` と `ExtraJson` 由来列が一致することを確認する。

## 5. 期待結果
- 本体と運転席の両方で停滞要約を読める。
- QueueDB の `Processing` は `leased` と `running` の内訳で追える。
- `trace_thumbnail_runtime.py` の観測結果と UI 表示が大きく矛盾しない。
- normal lane timeout は通常 job だけに掛かり、timeout 時は recovery へ自動移送される。
- `HangSuspected` 初回は recovery 戻し、再発時は `Failed` になる。
- `サムネ失敗` タブは QueueDB ではなく FailureDb の内容を表示する。

## 6. 異常時の確認ポイント
- `leased` だけ増え続ける:
  - `trace_thumbnail_runtime.py` の `oldest_leased`
  - QueueDB の `StartedAtUtc`
- `running` が増えない:
  - `MarkLeaseAsRunning(...)` 到達有無
  - worker health の `LastHeartbeatUtc`
- timeout しても recovery へ戻らない:
  - `thumbnail-timeout`
  - `thumbnail-recovery`
  - `IMM_THUMB_NORMAL_LANE_TIMEOUT_SEC`
- recovery success 後に同じ Queue をまた取り直す:
  - `repair success`
  - 直後の `consumer lease: acquired`
  - `queue_id` が同一か
  - `debug-runtime.log` の watch 側 `enqueue accepted` / `missing-thumb rescue`
- `hang` が増えない:
  - `LastError`
  - `FailureDb` の `FailureKind`
- 失敗タブに出ない:
  - FailureDb path 解決
  - active 時再読込
  - `MainDbFullPath` 一致

## 7. 確認後に残すもの
- `trace_thumbnail_runtime.py` の出力ログ
- 該当 FailureDb の対象行
- 必要なら `debug-runtime.log`

## 8. 関連
- [Implementation Plan_Queue実行状態分離とHangSuspected_実装計画兼タスクリスト_2026-03-10.md](./Implementation Plan_Queue実行状態分離とHangSuspected_実装計画兼タスクリスト_2026-03-10.md)
- [現状把握_サムネ失敗動画リカバリーフロー_2026-03-09.md](./現状把握_サムネ失敗動画リカバリーフロー_2026-03-09.md)
- [設計メモ_FailureKind_失敗分類と回復方針案_2026-03-09.md](./設計メモ_FailureKind_失敗分類と回復方針案_2026-03-09.md)
