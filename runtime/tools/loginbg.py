# -*- coding: utf-8 -*-
"""登录界面动态背景切换器（MikuSB S1 本地版）

原理
    客户端启动时读 Game/Content/Startup/Settings/login.txt（在 PAK_Game_Startup_Settings_0 里），
    每个版本一行：图片(picture) + 3D 场景(spine) + 背景音乐(music)。
    往 mods/ 放一个同名路径的覆盖 pak 即可换掉这一行，客户端不用改任何原文件。

用法
    python tools\\loginbg.py              交互式菜单（选完自动重启客户端）
    python tools\\loginbg.py --list       只列出可选背景
    python tools\\loginbg.py --set 5      直接切到 5 号（不重启）
    python tools\\loginbg.py --reset      恢复官方默认（删除覆盖包）
    python tools\\loginbg.py --current    显示当前用的是哪一项
    加 --restart 可让 --set / --reset 结束后自动重启客户端。

回滚
    删除 Game\\Content\\Paks\\mods\\Patch_Miku_LoginBG_100_P.pak 即恢复官方（= --reset）。
"""
import argparse
import json
import os
import sys
import time

import repak

TOOLS = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(TOOLS)                      # ...\SnowBreak\S1
GAME = os.path.join(os.path.dirname(ROOT), "Game")
PAK_DIR = os.path.join(GAME, "Content", "Paks", "mods")
PAK_PATH = os.path.join(PAK_DIR, "Patch_Miku_LoginBG_100_P.pak")
PAK_INNER = "Game/Content/Startup/Settings/login.txt"
TEMPLATE = os.path.join(TOOLS, "login_template.txt")
STAGE = os.path.join(TOOLS, "_loginbg_src")
STATE = os.path.join(TOOLS, "登录背景-当前.json")

PIC = "/Game/Startup/UI/Picture/pic_login_%s.pic_login_%s"
# 3D 登录场景自带的背景底板（2400x1080），在 Plot 包 Login_Plots/PoltAsset/Bg 下
PIC_PLATE = "/Game/Plot/CgPlot/Login_Plots/PoltAsset/Bg/%s.%s"
SCENE = "/Game/Plot/CgPlot/Login_Plots/PoltAsset/Scene/login_bg%03d.login_bg%03d"
# 少数期次的登录场景不按 login_bgNNN 编号，而是直接叫 login_dlcNN_01（1.1 明灯新仪就是这个）
SCENE_ANY = "/Game/Plot/CgPlot/Login_Plots/PoltAsset/Scene/%s.%s"
MUSIC = "/Game/Startup/Music/%s.%s"

# 客户端里实际存在的资源（2026-11-23 从 pak 索引核对）
SCENES = [1, 2] + list(range(4, 26))
# 场景资源不按 login_bgNNN 命名的期次：dlc01（1.1 明灯新仪）= login_dlc01_01
SCENE_ASSETS = {1: "login_dlc01_01"}
DLC_PICS = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 13, 15, 21, 22, 23, 24]

# 画面描述：2026-11-23 把 pak 里的贴图逐张解出来看过后填的，
# 不是按版本号猜的。改这里就能改菜单显示名。
DESC = {
    1: "霓虹灯笼夜市·白发少女（明灯新仪场景 login_dlc01_01）",
    2: "圆形机库·白发女主与全息屏",
    3: "圆形机库·白发女主与全息屏（与 3 号同场景）",
    4: "游艇甲板·四名泳装少女",
    5: "雪山基地·红发少女与白色高塔",
    6: "橙色圆形机库·蓝发与白发少女",
    7: "暗色遗迹·白发少女坐在巨手上",
    8: "城市天台巨幕·白蓝发与紫发少女",
    9: "紫色虚拟空间·蓝发少女与金色光环",
    10: "废墟战场·白发与金发少女",
    11: "白天霓虹街区·黑发紫挑染少女（漫天花灯）",
    13: "沙漠峡谷·粉发双马尾少女",
    15: "灯笼长廊·白发与黑发少女（中式红白）",
    21: "海底棕榈·金发与深发少女",
    22: "粉紫霞光·三少女",
    23: "庭院水景·狐耳黑发与白发红挑染",
    24: "紫色星空·双白灰发少女",
}


# 编号 19~24：登录场景自带的背景底板。缺主视觉的期次（12/14/16~20 等）就用这些补画面。
PLATES = [
    (19, "bg020", 20, "雪雾废墟·红伞金发少女", "BGM_DLC18_Theme"),
    (20, "bg021", 21, "海底棕榈·金发与深发少女", "BGM_DLC20_Theme"),
    (21, "bg022", 22, "粉紫霞光·三少女", "BGM_DLC21_Theme"),
    (22, "bg023", 23, "庭院水景·狐耳黑发与白发红挑染", "BGM_DLC22_Theme"),
    (23, "bg024_01", 24, "紫色星空·双白灰发少女", "BGM_DLC23_Theme"),
    (24, "bg025_01", 25, "彩虹云海·白毛兽耳少女", "BGM_DLC24_Theme"),
]


# 编号 25 起：没有主视觉贴图、但客户端仍保留 3D 登录场景的期次（12/14/16~19）。
# 画面描述为 2026-11-23 实拍（tools\loginbg_capture\sceneNNN.png），不是贴图猜的；
# picture 列借 dlc25_01 占位（该列不显示），音乐统一用确定存在的 BGM_DLC24_Theme。
SCENE_ONLY = [
    (12, "夏日游园·双泳装少女（摩天轮水花）"),
    (14, "紫色矿洞·白发红挑染少女特写"),
    (16, "紫色花海·紫发少女侧卧"),
    (17, "水下遗迹·白发少女与鱼群"),
    (18, "高空缆索·黑发眼镜少女（蓝天白云）"),
    (19, "蓝天拱廊·蓝发与红发少女"),
]


def build_catalog():
    cat = [
        dict(key="default", label="官方默认·3.6 版本（彩虹云海·白毛兽耳少女）",
             pic="dlc25_01", scene=25, music="BGM_DLC24_Theme"),
        dict(key="vanilla", label="最初版·雪原废墟与白色雕像",
             pic="bg001", scene=1, music="BGM_Login"),
    ]
    for n in DLC_PICS:
        scene = n if n in SCENES else max([s for s in SCENES if s <= n] or [1])
        e = dict(key="dlc%02d" % n,
                 label="dlc%02d｜%s" % (n, DESC.get(n, "")),
                 pic="dlc%02d_01" % n, scene=scene,
                 music="BGM_DLC%02d_Theme" % n)
        if n in SCENE_ASSETS:
            e["scene_asset"] = SCENE_ASSETS[n]
        cat.append(e)
    for idx, plate, scene, desc, music in PLATES:
        cat.append(dict(key="plate_%s" % plate, plate=plate,
                        label="%s｜%s" % (plate, desc),
                        pic=plate, scene=scene, music=music))
    for scene, desc in SCENE_ONLY:
        cat.append(dict(key="scene%03d" % scene,
                        label="场景%03d｜%s" % (scene, desc),
                        pic="dlc25_01", scene=scene, music="BGM_DLC24_Theme",
                        shot=scene))
    return cat


def preview_png(entry):
    """预览图：实拍场景用截图，主视觉是 <pic>.png，背景底板是 pic_login_<plate>.png。"""
    if entry.get("shot"):
        return os.path.join(TOOLS, "loginbg_capture", "scene%03d.png" % entry["shot"])
    base = ("pic_login_" + entry["plate"]) if entry.get("plate") else entry["pic"]
    return os.path.join(TOOLS, "loginbg_preview", base + ".png")


def row(entry):
    if entry.get("plate"):
        p = PIC_PLATE % (entry["plate"], entry["plate"])
    else:
        p = PIC % (entry["pic"], entry["pic"])
    if entry.get("scene_asset"):
        s = SCENE_ANY % (entry["scene_asset"], entry["scene_asset"])
    else:
        s = SCENE % (entry["scene"], entry["scene"])
    m = MUSIC % (entry["music"], entry["music"])
    return "\t".join(["1", entry["label"], p, "", s, "", m, "", "", "", ""]) + "\r\n"


def make_login_txt(entry):
    raw = open(TEMPLATE, "rb").read()
    bom = b"\xef\xbb\xbf"
    body = raw[len(bom):].decode("utf-8") if raw.startswith(bom) else raw.decode("utf-8")
    head = body.split("\r\n")[0] + "\r\n" + body.split("\r\n")[1] + "\r\n"
    return bom + (head + row(entry)).encode("utf-8")


def write_pak(entry):
    src = os.path.join(STAGE, "Game", "Content", "Startup", "Settings")
    os.makedirs(src, exist_ok=True)
    open(os.path.join(src, "login.txt"), "wb").write(make_login_txt(entry))
    os.makedirs(PAK_DIR, exist_ok=True)
    builder = repak.PakBuilder()
    w = builder.writer(PAK_PATH, version=repak.Version.V11, mount_point="../../../")
    with w:
        data = open(os.path.join(src, "login.txt"), "rb").read()
        w.write_file(PAK_INNER, data, compress=False)
        w.write_index()
    json.dump(entry, open(STATE, "w", encoding="utf-8"), ensure_ascii=False, indent=2)


def reset():
    if os.path.exists(PAK_PATH):
        os.remove(PAK_PATH)
    if os.path.exists(STATE):
        os.remove(STATE)


def current():
    """以覆盖包里的 login.txt 为准（可能是服务端按复刻期写的，不一定有状态文件）。"""
    if not os.path.exists(PAK_PATH):
        return None
    pic = None
    scene = None
    try:
        b = repak.PakBuilder()
        r = b.reader(PAK_PATH)
        tmp = os.path.join(TOOLS, "_cur_login.txt")
        r.read_file(PAK_INNER, tmp)
        with open(tmp, encoding="utf-8-sig", errors="ignore") as f:
            for line in f:
                if "pic_login_" in line:
                    pic = line.split("pic_login_", 1)[1].split(".")[0]
                if "login_bg" in line:
                    try:
                        scene = int(line.split("login_bg", 1)[1][:3])
                    except ValueError:
                        pass
        os.remove(tmp)
    except Exception:
        pic = None
    for e in build_catalog():
        if e["scene"] == scene and (e.get("shot") or e["pic"] == pic):
            return e
    for e in build_catalog():
        if e["pic"] == pic:
            return e
    try:
        return json.load(open(STATE, encoding="utf-8"))
    except Exception:
        return {"label": "（有覆盖包但不是本工具生成的）"}


def find_console():
    import ctypes
    from ctypes import wintypes
    u = ctypes.windll.user32
    u.SetProcessDPIAware()
    for title in ("MikuSB S1", "MikuSB"):
        h = u.FindWindowW(None, title)
        if h:
            return h
    return 0


def kill_client():
    """客户端会占用覆盖 pak，换包前必须先关掉。"""
    import subprocess
    subprocess.run(["taskkill", "/F", "/IM", "Game.exe"], capture_output=True)
    for _ in range(20):
        r = subprocess.run(["tasklist", "/FI", "IMAGENAME eq Game.exe"],
                           capture_output=True, text=True, errors="ignore")
        if "Game.exe" not in (r.stdout or ""):
            return True
        time.sleep(1)
    return False


def send_game():
    import ctypes
    h = find_console()
    if not h:
        print("!! 没找到 MikuSB 控制台窗口，请手动在服务端窗口输入 /game")
        return False
    u = ctypes.windll.user32
    u.ShowWindow(h, 9)
    u.SetForegroundWindow(h)
    time.sleep(1.0)
    for ch in "/game":
        r = u.VkKeyScanW(ord(ch))
        u.keybd_event(r & 0xFF, 0, 0, 0)
        u.keybd_event(r & 0xFF, 0, 2, 0)
        time.sleep(0.05)
    u.keybd_event(0x0D, 0, 0, 0)
    u.keybd_event(0x0D, 0, 2, 0)
    print("已发送 /game，客户端约 2 分钟进入登录界面")
    return True


def label_of(cat, cur):
    """状态文件里存的是旧文案，显示时按 key 反查最新描述。"""
    if not cur:
        return None
    for e in cat:
        if e.get("key") == cur.get("key"):
            return e["label"]
    return cur.get("label")


def show(cat):
    cur = current()
    cur_key = (cur or {}).get("key")
    for i, e in enumerate(cat):
        mark = "◆" if e["key"] == cur_key else " "
        sc = e.get("scene_asset") or ("bg%03d" % e["scene"])
        print("%s %2d) %-34s picture=%s  scene=%s  music=%s"
              % (mark, i, e["label"], e["pic"], sc, e["music"]))
    if cur is None:
        print("   当前：官方默认（无覆盖包）")
    else:
        print("   当前：%s" % label_of(cat, cur))


def main():
    ap = argparse.ArgumentParser(add_help=True)
    ap.add_argument("--list", action="store_true")
    ap.add_argument("--set", type=int)
    ap.add_argument("--reset", action="store_true")
    ap.add_argument("--current", action="store_true")
    ap.add_argument("--restart", action="store_true")
    ap.add_argument("--preview", type=int, help="用看图程序打开第 N 项的效果图")
    a = ap.parse_args()
    cat = build_catalog()

    if a.preview is not None:
        if not 0 <= a.preview < len(cat):
            print("编号超范围，用 --list 看可选值")
            return 2
        png = preview_png(cat[a.preview])
        if not os.path.exists(png):
            print("没有这一项的预览图：%s" % png)
            return 2
        os.startfile(png)
        return 0

    if a.list:
        show(cat)
        return 0
    if a.current:
        cur = current()
        print("当前：%s" % (label_of(cat, cur) if cur else "官方默认（无覆盖包）"))
        print("覆盖包：%s" % ("有" if os.path.exists(PAK_PATH) else "无"))
        return 0
    if a.reset:
        if a.restart:
            kill_client()
        reset()
        print("已删除覆盖包，恢复官方默认。")
        if a.restart:
            send_game()
        return 0
    if a.set is not None:
        if not 0 <= a.set < len(cat):
            print("编号超范围，用 --list 看可选值")
            return 2
        if a.restart:
            kill_client()
        try:
            write_pak(cat[a.set])
        except OSError as e:
            print("写入失败（%s）：请先关闭游戏客户端再试" % e)
            return 3
        print("已切换：%s" % cat[a.set]["label"])
        if a.restart:
            send_game()
        else:
            print("重启客户端后生效（或加 --restart）。")
        return 0

    # 交互式
    show(cat)
    print()
    print("输入编号切换；r = 恢复官方默认；q = 退出")
    while True:
        try:
            cmd = input("> ").strip().lower()
        except (EOFError, KeyboardInterrupt):
            return 0
        if cmd in ("q", ""):
            return 0
        if cmd == "r":
            kill_client()
            reset()
            print("已恢复官方默认。")
            send_game()
            return 0
        if cmd.isdigit() and 0 <= int(cmd) < len(cat):
            kill_client()
            try:
                write_pak(cat[int(cmd)])
            except OSError as e:
                print("写入失败（%s）。" % e)
                return 3
            print("已切换：%s" % cat[int(cmd)]["label"])
            if input("现在启动客户端看效果？(Y/n) ").strip().lower() != "n":
                send_game()
            return 0
        print("看不懂，输入编号 / r / q")


if __name__ == "__main__":
    sys.exit(main())
