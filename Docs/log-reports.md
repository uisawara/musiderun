# ジョブ別ログ（HTML レポート）

[← ドキュメント一覧](index.md)

各 Job 実行ごとに、ミラー worktree 配下の `BatchJobLogs/`（または `logOutputDirectory`）へログが出力されます。

## 出力ファイル

| ファイル | 内容 |
| -------- | ---- |
| `{jobId}-{timestamp}-mirror.log` | 同期・git・起動コマンド等（musiderun 側） |
| `{jobId}-{timestamp}.log` | Unity バッチ出力 |
| `{jobId}-{timestamp}.html` | 上記2つを統合した閲覧用レポート |

ジョブ完了時に HTML が自動生成されます。ウィンドウの各 Job 行にある **Open Log** でブラウザ表示できます。

## HTML レポートの構成

- ヘッダー（Job 名、状態、exit code、所要時間）
- **Labels** タグクラウド（種類 / セクション / ソース）
- 時系列ログ一覧（各行にラベル chip 付き）

## ラベル絞り込み

HTML 上部のタグクラウドでログ行を絞り込めます。

| 種別 | 例 |
| ---- | -- |
| 種類 (Severity) | Error, Warning, Info |
| セクション (Section) | musiderun, Licensing, Script Compilation, Test Run など |
| ソース (Source) | Mirror, Unity |

### 操作

- ラベルをクリックするたび、そのラベルの ON/OFF をトグル
- 複数ラベル ON 時は **AND 条件**（選択中のラベルをすべて持つ行のみ表示）
- 選択なし = 全行表示
- **Clear filters** で選択をリセット

## 関連ドキュメント

- ログ出力先の設定: [configuration.md](configuration.md)
