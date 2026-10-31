# 今回の確認範囲

ユーザーの「調査は自分で行う。開発だけ」に従い、ゲーム・Editor・Play Mode・契約テスト・自動対戦は実行していません。

- `compile-summary.json`：保存済み参照による3構成の静的C#コンパイル。コードコミットと入力のSHA256を記録。
- `compiled-source-sha256.json`：実際にコンパイルしたソースのバイト列のSHA256。現在のソースとの一致を確認。
- `*.log`：今回の静的コンパイラ出力。USG0001は情報診断です。
- `r4-152-acceptance.csv`：正式受入152ID。全件NOT RUN。
- `knowledge-120-acceptance.csv`：知識40件×三値120条件。全件NOT RUN。
- `scenario-40-acceptance.csv`：実戦40シナリオ。全件NOT RUN。
- `acceptance-gates.json`：差分範囲・静的コンパイルと正式ゲートの区別。

SourceReferencesはテストソースにIDが登場する場所の一覧であり、全fixtureの完成・登録・実行を保証しません。具体Given/Whenが未展開の行は台帳に明記しています。
コードを読んで推定した改善を、実戦で再現・修正確認した結果として扱いません。過去のテスト結果は今回へ転用していません。
