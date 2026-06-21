"""4時間ごとに全モードを再分析するスケジューラ.

クラウドVPS等で常時稼働させる:

    python scheduler.py

起動直後に1回走り、その後 REFRESH_HOURS ごとに繰り返す。
"""
from __future__ import annotations

import logging
import time

from apscheduler.schedulers.blocking import BlockingScheduler

from config import REFRESH_HOURS
import pipeline

logging.basicConfig(level=logging.INFO, format="%(asctime)s %(message)s")
log = logging.getLogger("scheduler")


def job():
    start = time.time()
    log.info("分析開始（全モード）")
    try:
        results = pipeline.run_all()
        for key, payload in results.items():
            log.info("  %s: 推奨 %d 件", payload["mode_label"], len(payload["ranking"]))
        log.info("分析完了（%.1f 秒）", time.time() - start)
    except Exception as e:  # 1回失敗しても次の周期で再挑戦
        log.exception("分析中にエラー: %s", e)


def main():
    job()  # 起動直後に1回
    sched = BlockingScheduler(timezone="Asia/Tokyo")
    sched.add_job(job, "interval", hours=REFRESH_HOURS, id="refresh")
    log.info("%d時間ごとに自動更新します。Ctrl+C で停止。", REFRESH_HOURS)
    try:
        sched.start()
    except (KeyboardInterrupt, SystemExit):
        log.info("停止しました")


if __name__ == "__main__":
    main()
