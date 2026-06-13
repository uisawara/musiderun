# musiderun (概念検証中：当面は不安定版)

Unity Editorで開発を続けたまま、バックグラウンドでプロジェクト複製を使ったテスト実行、ビルド実行ができるようにしたいパッケージです。
git worktree によるミラー同期と、別 Unity プロセスでのバッチビルド・テスト実行を行います。

※git cloneした階層と同階層に、git worktreeによる複製フォルダが数件作られるので、置き場所にはご注意ください。

このリポジトリは **デモ用 Unity プロジェクト** と **UPM パッケージ本体**（`Assets/UnityPackages/works.mmzk.util.musiderun`）を同梱しています。

## 要件

- Unity **2022.3 LTS** 以降（Unity 6 含む）
- git（PATH に通っていること）

> デモプロジェクトは Unity 6000.3.13f1 で作成していますが、パッケージ本体（`works.mmzk.util.musiderun`）は Unity 2022.3 以降のプロジェクトに導入できます。

## 導入方法

### A. このリポジトリを試す（デモプロジェクト）

パッケージは `Assets/UnityPackages/` に同梱済みのため、`Packages/manifest.json` への追記は不要です。

```bash
git clone https://github.com/uisawara/musiderun.git
```

1. Unity Hub でクローンしたプロジェクトを開く
2. `Tools/works.mmzk.util.musiderun/Create Settings JSON` で設定ファイルを作成（未作成の場合）
3. `Tools/works.mmzk.util.musiderun/Open Window` でウィンドウを開く

### B. 既存プロジェクトへ UPM（Git URL）で導入

リポジトリを Public にしたうえで、導入先の `Packages/manifest.json` の `dependencies` に追加します。

```json
{
  "dependencies": {
    "works.mmzk.util.musiderun": "https://github.com/uisawara/musiderun.git?path=Assets/UnityPackages/works.mmzk.util.musiderun"
  }
}
```

バージョンを Git タグで固定する場合（`package.json` の `version` とタグ名を揃える）:

```json
"works.mmzk.util.musiderun": "https://github.com/uisawara/musiderun.git?path=Assets/UnityPackages/works.mmzk.util.musiderun#v1.0.0"
```

導入後:

1. `Tools/works.mmzk.util.musiderun/Create Settings JSON` で `Assets/Settings/MusiderunSettings.json` を作成
2. `Tools/works.mmzk.util.musiderun/Open Window` でウィンドウを開く

> Package Manager の検索には表示されません。`manifest.json` への追記が必要です。

## 注意: git worktree は clone 先の外に作られます

Job 実行時にミラー用 git worktree がプロジェクトの**外**（既定: `../{プロジェクト名}-musiderun-{jobId}`）に自動作成されます。**clone 先を削除しても worktree は残る**ため、不要時は手動で整理してください。

詳細は [Docs/worktree.md](Docs/worktree.md) を参照してください。

## ドキュメント

- [Docs 一覧](Docs/index.md) — 設定・同期の仕組み・ログレポートなど技術詳細
- [パッケージ README](Assets/UnityPackages/works.mmzk.util.musiderun/README.md) — 基本操作

## ライセンス

[MIT License](LICENSE)
