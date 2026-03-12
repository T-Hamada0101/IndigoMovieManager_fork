# 注意事項 autogen shared DLL 向き先 2026-03-13

## 結論
- `autogen` を単体で試す時、`IMM_FFMPEG_EXE_PATH` を `ffmpeg.exe` に向けると失敗しやすい。
- `autogen` が必要としているのは `ffmpeg.exe` 本体ではなく、shared DLL が置いてあるフォルダ。
- 今回の正解は次。
  - `C:\Users\na6ce\source\repos\IndigoMovieManager_fork\tools\ffmpeg-shared`

## 今回実際に起きたこと
- `みずがめ座 (2).mp4` の `autogen 1秒 1枚` テストで、最初は `false` になった。
- しかし動画デコード失敗ではなく、初期化条件が違っていただけだった。
- `IMM_FFMPEG_EXE_PATH` を `ffmpeg.exe` 側へ向けた単体実行では、次の失敗になった。
  - `autogen init failed: DirectoryNotFoundException: ffmpeg shared directory not found. expected tools/ffmpeg-shared or IMM_FFMPEG_EXE_PATH`
- さらに `ffmpeg.exe` のある通常 ffmpeg フォルダを向けても、shared DLL ではないため `DynamicallyLoadedBindings.Initialize()` 側で失敗した。
- `tools\ffmpeg-shared` を向け直すと、`autogen` は `1秒 1枚` に成功した。

## 間違いやすい点
- `IMM_FFMPEG_EXE_PATH` という名前のせいで、`ffmpeg.exe` のフルパスを入れたくなる。
- しかし `autogen` 側の実装は `ResolveFfmpegSharedDirectory()` で shared DLL の場所を探している。
- そのため、単体調査では「CLI 用 ffmpeg」と「AutoGen 用 shared DLL」を同一視しない。

## 正しい指定

### autogen 単体調査
- 推奨:
```powershell
$env:IMM_FFMPEG_EXE_PATH='C:\Users\na6ce\source\repos\IndigoMovieManager_fork\tools\ffmpeg-shared'
```

### 間違いやすい指定
- これだと `autogen` 調査では誤ることがある:
```powershell
$env:IMM_FFMPEG_EXE_PATH='C:\Users\na6ce\source\repos\IndigoMovieManager_fork\tools\ffmpeg\ffmpeg.exe'
```

## 今後のルール
- `autogen` の単体確認を始める前に、最初に `IMM_FFMPEG_EXE_PATH` の中身を確認する。
- `autogen` で `false` が出たら、先に動画破損を疑うのではなく shared DLL の向き先を疑う。
- 単体調査メモには、次を必ず残す。
  - `IMM_FFMPEG_EXE_PATH`
  - `autogen` 成否
  - 出力画像パス

## ひとことで覚える
- `ffmpeg1pass` は `ffmpeg.exe` を使う。
- `autogen` は `ffmpeg-shared` を使う。
