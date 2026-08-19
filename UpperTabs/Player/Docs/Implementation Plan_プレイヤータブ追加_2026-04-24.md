# Implementation Plan: プレイヤータブ追加 (2026-04-24)

最終更新日: 2026-08-19

変更概要:
- 2026-08-19: Player 専用の WebView2 / MediaElement 二重経路を廃止し、`LibVLCSharp.WPF` の `VideoView` と Windows 版 LibVLC へ一本化した
- 既存の再生・一時停止、シーク、音量、ミュート、全画面、手動サムネイル、ブックマーク、終了を同じ LibVLC 再生実体へ接続した
- WebView2 は外部 skin 表示に必要なため、アプリ全体の依存からは削除しない
- WhiteBrowser DB とサムネイル保存先は変更しない

## 1. 目的

- MP4、MKV、HEVC Main10 などを Player タブ内で安定して再生する
- コンテナやコーデックごとの再生経路分岐をなくし、操作と状態管理を一系統にする
- 既存の軽い右レールと手動サムネイル導線を維持する

## 2. 現行構成

- 再生 UI: `LibVLCSharp.WPF.VideoView`
- 再生エンジン: `LibVLCSharp` 3.10.1
- Windows native runtime: `VideoLAN.LibVLC.Windows` 3.0.23.1
- 再生実体は Player タブで一度だけ遅延生成し、動画切替でも同じ `MediaPlayer` を再利用する
- 起動時 warm は行わず、最初に動画を開く時だけ LibVLC を初期化する
- 全画面は同じ `VideoView` を専用 Window へ載せ替え、動画の再読込や第2プレイヤー生成を行わない
- 右レールは `PlayerThumbnailList` 一系統で、Grid 用サムネイル資産を利用する

## 3. 操作と状態

- 動画選択後、LibVLC の `Playing` 通知で保留中の開始位置、再生/一時停止、ミュート、フォーカス要求を適用する
- `Playing`、`Paused`、`Stopped`、`EndReached`、`EncounteredError`、`LengthChanged` は Dispatcher へ戻して UI に反映する
- 再生終了後の再生操作は `Play()` で再開し、一時停止中は `SetPause(false)` で再開する
- 進捗表示は 250ms 間隔かつ `DispatcherPriority.Background` とし、入力を優先する
- 音量は中央状態を正本としてユーザー設定へ保存し、LibVLC の 0〜100 音量へ変換する
- Player タブを離れる時は一時停止し、アプリ終了時はイベント解除後に `MediaPlayer` と `LibVLC` を破棄する
- エラー時は保留要求と user-priority 区間を閉じ、`debug-runtime.log` へ `libvlc playback failed` を残す

## 4. 維持する既存契約

- Player タブのサムネイル生成対象、優先タブ、保存先解決は Grid 扱いへ正規化する
- 手動サムネイルは現在の LibVLC 再生位置を使い、既存サムネイル生成キューへ渡す
- ブックマーク追加も現在の LibVLC 再生位置を使う
- Player タブ選択時の自動再生は `ContextIdle` または 250ms 上限待ちの後へ送る
- 連続選択では古い user-priority 要求を解除し、現在選択だけを開く
- WhiteBrowser の `*.wb` は変更しない

## 5. WebView2 の扱い

- Player 専用の `uxWebVideoPlayer`、HTML5 video bridge、ナビゲーション fallback、全画面 snapshot は削除する
- Player XAML に `WebView2` と WPF `MediaElement` を置かない
- `Microsoft.Web.WebView2` パッケージは WhiteBrowser 互換 skin ホストが利用するため維持する

## 6. 検証

- 2026-08-19 実施結果: Release/x64 は警告 0・エラー 0、Player 重点テストは 53 件成功
- 出力先に `libvlc.dll`、`libvlccore.dll`、plugins を含む native ファイル 425 件を確認
- 通常 MP4、HEVC Main10 MP4、MKV の映像・時間更新を実機確認し、全画面表示、Esc 復帰、一時停止、再開も確認
- 初回 native 初期化は実機ログで 381ms。背景初期化中も UI の応答ありを確認
- Release/x64 ビルドが通ること
- Player 関連 source-policy テストが通ること
- native 配布物として `libvlc.dll`、`libvlccore.dll`、plugins が出力されること
- HEVC Main10 MP4、MKV、通常 MP4 で映像・音声・時間更新を確認すること
- 再生、一時停止、シーク、音量、ミュート、全画面復帰、手動サムネイル、ブックマークを確認すること
- Player コードに旧 WebView2 / MediaElement 経路が残っていないこと
- skin 用 WebView2 が退行していないこと

## 7. 配布時の注意

- LibVLCSharp と LibVLC は `LGPL-2.1-or-later`。GitHub Release 作成時は native runtime と plugins を欠落させず、第三者ライセンス表示と対応ソース情報を同梱する
- native runtime を単一 exe へ埋め込んで再リンク不能にしない。現行 NuGet 配布どおり別 DLL と plugins として配置する

## 8. 関連ファイル

- `IndigoMovieManager.csproj`
- `Views/Main/MainWindow.xaml`
- `Views/Main/MainWindow.Player.cs`
- `UpperTabs/Player/MainWindow.UpperTabs.LibVlcPlayer.cs`
- `UpperTabs/Player/MainWindow.UpperTabs.PlayerTab.cs`
- `UpperTabs/Player/MainWindow.UpperTabs.PlayerFullscreenWindow.cs`
- `BottomTabs/Bookmark/MainWindow.BottomTab.Bookmark.cs`
- `Tests/IndigoMovieManager.Tests/ManualPlayerResizeHookPolicyTests.cs`
