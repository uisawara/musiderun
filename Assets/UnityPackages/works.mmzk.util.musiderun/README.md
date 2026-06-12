# works.mmzk.util.musiderun

git worktree を使った一方通行ミラー同期と、別 Unity プロセスでのバッチ実行ツールです。
Job ごとに独立した worktree を使い、複数 Job を順次実行します。

## 導入

- **このリポジトリ（デモ）**: `Assets/UnityPackages/` に同梱済み。clone して Unity で開くだけ
- **UPM（Git URL）**: `Packages/manifest.json` の `dependencies` に Git URL を追加
- **ローカル同梱**: フォルダを `Assets/UnityPackages/` または `Packages/` に配置

詳細は [導入方法](https://github.com/uisawara/musiderun/blob/main/Docs/installation.md) を参照してください。

## 初回セットアップ

1. `Tools/works.mmzk.util.musiderun/Create Settings JSON` で `Assets/Settings/MusiderunSettings.json` を作成（未作成の場合）
2. `Tools/works.mmzk.util.musiderun/Open Window` でウィンドウを開く

## 使い方

1. JSON で Job を定義する（[設定の詳細](https://github.com/uisawara/musiderun/blob/main/Docs/configuration.md)）
2. ウィンドウで Job ごとにチェックボックスを ON/OFF
3. **Run Selected** — チェック ON かつ OS 一致 Job を順次実行
4. **▶** — 個別 Job を実行

## ログ

各 Job 完了時に HTML レポートが自動生成されます。ウィンドウの **Open Log** でブラウザ表示できます。
形式やラベル絞り込みの詳細は [ログレポート](https://github.com/uisawara/musiderun/blob/main/Docs/log-reports.md) を参照してください。

## 注意

Job 実行時にミラー用 git worktree が **プロジェクトの外**（既定: `../{プロジェクト名}-musiderun-{jobId}`）に自動作成されます。clone 先を削除しても worktree は残るため、不要時は手動で整理してください。Play モード中は Job を実行できません。

詳細は [worktree の注意](https://github.com/uisawara/musiderun/blob/main/Docs/worktree.md) を参照してください。

## 要件

- Unity **2022.3 LTS** 以降（Unity 6 含む）
- git（PATH に通っていること）

## 対応 OS

- macOS
- Windows

## ドキュメント

- [Docs 一覧](https://github.com/uisawara/musiderun/blob/main/Docs/index.md)
- [導入方法](https://github.com/uisawara/musiderun/blob/main/Docs/installation.md)
- [設定](https://github.com/uisawara/musiderun/blob/main/Docs/configuration.md)
- [worktree の注意](https://github.com/uisawara/musiderun/blob/main/Docs/worktree.md)
- [同期の仕組み](https://github.com/uisawara/musiderun/blob/main/Docs/mirror-sync.md)
- [ログレポート](https://github.com/uisawara/musiderun/blob/main/Docs/log-reports.md)
