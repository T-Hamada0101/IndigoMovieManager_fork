# Implementation Plan workthree 35967型救済条件の本線反映 2026-03-11

最終更新: 2026-03-11

## 0. 目的
- `35967.mp4` で確認した `autogen no-frames + ffmpeg midpoint success` 型を、本線へ戻すための最小実装計画を定義する。
- ファイル名依存ではなく、一般条件として `retry policy` へ落とす。

## 1. 背景
- `workthree` の検証で、`35967.mp4` は次を満たした。
  - `service` / `autogen` 側は `No frames decoded`
  - `ffmpeg.exe` の中間1枚抜きは成功
  - 長尺
  - bitrate は極端に低くない
- 同時に、`インデックス破壊-093-2-4K.mp4` は bitrate が高くても `ffmpeg` 中間1枚抜きに失敗した。
- このため、本線へ戻す条件は
  - bitrate 単独
  ではなく
  - `autogen no-frames + ffmpeg midpoint success`
  を主条件とする必要がある。

## 2. 代表ケースで確認済みの事実
- 対象:
  - `35967.mp4`
- duration:
  - `2872.529` 秒
- midpoint:
  - `1436.265` 秒
- estimated bitrate:
  - `2110.4 kbps`
- `service`:
  - `No frames decoded`
- `ffmpeg midpoint`:
  - 成功

## 3. 本線へ戻す暫定条件

### 3.1 主条件
- `autogen` / `service` 側は `No frames decoded`
- `ffmpeg.exe` の中間1枚抜きは成功
- 長尺動画である
- 手動実行ではない

### 3.2 補助条件
- duration に対して推定 bitrate が極端に低くない
- 用途:
  - 明らかに中身が薄い動画を除外する補助情報

### 3.3 今回は主条件に含めないもの
- bitrate の固定閾値
- repair の成功有無
- `FailureKind` の新設

## 4. 導入位置
- 第一候補:
  - `retry policy`
- 理由:
  - `autogen` 初回失敗後に `ffmpeg1pass` 救済へ落とす条件として最小
- 今回避ける位置:
  - `preflight`
    - 中間1枚抜き probe を前段へ入れると通常系への負荷が大きい
  - `finalizer`
    - ここでは遅すぎる

## 5. 実装方針
1. `autogen` 失敗結果から `No frames decoded` を判定する
2. 長尺条件を満たす場合のみ、限定的な `ffmpeg midpoint probe` を実行する
3. probe 成功時だけ `ffmpeg1pass` 救済へ落とす
4. probe 失敗時は現行フロー維持

## 6. 記録したい項目
- `service_error`
- `duration_sec`
- `midpoint_sec`
- `ffmpeg_midpoint_success`
- `estimated_bitrate_kbps`
- `decision_basis = autogen-no-frames + ffmpeg-midpoint-success`

## 7. 受け入れ基準
- `35967.mp4` 型で `ffmpeg1pass` 救済へ落ちる
- `インデックス破壊-093-2-4K.mp4` のような別群へ誤適用しない
- 既存の near-black / short-clip / index-repair 系を壊さない

## 8. リスク
- `ffmpeg midpoint probe` を増やすと I/O コストが増える
  - 対策:
    - `No frames decoded`
    - 長尺
    の時だけに限定する
- bitrate 条件の過信
  - 対策:
    - bitrate は補助情報に留め、主条件へ入れない

## 9. 次タスク
1. 本線側の `retry policy` 現状を確認する
2. `35967.mp4` 型に対応する最小分岐の差し込み位置を決める
3. 誤適用防止の回帰テスト観点を列挙する
   - 別紙: `設計メモ_workthree_35967型誤適用防止テスト観点_2026-03-11.md`

## 10. 関連
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\連絡用doc_workthree_35967型判定基準と本線反映候補_2026-03-11.md`
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\調査結果_workthree_優先順自動実行_2026-03-11.md`
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\現状把握_workthree_失敗動画検証と本線反映方針_2026-03-11.md`
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\設計メモ_workthree_35967型誤適用防止テスト観点_2026-03-11.md`
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\設計メモ_workthree_retry_policy差し込み位置_35967型と顔型_2026-03-11.md`
