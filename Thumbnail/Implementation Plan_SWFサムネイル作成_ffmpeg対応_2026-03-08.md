# Implementation Plan: SWFサムネイル作成 ffmpeg対応（2026-03-08）

## 1. 目的
- `*.swf` を現行の「即プレースホルダー」扱いから外し、`ffmpeg.exe` で代表フレームを取得してサムネイル化できるようにする。
- 既存の通常動画向け経路は崩さず、SWFだけを専用分岐で扱う。
- FFmpegで取得できないSWFは、無限再試行に入れず、最終的に既存のFlashプレースホルダーへ安全に落とす。

## 2. 現状整理
- 監視対象拡張子にはすでに `*.swf` が含まれている。
  - `App.config`
  - `Properties/Settings.settings`
- しかし `ThumbnailCreationService` は `.swf` を `IsUnsupportedPrecheck` として判定し、エンジン実行前に `Flash` プレースホルダーを出して成功扱いにしている。
- そのため現状は `autogen` `ffmpeg1pass` `ffmediatoolkit` `opencv` のどれも SWF には到達しない。
- 既存テストも「SWFはエンジン実行せずプレースホルダー成功」を前提にしている。

## 3. 採用方針

### 3.1 基本方針
- SWFは `ffmpeg.exe` 専用で扱う。
- `FFmpeg.AutoGen` や `FFMediaToolkit` には載せない。
- まずは「代表1枚を安定して作る」ことを優先する。
- 既存のWhiteBrowser互換JPEG末尾メタ情報は維持する。

### 3.2 なぜこの方針か
- SWFは通常動画よりも「プログラムに近い入力」で、共有DLL側より `ffmpeg.exe` の方が切り分けしやすい。
- 既存実装には `ffmpeg.exe` のプロセス起動、優先度、タイムアウト管理がすでにある。
- 失敗時のログをそのまま回収しやすく、将来の実ファイル検証にも向く。

### 3.3 今回の非対象
- ActionScript 3.0 の複雑な動的描画を完全に再現すること
- 外部XMLや外部画像を読み込むSWFの完全対応
- SWFを通常動画と同じ「多コマ時系列サムネイル」として厳密再現すること

### 3.4 並行開発中の進め方
- 現在は別作業が走っているため、`ThumbnailCreationService.cs` や既存本線ファイルへの大規模編集は避ける。
- SWF対応ロジックは、まず別ファイルで独立実装する。
- 既存本線への変更は、呼び出し口の追加や切替フックの最小変更に留める。
- 本格統合は、別作業の収束後に行う。

## 4. 別ファイル先行方針

### 4.1 先に作るファイル
- `Thumbnail/Swf/SwfThumbnailCandidate.cs`
  - 候補時刻、採用時刻、失敗理由などの軽量DTO
- `Thumbnail/Swf/SwfThumbnailCaptureOptions.cs`
  - サイズ、候補秒数、白画面判定閾値、タイムアウトを保持
- `Thumbnail/Swf/SwfThumbnailFrameAnalyzer.cs`
  - 白画面疑い、単色寄り判定を担当
- `Thumbnail/Swf/SwfThumbnailFfmpegCommandBuilder.cs`
  - SWF専用の `ffmpeg.exe` 引数構築を担当
- `Thumbnail/Swf/SwfThumbnailGenerationService.cs`
  - 候補時刻の順次試行、JPEG確認、採用フレーム決定を担当
- `Thumbnail/Swf/SwfThumbnailIntegrationPlan_2026-03-08.md`
  - 最後にどこへつなぐかの統合手順を固定する補助計画書

### 4.2 すぐには広く触らないファイル
- `Thumbnail/ThumbnailCreationService.cs`
- `Thumbnail/Engines/FfmpegOnePassThumbnailGenerationEngine.cs`
- `Thumbnail/Engines/ThumbnailEngineRouter.cs`
- `Thumbnail/MainWindow.ThumbnailCreation.cs`

方針:
- これらは「SWF専用サービスを呼ぶ入口を足す」段階までに留める。
- SWF固有ロジック本体は持ち込まない。

### 4.3 最終統合時に接続する場所
- `ThumbnailCreationService.CreateThumbAsync`
  - SWF判定後に `SwfThumbnailGenerationService` を呼ぶ
- 必要なら `FfmpegOnePassThumbnailGenerationEngine`
  - 既存のプロセス起動共通処理だけ再利用する

### 4.4 この進め方の利点
- 別作業の競合を減らせる
- SWF対応の責務境界が先に明確になる
- 後で本線へ統合する際に、差分が「呼び出し追加中心」になりやすい
## 5. 実装方針

### 5.1 SWF判定の見直し
- `.swf` 判定自体は維持する。
- ただし `TryDetectSwfKnownSignature` の役割を「即プレースホルダー化」から「SWF専用経路へ送るための軽量判定」に変更する。
- `CachedMovieMeta.IsUnsupportedPrecheck` にSWFを混ぜ続けると他の非対応入力と区別できないため、以下のどちらかで整理する。
  - `CachedMovieMeta` に `IsSwfCandidate` と `SwfDetail` を追加する
  - もしくは `UnsupportedKind` のような種別付きへ広げる

推奨:
- 最小変更で済む `IsSwfCandidate` 追加を優先する。

### 5.2 生成経路
- `ThumbnailCreationService.CreateThumbAsync` に SWF 専用分岐を追加する。
- 分岐位置は「DRM前処理の後、既存 unsupported precheck の前」を第一候補とする。
- SWF専用分岐では、まず別ファイルで作った `SwfThumbnailGenerationService` を呼ぶ。
- その内部で `ffmpeg.exe` 実行を行う。

理由:
- 既存ルーターは通常動画向けの選択器であり、SWFは常に `ffmpeg.exe` 固定でよい。
- ルーターへ混ぜると `CanHandle` とフォールバック順の意味が曖昧になる。

### 5.3 SWF用のffmpegコマンド
- `SwfThumbnailFfmpegCommandBuilder` で SWF専用の「単一フレーム抽出」引数を組み立てる。
- ベースコマンドは次を採用する。

```text
ffmpeg -y -hide_banner -loglevel error -an -sn -dn -i input.swf -ss 00:00:02 -frames:v 1 -vf scale=WxH:flags=bilinear output.jpg
```

ルール:
- `-ss` は `-i` の後ろに置く
- `-hwaccel` は付けない
- `-frames:v 1` を使う
- 出力はJPEG固定
- サイズは既存タブサイズに合わせる

### 5.4 代表フレームの取り方
- 初期値は `2秒` を第1候補にする。
- その後、候補時刻を順に試す。

候補時刻の初期案:
1. `2.0s`
2. `5.0s`
3. `0.0s`

理由:
- SWFは先頭が真っ白やロード画面になりやすい。
- ただし短尺や静的SWFもあるため、最後に `0秒` も拾う。

将来拡張しやすいように、候補時刻は環境変数化してよい。

候補:
- `IMM_THUMB_SWF_CAPTURE_SEC_LIST=2,5,0`

### 5.5 真っ白対策
- FFmpeg終了コードだけでは「白一色成功」を失敗扱いできない。
- そのため、出力JPEGを一度読み、単色寄りかどうかを簡易判定する。

最小実装:
- 平均輝度が高すぎる
- 輝度分散が小さすぎる
- 画素の大半が白寄り

この条件を満たした場合:
- 「ロード画面疑い」として次の候補時刻を試す

補足:
- 既存の `IsMostlyBlackPanel` と同じ思想で、SWF用に `IsMostlyFlatBrightFrame` のような判定を追加する。

### 5.6 出力形式
- FFmpegでは代表1枚だけを取得する。
- その後、既存のタブ分割数に合わせて同じ画像を複製し、既存JPEG保存形式へ合わせる。
- `ThumbInfo` の秒数配列は、採用された代表時刻を全パネルへ同値で入れる。

理由:
- UI側と既存JPEG末尾メタ情報の互換を崩さないため
- まずは最小実装で着地し、後で多コマ化する余地を残すため

## 6. 失敗時方針

### 6.1 再試行方針
- SWFは「ffmpegで作れるかどうか」が本質で、通常動画のような一時的デコード失敗再試行とは性質が違う。
- そのため、SWF専用経路で全候補時刻を試しても取れない場合は、QueueDBでの再試行増殖は避ける。

採用方針:
- SWF専用経路の最終失敗時は、既存の `Flash` プレースホルダーへ落として成功扱いにする。

### 6.2 ログ
- 以下を `ThumbnailRuntimeLog` に残す。
  - SWF判定ヒット
  - 採用した候補時刻
  - 白画面疑いでの再試行
  - FFmpeg標準エラーの要約
  - 最終的にプレースホルダーへ落ちた理由

## 7. 変更対象ファイル
### 7.1 先行作成ファイル
1. `Thumbnail/Swf/SwfThumbnailCandidate.cs`
2. `Thumbnail/Swf/SwfThumbnailCaptureOptions.cs`
3. `Thumbnail/Swf/SwfThumbnailFrameAnalyzer.cs`
4. `Thumbnail/Swf/SwfThumbnailFfmpegCommandBuilder.cs`
5. `Thumbnail/Swf/SwfThumbnailGenerationService.cs`
6. `Thumbnail/Swf/SwfThumbnailIntegrationPlan_2026-03-08.md`

### 7.2 最小変更で接続する既存ファイル
1. `Thumbnail/ThumbnailCreationService.cs`
2. `Thumbnail/Engines/FfmpegOnePassThumbnailGenerationEngine.cs`
3. `Tests/IndigoMovieManager_fork.Tests/AutogenExecutionFlowTests.cs`
4. `Tests/IndigoMovieManager_fork.Tests/FfmpegOnePassThumbnailGenerationEngineTests.cs`

必要なら追加:
- `Docs/FFmpeg_Guidelines.md`

## 8. 実装タスク

### Phase 0: 別ファイル先行作成
- [ ] `Thumbnail/Swf` 配下を新設する
- [ ] DTO、Options、Analyzer、CommandBuilder、Service を別ファイルで作る
- [ ] 統合補助ドキュメントを `Thumbnail/Swf` 配下へ置く

### Phase 1: 先行ロジック実装
- [ ] SWF判定用DTOとOptionsを固める
- [ ] `-i` 後 `-ss` の引数生成を `CommandBuilder` に実装する
- [ ] 候補時刻の順次試行を `Service` に実装する
- [ ] 白画面疑い時の再試行判定を `Analyzer` に実装する

### Phase 2: 既存保存形式へ接続
- [ ] 取得した1枚を既存タイルサイズへ複製する
- [ ] `ThumbInfo` に採用秒数を全パネル同値で保存する
- [ ] プレビュー画像も代表フレームから生成する
- [ ] この段階でも、既存本線への変更は最小に留める

### Phase 3: 失敗処理の整理
- [ ] SWF最終失敗時だけ `Flash` プレースホルダーへ落とす
- [ ] Queue再試行へ回さないログ文言を整理する
- [ ] 既存の unsupported precheck ログと混ざらないよう分類名を見直す

### Phase 4: 最終統合
- [ ] `ThumbnailCreationService` へ SWF専用サービス呼び出し口を追加する
- [ ] 必要最小限の既存分岐切替だけを行う
- [ ] 旧SWF即プレースホルダー経路を撤去する

### Phase 5: テスト
- [ ] `SWFは即プレースホルダー` 前提のテストを置き換える
- [ ] SWF用コマンド組み立ての単体テストを追加する
- [ ] 候補時刻の試行順の単体テストを追加する
- [ ] 白画面疑い判定の単体テストを追加する
- [ ] 最終失敗でプレースホルダー成功になる経路を検証する

## 9. テスト戦略

### 9.1 単体テスト
- `AutogenExecutionFlowTests`
  - 変更前: SWFはエンジン未実行で成功
  - 変更後: SWFは `SwfThumbnailGenerationService` を通る
- `FfmpegOnePassThumbnailGenerationEngineTests`
  - `-i` 後 `-ss` になっていること
  - 候補時刻が `2 -> 5 -> 0` の順で並ぶこと
  - 白画面疑い判定が期待どおり動くこと

追加候補:
- `SwfThumbnailFrameAnalyzerTests`
- `SwfThumbnailFfmpegCommandBuilderTests`
- `SwfThumbnailGenerationServiceTests`

### 9.2 手動確認
1. 静的SWFでサムネイルが作られる
2. 先頭が白いSWFで `2秒` 側の画像が採用される
3. 壊れたSWFでプレースホルダーへ落ちる
4. 通常のMP4処理に影響がない
5. 手動更新でも同じSWFサムネイルが再生成できる

## 10. 受け入れ条件
- [ ] `.swf` が即プレースホルダーではなく、一度は `ffmpeg.exe` で抽出を試みる
- [ ] 採用時刻と失敗理由がログで追える
- [ ] 代表1枚からでも既存タブ表示が崩れない
- [ ] 取得不能なSWFで無限再試行にならない
- [ ] MP4/WMV/ASFの既存経路を壊さない
- [ ] 統合前の段階でも、主要ロジックが `Thumbnail/Swf` 配下の別ファイルで完結している

## 11. リスクと対策
- リスク: FFmpegビルドによってはSWF入力自体が弱い
  - 対策: 標準エラー全文をログへ残し、プレースホルダーへ落とす
- リスク: ActionScript主体のSWFで黒画面や白画面になる
  - 対策: 候補時刻の複数試行 + 単色判定 + 最終プレースホルダー
- リスク: 多コマ時系列サムネイルでなくなる
  - 対策: 今回は互換優先で同一代表フレーム複製とし、将来の多コマ化を別タスクに分離する
- リスク: 並行開発中に本線競合が増える
  - 対策: SWF固有ロジックは `Thumbnail/Swf` 配下へ隔離し、本線変更は入口だけにする

## 11.1 実ファイル確認メモ（2026-03-08）
- `D:\BentchItem_HDD` の実SWF 8本で `ffmpeg` 抽出を確認した。
- 成功確認:
  - `dora.swf`: `2秒` で代表フレーム取得成功
  - `nightmare.swf`: `2秒` と `5秒` は空出力、`0秒` で取得成功
- 現行 `ffmpeg` 経路で代表フレームを取得できなかったもの:
  - `hattenardin.swf`
  - `loituma.swf`
  - `maiahi.swf`
  - `maiyahi.swf`
  - `popopopon.swf`
  - `vipteacher.swf`

注意:
- 上記6本は「音声SWFである」と断定しない。
- 正しい表現は「現行の `ffmpeg/ffprobe` 経路では動画フレームを検出できず、代表フレーム取得に失敗した」である。
- 失敗要因としては、`ffmpeg` が解釈できないSWFタグ構成、ActionScriptによる動的描画、外部アセット依存などを含みうる。
- したがって、現行実装での最終挙動は「プレースホルダー縮退」で妥当だが、入力自体の性質を断定しない。

## 12. 実装順の推奨
1. `Thumbnail/Swf` 配下の別ファイル群を先に作る
2. SWF専用の引数生成、候補時刻試行、白画面判定をそこで完結させる
3. 代表1枚を既存保存形式へ載せる
4. 最後に `ThumbnailCreationService` へ最小の接続口を入れる
5. テストを書き換える
6. 実SWFで手動確認する

## 13. メモ
- `App.config` と `Settings.settings` の `*.swf` はすでに入っているため、今回の主変更は拡張子設定ではなくサムネイル生成経路である。
- 最初から多コマSWF対応を狙うと仕様が膨らむ。まずは `ffmpeg.exe` による代表1枚取得で着地させるべきである。
- 現在は「別ファイルで作って後で統合する」が優先であり、既存本線に直接SWF固有ロジックを埋め込まないこと。
