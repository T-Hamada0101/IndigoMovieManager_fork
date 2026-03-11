# 実行手順 workthree 動画ラベル環境変数テンプレート 2026-03-11

最終更新: 2026-03-11

## 1. 目的
- `workthree` 側の Explicit テストを、ローカル絶対パス直書きなしで再実行する。
- ラベル化した対象動画を、環境変数で安全に差し替える。

## 2. 対象テスト
- `NearBlackBatchPlaygroundTests.cs`
  - 環境変数: `IMM_TEST_NEAR_BLACK_MOVIES`
- `P1ABranchDiffBatchTests.cs`
  - 環境変数: `IMM_TEST_P1A_MOVIES`
- `TrueNearBlackPairTests.cs`
  - 環境変数: `IMM_TEST_TRUE_NEAR_BLACK_MOVIES`

## 3. 値の入れ方
- 区切り文字は次を許容する。
  - `;`
  - `|`
  - 改行
- 並び順でラベルに対応する。

## 4. PowerShell 7 での設定例
```powershell
$env:IMM_TEST_TRUE_NEAR_BLACK_MOVIES = @'
C:\path\to\movie01.mp4
C:\path\to\movie02.mp4
'@

$env:IMM_TEST_P1A_MOVIES = @'
C:\path\to\movie11.mp4
C:\path\to\movie12.mp4
C:\path\to\movie13.mp4
'@

$env:IMM_TEST_NEAR_BLACK_MOVIES = @'
C:\path\to\movie01.mp4
C:\path\to\movie11.mp4
C:\path\to\movie12.mp4
C:\path\to\movie13.mp4
C:\path\to\movie02.mp4
'@
```

## 5. 実行例
```powershell
dotnet test .\Tests\IndigoMovieManager_fork.Tests\IndigoMovieManager_fork.Tests.csproj `
  -c Debug `
  -p:Platform=x64 `
  --filter "FullyQualifiedName~TrueNearBlackPairTests" `
  -- NUnit.ExplicitMode=None
```

## 5.2 `f3fd039` 取り込み後の再実行順
- 参照:
  - `C:\Users\{username}\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\作業メモ_workthree_f3fd039取り込み後のnear_black再実行順_2026-03-11.md`
- 推奨順:
  1. short no-frames 群のベンチまたは playground を先に流す
  2. `NearBlackBatchPlaygroundTests`
  3. `TrueNearBlackPairTests`
- 理由:
  - `EOFドレイン` は near-black 解決ではなく、超短尺 `No frames decoded` 群の取りこぼし対策だから
  - short no-frames 群を先に整理してから true near-black を見る方が、母集団がぶれない

## 5.1 実行テンプレート
- 次のテンプレートを複製して使う。
  - `C:\Users\{username}\source\repos\IndigoMovieManager_fork_workthree\scripts\run_workthree_label_tests.example.ps1`
- 実パスはローカルでだけ埋める。
- `.example` のままコミットし、実データ入り版はコミットしない。

## 6. 注意
- 既定値にローカル絶対パスは持たせない。
- ラベル対応は doc 側で管理し、テストコードへ推測可能な動画名を戻さない。
- `ffmpeg` / `ffprobe` を明示したい場合は次を使う。
  - `IMM_FFMPEG_EXE_PATH`
  - `IMM_FFPROBE_EXE_PATH`
- 対象動画がローカルに存在しない環境では、Explicit テストは `Assert.Ignore` で `skip` になる。

## 7. 関連
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\優先順位表_workthree_失敗15件の検証順_2026-03-11.md`
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\調査結果_workthree_near_black群ベースライン_2026-03-11.md`
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\調査結果_workthree_P1-A_branch差分確認_2026-03-11.md`
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\調査結果_workthree_true_near_black_2件固定_2026-03-11.md`
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\作業メモ_workthree_f3fd039取り込み後のnear_black再実行順_2026-03-11.md`
