// 渲染冒烟测试：用 node 造一个最小 DOM，跑真实的 render.js，验证四种情况不报错。
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
const ids = ['dash', 'hd-date', 'hd-week', 'k-total', 'k-delta', 'k-cover', 'k-switch', 'k-focus', 'k-span',
  'trend', 'groups', 'timeline', 'tl-note', 'dest', 'notes', 'notes-foot', 'hourly', 'gantt', 'tip',
  'clock', 'lv-label', 'lv-title', 'lv-timer', 'state-text', 'dot', 'btn-pause', 'btn-theme', 'dates', 'datepick'];
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

(async () => {
  const base = 'http://127.0.0.1:' + port();

  // 取最近一个有数据的日期，避免写死某天导致脚本失效
  const dates = JSON.parse(await get(base + '/api/dates'));
  if (!dates.length) throw new Error('没有可用数据，先用 make_demo.py 造一天或等采集');
  const day = dates[dates.length - 1];

  const D = JSON.parse(await get(base + '/api/day?d=' + day));
  const B = JSON.parse(await get(base + '/api/baseline'));
  console.log('测试日期:', day);

  for (const theme of ['light', 'dark']) {
    if (theme === 'dark') document.documentElement.dataset.theme = 'dark';
    else delete document.documentElement.dataset.theme;

    window.renderDashboard(D, B);
    console.log('\n=== ' + theme + ' ===');
    console.log('主数字:', els['k-total'].textContent, '| 日期:', els['hd-date'].textContent, els['hd-week'].textContent);
    console.log('对比:', els['k-delta'].innerHTML);
    console.log('覆盖/切换/专注/跨度:', els['k-cover'].textContent, els['k-switch'].textContent, els['k-focus'].textContent, els['k-span'].textContent);
    console.log('分类行数:', (els['groups'].innerHTML.match(/class="grow"/g) || []).length,
      '| 含基线刻度:', (els['groups'].innerHTML.match(/class="gbase"/g) || []).length);
    console.log('趋势柱数:', (els['trend'].innerHTML.match(/class="tcol/g) || []).length,
      '| 高亮:', (els['trend'].innerHTML.match(/tcol hi/g) || []).length);
    console.log('去处行数:', (els['dest'].innerHTML.match(/class="rank/g) || []).length);
    console.log('每小时列:', (els['hourly'].innerHTML.match(/class="hcol"/g) || []).length,
      '| 甘特长度:', els['gantt'].innerHTML.length);
    const g1 = (els['groups'].innerHTML.match(/background:(#[0-9a-f]{6})/i) || [])[1];
    console.log('主分类色:', g1);
    if (theme === 'light') {
      console.log('值得注意:', els['notes'].innerHTML.replace(/<[^>]+>/g, ' | ').slice(0, 260));
      console.log('脚注:', els['notes-foot'].textContent);
    }
  }

  // 没有基线时（静态报告）不能崩
  window.renderDashboard(D);
  console.log('\n=== 无基线降级 ===');
  console.log('对比:', JSON.stringify(els['k-delta'].innerHTML), '| 趋势:', JSON.stringify(els['trend'].innerHTML).slice(0, 40));
  console.log('值得注意:', els['notes'].innerHTML.replace(/<[^>]+>/g, ' | ').slice(0, 160));
  console.log('脚注:', els['notes-foot'].textContent);

  // 空日
  window.renderDashboard({ empty: true });
  console.log('\n=== 空日 ===');
  console.log('dash:', els['dash'].innerHTML.slice(0, 60));
  console.log('\n全部通过');
})().catch(e => { console.error('失败:', e.message); process.exit(1); });
