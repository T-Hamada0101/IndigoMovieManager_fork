# Implementation Plan + tasklist（起動時もたつき低減 / 段階起動と先行サムネ着手 2026-03-06）

## 0. 背景
- 起動時にHDDスピンアップ待ちやDB読込がUIスレッド側で発生し、初回表示直後に固まりやすい。
- フォルダ走査時に `existingThumbBodies` と `existingMovieByPath` を毎回全量構築し、その後の大量照合にも時間がかかる。
- 現状は「`MovieInfo` 生成 -> MainDB登録 -> サムネキュー投入」の順で進むため、MainDB登録が重いとサムネイル作成開始も遅れる。

## 1. 目的
- 起動直後のUI応答を優先し、HDD待ちや初回走査をバックグラウンドへ逃がす。
- 大量リスト照合の待ち時間を減らし、起動後の「新顔検知」までを短縮する。
- MainDB登録完了前でも、サムネイル作成を先行できる経路を作る。

## 2. 現状のボトルネック

### 2.1 UI停止系
- `MainWindow_ContentRendered` で `OpenDatafile(...)` を同期実行している。
  - 対象: `MainWindow.xaml.cs`
- `MainWindow` コンストラクタでも `LastDoc` 向けに `Path.Exists` / `TryValidateMainDatabaseSchema` / `GetSystemTable` が走る。
  - 対象: `MainWindow.xaml.cs`
- HDD休止明けだと、これらのファイルアクセス待ちがUIスレッドに直撃する。

### 2.2 大量照合系
- `CheckFolderAsync` 開始時に毎回以下を全量再構築している。
  - `BuildExistingThumbnailBodySet(snapshotThumbFolder)`
  - `BuildExistingMovieSnapshotByPath(snapshotDbFullPath)`
- その後、新規候補ごとに DB存在確認 / サムネ存在確認 / `MovieInfo` 生成へ進む。
  - 対象: `Watcher/MainWindow.Watcher.cs`

### 2.3 サムネ開始遅延系
- 新規動画は `pendingNewMovies` に積まれ、`FlushPendingNewMoviesAsync()` 内で
  - `InsertMoviesToMainDbBatchAsync`
  - UI反映
  - その後に `TryEnqueueThumbnailJob`
  の順で進む。
- つまり MainDB書き込みが詰まる間、サムネキューへ入れられない。
  - 対象: `Watcher/MainWindow.Watcher.cs`
- `InsertMovieTableBatch` 側では `Sinku.dll` メタ取得も同じトランザクション前段で走る。
  - 対象: `DB/SQLite.cs`

## 3. 方針
- 起動処理を「最初にUIを返す処理」と「後段でよい処理」に分割する。
- 照合用データは毎回全量再構築せず、DB単位のスナップショット/差分更新へ寄せる。
- サムネキュー投入キーを `MovieId` 依存から切り離し、`MoviePathKey + TabIndex + MainDbPathHash` ベースで先行投入可能にする。

## 4. スコープ
- IN
  - 起動時 `OpenDatafile` の非同期段階化
  - フォルダ照合キャッシュの再利用
  - MainDB登録とサムネキュー投入の順序分離
  - 起動時の計測ログ追加
- OUT
  - DBスキーマ大改修
  - サムネ生成エンジン自体のアルゴリズム変更
  - Everything/FileIndexProvider契約の全面改名

## 5. 実装方針詳細

### 5.1 Phase A: 起動段階の分離
- `MainWindow_ContentRendered` では同期 `OpenDatafile(...)` を直接叩かず、`OpenDatafileAsync` 相当のラッパーへ切り替える。
- 起動時の処理を3段に分ける。
  1. UI最小表示
  2. DB基本設定読込（skin / sort / watch設定）
  3. 重い全量走査・差分検出
- `LastDoc` の存在確認・スキーマ確認もUIスレッド直実行を減らし、必要最小限だけ先に行う。

### 5.2 Phase B: 照合キャッシュの再利用
- `existingMovieByPath` を DB切替単位で保持するキャッシュ層を追加する。
  - 初回だけ全量構築
  - 新規登録後は差分で辞書へ反映
- `existingThumbBodies` も起動直後の全量読込と、その後の差分反映へ寄せる。
  - 新規サムネ成功時に `Add`
  - 削除検知/救済で必要時のみ再同期
- `CheckFolderAsync` 開始時の毎回全量再構築をやめ、無効化条件付き再利用へ変更する。

### 5.3 Phase C: MainDB登録前の先行サムネ投入
- 新規動画検知時に、MainDB登録前でもサムネキューへ仮投入できる経路を追加する。
- キューの永続キーはすでに `MainDbPathHash + MoviePathKey + TabIndex` なので、`MovieId` 非依存運用に寄せやすい。
- 必要対応
  - `QueueObj` / `QueueRequest` で `MovieId` 未確定を許容
  - サムネ生成に必要な情報は `MovieFullPath`, `Hash`, `TabIndex` から解決
  - MainDB登録後にUI表示や補完情報だけ追従反映

### 5.4 Phase D: DB登録の軽量化
- `InsertMovieTableBatch` の前段 `Sinku.dll` 読込を完全同期で待たない構成を検討する。
- 優先案
  - MainDBには最小必須列だけ先に登録
  - `container/video/audio/extra` は後段補完ジョブで更新
- 代替案
  - まず `MovieCore` 最小情報で登録し、重いメタ取得は遅延実行

### 5.5 Phase E: 計測と可視化
- 起動経路に計測点を追加する。
  - `MainWindow_ContentRendered`
  - `OpenDatafile`
  - `BootNewDb`
  - `CheckFolderAsync`
  - `FlushPendingNewMoviesAsync`
- 追加ログ例
  - `startup phase`
  - `startup hdd wait suspected`
  - `matching cache hit/miss`
  - `thumb enqueue before db insert`

## 6. タスクリスト

| ID | 状態 | タスク | 主対象ファイル | 完了条件 |
|---|---|---|---|---|
| START-001 | 未着手 | 起動経路の同期処理棚卸しと計測点追加 | `MainWindow.xaml.cs` | 起動フェーズ別の elapsed_ms がログ出力される |
| START-002 | 未着手 | `OpenDatafile` 非同期段階化の設計と導入 | `MainWindow.xaml.cs` | UI初期表示後も入力が止まりにくい |
| START-003 | 未着手 | `existingMovieByPath` のDB単位キャッシュ追加 | `Watcher/MainWindow.Watcher.cs` | 毎回全量読込しない |
| START-004 | 未着手 | `existingThumbBodies` の差分更新運用へ変更 | `Watcher/MainWindow.Watcher.cs` | 起動直後以外は再構築回数が大きく減る |
| START-005 | 未着手 | MainDB登録前でもサムネキュー投入できる経路を追加 | `Watcher/MainWindow.Watcher.cs` `Thumbnail/MainWindow.ThumbnailQueue.cs` | MainDB登録待ちでもサムネ生成が始まる |
| START-006 | 未着手 | `QueueObj` / `QueueRequest` の `MovieId` 非依存確認 | `Thumbnail/QueueObj.cs` `Thumbnail/QueuePipeline/*` | `MovieId` 未確定でも安全に処理できる |
| START-007 | 未着手 | MainDB最小登録 + 後段補完の分離案を実装 | `DB/SQLite.cs` `Models/*` | 重いメタ取得が起動直列経路から外れる |
| START-008 | 未着手 | 起動回帰確認手順を追加 | 回帰メモ | HDD休止明けでも体感改善を確認できる |

## 7. 受け入れ基準
- 起動直後にウィンドウが表示された後、UI操作が長時間固まらない。
- HDD休止明けでも `OpenDatafile` 全体がUIスレッドを長く占有しない。
- `CheckFolderAsync` ごとの全量再構築回数が減り、起動後の新顔検知開始が短縮する。
- MainDB登録が遅くても、サムネ生成ジョブが先に動き始める。
- 既存の重複防止とキュー永続化が壊れない。

## 8. リスクと対策
- リスク: DB未登録の動画に対してサムネだけ先にでき、整合が崩れる
  - 対策: キューキーは `MoviePathKey` ベースを維持し、UI公開はDB登録完了後に切り替える。
- リスク: 照合キャッシュが古くなり、存在判定がズレる
  - 対策: DB切替、手動再読込、サムネ削除救済時に明示invalidateする。
- リスク: 起動非同期化で例外が見えにくくなる
  - 対策: startup phaseごとのログと通知を追加し、失敗箇所を段階名で残す。

## 9. 検証観点
1. HDD休止明けに `LastDoc` 自動オープンで起動し、タイトルバー表示後に入力が止まらない。
2. 大量監視フォルダで、`CheckFolderAsync` 開始から最初のサムネジョブ投入までの時間が短縮する。
3. MainDB書き込みが遅いケースでも、サムネイル進捗タブにジョブが先行して現れる。
4. DB切替時にキャッシュ混線が起きない。
5. 再起動後もQueueDB復元が成立する。

## 10. 関連ファイル
- `MainWindow.xaml.cs`
- `Watcher/MainWindow.Watcher.cs`
- `Thumbnail/MainWindow.ThumbnailQueue.cs`
- `Thumbnail/ThumbnailCreationService.cs`
- `DB/SQLite.cs`
- `Thumbnail/plan_AsyncQueueDbArchitecture_サムネイルキュー専用DB_非同期処理アーキテクチャ最終設計.md`
- `Thumbnail/調査結果_監視フォルダ追加スキャンDB登録ボトルネック_2026-02-24.md`

