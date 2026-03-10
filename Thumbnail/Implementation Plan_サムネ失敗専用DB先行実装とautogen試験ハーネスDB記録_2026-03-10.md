# Implementation Plan + tasklist（サムネ失敗専用DB先行実装とautogen試験ハーネスDB記録, 2026-03-10）

## 0. 目的
- サムネ失敗専用DBの土台を `Queue` 側へ先行実装する。
- UI切替前でも、Debug用途の失敗履歴を MainDB 単位で蓄積できる状態にする。
- `AutogenRepairPlaygroundTests` からも同じDBへ失敗記録を書き、実動画検証結果を履歴化する。

## 1. 前提
- MainDB (`*.wb`) の schema は変更しない。
- QueueDB の既存遷移や lease 制御は壊さない。
- 今回は `FailureDb` の土台と試験ハーネス連携までを対象とし、UI切替は後続とする。
- `DebugMode` 常時挿入の運用判定は呼び出し側で持ち、service 自体は常時利用可能にする。

## 2. 実装方針
- 配置は `src/IndigoMovieManager.Thumbnail.Queue/FailureDb/*` に集約する。
- path 解決は QueueDB と同じ `MainDbFullPath` 正規化 + hash8 を使う。
- DB は append 専用で始め、まずは `InsertFailureRecord` / `GetFailureRecords` を固める。
- `FailureKind` は文字列解析に戻らないよう enum で受け、DB には固定名で保存する。
- `AutogenRepairPlaygroundTests` は失敗した `autogen` 試行だけを記録する。

## 3. 実装対象
- `src/IndigoMovieManager.Thumbnail.Queue/FailureDb/ThumbnailFailureDebugDbPathResolver.cs`
- `src/IndigoMovieManager.Thumbnail.Queue/FailureDb/ThumbnailFailureDebugDbSchema.cs`
- `src/IndigoMovieManager.Thumbnail.Queue/FailureDb/ThumbnailFailureDebugDbService.cs`
- `src/IndigoMovieManager.Thumbnail.Queue/FailureDb/ThumbnailFailureRecord.cs`
- `Tests/IndigoMovieManager_fork.Tests/ThumbnailFailureDebugDbTests.cs`
- `Tests/IndigoMovieManager_fork.Tests/AutogenRepairPlaygroundTests.cs`

## 4. スキーマ方針
- テーブル名: `ThumbnailFailureDebug`
- 主キー: `RecordId`
- 必須列:
  - `DbName`
  - `MainDbFullPath`
  - `MainDbPathHash`
  - `MoviePath`
  - `MoviePathKey`
  - `PanelType`
  - `MovieSizeBytes`
  - `Reason`
  - `FailureKind`
  - `AttemptCount`
  - `OccurredAtUtc`
  - `UpdatedAtUtc`
- 追加列:
  - `Duration`
  - `TabIndex`
  - `OwnerInstanceId`
  - `WorkerRole`
  - `EngineId`
  - `QueueStatus`
  - `LeaseUntilUtc`
  - `StartedAtUtc`
  - `LastError`
  - `ExtraJson`

## 5. API方針
- `ResolveFailureDbPath(mainDbFullPath)`
- `InsertFailureRecord(record)`
- `GetFailureRecords()`
- 取得順は `OccurredAtUtc DESC, RecordId DESC`

## 6. autogen試験ハーネス連携
- `tempRoot/autogen-playground.wb` を擬似 MainDB として service を初期化する。
- `original / duration override / repaired-autogen / prepared-recovery-autogen / forced-recovery-autogen`
  の各 `autogen` 試行のうち、失敗したものだけを `FailureDb` へ append する。
- `Reason` には試行名、`EngineId` には `autogen`、`WorkerRole` には `explicit-test` を入れる。
- `ExtraJson` へ `ThumbSec` や override 情報を逃がす。

## 7. 受け入れ基準
- MainDB ごとに一意な failure DB path が解決できる。
- 失敗レコードを append できる。
- `GetFailureRecords()` で新しい順に返る。
- `AutogenRepairPlaygroundTests` 実行時に failure DB path が表示され、失敗時は件数が増える。
- 既存 QueueDB テストと明確に分離されている。

## 8. タスクリスト

| ID | 状態 | タスク | 対象 | 完了条件 |
|---|---|---|---|---|
| FDB-001 | 完了 | FailureDb path resolver を追加 | `FailureDb/ThumbnailFailureDebugDbPathResolver.cs` | MainDB 単位の `.failure-debug.imm` が解決できる |
| FDB-002 | 完了 | FailureKind enum と record DTO を追加 | `FailureDb/ThumbnailFailureRecord.cs` | 必須列を表す DTO が定義される |
| FDB-003 | 完了 | schema を追加 | `FailureDb/ThumbnailFailureDebugDbSchema.cs` | SQLite テーブルと index を生成できる |
| FDB-004 | 完了 | service を追加 | `FailureDb/ThumbnailFailureDebugDbService.cs` | insert / get が動く |
| FDB-005 | 完了 | service の単体テストを追加 | `ThumbnailFailureDebugDbTests.cs` | path / insert / sort を確認できる |
| FDB-006 | 完了 | autogen試験ハーネスから failure DB へ記録 | `AutogenRepairPlaygroundTests.cs` | 失敗試行が DB へ append される |
| FDB-007 | 完了 | 本計画書を追加 | 本ファイル | 後続が参照できる |
| FDB-008 | 未完了 | Queue本番経路の DebugMode insert 接続 | `ThumbnailQueueProcessor.cs` ほか | 実運用失敗も専用DBへ入る |
| FDB-009 | 未完了 | サムネ失敗タブの専用DB切替 | UI 側 | QueueDB 直読を置き換える |

## 9. リスク
- `FailureKind` の最終 enum は将来 Engine 側へ寄せ直す可能性がある。
- 現在は append 専用なので、履歴増加に対する retention は未実装。
- `AutogenRepairPlaygroundTests` は実動画依存のため、failure DB 件数は入力動画によって変わる。

## 10. 次段
- `ThumbnailQueueProcessor` / finalizer / hang suspected 分岐から `FailureDb` へ接続する。
- `サムネ失敗` タブを `FailureDb` 正読みに切り替える。
- `FailureKind` を Engine 側の正式判定へ寄せる。
