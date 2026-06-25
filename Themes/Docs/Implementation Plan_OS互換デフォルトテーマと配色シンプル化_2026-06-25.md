# Implementation Plan OS互換デフォルトテーマと配色シンプル化 2026-06-25

最終更新日: 2026-06-25

## 目的

新規環境の既定テーマを OS 追従へ寄せ、Simple 系テーマの文字色と背景色を中立で読みやすい構成へ整理する。

## 実装方針

- `ThemeMode` の新規既定値は `SystemAuto` とする。
- 既存ユーザーが保存済みの `Indigo` / `SimpleLight` / `SimpleDark` / `SystemAuto` は尊重する。
- `SimpleLight` は白背景、標準文字色、薄い灰色境界を中心にする。
- `SimpleDark` は中立灰色の暗色面と明るい文字色を中心にする。
- `App.ApplyTheme(...)` で上側タブ文字色を固定 `DarkGray` へ落とさず、テーマ辞書の本文色へ追従させる。

## 禁止線

- `Indigo` の従来互換色は勝手に壊さない。
- MaterialDesign 本体パッケージ依存へ戻さない。
- OS自動を理由に、既存ユーザーの保存済みテーマを強制移行しない。

## 検証

- `ThemeMode` の既定値が `SystemAuto` であること。
- `SimpleLight` / `SimpleDark` の主要 Brush key が Profile 内だけで解決できること。
- `SystemAuto` は OS 状態に応じて `SimpleLight` または `SimpleDark` を読むこと。
