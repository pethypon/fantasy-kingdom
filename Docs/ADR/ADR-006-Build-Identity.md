# ADR-006: ビルド検証と生成情報をEditor境界へ置く

日付: 2026-10-11。状態: 採用。

Inspector編集やマージで入った重複・参照切れを検出するため、全登録を検証する日本語メニューを提供し、同じ検証を通常ビルド前にも実行する。不正な保存用ID、必須参照、行動設定を含む製品はビルド境界で停止する。

ビルドの由来はVersion、Commit、Dirty、UnityVersion、UTC日時、SaveSchema、packages-lockハッシュとして同梱する。実行中は読み取り専用とし、欠落してもゲームを停止しない。コピーでは元Commitを環境変数で渡し、SOURCE_DATE_EPOCHで日時を固定できる。

Resources JSONはビルド中だけ生成し、元ファイルとGUIDを復元する。中断時の記録はLibraryに保持する。手動変更されたファイルは上書きしない。シーン、パッケージ、既存IDは変更しない。

実装はFKBuildInfo、FKBuildMetadataStage、FKLongTermBuildProcessor、LongTermContentValidation。検証はTests~/LongTermContentTests.cs、手順はBuild_Manual.mdにまとめる。
