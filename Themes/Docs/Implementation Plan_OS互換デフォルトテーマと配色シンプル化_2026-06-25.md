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
- 本体ヘッダーとメインタブの操作 UI は下部タブ「サムネイル進捗」程度の高密度に寄せ、サムネイル表示領域を優先して確保する。
- ヘッダーは 32px 帯、検索欄は 26px、ヘッダーボタン / コンパクト ComboBox / メインタブは 24px 帯を基準にする。
- 上部タブは個別 `Background` を持たせず、`AppTabItemStyle` の標準色へ任せる。
- ComboBox とドロップダウン項目は `SystemColors.Window*` 系へ寄せ、閉じた状態と展開状態で背景色が割れないようにする。
- ComboBox の閉じた状態は軽量テンプレートで描画し、入力型 ComboBox 用の `PART_EditableTextBox` は残して検索・設定入力を壊さない。
- 無指定の Button / ComboBox も暗黙 style で軽量テンプレートへ流し、局所画面だけ WPF 既定色へ戻る経路を減らす。
- 動画一覧パネルの `ListView` / `DataGrid` 背景も `SystemColors.WindowBrushKey` へ寄せる。
- List タブの `DataGrid` 列ヘッダー / セル / 行はアプリ共通 DataGrid style へ接続し、列ヘッダーの白背景と白文字を残さない。
- 重複動画タブなど UserControl 内の UI パーツも、Button / ComboBox / ListBox / DataGrid をアプリ共通 style と `SystemColors.Window*` 系へ寄せる。
- 詳細ペインのサムネ表示モード ComboBox は MaterialDesign 風の透明指定を外し、アプリ共通 ComboBox style と `SystemColors.Window*` 系へ寄せる。
- 救済タブの対象タブ ComboBox とサムネイル進捗の未作成走査ボタンも共通 style へ接続し、細部の文字消えを防ぐ。
- ScrollBar は Simple テーマ共通の軽量テンプレートを持たせ、WPF 既定の白いスクロール部品が暗色面へ混ざらないようにする。
- 設定フォームの左カテゴリ、内容面、最近使った管理ファイルのボタンは `MaterialDesignPaper` / `AppSurface*` / `AppText*` 系へ揃える。
- `App.ApplyTheme(...)` で上側タブ文字色を固定 `DarkGray` へ落とさず、テーマ辞書の本文色へ追従させる。

## 禁止線

- `Indigo` は従来互換を尊重しつつ、読めない配色は残さない。
- MaterialDesign 本体パッケージ依存へ戻さない。
- OS自動を理由に、既存ユーザーの保存済みテーマを強制移行しない。
- WPF 既定テンプレート任せで、選択中タブや ComboBox の文字色が背景から浮く状態へ戻さない。
- 本体ヘッダーを 48px 帯、メインタブを 36px 帯へ戻してサムネイル領域を削らない。
- `TabSmall` / `TabBig` / `TabGrid` / `TabBig10` などの個別タブへ背景色指定を戻さない。
- ComboBox のドロップダウン項目や動画一覧パネルだけが別背景になる指定を増やさない。
- ComboBox の閉じた状態を WPF 既定テンプレートへ戻し、暗色テーマで白い面が出る状態へ戻さない。
- 無指定の Button / ComboBox を新しく増やし、Simple テーマでだけ白背景や白文字が出る状態へ戻さない。
- UserControl 内の一覧や表だけが WPF 既定白背景へ戻る指定を増やさない。
- 詳細ペインの ComboBox へ `MaterialDesignOutlinedComboBox` / `TextFieldAssist` / `HintAssist` を戻さない。
- ScrollBar を画面個別の白背景スタイルで上書きしない。

## 検証

- `ThemeMode` の既定値が `SystemAuto` であること。
- `SimpleLight` / `SimpleDark` の主要 Brush key が Profile 内だけで解決できること。
- `SystemAuto` は OS 状態に応じて `SimpleLight` または `SimpleDark` を読むこと。
- `SystemColors.*BrushKey` と軽量テンプレートの存在をソースポリシーテストで守ること。
- 本体ヘッダー / メインタブの高密度寸法が維持され、旧 48px / 36px 帯へ戻っていないこと。
- 上部タブに `UpperTabBackground` の個別指定が残っていないこと。
- ComboBoxItem の暗黙 style と動画一覧パネル背景の標準色指定をテストで守ること。
- ComboBox / ComboBoxItem の軽量テンプレート、`PART_Popup`、`PART_EditableTextBox` をソースポリシーテストで守ること。
- Button / ComboBox の暗黙 style、救済タブ対象 ComboBox、サムネイル進捗の未作成走査ボタンをテストで守ること。
- List タブの `DataGrid` 列ヘッダー / セル / 行 style が共通 DataGrid style へ接続されていること。
- 重複動画タブの UI パーツが共通 style と `SystemColors.Window*` 系を使うこと。
- 詳細ペインの ComboBox と ScrollViewer がテーマ標準色へ接続されていること。
- ScrollBar の共通軽量テンプレートと `AppScrollBarStyle` を Profile 内で解決できること。
