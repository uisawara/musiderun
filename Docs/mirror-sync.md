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
- 同期前後で作業ツリーの状態（追跡＋未追跡ファイルの内容）を比較し、変化があれば Job を失敗扱いにします。比較には実インデックスに触れない一時インデックス上で `read-tree HEAD` → `add -A` → `write-tree` を行い、内容アドレスの決定的なツリーハッシュを指紋として用います（`git stash create` には依存しません）

## 作業ブランチの自動回復

Job 実行前に、メイン worktree が誤って mirror ブランチ（`musiderun/mirror-*`）を checkout したままになっていないか検証します。

- mirror ブランチ上だった場合、保存済みの作業ブランチ → `main` / `master` / `develop` → ローカルブランチ一覧の順で復帰先を探し、`git checkout` で戻します
- 復帰先となる通常ブランチが 1 つも見つからない（mirror ブランチしか存在しない）場合は、**現在の状態から作業ブランチを新規作成して復帰**します
  - 作成名は、保存済みの作業ブランチ → `MusiderunSettings.json` の `defaultWorkingBranch`（既定 `main`）の順で決定します
  - `git checkout -b <名前>` は現在の HEAD から作成し作業ツリー・index を変更しないため、同期前後の状態比較にも影響しません
  - 作成名が既存・または作成に失敗した場合のみ、手動で通常ブランチへ戻すよう促すエラーになります

## 同期対象外

`Library/`, `Temp/` 等は `.gitignore` により同期対象外です。ミラー側で独自に生成されます。

## .gitignore の自動チェック

musiderun はメインリポジトリ直下の `BatchJobLogs/`（および設定した `logOutputDirectory`）へログ/レポートを書き込みます。これらが git 追跡対象のままだと、同期前後の作業ツリー状態比較で差分が検出され Job が中断されます。

- Job 実行開始時に、必要なエントリが対象リポジトリの `.gitignore` に登録されているか自動チェックし、不足していれば `# === musiderun (auto-managed) ===` セクションへ追記します（`.gitignore` が無ければ作成）
- 手動で実行・確認したい場合は `Tools/musiderun/Check .gitignore Entries` メニュー、または musiderun ウィンドウの `Check .gitignore` ボタンを使用します
- ビルド成果物などはミラー worktree 側に生成されるため、本チェックの対象外です

## プラットフォーム固有の扱い

Windows では CRLF 警告を避けるため、スナップショット用 git 操作に `core.autocrlf=false` / `core.safecrlf=false` を適用します。

## 関連ドキュメント

- worktree の配置と掃除: [worktree.md](worktree.md)
