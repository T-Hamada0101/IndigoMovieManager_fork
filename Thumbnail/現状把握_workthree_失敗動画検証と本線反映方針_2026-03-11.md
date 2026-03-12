# 現状把握 workthree 失敗動画検証と本線反映方針

最終更新: 2026-03-11

## 1. この作業ラインの目的
- `workthree` は「サムネイル失敗動画を実動画で検証し、成功パターンを本線へ戻す」ための専用ラインとする。
- ここでの価値は機能追加そのものではなく、失敗条件と救済条件を実データで固めることにある。
- 個別動画にだけ効く場当たり対応は避け、再現した失敗パターンを一般条件へ落とす。

## 2. 現状
- 本線側には、`SWF` 事前判定、`FailureDb`、短尺 `ffmpeg1pass` 救済、長尺 `autogen no-frames` 救済の一部が入っている。
- それでも `E:\_サムネイル作成困難動画` の一括試行では未救済動画が残っている。
- 2026-03-11 時点の最新一括試行結果は以下。
  - 対象: 25件
  - 成功: 10件
  - 失敗: 15件
  - 主な失敗理由: `No frames decoded` 13件、`Autogen produced a near-black thumbnail` 2件
- 代表長尺 no-frames 事例では、`ffmpeg.exe` の中間位置 1枚抜きと `autogen` の差が出るケースを確認済み。

## 3. このラインでやること
- 失敗動画を実動画で再現し、`autogen` / `ffmpeg1pass` / repair のどこで救えるかを切り分ける。
- 成功した条件を、ファイル名ではなく失敗パターンベースの一般条件へ変換する。
- 明示テスト、playground テスト、`FailureDb`、runtime log を使って、成功理由と失敗理由を記録する。
- 本線へ戻す時は「救済条件」「除外条件」「回帰テスト」をセットで渡す。

## 4. このラインでやらないこと
- 特定ファイル名だけを見て分岐する本番実装。
- UI 都合の暫定逃げや、検証結果のない推測修正。
- 本線の責務分離を崩す近道実装。

## 5. 本線へ戻す判断基準
- 失敗パターンが2件以上の実例、または代表ケース1件で再現性を確認できること。
- 条件がファイル名依存ではなく、動画特性または実行結果で説明できること。
- 追加した救済で既存成功ケースを壊さない回帰テストがあること。
- 可能なら `FailureKind` と対策文書へ反映し、運用側が追えること。

## 6. 本線へ戻す時に必ず欲しい判断材料
- 動画ごとの失敗理由
  - どの engine で、どのエラーで失敗したか。
  - `near-black` と `No frames decoded` のような見た目が似た失敗も分離する。
- 成功した条件
  - 成功した `engine`、`seek`、`repair`、`preflight` 条件を動画単位で残す。
  - 成功時に追加で必要だった分岐や補助条件があるなら併記する。
- 再現率
  - 1回だけ成功したのか、複数回同条件で再現するのかを分ける。
  - 本線へ入れる条件は、最低でも代表ケースで再現確認済みであることを前提にする。
- 本番導入位置
  - `preflight`、`retry policy`、`repair workflow`、`finalizer` 前のどこへ入れるのが最小かを明記する。
  - playground で効いた条件でも、本番導入位置が曖昧なら保留扱いにする。
- 既存 `FailureKind` で足りるか
  - 既存分類で吸収できるなら新設しない。
  - 既存分類で判断が濁る場合だけ、`FailureKind` の追加または補助属性を検討する。

## 6.1 受領後に本線側で直ちに切る観点
- 一般化できる救済条件か。
- 特定動画専用の回避策に落ちていないか。
- 既存成功ケースへの副作用が小さい導入位置はどこか。
- `FailureDb`、`HangSuspected`、失敗タブ、観測スクリプト、計画書のどこへ反映が必要か。

## 7. 直近の重点対象
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork\Thumbnail\Engines\引き継ぎ_autogenEOFドレイン対応とベンチ画像出力_f3fd039_2026-03-11.md` を前提に、超短尺 `No frames decoded` 群を先に整理する。
- その後で、真の `Autogen produced a near-black thumbnail` 2件の救済条件整理を行う。
- `No frames decoded` 13件の中で、一般化価値が高い個体を優先して切る。
- `35967.mp4` や `インデックス破壊-093-2-4K.mp4` など、既存知見がある no-frames 代表を先に追う。
- `画像1枚あり顔.mkv` と `画像1枚ありページ.mkv` は、short + repair + fallback の再現率確認対象として維持する。

## 7.2 EOFドレイン取り込みの位置づけ
- `f3fd039` の主対象は、超短尺動画で
  - `send_packet` は通る
  - `receive_frame` が `EAGAIN`
  - そのまま `EOF`
  - `No frames decoded`
になる群である。
- したがって、`画像1枚あり顔.mkv` / `画像1枚ありページ.mkv` の整理には直接効く可能性が高い。
- 一方で、true near-black 2件は現時点で `Autogen produced a near-black thumbnail` が安定しているため、`EOFドレイン` を取り込んでも主論点は
  - 黒判定しきい値
  - 代表フレーム選定
  - `ffmpeg1pass` 逃がし条件
のまま変わらない見込みである。
- つまり `EOFドレイン` は near-black の解決策ではなく、「near-black 調査の母集団をきれいにする前処理」として扱う。

## 7.3 成功パターン集からの参照点
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork\Thumbnail\調査結果_難読動画成功パターン集_2026-03-11.md` のうち、`workthree` で直接効くのは次の3点。
- `P-03`
  - 超短尺・1フレーム動画は `EOF` 後 drain で救える
  - `画像1枚あり顔.mkv` / `画像1枚ありページ.mkv` の整理に直結する
- `P-04`
  - true near-black は「本当に黒い」のではなく、backward seek 後の古い暗フレーム混入が原因になり得る
  - `P1B-01` / `P1B-02` は、`EOFドレイン` 後も near-black が残るならこの論点で掘る
- `P-05`
  - 長尺 low-bitrate / partial file 群は `autogen` 本線の改善だけでなく、repair / onepass の責務として分ける
  - `35967型` と `インデックス破壊-093-2-4K.mp4` の切り分け時に参照する

## 7.1 `35967.mp4 型` の暫定判定基準
- 主条件
  - `autogen` / `service` 側は `No frames decoded`
  - `ffmpeg.exe` の中間1枚抜きは成功
  - 長尺動画である
- 補助条件
  - duration に対して推定 bitrate が極端に低くない
  - これは「明らかにスカスカな破損動画」を除外するための補助情報として使う
- 運用上の注意
  - bitrate だけで判定しない
  - `インデックス破壊-093-2-4K.mp4` のように bitrate が十分高くても `ffmpeg` 中間1枚抜きに失敗する個体は別群
  - したがって、主条件は必ず `ffmpeg midpoint success` を含める

## 8. 関連資料
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\調査結果_workthree_全動画再試行ベースライン_2026-03-11.md`
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\連絡用doc_workthree_35967型判定基準と本線反映候補_2026-03-11.md`
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\連絡用doc_workthree_画像1枚あり顔_極小seek成功条件_2026-03-11.md`
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\Implementation Plan_workthree_35967型救済条件の本線反映_2026-03-11.md`
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\調査結果_workthree_優先順自動実行_2026-03-11.md`
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\優先順位表_workthree_失敗15件の検証順_2026-03-11.md`
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork\Thumbnail\Engines\引き継ぎ_autogenEOFドレイン対応とベンチ画像出力_f3fd039_2026-03-11.md`
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork\Thumbnail\サムネイルが作成できない動画対策.md`
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork\Thumbnail\調査結果_難読動画成功パターン集_2026-03-11.md`
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork\Thumbnail\設計メモ_FailureKind_失敗分類と回復方針案_2026-03-09.md`
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork\Thumbnail\連絡用_workthree救済条件の受け皿整理_FailureDbExtraJson_2026-03-11.md`
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork\Thumbnail\設計整理_FailureDbExtraJson先行反映範囲_2026-03-11.md`
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork\Tests\IndigoMovieManager_fork.Tests\DifficultVideoBatchPlaygroundTests.cs`
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork\Tests\IndigoMovieManager_fork.Tests\AutogenRepairPlaygroundTests.cs`
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork\Tests\IndigoMovieManager_fork.Tests\FfmpegShortClipRecoveryPlaygroundTests.cs`

## 9. 備考
- `workthree` は検証専用ラインであり、ここで確定した一般条件だけを本線へ戻す。
- 本線へ戻す際は、検証用 playground そのものを持ち込むのではなく、必要最小限の実装と回帰テストへ畳む。
