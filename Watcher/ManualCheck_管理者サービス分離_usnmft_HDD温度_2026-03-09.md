# Manual Check（管理者サービス分離: usnmft / HDD温度 2026-03-09）

## 1. 目的
- 通常権限UIから `usnmft` を service 経由で使えることを確認する。
- service 不在時の fallback reason が UI とログで見えることを確認する。
- Thumbnail 系 telemetry が service source に切り替わることを確認する。
- HDD温度 probe chain は最後に回す前提で、現時点では `Unavailable` を正常系として扱う。

## 2. 前提
- 本体と `IndigoMovieManager.AdminService.exe` が同じ出力先にあること。
- 実行環境は `x64`。
- `FileIndexProvider=usnmft` を切り替えられること。
- 管理者サービス起動可否を切り替えられること。

## 3. 手動確認シナリオ

| ID | シナリオ | 期待結果 | 実施状況 | 結果メモ |
|---|---|---|---|---|
| MC-001 | 通常権限UIで `FileIndexProvider=usnmft` を選び監視開始 | 候補収集が成功し、`usnmft` reason が `ok:provider=usnmft` 系で残る | 未実施 |  |
| MC-002 | 管理者サービスを停止した状態で同条件を再実行 | 通常監視へ fallback し、service 未接続 reason が見える | 未実施 |  |
| MC-003 | 管理者サービス起動後に再実行 | fallback せず service 経路へ復帰する | 未実施 |  |
| MC-004 | ThumbnailQueue 実行中の telemetry runtime を確認 | `usnmft_source=service` または `DiskThermalSource=Service` が観測できる | 未実施 |  |
| MC-005 | HDD温度取得不能な環境で Queue 実行 | `Unavailable` でも処理継続し、異常終了しない | 未実施 | HDD温度 probe chain 実装後に再確認 |

## 4. ログ確認ポイント
- `availability_error:AdminServiceUnavailable`
- `availability_error:TimeoutException`
- `everything_query_error:TimeoutException`
- `everything_query_error:UnauthorizedAccessException`
- `ok:empty_result_fallback`

## 5. 補足
- 本メモは手動確認の記録先。実施後は `実施状況` と `結果メモ` を更新する。
- HDD温度 probe chain は別タスク完了後に `MC-005` を再実施する。
