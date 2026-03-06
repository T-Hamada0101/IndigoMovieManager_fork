# 手動回帰チェック手順（サムネイルスレッド制御とレーン設計 2026-03-06）

## 1. 目的
- `slow` が「内部1本固定」ではなく、待機延長とバッチクールダウン付きの低負荷モードとして動くことを確認する。
- `fast` が `slow` より高い実効並列で通常レーン優先に動くことを確認する。
- 巨大動画と再試行動画が、需要がある時だけ `ゆっくり` / `Recovery専` レーンへ流れることを確認する。
- `HighLoadScore` に基づく縮退と復帰が、進捗表示と `debug-runtime.log` で追えることを確認する。

## 2. 事前準備
1. Debug/x64 でビルドする。  
   `C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe IndigoMovieManager_fork.csproj /t:Build /p:Configuration=Debug /p:Platform=x64 /m`
2. 関連テストを実行する。  
   `dotnet test Tests/IndigoMovieManager_fork.Tests/IndigoMovieManager_fork.Tests.csproj -c Debug --filter "FullyQualifiedName~ThumbnailThreadPresetResolverTests|FullyQualifiedName~ThumbnailParallelControllerTests"`
3. 通常動画を6本以上、巨大動画を1本、再試行へ入れられる動画を1本用意する。  
   再試行用は「初回だけ失敗させられる動画」か、既に `AttemptCount > 0` へ入れられる動画を使う。
4. 巨大動画が `ゆっくり` レーンへ入るよう、対象動画サイズが `ThumbnailSlowLaneMinGb` を超えることを確認する。足りない場合は一時的に閾値を下げる。
5. `logs\debug-runtime.log` を開ける状態にして、各シナリオ開始時刻を控える。

## 3. 手動チェック手順（8ステップ）
1. 設定画面でプリセットを `slow` に変更し、アプリを起動したまま通常動画3本と巨大動画1本を投入する。
2. `サムネイル進捗` タブを開き、巨大動画がある間だけ `ゆっくり` パネルが使われることを確認する。通常動画だけの処理中は `Recovery専` が出っぱなしにならないことも確認する。
3. `logs\debug-runtime.log` の最新行を確認し、`consumer cooldown: wait_ms=750` が出ること、`consumer lease:` に `slow_reserved=True`、`thumb queue summary:` に `parallel_configured=2` と `slow_lane=1` または `slow_demand=1` が出ることを確認する。
4. 次に、再試行へ入れたい動画を1回失敗させて再投入し、`AttemptCount > 0` の状態を作る。`サムネイル進捗` タブで `Recovery専` パネルが使われることを確認する。
5. 同じ区間の `debug-runtime.log` を確認し、`consumer lease:` に `recovery_reserved=True`、`thumb queue summary:` に `recovery_lane=1` または `recovery_demand=1` が出ることを確認する。
6. プリセットを `fast` に切り替えて通常動画を6本以上投入し直す。`slow` シナリオ開始以降のログと比較して、`thumb queue summary:` の `parallel_configured` が増えることを確認する。`slow` 時のような `consumer cooldown: wait_ms=750` が新規に出続けないことも確認する。
7. `fast` のまま通常動画、巨大動画、再試行動画を同時に残した状態で数バッチ流し、`debug-runtime.log` に `parallel scale-down:` が `category=high-load` または `category=error+high-load` で出ることを確認する。再現しにくい場合は通常動画本数を増やして滞留を作る。
8. その後、失敗が止まり滞留が減った状態まで待ち、`parallel scale-up:` が `category=high-load` で出ること、進捗表示が処理継続中のまま戻ることを確認する。

## 4. 合格条件
- `slow` で `consumer cooldown: wait_ms=750` が出て、`parallel_configured=2` のまま低負荷運転になる。
- 巨大動画がある時だけ `ゆっくり` レーンが有効になり、`slow_reserved=True` または `slow_demand=1` をログで確認できる。
- 再試行動画がある時だけ `Recovery専` レーンが有効になり、`recovery_reserved=True` または `recovery_demand=1` をログで確認できる。
- `fast` で `slow` より高い `parallel_configured` が確認できる。
- 高負荷時に `parallel scale-down:`、回復後に `parallel scale-up:` が出て、`category=high-load` 系で追跡できる。
- 進捗タブが処理中に固まらず、`ゆっくり` / `Recovery専` / 通常 `Thread n` の役割が崩れない。

## 5. 実施ログ（記入欄）
- [ ] 手順1: `slow` へ切替
- [ ] 手順2: `ゆっくり` レーン確認
- [ ] 手順3: `slow` ログ確認
- [ ] 手順4: 再試行投入
- [ ] 手順5: `Recovery専` ログ確認
- [ ] 手順6: `fast` 切替と並列増加確認
- [ ] 手順7: `parallel scale-down` 確認
- [ ] 手順8: `parallel scale-up` 確認

### メモ
- 実施日時:
- 実施者:
- 対象MainDB:
- 結果:
- 補足:
