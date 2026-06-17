# AGENTS.md
- 最初に読む正本: `AI向け_現在の全体プラン_workthree_2026-03-20.md`
- 最初に読む正本: `Docs\forAI\Implementation Plan_UIを含む高速化のための抜本改善プラン_2026-04-17.md`
- 上位判断基準: `Docs\forAI\Goal_Indigoの未来図_2026-05-28.md`。ただし日々の着手順は上記2正本を優先する

- 人類とAIの信頼関係を築く
- アプリケーションの作成とは新たな宇宙を想像する神の仕事

## 基本ルール
- 日本語で回答、ドキュメントも日本語で
- コードには日本語で流れ重視のコメントを記載する
- MVVMを基本とするが開発者がコードを掴みやすいようにする事の方が重要
- 文字コードと改行は「UTF-8 (BOMなし) + LF」を使用する
- チャットコメントのリンクは必ず生パスで記載する（Markdownリンク形式は使わない）

## Mermaid表示ルール
- Mermaidプレビューは `Markdown Preview Mermaid Support`（`bierner.markdown-mermaid`）を標準とする
- Mermaid確認は VS Code の Markdown Preview で行う
- Mermaid系の他拡張（別レンダラ）は同時有効化しない（競合回避）
- Mermaid記法は互換性優先で記述する（`subgraph` 内 `direction` や記号多用ラベルは避ける）

## 開発方針（VS2026）
- このプロジェクトは Visual Studio 2026 前提で開発する
- ビルド/テストのプラットフォームは `x64` に統一する（`Any CPU` は使わない）

## ビルド失敗時ルール
- ビルド失敗時は原因を先に特定してから再試行する
- ロックされている場合はユーザーが実行中の可能性を考慮し確認する
- フォーマット起因が疑わしい場合は `CSharpier` を実行して整形する

## 実行環境ルール
- PowerShellはUTF-16変換を防ぐため7.x.xを使用する
- ネット検索はコンテキスト書き換えや汚染を避けるため、必ずサンドボックス内でのみ行う
- AIエージェントは必要に応じて `C:\python\work` の Python 作業環境を利用してよい
- Python作業環境を使う場合は、`C:\python\Open-Initialize-PythonWork.cmd` で初期化し、通常作業は `C:\python\Open-PythonWork.cmd` を利用する
- Python関連コマンドを直接実行する場合も、原則として `C:\python\work\.venv` の仮想環境を前提にする
- 検証用の detached worktree、退避コピー、commit hash 名フォルダ、`HEAD` フォルダはリポジトリ直下に作らない
- 検証用 worktree は `C:\Users\na6ce\source\repos\IndigoMovieManager-worktree-*` のように sibling ディレクトリへ作る
- 検証用 worktree や退避コピーは使用後に必ず削除し、リポジトリ直下へ残置しない
- やむを得ず一時フォルダを作った場合も、作業完了時に `git status --short` で残置物が本体ビルドへ混ざらないことを確認する

## スキル
- 必要に応じて `.agent\skills` を参照・作成する
- GitHub Release の preview 実行、tag 公開、release 失敗からの復旧を行う時は、`.agent\skills\indigomoviemanager-github-release\SKILL.md` を必ず参照し、その手順に従う

## 実行環境ルール
- PowerShellはUTF-16変換を防ぐため7.x.xを使用する

## プロジェクトルール
- WhiteBrowserの互換プログラムとして開発する
- WhiteBrowserのDB(*.wb)は変更しない

## 主要パス
- 実行時ログの出力先は `%LOCALAPPDATA%\IndigoMovieManager\logs\`
- `launchSettings.json` の場所は `Properties\launchSettings.json`

## ブランチ運用方針（AI必読）
- このブランチで作業するAIは、着手前に `AI向け_ブランチ方針_workthreeユーザー体感テンポ最優先_2026-03-11.md` を必ず読む
- 作業の大粒度優先順位と現在の全体計画は `AI向け_現在の全体プラン_workthree_2026-03-20.md` を正本として確認する
- このブランチは**開発本線**として、ユーザーが感じるテンポ感を最優先に、UI を含む高速化と安定化を進める

## 正本プランの見方（AI必読）
- 全体の優先順位と着手順の正本は `AI向け_現在の全体プラン_workthree_2026-03-20.md`
- ブランチの判断基準と禁止線の正本は `AI向け_ブランチ方針_workthreeユーザー体感テンポ最優先_2026-03-11.md`
- UI 分離とスムーズ表示の上位ゴールは `Docs\forAI\Goal_UI分離とスムーズ表示アーキテクチャ_2026-05-27.md`
- UI の詰まり解消と抜本高速化の正本は `Docs\forAI\Implementation Plan_UIを含む高速化のための抜本改善プラン_2026-04-17.md`
- `skin` 個別の高速化と保存分離の正本は `WhiteBrowserSkin\Docs\Implementation Plan_skin切り替え高速化_DB保存分離先行_2026-04-13.md`

## UI高速化プランの最新見直し（2026-05-26 AI必読）
- 計画の軸は維持する。進捗は実装ベース約 80〜82%、実機確認込み 72〜74% として扱い、完了判定は実機ログで閉じる
- 次の最優先は `Refresh()` / `Items.Refresh()` / `FilterAndSort(..., true)` 残りの局所反映化、大件数 sort の background + revision guard
- 起動 / skin / visible-first は、`debug-runtime.log` で `first-page shown` / `input ready` / `refresh end` / `catalog_*` / `persist_*` を説明できるまで完了扱いにしない
- skin は DB 分離だけで完了扱いにせず、`refresh` / stale / catalog / navigate の削減と trace 観測を優先する

## UI高速化プランの最新見直し（2026-06-18 AI必読）
- 済: `RefreshMovieViewFromCurrentSourceAsync(...)` は背景計算だけでなく Dispatcher apply 待ちにも後着キャンセル token を渡し、古い in-memory refresh が UI 反映待ち中に残った時は `stage=apply-dispatch` でキャンセルしてログへ閉じる。
- 済: サムネ成功 / rescued sync の選択中反映は、汎用 `Refresh()` ではなく `RefreshSelectedThumbnailDetail()` へ寄せ、タグ編集再表示を巻き込まずに選択中詳細のサムネ表示だけを揺すり直す。
- 済: サムネ成功後段の main tab local refresh 予約は、非 UI スレッドから `DispatcherPriority.Background` で UI へ戻し、shutdown 中は予約を積まない。
- 済: user-priority 解除ログは `begin_reason` / `end_reason` / `elapsed_ms` / `release_reason` / `deferred_watch` を持つ。timeout は runtime release log へ接続済みで、既定 30 秒超過時だけ `release_reason=timeout` として出し、強制解除はしない。
- 済: `CreateWatcher()` / `BuildWatcherCreationPlan(...)` は `availability_ms` / `watch_table_load_ms` / `folder_plan_ms` / `registration_ms` / `apply_ms` に加え、`attempted` / `failed` / `first_registered_ms` をログへ出し、起動後 watcher 作成の支配要因を実機ログで切り分けられる。
- 済: manual reload deferred scan は `Dispatcher` / `MainVM` / DB path / queue 初期化状態を入口と遅延後に guard し、skip reason と例外 type / origin をログへ残す。
- 済: Header Reload は `reload_id` を発行し、`header reload begin/end/failed` と `manual reload deferred scan scheduled/skipped/failed` を同じIDで結ぶ。再読込本体と後続 scan の因果を実機ログだけで追える。
- 済: Header Reload の `external_skin_refresh_queued` は外部 skin refresh 要求の実受理可否を表す。`QueueExternalSkinHostRefresh(...)` / `ExternalSkinHostRefreshScheduler.Queue(...)` は bool 契約を持ち、teardown / scheduler 未初期化 / dispatcher shutdown / `BeginInvoke` 受理失敗では false を返す。
- 済: Header Reload の遅延 manual scan は latest-only 化し、古い `reload_id` は `reason=superseded` で skip する。短時間連打でも最新の `Header.ReloadButton:deferred` だけを watch 側へ進める。
- 済: Header Reload / minimal chrome reload / fallback retry の host clear は `host clear begin/end/failed` で `reason` / `has_host` / `elapsed_ms` を出し、blank 遷移時間を後段 `refresh end` の navigate 系計測と分けて確認できる。
- 済: watch full fallback は schedule / apply / final 系ログに `recovery_reason` を併記し、`dirty-fields-unsafe:*` など次に削る条件を実機ログだけで選べる。
- 済: 検索 full reload の DB 読込入口にも後着キャンセル token を通し、db-reload 段階のキャンセルは未観測例外にせず `filter canceled: ... stage=db-reload` でログへ閉じる。
- 済: active skin の通常 `dbinfo-*` refresh は同一 document / host 入力 / dbKey なら再 `NavigateToString` を skip できる。skip 時は `onSkinLeave` を送らず、実際に navigate する時だけ leave callback を送ること。実 navigate へ進む時は旧 reuse key を先に無効化し、same-document skip では外部サムネ許可リストを消さない。
- PM判断: `header-reload` / `fallback-notice-retry` は明示 `CatalogRefresh` なので、実機ログで支配要因と表示互換を確認するまで same-document skip 対象へ広げない。速度目的で再読込の鮮度確認意味を変えない。
- 現行実機ログでは `first-page shown` / `input ready` は良好で、次の確認軸は起動後 `CreateWatcher` 約13秒の内訳、active skin の WebView navigate 800〜980ms帯、過去1件の manual reload deferred scan NullReference。
- 次は新ログ入りの実機 `debug-runtime.log` で、watcher 作成の遅延が Everything availability / watch table / folder plan / registration / apply / 初回登録待ち / 登録失敗混入のどれかを確定してから削る。skin は `host clear end elapsed_ms` と `refresh end host_navigate_ms` / `navigate_to_string_ms`、`navigate_skipped` / `navigate_skip_reason`、実WebView2表示崩れの有無を確認する。

## 2チーム体制（AI必読）
- 本線チームは `AI向け_現在の全体プラン_workthree_2026-03-20.md` と `Docs\forAI\Implementation Plan_UIを含む高速化のための抜本改善プラン_2026-04-17.md` を正本として進める
- スキンチームは `WhiteBrowserSkin\Docs\Implementation Plan_skin切り替え高速化_DB保存分離先行_2026-04-13.md` を正本として進める
- 本線チームは一覧、Watcher、Queue、起動、画像供給、UIスレッドの簡素化を主担当とする
- スキンチームは `refresh`、stale、catalog、skin API、保存分離、trace 観測を主担当とする
- どちらのチームも、着手前に全体正本を確認し、局所最適が全体優先順位と衝突しないことを確認する
- 共有ルールとして、DB 分離は主因ではなく土台施策と位置づけ、`refresh` / stale / catalog / diff-first UI を先に評価する

## ドキュメント運用ルール
- 今回作成したAI向け理解書・フロー資料・フォルダ構成書は、関連コード変更時に必ず随時更新する
- 実装とドキュメントの差分を放置せず、同一PR/同一コミット系列で整合を取る
- AI向け資料を更新した場合は、対応する資料（01〜04/フロー資料/構成書）を更新日付きで明示し、変更概要を残す

## コミット時ルール
- コミット前に `git diff --check` と `git status --short` を確認し、意図しない差分を混ぜない
- コミット前にローカル固有情報を確認する
  - 絶対パス
  - ユーザー名
  - メールアドレス
  - ローカル環境名
  - `.local` 配下参照
- 実動画依存のテスト・playground・script は、既定値にローカル絶対パスを直書きしない
- ドキュメントやサンプルでリポジトリ配下の例を書く時は、`%USERPROFILE%\source\repos\...` ベースで統一する
- ユーザー依存の例やローカル作業パスは、`%USERPROFILE%` / `%USERNAME%` ベースで統一し、`C:\Users\<ユーザー名>\...` を直書きしない
- コミットメッセージは日本語で、1コミット1目的を基本とする
- 大きな作業でも、意味の異なる変更はコミットを分ける
- コミット前に必要なドキュメント更新を同じコミット系列へ含める
- `Author` / `Committer` は GitHub ハンドル名 + `noreply` を基本とし、既定では `T-Hamada0101 <T-Hamada0101@users.noreply.github.com>` を使用する
- PowerShell からコミット・amend する時は、環境変数の一時設定より `git -c user.name="T-Hamada0101" -c user.email="T-Hamada0101@users.noreply.github.com" commit --author="T-Hamada0101 <T-Hamada0101@users.noreply.github.com>" ...` を優先し、`Author` / `Committer` の取り違えを防ぐ
- 既存コミットを修正する場合は、内容変更が無くても `Author` / `Committer` の確認を行う
- push 前に、公開して問題ない情報だけが履歴と差分に含まれていることを再確認する
# 🔥 各AIエージェントへの指令（必読！） 🔥
あなた（AIエージェント）は、自分の名前に対応する以下のドキュメントを**必ず**読み、そこに書かれたペルソナ（人格・口調）に従ってユーザーと対話、コード記述、ドキュメント作成を行ってください！

- **Gemini のあなたはここを読め！** 👉 [.GEMINI.md](.GEMINI.md)
- **Claude / Opus のあなたはここを読め！** 👉 [.CLAUDE.md](.CLAUDE.md)
- **Codex のあなたはここを読め！** 👉 [.CODEX.md](.CODEX.md)
- **このブランチで作業する全AIはここも読め！** 👉 [AI向け_ブランチ方針_workthreeユーザー体感テンポ最優先_2026-03-11.md](AI向け_ブランチ方針_workthreeユーザー体感テンポ最優先_2026-03-11.md)


## `.local` の運用ルール
- 保管場所: `C:\Users\osakacenter\source\repos\MyLab\.local`
- 用途: 環境固有情報、機密情報（APIキー、トークン、接続文字列、資格情報など）の保管専用。

## 取り扱い方針
- `.local` 配下の実データは Git にコミットしない。
- `.local` 配下の内容をドキュメント、Issue、チャットへそのまま貼り付けない。
- 共有が必要な場合は、値を除いたテンプレート（例: `.example`）のみ共有する。
- ログ出力やエラー出力に機密値を含めない。

## 補足
- `.local` はローカル環境専用ディレクトリとして扱う。
- 既存ツールやスクリプトで参照する場合も、機密値の平文露出を避ける。
