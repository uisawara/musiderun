# 導入方法

[← ドキュメント一覧](index.md)

## このリポジトリ（デモプロジェクト）で使う

パッケージは `Assets/UnityPackages/works.mmzk.util.musiderun` に同梱されています。`Packages/manifest.json` への追記は不要です。

1. リポジトリを clone し、Unity でプロジェクトを開く
2. `Tools/musiderun/Create Settings JSON` で既定 JSON を作成（未作成の場合）
3. `Tools/musiderun/Open Window` でウィンドウを開く

## 他プロジェクトへ UPM（Git URL）で導入

`Packages/manifest.json` の `dependencies` に追加します（`?path=` が必須です）。

```json
{
  "dependencies": {
    "com.mmzkworks.util.musiderun": "https://github.com/uisawara/musiderun.git?path=Assets/UnityPackages/works.mmzk.util.musiderun"
  }
}
```

> Package Manager の検索には表示されません。`manifest.json` への追記が必要です。

### タグでバージョン固定

`package.json` の `version` とタグ名を揃えます。

```json
"com.mmzkworks.util.musiderun": "https://github.com/uisawara/musiderun.git?path=Assets/UnityPackages/works.mmzk.util.musiderun#v0.0.2"
```

導入後は [configuration.md](configuration.md) の手順に従い、初回セットアップを行ってください。

## 他プロジェクトへローカル同梱で導入

`works.mmzk.util.musiderun` フォルダを次のいずれかに配置します。

| 配置先 | manifest への追記 |
| ------ | ----------------- |
| `Assets/UnityPackages/works.mmzk.util.musiderun/` | 不要 |
| `Packages/works.mmzk.util.musiderun/` | `"com.mmzkworks.util.musiderun": "file:works.mmzk.util.musiderun"` |
| プロジェクト外など | `"com.mmzkworks.util.musiderun": "file:../../path/to/works.mmzk.util.musiderun"`（`Packages/` からの相対パス） |

## 初回セットアップ

1. `Tools/musiderun/Create Settings JSON` で `Assets/Settings/MusiderunSettings.json` を作成（未作成の場合）
2. `Tools/musiderun/Open Window` でウィンドウを開く

## 要件

- Unity **2022.3 LTS** 以降（Unity 6 含む）
- git が PATH にあること
- Windows: [Git for Windows](https://gitforwindows.org/) 推奨
- macOS: Xcode Command Line Tools または Homebrew git

## 対応 OS

- macOS
- Windows
