#!/usr/bin/env python3
"""Generate matching SVG and PNG architecture diagrams (Pillow 13.0.0)."""
from __future__ import annotations

import html
import math
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'docs/assets'
NAVY, TEAL, PALE, GRAY = '#123047', '#007d78', '#eef4f5', '#4c5e6b'


def font(size: int, bold: bool = False):
    candidates = [
        Path('/System/Library/Fonts/Supplemental') / ('Arial Bold.ttf' if bold else 'Arial.ttf'),
        Path('/usr/share/fonts/truetype/dejavu') / ('DejaVuSans-Bold.ttf' if bold else 'DejaVuSans.ttf'),
        Path('C:/Windows/Fonts') / ('arialbd.ttf' if bold else 'arial.ttf'),
    ]
    for candidate in candidates:
        if candidate.exists():
            return ImageFont.truetype(str(candidate), size * 2)
    return ImageFont.load_default(size=size * 2)


class Diagram:
    def __init__(self, width: int, height: int):
        self.width, self.height = width, height
        self.image = Image.new('RGB', (width * 2, height * 2), 'white')
        self.draw = ImageDraw.Draw(self.image)
        self.svg = [f'<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height}" viewBox="0 0 {width} {height}">',
                    f'<rect width="{width}" height="{height}" fill="white"/>']

    def text(self, x, y, value, size=21, color=NAVY, bold=False, anchor='middle'):
        family = 'Arial, sans-serif'
        weight = 'bold' if bold else 'normal'
        self.svg.append(f'<text x="{x}" y="{y}" font-family="{family}" font-size="{size}" font-weight="{weight}" text-anchor="{anchor}" dominant-baseline="middle" fill="{color}">{html.escape(value)}</text>')
        self.draw.text((x * 2, y * 2), value, font=font(size, bold), fill=color, anchor='mm' if anchor == 'middle' else 'lm')

    def box(self, x, y, w, h, title, subtitle, fill=PALE):
        self.svg.append(f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="12" fill="{fill}" stroke="{TEAL}" stroke-width="2"/>')
        self.draw.rounded_rectangle((x * 2, y * 2, (x+w) * 2, (y+h) * 2), radius=24, fill=fill, outline=TEAL, width=4)
        self.text(x + w/2, y + h/2 - 14, title, 24, bold=True)
        self.text(x + w/2, y + h/2 + 19, subtitle, 19, GRAY)

    def line(self, x1, y1, x2, y2, color=TEAL, dashed=False, arrow=True):
        dash = ' stroke-dasharray="6 6"' if dashed else ''
        self.svg.append(f'<line x1="{x1}" y1="{y1}" x2="{x2}" y2="{y2}" stroke="{color}" stroke-width="2.5"{dash}/>')
        if dashed:
            length = math.hypot(x2-x1, y2-y1)
            if length:
                for d in range(0, int(length), 12):
                    a, b = d/length, min(d+6, length)/length
                    self.draw.line(((x1+(x2-x1)*a)*2, (y1+(y2-y1)*a)*2, (x1+(x2-x1)*b)*2, (y1+(y2-y1)*b)*2), fill=color, width=5)
        else:
            self.draw.line((x1*2,y1*2,x2*2,y2*2),fill=color,width=5)
        if arrow:
            angle = math.atan2(y2-y1,x2-x1)
            points = [(x2,y2)] + [(x2-12*math.cos(angle+a), y2-12*math.sin(angle+a)) for a in (-.45,.45)]
            self.svg.append(f'<polygon points="{" ".join(f"{x},{y}" for x,y in points)}" fill="{color}"/>')
            self.draw.polygon([(x*2,y*2) for x,y in points],fill=color)

    def save(self, name):
        OUT.mkdir(parents=True, exist_ok=True)
        (OUT / f'{name}.svg').write_text('\n'.join(self.svg + ['</svg>'])+'\n',encoding='utf-8')
        self.image.save(OUT / f'{name}.png', optimize=True)


def architecture():
    d=Diagram(1000,780)
    d.text(500,28,'LLAMADAS DURANTE UNA SOLICITUD',19,TEAL,bold=True)
    d.box(330,65,340,80,'Cliente HTTPS','Cookie + token CSRF')
    d.line(500,145,500,185)
    d.box(280,185,440,95,'Store.Api','DTO · autenticación · políticas')
    d.line(360,280,270,340)
    d.line(640,280,730,340)
    d.text(230,310,'Catálogo',18,GRAY)
    d.text(775,310,'Identidad',18,GRAY)
    d.box(45,340,450,100,'Store.Application','StoreService + IStoreRepository')
    d.box(545,340,410,100,'ASP.NET Core Identity','UserManager + SignInManager')
    d.line(160,440,160,500)
    d.text(91,472,'Reglas',17,GRAY)
    d.box(40,500,240,90,'Store.Domain','Invariantes puras')
    d.line(380,440,500,500)
    d.line(750,440,750,500)
    d.box(355,500,600,110,'Store.Infrastructure · Dapper','SqliteStoreRepository + DapperUserStore')
    d.line(655,610,655,655)
    d.box(430,655,450,85,'SQLite','Catálogo · usuarios · roles')
    d.text(173,654,'Sin dependencias',18,GRAY)
    d.text(173,680,'de HTTP o SQL',18,GRAY)
    d.save('architecture')


def authentication():
    d=Diagram(1000,870)
    d.text(500,28,'SESIÓN Y AUTORIZACIÓN',19,TEAL,bold=True)
    cols=[145,500,855]
    for x,title in zip(cols,['Cliente HTTPS','ASP.NET Core','Dapper / SQLite']):
        d.box(x-125,65,250,75,title,'',fill=PALE)
        d.line(x,150,x,790,color='#b8c7cd',dashed=True,arrow=False)
    steps=[
        (0,1,205,'1  Obtener cookie y token CSRF'),
        (0,1,285,'2  Login + contraseña + CSRF'),
        (1,2,355,'3  Cargar usuario y roles'),
        (1,0,445,'4  Validar contraseña; emitir cookie'),
        (0,1,525,'5  Obtener CSRF autenticado'),
        (0,1,605,'6  Solicitud + cookie + CSRF'),
        (1,2,675,'7  Verificar sello y roles vigentes'),
        (1,0,765,'8  Política permite o devuelve 401 / 403'),
    ]
    for src,dst,y,label in steps:
        x1,x2=cols[src],cols[dst]
        d.line(x1,y,x2,y)
        d.text((x1+x2)/2,y-20,label,19)
    d.text(500,837,'CSRF se exige en login y escrituras. La sesión dura 30 minutos.',19,GRAY)
    d.save('authentication-flow')


if __name__=='__main__':
    architecture()
    authentication()
    print(f'Created SVG and PNG diagrams in {OUT}')
