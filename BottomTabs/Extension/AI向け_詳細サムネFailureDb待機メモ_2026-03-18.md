# 詳細サムネ FailureDb 待機メモ

更新日: 2026-05-05

変更概要:
- 詳細サムネ表示時の既存 jpg 確認、ERROR marker 判定、FailureDb の open rescue 判定、未生成時の通常キュー投入、ERROR 時の救済投入を UI 入口から外し、DB パス付き snapshot を背景確認してから UI へ反映する形へ寄せた。
- 背景確認の後着は `AreSameMainDbPath` と選択中レコード確認で捨て、shutdown 中も反映・投入しない。

## 背景
- 下部詳細タブは、サムネ不足時に通常キュー投入と ERROR 救済を自動で行う。
- `TryEnqueueThumbnailDisplayErrorRescueJob(...)` は救済要求の前に `#ERROR` を消すため、未完了解析が残っていても次回描画では「ただの未生成」に見えやすい。

## 現在の扱い
- `MainWindow.BottomTab.Extension.DetailThumbnail.cs` では、`FailureDb` に detail(`tab=99`) の open rescue がある間は通常キューへ戻さない。
- この間は `errorGrid.jpg` placeholder を維持し、同じ detail rescue も重複要求しない。
- UI 側は予想サムネイルパスを先に入れるだけにし、`Path.Exists` / `Directory.Exists` / FailureDb read / rescue request 判定は背景側で行う。
- 背景結果は現在 MainDB と選択中レコードが一致する時だけ `ThumbDetail` と下部 snapshot 更新へ戻す。

## 目的
- サムネ完成前に通常キューへ何度も戻るループを止める。
- `Watcher` 側で止めた `main` サムネの再投入抑止と、詳細タブ側の挙動を揃える。
