# 調査メモ: CommonSettingsWindow共有設定画面棚卸し（2026-03-06）

## 1. 目的
- `CommonSettingsWindow` にサムネイル負荷プリセットUIを追加する前に、既存の共有設定画面との衝突点を整理する。
- 特に `Watcher` 側で完了した `FileIndexProvider` 3択UIとの整合を先に固定する。

## 2. 調査対象
- `CommonSettingsWindow.xaml`
- `CommonSettingsWindow.xaml.cs`
- `Watcher/AI向け_ファイルインデックス3プロバイダ分離メモ_2026-03-06.md`

## 3. 現状の結論
- `CommonSettingsWindow` は既に `FileIndexProvider` 3択UIを持つ共有設定画面である。
- サムネイル関連UIは既に3ブロック存在する。
  - `ThumbnailParallelism`
  - `ThumbnailPriorityLaneMaxMb`
  - `ThumbnailSlowLaneMinGb`
  - 閾値プリセット3ボタン
- 追加予定の「サムネイル負荷プリセット」は、既存の「閾値プリセット」と意味が近いため、見出し分離をしないと誤解される。

## 4. 既存UIの重要ポイント

### 4.1 画面は共有設定画面
- `FileIndexProviderSelector` が既に配置されている。
- `EverythingIntegrationMode` も同一画面にある。
- そのため、サムネイル設定を追加する時は `Watcher` 側設定と同じ画面で共存する前提になる。

### 4.2 画面サイズに余裕が少ない
- `Width=800`
- `Height=720`
- `ResizeMode=NoResize`
- `ScrollViewer` が無い
- 既存項目だけで縦方向の密度が高い。
- プリセット説明文や追加コンボボックスを入れるなら、以下のどちらかが必要。
  - 高さ拡張
  - `ScrollViewer` 化

### 4.3 保存タイミングが混在している
- 一般設定や `FileIndexProvider` は `OnClosing` 保存。
- サムネイル並列数とレーン閾値はスライダー変更時に即時反映。
- 新しい `ThumbnailThreadPreset` を追加するなら、以下を先に決める必要がある。
  - `OnClosing` 保存へ寄せるか
  - 既存サムネイル設定と同様に即時反映するか

### 4.4 サムネイル設定には既存プリセットがある
- 既存の「軽量動画重視 / バランス / 巨大動画重視」は、レーン閾値プリセットである。
- 今回追加する `slow / normal / ballence / fast / max / custum` は、負荷プリセットであり別物。
- ラベルを曖昧にすると、利用者も開発者も混同する。

### 4.5 再起動要否が設定ごとに異なる
- `FileIndexProvider` は「再起動後に反映」。
- サムネイル並列数は即時反映。
- 将来の `ThumbnailThreadPreset` も即時反映に寄せるなら、`FileIndexProvider` と同じ見た目に置くと誤解されやすい。

## 5. コード上の重要ポイント

### 5.1 コンストラクタ初期化
- `FileIndexProviderSelector.SelectedValue` は `FileIndexProviderFactory.NormalizeProviderKey` で初期化される。
- サムネイル側は `SyncThumbnailParallelismSliderFromSettings()` と `SyncThumbnailLaneThresholdSlidersFromSettings()` で初期同期している。
- `ThumbnailThreadPreset` 追加時も、同様の初期同期メソッドが必要。

### 5.2 `OnClosing` 保存
- `FileIndexProvider`
- `EverythingIntegrationMode`
- `ThumbnailParallelism`
- `ThumbnailPriorityLaneMaxMb`
- `ThumbnailSlowLaneMinGb`
- が同一メソッドで保存される。
- 設定項目追加時は、この保存順と責務を崩さない方が安全。

### 5.3 `PropertyChanged` 追従はサムネイル側のみ
- `SettingsDefault_PropertyChanged` は現状 `ThumbnailParallelism` とレーン閾値しか監視していない。
- `FileIndexProvider` は即時追従していない。
- `ThumbnailThreadPreset` を即時反映する場合は、監視対象へ追加するか、別同期経路が必要。

## 6. 衝突点

### 6.1 用語衝突
- 「閾値プリセット」と「負荷プリセット」が混ざる。
- 対策:
  - 見出しを分ける。
  - `閾値プリセット` はそのまま残し、新規は `負荷プリセット` と明記する。

### 6.2 配置衝突
- 現行レイアウトは縦積みで余白が少ない。
- 対策:
  - サムネイル設定ブロックをグループ化する。
  - 必要なら `ScrollViewer` を導入する。

### 6.3 保存方式衝突
- `FileIndexProvider` は再起動反映。
- サムネイル設定は実行中反映。
- 対策:
  - 新規プリセットは「即時反映」で統一する。
  - ただし保存自体は `Settings.Default` へ即時反映し、最終永続化は `OnClosing` に乗せてもよい。

### 6.4 管理者権限の意味衝突
- `usnmft` の `AdminRequired` はファイルインデックスの可用性問題。
- サムネイル側の高負荷縮退は性能制御。
- 対策:
  - UIメッセージとログを別軸で扱う。

## 7. 実装前に守ること
- `FileIndexProviderSelector` の既存初期化と保存を壊さない。
- `EverythingIntegrationMode` の既存互換を壊さない。
- 新規 `ThumbnailThreadPreset` は、既存の `ThumbnailParallelism` と役割分離する。
- `CommonSettingsWindow.xaml` は今後も共有設定画面である前提で、サムネイル専用画面のように寄せすぎない。

## 8. 推奨する次の実装順
1. `ThumbnailThreadPreset` を設定へ追加する。
2. `CommonSettingsWindow` に「サムネイル負荷プリセット」UIを追加する。
3. `ThumbnailParallelism` と `ThumbnailThreadPreset` の関係を `custum` 前提で整理する。
4. その後に低負荷モードと予約制御へ進む。

## 9. この調査の完了条件
- 共有設定画面での主な衝突点が列挙されている。
- `LTH-001A` / `THR-001A` の前提判断として使える。
- 次の `LTH-001` へ安全に進める。
