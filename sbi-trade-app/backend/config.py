"""アプリ全体の設定とモード定義.

モード = 「その時々で何を重視するか」の重み設定。
独自AI(engine.py)が各シグナルのスコアをこの重みで統合する。
"""
from dataclasses import dataclass, field


@dataclass(frozen=True)
class Mode:
    key: str
    label: str
    description: str
    # 各シグナルの重み（合計は内部で正規化される）
    weights: dict = field(default_factory=dict)
    # 推奨を出す最低確信度。これ未満は「様子見」に倒す
    min_confidence: float = 0.5
    # ランキングに出す最大件数
    top_n: int = 15


# シグナル名（analysis/engine.py が返すスコアのキー）
SIGNAL_KEYS = ["technical", "momentum", "volume", "sentiment", "value", "dividend"]


MODES: dict[str, Mode] = {
    "short_trade": Mode(
        key="short_trade",
        label="短期トレード",
        description="数日〜数週間。テクニカルとモメンタム、出来高を重視。",
        weights={"technical": 0.35, "momentum": 0.30, "volume": 0.20, "sentiment": 0.15},
        min_confidence=0.55,
    ),
    "news_burst": Mode(
        key="news_burst",
        label="ニュース速報重視",
        description="材料・ニュースの強さを最優先。短期の急変を拾う。",
        weights={"sentiment": 0.50, "momentum": 0.25, "volume": 0.25},
        min_confidence=0.50,
    ),
    "value": Mode(
        key="value",
        label="割安(中長期)",
        description="割安さとトレンドの底打ちを重視した中長期目線。",
        weights={"value": 0.45, "technical": 0.30, "sentiment": 0.25},
        min_confidence=0.50,
    ),
    "dividend": Mode(
        key="dividend",
        label="配当重視",
        description="配当利回りと安定性を重視。",
        weights={"dividend": 0.50, "value": 0.30, "technical": 0.20},
        min_confidence=0.45,
    ),
    "cautious": Mode(
        key="cautious",
        label="様子見強め",
        description="根拠がよく揃ったものだけを厳選。確信度の閾値が高い。",
        weights={"technical": 0.30, "momentum": 0.25, "volume": 0.20, "sentiment": 0.25},
        min_confidence=0.70,
    ),
}

DEFAULT_MODE = "short_trade"

# 4時間ごと更新
REFRESH_HOURS = 4

# SQLite の保存先
DB_PATH = "sbi_trade.db"
