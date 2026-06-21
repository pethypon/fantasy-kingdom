"""ニュース感情分析（日本語・辞書ベース）.

外部の有料APIに頼らない「独自AI」の感情パート。
- ポジ/ネガの金融語彙でスコアリング（透明・高速・無料）
- 記事の新しさで重みづけ（古いニュースは影響を減衰）
- 後でローカルLLMや学習モデルに差し替え可能なように関数を分離

辞書ベースは万能ではないが、根拠（どの語が効いたか）を提示できる点が強み。
"""
from __future__ import annotations

import time
from dataclasses import dataclass

from data_sources.news import NewsItem

POSITIVE_WORDS = {
    "最高益": 3, "上方修正": 3, "増益": 2, "増収": 2, "黒字": 2, "好調": 2,
    "受注": 1, "提携": 2, "買収": 1, "新製品": 1, "過去最高": 3, "急騰": 2,
    "上昇": 1, "回復": 2, "増配": 3, "自社株買い": 3, "好決算": 3, "上場来高値": 2,
    "需要拡大": 2, "黒字転換": 3, "成長": 1, "受賞": 1, "シェア拡大": 2,
}
NEGATIVE_WORDS = {
    "下方修正": -3, "減益": -2, "減収": -2, "赤字": -3, "不振": -2, "急落": -2,
    "下落": -1, "リコール": -3, "不正": -3, "減配": -3, "無配": -3, "提訴": -2,
    "訴訟": -2, "業績悪化": -3, "撤退": -2, "リストラ": -1, "違反": -2,
    "安値": -1, "懸念": -1, "売り": -1, "下振れ": -2, "停止": -2, "炎上": -2,
}

# 影響の半減期（秒）。約2日で重みが半分に。
HALF_LIFE_SEC = 2 * 24 * 3600


@dataclass
class SentimentResult:
    score: float           # 0..100（50が中立）
    article_count: int     # 一致記事数（多いほど確信材料が多い）
    reasons: list[str]     # 効いた根拠（記事タイトル＋効いた語）


def _recency_weight(published: float, now: float) -> float:
    if not published:
        return 0.5  # 日時不明はやや低めの重み
    age = max(0.0, now - published)
    return float(0.5 ** (age / HALF_LIFE_SEC))


def analyze(matched: list[NewsItem]) -> SentimentResult:
    if not matched:
        return SentimentResult(score=50.0, article_count=0, reasons=["関連ニュースなし（中立）"])

    now = time.time()
    weighted_sum = 0.0
    weight_total = 0.0
    reasons: list[str] = []

    for n in matched[:12]:
        text = n.text
        raw = 0
        hit_words: list[str] = []
        for w, v in POSITIVE_WORDS.items():
            if w in text:
                raw += v; hit_words.append(f"+{w}")
        for w, v in NEGATIVE_WORDS.items():
            if w in text:
                raw += v; hit_words.append(w)
        if raw == 0:
            continue
        w = _recency_weight(n.published, now)
        weighted_sum += raw * w
        weight_total += w
        if len(reasons) < 5:
            tag = "／".join(hit_words[:3])
            reasons.append(f"「{n.title[:32]}」 [{tag}]")

    if weight_total == 0:
        return SentimentResult(50.0, len(matched), ["関連ニュースはあるが材料語は中立"])

    avg = weighted_sum / weight_total           # おおよそ -3..+3
    score = float(max(0.0, min(100.0, 50 + avg * 12)))
    if not reasons:
        reasons = ["材料は中立的"]
    return SentimentResult(score=score, article_count=len(matched), reasons=reasons)
