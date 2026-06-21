"""1回分の分析パイプライン.

データ取得 → 独自AIで評価 → モード別ランキング → SQLite 保存。
scheduler.py から4時間ごとに呼ばれる。単体実行も可能:

    python pipeline.py --mode short_trade
    python pipeline.py --all
"""
from __future__ import annotations

import argparse
import time
from dataclasses import asdict

from analysis import engine
from config import MODES
from data_sources import market, news
from storage import db
from universe import UNIVERSE, display_name, news_keyword, tickers


def run_mode(mode_key: str, histories=None, all_news=None) -> dict:
    mode = MODES[mode_key]
    if histories is None:
        histories = market.fetch_many(tickers())
    if all_news is None:
        all_news = news.fetch_all_news()

    evals = []
    for tk in tickers():
        ph = histories.get(tk) or market.fetch_history(tk)
        ev = engine.evaluate(ph, display_name(tk), news_keyword(tk), all_news, mode.weights)
        evals.append(ev)

    ranked = engine.rank(evals, mode.min_confidence, mode.top_n)
    payload = {
        "mode": mode_key,
        "mode_label": mode.label,
        "generated_at": time.time(),
        "ranking": [asdict(e) for e in ranked],
        "all_evaluated": len(evals),
    }
    db.save_snapshot(mode_key, payload)
    return payload


def run_all() -> dict[str, dict]:
    """全モードを共通データで一括実行（取得は1回で済ませて効率化）。"""
    histories = market.fetch_many(tickers())
    all_news = news.fetch_all_news()
    out = {}
    for key in MODES:
        out[key] = run_mode(key, histories, all_news)
    return out


def main():
    p = argparse.ArgumentParser(description="SBI Trade Assist 分析パイプライン")
    p.add_argument("--mode", default=None, help="単一モードを実行")
    p.add_argument("--all", action="store_true", help="全モードを実行")
    args = p.parse_args()

    if args.all or not args.mode:
        results = run_all()
        for key, payload in results.items():
            print(f"[{payload['mode_label']}] 推奨 {len(payload['ranking'])} 件 "
                  f"/ 評価 {payload['all_evaluated']} 銘柄")
            for e in payload["ranking"][:5]:
                print(f"  - {e['name']}: スコア{e['score']} 確信度{e['confidence']}")
    else:
        payload = run_mode(args.mode)
        print(f"[{payload['mode_label']}] 推奨 {len(payload['ranking'])} 件")
        for e in payload["ranking"]:
            print(f"  - {e['name']}: スコア{e['score']} 確信度{e['confidence']} {e['action']}")


if __name__ == "__main__":
    main()
