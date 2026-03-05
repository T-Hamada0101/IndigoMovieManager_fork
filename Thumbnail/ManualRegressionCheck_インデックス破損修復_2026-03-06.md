# 手動回帰チェック手順（インデックス破損修復 2026-03-06）

## 1. 目的
- リカバリーレーン（`AttemptCount > 0`）時だけ、インデックス判定/修復が動くことを確認する。
- 修復APIの安全制約（同一パス禁止・出力拡張子制限）が壊れていないことを確認する。

## 2. 事前準備
1. ビルド（Debug/x64）  
   `C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe IndigoMovieManager_fork.csproj /t:Build /p:Configuration=Debug /p:Platform=x64 /m`
2. 対象テストを実行  
   `dotnet test Tests/IndigoMovieManager_fork.Tests/IndigoMovieManager_fork.Tests.csproj -c Debug --filter "FullyQualifiedName~AutogenExecutionFlowTests|FullyQualifiedName~VideoIndexRepairServiceTests"`
3. 破損FLVサンプルを1本用意（例: `E:\fla26.tmp.flv`）。

## 3. 手動チェック手順（6ステップ）
1. アプリを起動し、破損FLVを監視対象へ投入する。
2. サムネイル生成を実行し、初回失敗で再試行に入ることを確認する。
3. `logs\debug-runtime.log` を確認し、`index-probe` ログが出ることを確認する。  
   例: `probe result: movie='...', detected=True/False`
4. 破損検知時に `index-repair-summary` ログが出ることを確認する。  
   例: `repair applied` または `repair skipped`
5. 処理結果が以下のどちらかで妥当なことを確認する。  
   - 修復成功: サムネイル生成が `Done` になる  
   - 修復失敗: 既存ルール通り `Pending/Failed` 遷移する
6. `MainDB` のサムネパスと `QueueDB` の最終状態が矛盾しないことを確認する。

## 4. 合格条件
- 通常動画（`AttemptCount=0` 相当）で `index-probe` が過剰発火しない。
- リトライ対象（`AttemptCount>0`）でのみ `index-probe` / `index-repair-summary` が出力される。
- 一時修復ファイル（`%TEMP%\IndigoMovieManager_fork\index-repair\*.mkv`）が処理後に残留しない。
- テスト `AutogenExecutionFlowTests` と `VideoIndexRepairServiceTests` が成功する。

## 5. 実施ログ（記入欄）
- [ ] 手順1: 破損FLV投入
- [ ] 手順2: 再試行入り確認
- [ ] 手順3: `index-probe` ログ確認
- [ ] 手順4: `index-repair-summary` ログ確認
- [ ] 手順5: 状態遷移確認
- [ ] 手順6: DB整合確認

### メモ
- 実施日時:
- 実施者:
- 対象MainDB:
- 結果:
- 補足:
