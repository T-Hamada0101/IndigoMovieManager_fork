# Implementation Plan_管理者サービス分離_usnmft_HDD温度_2026-03-09

## 1. 目的
- `usnmft` の管理者必須処理を本体UIプロセスから分離し、通常権限の常用運転でも使える形にする。
- サムネイル並列制御が参照する `HDD温度取得` / `UsnMft状態取得` を、同じ管理者サービスへ寄せる。
- 既に入っている `named-pipe + length-prefixed-json` 前提の契約を活かし、最小変更で本線へ乗せる。

## 2. 実コード調査で確定した現状

### 2.1 `usnmft` はまだ同一プロセス内の管理者前提
- `Watcher/UsnMftProvider.cs`
  - `IsAdministrator()` を直接見て、非管理者なら `availability_error:AdminRequired` を返している。
- `Watcher/LiteFileIndexProviderBase.cs`
  - `new Lite.FileIndexService(options)` を直接生成している。
  - つまり `Watcher` 側から見た `usnmft` は、まだローカル直呼び前提。
- `src/IndigoMovieManager.FileIndex.UsnMft/FileIndexService.cs`
  - `AdminUsnMft` / `StandardFileSystem` の切替責務を持つ。
- `src/IndigoMovieManager.FileIndex.UsnMft/AdminUsnMftIndexBackend.cs`
  - `CreateFile("\\\\.\\C:")`、`DeviceIoControl(FSCTL_ENUM_USN_DATA / READ_USN_JOURNAL)` を叩く実装で、管理者権限境界の本体はここ。

### 2.2 管理者サービス用の受け口は先に作られている
- `src/IndigoMovieManager.Thumbnail.Queue/Ipc/AdminTelemetryContracts.cs`
  - `IAdminTelemetryClient`
  - `AdminTelemetryConsumerKind` に `WatcherFacade` がある。
- `src/IndigoMovieManager.Thumbnail.Queue/Ipc/ThumbnailIpcTransportPolicy.cs`
  - `AdminServicePipeName = "IndigoMovieManager.AdminTelemetry.v1"`
  - `ConnectTimeoutMs` / `RequestTimeoutMs` / `HealthCheckTimeoutMs` が固定済み。
- `src/IndigoMovieManager.Thumbnail.Queue/Ipc/AdminTelemetryRuntimeResolver.cs`
  - サービス未接続時の `internal-only` への縮退が既に整理済み。
- つまり「サービスを使う設計」はあるが、「本物の client / server 実装」がまだない。

### 2.3 HDD温度取得は契約だけあり、実装は未配線
- `src/IndigoMovieManager.Thumbnail.Queue/Ipc/ThumbnailIpcDtos.cs`
  - `DiskThermalSnapshotDto`
  - `UsnMftStatusDto`
- `src/IndigoMovieManager.Thumbnail.Queue/ThumbnailQueueProcessor.cs`
  - `adminTelemetryClient`
  - `thermalDiskIdResolver`
  - `usnMftVolumeResolver`
  - これらを受け取れるが、呼び出し側でほぼ渡していない。
- `Thumbnail/MainWindow.ThumbnailCreation.cs`
  - in-process consumer 起動時に `adminTelemetryClient` を渡していない。
- `src/IndigoMovieManager.Thumbnail.WorkerCore/ThumbnailWorkerHostService.cs`
  - 外部Worker側でも `adminTelemetryClient` を渡していない。
- つまり温度取得と `UsnMftStatus` は設計上の差し込み口だけ先にあり、現在は `NoOp` 運転。

### 2.4 設定と配布の文脈
- ルートの `IndigoMovieManager_fork.csproj` は SDK 形式の WPF 本体。
- `src/` 配下の Worker / Coordinator / UsnMft は `net8.0-windows` の x64 プロジェクトで統一されている。
- 本体ビルド後に外部Workerを出力へコピーする仕組みが既にある。
- このため、初手は「SCM登録のWindows Service」よりも「昇格常駐の外部サービスEXE」の方が既存流儀に合う。

## 3. 結論
- 第一段階では **Windows Service(SCM) ではなく、昇格起動する常駐サービスEXE** を採用する。
- 理由は以下。
  - 既存の Worker / Coordinator と同じ配布・更新モデルに乗せられる。
  - 本体ビルド後コピーの仕組みを流用できる。
  - サービス導入のためのインストーラ、登録解除、再起動制御、pipe ACL の複雑度を一段抑えられる。
  - それでも `管理者権限の別プロセス` という目的は満たせる。
- どうしても OS 起動時常駐が必要になった時だけ、第二段階で SCM 化を検討する。

## 4. 採用アーキテクチャ

### 4.1 プロセス構成
- `IndigoMovieManager_fork.exe`
  - UI / Watcher / Worker supervisor
- `IndigoMovieManager.AdminService.exe`（新規）
  - 管理者権限で起動する broker
  - named pipe server を持つ
  - `usnmft` 実処理と温度取得を内包する
- `IndigoMovieManager.Thumbnail.Worker.exe`
  - 通常権限で動き、管理者処理だけ broker に問い合わせる

### 4.2 責務分離
- 本体/Workerに残すもの
  - UI文言
  - fallback判断
  - throttle判断
  - 結果表示
- 管理者サービスへ寄せるもの
  - USN/MFTインデックス構築
  - USNジャーナル差分監視
  - `UsnMftStatusDto` の実値生成
  - HDD/SSD温度取得
  - 将来の管理者専用メトリクス追加

### 4.3 契約方針
- 既存 `IAdminTelemetryClient` は温度 / 状態取得用としてそのまま活かす。
- `usnmft` 本体は別契約へ分離する。
  - 推奨新設: `IAdminFileIndexClient`
  - 理由: telemetry と file index query を同じ interface に押し込むと責務が崩れる。
- pipe は1本でよい。
  - 通信コマンドを `Telemetry` と `FileIndex` で分ける。
  - 実 transport は共通化する。

## 5. 具体的な実装方針

### 5.1 新規プロジェクト
- `src/IndigoMovieManager.AdminService`
  - `net8.0-windows`
  - x64
  - named pipe server
  - `IndigoMovieManager.FileIndex.UsnMft` を参照
- 余力があれば契約分離
  - `src/IndigoMovieManager.AdminContracts`
  - ただし初手は変更波及を抑えるため、既存 DTO を使い回してもよい

### 5.2 `usnmft` サービス化の境界
- `AdminUsnMftIndexBackend` はサービス側へ閉じ込める。
- Watcher側は `FileIndexService` を直接 `new` しない。
- `Watcher/LiteFileIndexProviderBase.cs` は以下の2段構成へ切る。
  - ローカル実装: `StandardFileSystemProvider` 用
  - サービス実装: `UsnMftProvider` 用
- `UsnMftProvider.CheckAvailability()` は「自プロセスが管理者か」ではなく、以下を見る。
  - 管理者サービスへ接続できるか
  - そのサービスが `SupportsWatcherIntegration` と `SupportsUsnMftStatus` を返すか

### 5.3 `usnmft` 用APIの最小単位
- サービス側に持たせる呼び出しは以下に絞る。
  - `CollectMoviePaths`
  - `GetUsnMftStatus`
  - `WarmupIndex` または `EnsureIndex`
- `CollectThumbnailBodies` は管理者不要なのでサービスへ載せない。
- フィルタリングは service 側でやる。
  - 理由: 全件を pipe 経由で返すとデータ量が大き過ぎる。

### 5.4 HDD温度取得
- サービス内に `IDiskThermalProbe` を作る。
- 実装は probe chain にする。
  - 先に SMART / Storage 系
  - 取れなければ `Unavailable`
- 大事なのは「取れないドライブが普通にある」前提を契約で守ること。
- 失敗時は例外を上へ投げず、`DiskThermalSnapshotDto.ThermalState = Unavailable` を返す。

### 5.5 サービス client
- named pipe client を1つ実装し、以下から共通利用する。
  - `MainWindow` の in-process consumer
  - `ThumbnailWorkerHostService`
  - `Watcher/UsnMftProvider`
- 再接続方針は既存 `ThumbnailIpcTransportPolicy` の timeout をそのまま使う。

### 5.6 サービスの起動・監視
- `ThumbnailWorkerProcessManager` と同じノリの `AdminServiceProcessManager` を新設する。
- 起動条件
  - `FileIndexProvider=usnmft`
  - またはサムネイル側で admin telemetry を使う設定が有効
- 停止条件
  - 本体終了時
  - 設定で不要になった時
- 初期段階では「本体が親」のライフサイクルで十分。

## 6. 変更対象

### 6.1 既存ファイルの主変更点
- `Watcher/UsnMftProvider.cs`
  - ローカル管理者判定を削り、サービス接続判定へ置換
- `Watcher/LiteFileIndexProviderBase.cs`
  - `CreateService()` 直呼びを抽象化
- `src/IndigoMovieManager.Thumbnail.Queue/Ipc/AdminTelemetryContracts.cs`
  - 既存契約は維持
  - 必要なら watcher 用 request context 生成を追加
- `src/IndigoMovieManager.Thumbnail.Queue/Ipc/AdminTelemetryRuntimeResolver.cs`
  - `CreateWatcherRequestContext()` を追加
- `Thumbnail/MainWindow.ThumbnailCreation.cs`
  - in-process consumer に `adminTelemetryClient` と resolver を渡す
- `src/IndigoMovieManager.Thumbnail.WorkerCore/ThumbnailWorkerHostService.cs`
  - worker でも同 client を渡す
- `IndigoMovieManager_fork.csproj`
  - `BuildAdminService` 相当のコピー処理を追加

### 6.2 新規追加候補
- `src/IndigoMovieManager.AdminService/*`
- `Thumbnail/Worker/AdminServiceProcessManager.cs`
- `Watcher/AdminFileIndexClient.cs`
- `src/IndigoMovieManager.Thumbnail.Queue/Ipc/NamedPipeAdminTelemetryClient.cs`
- `src/IndigoMovieManager.Thumbnail.Queue/Ipc/NamedPipeMessageFraming.cs`

## 7. 実装順

### Phase 1: 足場を通す
- [ ] 管理者サービスEXEを新設
- [ ] named pipe server/client の疎通
- [ ] `GetCapabilitiesAsync` の実装
- [ ] `GetDiskThermalSnapshotAsync` の実装
- [ ] `GetUsnMftStatusAsync` の実装
- 完了条件
  - Worker / in-process consumer から service source の telemetry が見える

### Phase 2: `usnmft` 本体をサービスへ移す
- [ ] `IAdminFileIndexClient` 相当の契約追加
- [ ] `CollectMoviePaths` を service 実装
- [ ] `Watcher/UsnMftProvider` を service client 経由へ置換
- [ ] 非管理者でも `usnmft` が service 経由で成功することを確認
- 完了条件
  - Watcher の `usnmft` が UI 非昇格でも使える

### Phase 3: 配布と運用を固める
- [ ] 本体ビルド後に service 出力をコピー
- [ ] service 起動失敗時の通知と reason を固定
- [ ] ログカテゴリを整理
- [ ] 将来必要なら SCM 化を検討

## 8. テスト方針
- 単体テスト
  - pipe framing
  - timeout
  - access denied
  - service unavailable
  - `DiskThermalSnapshotDto` / `UsnMftStatusDto` の既定値
- 結合テスト
  - 通常権限UI -> 管理者service -> `usnmft` 成功
  - 通常権限Worker -> 管理者service -> telemetry 取得
  - service停止時に `internal-only` へ縮退
- 回帰観点
  - `everything`
  - `standardfilesystem`
  - 既存の Worker supervisor / Coordinator supervisor

## 9. リスクと対策
- リスク: `usnmft` 検索結果を pipe 越しに大量返送すると詰まる
  - 対策: service 側で root / ext / since を適用してから返す
- リスク: 温度取得APIが機種依存
  - 対策: probe chain + `Unavailable` 固定
- リスク: service が死ぬと watcher と worker の両方へ波及
  - 対策: reconnect + fallback reason 固定 + supervisor 再起動
- リスク: 契約が Thumbnail.Queue 配下にあるため watcher 視点で不自然
  - 対策: 初手は流用、安定後に `AdminContracts` へ抽出

## 10. 先にやるべき最小実装
- 最初のPRは欲張らない。
- まずやるべきはこの順。
  1. 管理者サービスEXEを立てる
  2. `IAdminTelemetryClient` の本実装を入れる
  3. Worker / in-process consumer へ配線する
  4. 次のPRで `Watcher/UsnMftProvider` を service client 化する
- 理由
  - HDD温度取得と `UsnMftStatus` は既存契約が揃っていて、先に通しやすい
  - `usnmft` 本体移行は watcher 側の責務分離を伴うので、1段後ろに置いた方が安全

## 11. この計画の判断
- このリポジトリでは「いきなり SCM の Windows Service」へ行くより、「昇格常駐EXE + named pipe」で先に価値を出す方が正しい。
- 既存コードはそこへ向かう下地まで既にある。
- 今足りないのは設計ではなく、`client / server 実装` と `Watcherへの配線` の2点。
