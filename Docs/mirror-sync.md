# 同期の仕組み

[← ドキュメント一覧](index.md)

musiderun は git worktree を使った **一方通行ミラー同期** を行います。Job ごとに独立した worktree を使い、複数 Job を順次実行します。

## 処理の流れ

1. リポジトリルート（`Assets` の親）を基準に git worktree を使用
2. Job ごとに `musiderun/mirror-{jobId}` ブランチへスナップショットコミットを作成し、ミラー worktree を `reset --hard`
3. ミラー worktree 上で別 Unity プロセスをバッチ起動

## メインプロジェクトの保護

- **メインプロジェクトの作業ツリーは変更しません**
- mirror ブランチの作成・更新は `commit-tree` / `git branch`（ref 作成のみ）で行い、メインで `checkout` や `reset --hard` は実行しません
- 同期前後で `git stash create` により作業ツリー状態を比較し、変化があれば Job を失敗扱いにします

## 同期対象外

`Library/`, `Temp/` 等は `.gitignore` により同期対象外です。ミラー側で独自に生成されます。

## プラットフォーム固有の扱い

Windows では CRLF 警告を避けるため、スナップショット用 git 操作に `core.autocrlf=false` / `core.safecrlf=false` を適用します。

## 関連ドキュメント

- worktree の配置と掃除: [worktree.md](worktree.md)
