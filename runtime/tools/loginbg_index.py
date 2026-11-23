# -*- coding: utf-8 -*-
"""生成「编号 -> 真实画面」总览图与清单（给用户重新填对照表用）。

用法：
    python tools\\loginbg_index.py

输出：
    桌面\\登录背景-编号总览.png      31 格（0~30），每格一张实机截图
    桌面\\登录背景-编号一览.txt      同内容的文字版
    桌面\\登录背景-编号总览-host.png  截图来源版本（本机 S1 客户端）

截图来源：tools\\loginbg_capture\\sceneNNN.png（实拍）；2 号用 dlc01_login.png。
"""
import os
import sys

from PIL import Image, ImageDraw, ImageFont

TOOLS = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(TOOLS)
sys.path.insert(0, TOOLS)
import loginbg as L  # noqa: E402

CAP = os.path.join(TOOLS, "loginbg_capture")
DESKTOP = os.path.join(os.path.expanduser("~"), "Desktop")

SHORT = {
    0: "彩虹云海·白毛兽耳少女（3.6 官方默认）",
    1: "雪原废墟·白色巨型雕像（最初版）",
    2: "霓虹灯笼夜市·白发少女（明灯新仪）",
    3: "圆形机库·白发女主与全息屏",
    4: "圆形机库（与 3 号同一场景）",
    5: "游艇甲板·四名泳装少女",
    6: "雪山基地·红发少女与白色高塔",
    7: "橙色圆形机库·蓝发与白发少女",
    8: "暗色遗迹·白发少女坐在巨手",
    9: "城市天台巨幕·白蓝发与紫发",
    10: "紫色虚拟空间·蓝发少女",
    11: "废墟战场·白发与金发",
    12: "白天霓虹街区·黑发紫挑染（花灯）",
    13: "沙漠峡谷·粉发双马尾",
    14: "灯笼长廊·白发与黑发（中式）",
    15: "海底棕榈·金发与深发",
    16: "粉紫霞光·三名少女",
    17: "庭院水景·狐耳黑发与白发红挑染",
    18: "紫色星空·双白灰发少女",
    19: "雪雾废墟·红伞金发少女",
    20: "海底棕榈（与 15 号同场景）",
    21: "粉紫霞光（与 16 号同场景）",
    22: "庭院水景（与 17 号同场景）",
    23: "紫色星空（与 18 号同场景）",
    24: "彩虹云海（与 0 号同场景）",
    25: "夏日游园·双泳装少女",
    26: "紫色矿洞·白发红挑染特写",
    27: "紫色花海·紫发少女侧卧",
    28: "水下遗迹·白发少女与鱼群",
    29: "高空缆索·黑发眼镜少女",
    30: "蓝天拱廊·蓝发与红发少女",
}


def shot_of(idx, entry):
    if idx == 2:
        return os.path.join(CAP, "dlc01_login.png")
    return os.path.join(CAP, "scene%03d.png" % entry["scene"])


def scene_of(entry):
    return entry.get("scene_asset") or ("login_bg%03d" % entry["scene"])


def era_map():
    """当前 map.txt 里 编号 -> [期号]，用来说明「现在谁在用这个编号」。"""
    rev = {}
    p = os.path.join(ROOT, "Resources", "loginbg", "map.txt")
    if not os.path.exists(p):
        return rev
    for ln in open(p, encoding="utf-8", errors="ignore"):
        c = ln.rstrip("\n").split("\t")
        if len(c) > 1 and c[0].strip().isdigit() and c[1].strip().isdigit():
            rev.setdefault(int(c[1]), []).append(c[0].strip())
    return rev


def font(size, bold=False):
    path = "C:/Windows/Fonts/msyhbd.ttc" if bold else "C:/Windows/Fonts/msyh.ttc"
    return ImageFont.truetype(path, size)


def build_png(cat):
    COLS, CW, CH, GAP, M = 6, 372, 209, 18, 40
    LAB = 62
    TOP, BOT = 108, 66
    rows = (len(cat) + COLS - 1) // COLS
    W = M * 2 + COLS * CW + (COLS - 1) * GAP
    H = TOP + rows * (CH + LAB) + (rows - 1) * 16 + BOT
    im = Image.new("RGB", (W, H), "#12141a")
    d = ImageDraw.Draw(im)
    f_title, f_sub = font(36, True), font(17)
    f_no, f_desc = font(21, True), font(15)
    f_note = font(15)

    d.text((M, 26), "登录背景编号总览 · 按编号看真实画面", font=f_title, fill="#ffffff")
    d.text((M, 76), "2026-11-23 逐场景实机截图（客户端 3.6.0.140）；编号与「切换登录背景.cmd」菜单、对照表第 5 列一致",
           font=f_sub, fill="#9aa4b2")

    rev = era_map()
    for i, e in enumerate(cat):
        r, c = divmod(i, COLS)
        x = M + c * (CW + GAP)
        y = TOP + r * (CH + LAB + 16)
        p = shot_of(i, e)
        if os.path.exists(p):
            img = Image.open(p).convert("RGB").resize((CW, CH), Image.LANCZOS)
            im.paste(img, (x, y))
        else:
            d.rectangle([x, y, x + CW - 1, y + CH - 1], fill="#242832")
            d.text((x + 12, y + 90), "(无截图)", font=f_desc, fill="#7c8598")
        color = "#ffd166" if i == 2 else "#2c313c"
        d.rectangle([x, y, x + CW - 1, y + CH - 1], outline=color, width=2 if i == 2 else 1)
        d.rectangle([x, y + CH, x + CW, y + CH + LAB], fill="#1b1f27")
        title = "%2d 号" % i
        if i == 2:
            title += "  ★ 第 1 期 明灯新仪"
        d.text((x + 12, y + CH + 7), title, font=f_no, fill=color if i == 2 else "#ffd166")
        d.text((x + 12, y + CH + 34), SHORT.get(i, e["label"])[:30], font=f_desc, fill="#cfd6e2")
        used = rev.get(i, [])
        if used and used != ["99"]:
            d.text((x + CW - 108, y + CH + 7), "期 " + "/".join(used[:3]), font=f_note, fill="#6f7a8c")

    note = ("截图来自各编号实际套用的登录场景（spine 列），与主视觉贴图不是同一张；"
            "3 与 4、15 与 20、16 与 21、17 与 22、18 与 23、0 与 24 是同一场景的两个编号。")
    d.text((M, H - BOT + 16), note, font=f_note, fill="#8b94a3")
    d.text((M, H - BOT + 38), "填表：桌面《登录背景-复刻期对照表.txt》第 5 列填编号，0~30；留空 = 该期不换背景。",
           font=f_note, fill="#8b94a3")
    return im


def build_txt(cat):
    rev = era_map()
    out = ["登录背景编号一览（MikuSB S1，2026-11-23 实机截图）", ""]
    out.append("编号\t画面\t场景资源\t当前占用期号")
    for i, e in enumerate(cat):
        used = rev.get(i, [])
        out.append("%d\t%s\t%s\t%s" % (i, SHORT.get(i, e["label"]), scene_of(e),
                                       "/".join(used) if used else ""))
    out += ["",
            "# 2 号是第 1 期「明灯新仪」的画面（霓虹灯笼夜市），2026-11-23 从 login_dlc01_01 修正；",
            "# 原来 2 号挂的是 login_bg001（雪原），所以显示成雪原废墟。",
            "# 编号对照「切换登录背景.cmd」菜单和 GM 网页版本卡下方的按钮。"]
    return "\n".join(out) + "\n"


def main():
    cat = L.build_catalog()
    im = build_png(cat)
    targets = [os.path.join(DESKTOP, "登录背景-编号总览.png"),
               os.path.join(ROOT, "登录背景-编号总览.png")]
    for t in targets:
        im.save(t)
        print("已生成", t, im.size)
    tp = os.path.join(DESKTOP, "登录背景-编号一览.txt")
    open(tp, "wb").write(("\ufeff" + build_txt(cat).replace("\n", "\r\n")).encode("utf-8"))
    print("已生成", tp)


if __name__ == "__main__":
    main()
