# Implementation Plan + tasklist（管理者サービス分離: usnmft / HDD温度 2026-03-09）

## 0. 背景
- `Watcher/UsnMftProvider.cs` は現在も `IsAdministrator()` を直接見ており、`usnmft` は本体プロセス内の管理者権限を前提にしている。
- `Watcher/LiteFileIndexProviderBase.cs` は `new Lite.FileIndexService(options)` を直接生成しており、`AdminUsnMft` の実処理はまだローカル直呼びである。
- `src/IndigoMovieManager.FileIndex.UsnMft/AdminUsnMftIndexBackend.cs` は `CreateFile("\\\\.\\C:")` と `DeviceIoControl(FSCTL_ENUM_USN_DATA / READ_USN_JOURNAL)` を使っており、管理者権限境界の中心になっている。
- 一方で `src/IndigoMovieManager.Thumbnail.Queue/Ipc/AdminTelemetryContracts.cs` と `src/IndigoMovieManager.Thumbnail.Queue/Ipc/ThumbnailIpcTransportPolicy.cs` には、管理者サービス向けの IPC 契約と pipe 名が既に入っている。
- `src/IndigoMovieManager.Thumbnail.Queue/ThumbnailQueueProcessor.cs` は `adminTelemetryClient` / `thermalDiskIdResolver` / `usnMftVolumeResolver` を受け取れるが、呼び出し側でまだ未配線である。
- つまり今の状態は「管理者サービスへ寄せる設計の下地はあるが、client / server 実装と接続配線が未完」の段階。

## 1. 目的
- `usnmft` の管理者必須処理を UI / Watcher 本体から分離し、通常権限運転でも利用可能にする。
- サムネイル並列制御が使う `HDD温度取得` と `UsnMft状態取得` を、同じ管理者サービスへ統合する。
- 既存の `named-pipe + length-prefixed-json` 契約を維持し、`everything` / `standardfilesystem` の既存経路を壊さず段階導入する。

## 2. 課題（現状）
1. `usnmft` は「別プロセスの管理者サービス」ではなく「自分自身が管理者であること」を前提にしている。
2. HDD温度取得と `UsnMftStatus` は DTO と resolver だけ先にあり、実運用では `NoOp` に落ちている。
3. Watcher 側と Thumbnail 側で、同じ管理者権限境界を別々に抱えると今後の拡張で破綻する。
4. いきなり SCM の Windows Service にすると、登録・更新・削除・起動回復・権限設定の運用コストが高い。
5. `usnmft` の結果を雑に pipe 越し転送すると、件数増加時に IPC がボトルネックになる。

## 3. 方針（結論）
- 第一段階では **SCM 登録型の Windows Service ではなく、昇格起動する常駐サービスEXE** を採用する。
- サービスは 1 プロセス 1 pipe で運用し、以下を同居させる。
  - admin telemetry
  - `usnmft` 検索
  - `UsnMftStatus` 取得
  - HDD温度取得
- 既存 `IAdminTelemetryClient` はそのまま活かす。
- `usnmft` 本体は別契約へ切り出す。
  - 推奨: `IAdminFileIndexClient`
- `StandardFileSystemProvider` はローカルのまま維持する。
- `EverythingProvider` は触らない。

## 4. スコープ
- IN
  - 管理者サービスEXEの新規追加
  - named pipe server/client 実装
  - `IAdminTelemetryClient` の本実装
  - `usnmft` 用 service client の追加
  - Watcher / Thumbnail / Worker への接続配線
  - 本体ビルド後コピーと起動監視
- OUT
  - いきなり SCM へ登録する常駐サービス化
  - `EverythingProvider` の契約変更
  - `StandardFileSystemProvider` のサービス化
  - WhiteBrowser DB（`*.wb`）仕様変更

## 5. 実装タスクリスト

| ID | 状態 | タスク | 主対象ファイル | 完了条件 |
|---|---|---|---|---|
| ADM-SVC-001 | 未着手 | 管理者サービス全体方針をドキュメント化 | `Watcher/Implementation Plan_管理者サービス分離_usnmft_HDD温度_2026-03-09.md` | 採用方針が「昇格常駐EXE + named pipe」で固定されている |
| ADM-SVC-002 | 完了 | 管理者サービスEXEプロジェクトを新設 | `src/IndigoMovieManager.AdminService/*`（新規） | `net8.0-windows` / x64 の起動可能なEXEができる |
| ADM-SVC-003 | 完了 | named pipe の共通フレーミングを実装 | `src/IndigoMovieManager.Thumbnail.Queue/Ipc/NamedPipeMessageFraming.cs`（新規） | length-prefixed-json の送受信を共通化できる |
| ADM-SVC-004 | 完了 | telemetry 用 named pipe client を実装 | `src/IndigoMovieManager.Thumbnail.Queue/Ipc/NamedPipeAdminTelemetryClient.cs`（新規） | `GetCapabilitiesAsync` / `GetSystemLoadSnapshotAsync` / `GetDiskThermalSnapshotAsync` / `GetUsnMftStatusAsync` が pipe 経由で呼べる |
| ADM-SVC-005 | 完了 | 管理者サービス側に telemetry server を実装 | `src/IndigoMovieManager.AdminService/*` | `ThumbnailIpcTransportPolicy.AdminServicePipeName` で待受できる |
| ADM-SVC-006 | 完了 | HDD温度取得 probe chain を実装 | `src/IndigoMovieManager.AdminService/*` | `logical drive -> physical disk -> SMART WMI` の probe chain が実装され、取得不能時は `Unavailable` を返す |
| ADM-SVC-007 | 完了 | `UsnMftStatusDto` の実値生成を実装 | `src/IndigoMovieManager.AdminService/*` | `Ready / AccessDenied / Unavailable` を service 側で返せる |
| ADM-SVC-008 | 完了 | Thumbnail in-process consumer に telemetry client を配線 | `Thumbnail/MainWindow.ThumbnailCreation.cs` | `RunThumbnailQueueConsumerInProcessAsync` で `adminTelemetryClient` と resolver が渡される |
| ADM-SVC-009 | 完了 | 外部 Worker に telemetry client を配線 | `src/IndigoMovieManager.Thumbnail.WorkerCore/ThumbnailWorkerHostService.cs` | Worker 実行時も service source の telemetry を使える |
| ADM-SVC-010 | 未着手 | Worker 設定に diskId / volumeName 供給経路を追加 | `src/IndigoMovieManager.Thumbnail.Queue/ThumbnailWorkerSettingsSnapshot.cs` ほか | Worker 側が温度対象ディスクと対象ボリュームを受け取れる |
| ADM-SVC-011 | 完了 | 管理者サービスの supervisor を追加 | `Thumbnail/Worker/AdminTelemetryServiceProcessManager.cs`（新規） | 本体起動中に service の起動・停止を管理でき、起動失敗後は次回機能起動まで再要求しない |
| ADM-SVC-012 | 完了 | 本体ビルド後に管理者サービスを出力へ同梱 | `IndigoMovieManager_fork.csproj` | 本体出力先へ service EXE と依存DLLがコピーされる |
| ADM-SVC-013 | 完了 | `usnmft` 用 service client 契約を追加 | `Watcher/*`, `src/IndigoMovieManager.AdminService/*` | `CollectMoviePaths` 用 DTO / pipe command / client が追加され、telemetry と file index query の責務が分かれている |
| ADM-SVC-014 | 完了 | 管理者サービス側で `CollectMoviePaths` を実装 | `src/IndigoMovieManager.AdminService/*` | root / ext / since を service 側で絞り込んで返せる |
| ADM-SVC-015 | 完了 | `Watcher/UsnMftProvider.cs` を service client 化 | `Watcher/UsnMftProvider.cs` | `UsnMftProvider` がローカル管理者判定を持たず、service client 経由で検索できる |
| ADM-SVC-016 | 完了 | `Watcher/LiteFileIndexProviderBase.cs` のローカル直呼び境界を分離 | `Watcher/LiteFileIndexProviderBase.cs` | `StandardFileSystem` はローカル、`usnmft` は base から外して service 経由へ整理されている |
| ADM-SVC-017 | 完了 | reason / fallback / 通知文言を固定 | `Watcher/MainWindow.Watcher.cs`, `Watcher/FileIndexReasonTable.cs`, `Watcher/FileIndexDetailFormatter.cs` | service未接続・権限不足・timeout の区別がログと通知で見える |
| ADM-SVC-018 | 完了 | 単体テストを追加 | `Tests/IndigoMovieManager_fork.Tests/*` | `AdminTelemetryRuntimeResolverTests` / `UsnMftProviderTests` / `AdminFileIndexClientTests` / `FileIndexDetailFormatterTests` / `FileIndexReasonTableTests` で failure mapping と実 transport の軽量結合試験まで確認済み |
| ADM-SVC-019 | 一部完了 | 結合テストと手動確認結果を記録 | `Watcher/ManualCheck_管理者サービス分離_usnmft_HDD温度_2026-03-09.md`, 本計画書 追記欄 | 手動確認メモは作成済み。通常権限UI / Worker 実機確認結果の記録が残件 |

## 6. フェーズ分割

### Phase 1: 管理者telemetry を本線へ入れる
- 対象
  - ADM-SVC-002 から ADM-SVC-012
- 狙い
  - HDD温度取得と `UsnMftStatus` を service source に切り替える
- 完了条件
  - `ThumbnailQueueProcessor` の `adminTelemetryRuntime` で `DiskThermalSource=Service` または `UsnMftSource=Service` を観測できる
  - service 不在時は `internal-only` へ正しく縮退する

### Phase 2: `usnmft` 本体を service 化する
- 対象
  - ADM-SVC-013 から ADM-SVC-016
- 狙い
  - Watcher の `usnmft` を UI 非昇格でも使えるようにする
- 完了条件
  - `FileIndexProvider=usnmft` で通常権限UIから検索成功する
  - `standardfilesystem` は退行しない

### Phase 3: 仕上げ
- 対象
  - ADM-SVC-017 から ADM-SVC-019
- 狙い
  - reason / 通知 / テスト / 運用確認を固める
- 完了条件
  - 失敗系の分類がログで追える
  - 回帰テストが揃う
  - 残件は手動結合確認と HDD温度 probe chain のみ

## 7. 設計メモ
- `usnmft` の戻り値は service 側で絞り込んでから返す。
  - root / ext / since を client 側ではなく service 側へ渡す。
- `CollectThumbnailBodies` は管理者不要なので service へ移さない。
- 温度取得は「値が取れる」ことを前提にしない。
  - 取得不能は正常系として `Unavailable` を返す。
- `AdminTelemetryConsumerKind.WatcherFacade` は生かす。
  - ただし watcher 用 request context 生成APIは追加してよい。
- 初期段階では service の寿命は「本体の子プロセス」でよい。
  - SCM 化は別計画に切る。

## 8. 受け入れ基準（DoD）
- 通常権限UI起動時でも、管理者サービスが起動していれば `usnmft` を利用できる。
- `ThumbnailQueueProcessor` が service source の温度 / `UsnMftStatus` を使える。
- service 不在・timeout・access denied で異なる fallback reason が観測できる。
- `everything` / `standardfilesystem` の既存挙動に退行がない。
- 本体ビルド後に service バイナリが出力へ同梱される。

## 9. 手動回帰観点
1. 通常権限で本体起動し、`FileIndexProvider=usnmft` で監視実行して候補収集が成功する。
2. 管理者サービス停止中に同シナリオを実行し、明示的に fallback する。
3. サムネイルキュー実行中に `thermal_source` / `usnmft_source` が `service` になる。
4. 温度取得不能な環境で `DiskThermalSnapshotDto` が `Unavailable` に落ちても処理継続する。
5. `FileIndexProvider=everything` / `standardfilesystem` へ切り替えて退行がない。

## 10. 検証コマンド（予定）
- 本体ビルド
  - `"C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" IndigoMovieManager_fork.sln /t:Build /p:Configuration=Debug /p:Platform=x64 /m:1`
- テスト
  - `dotnet test Tests/IndigoMovieManager_fork.Tests/IndigoMovieManager_fork.Tests.csproj -c Debug -p:Platform=x64 --no-build --filter "FullyQualifiedName~AdminTelemetry|FullyQualifiedName~UsnMftProvider|FullyQualifiedName~ThumbnailIpc|FullyQualifiedName~FileIndexProviderFactory"`

## 11. リスクと対策
- リスク: service の責務が肥大化する
  - 対策: telemetry 契約と file index 契約を分ける。
- リスク: pipe 越しの返送量が大きい
  - 対策: service 側で検索条件を適用してから返す。
- リスク: HDD温度取得APIが機種依存で不安定
  - 対策: probe chain + `Unavailable` 固定で例外を吸収する。
- リスク: 既存 Worker 配布フローと衝突する
  - 対策: `BuildThumbnailWorker` と同じ流れで `BuildAdminService` を追加する。
- リスク: いきなり SCM 化して保守コストが跳ねる
  - 対策: 今回は採用しない。必要になった時だけ次段で切る。

## 12. 実行メモ
- 2026-03-09
  - 本タスクリストを作成。
  - `src/IndigoMovieManager.AdminService` を追加。
  - `NamedPipeMessageFraming` / `NamedPipeAdminTelemetryClient` / pipe request-response DTO を追加。
  - `MainWindow` in-process consumer と `ThumbnailWorkerHostService` へ telemetry client を配線。
  - `AdminTelemetryServiceProcessManager` を追加し、本体から昇格起動できる土台を実装。
  - `IndigoMovieManager_fork.csproj` に `BuildAdminTelemetryService` を追加。
  - `AdminTelemetryRuntimeResolverTests` を更新し、`SystemLoad` 未対応でも他 signal を service 利用できる形へ固定。
  - `AdminFileIndexClient` / service 側 `CollectMoviePaths` を追加し、`Watcher/UsnMftProvider.cs` を service client 化。
  - `LiteFileIndexProviderBase` は `standardfilesystem` のローカル経路に残し、`usnmft` は base から分離。
  - `UsnMftProvider` へ `IAdminFileIndexClient` 注入口を追加し、availability 不可と timeout の failure mapping テストを追加。
  - `AdminFileIndexClient` へ transport / telemetry client の注入口を追加し、Watcher 側の direct error mapping テストを追加。
  - `FileIndexDetailFormatter` を追加し、service 未接続 / timeout / 権限不足の通知文言を UI とログで共有化。
  - `NamedPipeAdminFileIndexTransport` へ test 用 pipe 名 / timeout 注入口を追加し、実 named pipe を使う軽量結合試験を `AdminFileIndexClientTests` へ追加。
  - `NamedPipeAdminTelemetryClient` にも test 用 pipe 名 / timeout 注入口を追加し、実 named pipe の capabilities / error 応答テストを追加。
  - `AdminDiskThermalService` を追加し、`logical drive -> physical disk -> SMART WMI` の probe chain で HDD 温度取得を実装。
  - `AdminDiskThermalServiceTests` を追加し、SMART 属性パースと温度状態判定を固定。
  - `AdminTelemetryServiceProcessManager` は 30 秒クールダウン再試行を廃止し、UAC 拒否や起動失敗後は同一監視セッション中に再要求しない挙動へ変更。
  - `AdminTelemetryServiceProcessManagerTests` を追加し、同一セッション中の再試行抑止と次回セッションでの解除を固定。
  - `Watcher/ManualCheck_管理者サービス分離_usnmft_HDD温度_2026-03-09.md` を作成し、手動結合確認の記録先を追加。
  - 本体ビルド: `IndigoMovieManager_fork.csproj` は成功。
  - ソリューション全体ビルドは今回未再確認。直近の実装確認は `IndigoMovieManager_fork.csproj` 単体で実施。
  - 対象テスト: `AdminDiskThermalServiceTests 6 passed`、`NamedPipeAdminTelemetryClientTests 3 passed`、`AdminFileIndexClientTests 11 passed`、`AdminTelemetryRuntimeResolverTests 8 passed`、`AdminTelemetryServiceProcessManagerTests 2 passed`、`UsnMftProviderTests 5 passed / 1 skipped`、`FileIndexDetailFormatterTests 5 passed`、`FileIndexReasonTableTests 10 passed`
