# ChillPatcher

これは、以下の機能拡張を提供する BepInEx プラグインです。

  - ゲームへの完全な FLAC 形式のサポート
  - 楽曲の一括インポートとプレイリスト管理機能
  - 『Chill With You』を Wallpaper Engine 環境で正しく動作させる機能
  - ゲーム内入力メソッド（IME）の追加

## 🐛 問題が発生しましたか？

**調査のためにログファイルを提供してください！**

ログファイルの場所：

```
# プラグインログ
<ゲームディレクトリ>\BepInEx\LogOutput.log
# unity ログ
C:\Users\<ユーザー名>\AppData\LocalLow\Nestopi\Chill With You\Player.log
```

Issue を報告する際は、必ずログファイルを添付してください。ログがない場合、問題の特定ができません。

-----

## ✨ 主な機能

### コア機能

  - **🎮 Wallpaper Engine モード**：Steam なしでゲームを起動可能にします。
  - **💾 セーブデータ切り替え**：複数のセーブスロットをサポート、または元の Steam ユーザーのセーブデータを読み込み可能。
  - **⌨️ デスクトップ入力**：Wallpaper Engine 上で、デスクトップから直接キーボード入力が可能。
  - **🇨🇳 中国語入力メソッド (RIME)**：RIME（中州韻）入力エンジンを統合しています。
      - *注：デフォルトでは中国語（ピンイン等）の入力設定が含まれていますが、RIME エンジン自体は日本語入力もサポートしています。*
  - **🌍 言語切り替え**：デフォルト言語設定をカスタマイズ可能。

### 🎵 音楽プレイヤー拡張

  - **📁 フォルダプレイリスト**：オーディオフォルダを自動スキャンし、ディレクトリごとにプレイリストを生成。
  - **💿 アルバム管理**：サブフォルダ（2階層目）をアルバムとして自動認識し、ジャケット画像に対応。
  - **🎵 拡張オーディオ形式**：OGG, FLAC, AIFF, .egg をサポート。
  - **🔢 制限解除**：100曲の制限を突破し、さらに12個の追加カスタムタグをサポート。
  - **⚡ 仮想スクロール**：可視範囲のリスト項目のみを描画し、2000曲以上のリストでもスムーズに動作。
  - **📋 再生キュー管理**：キューへの楽曲追加、クリアなどの操作をサポート。
  - **⏮️ 再生履歴**：最近再生した50曲を記録し、「前の曲」に戻る機能をサポート。
  - **💾 再生状態の復元**：再生位置、キュー、履歴を自動保存し、次回起動時に復元。

### 🔊 オーディオ制御

  - **🎛️ システムメディアコントロール (SMTC)**：Windows のメディアオーバーレイに曲情報とジャケットを表示し、メディアキーでの操作に対応。
  - **🔇 オーディオ回避 (Audio Ducking)**：他のアプリで音が鳴っている場合、自動的にゲームの音量を下げ、止まると元に戻します。

### 🎨 UI 最適化

  - **🖼️ アルバムアート表示**：プレイリストにアルバムアートを表示し、再生中は現在の曲のカバーを表示。
  - **📐 UI レイアウト変更**：ゲーム UI を音楽プレイヤーに近いレイアウトに調整。
  - **📜 アルバムグループ化**：アルバムごとに楽曲をグループ化し、折りたたみ/展開に対応。

## 📦 インストール方法

### 1\. BepInEx のインストール

1.  [BepInEx 5.x](https://github.com/BepInEx/BepInEx/releases) をダウンロードします。
2.  ゲームの `.exe` があるディレクトリに解凍します：
    ```
    # steam インストールの場合
    steamapps\common\Chill with You Lo-Fi Story
    # Wallpaper Engine プロジェクトの場合
    wallpaper_engine\projects\myprojects\chill_with_you
    ```

### 2\. ChillPatcher プラグインのインストール

1.  [可疑链接已删除] から最新の `ChillPatcher.zip` をダウンロードします。
2.  `ChillPatcher.zip` 内の `ChillPatcher` フォルダを以下に解凍・コピーします：
    ```
    # steam インストールの場合
    steamapps\common\Chill with You Lo-Fi Story\BepInEx\plugins\
    # Wallpaper Engine プロジェクトの場合
    wallpaper_engine\projects\myprojects\chill_with_you\BepInEx\plugins\
    ```

### 3\. 完了！

## **FLAC サポートについて**

  - **元のゲームの問題**：元のゲームは `.mp3`/`.wav` しか明確に処理しておらず、Unity のランタイムでの `.flac` サポートは限定的です。

      - ❌ サンプリングレートの誤認（再生速度がおかしくなる）
      - ❌ 一部のプラットフォームで FLAC 非対応
      - ❌ 動作の一貫性がない（Windows Editor では動くが、Standalone では失敗するなど）

  - **プラグインの解決策**：本プラグインは `dr_flac` ベースのネイティブデコーダー（`NativePlugins/FlacDecoder`）を内蔵し、Harmony パッチを通じてゲームのオーディオ読み込みプロセスに介入します：

      - 拡張フォーマット（`EnableExtendedFormats`）が有効で `.flac` ファイルに遭遇した場合、プラグインは優先的にネイティブデコーダーを使用してストリーミングデコードと再生を行います（`AudioClip.Create(..., stream: true)` + PCM コールバックを使用）。
      - ネイティブデコーダーはストリーミング API（`OpenFlacStream` / `ReadFlacFrames` / `SeekFlacStream` / `CloseFlacStream`）をエクスポートし、マネージド層は `Native/FlacDecoder.cs` の `FlacStreamReader` を通じて安全にカプセル化し、低メモリ消費とシーク機能を保証します。

詳細とビルドオプションについては [FlacDecoder](https://www.google.com/search?q=NativePlugins/FlacDecoder/README.md) を参照してください。

## 📋 再生キューと履歴

本プラグインは完全な再生キュー管理システムを実装しており、本格的な音楽プレイヤーのような体験を提供します。

### コアコンセプト

  - **再生キュー**：次に再生される曲のリスト。リストの先頭は常に「現在再生中の曲」です。
  - **再生履歴**：最近再生された曲の記録（最大50曲）。「前の曲」機能で使用されます。
  - **状態復元**：再生の進行状況、キュー、履歴を自動的に保存・復元します。

### 使用方法

**キューに追加**：

  - プレイリスト内の楽曲コントロールをクリックし、「キューに追加」や「次に再生」を選択します。
  - 楽曲は現在の曲の後、またはキューの最後に追加されます。

**キューの確認**：

  - 再生画面の「キュー」ボタンをクリックして、キュービューに入ります。
  - 待機中の曲や再生履歴を確認できます。
  - ドラッグして並べ替えたり、削除したりできます。

**キュー操作ボタン**（タグのドロップダウンメニュー内）：

  - **キューをクリア**：待機中の曲をすべて消去し、プレイリストからの再生を続けます。
  - **未来のキューをクリア**：これから再生される予定の曲のみを消去し、現在の曲は維持します。
  - **履歴をクリア**：再生履歴を消去します。

**前の曲 / 次の曲**：

  - 次の曲：キュー内の次の曲を再生します。
  - 前の曲：履歴内の前の曲に戻ります。
  - 履歴が尽きた場合、プレイリストの前方向へ探索を続けます。

### 状態の自動保存

ゲーム実行中、以下の状態が自動保存されます：

  - 現在再生中の曲
  - 再生キュー内の曲
  - 再生履歴
  - シャッフル/1曲リピートモード
  - 現在選択されているプレイリスト

次回起動時にこれらの状態が自動的に復元されます。

状態ファイルの場所：

```
C:\Users\<ユーザー名>\AppData\LocalLow\Nestopi\Chill With You\ChillPatcher\playback_state.json
```

## ⚙️ 設定オプション

設定ファイルは以下にあります：`<ゲームディレクトリ>\BepInEx\config\com.chillpatcher.plugin.cfg`

### Wallpaper Engine モード

```ini
[WallpaperEngine]

## Wallpaper Engine 互換機能を有効にするか
## true = オフラインモードを有効にし、すべての Steam オンライン機能をブロック
## false = ゲーム本来のロジックを使用（デフォルト）
## 注意：有効にすると、設定されたセーブデータが強制使用され、実績は Steam に同期されません
# Setting type: Boolean
# Default value: false
EnableWallpaperEngineMode = true
```

このオプションを変更して Wallpaper Engine モードを起動します。
このモードは Steam 認証を必要とせず、マウスでのインタラクションが可能です。

#### プレイ時間と実績について

  - **Steam プレイ時間は計測されません**
  - **キャッシュされた実績**

Wallpaper Engine モードでは実績がローカルにキャッシュされます。オンラインで起動した際、キャッシュされた実績から Steam のロック解除を試みますが、`OfflineUserId` をあなたの Steam ID（Steam のセーブデータ名）に設定する必要があります。
場所：

```
C:\Users\<ユーザー名>\AppData\LocalLow\Nestopi\Chill With You\SaveData\Release\v2
```

あなたの Steam にインストールされたゲームにもこのプラグインがインストールされており、かつ Wallpaper Engine モードがオフになっている場合にのみ、キャッシュからの実績同期が試みられます。

### 機能フレームワーク

```ini
[Features]

## 無制限の楽曲インポート（フォルダプレイリスト有効時に自動適用）
## Enable unlimited song import (may affect save compatibility)
# Setting type: Boolean
# Default value: false
EnableUnlimitedSongs = false

## 楽曲インポート形式の制限解除（フォルダインポートを使わない場合でも、公式のインポート制限を回避するために使用可能）
## Enable extended audio formats (OGG, FLAC, AIFF)
# Setting type: Boolean
# Default value: false
EnableExtendedFormats = false

## 仮想スクロール
## Enable virtual scrolling for better performance
# Setting type: Boolean
# Default value: true
EnableVirtualScroll = true

## フォルダインポート機能
## Enable folder-based playlists (runtime only, not saved)
# Setting type: Boolean
# Default value: true
EnableFolderPlaylists = true

## アルバムセパレーター（プレイリストにアルバムヘッダーを表示）
## Enable album separators in playlist view
# Setting type: Boolean
# Default value: true
EnableAlbumSeparators = true

## アルバムアート表示（再生時に現在の曲のカバーを表示）
## Enable album art display during playback
# Setting type: Boolean
# Default value: true
EnableAlbumArtDisplay = true

## UI 再配置（UI を音楽プレイヤーに近いレイアウトに調整）
## Enable UI rearrangement for music player style
# Setting type: Boolean
# Default value: true
EnableUIRearrange = true
```

### 仮想スクロール詳細設定

```ini
[Advanced]
## 仮想スクロールのバッファサイズ
## 可視領域の前後でレンダリングする追加項目の数
## 大きい値：スクロールが滑らかになるが、メモリ使用量がわずかに増える
## 小さい値：メモリ使用量は低いが、高速スクロール時に遅延が発生する可能性あり
## 推奨値：3-5
VirtualScrollBufferSize = 3
```

### フォルダプレイリスト設定

```ini
[Playlist]

## フォルダプレイリストシステムを有効にするか
## true = 有効（デフォルト）、ディレクトリをスキャンしてカスタムタグを作成
## false = 無効、フォルダのスキャンもカスタムタグの追加も行わない
# Setting type: Boolean
# Default value: true
EnableFolderPlaylists = true

## プレイリストのルートディレクトリパス
## ゲームのルートディレクトリ（.dllがある場所）からの相対パス
## デフォルト：playlist（ChillPatcher.dll と同階層の playlist フォルダ）
# Setting type: String
# Default value: playlist
RootFolder = playlist

## playlist.json を自動生成するか
## true = 初回スキャン時に JSON キャッシュを自動生成（デフォルト）
## false = 既存の JSON ファイルのみを使用
# Setting type: Boolean
# Default value: true
AutoGenerateJson = true

## プレイリストキャッシュを有効にするか
## true = playlist.json キャッシュを読み込み、起動を高速化（デフォルト）
## false = 起動ごとにすべてのオーディオファイルを再スキャン
# Setting type: Boolean
# Default value: true
EnableCache = true
```

**使用例**：

音楽フォルダ構成が以下のようになっていると仮定します：

```
playlist/
├── PlaylistA/                  ← 第1階層フォルダ = プレイリスト名
│   ├── song1.mp3               ← ルートの曲は「その他」アルバムに入る
│   ├── song2.ogg
│   ├── Album1/                 ← 第2階層フォルダ = アルバム名
│   │   ├── cover.jpg           ← アルバムアート（任意）
│   │   ├── track1.flac
│   │   └── track2.flac
│   └── Album2/
│       └── track3.mp3
└── PlaylistB/
    └── ...
```

`RootFolder = playlist` と設定すると、以下のプレイリストが自動生成されます：

  - 📁 PlaylistA（「その他」アルバム + Album1 + Album2 を含む）
  - 📁 PlaylistB

**注意**：

  - ルートディレクトリ（playlist/直下）のオーディオファイルは自動的に `default` フォルダに移動されます。
  - 第1階層フォルダがプレイリスト、第2階層フォルダがアルバムとして扱われます。
  - 各アルバムには個別のカバー画像を設定できます。

**対応オーディオ形式**：

  - `.mp3` - MP3 (MPEG Audio)
  - `.wav` - WAV (Waveform Audio)
  - `.ogg` - Ogg Vorbis
  - `.egg` - Ogg Vorbis
  - `.flac` - FLAC (Free Lossless Audio Codec)
  - `.aiff` / `.aif` - AIFF (Audio Interchange File Format)

**アルバムアート**：

システムは以下の優先順位でアルバムアートを検索します：

1.  アルバムディレクトリ内の画像ファイル（優先順）：
      - `cover.jpg`, `cover.png`, `cover.jpeg`
      - `folder.jpg`, `folder.png`
      - `album.jpg`, `album.png`
      - `front.jpg`, `front.png`
2.  オーディオファイルに埋め込まれたカバー画像（MP3 の ID3 タグ、FLAC のメタデータなど）

**新曲の追加方法（差分更新）**：

初回実行後、各プレイリストフォルダにキャッシュファイルが生成されます：

```
playlist/
├── MyFavorites/
│   ├── !rescan_playlist    ← 再スキャン用フラグファイル
│   ├── playlist.json       ← 楽曲キャッシュ
│   ├── song1.mp3
│   └── Album1/
│       ├── cover.jpg       ← カバー画像
│       └── track1.mp3
```

新曲を追加するには：

1.  新しいオーディオファイルをプレイリストフォルダまたはアルバムフォルダに入れます。
2.  そのプレイリストフォルダ内の `!rescan_playlist` ファイルを削除します。
3.  ゲームを再起動します。

システムは以下の処理を行います：

  - ✅ 既存曲の UUID を保持（お気に入り、並び順、除外状態を維持）
  - ✅ 新曲に新しい UUID を割り当て
  - ✅ `playlist.json` キャッシュを更新
  - ✅ `!rescan_playlist` フラグファイルを再作成

**注意**：

  - 各プレイリストフォルダは独立して管理され、相互に影響しません。
  - 更新が必要なフォルダのフラグファイルのみを削除してください。
  - フラグファイルを削除しない場合、キャッシュを使用して高速に読み込まれます。

### 🔊 オーディオ制御

```ini
[Audio]
## システムオーディオ検出による自動ミュート機能を有効にするか
## 他のアプリが音声を再生していることを検出した場合、ゲームの音楽音量を自動的に下げます
## Windows WASAPI を使用するため、Windows でのみ有効です
# Setting type: Boolean
# Default value: false
EnableAutoMuteOnOtherAudio = false

## 他の音声を検出した際のターゲット音量（0-1）
## 0 = 完全ミュート
## 0.1 = 10%まで下げる（デフォルト）
# Setting type: Float
# Default value: 0.1
AutoMuteVolumeLevel = 0.1

## 他の音声の検出間隔（秒）
## デフォルト：1秒
# Setting type: Float
# Default value: 1.0
AudioDetectionInterval = 1.0

## 音量復帰時のフェードイン時間（秒）
# Setting type: Float
# Default value: 1.0
AudioResumeFadeInDuration = 1.0

## 音量低下時のフェードアウト時間（秒）
# Setting type: Float
# Default value: 0.3
AudioMuteFadeOutDuration = 0.3

## システムメディアコントロール (SMTC) 機能を有効にするか
## システムメディアオーバーレイに再生情報を表示し、メディアキー制御をサポート
## ChillSmtcBridge.dll が必要です。Windows 10/11 でのみ有効
# Setting type: Boolean
# Default value: false
EnableSystemMediaTransport = false
```

**オーディオ回避機能の説明**：

  - 動画視聴、通話、または他のアプリで音声を再生している間、ゲーム音楽の音量が自動的に下がります。
  - 他の音声が停止すると、ゲーム音楽はスムーズに元の音量に戻ります。
  - 検出間隔やフェード時間を調整可能です。

**システムメディアコントロール (SMTC) 機能の説明**：

  - Windows のシステムメディアオーバーレイに現在再生中の曲情報とカバーを表示します。
  - キーボードのメディアキー（再生/一時停止、前へ、次へ）での制御をサポートします。
  - システム音量 OSD を使用した再生制御をサポートします。

### 言語設定

重要ではありません。セーブデータがない場合のみ有効です。

```ini
[Language]
## デフォルトのゲーム言語
## 0 = None (なし)
## 1 = Japanese (日本語)
## 2 = English (英語)
## 3 = ChineseSimplified (簡体字中国語) - デフォルト
## 4 = ChineseTraditional (繁体字中国語)
## 5 = Portuguese (ポルトガル語)
DefaultLanguage = 3
```

### セーブデータ設定

```ini
[SaveData]
## オフラインモードで使用するユーザーID
## この値を変更することで異なるセーブスロットを使用したり、元のSteamユーザーのセーブを読み込んだりできます
## 例：これをあなたの Steam ID に変更すると、元のセーブデータにアクセスできます
OfflineUserId = OfflineUser

## マルチセーブ機能を使用するか
## true = 設定されたオフラインユーザーIDをセーブパスとして使用し、異なるセーブを切り替え可能
## false = Steam ID をセーブパスとして使用（デフォルト）
## 注意：有効にすると、Wallpaper Engine モードでなくても設定されたセーブパスが使用されます
# Setting type: Boolean
# Default value: false
UseMultipleSaveSlots = false
```

**元の Steam セーブデータを使用するには？**

1.  あなたの Steam ID（17桁の数字）を確認します。
2.  設定ファイルの `OfflineUserId = あなたのSteamID` に変更します。
3.  ゲームを再起動すると、元のセーブデータが使用されます。

**複数のセーブスロットを使用するには？**

  - `UseMultipleSaveSlots = true` を有効にします。
  - 異なる `OfflineUserId` が異なるセーブデータに対応します。
  - 例：`OfflineUserId = Save1`、`OfflineUserId = Save2`

### DLC 設定

```ini
[DLC]
## DLC機能を有効にするか
EnableDLC = false
```

### キーボードフック設定

```ini
[KeyboardHook]
## キーボードフックのメッセージループチェック間隔（ミリ秒）
## デフォルト値：1ms（推奨）
## 小さい値：応答が速いが、CPU使用率がわずかに高い
## 大きい値：CPU使用率は低いが、応答がわずかに遅い
## 推奨範囲：1-10ms
MessageLoopInterval = 1
```

**調整の提案**：

  - `1ms` - 最適な応答速度（デフォルト推奨）
  - `5ms` - パフォーマンスと応答のバランス
  - `10ms` - 低 CPU 使用率

### RIME 入力メソッド設定

```ini
[InputMethod]
## RIME入力メソッドを有効にするか
EnableRimeInputMethod = true

## Rime共有データディレクトリパス（スキーマ設定ファイル）
## 空欄の場合、自動的に検索されます。優先順位：
## 1. BepInEx/plugins/ChillPatcher/rime-data/shared
## 2. %AppData%/Rime
## 3. この設定で指定されたカスタムパス
# Setting type: String
# Default value: 
SharedDataPath = 

## Rimeユーザーデータディレクトリパス（辞書、ユーザー設定）
## 空欄の場合、以下を使用：BepInEx/plugins/ChillPatcher/rime-data/user
# Setting type: String
# Default value: 
UserDataPath = 
```

## 🖥️ Wallpaper Engine 使用説明

### デスクトップ入力機能

ゲームウィンドウではなく、デスクトップをクリックしている状態でも、ゲームの入力ボックスに入力することができます：

1.  ゲーム内の入力ボックス（検索ボックス、チャットボックスなど）をクリックします。
2.  入力ボックスにフォーカスが当たります。
3.  デスクトップをクリックしても、キーボード入力はキャプチャされ、ゲームに入力されます。

**サポート機能**：

  - ✅ 日本語・中国語入力（RIME エンジン経由）
  - ✅ 英数字、一般的な記号
  - ✅ Backspace（削除）、Delete、矢印キー
  - ✅ Enter（確定）、上下キーでの候補選択

### 🇨🇳 RIME 入力メソッド（および日本語サポートについて）

本プラグインは **RIME（中州韻）入力メソッドエンジン** を統合しています。これは強力なオープンソースのクロスプラットフォーム入力フレームワークです。

**⚠️ 重要なお知らせ**：
本プラグインには、デフォルトで **中国語（ピンイン等）の入力スキーマ** が同梱されています。これはゲームのオリジナル機能（中国語チャット等）をサポートするためです。
ただし、**RIME エンジン自体は汎用的であり、日本語入力（ローマ字入力等）も完全にサポートしています。** 日本語入力を使用したい場合、別途 RIME 用の日本語スキーマ設定を追加することで利用可能です。

#### RIME とは？

RIME（Rime Input Method Engine）は、以下をサポートするオープンソースの入力エンジンです：

  - 🎯 **多様な入力スキーマ**：ピンイン、注音、倉頡、五筆など（設定により日本語ローマ字なども可）
  - 🔧 **高度なカスタマイズ**：YAML 設定ファイルによる自由なカスタマイズ
  - 📚 **スマート変換**：クラウド入力、ユーザー辞書、自動学習をサポート
  - 🌏 **クロスプラットフォーム**：Windows(Weasel), macOS(Squirrel), Linux(ibus-rime)

詳細情報：

  - 公式サイト：[https://rime.im/](https://rime.im/)
  - GitHub：[https://github.com/rime/home](https://github.com/rime/home)
  - 詳細ドキュメント：[https://github.com/rime/home/wiki](https://github.com/rime/home/wiki)

#### ショートカットキー

| キー | 機能 |
|------|------|
| **F4** | スキーマメニューを開く（入力スキーマ切替、全角/半角など） |
| **F6** | RIME 再デプロイ（再起動せずに設定をリロード） |
| **上/下** | 候補の選択 |
| **数字キー 1-9** | 対応する候補を直接選択 |
| **Space** | 最初の候補を確定 |
| **左/右** | カーソル移動 |

#### デフォルト入力スキーマ（同梱）

初回実行時に、以下の中国語スキーマが自動展開されます：

  - 🌙 **明月拼音** (luna\_pinyin) - 全拼、デフォルト
  - 📌 **小鶴双拼** (double\_pinyin\_flypy)
  - 🎹 **自然碼双拼** (double\_pinyin)
  - 🪟 **Microsoft双拼** (microsoft\_shuangpin)
  - など

#### 設定ファイルの場所

RIME 設定ファイルは以下にあります：

```
BepInEx\plugins\ChillPatcher\rime-data\shared
```

よく使うファイル：

  - `default.yaml` - グローバル設定（スキーマリスト、ショートカットなど）
  - `<スキーマ名>.schema.yaml` - 各入力スキーマの設定
  - `<スキーマ名>.custom.yaml` - ユーザーカスタム設定（推奨）
  - `<スキーマ名>.userdb.txt` - ユーザー辞書（インポート/エクスポート可）

#### カスタマイズ例

`user/default.custom.yaml` を修正（存在しない場合は作成）：

```yaml
# カスタムパッチファイル
patch:
  # 候補単語数を変更
  "menu/page_size": 7
  
  # ショートカットを変更
  "switcher/hotkeys":
    - "Control+grave"  # Ctrl+` でスキーマ切り替え
    - "F4"
  
  # カスタムスキーマを追加
  "schema_list":
    - schema: luna_pinyin         # 明月拼音
    - schema: double_pinyin_flypy # 小鶴双拼
```

修正後、**F6** を押して再デプロイすると有効になります。

#### 候補表示形式

  - **下付き数字** `₁₂₃` - 未選択の候補
  - **上付き数字** `¹²³` - 現在選択中の候補

例：`nihao [你¹ 呢₂ 尼₃ 倪₄]`（現在「你」を選択中）

#### よくある質問

**Q: 入力スキーマを切り替えるには？**
A: `F4` を押してスキーマメニューを開き、数字キーまたは矢印キーで選択します。

**Q: 設定を変更した後、どうすれば反映されますか？**
A: `F6` を押して RIME を再デプロイしてください。ゲームの再起動は不要です。

**Q: 自分の辞書をインポートするには？**
A: `.userdb.txt` または `.dict.yaml` を `rime/user/` ディレクトリに入れ、`F6` で再デプロイします。

**Q: RIME 入力メソッドに問題がある場合は？**
A:

  - ✅ まず RIME 公式ドキュメントを確認してください：[https://github.com/rime/home/wiki](https://github.com/rime/home/wiki)
  - ✅ ログファイルを確認してください：`rime/user/logs/`
  - ❌ **RIME 公式リポジトリに issue を送信しないでください**（これはサードパーティによる統合です）
  - ✅ 本プラグインの統合に関する問題であると確認できた場合、本プロジェクトに issue を送信してください。

**Q: 日本語を入力したいのですが？**
A: RIME は日本語入力をサポートしていますが、本プラグインにはデフォルトで日本語設定ファイルが含まれていません。RIME 用の日本語スキーマを `rime-data` フォルダに追加し、設定することで使用可能です。

**Q: 入力メソッドを完全に無効にするには？**
A: 設定ファイル `BepInEx\config\com.chillpatcher.plugin.cfg` を修正します：

```ini
[InputMethod]
EnableRimeInputMethod = false
```

### 入力バッファのクリア

入力を中断したい場合は：

  - ゲーム内の任意の場所をクリック（左クリック）
  - または、他の入力ボックスをクリック（自動的に RIME 状態がクリアされます）

## 🔧 開発ビルド

```bash
# リポジトリをクローン
git clone <repository-url>

# Visual Studio または Rider で開く
ChillPatcher.sln

# プロジェクトをビルド
dotnet build

# 出力ディレクトリ
bin/Debug/ChillPatcher.dll
```

## ❓ よくある質問

### Q: ゲーム起動時に白い画面になる/フリーズする？

A: `BepInEx\LogOutput.log` でエラー情報を確認してください。通常は BepInEx のバージョン非互換が原因です。

### Q: デスクトップ入力が機能しない？

A: 以下を確認してください：

1.  ゲームの入力ボックスにフォーカスがあること
2.  現在のフォアグラウンドウィンドウがデスクトップであること（他のアプリではない）
3.  `MessageLoopInterval` 設定を調整してみてください

### Q: ゲーム終了後もプロセスが応答なしになる？

A: 最新バージョンでは修正済みです。問題が続く場合は、ログの `[KeyboardHook]` 情報を確認してください。

### Q: デスクトップ入力機能を無効にするには？

A: 現在、設定での無効化はサポートしていません。無効にしたい場合は `ChillPatcher` プラグインを削除してください（入力メソッド自体は設定で無効化可能です）。

## 📜 ライセンス

本プロジェクトは **GPL v3** ライセンスを採用しています：

  - **librime** ([Rime Input Method Engine](https://github.com/rime/librime)) - BSD 3-Clause License
  - **BepInEx** - LGPL 2.1 License
  - **HarmonyX** - MIT License

オープンソースライセンスの互換性に基づき、ChillPatcher 全体は GPL v3 ライセンスとなります。詳細は [LICENSE](https://www.google.com/search?q=LICENSE) ファイルを参照してください。

**注意**: 本プロジェクトは学習・研究目的でのみ提供されています。ゲーム本体の著作権は元の開発者に帰属します。正規版をサポートしてください。

## 🙏 謝辞

  - [RIME/中州韻輸入法引擎](https://github.com/rime/librime) - 強力なオープンソース入力エンジン
  - [BepInEx](https://github.com/BepInEx/BepInEx) - Unity ゲーム MOD フレームワーク
  - [HarmonyX](https://github.com/BepInEx/HarmonyX) - .NET ランタイムメソッドパッチライブラリ
  - [dr\_libs](https://github.com/mackron/dr_libs) - flac デコードサポート
