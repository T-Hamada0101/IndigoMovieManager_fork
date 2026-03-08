# Implementation Plan: SWF埋め込み画像抽出優先化（2026-03-08）

## 目的
- SWFサムネイル作成を、まず「描画せずに取れる画像があるか」で判定する。
- 埋め込み画像を直接抜けたSWFは、`ffmpeg` の描画キャプチャに依存しない。
- 埋め込み画像が無い、または読めないSWFだけ既存の `ffmpeg` 経路へ縮退する。

## 今回の実装範囲
- `FWS` と `CWS` のヘッダー判定
- `CWS` の zlib 展開
- タグ走査
- `DefineBits` / `DefineBitsJPEG2` / `DefineBitsJPEG3` / `DefineBitsJPEG4` からのJPEG系抽出
- 抽出成功時の正規化JPEG保存
- 抽出失敗時の既存 `ffmpeg` 候補試行への縮退

## 今回あえて外した範囲
- `ZWS` の LZMA 展開
- `DefineBitsLossless` / `DefineBitsLossless2` のPNG相当復元
- `DefineShape` 系のベクター再描画
- ActionScript 実行を伴う動的画面の完全再現

## 実装方針
1. `SwfEmbeddedImageExtractor` でSWFを正規化してタグ列へ進む
2. JPEG系タグのうち、読める画像だけを候補として拾う
3. 白一色寄り画像は候補から外す
4. 一番大きい画像を代表画像として採用する
5. 代表画像をタブサイズへ正規化してJPEG保存する
6. 取れなければ `SwfThumbnailGenerationService` の既存 `ffmpeg` 候補試行へ落とす

## テスト観点
- `FWS` の `DefineBitsJPEG2` を直接抽出できる
- `CWS` の `DefineBitsJPEG2` を直接抽出できる
- 直接抽出できた場合、`ffmpeg` 縮退が呼ばれない

## 次の拡張候補
- `DefineBitsLossless2` のARGB復元
- `JPEGTables` 依存が強い古い `DefineBits` の実データ検証強化
- 埋め込み画像が複数ある時の採用基準に「面積だけでなく中央配置や彩度」を追加
- `ZWS` 対応
