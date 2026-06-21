"""SQLite 保存（分析結果のスナップショットを履歴として残す）."""
from __future__ import annotations

import json
import sqlite3
import time
from contextlib import contextmanager

from config import DB_PATH


@contextmanager
def _conn(path: str = DB_PATH):
    con = sqlite3.connect(path)
    con.row_factory = sqlite3.Row
    try:
        yield con
        con.commit()
    finally:
        con.close()


def init(path: str = DB_PATH) -> None:
    with _conn(path) as con:
        con.execute(
            """
            CREATE TABLE IF NOT EXISTS snapshots (
                id        INTEGER PRIMARY KEY AUTOINCREMENT,
                mode      TEXT NOT NULL,
                created   REAL NOT NULL,
                payload   TEXT NOT NULL
            )
            """
        )
        con.execute("CREATE INDEX IF NOT EXISTS idx_mode_created ON snapshots(mode, created)")


def save_snapshot(mode: str, payload: dict, path: str = DB_PATH) -> None:
    init(path)
    with _conn(path) as con:
        con.execute(
            "INSERT INTO snapshots(mode, created, payload) VALUES (?,?,?)",
            (mode, time.time(), json.dumps(payload, ensure_ascii=False)),
        )


def latest_snapshot(mode: str, path: str = DB_PATH) -> dict | None:
    init(path)
    with _conn(path) as con:
        row = con.execute(
            "SELECT payload, created FROM snapshots WHERE mode=? ORDER BY created DESC LIMIT 1",
            (mode,),
        ).fetchone()
    if not row:
        return None
    data = json.loads(row["payload"])
    data["created"] = row["created"]
    return data
