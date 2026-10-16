#!/usr/bin/env python3
"""개발용: bench --dump 결과를 경위도 그림(PNG)으로. 사용: python3 tools/planet_map.py <dump 앞이름> <출력.png>"""
import sys, numpy as np
from PIL import Image
pre, out = sys.argv[1], sys.argv[2]
d = np.genfromtxt(pre + ".csv", delimiter=",", names=True)
ids = np.fromfile(pre + ".ids", dtype=np.int32).reshape(512, 1024)
terr = d["terrain"].astype(int); bio = d["biome"].astype(int); t = d["temp10"] / 10; p = d["precip"]; riv = d["river"].astype(int)
TER = np.array([(22,40,80),(45,85,140),(70,120,190),(120,160,80),(150,150,90),(185,165,115),(130,105,85),(240,240,245)])
BIO = np.array([(40,70,130),(235,240,250),(170,175,150),(50,95,70),(70,135,60),(175,190,100),(225,200,130),(190,170,80),(25,110,40),(90,120,100),(140,130,120)])
def col_terrain():
    c = TER[terr].astype(float)
    c[riv >= 1] = c[riv >= 1] * 0.4 + np.array([60,110,220]) * 0.6
    return c
def col_biome():
    c = BIO[bio].astype(float)
    c[riv >= 2] = (60,110,220)
    return c
def col_temp():
    x = np.clip((t + 30) / 60, 0, 1)
    return np.stack([255*x, 255*(1-abs(x-0.5)*2)*0.9+20, 255*(1-x)], 1)
def col_rain():
    x = np.clip(np.log(np.maximum(p, 20) / 20) / np.log(4000 / 20), 0, 1)
    c = np.stack([200*(1-x)+20, 120+100*x*(1-x)*2, 60+190*x], 1)
    c[terr <= 1] = c[terr <= 1] * 0.4
    return c
panels = [col_terrain(), col_biome(), col_temp(), col_rain()]
img = np.concatenate([np.concatenate([panels[0][ids], panels[1][ids]], 1), np.concatenate([panels[2][ids], panels[3][ids]], 1)], 0)
Image.fromarray(img.clip(0,255).astype(np.uint8)).save(out)
# 땅 쪽에서 본 지구본(지형 · 생물군계 · 기온)
import os
if os.path.exists(pre + ".ortho"):
    o = np.fromfile(pre + ".ortho", dtype=np.int32).reshape(512, 512)
    parts = []
    for c in panels[:3]:
        a = c[np.maximum(o, 0)]
        a[o < 0] = (10, 10, 14)
        parts.append(a)
    Image.fromarray(np.concatenate(parts, 1).clip(0,255).astype(np.uint8)).save(out.replace(".png", "-globe.png"))
print(out)
