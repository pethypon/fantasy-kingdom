"""市場データ取得.

いまは yfinance（遅延データ・無料）。
SBI口座やJ-Quantsが使えるようになったら、この関数の中身だけ差し替えれば
上位の分析エンジンはそのまま動く（インターフェースを固定している）。
"""
from __future__ import annotations

from dataclasses import dataclass

import pandas as pd

try:
    import yfinance as yf
except ImportError:  # オフライン/未インストールでも import 自体は通す
    yf = None


@dataclass
class PriceHistory:
    ticker: str
    df: pd.DataFrame  # columns: Open, High, Low, Close, Volume  index: 日付
    info: dict        # PER, 配当利回り 等のファンダ（取れた範囲で）

    @property
    def ok(self) -> bool:
        return self.df is not None and len(self.df) >= 30

    @property
    def last_close(self) -> float:
        return float(self.df["Close"].iloc[-1]) if self.ok else float("nan")


def fetch_history(ticker: str, period: str = "6mo", interval: str = "1d") -> PriceHistory:
    """過去データを取得。失敗時は空の PriceHistory を返す（落とさない）。"""
    if yf is None:
        return PriceHistory(ticker, pd.DataFrame(), {})
    try:
        t = yf.Ticker(ticker)
        df = t.history(period=period, interval=interval, auto_adjust=False)
        df = df.dropna()
        info = {}
        try:
            raw = t.info or {}
            info = {
                "trailingPE": raw.get("trailingPE"),
                "priceToBook": raw.get("priceToBook"),
                "dividendYield": raw.get("dividendYield"),
                "marketCap": raw.get("marketCap"),
            }
        except Exception:
            info = {}
        return PriceHistory(ticker, df, info)
    except Exception:
        return PriceHistory(ticker, pd.DataFrame(), {})


def fetch_many(tickers: list[str]) -> dict[str, PriceHistory]:
    return {tk: fetch_history(tk) for tk in tickers}
