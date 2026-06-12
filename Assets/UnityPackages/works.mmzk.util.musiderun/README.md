# works.mmzk.util.musiderun

git worktree を使った一方通行ミラー同期と、別 Unity プロセスでのバッチ実行ツールです。
Job ごとに独立した worktree を使い、複数 Job を順次実行します。

## 導入

### このリポジトリ（デモプロジェクト）で使う

パッケージは `Assets/UnityPackages/works.mmzk.util.musiderun` に同梱されています。`Packages/manifest.json` への追記は不要です。

1. リポジトリを clone し、Unity でプロジェクトを開く
2. `Tools/works.mmzk.util.musiderun/Create Settings JSON` で既定 JSON を作成（未作成の場合）
3. `Tools/works.mmzk.util.musiderun/Open Window` でウィンドウを開く

### 他プロジェクトへ UPM（Git URL）で導入

`Packages/manifest.json` の `dependencies` に追加します（`?path=` が必須です）。

```json
{
  "dependencies": {
    "works.mmzk.util.musiderun": "https://github.com/uisawara/musiderun.git?path=Assets/UnityPackages/works.mmzk.util.musiderun"
  }
}
```

タグでバージョン固定する場合:

```json
"works.mmzk.util.musiderun": "https://github.com/uisawara/musiderun.git?path=Assets/UnityPackages/works.mmzk.util.musiderun#v1.0.0"
```

導入後、下記「初回セットアップ」を実行してください。

### 他プロジェクトへローカル同梱で導入

`works.mmzk.util.musiderun` フォルダを次のいずれかに配置します。

| 配置先 | manifest への追記 |
| ------ | ----------------- |
| `Assets/UnityPackages/works.mmzk.util.musiderun/` | 不要 |
| `Packages/works.mmzk.util.musiderun/` | `"works.mmzk.util.musiderun": "file:works.mmzk.util.musiderun"` |
| プロジェクト外など | `"works.mmzk.util.musiderun": "file:../../path/to/works.mmzk.util.musiderun"`（`Packages/` からの相対パス） |

### 初回セットアップ

1. `Tools/works.mmzk.util.musiderun/Create Settings JSON` で `Assets/Settings/MusiderunSettings.json` を作成（未作成の場合）
2. `Tools/works.mmzk.util.musiderun/Open Window` でウィンドウを開く

## 設定（JSON）

Job 定義の正は [`Assets/Settings/MusiderunSettings.json`](../../Settings/MusiderunSettings.json) です（固定パス）。

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
      "batchArguments": "-executeMethod Works.Mmzk.Util.Musiderun.Editor.BatchBuildEntry.Execute"
    }
  ]
}
```

- `targetOS`: `Any` | `Windows` | `macOS` | `Linux`
- `batchArguments`: Unity 起動時の追加コマンドライン引数

## 使い方

1. JSON で Job を定義する
2. ウィンドウで Job ごとにチェックボックスを ON/OFF
3. **Run Selected** — チェック ON かつ OS 一致 Job を順次実行
4. **▶** — 個別 Job を実行

実行時に Job ごとの worktree が自動作成されます。既定では **clone / プロジェクトルートの外** に置かれます（`../{プロジェクト名}-musiderun-{jobId}`）。詳細は [注意事項](#注意事項) を参照してください。

## ジョブ別ログ（HTML）

各 Job 実行ごとに、ミラー worktree 配下の `BatchJobLogs/`（または `logOutputDirectory`）へ次のファイルが出力されます。

| ファイル | 内容 |
| -------- | ---- |
| `{jobId}-{timestamp}-mirror.log` | 同期・git・起動コマンド等（musiderun 側） |
| `{jobId}-{timestamp}.log` | Unity バッチ出力 |
| `{jobId}-{timestamp}.html` | 上記2つを統合した閲覧用レポート |

ジョブ完了時に HTML が自動生成されます。ウィンドウの各 Job 行にある **Open Log** でブラウザ表示できます。

HTML レポートの構成:

- ヘッダー（Job 名、状態、exit code、所要時間）
- **Labels** タグクラウド（種類 / セクション / ソース）
- 時系列ログ一覧（各行にラベル chip 付き）

### ラベル絞り込み

HTML 上部のタグクラウドでログ行を絞り込めます。

| 種別 | 例 |
| ---- | -- |
| 種類 (Severity) | Error, Warning, Info |
| セクション (Section) | musiderun, Licensing, Script Compilation, Test Run など |
| ソース (Source) | Mirror, Unity |

- ラベルをクリックするたび、そのラベルの ON/OFF をトグル
- 複数ラベル ON 時は **AND 条件**（選択中のラベルをすべて持つ行のみ表示）
- 選択なし = 全行表示
- **Clear filters** で選択をリセット

## 同期の仕組み

- リポジトリルート（`Assets` の親）を基準に git worktree を使用
- Job ごとに `musiderun/mirror-{jobId}` ブランチへスナップショットコミットを作成し、ミラー worktree を `reset --hard`
- `Library/`, `Temp/` 等は `.gitignore` により同期対象外（ミラー側で独自生成）
- **メインプロジェクトの作業ツリーは変更しません**。mirror ブランチの作成・更新は `commit-tree` / `git branch`（ref 作成のみ）で行い、メインで `checkout` や `reset --hard` は実行しません
- 同期前後で `git stash create` により作業ツリー状態を比較し、変化があれば Job を失敗扱いにします
- Windows では CRLF 警告を避けるため、スナップショット用 git 操作に `core.autocrlf=false` / `core.safecrlf=false` を適用します

## 要件

- Unity **2022.3 LTS** 以降（Unity 6 含む）
- git が PATH にあること
- Windows: [Git for Windows](https://gitforwindows.org/) 推奨
- macOS: Xcode Command Line Tools または Homebrew git

## 注意事項

### git worktree は clone 先の外に作られる

安全のため、ミラー worktree は **リポジトリ（プロジェクト）配下には作成できません**。既定ではプロジェクトルートの**親ディレクトリ**に次のようなパスで作られます。

```
parent/
├── YourProject/                      ← git clone / 作業コピー
└── YourProject-musiderun-{jobId}/    ← Job ごとのミラー worktree
```

| 項目 | 既定の挙動 |
| ---- | ---------- |
| worktree の場所 | `../{プロジェクト名}-musiderun-{jobId}` |
| 変更方法 | `MusiderunSettings.json` の `mirrorWorktreeBasePath`（配下に `{jobId}` が付く） |
| ログ出力 | ミラー worktree 内の `BatchJobLogs/`（または `logOutputDirectory`） |
| git ブランチ | `musiderun/mirror-{jobId}`（ローカル。通常は push 不要） |

- **clone 先だけ削除しても、外側の worktree や `musiderun/mirror-*` ブランチは残ります**。不要時は `git worktree list` で確認し、`git worktree remove` 等で整理してください
- ミラー worktree は musiderun が同期したスナップショット用です。**手動で編集しないでください**

### その他

- **Play モード中は Job を実行できません**（未保存の変更をディスクに書き出せないため）
- メインプロジェクトの未コミット変更は Job 実行で消えません

## 対応 OS

- macOS
- Windows
