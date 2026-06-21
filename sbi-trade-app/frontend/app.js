// API のベースURL。スマホから見る場合は PC の IP に書き換える。
// 例: const API_BASE = "http://192.168.1.20:8000";
const API_BASE = `${location.protocol}//${location.hostname}:8000`;

const SIGNAL_LABEL = {
  technical: "テクニカル", momentum: "モメンタム", volume: "出来高",
  sentiment: "ニュース", value: "割安", dividend: "配当",
};

let modes = [];
let currentMode = null;

const $ = (id) => document.getElementById(id);

async function loadModes() {
  try {
    const res = await fetch(`${API_BASE}/api/modes`);
    modes = await res.json();
    currentMode = modes[0]?.key;
    renderModeBar();
    await loadRanking();
  } catch (e) {
    $("status").textContent = "APIに接続できません。backend を起動してください。";
  }
}

function renderModeBar() {
  const bar = $("modeBar");
  bar.innerHTML = "";
  modes.forEach((m) => {
    const btn = document.createElement("button");
    btn.textContent = m.label;
    btn.className = m.key === currentMode ? "active" : "";
    btn.onclick = () => { currentMode = m.key; renderModeBar(); loadRanking(); };
    bar.appendChild(btn);
  });
  const m = modes.find((x) => x.key === currentMode);
  $("modeDesc").textContent = m ? m.description : "";
}

async function loadRanking() {
  $("status").textContent = "分析結果を取得中…";
  $("list").innerHTML = "";
  try {
    const res = await fetch(`${API_BASE}/api/ranking?mode=${currentMode}`);
    const data = await res.json();
    renderRanking(data);
  } catch (e) {
    $("status").textContent = "取得に失敗しました。";
  }
}

function renderRanking(data) {
  const when = data.generated_at
    ? new Date(data.generated_at * 1000).toLocaleString("ja-JP")
    : "—";
  $("status").textContent = `更新: ${when}　/　${data.all_evaluated}銘柄を評価`;

  const list = $("list");
  list.innerHTML = "";
  if (!data.ranking || data.ranking.length === 0) {
    list.innerHTML = `<div class="empty">いまは確信度の高い推奨がありません。<br>
      （根拠が揃っていないため「様子見」が正解の場面です）</div>`;
    return;
  }
  data.ranking.forEach((e, i) => list.appendChild(card(e, i + 1)));
}

function card(e, rank) {
  const el = document.createElement("div");
  el.className = "card";
  const confPct = Math.round(e.confidence * 100);
  const actLabel = { buy_watch: "買い候補", neutral: "中立", avoid: "回避" }[e.action] || e.action;

  const chips = Object.entries(e.signals || {})
    .map(([k, v]) => `<span class="chip">${SIGNAL_LABEL[k] || k} ${v}</span>`)
    .join("");

  const reasons = (e.reasons || [])
    .map((r) => `<li>${escapeHtml(r)}</li>`).join("");

  el.innerHTML = `
    <div class="card-top">
      <div class="rank-name">${rank}. ${escapeHtml(e.name)}
        <small>${e.ticker}${isFinite(e.last_close) ? " / " + Math.round(e.last_close) + "円" : ""}</small>
        <span class="badge ${e.action}">${actLabel}</span>
      </div>
      <div class="score">${e.score}</div>
    </div>
    <div class="conf-row">
      <div class="conf-label"><span>確信度</span><span>${confPct}%（材料${e.news_count}件）</span></div>
      <div class="conf-bar"><div class="conf-fill" style="width:${confPct}%"></div></div>
    </div>
    <div class="signals">${chips}</div>
    <div class="toggle">根拠を表示 ▾</div>
    <ul class="reasons" style="display:none">${reasons}</ul>
  `;
  const toggle = el.querySelector(".toggle");
  const ul = el.querySelector(".reasons");
  toggle.onclick = () => {
    const open = ul.style.display !== "none";
    ul.style.display = open ? "none" : "block";
    toggle.textContent = open ? "根拠を表示 ▾" : "根拠を隠す ▴";
  };
  return el;
}

function escapeHtml(s) {
  return String(s).replace(/[&<>"]/g, (c) =>
    ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" }[c]));
}

$("refreshBtn").onclick = async () => {
  $("status").textContent = "再分析中…（少し時間がかかります）";
  try {
    await fetch(`${API_BASE}/api/refresh?mode=${currentMode}`, { method: "POST" });
  } catch (e) {}
  loadRanking();
};

if ("serviceWorker" in navigator) {
  navigator.serviceWorker.register("service-worker.js").catch(() => {});
}

loadModes();
