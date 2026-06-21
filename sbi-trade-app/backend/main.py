"""FastAPI：PWA へ分析結果を配信する API.

  GET /api/modes              … モード一覧
  GET /api/ranking?mode=...   … 最新ランキング（無ければその場で生成）
  POST /api/refresh?mode=...  … 手動で再分析
"""
from __future__ import annotations

from fastapi import FastAPI, Query
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import JSONResponse

from config import DEFAULT_MODE, MODES
from storage import db
import pipeline

app = FastAPI(title="SBI Trade Assist")

# 自分専用なので全許可。公開しないこと。
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_methods=["*"],
    allow_headers=["*"],
)


@app.get("/api/modes")
def get_modes():
    return [
        {"key": m.key, "label": m.label, "description": m.description,
         "min_confidence": m.min_confidence}
        for m in MODES.values()
    ]


@app.get("/api/ranking")
def get_ranking(mode: str = Query(DEFAULT_MODE)):
    if mode not in MODES:
        return JSONResponse({"error": f"unknown mode: {mode}"}, status_code=400)
    snap = db.latest_snapshot(mode)
    if snap is None:
        snap = pipeline.run_mode(mode)
    return snap


@app.post("/api/refresh")
def refresh(mode: str = Query(DEFAULT_MODE)):
    if mode not in MODES:
        return JSONResponse({"error": f"unknown mode: {mode}"}, status_code=400)
    return pipeline.run_mode(mode)


@app.get("/api/health")
def health():
    return {"status": "ok"}
