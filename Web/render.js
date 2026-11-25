(function (global) {
  function esc(s) {
    return String(s == null ? '' : s).replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
  }

  function fmt(sec) {
    if (sec < 60) return Math.round(sec) + 's';
    const h = Math.floor(sec / 3600), m = Math.round((sec % 3600) / 60);
    return h > 0 ? h + 'h ' + String(m).padStart(2, '0') + 'm' : m + 'm';
  }

  function hhmm(sec) {
    const h = Math.floor(sec / 3600) % 24, m = Math.floor(sec % 3600 / 60);
    return String(h).padStart(2, '0') + ':' + String(m).padStart(2, '0');
  }

  const isDark = () => document.documentElement.dataset.theme === 'dark';

  // 后端返回的分类色是按深色底调的，浅色底上要压暗，否则浅绿浅粉看不清
  function hex2hsl(hex) {
    const r = parseInt(hex.slice(1, 3), 16) / 255, g = parseInt(hex.slice(3, 5), 16) / 255, b = parseInt(hex.slice(5, 7), 16) / 255;
    const mx = Math.max(r, g, b), mn = Math.min(r, g, b), l = (mx + mn) / 2;
    let h = 0, s = 0;
    if (mx !== mn) {
      const d = mx - mn;
      s = l > 0.5 ? d / (2 - mx - mn) : d / (mx + mn);
      if (mx === r) h = (g - b) / d + (g < b ? 6 : 0);
      else if (mx === g) h = (b - r) / d + 2;
      else h = (r - g) / d + 4;
      h *= 60;
    }
    return [h, s * 100, l * 100];
  }

  function hsl2hex(h, s, l) {
    s /= 100; l /= 100;
    const k = n => (n + h / 30) % 12;
    const a = s * Math.min(l, 1 - l);
    const f = n => l - a * Math.max(-1, Math.min(k(n) - 3, Math.min(9 - k(n), 1)));
    const to = x => Math.round(255 * x).toString(16).padStart(2, '0');
    return '#' + to(f(0)) + to(f(8)) + to(f(4));
  }

  function adapt(hex) {
    if (!hex || hex.charAt(0) !== '#' || hex.length !== 7) return hex;
    if (isDark()) return hex;
    const [h, s, l] = hex2hsl(hex);
    return hsl2hex(h, Math.min(100, s * 1.06), Math.min(l, 45));
  }

  function pal() {
    return isDark()
      ? { grid: '#1f2634', axis: '#59627a', muted: '#8b94a7', track: '#161d29', text: '#e8ecf3', accent: '#4f9cf9' }
      : { grid: '#e7eaf0', axis: '#98a1b0', muted: '#5f6a7d', track: '#eef1f5', text: '#161b24', accent: '#2f6fd0' };
  }

  // 个人高效时段：近 30 天里每小时编码秒数中位最高的几个钟头。
  // 不看这个就永远不知道「自己几点最有效率」，只能凭感觉
  function productiveHours(B) {
    const set = new Set();
    if (!B || !B.hourCodeMed || !B.hourCodeMed.length) return set;
    B.hourCodeMed
      .map((v, i) => ({ i, v }))
      .filter(x => x.v > 0)
      .sort((a, b) => b.v - a.v)
      .slice(0, 5)
      .forEach(x => set.add(x.i));
    return set;
  }

  function ticks(t0, t1, width, labelW, P) {
    const hours = (t1 - t0) / 3600;
    const step = hours <= 8 ? 1 : hours <= 14 ? 2 : 3;
    let out = '';
    for (let h = Math.ceil(t0 / 3600); h <= Math.floor(t1 / 3600); h++) {
      if (h % step !== 0) continue;
      const x = labelW + (h * 3600 - t0) / (t1 - t0) * (width - labelW);
      out += `<line x1="${x}" y1="0" x2="${x}" y2="100%" stroke="${P.grid}" stroke-width="1"/>`;
      out += `<text x="${x}" y="100%" dy="14" fill="${P.axis}" font-size="10" text-anchor="middle">${String(h).padStart(2, '0')}:00</text>`;
    }
    return out;
  }

  function el(id) { return document.getElementById(id); }

  let destView = 'site';
  const openSites = new Set();
  let lastDay = null;
  let lastBase = null;
  let lastWeek = null;

  function renderDashboard(D, B) {
    if (!D || D.empty) {
      const box = el('dash');
      if (box) box.innerHTML = '<div class="empty">这一天还没有记录。</div>';
      return;
    }
    lastDay = D;
    lastBase = B || null;

    el('hd-date').textContent = D.date;
    el('hd-week').textContent = D.weekday;
    el('k-total').textContent = fmt(D.totalSec);
    el('k-cover').textContent = D.coverage.toFixed(0) + '%';
    el('k-switch').textContent = D.switches;
    el('k-focus').textContent = D.focus && D.focus.d > 0 ? fmt(D.focus.d) : '-';
    el('k-deep').textContent = D.deepCount > 0 ? D.deepCount + ' 段' : '0';
    el('k-stay').textContent = D.medianStay > 0 ? Math.round(D.medianStay) + 's' : '-';
    el('k-span').textContent = hhmm(D.firstSec) + '–' + hhmm(D.lastSec);

    renderDelta(D, B);
    renderTrend(D, B);
    renderGroups(D, B);
    renderTimeline(D, B);
    renderDest(D);
    renderHourly(D);
    renderGantt(D);
    renderNotes(D, B);
    renderUnknown(D);
  }

  function renderDelta(D, B) {
    const box = el('k-delta');
    if (!B || !B.medTotal) { box.innerHTML = ''; return; }
    const diff = D.totalSec - B.medTotal;
    const up = diff >= 0;
    box.innerHTML = `<span class="${up ? 'up' : 'down'}">${up ? '▲' : '▼'} ${fmt(Math.abs(diff))}</span>`
      + ` ${up ? '高于' : '低于'}近 30 天中位 ${fmt(B.medTotal)}`;
  }

  // 近 30 天柱状，当前查看日高亮。比"绝对数字"更快看出今天在什么水平
  function renderTrend(D, B) {
    const box = el('trend');
    if (!B || !B.recent || !B.recent.length) { box.innerHTML = ''; return; }
    const key = D.date.slice(5);
    let hit = false;
    const list = B.recent.slice(-30).map(x => {
      const hi = x.d === key;
      if (hi) hit = true;
      return { d: x.d, sec: x.sec, hi };
    });
    if (!hit) list.push({ d: key, sec: D.totalSec, hi: true });

    const max = Math.max(...list.map(x => x.sec), 1);
    box.innerHTML = list.map(x =>
      `<div class="tcol${x.hi ? ' hi' : ''}" style="height:${Math.max(3, x.sec / max * 100).toFixed(1)}%"`
      + ` data-tip="${esc(x.d)} · ${fmt(x.sec)}"></div>`
    ).join('');
  }

  function renderGroups(D, B) {
    const box = el('groups');
    const data = (D.groups || []).slice(0, 6);
    if (!data.length) { box.innerHTML = '<div class="empty">没有数据</div>'; return; }

    const max = Math.max(...data.map(g => g.pct), 1);
    box.innerHTML = data.map(g => {
      const base = B && B.groupPct ? B.groupPct[g.n] : undefined;
      const bx = base != null ? Math.min(100, base / max * 100) : null;
      const tip = base != null ? `${g.n} ${g.pct.toFixed(1)}% · 中位 ${base.toFixed(1)}%` : `${g.n} ${g.pct.toFixed(1)}%`;
      return `<div class="grow" data-tip="${esc(tip)}">
        <div class="gname">${esc(g.n)}</div>
        <div class="gtrack">
          <div class="gfill" style="width:${(g.pct / max * 100).toFixed(1)}%;background:${adapt(g.c)}"></div>
          ${bx != null ? `<div class="gbase" style="left:${bx.toFixed(1)}%"></div>` : ''}
        </div>
        <div class="gval">${g.pct.toFixed(0)}%</div>
      </div>`;
    }).join('');
  }

  function renderTimeline(D, B) {
    const P = pal();
    const W = 1000, labelW = 58, t0 = D.spanStartSec, t1 = D.spanEndSec, span = t1 - t0 || 1;
    const plotW = W - labelW;
    const hot = productiveHours(B);

    let s = `<svg viewBox="0 0 ${W} 64">`;
    s += ticks(t0, t1, W, labelW, P);

    // 先铺高效时段的底色，再画使用块，块永远压在上面
    for (const h of hot) {
      const a = Math.max(t0, h * 3600), b = Math.min(t1, (h + 1) * 3600);
      if (b <= a) continue;
      const x = labelW + (a - t0) / span * plotW;
      const w = (b - a) / span * plotW;
      s += `<rect x="${x.toFixed(2)}" y="6" width="${w.toFixed(2)}" height="36" fill="${P.accent}" opacity=".10" rx="2"/>`;
    }

    s += `<text x="0" y="24" fill="${P.muted}" font-size="12" dominant-baseline="central">全天</text>`;
    s += `<rect x="${labelW}" y="10" width="${plotW}" height="28" fill="${P.track}" rx="4"/>`;
    for (const g of D.segs) {
      const x = labelW + (g.s - t0) / span * plotW;
      const w = Math.max(0.6, g.d / span * plotW);
      const label = g.w ? g.l + ' · ' + g.w : g.l;
      s += `<rect x="${x.toFixed(2)}" y="10" width="${w.toFixed(2)}" height="28" fill="${adapt(g.c)}" rx="1.5" data-tip="${esc(label)} · ${hhmm(g.s)}–${hhmm(g.s + g.d)} · ${fmt(g.d)}"/>`;
    }
    s += '</svg>';
    el('timeline').innerHTML = s;

    const gaps = (1 - D.totalSec / ((D.lastSec - D.firstSec) || 1)) * 100;
    const hotTxt = hot.size ? ' · 淡蓝为你的高效时段' : '';
    el('tl-note').textContent = '空白 ' + Math.max(0, gaps).toFixed(0) + '% 为离开或锁屏' + hotTxt;
  }

  function destData(D) {
    if (destView === 'group') return (D.groups || []).map(g => ({ n: g.n, sec: g.sec, pct: g.pct, c: g.c }));
    if (destView === 'app') return (D.rows || []).map(r => ({ n: r.l, sec: r.sec, pct: r.pct, c: r.c }));
    return D.sites || [];
  }

  function renderDest(D) {
    const data = destData(D);
    const box = el('dest');
    if (!box) return;

    if (!data.length) { box.innerHTML = '<div class="empty">没有数据</div>'; return; }

    const max = Math.max(...data.map(x => x.sec), 1);
    const limit = destView === 'site' ? 10 : 8;
    let html = '';

    data.slice(0, limit).forEach(x => {
      const expandable = destView === 'site' && x.pages && x.pages.length > 1;
      const open = expandable && openSites.has(x.n);
      const tag = destView === 'site' && x.g ? `<span class="gtag">${esc(x.g)}</span>` : '';
      html += `<div class="rank${expandable ? ' clickable' : ''}${open ? ' open' : ''}" data-n="${esc(x.n)}" data-exp="${expandable ? 1 : 0}">
        <div class="rname" title="${esc(x.n)}">${esc(x.n)}${tag}</div>
        <div class="rtrack"><div class="rfill" style="width:${(x.sec / max * 100).toFixed(1)}%;background:${adapt(x.c)}"></div></div>
        <div class="rval">${fmt(x.sec)}</div>
        <div class="rpct">${x.pct.toFixed(0)}%</div>
      </div>`;

      if (open) {
        html += '<div class="pages">' + x.pages.slice(0, 7).map(p =>
          `<div class="prow"><span>${esc(p.n)}</span><span>${fmt(p.sec)} · ${p.pct.toFixed(0)}%</span></div>`
        ).join('') + '</div>';
      }
    });

    if (data.length > limit) {
      const rest = data.slice(limit);
      const restSec = rest.reduce((a, b) => a + b.sec, 0);
      html += `<div class="rank rest">
        <div class="rname">其余 ${rest.length} 项</div>
        <div class="rtrack"><div class="rfill" style="width:${(restSec / max * 100).toFixed(1)}%;background:${adapt('#8892a6')}"></div></div>
        <div class="rval">${fmt(restSec)}</div>
        <div class="rpct"></div>
      </div>`;
    }

    box.innerHTML = html;

    box.querySelectorAll('.rank.clickable').forEach(row => {
      row.onclick = () => {
        const n = row.dataset.n;
        if (openSites.has(n)) openSites.delete(n); else openSites.add(n);
        if (lastDay) renderDest(lastDay);
      };
    });
  }

  document.addEventListener('click', e => {
    const b = e.target.closest('#dest-tabs b');
    if (!b) return;
    destView = b.dataset.v;
    document.querySelectorAll('#dest-tabs b').forEach(x => x.classList.toggle('on', x === b));
    if (lastDay) renderDest(lastDay);
  });

  function renderHourly(D) {
    const h = D.hourly;
    let lo = h.findIndex(v => v > 0), hi = -1;
    for (let i = h.length - 1; i >= 0; i--) { if (h[i] > 0) { hi = i; break; } }
    if (lo < 0) { el('hourly').innerHTML = ''; return; }

    const max = Math.max(...h, 1);
    let out = '';
    for (let i = lo; i <= hi; i++) {
      const parts = D.hourlyStack.map(s => ({ c: s.c, l: s.l, v: s.v[i] || 0 })).filter(x => x.v > 0);
      let inner = '';
      if (parts.length === 0) {
        inner = `<div class="hbar" style="height:2px;background:${pal().grid}"></div>`;
      } else {
        for (const p of parts) {
          inner += `<div class="hbar" style="height:${(p.v / max * 100).toFixed(2)}%;background:${adapt(p.c)}" data-tip="${String(i).padStart(2, '0')}:00 · ${esc(p.l)} · ${fmt(p.v)}"></div>`;
        }
      }
      out += `<div class="hcol"><div class="hstack">${inner}</div><div class="hlab">${String(i).padStart(2, '0')}</div></div>`;
    }
    el('hourly').innerHTML = out;
  }

  function renderGantt(D) {
    const P = pal();
    const rows = (D.sites || []).slice(0, 8);
    const labelW = 150, W = 1000, rowH = 22, gap = 7;
    const t0 = D.spanStartSec, t1 = D.spanEndSec, span = t1 - t0 || 1;
    const plotW = W - labelW;

    const byLabel = {};
    for (const g of D.segs) { (byLabel[g.l] = byLabel[g.l] || []).push(g); }

    const H = rows.length * (rowH + gap);
    let s = `<svg viewBox="0 0 ${W} ${H + 18}">`;
    s += ticks(t0, t1, W, labelW, P);
    rows.forEach((r, i) => {
      const y = i * (rowH + gap);
      s += `<text x="0" y="${y + rowH / 2}" fill="${P.muted}" font-size="11.5" dominant-baseline="central">${esc(r.n)}</text>`;
      s += `<rect x="${labelW}" y="${y}" width="${plotW}" height="${rowH}" fill="${P.track}" rx="3"/>`;
      for (const g of (byLabel[r.n] || [])) {
        const x = labelW + (g.s - t0) / span * plotW;
        const w = Math.max(1, g.d / span * plotW);
        const label = g.w ? g.l + ' · ' + g.w : g.l;
        s += `<rect x="${x.toFixed(2)}" y="${y}" width="${w.toFixed(2)}" height="${rowH}" fill="${adapt(r.c)}" rx="2" opacity=".92" data-tip="${esc(label)} · ${hhmm(g.s)}–${hhmm(g.s + g.d)} · ${fmt(g.d)}"/>`;
      }
    });
    s += '</svg>';
    el('gantt').innerHTML = s;
  }

  // 值得注意：不做数据复述，只挑「和平时不一样」的地方
  function renderNotes(D, B) {
    const box = el('notes');
    const foot = el('notes-foot');
    const items = [];
    const isFullDay = D.totalSec >= 10800;

    if (B && B.groupPct) {
      for (const g of D.groups) {
        const base = B.groupPct[g.n];
        if (base == null) continue;
        const diff = g.pct - base;
        if (Math.abs(diff) < 8) continue;
        items.push({
          w: 10 + Math.abs(diff),
          cls: diff > 0 ? '' : 'warn',
          t: `${g.n}占 ${g.pct.toFixed(0)}%，比近 30 天中位 ${base.toFixed(0)}% ${diff > 0 ? '高' : '低'} ${Math.abs(diff).toFixed(0)} 个点`,
        });
      }
    }

    const un = (D.groups || []).find(g => g.n === '未归类');
    if (un && un.sec >= 300) {
      items.push({ w: 20, cls: 'warn', t: `有 ${fmt(un.sec)} 落在未归类页面，规则还缺条目` });
    }

    // 专注判断改用分类口径：站点口径会把「编码中切了下文件」也算成中断，
    // 用它下结论会得出「你只有 20 分钟专注」这种错误判断
    if (D.focus && D.focus.d > 0) {
      if (D.focus.d < 1500 && isFullDay) {
        items.push({ w: 9, cls: 'warn', t: `最长专注只有 ${fmt(D.focus.d)}（${D.focus.l}），一整天没有超过 25 分钟的连续块` });
      } else if (D.focus.d >= 2400) {
        items.push({ w: 6, cls: 'ok', t: `最长专注 ${fmt(D.focus.d)}（${D.focus.l} · ${D.focus.sub}），有一段完整推进` });
      }
    }

    if (D.medianStay > 0 && D.shortPct >= 75 && isFullDay) {
      items.push({
        w: 8, cls: 'warn',
        t: `工作中位停留 ${Math.round(D.medianStay)} 秒，${D.shortPct.toFixed(0)}% 的段落短于 1 分钟，每小时切换约 ${Math.round(D.switchPerHour)} 次`,
      });
    }

    if (B && B.medCoverage) {
      if (D.coverage < B.medCoverage - 12) {
        items.push({ w: 7, cls: 'warn', t: `覆盖率 ${D.coverage.toFixed(0)}%，低于中位 ${B.medCoverage.toFixed(0)}%，离开时间偏多` });
      }
      if (B.medSwitches && D.switches > B.medSwitches * 1.6) {
        items.push({ w: 5, t: `切换 ${D.switches} 次，明显多于中位 ${Math.round(B.medSwitches)} 次` });
      }
    }

    if (!items.length && D.insights && D.insights.length) {
      // 没有异常时才退回后端解读。后端每条带权重，排序取前三，不再靠数组下标猜
      D.insights.slice().sort((a, b) => b.w - a.w).slice(0, 3)
        .forEach(x => items.push({ w: 0, cls: '', t: x.t }));
    }

    const top = items.sort((a, b) => b.w - a.w).slice(0, 3);
    box.innerHTML = top.length
      ? top.map(x => `<li class="${x.cls}">${esc(x.t)}</li>`).join('')
      : '<li class="none">今天各项指标都在基线附近，没有异常。</li>';

    let extra = '';
    if (D.focus && D.focus.sd > 0 && D.focus.sub) {
      extra = ` · 完全没切换的最长段 ${fmt(D.focus.sd)}（${D.focus.sl}）`;
    }
    foot.textContent = (B
      ? `对比基准：近 30 天中位，参与统计 ${B.days} 个工作日`
      : '静态报告，不含基线对比') + extra;
  }

  // 未归类：只负责「信号 + 入口」。明细列表和归类操作都在 /rules 配置页，
  // 这里再列一遍没有意义 —— 它原本唯一的交互是「复制规则粘进 JSON」，那套流程已经作废了。
  function renderUnknown(D) {
    const card = el('unknown-card');
    const box = el('unknown');
    const sum = el('unknown-sum');
    if (!card || !box) return;

    const list = (D.unknown || []).filter(x => x.sec >= 30);
    if (!list.length) { card.style.display = 'none'; return; }

    card.style.display = '';
    const total = list.reduce((a, x) => a + x.sec, 0);
    if (sum) sum.textContent = `${fmt(total)} · ${list.length} 个页面`;

    // 只给前三个页面名：够用来认出「是不是又是我没想到的那个页面」，
    // 再多就是噪音了 —— 1% 量级的问题不该在主页上铺开成一张表
    const names = list.slice(0, 3).map(x => x.n);
    box.textContent = names.join(' · ') + (list.length > 3 ? ` 等 ${list.length} 个` : '');
    box.title = list.map(x => `${x.n}  ${fmt(x.sec)}`).join('\n');
  }

  // ---------- 周视图 ----------
  // 目标：一眼回答「这周比上周怎么样、哪天最猛、结构变了没有」
  function renderWeek(W, B) {
    if (!W || !W.days) return;
    lastWeek = W;
    const today = new Date().toLocaleDateString('sv');

    el('wk-label').textContent = '本周 ' + W.label;
    el('wk-range').textContent = W.start + ' → ' + W.end;
    el('wk-total').textContent = fmt(W.totalSec);
    el('wk-avg').textContent = W.avgSec > 0 ? fmt(W.avgSec) : '-';
    el('wk-daycount').textContent = W.recorded;
    el('wk-deep').textContent = W.deepCount > 0 ? W.deepCount + ' 段 · ' + fmt(W.deepSec) : '0';
    el('wk-stay').textContent = W.medStay > 0 ? Math.round(W.medStay) + 's' : '-';

    const dl = el('wk-delta');
    if (W.prevRecorded > 0 && W.prevAvgSec > 0) {
      const up = W.deltaPct >= 0;
      dl.innerHTML = `<span class="${up ? 'up' : 'down'}">${up ? '▲' : '▼'} ${Math.abs(W.deltaPct).toFixed(0)}%</span>`
        + ` 日均，上周 ${fmt(W.prevAvgSec)}（${W.prevRecorded} 个工作日）`;
    } else {
      dl.innerHTML = '<span style="color:var(--dim)">上周数据不足以对比</span>';
    }

    // 7 根柱：高度用 px 算，百分比在 flex 列里会被标签挤掉
    const max = Math.max(...W.days.map(d => d.sec), 1);
    el('wk-bars').innerHTML = W.days.map(d => {
      const isToday = d.d === today;
      const h = d.sec > 0 ? Math.max(3, Math.round(d.sec / max * 46)) : 2;
      const cls = d.sec <= 0 ? 'wkbar none' : isToday ? 'wkbar hi' : 'wkbar';
      const tip = d.sec > 0
        ? `${d.weekday} ${d.d.slice(5)} · ${fmt(d.sec)}${isToday ? ' · 今天' : ''}`
        : `${d.weekday} ${d.d.slice(5)} · ${d.d > today ? '还没到' : '无记录'}`;
      return `<div class="wkcol clickable" data-d="${d.d}" data-tip="${esc(tip)}">
        <div class="${cls}" style="height:${h}px"></div>
        <div class="wklab${isToday ? ' hi' : ''}">${d.weekday.slice(1)}</div>
      </div>`;
    }).join('');

    // 分类结构：本周条形 + 上周位置竖线
    const gs = (W.groups || []).slice(0, 6);
    const gmax = Math.max(...gs.map(g => g.pct), 1);
    el('wk-groups').innerHTML = gs.length ? gs.map(g => {
      const bx = g.hasPrev ? Math.min(100, g.pPct / gmax * 100) : null;
      const diff = g.hasPrev ? g.pct - g.pPct : null;
      const tip = g.hasPrev
        ? `${g.n} ${g.pct.toFixed(1)}% · 上周 ${g.pPct.toFixed(1)}%`
        : `${g.n} ${g.pct.toFixed(1)}% · 上周无记录`;
      const dt = diff != null && Math.abs(diff) >= 1.5 ? ` <i>${diff > 0 ? '+' : ''}${diff.toFixed(0)}</i>` : '';
      return `<div class="grow" data-tip="${esc(tip)}">
        <div class="gname">${esc(g.n)}</div>
        <div class="gtrack">
          <div class="gfill" style="width:${(g.pct / gmax * 100).toFixed(1)}%;background:${adapt(g.c)}"></div>
          ${bx != null ? `<div class="gbase" style="left:${bx.toFixed(1)}%"></div>` : ''}
        </div>
        <div class="gval">${g.pct.toFixed(0)}%${dt}</div>
      </div>`;
    }).join('') : '<div class="empty">没有数据</div>';

    // 逐日：条长按本周最长的一天归一，同一天之间才能横向比
    const dmax = Math.max(...W.days.map(d => d.sec), 1);
    el('wk-days-list').innerHTML = W.days.map(d => {
      if (d.empty) {
        const why = d.d > today ? '还没到' : '无记录';
        return `<div class="wkrow">
          <div class="wkdow">${d.weekday}</div>
          <div class="wkdate">${d.d.slice(5)}</div>
          <div class="wktrack"></div>
          <div class="wksec" style="color:var(--dim)">${why}</div>
          <div class="wkmeta"></div>
        </div>`;
      }
      const meta = `覆盖 ${d.coverage.toFixed(0)}% · 专注 ${fmt(d.focusSec)} · 深度 ${d.deepCount}`;
      return `<div class="wkrow clickable" data-d="${d.d}">
        <div class="wkdow">${d.weekday}</div>
        <div class="wkdate">${d.d.slice(5)}</div>
        <div class="wktrack"><div class="wkfill" style="width:${(d.sec / dmax * 100).toFixed(1)}%;background:${adapt(d.topc || '#8892a6')}"></div></div>
        <div class="wksec">${fmt(d.sec)}</div>
        <div class="wkmeta">${esc(meta)}</div>
      </div>`;
    }).join('');

    const sites = W.sites || [];
    if (sites.length) {
      const smax = Math.max(...sites.map(s => s.sec), 1);
      el('wk-sites').innerHTML = sites.slice(0, 8).map(s => `<div class="rank">
        <div class="rname" title="${esc(s.n)}">${esc(s.n)}<span class="gtag">${esc(s.g)}</span></div>
        <div class="rtrack"><div class="rfill" style="width:${(s.sec / smax * 100).toFixed(1)}%;background:${adapt(s.c)}"></div></div>
        <div class="rval">${fmt(s.sec)}</div>
        <div class="rpct">${s.pct.toFixed(0)}%</div>
      </div>`).join('');
    } else {
      el('wk-sites').innerHTML = '<div class="empty">没有数据</div>';
    }

    const notes = W.notes || [];
    el('wk-notes').innerHTML = notes.length
      ? notes.map(t => `<li>${esc(t)}</li>`).join('')
      : '<li class="none">本周没有特别值得注意的地方。</li>';
    el('wk-foot').textContent = '日均只统计 3 小时以上的完整工作日，半天会把平均数拉低';

    // 点柱 / 点行 → 回日视图看那天
    document.querySelectorAll('#wk-bars .wkcol, #wk-days-list .wkrow.clickable').forEach(x => {
      x.onclick = () => { if (global.uwGoto) global.uwGoto(x.dataset.d); };
    });
  }

  const tip = () => document.getElementById('tip');

  document.addEventListener('mouseover', e => {
    const t = e.target.closest('[data-tip]');
    const box = tip();
    if (!box) return;
    if (!t) { box.style.display = 'none'; return; }
    box.textContent = t.dataset.tip;
    box.style.display = 'block';
  });

  document.addEventListener('mousemove', e => {
    const box = tip();
    if (!box || box.style.display !== 'block') return;
    box.style.left = Math.min(e.clientX + 14, window.innerWidth - 280) + 'px';
    box.style.top = (e.clientY + 16) + 'px';
  });

  global.renderDashboard = renderDashboard;
  global.renderWeek = renderWeek;
  global.setDestView = function (v) {
    if (v !== 'site' && v !== 'group' && v !== 'app') return;
    destView = v;
    document.querySelectorAll('#dest-tabs b').forEach(x => x.classList.toggle('on', x.dataset.v === v));
    if (lastDay) renderDest(lastDay);
  };
  global.ufmt = fmt;
  global.uhhmm = hhmm;

  // 主题切换放在这里而不是实时分支里：静态日报也复用同一套脚本，同样要能切
  (function initTheme() {
    const btn = document.getElementById('btn-theme');
    if (!btn) return;
    const SUN = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><circle cx="12" cy="12" r="4.2"/><path d="M12 2.4v2.2M12 19.4v2.2M4.6 4.6l1.6 1.6M17.8 17.8l1.6 1.6M2.4 12h2.2M19.4 12h2.2M4.6 19.4l1.6-1.6M17.8 6.2l1.6-1.6"/></svg>';
    const MOON = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M20 14.5A8.2 8.2 0 0 1 9.5 4a8.5 8.5 0 1 0 10.5 10.5z"/></svg>';
    const paint = () => { btn.innerHTML = isDark() ? SUN : MOON; };
    btn.onclick = () => {
      const dark = isDark();
      if (dark) delete document.documentElement.dataset.theme;
      else document.documentElement.dataset.theme = 'dark';
      try { localStorage.setItem('uw-theme', dark ? 'light' : 'dark'); } catch (e) { }
      paint();
      if (lastDay) renderDashboard(lastDay, lastBase);
      if (lastWeek) renderWeek(lastWeek, lastBase);
    };
    paint();
  })();
})(window);
