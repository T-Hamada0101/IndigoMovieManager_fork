# Implementation Plan + tasklist（ファイルインデックス3プロバイダ分離 / UI反映 2026-03-06）

## 0. 背景
- 既存の `FileIndexProvider` 設定は実質 `everything / usnmft` の2択だった。
- ただし実装上の `UsnMftProvider` は `StandardFileSystem` バックエンドを使っており、名前と実体が一致していなかった。
- UI上も `Everything連携` という表現が残り、`usnmft` 選択時の挙動やフォールバック理由が誤解されやすかった。
- 今後の調査・チューニング・A/B比較のため、`Everything / usnmft / StandardFileSystem` を実装・UIともに明確に分離する必要があった。

## 1. 目的
- `everything / usnmft / StandardFileSystem` を明示的な3プロバイダとして扱えるようにする。
- `UsnMftProvider` を名前どおり `AdminUsnMft` 専用へ戻し、`StandardFileSystem` は別プロバイダへ切り出す。
- 設定画面・通知・reason契約・テストが3分割構成と整合するようにする。
- 次のAI/開発者が、現在の実装意図をドキュメントから追える状態にする。

## 2. スコープ
- IN
  - `IFileIndexProvider` 契約の3プロバイダ対応
  - `FileIndexProviderFactory` の3択化
  - `UsnMftProvider` / `StandardFileSystemProvider` の責務分離
  - 設定画面の選択肢更新
  - `MainWindow.Watcher` の通知文言更新
  - テスト追加/改修
  - AI向けドキュメント追加
- OUT
  - `strategy` 文字列互換の全面改名
  - `EverythingFolderSyncService` 側の旧命名全面刷新
  - 管理者昇格UIや権限要求フローの追加

## 3. 方針
- 共通処理は基底クラスへ集約し、派生側はバックエンド差分だけを持つ。
- 既存の `ok:` / `everything_query_error:` などの reason Prefix 契約は維持する。
- `strategy` は既存互換を優先し、`everything / filesystem` の2値を維持する。
- UIは「高速経路の名前」と「通常監視へのフォールバック」を利用者が誤解しない文言に寄せる。

## 4. 実装方針詳細

### 4.1 プロバイダ構成
- `EverythingProvider`
  - Everything IPC専用
- `UsnMftProvider`
  - `AdminUsnMft` 専用
  - 管理者権限必須
- `StandardFileSystemProvider`
  - `StandardFileSystem` 専用

### 4.2 共通化
- ルート単位キャッシュ
- クールダウン付き再構築
- `CollectMoviePaths`
- `CollectThumbnailBodies`
- reason組み立て

上記は `Watcher/LiteFileIndexProviderBase.cs` へ集約する。

### 4.3 UI/Facade
- 設定保存値 `FileIndexProvider` は3択で正規化する。
- Facadeは `ProviderKey` / `ProviderDisplayName` を保持し、通知文言へ渡す。
- `MainWindow.Watcher` の通知は `Everything連携` 固定ではなく `インデックス連携` とする。

## 5. タスクリスト

| ID | 状態 | タスク | 主対象ファイル | 完了条件 |
|---|---|---|---|---|
| FIP-001 | 完了 | 共通基底 `LiteFileIndexProviderBase` を追加 | `Watcher/LiteFileIndexProviderBase.cs` | Standard/UsnMft の共通処理が集約されている |
| FIP-002 | 完了 | `UsnMftProvider` を `AdminUsnMft` 専用へ変更 | `Watcher/UsnMftProvider.cs` | `BackendMode=AdminUsnMft` で動作する |
| FIP-003 | 完了 | `StandardFileSystemProvider` を新設 | `Watcher/StandardFileSystemProvider.cs` | 標準FS経路が独立プロバイダになっている |
| FIP-004 | 完了 | Factory と契約を3プロバイダ対応へ更新 | `Watcher/FileIndexProviderFactory.cs` `Watcher/IFileIndexProvider.cs` `Watcher/FileIndexContracts.cs` | `everything/usnmft/standardfilesystem` を扱える |
| FIP-005 | 完了 | 設定UIを3択へ更新 | `CommonSettingsWindow.xaml` `CommonSettingsWindow.xaml.cs` | UIから3つを選択・保存できる |
| FIP-006 | 完了 | 実行中通知をプロバイダ名ベースへ更新 | `Watcher/MainWindow.Watcher.cs` | 通知文言が実際の選択プロバイダ名になる |
| FIP-007 | 完了 | テストを追加/再編成 | `Tests/IndigoMovieManager_fork.Tests/*` | Standard/UsnMft/Factory/A-B比較が検証できる |
| FIP-008 | 完了 | AI向け運用ドキュメントを追加 | `Watcher/AI向け_ファイルインデックス3プロバイダ分離メモ_2026-03-06.md` | 次作業者向け引き継ぎが読める |

## 6. 受け入れ基準
- 設定画面で `Everything / usnmft / StandardFileSystem` を選べる。
- 起動時に `FileIndexProvider` が3択で正しく解決される。
- `usnmft` は管理者権限不足時に明示reasonを返し、通常監視へフォールバックできる。
- `StandardFileSystem` は旧 `UsnMftProvider` 相当の標準FS走査を維持する。
- 対象ビルドと対象テストが通る。

## 7. リスクと対策
- リスク: `strategy` 文字列とプロバイダ名を混同する
  - 対策: `strategy` は互換維持、表示用は `ProviderDisplayName` を新設して分離する。
- リスク: `usnmft` が通常権限環境で常時失敗に見える
  - 対策: `availability_error:AdminRequired` を返し、通知文言でも権限要件を明示する。
- リスク: 旧 `UsnMftProviderTests` が実態とズレる
  - 対策: UsnMft専用テストへ縮退し、標準FSの主要ケースは `StandardFileSystemProviderTests` へ移す。

## 8. 検証コマンド
- ビルド
  - `"C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" IndigoMovieManager_fork.sln /restore /t:Build /p:Configuration=Debug /p:Platform=x64 /m:1`
- テスト
  - `dotnet test Tests/IndigoMovieManager_fork.Tests/IndigoMovieManager_fork.Tests.csproj -c Debug -p:Platform=x64 --no-build --filter "FullyQualifiedName~UsnMftProviderTests|FullyQualifiedName~StandardFileSystemProviderTests|FullyQualifiedName~FileIndexProviderAbDiffTests|FullyQualifiedName~FileIndexReasonTableTests|FullyQualifiedName~FileIndexProviderFactoryTests"`

## 9. 実行結果メモ（2026-03-06）
- ビルド
  - 成功
- テスト
  - `21 passed / 5 skipped / 0 failed`
- スキップ理由
  - `usnmft` は管理者権限必須のため、通常権限環境では `UsnMftProviderTests` と `Everything vs UsnMft` の一部比較がスキップされる。
- 既知警告
  - `NETSDK1206`（`SQLitePCLRaw.lib.e_sqlite3` の RID 警告）は既存で、今回変更由来ではない。

## 10. 関連ファイル
- `Watcher/LiteFileIndexProviderBase.cs`
- `Watcher/EverythingProvider.cs`
- `Watcher/UsnMftProvider.cs`
- `Watcher/StandardFileSystemProvider.cs`
- `Watcher/FileIndexProviderFactory.cs`
- `Watcher/IndexProviderFacade.cs`
- `Watcher/MainWindow.Watcher.cs`
- `CommonSettingsWindow.xaml`
- `Tests/IndigoMovieManager_fork.Tests/StandardFileSystemProviderTests.cs`
- `Tests/IndigoMovieManager_fork.Tests/UsnMftProviderTests.cs`
- `Watcher/AI向け_ファイルインデックス3プロバイダ分離メモ_2026-03-06.md`

