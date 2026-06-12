# 設定（MusiderunSettings.json）

[← ドキュメント一覧](index.md)

Job 定義の正は `Assets/Settings/MusiderunSettings.json` です（固定パス）。

## サンプル

```json
{
  "version": 1,
  "unityExecutablePath": "",
  "logOutputDirectory": "",
  "mirrorWorktreeBasePath": "",
  "mirrorBranchPrefix": "musiderun/mirror",
  "jobs": [
    {
      "id": "build",
      "displayName": "Build",
      "targetOS": "macOS",
      "batchArguments": "-executeMethod Works.Mmzk.Util.Musiderun.Editor.BatchBuildEntry.Execute",
      "artifactFolder": "Builds"
    },
    {
      "id": "tests-editmode",
      "displayName": "Run Tests - EditMode",
      "targetOS": "Any",
      "batchArguments": "-runTests -testPlatform editmode",
      "artifactFolder": "TestResults"
    }
  ]
}
```

`Tools/works.mmzk.util.musiderun/Create Settings JSON` で上記に近い既定 JSON を生成できます。

## ルートレベルのフィールド

| フィールド | 型 | 既定値 | 説明 |
| ---------- | -- | ------ | ---- |
| `version` | int | `1` | 設定ファイルのスキーマバージョン |
| `unityExecutablePath` | string | `""` | バッチ実行に使う Unity 実行ファイルのパス。空の場合は現在の Editor と同じバイナリを使用 |
| `logOutputDirectory` | string | `""` | ログ出力先。空の場合はミラー worktree 内の `BatchJobLogs/` |
| `mirrorWorktreeBasePath` | string | `""` | ミラー worktree の親ディレクトリ。空の場合は `../{プロジェクト名}-musiderun-{jobId}`。**リポジトリ配下には指定できません**（詳細は [worktree.md](worktree.md)） |
| `mirrorBranchPrefix` | string | `"musiderun/mirror"` | ミラー用ブランチのプレフィックス。実際のブランチ名は `{prefix}-{jobId}` |
| `jobs` | array | `[]` | Job 定義の配列 |

## Job 定義（`jobs[]`）

| フィールド | 型 | 既定値 | 説明 |
| ---------- | -- | ------ | ---- |
| `id` | string | `""` | Job の識別子。worktree パス・ブランチ名・ログファイル名に使用 |
| `displayName` | string | `""` | ウィンドウに表示する名前 |
| `targetOS` | string | `"Any"` | 実行対象 OS。`Any` \| `Windows` \| `macOS` \| `Linux`。現在の OS と一致しない Job はウィンドウで実行対象外 |
| `batchArguments` | string | `""` | Unity バッチ起動時の追加コマンドライン引数（`-executeMethod`、`-runTests` など） |
| `artifactFolder` | string | `""` | ビルド成果物・テスト結果などの出力先（ミラー worktree 内の相対パス） |

## 関連ドキュメント

- worktree の場所と制約: [worktree.md](worktree.md)
- ログ出力先の詳細: [log-reports.md](log-reports.md)
