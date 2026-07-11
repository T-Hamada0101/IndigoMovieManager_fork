# forAI README

最終更新日: 2026-07-11

このフォルダは、AI や実装担当が着手前に読む資料を集める場所です。
進行中の計画と調査結果をここへ寄せます。完了済みの一時的な作業指示やレビュー記録は `Archive` へ移します。

## 着手前にまず見るもの

1. **[../../AGENTS.md](../../AGENTS.md)**
2. **[../../AI向け_現在の全体プラン_workthree_2026-03-20.md](../../AI向け_現在の全体プラン_workthree_2026-03-20.md)**
3. **[ドキュメント案内_人向け_AI向け_2026-03-12.md](ドキュメント案内_人向け_AI向け_2026-03-12.md)**

## まず読むと迷いにくい資料

- **[ドキュメント案内_人向け_AI向け_2026-03-12.md](ドキュメント案内_人向け_AI向け_2026-03-12.md)**
  - 人向け入口と AI 向け入口をまとめた案内です。
- **[AI向け_大機能詳細理解書_2026-03-07.md](AI向け_大機能詳細理解書_2026-03-07.md)**
  - 起動、Watcher、Thumbnail、検索の大きな責務をまとめた理解書です。
- **[Goal_Indigoの未来図_2026-05-28.md](Goal_Indigoの未来図_2026-05-28.md)**
  - Indigo 全体の将来像と判断基準を示す上位ゴールです。
- **[Implementation Plan_UIを含む高速化のための抜本改善プラン_2026-04-17.md](Implementation%20Plan_UIを含む高速化のための抜本改善プラン_2026-04-17.md)**
  - UI の詰まり解消と体感高速化を進める現行実装計画です。
- **[Implementation Plan_長期ロードマップ_体感高速化UI分離_Worker契約_2026-06-18.md](Implementation%20Plan_長期ロードマップ_体感高速化UI分離_Worker契約_2026-06-18.md)**
  - UI 分離と Worker 契約を段階的に進める長期ロードマップです。
- **[ThumbnailLogic_2026-02-28.md](../Gemini/ThumbnailLogic_2026-02-28.md)**
  - サムネイル生成の流れを技術的に追う入口です。
- **[ThumbnailEngineRouting_2026-03-01.md](../Gemini/ThumbnailEngineRouting_2026-03-01.md)**
  - エンジン切り替え基準を確認する入口です。
- **[Everything_to_Everything_Flow_Design_2026-02-28.md](../Gemini/Everything_to_Everything_Flow_Design_2026-02-28.md)**
  - Watcher / Everything 系のフロー設計です。
- **[FFmpeg_Guidelines.md](FFmpeg_Guidelines.md)**
  - FFmpeg 利用方針と関連調査の入口です。
- **[WhiteBrowser_タグ仕様書_2026-04-01.md](WhiteBrowser_%E3%82%BF%E3%82%B0%E4%BB%95%E6%A7%98%E6%9B%B8_2026-04-01.md)**
  - WhiteBrowser のタグ、tagbar、タグ検索、タグレット、関連 API の正本整理です。
- **[EmojiPathStatus_2026-03-01.md](../Gemini/EmojiPathStatus_2026-03-01.md)**
  - 絵文字パス問題の現状整理です。
- **[動的並列_ジョブ優先度とスレッド優先度の違い_2026-03-05.md](../Gemini/動的並列_ジョブ優先度とスレッド優先度の違い_2026-03-05.md)**
  - 並列制御まわりの設計判断を追う資料です。
- **[ライブラリ比較_変換速度ベンチ結果_2026-02-25.md](../Gemini/ライブラリ比較_変換速度ベンチ結果_2026-02-25.md)**
  - ベンチ結果から採用判断を確認する資料です。

## 文書の見分け方

- `AI向け_作業指示_*.md`
  - 実装担当に渡すスコープと禁止線です。
- `AI向け_レビュー指示_*.md`
  - レビュー担当に渡す観点です。
- `AI向け_レビュー結果_*.md`
  - 受け入れ判断と残課題の記録です。完了後は `Archive` へ移します。
- `Implementation Plan_*.md`
  - 実装計画と段取りです。
- `調査結果_*.md`
  - 事象の切り分けと原因分析です。
- `_初版.md`
  - 初期メモです。通常は日付付きの新版を優先します。

## 他フォルダへの入口

- 完了済み資料の履歴を見る
  - **[Archive/README.md](Archive/README.md)**

- 人が全体像を掴む資料を見る
  - **[../forHuman/README.md](../forHuman/README.md)**
- 背景説明や熱量高めの補助資料を見る
  - **[../Gemini/README.md](../Gemini/README.md)**
- Watcher 領域を見る
  - **[../../Watcher/README.md](../../Watcher/README.md)**
- Thumbnail 領域を見る
  - **[../../Thumbnail/README.md](../../Thumbnail/README.md)**
