# Implementation Plan: 本体と運転席 接続点先行実装（2026-03-09）

## 1. 目的
- `MainWindow` 側と外側のサムネイル運転席側を、互いの実装待ち無しで並列開発できる状態にする。
- そのために、見た目や内部実装より先に「接続点」を固定する。
- 今回は Coordinator 本体の完成ではなく、接続契約の先行実装を終えることを目的にする。

## 2. この計画の結論
- 先に固めるべき接続点は `4` つである。
  - process 起動契約
  - control snapshot 契約
  - command snapshot 契約
  - MainWindow 要約表示契約
- ここを先に実装すれば、本体側は「Coordinator がいる前提の入口」と要約UIを進められる。
- 同時に運転席側は、Coordinator と外側UIの中身を本体から独立して作り込める。

## 3. 背景
- 現状は `progress` と `health` の外部 snapshot はあるが、制御面の外部契約はまだ無い。
- このため、運転席側を作ろうとすると `MainWindow` 側の設定反映や起動方式が毎回揺れる。
- 逆に本体側も、Coordinator の未完成を理由に UI の接続点が確定しない。
- 先に契約を固定しないと、両側が同じ論点を何度も実装し直すことになる。

## 4. 今回のスコープ
- 接続契約の新設
- 契約を読み書きする最小実装
- `MainWindow` から見た Coordinator の起動口と要約表示口の追加
- 外側運転席から見た command/control の入口確保

## 5. 今回やらないこと
- Coordinator の本格スケジューラ完成
- Worker executor 化
- queue 分類ロジックの全面移設
- 外側運転席 UI の完成
- `MainWindow` の既存設定UIの大整理

## 6. 並列開発の前提

### 6.1 本体側が先に必要とするもの
- Coordinator を起動する方法
- Coordinator が今どういう状態かを要約表示する方法
- 外側運転席を開く入口

### 6.2 運転席側が先に必要とするもの
- 本体から渡される DB / owner / parent の起動契約
- 変更要求を書き出す command 契約
- 現在値を返す control 契約

### 6.3 先に凍結すべき理由
- ここが未確定だと、両側で別形式の JSON や別引数を仮実装して破綻する
- UI文言や内部ロジックは後で変えられるが、接続点は後から変えると両側修正になる

## 7. 固定する接続点

### 7.1 Process 起動契約
- 新規対象:
  - `IndigoMovieManager.Thumbnail.Coordinator.exe`
- 起動引数は最小限に絞る
  - `--main-db`
  - `--db-name`
  - `--owner`
  - `--parent-pid`
  - `--initial-settings-snapshot`

### 7.2 Control Snapshot 契約
- ファイル名:
  - `thumbnail-control-<owner>.json`
- 用途:
  - Coordinator が現在の希望値・実効値・モード・理由を返す
- 読み手:
  - `MainWindow`
  - 外側運転席 UI

### 7.3 Command Snapshot 契約
- ファイル名:
  - `thumbnail-command-<owner>.json`
- 用途:
  - 本体または外側運転席 UI が、Coordinator へ変更要求を渡す
- 書き手:
  - `MainWindow`
  - 外側運転席 UI
- 読み手:
  - Coordinator

### 7.4 Summary 表示契約
- `MainWindow` は次の4行だけを正として表示する
  - Coordinator 状態
  - progress 要約
  - health 要約
  - control 要約
- 本体は Coordinator の内部判断を再計算しない

## 8. データ契約

### 8.1 control snapshot
- `SchemaVersion`
- `OwnerInstanceId`
- `MainDbFullPath`
- `DbName`
- `CoordinatorState`
- `RequestedParallelism`
- `TemporaryParallelismDelta`
- `EffectiveParallelism`
- `LargeMovieThresholdGb`
- `GpuDecodeEnabled`
- `OperationMode`
- `FastSlotCount`
- `SlowSlotCount`
- `ActiveWorkerCount`
- `ActiveFfmpegCount`
- `Reason`
- `UpdatedAtUtc`

### 8.2 command snapshot
- `SchemaVersion`
- `OwnerInstanceId`
- `MainDbFullPath`
- `RequestedParallelism`
- `TemporaryParallelismDelta`
- `LargeMovieThresholdGb`
- `GpuDecodeEnabled`
- `OperationMode`
- `IssuedAtUtc`
- `IssuedBy`

### 8.3 coordinator state 値
- `starting`
- `running`
- `degraded`
- `stopped`
- `start-failed`

### 8.4 operation mode 値
- `normal-first`
- `power-save`
- `recovery-first`

## 9. 実装方針

### 9.1 本体側
- Coordinator 専用 `ProcessManager` を追加する
- `MainWindow` は Worker ではなく Coordinator を監視する入口を持つ
- 下部要約タブへ control 要約行を追加できる構造だけ先に入れる
- 既存の並列数UIは当面残してよいが、最終反映は command file 経由に寄せる

### 9.2 運転席側
- 既存 `ThumbnailProgressViewer.exe` をベースにしてよい
- まずは control snapshot の表示だけ対応する
- command 発行 UI は次段階でもよいが、command 書き込み用の土台は先に用意する

### 9.3 Coordinator 側
- 最初は本格制御なしでよい
- まずは
  - 起動できる
  - control snapshot を定期出力する
  - command snapshot を読める
 だけ満たす

## 10. タスク分解

### T1 契約型追加
- 追加対象:
  - `ThumbnailCoordinatorControlSnapshot.cs`
  - `ThumbnailCoordinatorCommandSnapshot.cs`
  - `ThumbnailCoordinatorState.cs`
- 置き場所:
  - `src/IndigoMovieManager.Thumbnail.Queue` もしくは新規 `Coordinator` 共通層

完了条件:
- 本体側と運転席側が同じ型定義を参照できる

### T2 Store 追加
- 追加対象:
  - `ThumbnailCoordinatorControlStore.cs`
  - `ThumbnailCoordinatorCommandStore.cs`
- 要件:
  - UTF-8 BOMなし + LF
  - temp 書込 + move
  - stale 読み取り許容

完了条件:
- JSON 契約を安定して読み書きできる

### T3 Coordinator ProcessManager 追加
- 追加対象:
  - `Thumbnail/Worker/ThumbnailCoordinatorProcessManager.cs`
- 役割:
  - exe 存在確認
  - 起動 / 停止 / 再起動
  - health 的な最小ログ

完了条件:
- `MainWindow` が Coordinator を supervisor できる

### T4 MainWindow 接続口追加
- 追加対象:
  - `MainWindow.xaml.cs`
  - 必要なら `Thumbnail/MainWindow.ThumbnailCreation.cs`
- 内容:
  - Coordinator launch config 解決
  - control snapshot 読み取り
  - 要約行の表示口追加

完了条件:
- Coordinator 未実装でも、接続口だけでビルドできる

### T5 外側運転席の control 読み取り追加
- 追加対象:
  - `ThumbnailProgressViewerWindow.xaml.cs`
  - `ThumbnailProgressViewerWindow.xaml`
- 内容:
  - control snapshot 読み取り
  - 現在値 / 実効値の表示エリア

完了条件:
- 外側で control の現在値が見える

### T6 command 発行の最小経路追加
- 追加対象:
  - `MainWindow` もしくは運転席 UI
- 内容:
  - 仮ボタンまたは仮入力から command snapshot を書く

完了条件:
- Coordinator 未完成でも command file を吐ける

## 11. 並列開発の分担

### 11.1 本体チームが先に触る範囲
- T1
- T2
- T3
- T4

### 11.2 運転席チームが先に触る範囲
- T1
- T2
- T5
- T6

### 11.3 依存関係
- T1 と T2 が終われば、本体側と運転席側はほぼ独立して進められる
- T3/T4 は本体側だけで進められる
- T5/T6 は運転席側だけで進められる

## 12. 最小マイルストーン

### M1 接続契約固定
- control/command の型と store が確定

### M2 本体接続完了
- `MainWindow` から Coordinator を起動できる
- control 要約を読める

### M3 運転席接続完了
- 外側運転席が control を読める
- command を書ける

### M4 実制御移行準備完了
- Coordinator 本体未完成でも、両側が同じ契約で待ち合わせできる

## 13. 完了条件
- 本体側と運転席側が、互いの UI 完成待ちをせずにビルド可能
- control/command 契約がドキュメントとコードで一致している
- `MainWindow` 側に Coordinator 用接続口がある
- 外側運転席側に control 表示口と command 発行口がある

## 14. リスク
- 既存 Worker supervisor と Coordinator supervisor が一時的に二重になる
- command を本体と運転席の両方から書ける期間は、競合ルールを決めないと事故る
- 契約を頻繁に変えると、並列開発の意味が消える

## 15. 競合回避ルール
- 契約型の変更は `SchemaVersion` を上げる時だけにする
- 列追加は後方互換な追加だけにする
- 列名変更や意味変更は、接続点固定フェーズでは禁止する
- command の最終書込者が勝つ単純ルールで始める

## 16. 次アクション
1. `control` / `command` 契約型を追加する
2. 対応 store を追加する
3. Coordinator ProcessManager の雛形を追加する
4. `MainWindow` の要約表示口を追加する
5. 外側運転席へ control 表示口を追加する
