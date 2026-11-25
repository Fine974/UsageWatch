// 渲染冒烟测试：用 node 造一个最小 DOM，跑真实的 render.js，验证各种情况不报错。
// 需要程序正在运行（先双击 usagewatch.exe）。
//
// 用法：node tools/verify-render.js
//
// 为什么不放浏览器里测：这个项目没有前端构建链，也没有端到端测试环境，
// 用 DOM 桩能覆盖「渲染函数是否抛错、关键节点是否产出内容」这类问题，
// 成本极低，适合每次改 render.js 后跑一遍。

const fs = require('fs');
const path = require('path');
const http = require('http');
const os = require('os');

function get(url) {
  return new Promise((res, rej) => {
    http.get(url, r => {
      let b = '';
      r.on('data', c => b += c);
      r.on('end', () => res(b));
    }).on('error', rej);
  });
}

function mk(id) {
  const o = { id, textContent: '', innerHTML: '', style: {}, dataset: {}, children: [] };
  o.classList = { add() { }, remove() { }, toggle() { }, contains() { return false; } };
  o.querySelectorAll = () => [];
  o.setAttribute = () => { };
  return o;
}

// 与 Web/index.html 里的 id 保持一致；render.js 取不到的节点会走空值分支，
// 这里列全是为了让断言有意义。
const ids = ['dash', 'hd-date', 'hd-week', 'k-total', 'k-delta', 'k-cover', 'k-switch', 'k-focus',
  'k-deep', 'k-stay', 'k-span',
  'trend', 'groups', 'timeline', 'tl-note', 'dest', 'notes', 'notes-foot', 'hourly', 'gantt', 'tip',
  'unknown-card', 'unknown', 'unknown-sum',
  'weekdash', 'wk-label', 'wk-range', 'wk-total', 'wk-delta', 'wk-avg', 'wk-daycount', 'wk-deep',
  'wk-stay', 'wk-bars', 'wk-groups', 'wk-days-list', 'wk-sites', 'wk-notes', 'wk-foot',
  'clock', 'lv-label', 'lv-title', 'lv-timer', 'state-text', 'dot', 'btn-pause', 'btn-theme',
  'dates', 'datepick', 'viewtabs', 'wk-prev', 'wk-next'];
const els = {};
ids.forEach(i => els[i] = mk(i));

global.document = {
  getElementById: i => els[i] || null,
  addEventListener() { },
  querySelectorAll: () => [],
  documentElement: { dataset: {} },
};
global.window = {};
global.localStorage = { getItem() { return null; }, setItem() { } };

eval(fs.readFileSync(path.join(__dirname, '..', 'Web', 'render.js'), 'utf8'));

function port() {
  const p = path.join(process.env.APPDATA || os.homedir(), 'AppUsage', 'port.txt');
  return fs.existsSync(p) ? fs.readFileSync(p, 'utf8').trim() : '5279';
}

const strip = s => String(s || '').replace(/<[^>]+>/g, ' | ');
const count = (s, re) => (String(s || '').match(re) || []).length;

(async () => {
  const base = 'http://127.0.0.1:' + port();

  // 取最近一个有数据的日期。/api/dates 是倒序返回，所以取第一个而不是最后一个
  const dates = JSON.parse(await get(base + '/api/dates'));
  if (!dates.length) throw new Error('没有可用数据，先用 make_demo.py 造一天或等采集');
  const day = dates[0];

  const D = JSON.parse(await get(base + '/api/day?d=' + day));
  const B = JSON.parse(await get(base + '/api/baseline'));
  const W = JSON.parse(await get(base + '/api/week?d=' + day));
  console.log('测试日期:', day, '| 数据日期数:', dates.length);

  for (const theme of ['light', 'dark']) {
    if (theme === 'dark') document.documentElement.dataset.theme = 'dark';
    else delete document.documentElement.dataset.theme;

    window.renderDashboard(D, B);
    window.renderWeek(W, B);
    console.log('\n=== ' + theme + ' ===');
    console.log('日 · 主数字:', els['k-total'].textContent, '| 日期:', els['hd-date'].textContent, els['hd-week'].textContent);
    console.log('日 · 覆盖/切换/专注/深度/停留/跨度:',
      els['k-cover'].textContent, els['k-switch'].textContent, els['k-focus'].textContent,
      els['k-deep'].textContent, els['k-stay'].textContent, els['k-span'].textContent);
    console.log('日 · 分类行数:', count(els['groups'].innerHTML, /class="grow"/g),
      '| 含基线刻度:', count(els['groups'].innerHTML, /class="gbase"/g));
    console.log('日 · 趋势柱数:', count(els['trend'].innerHTML, /class="tcol/g),
      '| 高亮:', count(els['trend'].innerHTML, /tcol hi/g));
    console.log('日 · 去处行数:', count(els['dest'].innerHTML, /class="rank/g));
    console.log('日 · 每小时列:', count(els['hourly'].innerHTML, /class="hcol"/g),
      '| 甘特长度:', els['gantt'].innerHTML.length);
    console.log('日 · 高效时段底色块:', count(els['timeline'].innerHTML, /opacity=".10"/g),
      '| 时间轴说明:', els['tl-note'].textContent);
    console.log('日 · 未归类卡:', els['unknown-card'].style.display === 'none' ? '隐藏' : '显示',
      '| 摘要:', els['unknown-sum'].textContent, '| 页面:', els['unknown'].textContent);
    console.log('周 · 标题:', els['wk-label'].textContent, els['wk-range'].textContent);
    console.log('周 · 合计/日均/工作日/深度/停留:', els['wk-total'].textContent, els['wk-avg'].textContent,
      els['wk-daycount'].textContent, els['wk-deep'].textContent, els['wk-stay'].textContent);
    console.log('周 · 对比:', strip(els['wk-delta'].innerHTML).slice(0, 90));
    console.log('周 · 柱数:', count(els['wk-bars'].innerHTML, /class="wkcol/g),
      '| 高亮:', count(els['wk-bars'].innerHTML, /wkbar hi/g));
    console.log('周 · 分类行:', count(els['wk-groups'].innerHTML, /class="grow"/g),
      '| 上周刻度:', count(els['wk-groups'].innerHTML, /class="gbase"/g));
    console.log('周 · 逐日行:', count(els['wk-days-list'].innerHTML, /class="wkrow/g),
      '| 站点行:', count(els['wk-sites'].innerHTML, /class="rank/g),
      '| 要点:', count(els['wk-notes'].innerHTML, /<li/g));
    if (theme === 'light') {
      console.log('日 · 值得注意:', strip(els['notes'].innerHTML).slice(0, 300));
      console.log('日 · 脚注:', els['notes-foot'].textContent);
      console.log('周 · 要点:', strip(els['wk-notes'].innerHTML).slice(0, 300));
    }
  }

  // 没有基线时（静态报告）不能崩
  window.renderDashboard(D);
  window.renderWeek(W);
  console.log('\n=== 无基线降级 ===');
  console.log('对比:', JSON.stringify(els['k-delta'].innerHTML), '| 趋势:', JSON.stringify(els['trend'].innerHTML).slice(0, 40));
  console.log('日 · 值得注意:', strip(els['notes'].innerHTML).slice(0, 160));
  console.log('周 · 对比:', strip(els['wk-delta'].innerHTML).slice(0, 60));

  // 空日
  window.renderDashboard({ empty: true });
  console.log('\n=== 空日 ===');
  console.log('dash:', els['dash'].innerHTML.slice(0, 60));

  // 空周（没有记录的一周）
  window.renderWeek({ label: '—', start: '', end: '', days: [], totalSec: 0, avgSec: 0, recorded: 0, groups: [], sites: [], notes: [] });
  console.log('空周柱数:', count(els['wk-bars'].innerHTML, /wkcol/g), '| 空周要点:', count(els['wk-notes'].innerHTML, /<li/g));

  console.log('\n全部通过');
})().catch(e => { console.error('失败:', e.message); process.exit(1); });
