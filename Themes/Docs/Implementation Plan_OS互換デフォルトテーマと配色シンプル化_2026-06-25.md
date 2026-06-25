# Implementation Plan OS互換デフォルトテーマと配色シンプル化 2026-06-25

最終更新日: 2026-06-25

## 目的

新規環境の既定テーマを OS 追従へ寄せ、Simple 系テーマと本体 UI 部品の文字色・背景色を中立で読みやすい構成へ整理する。

## 実装方針

- `ThemeMode` の新規既定値は `SystemAuto` とする。
- 既存ユーザーが保存済みの `Indigo` / `SimpleLight` / `SimpleDark` / `SystemAuto` は尊重する。
- `SimpleLight` は白背景、標準文字色、薄い灰色境界を中心にする。
- `SimpleDark` は中立灰色の暗色面と明るい文字色を中心にする。
- WPF 標準テンプレートが残る部品でも白背景/白文字を起こさないよう、各テーマで `SystemColors.*BrushKey` を明示する。
- ボタンとタブは軽量テンプレートを持たせ、選択状態や hover 状態で背景と文字が分離しないようにする。
- 設定フォームの左カテゴリ、内容面、最近使った管理ファイルのボタンは `MaterialDesignPaper` / `AppSurface*` / `AppText*` 系へ揃える。
- `App.ApplyTheme(...)` で上側タブ文字色を固定 `DarkGray` へ落とさず、テーマ辞書の本文色へ追従させる。

## 禁止線

- `Indigo` は従来互換を尊重しつつ、読めない配色は残さない。
- MaterialDesign 本体パッケージ依存へ戻さない。
- OS自動を理由に、既存ユーザーの保存済みテーマを強制移行しない。
- WPF 既定テンプレート任せで、選択中タブや ComboBox の文字色が背景から浮く状態へ戻さない。

## 検証

- `ThemeMode` の既定値が `SystemAuto` であること。
- `SimpleLight` / `SimpleDark` の主要 Brush key が Profile 内だけで解決できること。
- `SystemAuto` は OS 状態に応じて `SimpleLight` または `SimpleDark` を読むこと。
- `SystemColors.*BrushKey` と軽量テンプレートの存在をソースポリシーテストで守ること。
