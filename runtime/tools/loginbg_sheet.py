# -*- coding: utf-8 -*-
"""生成 S1\\登录背景一览.png（编号 + 描述 + 贴图名），编号与切换器菜单一致。"""
import os
import sys

from PIL import Image, ImageDraw, ImageFont

TOOLS = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(TOOLS)
sys.path.insert(0, TOOLS)
import loginbg as L  # noqa: E402

cat = L.build_catalog()
F = ImageFont.truetype(r"C:\Windows\Fonts\msyh.ttc", 17)
FB = ImageFont.truetype(r"C:\Windows\Fonts\msyhbd.ttc", 30)
FS = ImageFont.truetype(r"C:\Windows\Fonts\msyh.ttc", 14)

TW = 400
IMGW = TW - 14
IMGH = int(IMGW * 1080 / 2400)
TH = IMGH + 54
cols = 4
rows = (len(cat) + cols - 1) // cols
sheet = Image.new("RGB", (cols * TW, 92 + rows * TH), (16, 18, 22))
d = ImageDraw.Draw(sheet)
d.text((20, 16), "登录界面背景一览（MikuSB S1）", font=FB, fill=(255, 214, 102))
d.text((22, 56),
       "编号与「切换登录背景.cmd」一致；图片由 pak 内贴图直接解码。"
       "19~24 是 3D 登录场景自带的背景底板，游戏内还会叠加骨架角色/前景动画，实际画面更满。",
       font=FS, fill=(175, 180, 190))

for i, e in enumerate(cat):
    x = (i % cols) * TW
    y = 92 + (i // cols) * TH
    png = L.preview_png(e)
    if os.path.exists(png):
        im = Image.open(png).convert("RGB").resize((IMGW, IMGH), Image.LANCZOS)
        sheet.paste(im, (x + 7, y + 2))
    else:
        d.rectangle([x + 7, y + 2, x + 7 + IMGW, y + 2 + IMGH], fill=(40, 42, 48))
        d.text((x + 20, y + IMGH // 2), "（无预览）", font=F, fill=(200, 200, 200))
    d.text((x + 8, y + IMGH + 6), "%d) %s" % (i, e["label"]), font=F, fill=(240, 240, 240))
    d.text((x + 10, y + IMGH + 30), "   %s" % (e.get("plate") or e["pic"]), font=FS,
           fill=(120, 165, 215))

dst = os.path.join(ROOT, "登录背景一览.png")
sheet.save(dst, optimize=True)
print("saved", dst, sheet.size, len(cat))
