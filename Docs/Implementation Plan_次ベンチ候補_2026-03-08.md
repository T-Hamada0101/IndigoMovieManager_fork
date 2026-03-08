# 次ベンチ候補 (2026-03-08)

## 結論
- 次にやるべきベンチは `709a137` 側の同条件測定
- 比較対象は以下の2本
  - `D:\BentchItem_HDD`

## 優先順
1. `709a137` + `D:\BentchItem_HDD`
2. 必要なら上流現行の `TabIndex=4` 再測定

## 理由
- 上流現行の挙動は一通り取れた
- いま不足しているのは「初期原型はどこまで遅いか」の基準線
- フォーク現行はまだ実装中なので、比較対象に入れると数値が揺れる

## 実施条件
- `D:\BentchItem_HDD`
  - 完走を狙う
  - `thumb_start / thumb_end` 件数を見る
- `D:\BentchItem_EXBIG`
  - 今回は実施しない
  - 追加実測も原因調査も行わない

## 記録項目
- `open_datafile_end elapsed_ms`
- `scan_end elapsed_ms`
- `thumb_start` 件数
- `thumb_end` 件数
- 出力jpg件数
- タイムアウト有無
- 表示不整合有無

## 注意
- `.swf` は見た目成功を成功判定に使わない
- 既存jpg残留を避けるため、毎回クリーン開始
- `EXBIG` は今回は対象外
