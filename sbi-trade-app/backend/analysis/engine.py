"""独自AI：マルチシグナル統合エンジン.

設計思想（= ユーザー要望「確実な情報になるまで集める」の数値化）:
  1. 複数シグナル（テクニカル/モメンタム/出来高/感情/割安/配当）を 0..100 で算出
  2. モードの重みで「総合スコア」を合成
  3. confidence(確信度) を別途算出。confidence は次の3つで決まる：
       - データ充足度   : 価格履歴やニュースが十分あるか
       - シグナル一致度 : 各シグナルが同じ方向（強気/弱気）を指しているか
       - 材料の厚み     : 関連ニュース件数
     → 根拠が「揃って」「多い」ほど confidence が上がる。
  4. confidence が低い銘柄は推奨に出さず「様子見」に倒す（断言しない）。
"""
from __future__ import annotations

from dataclasses import dataclass, field

import numpy as np

from analysis import indicators, sentiment
from data_sources.market import PriceHistory
from data_sources.news import NewsItem, match_news


@dataclass
class Evaluation:
    ticker: str
    name: str
    score: float                 # 総合スコア 0..100（モード重み適用後）
    confidence: float            # 確信度 0..1
    action: str                  # "buy_watch" / "neutral" / "avoid"
    signals: dict = field(default_factory=dict)   # 各シグナル素点
    reasons: list[str] = field(default_factory=list)
    news_count: int = 0
    last_close: float = float("nan")


def _agreement(signals: dict, weights: dict) -> float:
    """重み付きシグナルが中立(50)からどれだけ同方向に揃っているか 0..1。"""
    vals, ws = [], []
    for k, w in weights.items():
        if k in signals:
            vals.append(signals[k] - 50.0)
            ws.append(w)
    if not vals:
        return 0.0
    vals = np.array(vals, dtype=float)
    ws = np.array(ws, dtype=float)
    ws = ws / ws.sum()
    mean = float(np.average(vals, weights=ws))
    # ばらつき（重み付き標準偏差）が小さいほど一致
    var = float(np.average((vals - mean) ** 2, weights=ws))
    std = var ** 0.5
    direction_strength = min(abs(mean) / 30.0, 1.0)       # 方向の強さ
    consistency = max(0.0, 1.0 - std / 30.0)              # 散らばりの小ささ
    return float(np.clip(0.5 * direction_strength + 0.5 * consistency, 0, 1))


def evaluate(
    ph: PriceHistory,
    name: str,
    news_keyword: str,
    all_news: list[NewsItem],
    weights: dict,
) -> Evaluation:
    signals: dict[str, float] = {}
    reasons: list[str] = []

    # データ不足なら早期に低確信で返す（＝確実でないものは推さない）
    if not ph.ok:
        return Evaluation(
            ticker=ph.ticker, name=name, score=50.0, confidence=0.1,
            action="neutral", signals={}, reasons=["価格データ不足のため判定保留"],
            news_count=0, last_close=ph.last_close,
        )

    tech, r = indicators.technical_score(ph.df); signals["technical"] = tech; reasons += r
    mom, r = indicators.momentum_score(ph.df); signals["momentum"] = mom; reasons += r
    vol, r = indicators.volume_score(ph.df); signals["volume"] = vol; reasons += r
    val, r = indicators.value_score(ph.info); signals["value"] = val; reasons += r
    div, r = indicators.dividend_score(ph.info); signals["dividend"] = div; reasons += r

    matched = match_news(news_keyword, all_news)
    sent = sentiment.analyze(matched); signals["sentiment"] = sent.score
    reasons += [f"ニュース: {x}" for x in sent.reasons]

    # --- モード重みで総合スコア ---
    used = {k: v for k, v in weights.items() if k in signals}
    wsum = sum(used.values()) or 1.0
    score = sum(signals[k] * w for k, w in used.items()) / wsum

    # --- 確信度 ---
    data_suff = min(len(ph.df) / 120.0, 1.0)                       # 履歴の厚み
    news_suff = min(sent.article_count / 5.0, 1.0)                  # 材料の厚み
    agree = _agreement(signals, used)                              # 一致度
    confidence = float(np.clip(0.45 * agree + 0.35 * data_suff + 0.20 * news_suff, 0, 1))

    # --- アクション判定 ---
    if score >= 60 and confidence >= 0.5:
        action = "buy_watch"
    elif score <= 40 and confidence >= 0.5:
        action = "avoid"
    else:
        action = "neutral"

    return Evaluation(
        ticker=ph.ticker, name=name, score=round(score, 1),
        confidence=round(confidence, 2), action=action,
        signals={k: round(v, 1) for k, v in signals.items()},
        reasons=reasons, news_count=sent.article_count, last_close=ph.last_close,
    )


def rank(evaluations: list[Evaluation], min_confidence: float, top_n: int) -> list[Evaluation]:
    """確信度の閾値を満たし、買い目線のものを総合スコア順に並べる。

    閾値未満は「様子見」として action を neutral に倒した上で除外する
    （＝確実でないものは推さない）。
    """
    candidates = [
        e for e in evaluations
        if e.action == "buy_watch" and e.confidence >= min_confidence
    ]
    candidates.sort(key=lambda e: (e.score * e.confidence), reverse=True)
    return candidates[:top_n]
