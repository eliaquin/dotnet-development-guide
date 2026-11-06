#!/usr/bin/env python3
"""Render docs/guia.md to docs/guia.pdf. Requires reportlab==4.4.9."""
from __future__ import annotations

import html
from pathlib import Path
import re
import xml.etree.ElementTree as ET

from reportlab.graphics.shapes import Drawing, Line, Polygon, Rect, String
from reportlab.lib import colors
from reportlab.lib.styles import ParagraphStyle, getSampleStyleSheet
from reportlab.pdfbase import pdfmetrics
from reportlab.platypus import (
    BaseDocTemplate, Frame, Image, PageBreak, PageTemplate,
    Paragraph, Preformatted, Spacer, Table, TableStyle,
)

ROOT = Path(__file__).resolve().parents[1]
NAVY = colors.HexColor('#123047')
TEAL = colors.HexColor('#007d78')
GRAY = colors.HexColor('#4c5e6b')
PALE = colors.HexColor('#eef4f5')
WIDTH, HEIGHT = 612, 792
MARGIN = 54
CONTENT = WIDTH - MARGIN * 2

styles = getSampleStyleSheet()
styles.add(ParagraphStyle('TitleGuide', fontName='Helvetica-Bold', fontSize=38, leading=43,
                          textColor=NAVY, spaceAfter=24))
styles.add(ParagraphStyle('SectionGuide', fontName='Helvetica-Bold', fontSize=23, leading=28,
                          textColor=NAVY, spaceAfter=18))
styles.add(ParagraphStyle('SubGuide', fontName='Helvetica-Bold', fontSize=13, leading=17,
                          textColor=TEAL, spaceBefore=8, spaceAfter=9))
styles.add(ParagraphStyle('BodyGuide', fontName='Helvetica', fontSize=10, leading=14.8,
                          textColor=NAVY, spaceAfter=10))
styles.add(ParagraphStyle('BulletGuide', parent=styles['BodyGuide'], leftIndent=11,
                          firstLineIndent=-9, spaceAfter=6))
styles.add(ParagraphStyle('CellGuide', parent=styles['BodyGuide'], fontSize=9, leading=12,
                          spaceAfter=0))
styles.add(ParagraphStyle('CodeGuide', fontName='Courier', fontSize=7.5, leading=10.8,
                          backColor=PALE, borderPadding=10, spaceBefore=5, spaceAfter=15))


def inline(value: str) -> str:
    value = html.escape(value)
    value = re.sub(r'\[([^\]]+)\]\((https?://[^)]+)\)',
                   r'<link href="\2" color="#007d78"><u>\1</u></link>', value)
    value = re.sub(r'\*\*(.+?)\*\*', r'<b>\1</b>', value)
    value = re.sub(r'`([^`]+)`', r'<font name="Courier">\1</font>', value)
    return value


def vector_diagram(path: Path) -> Drawing:
    """Load the local primitive SVG format emitted by build_diagrams.py."""
    root = ET.parse(path).getroot()
    width, height = float(root.attrib['width']), float(root.attrib['height'])
    drawing = Drawing(width, height)
    for node in root:
        kind = node.tag.rsplit('}', 1)[-1]
        a = node.attrib
        if kind == 'rect':
            x, y, w, h = (float(a.get(k, 0)) for k in ('x', 'y', 'width', 'height'))
            drawing.add(Rect(x, height-y-h, w, h, rx=float(a.get('rx', 0)),
                             ry=float(a.get('rx', 0)), fillColor=colors.toColor(a.get('fill', 'white')),
                             strokeColor=colors.toColor(a['stroke']) if 'stroke' in a else None,
                             strokeWidth=float(a.get('stroke-width', 1))))
        elif kind == 'line':
            drawing.add(Line(float(a['x1']), height-float(a['y1']), float(a['x2']), height-float(a['y2']),
                             strokeColor=colors.toColor(a['stroke']), strokeWidth=float(a['stroke-width']),
                             strokeDashArray=[6, 6] if 'stroke-dasharray' in a else None))
        elif kind == 'polygon':
            points = []
            for point in a['points'].split():
                x, y = point.split(',')
                points.extend((float(x), height-float(y)))
            drawing.add(Polygon(points, fillColor=colors.toColor(a['fill']), strokeColor=None))
        elif kind == 'text':
            name = 'Helvetica-Bold' if a.get('font-weight') == 'bold' else 'Helvetica'
            size = float(a['font-size'])
            ascent, descent = pdfmetrics.getAscentDescent(name, size)
            drawing.add(String(float(a['x']), height-float(a['y'])-(ascent+descent)/2,
                               node.text or '', fontName=name, fontSize=size,
                               fillColor=colors.toColor(a['fill']), textAnchor=a.get('text-anchor', 'middle')))
        else:
            raise ValueError(f'Unsupported diagram primitive: {kind}')
    scale = min(CONTENT / width, 370 / height)
    drawing.scale(scale, scale)
    drawing.width *= scale
    drawing.height *= scale
    drawing.hAlign = 'CENTER'
    return drawing


def draw_page(canvas, doc):
    canvas.saveState()
    canvas.setStrokeColor(TEAL)
    canvas.setLineWidth(2)
    canvas.line(MARGIN, HEIGHT - 40, WIDTH - MARGIN, HEIGHT - 40)
    canvas.setFont('Helvetica-Bold', 8)
    canvas.setFillColor(TEAL)
    canvas.drawString(MARGIN, HEIGHT - 29, 'GUÍA DE DESARROLLO / .NET 10 LTS')
    canvas.setFont('Helvetica', 8)
    canvas.setFillColor(GRAY)
    canvas.drawRightString(WIDTH - MARGIN, HEIGHT - 29, 'EDICIÓN 3.0 / NOV 2026')
    canvas.setStrokeColor(colors.HexColor('#d9e2e7'))
    canvas.setLineWidth(.5)
    canvas.line(MARGIN, 42, WIDTH - MARGIN, 42)
    canvas.setFont('Helvetica', 8)
    canvas.drawString(MARGIN, 28, 'Dapper + SQLite + ASP.NET Core + xUnit.net')
    canvas.drawRightString(WIDTH - MARGIN, 28, f'{doc.page:02d}')
    canvas.restoreState()


def parse_page(source: str, index: int):
    lines = source.strip().splitlines()
    flow = []
    i = 0
    if index == 0:
        flow.append(Spacer(1, 40))
    while i < len(lines):
        line = lines[i].strip()
        if not line:
            i += 1
            continue
        picture = re.fullmatch(r'!\[([^\]]*)\]\(([^)]+)\)', line)
        if picture:
            picture_path = (ROOT / 'docs' / picture.group(2)).resolve()
            if not picture_path.is_relative_to((ROOT / 'docs/assets').resolve()):
                raise ValueError('Guide images must be inside docs/assets')
            if picture_path.with_suffix('.svg').exists():
                drawing = vector_diagram(picture_path.with_suffix('.svg'))
            else:
                drawing = Image(str(picture_path))
                scale = min(CONTENT / drawing.imageWidth, 370 / drawing.imageHeight)
                drawing.drawWidth = drawing.imageWidth * scale
                drawing.drawHeight = drawing.imageHeight * scale
                drawing.hAlign = 'CENTER'
            flow.extend([drawing, Spacer(1, 12)])
            i += 1
            continue
        if line.startswith('```'):
            code = []
            i += 1
            while i < len(lines) and not lines[i].strip().startswith('```'):
                code.append(lines[i])
                i += 1
            for code_line in code:
                if len(code_line) * 4.5 > CONTENT - 20:
                    raise ValueError(f'Code line too wide on designed page {index + 1}: {code_line}')
            flow.append(Preformatted('\n'.join(code), styles['CodeGuide']))
            i += 1
            continue
        if line.startswith('|'):
            rows = []
            while i < len(lines) and lines[i].strip().startswith('|'):
                parts = [s.strip() for s in lines[i].strip().strip('|').split('|')]
                if not all(re.fullmatch(r':?-+:?', p) for p in parts):
                    rows.append([Paragraph(inline(p), styles['CellGuide']) for p in parts])
                i += 1
            table = Table(rows, colWidths=[CONTENT * .36, CONTENT * .64], hAlign='LEFT')
            table.setStyle(TableStyle([
                ('BACKGROUND', (0, 0), (-1, 0), PALE),
                ('LINEBELOW', (0, 0), (-1, 0), 1, TEAL),
                ('LINEBELOW', (0, 1), (-1, -1), .3, colors.HexColor('#d9e2e7')),
                ('VALIGN', (0, 0), (-1, -1), 'TOP'),
                ('LEFTPADDING', (0, 0), (-1, -1), 9),
                ('RIGHTPADDING', (0, 0), (-1, -1), 9),
                ('TOPPADDING', (0, 0), (-1, -1), 8),
                ('BOTTOMPADDING', (0, 0), (-1, -1), 8),
            ]))
            flow.extend([table, Spacer(1, 13)])
            continue
        if line.startswith('# '):
            style = styles['TitleGuide'] if index == 0 else styles['SectionGuide']
            flow.append(Paragraph(inline(line[2:]), style))
            i += 1
            continue
        if line.startswith('## '):
            flow.append(Paragraph(inline(line[3:]), styles['SubGuide']))
            i += 1
            continue
        bullet = line.startswith('- ') or re.match(r'^\d+\. ', line)
        if bullet:
            if line.startswith('- '):
                line = '• ' + line[2:]
            flow.append(Paragraph(inline(line), styles['BulletGuide']))
            i += 1
            continue
        paragraph = [line]
        i += 1
        while i < len(lines) and lines[i].strip() and not lines[i].startswith(('#', '```', '|', '- ')):
            paragraph.append(lines[i].strip())
            i += 1
        flow.append(Paragraph(inline(' '.join(paragraph)), styles['BodyGuide']))
    return flow


def main():
    source = (ROOT / 'docs/guia.md').read_text(encoding='utf-8')
    if '{{' in source:
        raise ValueError('Unresolved placeholders in guide')
    pages = source.split('<!-- pagebreak -->')
    output = ROOT / 'docs/guia.pdf'
    doc = BaseDocTemplate(str(output), pagesize=(WIDTH, HEIGHT), leftMargin=MARGIN,
                          rightMargin=MARGIN, topMargin=65, bottomMargin=57,
                          title='Desarrollo de aplicaciones con .NET 10',
                          author='Eliaquín Encarnación',
                          subject='Arquitectura, Identity, autorización, Dapper y pruebas automatizadas',
                          pageCompression=1)
    frame = Frame(MARGIN, 57, CONTENT, HEIGHT - 122, leftPadding=0, rightPadding=0,
                  topPadding=0, bottomPadding=0)
    doc.addPageTemplates(PageTemplate(id='guide', frames=frame, onPage=draw_page))
    story = []
    for i, page in enumerate(pages):
        if i:
            story.append(PageBreak())
        story.extend(parse_page(page, i))
    doc.build(story)
    print(f'Created {output} ({len(pages)} designed pages)')


if __name__ == '__main__':
    main()
