# 長期タスクリスト: サムネイルスレッド制御ロードマップ（2026-03-06）

## 0. 目的
- サムネイルスレッド制御まわりの中長期タスクを、実施順で管理する。
- 実コードベースで、今どこまで進んだかを追いやすくする。
- 順番にこなす前提で、各タスクの依存関係と完了条件を明確にする。

## 1. 運用ルール
- このリストは「上から順に処理」を原則とする。
- 状態判定は関連ドキュメントではなく、2026-03-07 時点の実コードを正として更新する。
- 各タスクは、着手前に `未着手`、作業中に `進行中`、完了後に `完了` へ更新する。
- 実装タスクは、原則としてコード上の到達点またはテスト追加まで確認できた時だけ完了とする。
- 設計・文書タスクは、対応するコード上の受け口または固定値が確認できる時だけ完了とする。

## 2. コード確認対象
- `CommonSettingsWindow.xaml`
- `CommonSettingsWindow.xaml.cs`
- `MainWindow.xaml`
- `MainWindow.xaml.cs`
- `Thumbnail/MainWindow.ThumbnailQueue.cs`
- `Thumbnail/ThumbnailThreadPresetResolver.cs`
- `Thumbnail/ThumbnailParallelController.cs`
- `src/IndigoMovieManager.Thumbnail.Queue/ThumbnailQueueProcessor.cs`
- `src/IndigoMovieManager.Thumbnail.Queue/ThumbnailProgressRuntime.cs`
- `src/IndigoMovieManager.Thumbnail.Queue/Ipc/*.cs`
- `Properties/Settings.settings`
- `Properties/Settings.Designer.cs`
- `App.config`
- `Tests/IndigoMovieManager_fork.Tests/*`

## 3. 補助ドキュメント
- `Thumbnail/要件定義_サムネイルスレッド制御とレーン設計_2026-03-06.md`
- `Thumbnail/仕様書_サムネイルスレッドプリセット設計_2026-03-06.md`
- `Thumbnail/Implementation Plan_サムネイルスレッド制御とレーン設計_実装計画兼タスクリスト_2026-03-06.md`
- `Thumbnail/設計メモ_サムネイルサービスIPC構成図_2026-03-06.md`
- `Thumbnail/設計メモ_共通管理者権限サービス基盤方針_2026-03-07.md`

## 4. 全体方針
- 先に設定とUIの骨格を固める。
- 次に、低負荷モードとレーン予約を安定化する。
- その後、高負荷縮退と復帰を内製メトリクスで成立させる。
- 最後に、IPCと管理者権限サービス連携へ広げる。
- 2026-03-07 時点の実コードでは、`slow / normal / ballence / fast / max / custum` の6プリセット、進捗タブの状態表示、`+ / -` による並列数変更、`HighLoadScore`、IPC基盤までは入っている。
- 同日時点の実コードでは、`idle` プリセットと、進捗タブのモード選択ドロップは未実装である。

## 5. 実施順ロードマップ

| 順番 | 長期ID | 状態 | 分類 | 内容 | 依存 | 完了条件 |
|---|---|---|---|---|---|---|
| 1 | LTH-001 | 完了 | 設定 | `ThumbnailThreadPreset` を設定として保存可能にする | なし | 設定保存と読込が成立する |
| 2 | LTH-001A | 完了 | 調査 | `CommonSettingsWindow` の既存 `FileIndexProvider` 3択UIとの衝突点を棚卸しする | なし | 共有設定画面の変更点と回帰観点が整理される |
| 3 | LTH-002 | 完了 | UI | 共有設定画面へプリセット選択UIを追加する | LTH-001, LTH-001A | `CommonSettingsWindow` から `slow / normal / ballence / fast / max / custum` を選べる |
| 4 | LTH-003 | 完了 | ロジック | プリセットから解決並列数を求める | LTH-001 | `slow / normal / ballence / fast / max / custum` が解決できる |
| 5 | LTH-004 | 完了 | 互換 | 既存手動並列数を `custum` へ移行する | LTH-001, LTH-003 | 既存設定を壊さず移行できる |
| 6 | LTH-005 | 完了 | 実行制御 | `slow` 用の低負荷モードを導入する | LTH-003 | ポーリング間隔延長とバッチ間クールダウンが効く |
| 7 | LTH-006 | 完了 | 実行制御 | 需要ベースのリカバリ枠予約を入れる | LTH-003 | 再試行対象がある時だけ予約される |
| 8 | LTH-007 | 完了 | 実行制御 | 需要ベースの巨大動画低速枠予約を入れる | LTH-003 | 巨大動画がある時だけ低速枠が有効になる |
| 9 | LTH-008 | 完了 | 制御統合 | 既存動的並列制御とプリセット起点の解決値を整合させる | LTH-005, LTH-006, LTH-007 | 並列縮退と復帰が競合しない |
| 10 | LTH-009 | 完了 | 設計 | 高負荷検知の内部メトリクス入力を確定する | LTH-008 | Error / QueuePressure / SlowBacklog / RecoveryBacklog / ThroughputPenalty が固定される |
| 11 | LTH-010 | 完了 | 実装 | `HighLoadScore` の内製版を実装する | LTH-009 | 内部メトリクスだけで縮退できる |
| 12 | LTH-011 | 完了 | 実装 | 縮退からの段階復帰とヒステリシスを実装する | LTH-010 | 境界で縮退と復帰が暴れない |
| 13 | LTH-012 | 完了 | ログ | `error` と `high-load` と `fallback` をログで区別する | LTH-010 | 抑制理由を追跡できる |
| 14 | LTH-013 | 完了 | UI | 進捗UIへレーン意味と現在制御状態を表示し、`+ / -` で並列数を変えられるようにする | LTH-008, LTH-012 | 進捗タブで `ThreadText` / `ControlStateText` / `LaneGuideText` と並列数ボタンが動く |
| 15 | LTH-013A | 未着手 | UI | 進捗タブへモード選択ドロップを追加する | LTH-013 | 進捗タブから `slow / normal / ballence / fast / max / custum` を切替できる |
| 16 | LTH-014 | 完了 | テスト | プリセット解決と予約条件の単体テストを追加する | LTH-008 | 主要分岐がテストで再現できる |
| 17 | LTH-015 | 完了 | テスト | `HighLoadScore` と復帰制御の単体テストを追加する | LTH-011 | 境界値とヒステリシスを確認できる |
| 18 | LTH-016 | 完了 | 手動確認 | 回帰確認手順を文書化する | LTH-013, LTH-015 | 手動確認シナリオが揃う |
| 19 | LTH-017 | 完了 | IPC設計 | IPC DTOを C# 型として固定する | LTH-010 | `record` と `enum` の責務が固まる |
| 20 | LTH-018 | 完了 | IPC設計 | IPC方式と接続方針を確定する | LTH-017 | `named pipe + length-prefixed UTF-8 JSON` と接続方針が固定される |
| 21 | LTH-019 | 完了 | サービス設計 | 管理者権限サービスの責務境界を確定する | LTH-018 | Disk温度, UsnMft, 権限境界が整理される |
| 22 | LTH-019A | 完了 | サービス設計 | `AdminUsnMft` とサムネイル高負荷検知の共通管理者権限基盤方針を決める | LTH-018 | UsnMft と Disk温度で別サービスを作らない方針が確定する |
| 23 | LTH-020 | 完了 | サービス連携 | サービス未接続フォールバックを実装する | LTH-018, LTH-019, LTH-019A | 未接続でも内部メトリクスで継続できる |
| 24 | LTH-021 | 完了 | サービス連携 | Disk温度シグナルを高負荷判定へ統合する | LTH-019, LTH-019A, LTH-020 | 温度危険時に即時縮退できる |
| 25 | LTH-022 | 完了 | サービス連携 | UsnMft状態を高負荷判定へ統合する | LTH-019, LTH-019A, LTH-020 | I/O監視状態を縮退判断へ反映できる |
| 26 | LTH-023 | 完了 | ログ | IPC失敗、権限不足、未接続を区別して記録する | LTH-020 | `unavailable` `access-denied` `timeout` を分離できる |
| 27 | LTH-023A | 完了 | ログ | FileIndexProvider異常とサムネイル高負荷を別軸で記録する | LTH-012, LTH-020 | `AdminRequired` と `high-load` を混同しない |
| 28 | LTH-024 | 完了 | 実装 | 高負荷係数と閾値を `Settings` 化して実測調整可能にする | LTH-021, LTH-022, LTH-023, LTH-023A | 係数と閾値をコード改修なしで調整できる |
| 29 | LTH-025 | 未着手 | 設定/UI | `idle` プリセットを設定値・共有設定画面・解決ロジックへ追加する | LTH-003, LTH-013A | `idle` を選択・保存・解決できる |
| 30 | LTH-026 | 未着手 | 実行制御 | `idle` 実行モードを実装する | LTH-025 | 実効並列1、プレビュー抑止、更新間引き、`Idle` 相当優先度が動く |
| 31 | LTH-027 | 未着手 | 手動確認 | `idle` の回帰確認手順を追加する | LTH-026 | 触感優先モードの確認観点が固定される |

## 6. 直近で着手する順

### 6.1 先頭バッチ
- `LTH-013A`
- `LTH-025`
- `LTH-026`

### 6.2 次バッチ
- `LTH-027`
- `LTH-024` の実測再調整運用

## 7. 今の着手対象
- 現在の先頭タスクは `LTH-013A`。
- `LTH-013` までは実コードで確認できる。
  - `MainWindow.xaml` に `ThreadText` / `ControlStateText` / `LaneGuideText` と `+ / -` ボタンがある。
  - `MainWindow.xaml.cs` に `ThumbnailParallelMinusButton_Click` / `ThumbnailParallelPlusButton_Click` がある。
- `LTH-013A` は未着手。
  - `MainWindow.xaml` には進捗タブ用のモード選択 `ComboBox` がまだ無い。
  - `MainWindow.xaml.cs` に進捗タブ用のプリセット変更イベントもまだ無い。
- `LTH-025` は未着手。
  - `ThumbnailThreadPresetResolver.cs` は `slow / normal / ballence / fast / max / custum` のみを持ち、`idle` を持たない。
  - `CommonSettingsWindow.xaml` のプリセット `ComboBox` も `idle` を持たない。
- `LTH-026` は未着手。
  - `ThumbnailQueueProcessor.cs` と `ThumbnailThreadPresetResolver.cs` には `Idle` 優先度、実効並列1、プレビュー抑止の実装が無い。
  - 現在の低負荷モードは `slow` だけである。
- `LTH-024` は実装済み。
  - `ThumbnailParallelController.cs` は係数・閾値を設定値から読む。
  - ただし、実測値での再調整運用は別途ログ採取を前提とする。

## 8. 完了の見方
- `LTH-001` から `LTH-013` 完了:
  - 共有設定画面、`slow` 中心のプリセット制御、進捗表示、並列数即時調整、動的縮退復帰、IPC基盤が成立した状態。
- `LTH-013A` 完了:
  - 進捗タブから設定画面へ戻らずに負荷モードを切り替えられる状態。
- `LTH-025` から `LTH-027` 完了:
  - `idle` を含む低干渉モードが実装され、確認手順まで固定された状態。

## 9. 更新ルール
- タスク完了時は、この文書の状態を更新する。
- 状態更新時は、該当コードの確認箇所も併記する。
- 補助ドキュメントと差分が出た場合でも、先にこのロードマップをコード基準へ更新する。
