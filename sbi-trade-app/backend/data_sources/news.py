"""ニュース取得（無料RSS）.

現在のニュースに加え、過去記事も遡ってキーワード一致を集める。
有料ニュースAPIを足したくなったら fetch_news に分岐を足すだけ。
"""
from __future__ import annotations

import time
from dataclasses import dataclass

try:
    import feedparser
except ImportError:
    feedparser = None


# 無料で取得できる代表的な経済・マーケット系RSS
RSS_FEEDS = [
    "https://news.yahoo.co.jp/rss/categories/business.xml",
    "https://assets.wor.jp/rss/rdf/reuters/business.rdf",
    "https://www.nikkei.com/rss/index.html",
]


@dataclass
class NewsItem:
    title: str
    summary: str
    published: float  # epoch 秒（不明なら 0）
    link: str

    @property
    def text(self) -> str:
        return f"{self.title} {self.summary}"


def _parse_time(entry) -> float:
    for key in ("published_parsed", "updated_parsed"):
        t = getattr(entry, key, None) or (entry.get(key) if hasattr(entry, "get") else None)
        if t:
            try:
                return time.mktime(t)
            except Exception:
                pass
    return 0.0


def fetch_all_news(max_per_feed: int = 60) -> list[NewsItem]:
    """全フィードからまとめて取得。重複タイトルは除く。"""
    if feedparser is None:
        return []
    seen: set[str] = set()
    items: list[NewsItem] = []
    for url in RSS_FEEDS:
        try:
            feed = feedparser.parse(url)
        except Exception:
            continue
        for e in feed.entries[:max_per_feed]:
            title = getattr(e, "title", "") or ""
            if not title or title in seen:
                continue
            seen.add(title)
            items.append(
                NewsItem(
                    title=title,
                    summary=getattr(e, "summary", "") or "",
                    published=_parse_time(e),
                    link=getattr(e, "link", "") or "",
                )
            )
    return items


def match_news(keyword: str, news: list[NewsItem]) -> list[NewsItem]:
    """銘柄キーワード（スペース区切りで複数可）に一致するニュースを返す。"""
    keys = [k for k in keyword.split() if k]
    hits = []
    for n in news:
        if any(k in n.text for k in keys):
            hits.append(n)
    # 新しい順
    hits.sort(key=lambda x: x.published, reverse=True)
    return hits
