# -*- coding: utf-8 -*-
"""批量实拍：逐个切换 3D 登录场景（login_bgNNN）并截图，用来确认「编号→实际画面」。

背景（第 526 节实测结论）：
    login.txt 的 picture 列（pic_login_*）在这版客户端**不显示**，
    屏幕上的画面完全来自 spine 列（Login_Plots/.../Scene/login_bgNNN 3D 场景）。
    把 spine 清空会得到纯黑登录界面，可反证。

用法：
    python tools\\loginbg_capture.py 2 13 17 20          # 抓这几个场景
    python tools\\loginbg_capture.py --all                # 抓包里全部场景
输出：
    tools\\loginbg_capture\\sceneNNN.png
    tools\\loginbg_capture\\capture.log
"""
import argparse
import ctypes
from ctypes import wintypes
import os
import subprocess
import sys
import time
import traceback

from PIL import Image

TOOLS = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(TOOLS)
OUT = os.path.join(TOOLS, "loginbg_capture")
LOG = os.path.join(OUT, "capture.log")

sys.path.insert(0, TOOLS)
import loginbg as L  # noqa: E402

user32 = ctypes.windll.user32
gdi32 = ctypes.windll.gdi32
TITLE = "Snowbreak: Containment Zone"


def log(msg):
    os.makedirs(OUT, exist_ok=True)
    line = "%s %s" % (time.strftime("%H:%M:%S"), msg)
    print(line, flush=True)
    with open(LOG, "a", encoding="utf-8") as f:
        f.write(line + "\n")


def shot(path):
    hwnd = user32.FindWindowW(None, TITLE)
    if not hwnd:
        return None
    r = wintypes.RECT()
    user32.GetClientRect(hwnd, ctypes.byref(r))
    w, h = r.right - r.left, r.bottom - r.top
    if w < 100 or h < 100:
        return None
    hdc = user32.GetWindowDC(hwnd)
    mdc = gdi32.CreateCompatibleDC(hdc)
    bmp = gdi32.CreateCompatibleBitmap(hdc, w, h)
    gdi32.SelectObject(mdc, bmp)
    user32.PrintWindow(hwnd, mdc, 3)

    class BIH(ctypes.Structure):
        _fields_ = [("biSize", ctypes.c_uint32), ("biWidth", ctypes.c_int32),
                    ("biHeight", ctypes.c_int32), ("biPlanes", ctypes.c_uint16),
                    ("biBitCount", ctypes.c_uint16), ("biCompression", ctypes.c_uint32),
                    ("biSizeImage", ctypes.c_uint32), ("biXPelsPerMeter", ctypes.c_int32),
                    ("biYPelsPerMeter", ctypes.c_int32), ("biClrUsed", ctypes.c_uint32),
                    ("biClrImportant", ctypes.c_uint32)]

    bi = BIH()
    bi.biSize = ctypes.sizeof(BIH)
    bi.biWidth = w
    bi.biHeight = -h
    bi.biPlanes = 1
    bi.biBitCount = 32
    buf = ctypes.create_string_buffer(w * h * 4)
    gdi32.GetDIBits(mdc, bmp, 0, h, buf, ctypes.byref(bi), 0)
    gdi32.DeleteObject(bmp)
    gdi32.DeleteDC(mdc)
    user32.ReleaseDC(hwnd, hdc)
    im = Image.frombuffer("RGBA", (w, h), buf, "raw", "BGRA", 0, 1).convert("RGB")
    im.save(path)
    return im


def ready(im):
    """登录界面是否已经画出来（黑屏/加载中/健康游戏忠告白底页返回 False）。"""
    if im is None:
        return False
    box = (im.size[0] // 10, im.size[1] // 10, im.size[0] * 9 // 10, im.size[1] * 9 // 10)
    g = im.convert("L").crop(box)
    px = list(g.getdata())[::17]
    if not px:
        return False
    avg = sum(px) / len(px)
    var = sum((p - avg) ** 2 for p in px) / len(px)
    if not (avg > 6 and var > 40):
        return False
    # 启动时的「健康游戏忠告」是白底黑字：亮度极高、几乎无彩色
    hsv = im.convert("HSV").crop(box)
    sat = list(hsv.getchannel("S").getdata())[::17]
    avg_sat = sum(sat) / len(sat)
    return not (avg > 195 and avg_sat < 45)


def write_pak(scene):
    entry = dict(key="cap", label="capture scene %03d" % scene, pic="dlc25_01",
                 scene=scene, music="BGM_DLC24_Theme")
    L.write_pak(entry)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("scenes", nargs="*", type=int)
    ap.add_argument("--all", action="store_true")
    ap.add_argument("--wait", type=int, default=260, help="单次最多等待秒数")
    a = ap.parse_args()

    if a.all:
        scenes = [1, 2] + list(range(4, 26))
    else:
        scenes = a.scenes
    if not scenes:
        print(__doc__)
        return 2

    os.makedirs(OUT, exist_ok=True)
    log("=== 开始批量实拍，场景 %s ===" % scenes)
    for scene in scenes:
        dst = os.path.join(OUT, "scene%03d.png" % scene)
        try:
            subprocess.run(["taskkill", "/F", "/IM", "Game.exe"], capture_output=True)
            time.sleep(6)
            write_pak(scene)
            L.send_game()
            t0 = time.time()
            ok = False
            time.sleep(42)                      # 跳过启动忠告页（约 20~35 秒出现）
            while time.time() - t0 < a.wait:
                im = shot(dst)
                if ready(im):
                    ok = True
                    break
                time.sleep(10)
            log("scene%03d %s（%d 秒）" % (scene, "OK" if ok else "超时", int(time.time() - t0)))
        except Exception as e:
            log("scene%03d 失败 %s: %s" % (scene, type(e).__name__, e))
            log(traceback.format_exc())
    log("=== 结束 ===")
    return 0


if __name__ == "__main__":
    sys.exit(main())
