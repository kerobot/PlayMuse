# サードパーティ ライセンス通知（プレースホルダー）

このファイルは、PlayMuseが依存するサードパーティライブラリのライセンス情報を記録するプレースホルダーです。
プロジェクト全体のライセンス方針は後日調整するため、現時点では検出済みライブラリを一覧化するに留めます。
配布前には、各ライブラリのライセンス全文の同梱要否を含めて再確認してください。

| ライブラリ | ライセンス | 用途 | 備考 |
| --- | --- | --- | --- |
| TagLibSharp | LGPL-2.1 | mp3(ID3)/flac(Vorbis Comment)等のメタデータ読み取り | LGPLのため、静的リンクではなく動的参照（NuGetパッケージ参照）である点を確認済み。配布形態によっては再頒布条件（ライセンス文書の同梱、リンク方法の開示等）の対応が必要になる可能性がある。 |
| NAudio | MIT | WASAPI再生、音声デコード(mp3/flac等) | |
| CommunityToolkit.Mvvm | MIT | MVVM基盤(ObservableObject/RelayCommand) | |
| Microsoft.Extensions.DependencyInjection | MIT | DIコンテナ | |

## TODO
- [ ] TagLibSharp (LGPL-2.1) のライセンス全文同梱要否を確認する。
- [ ] 配布物にNOTICEファイルとして本ファイルを含めるか、あるいは正式なライセンス表記ページを別途作成するか決定する。
