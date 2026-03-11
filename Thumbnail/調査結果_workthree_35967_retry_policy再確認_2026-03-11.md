# 調査結果 workthree 35967 retry policy再確認 2026-03-11

最終更新: 2026-03-11

## 1. 対象
- `E:\_サムネイル作成困難動画\作成1ショットOK\35967.mp4`

## 2. ffprobe
- `duration=2872.529002`
- `size=757788401`
- `bit_rate=2110442`
- video stream:
  - `codec_name=h264`
  - `width=320`
  - `height=240`
  - `avg_frame_rate=25/1`
  - `nb_frames=71813`
- 補足:
  - `attached_pic=1` の PNG stream が先頭にある

## 3. bench結果

### 3.1 非 Recovery
- `autogen`
  - `success=failed`
  - `error_message=No frames decoded`
  - `elapsed_ms=4259`
  - `output_bytes=0`
- `ffmpeg1pass`
  - `success=success`
  - `elapsed_ms=47069`

## 4. midpoint 直接抽出
- `midpoint_sec = 1436.2645`
- `ffmpeg -ss midpoint -frames:v 1` は成功
- 出力先:
  - `C:\Users\{username}\AppData\Local\Temp\35967_midpoint.jpg`

## 5. 判断
- いまの `workthree` でも `autogen = No frames decoded` と `ffmpeg1pass success` の差分は維持されている。
- 中央 1 枚抜きも成功したため、`35967型` は引き続き `retry policy` 側の代表個体でよい。
- `repair workflow` 前提に寄せる必要はなく、通常系初回失敗後に one-pass へ落とす条件の説明に使いやすい。

## 6. 成功条件として固定する項目
- `service_error = No frames decoded`
- `ffmpeg1pass_success = true`
- `ffmpeg_midpoint_success = true`
- `duration_sec = 2872.529002`
- `estimated_bitrate_kbps = 2110.442`
- `decision_basis = autogen-no-frames + ffmpeg1pass-success + ffmpeg-midpoint-success`

## 7. 本線への示唆
- `35967型` は引き続き最優先の `retry policy` 候補として返してよい。
- 補助条件として bitrate を見るのは有効だが、主判定はあくまで `ffmpeg midpoint success` と `ffmpeg1pass success` に置く。
- `インデックス破壊-093-2-4K.mp4` のような repair 主体個体とは分離して扱う。

## 8. 参照ログ
- `C:\Users\{username}\AppData\Local\IndigoMovieManager_fork\logs\thumbnail-engine-bench-20260311-133123.csv`
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\連絡用doc_workthree_35967型判定基準と本線反映候補_2026-03-11.md`
