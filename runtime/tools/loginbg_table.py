# -*- coding: utf-8 -*-
"""生成《登录背景-复刻期对照表.txt》（桌面），保留已填过的编号。

用法:
    python tools\\loginbg_table.py            # 生成/刷新桌面表（会保留第 5 列已填内容）
    python tools\\loginbg_table.py --sync     # 同时把表里的映射写进 Resources\\loginbg\\map.txt
数据源: GM 后台接口 http://127.0.0.1:21400/api/replay（期号/名称/时间与网页下拉一致）
"""
import argparse
import datetime
import json
import os
import sys
import urllib.request

TOOLS = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(TOOLS)
MAP_FILE = os.path.join(ROOT, "Resources", "loginbg", "map.txt")
DESKTOP = os.path.join(os.path.expanduser("~"), "Desktop", "登录背景-复刻期对照表.txt")
API = "http://127.0.0.1:21400/api/replay"


def _admin_token():
    """调试口口令：优先读 S1\\Config\\Config.json 的 HttpServer.AdminToken，其次环境变量。"""
    try:
        with open(os.path.join(ROOT, "Config", "Config.json"), encoding="utf-8") as fh:
            value = (json.load(fh).get("HttpServer", {}).get("AdminToken") or "").strip()
        if value:
            return value
    except Exception:
        pass
    return os.environ.get("MIKUSB_ADMIN_TOKEN", "dev-admin-token")


TOKEN = _admin_token()

sys.path.insert(0, TOOLS)
import loginbg as L  # noqa: E402


def fetch():
    req = urllib.request.Request(API, headers={"Authorization": "Bearer " + TOKEN})
    with urllib.request.urlopen(req, timeout=8) as r:
        return json.loads(r.read().decode("utf-8"))


def parse_filled(path):
    """读旧表，取出 期号 -> 第5列。"""
    got = {}
    if not os.path.exists(path):
        return got
    for line in open(path, encoding="utf-8-sig", errors="ignore"):
        cols = line.rstrip("\n").split("\t")
        if len(cols) < 5 or not cols[0].strip().isdigit():
            continue
        cell = cols[4].strip()
        if cell and cell != "-" and cell.isdigit():
            got[int(cols[0])] = int(cell)
    return got


def read_old99(default=2):
    """从 map.txt 取 99（游戏默认）当前的编号，作为表里默认行的初始值。"""
    if os.path.exists(MAP_FILE):
        for ln in open(MAP_FILE, encoding="utf-8", errors="ignore"):
            c = ln.rstrip().split("\t")
            if len(c) > 1 and c[0].strip() == "99" and c[1].strip().isdigit():
                return int(c[1])
    return default


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--sync", action="store_true")
    a = ap.parse_args()

    cat = L.build_catalog()
    acts = json.load(open(os.path.join(ROOT, "Resources", "dlc", "dlc_activities.json"),
                          encoding="utf-8"))
    icon = {x["ID"]: (x.get("Icon") or 0) for x in acts}

    api = fetch()
    filled = parse_filled(DESKTOP)
    old99 = read_old99()
    v99 = filled.get(99, old99)

    def ts(u):
        return datetime.datetime.fromtimestamp(u).strftime("%Y-%m-%d") if u else ""

    rows = []
    for e in api.get("eras", []):
        i = int(e["id"])
        ic = icon.get(i, 0)
        origin = ic - 2400000 if ic >= 2400001 else 0
        rows.append((i, e.get("name", ""), ts(e.get("start_unix")), ts(e.get("end_unix")),
                     origin if origin != i else 0))

    out = []
    out.append("登录背景 ←→ 活动复刻期 对照表（MikuSB S1）")
    out.append("生成时间：%s    期号/名称取自 GM 后台接口，与网页下拉框一致"
               % datetime.datetime.now().strftime("%Y-%m-%d %H:%M"))
    out.append("")
    out.append("【怎么填】只改「登录背景编号」这一列：填 0~%d（与「切换登录背景.cmd」菜单编号一致）；" % (len(cat) - 1))
    out.append("          填 0 = 官方默认；留空或填 - = 该期不换背景。改完保存，服务端切期时自动套用。")
    out.append("          末行「99 游戏默认」= GM 首张卡「游戏默认」按钮套的图，不是某一期，改动同样即时生效。")
    out.append("")
    out.append("编号对照：见桌面《登录背景-编号总览.png》（0~%d 每格一张实机截图），" % (len(cat) - 1))
    out.append("          或同目录《登录背景-编号一览.txt》（文字版，列出每个编号的画面与场景资源）。")
    out.append("")
    out.append("期号\t主题\t开始\t结束\t登录背景编号\t复刻来源期号\t备注")
    filled_n = 0
    for i, name, st, en, origin in rows:
        v = filled.get(i, "")
        if v:
            filled_n += 1
        out.append("%d\t%s\t%s\t%s\t%s\t%s\t%s" % (
            i, name, st, en, v,
            ("%d" % origin) if origin else ("1" if i == 1 else ""),
            "" if v else "缺主视觉，可填 25~30（该期 3D 场景）或 19~24（背景底板）"))
    out.append("99\t游戏默认（不指定期号）\t-\t-\t%d\t\tGM 首张卡「游戏默认」按钮套这张" % v99)
    out.append("")
    out.append("# 说明：23 期以后是复刻期，主题名沿用原版期号的文案；「复刻来源期号」即原版期号。")
    out.append("# 12/14/16~20 期没有主视觉贴图，当年登录是 3D 场景，所以 25~30 号就是这些场景（已实拍确认）。")
    out.append("# 缺图期的对应关系：原 12 期=25、原 14 期=26、原 16 期=27、原 17 期=28、原 18 期=29、原 19 期=30。")

    data = "\r\n".join(out) + "\r\n"
    open(DESKTOP, "wb").write(("\ufeff" + data).encode("utf-8"))
    print("已写 %s（保留已填 %d 期）" % (DESKTOP, filled_n))

    if a.sync:
        lines = ["# 服务端兜底映射（桌面表读不到时用；桌面表存在时以桌面表为准）",
                 "# 期号<TAB>编号"]
        for i, name, st, en, origin in rows:
            if filled.get(i):
                lines.append("%d\t%d\t%s" % (i, filled[i], name))
        lines.append("99\t%d\t游戏默认（不指定期号）" % v99)
        os.makedirs(os.path.dirname(MAP_FILE), exist_ok=True)
        open(MAP_FILE, "w", encoding="utf-8").write("\n".join(lines) + "\n")
        print("已同步 %s（%d 条 + 默认 99→%d）" % (
            MAP_FILE, sum(1 for i, *_ in rows if filled.get(i)), v99))
    return 0


if __name__ == "__main__":
    sys.exit(main())
