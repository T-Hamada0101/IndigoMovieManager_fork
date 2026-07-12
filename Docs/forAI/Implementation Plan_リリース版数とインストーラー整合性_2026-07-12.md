# Implementation Plan リリース版数とインストーラー整合性 2026-07-12

最終更新日: 2026-07-12

変更概要:
- 正式タグ、project、配布EXE、ZIP名、Setup名の版数を同一契約で検証する
- MSIの制約により、公開版は4桁目だけを上げず、先頭3桁を進める
- 手動previewの `manual-*` ラベルは正式版数として扱わず、配布EXEの版数をインストーラー実体へ使う

## 1. 目的

version up 時に、GitHub Release のタグと成果物名だけが新しく、配布EXEやインストーラー実体が旧版のままになる事故を防ぐ。

## 2. 正本

アプリ版数の正本は `IndigoMovieManager.csproj` の次の3項目とする。

- `Version`
- `FileVersion`
- `AssemblyVersion`

正式リリースでは、次をすべて同じ4要素版数へ揃える。

- Git tag: `v1.0.5.0`
- project三種: `1.0.5.0`
- 配布EXE `FileVersion`: `1.0.5.0`
- ZIP名: `IndigoMovieManager-v1.0.5.0-win-x64.zip`
- Setup名: `IndigoMovieManager-Setup-v1.0.5.0-win-x64.exe`
- Burn Bundle version: `1.0.5.0`
- MSI ProductVersion: `1.0.5`

## 3. MSI版数制約

MSIのupgrade判定は先頭3桁を使う。このため、現在の `1.0.4.0` から `1.0.4.1` のように4桁目だけを上げて公開してはいけない。

次の公開版候補は、少なくとも `1.0.5.0` のように先頭3桁を進める。

## 4. 実装

`scripts/assert_release_version_consistency.ps1` を共通の検証入口とする。

- tag workflow開始時に、正式タグとproject三種を照合する
- ZIP作成時に、成果物ラベル、project三種、配布EXEを照合する
- Installer作成時に、Setup名へ使うラベルと実際に包む配布EXEを照合する
- 不整合時はPrivate Engine同期やWiXビルドを進めず、早期に失敗させる

過去版PackageDirからInstallerを再生成する経路では、checkout中のproject版数とは比較しない。包む配布EXEを正とし、明示した成果物ラベルだけを照合する。

## 5. リリース前チェック

1. `IndigoMovieManager.csproj` の三種版数を同じ値へ上げる
2. `scripts/assert_release_version_consistency.ps1` で正式タグ候補との一致を確認する
3. Release x64のテストとビルドを成功させる
4. GitHub Actionsの入力なしpreviewを成功させる
5. ZIP内EXEの `FileVersion` とSetup名を確認する
6. 旧公開版の上からInstallerを実行し、レジストリの `DisplayVersion` と実体EXE版数を確認する
7. preview成功後にのみ正式タグをpushする

## 6. 完了条件

- 一致する版数は全経路を通る
- 正式タグとprojectまたは配布EXEが違う場合は成果物公開前に停止する
- `manual-*` previewは従来どおり作成できる
- 旧公開版から新公開版への上書きインストールを実機で確認する

## 7. 2026-07-12 実機テスト

`IndigoMovieManager-Setup-v1.0.3.5-win-x64.exe` を使い、アンインストール、インストール、アンインストールの順で確認した。

確認できたこと:

- 1回目と2回目のBundleアンインストールは完了した
- インストール後のBundle表示版数は `1.0.3.5`
- インストール後の本体EXE `FileVersion` は `1.0.3.5`
- アンインストール後はBundleとMSIの製品登録が両方削除された
- `%LOCALAPPDATA%\IndigoMovieManager` のユーザーデータは保持された
- インストール先に残った `layout.xml` があっても再インストールできた

公開前に直すこと:

- 旧検証Installerでは、Bundle `1.0.3.5` とMSI `1.0.3` の登録を表示属性まで区別できていなかった
- 旧検証InstallerはStart Menu用Component追加前の生成物で、ショートカットが生成されなかった
- アンインストール後も `%LOCALAPPDATA%\Programs\IndigoMovieManager\layout.xml` と空でない親フォルダが残った

判定:

- インストールとアンインストールの本体経路は成立している
- 版数実体は一致している
- 下記の修正版Installer実機確認が閉じるまで次版Installerの完了扱いにはしない

## 8. 2026-07-12 修正内容

- MSIへ `ARPSYSTEMCOMPONENT=1` を固定し、BundleだけをWindowsのアプリ一覧へ表示する
- Bundle側の `MsiPackage Visible="no"` も維持し、Bundle経路とMSI単体経路の両方で表示契約を守る
- Start MenuショートカットをMainFeatureへ含め、非advertised shortcutとして生成する
- dock layoutの保存先を `%LOCALAPPDATA%\IndigoMovieManager\Layouts` へ移す
- 旧インストール先の `layout.xml` / `layout.default.xml` は初回起動時に新保存先へ移行する
- MSI uninstallでは旧 `layout*.xml` と空になったインストール先フォルダを削除する
- WiX標準 `hyperlinkLicense` を土台にカスタムテーマを適用し、主要ボタンを幅90〜120、高さ27へ広げる
- uninstall進行中は `アンインストールしています` / `削除中:` を表示する

## 9. 2026-07-12 修正版Installer実機テスト

対象:

- `IndigoMovieManager-Setup-manual-installer-fix-win-x64.exe`
- Bundle version `1.0.4.0`
- 本体EXE `FileVersion=1.0.4.0`

インストール後:

- Bundle登録 `1.0.4.0` を確認した
- MSI登録 `1.0.4` は `SystemComponent=1` で非表示契約になった
- Start Menuフォルダと `IndigoMovieManager.lnk` の生成を確認した
- MSI実体のShortcut / RemoveFile / FeatureComponents / ARPSYSTEMCOMPONENTテーブルを確認した

アンインストールUI:

- DPI拡大環境でボタン幅を広げた修正版をユーザー確認した
- uninstall進行中の専用文言をユーザー確認した
- 初回 `Theme="none"` 指定は起動時 `0x80070490` となったため撤回し、`Theme="hyperlinkLicense" + ThemeFile` へ修正して再生成した

アンインストール後:

- Bundle / MSI登録は0件
- Start Menuショートカットは0件
- `%LOCALAPPDATA%\Programs\IndigoMovieManager` はフォルダごと削除された
- 旧 `layout.xml` は残らなかった
- `%LOCALAPPDATA%\IndigoMovieManager` のユーザーデータは保持された
- Bundle logの終了コードは `0x0`

判定:

- 指摘された5項目は修正版Installerの実機確認まで完了した
