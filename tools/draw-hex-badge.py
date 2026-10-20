"""슬롯 육각 배지 그림 (4배 해상도): 틀(흰 선, 색은 게임에서 입힌다), 빛(흰 번짐), 안쪽 바탕.
사용: python tools/draw-hex-badge.py client/PrestigePerks.Client/Assets/Sprites
(Hex_Frame.png, Hex_Fill.png, Hex_Glow.png 를 만든다)"""
import math
import sys
from PIL import Image, ImageDraw, ImageFilter

out = sys.argv[1]
W, H = 376, 436          # 틀 그림 크기 (원본 Frames_0 94x109 의 4배)
SS = 4                   # 그릴 때 한 번 더 키웠다 줄여 매끄럽게


def hex_points(cx, cy, r):
    return [(cx + r * math.cos(math.radians(90 + 60 * k)), cy + r * math.sin(math.radians(90 + 60 * k))) for k in range(6)]


def draw(w, h, fn):
    big = Image.new("RGBA", (w * SS, h * SS), (0, 0, 0, 0))
    fn(ImageDraw.Draw(big), SS)
    return big.resize((w, h), Image.LANCZOS)


R = H / 2 - 6            # 틀 바깥 꼭짓점 반지름


def frame(d, s):
    cx, cy = W / 2 * s, H / 2 * s
    # 한 겹 선 (안쪽 테두리는 뺐다)
    d.polygon(hex_points(cx, cy, R * s), outline=None, fill=(255, 255, 255, 255))
    d.polygon(hex_points(cx, cy, (R - 10) * s), fill=(0, 0, 0, 0))


def fill(d, s):
    cx, cy = W / 2 * s, H / 2 * s
    d.polygon(hex_points(cx, cy, (R - 8) * s), fill=(255, 255, 255, 255))


img = draw(W, H, frame)
img.save(out + "/Hex_Frame.png")

# 안쪽 바탕: 어두운 세로 그라데이션을 육각 모양으로
mask = draw(W, H, fill).split()[3]
grad = Image.new("RGBA", (W, H))
gd = ImageDraw.Draw(grad)
for y in range(H):
    t = y / (H - 1)
    c = tuple(int(a + (b - a) * t) for a, b in zip((22, 26, 28), (9, 11, 12)))
    gd.line((0, y, W, y), fill=c + (255,))
grad.putalpha(mask)
grad.save(out + "/Hex_Fill.png")

# 빛: 틀 바깥으로 번지는 흰 빛 (틀보다 30% 크게 그린다)
GW, GH = int(W * 1.3), int(H * 1.3)
glow = Image.new("RGBA", (GW, GH), (0, 0, 0, 0))
gdraw = ImageDraw.Draw(glow)
gdraw.polygon(hex_points(GW / 2, GH / 2, R), outline=(255, 255, 255, 255), width=14)
glow = glow.filter(ImageFilter.GaussianBlur(18))
a = glow.split()[3].point(lambda v: min(255, int(v * 2.2)))
glow = Image.new("RGBA", (GW, GH), (255, 255, 255, 0))
glow.putalpha(a)
glow.save(out + "/Hex_Glow.png")
print("ok", img.size, glow.size)
