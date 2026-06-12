# musiderun

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

musiderun は Job 実行時に **ミラー用 git worktree** を自動作成します。既定では clone したリポジトリの**外**（プロジェクトルートの兄弟ディレクトリ）に置かれます。

```
parent/
├── musiderun/                    ← git clone した作業コピー
└── musiderun-musiderun-build/    ← Job 用 worktree（例: jobId = build）
```

- 既定パス: `../{プロジェクト名}-musiderun-{jobId}`（`Assets` の親ディレクトリの外）
- 変更する場合: `Assets/Settings/MusiderunSettings.json` の `mirrorWorktreeBasePath` を指定（**リポジトリ配下には置けません**）
- ログ・ビルド成果物も、原則としてミラー worktree 側（`BatchJobLogs/` や `artifactFolder`）に出力されます
- `musiderun/mirror-{jobId}` ブランチはローカル git に作成されます（通常は push 不要）
- **clone 先フォルダを削除しても、外側の worktree は自動では消えません**。不要になったら `git worktree remove` やディレクトリ削除で手動整理してください
- Job 実行はメインプロジェクトの未コミット変更を消しません（ミラー worktree / mirror ブランチのみ更新）。Play モード中は Job を実行できません

## ドキュメント

使い方・設定・ログレポートの詳細はパッケージ README を参照してください。

- [Assets/UnityPackages/works.mmzk.util.musiderun/README.md](Assets/UnityPackages/works.mmzk.util.musiderun/README.md)

## ライセンス

[MIT License](LICENSE)
