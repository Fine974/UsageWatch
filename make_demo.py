"""生成一份示例数据，用来在没有真实数据时预览看板效果。

用法：
    python make_demo.py                # 写入今天
    python make_demo.py 2026-11-11     # 写入指定日期
    python make_demo.py --out ./demo   # 写到指定目录（默认 %APPDATA%\\AppUsage）

生成的文件名形如 2026-11-11.jsonl，程序启动后在看板上就能选到这一天。
注意：写入的是数据目录，会与真实采集数据混在一起，看完记得删掉。
"""

import datetime
import json
import os
import sys

# 一天的作息安排：(开始, 结束, 进程名, 窗口标题)
PLAN = [
    ("09:12", "09:38", "Weixin", "微信"),
    ("09:38", "10:21", "idea64", "my-project - IntelliJ IDEA"),
    ("10:21", "10:26", "chrome", "测试用例管理"),
    ("10:26", "11:52", "idea64", "my-project - IntelliJ IDEA"),
    ("11:52", "12:31", "Weixin", "微信"),
    ("12:31", "13:04", "chrome", "Google Chrome"),
    ("13:04", "14:28", "idea64", "my-api - IntelliJ IDEA"),
    ("14:28", "14:34", "DingTalk", "钉钉"),
    ("14:34", "15:47", "idea64", "my-api - IntelliJ IDEA"),
    ("15:47", "15:58", "WindowsTerminal", "Windows Terminal"),
    ("15:58", "16:07", "chrome", "Grafana - 监控面板"),
    ("16:07", "16:11", "WindowsTerminal", "Windows Terminal"),
    ("16:11", "17:18", "idea64", "my-project - IntelliJ IDEA"),
    ("17:18", "17:24", "Weixin", "微信"),
    ("17:24", "18:16", "idea64", "my-project - IntelliJ IDEA"),
    ("18:16", "18:29", "mstsc", "远程桌面"),
    ("18:29", "18:41", "idea64", "my-project - IntelliJ IDEA"),
]

# 零散插曲，模拟真实场景里的频繁切换
NOISE = [
    ("10:44", 38, "chrome", "Google Chrome"),
    ("11:20", 27, "Weixin", "微信"),
    ("13:41", 45, "DingTalk", "钉钉"),
    ("14:59", 33, "chrome", "Google Chrome"),
    ("16:35", 51, "Weixin", "微信"),
    ("17:02", 29, "WindowsTerminal", "Windows Terminal"),
]


def main() -> None:
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    out_dir = os.path.join(os.environ["APPDATA"], "AppUsage")
    for i, a in enumerate(sys.argv):
        if a == "--out" and i + 1 < len(sys.argv):
            out_dir = sys.argv[i + 1]
            args = [x for x in args if x != sys.argv[i + 1]]

    day = datetime.date.fromisoformat(args[0]) if args else datetime.date.today()

    def at(hhmm: str) -> datetime.datetime:
        h, m = map(int, hhmm.split(":"))
        return datetime.datetime.combine(day, datetime.time(h, m))

    rows = [(at(s), at(e), p, w) for s, e, p, w in PLAN]
    for s, dur, p, w in NOISE:
        st = at(s)
        rows.append((st, st + datetime.timedelta(seconds=dur), p, w))
    rows.sort(key=lambda r: r[0])

    lines = [
        json.dumps(
            {
                "t": st.strftime("%Y-%m-%dT%H:%M:%S"),
                "e": en.strftime("%Y-%m-%dT%H:%M:%S"),
                "p": p,
                "w": w,
                "s": round((en - st).total_seconds(), 1),
            },
            ensure_ascii=False,
        )
        for st, en, p, w in rows
    ]

    path = os.path.join(out_dir, f"{day.isoformat()}.jsonl")
    os.makedirs(out_dir, exist_ok=True)
    with open(path, "w", encoding="utf-8") as f:
        f.write("\n".join(lines) + "\n")

    total = sum((en - st).total_seconds() for st, en, _, _ in rows)
    print(f"写入 {len(lines)} 条，总计 {total / 3600:.2f}h -> {path}")


if __name__ == "__main__":
    main()
