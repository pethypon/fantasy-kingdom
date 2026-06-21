"""テクニカル指標の計算（依存は pandas/numpy のみ）.

各指標は 0..100 の「強気スコア」と、人が読める理由文字列を返す。
"""
from __future__ import annotations

import numpy as np
import pandas as pd


def _rsi(close: pd.Series, period: int = 14) -> float:
    delta = close.diff()
    gain = delta.clip(lower=0).rolling(period).mean()
    loss = (-delta.clip(upper=0)).rolling(period).mean()
    rs = gain / loss.replace(0, np.nan)
    rsi = 100 - (100 / (1 + rs))
    val = rsi.iloc[-1]
    return float(val) if not np.isnan(val) else 50.0


def _macd_hist(close: pd.Series) -> float:
    ema12 = close.ewm(span=12, adjust=False).mean()
    ema26 = close.ewm(span=26, adjust=False).mean()
    macd = ema12 - ema26
    signal = macd.ewm(span=9, adjust=False).mean()
    return float((macd - signal).iloc[-1])


def technical_score(df: pd.DataFrame) -> tuple[float, list[str]]:
    """移動平均・RSI・MACD を統合したテクニカル強気度 (0..100)。"""
    close = df["Close"]
    reasons: list[str] = []
    sub: list[float] = []

    # --- 移動平均（トレンド） ---
    sma25 = close.rolling(25).mean().iloc[-1]
    sma75 = close.rolling(75).mean().iloc[-1] if len(close) >= 75 else sma25
    price = close.iloc[-1]
    if price > sma25 > sma75:
        sub.append(80); reasons.append("価格が25日・75日線の上（上昇トレンド）")
    elif price > sma25:
        sub.append(62); reasons.append("価格が25日線の上（短期は強い）")
    elif price < sma25 < sma75:
        sub.append(22); reasons.append("価格が25日・75日線の下（下降トレンド）")
    else:
        sub.append(45); reasons.append("移動平均は方向感に乏しい")

    # --- RSI（過熱/底値） ---
    rsi = _rsi(close)
    if rsi >= 70:
        sub.append(35); reasons.append(f"RSI {rsi:.0f}（買われ過ぎ・反落注意）")
    elif rsi <= 30:
        sub.append(70); reasons.append(f"RSI {rsi:.0f}（売られ過ぎ・反発期待）")
    else:
        sub.append(50 + (rsi - 50) * 0.4)
        reasons.append(f"RSI {rsi:.0f}（中立圏）")

    # --- MACD（勢いの転換） ---
    hist = _macd_hist(close)
    if hist > 0:
        sub.append(68); reasons.append("MACDがプラス（上向きの勢い）")
    else:
        sub.append(38); reasons.append("MACDがマイナス（下向きの勢い）")

    score = float(np.clip(np.mean(sub), 0, 100))
    return score, reasons


def momentum_score(df: pd.DataFrame) -> tuple[float, list[str]]:
    """直近の値動き（リターン）から短期モメンタムを評価 (0..100)。"""
    close = df["Close"]
    reasons: list[str] = []
    r5 = (close.iloc[-1] / close.iloc[-6] - 1) * 100 if len(close) > 6 else 0.0
    r20 = (close.iloc[-1] / close.iloc[-21] - 1) * 100 if len(close) > 21 else 0.0
    # 5日リターンを ±8% で 0..100 にマップ
    score = float(np.clip(50 + r5 * 6.25 + r20 * 1.25, 0, 100))
    reasons.append(f"直近5日 {r5:+.1f}% / 20日 {r20:+.1f}%")
    return score, reasons


def volume_score(df: pd.DataFrame) -> tuple[float, list[str]]:
    """出来高の増加＝注目度。価格上昇を伴う出来高増を高評価 (0..100)。"""
    vol = df["Volume"]
    close = df["Close"]
    reasons: list[str] = []
    avg = vol.rolling(20).mean().iloc[-1]
    if avg <= 0 or np.isnan(avg):
        return 50.0, ["出来高データ不足"]
    ratio = float(vol.iloc[-1] / avg)
    up = close.iloc[-1] >= close.iloc[-2]
    base = np.clip(40 + (ratio - 1) * 40, 0, 100)
    if up:
        base = min(100, base + 8)
        reasons.append(f"出来高が平均の{ratio:.1f}倍＋上昇（注目集まる）")
    else:
        base = max(0, base - 8)
        reasons.append(f"出来高が平均の{ratio:.1f}倍だが下落（売り圧）")
    return float(base), reasons


def value_score(info: dict) -> tuple[float, list[str]]:
    """PER/PBR から割安度を評価 (0..100)。データが無ければ中立。"""
    per = info.get("trailingPE")
    pbr = info.get("priceToBook")
    reasons: list[str] = []
    sub: list[float] = []
    if per and per > 0:
        # PER 8→高評価, 30→低評価
        sub.append(float(np.clip(100 - (per - 8) * 4, 0, 100)))
        reasons.append(f"PER {per:.1f}")
    if pbr and pbr > 0:
        sub.append(float(np.clip(100 - (pbr - 0.8) * 35, 0, 100)))
        reasons.append(f"PBR {pbr:.2f}")
    if not sub:
        return 50.0, ["割安指標データなし（中立）"]
    return float(np.mean(sub)), reasons


def dividend_score(info: dict) -> tuple[float, list[str]]:
    """配当利回りを評価 (0..100)。"""
    dy = info.get("dividendYield")
    if not dy:
        return 40.0, ["配当データなし"]
    # yfinance は 0.03 のような小数 or 3.0 のような % で返ることがあるため吸収
    pct = dy * 100 if dy < 1 else dy
    score = float(np.clip(pct * 18, 0, 100))  # 約5.5%で満点
    return score, [f"配当利回り 約{pct:.1f}%"]
