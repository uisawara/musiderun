# git worktree とミラー配置

[← ドキュメント一覧](index.md)

## 概要

musiderun は Job 実行時に **ミラー用 git worktree** を自動作成します。安全のため、ミラー worktree は **リポジトリ（プロジェクト）配下には作成できません**。既定ではプロジェクトルートの**親ディレクトリ**に置かれます。

```
parent/
├── YourProject/                      ← git clone / 作業コピー
└── YourProject-musiderun-{jobId}/    ← Job ごとのミラー worktree
```

## 既定の挙動

| 項目 | 既定の挙動 |
| ---- | ---------- |
| worktree の場所 | `../{プロジェクト名}-musiderun-{jobId}`（`Assets` の親ディレクトリの外） |
| 変更方法 | `MusiderunSettings.json` の `mirrorWorktreeBasePath` を指定（配下に `{jobId}` が付く） |
| ログ出力 | ミラー worktree 内の `BatchJobLogs/`（または `logOutputDirectory`） |
| ビルド成果物 | ミラー worktree 内の `artifactFolder` |
| git ブランチ | なし（**detached HEAD** の worktree。メインの HEAD コミットに同期） |

## 手動整理

- **clone 先だけ削除しても、外側の worktree は残ります**
- 不要時は `git worktree list` で確認し、`git worktree remove` やディレクトリ削除で整理してください
- ミラー worktree は musiderun が HEAD に同期するための作業用です。**手動で編集しないでください**

## 実行時の制約

- **Play モード中は Job を実行できません**（未保存の変更をディスクに書き出せないため）
- ミラー対象は **コミット済み（HEAD）の内容のみ**です。未コミットの変更はビルド/テストされないため、対象にしたい場合は実行前に commit してください
- メインプロジェクトの作業ツリー・index・ブランチは Job 実行で変更されません（ミラー worktree のみ更新）

## 関連ドキュメント

- 同期の内部動作: [mirror-sync.md](mirror-sync.md)
- `mirrorWorktreeBasePath` の設定: [configuration.md](configuration.md)
