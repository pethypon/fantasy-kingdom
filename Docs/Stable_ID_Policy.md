# 保存用IDの運用方針

更新日: 2026-10-11。対象は登録コンテンツ、対戦セーブ、AI学習データです。

| 対象 | 保存用の識別子 | 管理場所 |
| --- | --- | --- |
| 独立した駒の定義 | `UnitData.definitionId` | `UnitAuthoringCatalog` |
| 建物の定義 | `FacilityDefinitionData.definitionId` | `FacilityAuthoringCatalog` |
| 駒・建物の個体 | `Status.ReflectionLifeId` | 対戦セーブとAIの観測記録 |
| 第三陣営イベント | `ThirdFactionEventDefinition.EventId` | `ThirdFactionDirectorConfig.Events` |
| 遭遇内容 | `R1ContentCatalog.Encounter.Id` | 魔物・乱入者の一覧 |
| 固有報酬 | `R1ContentCatalog.UniqueReward.Id` | 遭遇・イベント・強敵の報酬 |
| スキル | `SkillData.Id` の明示した整数 | `SkillData.Table` |
| 標準兵種・陣営・向き・能力 | `StableSaveKeys` の固定文字列 | 対戦セーブの互換表 |

駒・建物の制作時はGUIDを含むIDを発行します。複製には新しいIDを発行します。表示名、分類、Prefabのファイル名を変更しても、既存のIDは維持してください。比較は大文字・小文字を区別するOrdinalです。前後の空白・制御文字を含むIDは登録検証で拒否し、自動で切り詰めたり名前から作り直したりしません。

`ReflectionLifeId` は個体に一度発行し、ロード時は保存値を復元します。UnityのInstanceIDは実行中の検索に使用できますが、永続IDとして保存しません。配列順序、Listの添字、GameObject名、クラス名も永続IDに使用しません。

## 互換性を守る変更

`StableSaveKeys` の文字列は公開済みデータとして扱います。現在の文字列がenum名と同じでも、将来enum名を変更するときは固定文字列を維持します。新しいenum値を追加するときは固定キーと互換テストを追加してください。既存の列挙値を並べ替えたり、削除した数値を別用途へ再利用したりしません。Unityアセットと旧AIデータには数値enumが残っているためです。

旧兵種の上書き用データは空のdefinitionIdを許可し、固定兵種キーで識別します。独立した追加駒・登録建物にはIDを要求します。未登録の編集用データはゲームへの登録と区別します。既存建物の編集用複製を一覧に置くだけで、その建物を建築候補へ追加しません。

コンテンツを引退させるときはIDと旧セーブからの解決方法を残します。やむを得ずIDを移行する場合は、旧IDから新IDへの明示した変換と旧セーブ検証を同じ変更に含めます。見つからないIDを無関係な駒へ置き換えてセーブを上書きすることは避けます。

## 検証

Unityの **Fantasy Kingdom → 長期運用 → 全コンテンツと保存用IDを検証** を実行します。同じ検証は製品版ビルド前にも実行し、不正な登録がある場合はビルドを停止します。

検査は有効なResourcesの登録を対象とします。ID、兵種上書きの重複、Prefab、固有スキル、8×8原点、遭遇・イベントID、遭遇参照を確認します。魔物と乱入者の遭遇IDは共通の検索範囲なので、両一覧を合わせて重複を検査します。未設定の任意報酬と旧兵種の空IDは許容します。

制作は `Assets/Editor/GameDevelopmentStudioWindow.Data.cs`、検査は `Assets/Editor/LongTermContentValidation.cs`、回帰検証は `Tests~/LongTermContentTests.cs` です。
