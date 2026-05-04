# Implementation Plan テーマ分割とMaterialDesign切り分け 2026-05-04

## 目的

UI が重い原因を切り分けるため、色テーマとコントロールテーマを分離する。

既存の `Original` は歴史的に Indigo 相当として扱い、今後の表示名と内部モードは `Indigo` に寄せる。
MaterialDesign を使う従来互換の見た目を残しつつ、軽量な WPF 標準スタイルへ切り替えられる構成を作る。

## 結論

テーマモードは次の 4 つに整理する。

| モード | 役割 | MaterialDesign |
|---|---|---|
| `Indigo` | 従来互換テーマ。既存の見た目を守る逃げ道 | あり |
| `SimpleLight` | 軽量ライトテーマ | なしを目標 |
| `SimpleDark` | 軽量ダークテーマ | なしを目標 |
| `SystemAuto` | OS のライト/ダーク設定に追従 | なしを目標 |

`SystemAuto` は独立した色定義ファイルではなく、OS 状態に応じて `SimpleLight` または `SimpleDark` を選ぶモードとして扱う。

## 基本方針

1. `*.wb` は変更しない。
2. MaterialDesign 全撤去を最初の目標にしない。
3. hot path から順に軽量化する。
4. 既存 XAML が MaterialDesign のキーを直接参照し続けないよう、アプリ独自の style key を挟む。
5. `Indigo` は互換テーマとして残し、表示崩れや速度比較時の fallback に使う。
6. `SimpleLight` / `SimpleDark` / `SystemAuto` は軽量テーマとして育てる。

## 目標構成

```text
Themes/
  Colors/
    Indigo.xaml
    SimpleLight.xaml
    SimpleDark.xaml

  Controls/
    Lightweight.xaml

  Profiles/
    Indigo.xaml
    SimpleLight.xaml
    SimpleDark.xaml
```

`Profiles/Indigo.xaml` は `Colors/Indigo.xaml` と `Controls/Lightweight.xaml` を読み込む。
`Profiles/SimpleLight.xaml` は `Colors/SimpleLight.xaml` と `Controls/Lightweight.xaml` を読み込む。
`Profiles/SimpleDark.xaml` は `Colors/SimpleDark.xaml` と `Controls/Lightweight.xaml` を読み込む。

`SystemAuto` は OS 判定後に `Profiles/SimpleLight.xaml` または `Profiles/SimpleDark.xaml` を読み込む。

## 設定値

`Properties.Settings.Default.ThemeMode` は文字列で維持する。

有効値:

```text
Indigo
SimpleLight
SimpleDark
SystemAuto
```

既存値の移行:

| 旧値 | 新値 |
|---|---|
| `Original` | `Indigo` |
| `OsSync` | `SystemAuto` |
| 空または不明値 | `Indigo` |

既定値は安全側として `Indigo` とする。

## ResourceDictionary 設計

### 色キー

既存 XAML の参照を壊さないため、当面は MaterialDesign 互換キーも自前定義する。

代表キー:

```text
MaterialDesignPaper
MaterialDesignCardBackground
MaterialDesignDivider
MaterialDesignBody
MaterialDesignBodyLight
MaterialDesignBodySecondary
PrimaryHueMidBrush
PrimaryHueMidForegroundBrush
```

アプリ独自キー:

```text
AppWindowBackgroundBrush
AppSurfaceBrush
AppSurfaceAltBrush
AppBorderBrush
AppTextBrush
AppTextSubtleBrush
AppAccentBrush
AppAccentForegroundBrush
MainHeaderBackgroundBrush
MainHeaderForegroundBrush
MainHeaderInputForegroundBrush
NavigationDrawerBackgroundBrush
NavigationDrawerForegroundBrush
UpperTabBackgroundBrush
ThumbImageBackgroundBrush
```

方針:

- `Indigo.xaml` は既存の Indigo 見た目を再現する。
- `SimpleLight.xaml` は白背景、濃色文字、薄い境界線を基本にする。
- `SimpleDark.xaml` は暗色背景、明色文字、暗すぎない境界線を基本にする。
- 状態色や警告色はテーマ色へ寄せすぎず、意味が伝わる固定色または専用キーにする。

### コントロールキー

XAML からは MaterialDesign の style key を直接参照せず、次のようなアプリ独自キーへ寄せる。

```text
AppHeaderButtonStyle
AppIconButtonStyle
AppSearchComboBoxStyle
AppCompactComboBoxStyle
AppTextBoxStyle
AppTreeViewStyle
AppDataGridColumnHeaderStyle
AppDataGridCellStyle
AppDataGridRowStyle
AppBottomTabButtonStyle
```

`Controls/Lightweight.xaml` では WPF 標準 style を `BasedOn` する。

```xaml
<Style x:Key="AppSearchComboBoxStyle"
       BasedOn="{StaticResource {x:Type ComboBox}}"
       TargetType="ComboBox" />
```

## 実装ステップ

### Step 1: テーマ適用入口を固定する

対象:

- `App.xaml`
- `App.xaml.cs`
- `Properties/Settings.settings`

実施内容:

1. `App.xaml` から常時読み込みの MaterialDesign 辞書を最小化する準備を入れる。
2. `App.xaml.cs` に `ApplyTheme(string themeMode)` を整理する。
3. `ThemeMode` の旧値を `Indigo` / `SystemAuto` へ正規化する。
4. `SystemAuto` 選択時は OS のライト/ダーク状態を見て `SimpleLight` / `SimpleDark` を適用する。
5. OS テーマ変更イベントでは `SystemAuto` の時だけ再適用する。

完了条件:

- 起動時に 4 モードを解決できる。
- 不明な設定値でも `Indigo` へ安全に戻る。
- `SystemAuto` は XAML ファイル名ではなくモードとして扱われる。

### Step 2: 色リソースを分割する

対象:

- `Themes/Colors/Indigo.xaml`
- `Themes/Colors/SimpleLight.xaml`
- `Themes/Colors/SimpleDark.xaml`

実施内容:

1. 既存 `Themes/OriginalColors.xaml` の役割を `Indigo.xaml` へ整理する。
2. `SimpleLight.xaml` / `SimpleDark.xaml` に MaterialDesign 互換キーとアプリ独自キーを定義する。
3. 既存参照が多い `MaterialDesignPaper` などは当面残す。
4. 新規・改修箇所は `App*` / `MainHeader*` などのアプリ独自キーを優先して使う。

完了条件:

- MaterialDesign 辞書がなくても主要 Brush key が解決できる。
- `SimpleLight` / `SimpleDark` で文字が背景に埋もれない。

### Step 3: コントロールテーマを分割する

対象:

- `Themes/Controls/Lightweight.xaml`
- `Themes/Generic.xaml`

実施内容:

1. `Themes/Generic.xaml` の共通 style を軽量 key へ寄せる。
2. `Controls/Lightweight.xaml` に既存 MaterialDesign 互換 key とアプリ独自 key を用意する。
3. XAML 側は `AppSearchComboBoxStyle` などのアプリ独自 key だけを見る。
4. `PackIcon` を直接使う箇所は、後続ステップで置換できるよう棚卸しする。

完了条件:

- `Indigo` と `SimpleLight` が同じ XAML で別 style を使える。
- style key の未解決が出ない。

### Step 4: MainWindow hot path を軽量化する

優先対象:

1. 検索欄 `MainHeaderSearchBoxStyle`
2. ソートなどのヘッダー ComboBox `MainHeaderCompactComboBoxStyle`
3. ヘッダーボタン `MainHeaderButtonStyle`
4. 一覧周辺の `DataGrid` / `ListView`
5. 左 Drawer / TreeView

実施内容:

1. 検索欄とヘッダー ComboBox を `AppSearchComboBoxStyle` / `AppCompactComboBoxStyle` へ置換する。
2. `Simple*` では `MaterialDesignOutlinedComboBox` を使わない。
3. `DrawerHost` は `Indigo` では維持し、`Simple*` では標準 `Grid` + 開閉パネルへ置換する候補を分ける。
4. `PackIcon` はまず hot path のアイコンから軽量化する。

完了条件:

- `Indigo` では従来見た目が大きく崩れない。
- `Simple*` では検索入力、タブ切り替え、ページ移動の体感比較ができる。

### Step 5: 設定画面に 4 モードを出す

対象:

- `Views/Settings/CommonSettingsWindow.xaml`
- `Views/Settings/CommonSettingsWindow.xaml.cs`
- 必要なら ViewModel / policy

表示名:

```text
Indigo
Simple Light
Simple Dark
OS 自動
```

実施内容:

1. 設定 UI の選択肢を 4 つにする。
2. 選択変更時に即時 `ApplyTheme(...)` を呼ぶ。
3. 保存値は内部値 `Indigo` / `SimpleLight` / `SimpleDark` / `SystemAuto` に固定する。
4. 設定画面自体が `SimpleDark` で読めることを確認する。

完了条件:

- 再起動後も選択テーマが維持される。
- `SystemAuto` では OS 変更後に `SimpleLight` / `SimpleDark` が切り替わる。

### Step 6: MaterialDesign 依存の棚卸しと段階撤去

棚卸し対象:

- `materialDesign:PackIcon`
- `materialDesign:DrawerHost`
- `materialDesign:ColorZone`
- `materialDesign:PopupBox`
- `materialDesign:HintAssist`
- `materialDesign:TextFieldAssist`
- `MaterialDesignOutlinedComboBox`
- `MaterialDesignOutlinedTextBox`
- `MaterialDesignDataGrid*`
- `MaterialDesignTreeView`

方針:

- `Indigo` では残してよい。
- `Simple*` の hot path では使わない。
- ダイアログや設定画面など低頻度画面は後回しにする。

完了条件:

- `Simple*` の MainWindow 初期表示で MaterialDesign 重テンプレートへの依存が減っている。
- NuGet 参照削除まで実施済み。残る `materialDesign:` 参照はアプリ内 shim で段階吸収する。

## 計測計画

最低限、次のログで比較する。

1. 起動: `ContentRendered -> first-page shown`
2. 検索: `search input -> filter-movies -> replace-filtered`
3. UI 操作: タブ切り替え、ページ移動、Drawer 開閉
4. 画像: `viewport request -> image ready`
5. XAML 初期化: MainWindow 表示直後の UI hang 通知有無

比較ペア:

```text
Indigo vs SimpleLight
Indigo vs SimpleDark
SimpleLight vs SystemAuto(OSライト)
SimpleDark vs SystemAuto(OSダーク)
```

判断基準:

- `Simple*` で起動または検索入力の引っかかりが軽くなるか。
- 見た目の崩れが許容範囲か。
- MaterialDesign 依存を減らした結果、保守性が悪化していないか。

## 受け入れ基準

1. `Indigo` で従来互換の見た目を保てる。
2. `SimpleLight` / `SimpleDark` で主要画面が読める。
3. `SystemAuto` は OS 状態に応じて `SimpleLight` / `SimpleDark` を選ぶ。
4. hot path の XAML が MaterialDesign 固有 key を直接参照しない。
5. MaterialDesign ありなしを設定だけで A/B 比較できる。
6. `debug-runtime.log` で体感差の原因を追える。

## リスクと対策

| リスク | 対策 |
|---|---|
| style key 未解決で起動失敗 | `Indigo` を既定 fallback にし、共通 key を先に揃える |
| ダークテーマで文字が見えない | `SimpleDark` の Brush key を先に固定し、主要画面を手動確認する |
| MaterialDesign 前提の添付プロパティが残る | `Simple*` hot path から順に削る。低頻度画面は後回し |
| 一括置換で見た目が崩れる | MainWindow hot path だけ先行し、設定画面やダイアログは別ステップにする |
| 速度差が出ない | 計測結果をもとに撤退し、テーマ分割だけを保守性改善として残す |

## 今回やらないこと

- 全画面の一括 Simple 化
- テーマ刷新と UI デザイン刷新の同時実施
- `*.wb` 変更
- Redis / FASTER など外部 cache 導入

## 着手順の推奨

1. `ThemeMode` の正規化と 4 モード定義
2. `Colors` / `Controls` / `Profiles` の器を作る
3. `Indigo` を既存互換として通す
4. `SimpleLight` を MainWindow hot path だけで通す
5. `SimpleDark` を同じ範囲で通す
6. `SystemAuto` を `SimpleLight` / `SimpleDark` の切替モードとして通す
7. ログ比較して、MaterialDesign が本当に体感に効いているか判断する

## 実装メモ 2026-05-04

- `App.xaml` の `StartupUri` を外し、`App.OnStartup` でテーマ適用後に `MainWindow` を明示生成して `Show()` する。
- `App.xaml` を空辞書にした構成では、MainWindow の XAML 生成前に Profile 辞書を積む順序を固定する必要がある。
- `Indigo` Profile も MaterialDesign 本体を読まない。色は `Themes\Colors\Indigo.xaml`、control は `Themes\Controls\Lightweight.xaml` へ寄せる。
- Indigo から `SystemAuto` へ切り替える時は、新 Profile を先に積んでから旧 Profile を外す。切替途中に DynamicResource の参照先が空になる瞬間を作らない。
- `PaletteHelper` / `BundledTheme` / `MaterialDesignThemes.Wpf;component` の外部辞書読み込みは削除する。
- 既存 XAML の段階移行用に、`Compatibility\MaterialDesignCompatibility.cs` へ `PackIcon` / `DrawerHost` / `ColorZone` / attached property の軽量 shim を置く。
- この shim は外部 DLL ではなくアプリ内実装であり、NuGet の `MaterialDesignColors` / `MaterialDesignThemes` は削除する。
- 起動確認は Release x64 の実行プロセスで `MainWindowHandle` と可視トップレベルウィンドウを確認する。

## 関連資料

- `Themes\Docs\テーマ切替_実装プラン_2026-03-15.md`
- `Themes\Docs\アプリ全体ダークモード対応プラン 2026-03-15.md`
- `Themes\OriginalColors.xaml`
- `Themes\Generic.xaml`
- `App.xaml`
- `App.xaml.cs`
