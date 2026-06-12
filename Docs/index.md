# musiderun ドキュメント

musiderun の技術的な詳細ドキュメントです。導入と基本操作は README を先に読んでください。

## README

| 対象 | 内容 |
| ---- | ---- |
| [リポジトリ README](../README.md) | デモプロジェクトの概要・導入・注意喚起 |
| [パッケージ README](../Assets/UnityPackages/works.mmzk.util.musiderun/README.md) | パッケージの基本操作・ウィンドウの使い方 |

## 技術ドキュメント

| ドキュメント | 内容 |
| ------------ | ---- |
| [installation.md](installation.md) | 導入方法の詳細（UPM・ローカル同梱・タグ固定） |
| [configuration.md](configuration.md) | `MusiderunSettings.json` のスキーマと各フィールド |
| [worktree.md](worktree.md) | ミラー worktree の場所・掃除・制約 |
| [mirror-sync.md](mirror-sync.md) | 一方通行ミラー同期の仕組み |
| [log-reports.md](log-reports.md) | ジョブログ・HTML レポートの形式と絞り込み |

## 読み方の目安

1. README で導入と初回セットアップを済ませる
2. [configuration.md](configuration.md) で Job を定義する
3. 挙動やトラブル時は [worktree.md](worktree.md) と [mirror-sync.md](mirror-sync.md) を参照する
4. ログの見方は [log-reports.md](log-reports.md) を参照する
