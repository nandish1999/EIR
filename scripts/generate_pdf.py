#!/usr/bin/env python3
"""
Generate a professional PDF from the supervisor-brief markdown documentation.
Uses fpdf2 with TTF Unicode fonts for proper character support.
"""

import os
import re
from fpdf import FPDF

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
PROJECT_DIR = os.path.dirname(SCRIPT_DIR)
INPUT_MD = os.path.join(PROJECT_DIR, "candidateK_supervisor_brief_documentation.md")
OUTPUT_PDF = os.path.join(PROJECT_DIR, "candidateK_supervisor_brief_documentation.pdf")

# System TTF fonts
FONT_DIR = "/System/Library/Fonts/Supplemental"
FONT_REGULAR = os.path.join(FONT_DIR, "Arial.ttf")
FONT_BOLD = os.path.join(FONT_DIR, "Arial Bold.ttf")
FONT_ITALIC = os.path.join(FONT_DIR, "Arial Italic.ttf")
FONT_MONO = "/System/Library/Fonts/Monaco.ttf"


class MarkdownPDF(FPDF):
    def __init__(self):
        super().__init__(format='A4')
        self.set_auto_page_break(auto=True, margin=25)
        self.set_margins(22, 22, 22)
        # Register Unicode TTF fonts
        self.add_font('body', '', FONT_REGULAR)
        self.add_font('body', 'B', FONT_BOLD)
        self.add_font('body', 'I', FONT_ITALIC)
        self.add_font('mono', '', FONT_MONO)

    def header(self):
        if self.page_no() > 1:
            self.set_font('body', 'I', 8)
            self.set_text_color(120, 120, 120)
            self.cell(0, 8, 'Candidate K Method and Evaluation', align='L')
            self.ln(4)
            self.set_draw_color(200, 200, 200)
            self.line(22, self.get_y(), self.w - 22, self.get_y())
            self.ln(6)

    def footer(self):
        self.set_y(-18)
        self.set_font('body', '', 8)
        self.set_text_color(120, 120, 120)
        self.cell(0, 10, f'Page {self.page_no()}', align='C')


def clean_inline(text):
    """Strip markdown inline formatting for PDF text."""
    text = re.sub(r'\*\*(.+?)\*\*', r'\1', text)
    text = re.sub(r'(?<!\*)\*([^*]+?)\*(?!\*)', r'\1', text)
    text = re.sub(r'`(.+?)`', r'\1', text)
    text = re.sub(r'\[([^\]]+)\]\([^\)]+\)', r'\1', text)
    text = re.sub(r'\$\$', '', text)
    text = re.sub(r'\$([^$]+)\$', r'\1', text)
    return text


def render_table(pdf, table_lines):
    """Parse and render a markdown table."""
    rows = []
    sep_idx = None
    for idx, line in enumerate(table_lines):
        line = line.strip()
        if not line.startswith('|'):
            continue
        cells = [c.strip() for c in line.split('|')[1:-1]]
        if all(re.match(r'^[-:]+$', c) for c in cells if c):
            sep_idx = idx
            continue
        rows.append(cells)

    if not rows:
        return

    headers = rows[0] if sep_idx is not None and sep_idx > 0 else None
    data_rows = rows[1:] if headers else rows
    n_cols = len(rows[0])

    if headers:
        headers = [clean_inline(h) for h in headers]
    data_rows = [[clean_inline(c) for c in row] for row in data_rows]

    usable_w = pdf.w - 44
    # Compute column widths from content length
    max_lens = [0] * n_cols
    for row in ([headers] if headers else []) + data_rows:
        for j, c in enumerate(row):
            if j < n_cols:
                max_lens[j] = max(max_lens[j], len(c))
    total = sum(max_lens) or 1
    col_widths = [max(usable_w * (ml / total), 16) for ml in max_lens]
    scale = usable_w / sum(col_widths)
    col_widths = [w * scale for w in col_widths]

    row_h = 5.8

    total_rows = (1 if headers else 0) + len(data_rows)
    if pdf.get_y() + total_rows * row_h + 10 > pdf.h - 25:
        pdf.add_page()

    pdf.ln(2)

    if headers:
        pdf.set_font('body', 'B', 8.5)
        pdf.set_text_color(34, 34, 34)
        pdf.set_fill_color(235, 235, 235)
        for j, h in enumerate(headers):
            if j < len(col_widths):
                pdf.cell(col_widths[j], row_h, h, border=1, fill=True)
        pdf.ln(row_h)

    pdf.set_font('body', '', 8.5)
    for ri, row in enumerate(data_rows):
        pdf.set_text_color(26, 26, 26)
        fill = ri % 2 == 1
        if fill:
            pdf.set_fill_color(250, 250, 250)
        for j in range(n_cols):
            cell_text = row[j] if j < len(row) else ''
            if j < len(col_widths):
                pdf.cell(col_widths[j], row_h, cell_text, border=1, fill=fill)
        pdf.ln(row_h)

    pdf.ln(3)


def build_pdf(md_text):
    pdf = MarkdownPDF()
    pdf.add_page()

    lines = md_text.split('\n')
    i = 0
    in_code = False
    code_buf = []

    while i < len(lines):
        line = lines[i]

        # --- Code blocks ---
        if line.strip().startswith('```'):
            if in_code:
                code_text = '\n'.join(code_buf)
                pdf.ln(2)
                pdf.set_font('mono', '', 9.0) # increased from 7.8
                pdf.set_text_color(51, 51, 51)
                pdf.set_fill_color(248, 248, 248)
                pdf.set_draw_color(220, 220, 220)
                code_lines = code_text.split('\n')
                block_h = len(code_lines) * 4.4 + 6 # increased
                y = pdf.get_y()
                if y + block_h > pdf.h - 25:
                    pdf.add_page()
                    y = pdf.get_y()
                pdf.rect(22, y, pdf.w - 44, block_h, style='DF')
                pdf.set_xy(25, y + 3)
                for cl in code_lines:
                    pdf.cell(0, 4.4, cl.replace('\t', '    ')) # increased
                    pdf.ln(4.4) # increased
                pdf.set_y(y + block_h + 2)
                pdf.ln(2)
                code_buf = []
                in_code = False
            else:
                in_code = True
                code_buf = []
            i += 1
            continue

        if in_code:
            code_buf.append(line)
            i += 1
            continue

        # --- Tables ---
        if '|' in line and line.strip().startswith('|'):
            tbl = []
            while i < len(lines) and '|' in lines[i] and lines[i].strip().startswith('|'):
                tbl.append(lines[i])
                i += 1
            render_table(pdf, tbl)
            continue

        # --- H1 ---
        if line.startswith('# ') and not line.startswith('## '):
            text = line[2:].strip()
            pdf.ln(2)
            pdf.set_font('body', 'B', 20)
            pdf.set_text_color(17, 17, 17)
            pdf.multi_cell(0, 9, text)
            pdf.ln(2)
            pdf.set_draw_color(40, 40, 40)
            pdf.set_line_width(0.8)
            pdf.line(22, pdf.get_y(), pdf.w - 22, pdf.get_y())
            pdf.set_line_width(0.2)
            pdf.ln(6)
            i += 1
            continue

        # --- H2 ---
        if line.startswith('## '):
            text = line[3:].strip()
            pdf.ln(8)
            pdf.set_font('body', 'B', 14)
            pdf.set_text_color(26, 26, 26)
            pdf.multi_cell(0, 7, text)
            pdf.ln(1)
            pdf.set_draw_color(190, 190, 190)
            pdf.line(22, pdf.get_y(), pdf.w - 22, pdf.get_y())
            pdf.ln(4)
            i += 1
            continue

        # --- H3 ---
        if line.startswith('### '):
            text = line[4:].strip()
            pdf.ln(5)
            pdf.set_font('body', 'B', 11.5)
            pdf.set_text_color(50, 50, 50)
            pdf.multi_cell(0, 6, text)
            pdf.ln(2)
            i += 1
            continue

        # --- H4 ---
        if line.startswith('#### '):
            text = line[5:].strip()
            pdf.ln(3)
            pdf.set_font('body', 'B', 10.5)
            pdf.set_text_color(68, 68, 68)
            pdf.multi_cell(0, 6, text)
            pdf.ln(1)
            i += 1
            continue

        # --- HR ---
        if line.strip() == '---':
            pdf.ln(4)
            pdf.set_draw_color(200, 200, 200)
            pdf.line(22, pdf.get_y(), pdf.w - 22, pdf.get_y())
            pdf.ln(6)
            i += 1
            continue

        # --- Blockquote ---
        if line.strip().startswith('>'):
            quote = []
            while i < len(lines) and lines[i].strip().startswith('>'):
                quote.append(lines[i].strip().lstrip('> ').strip())
                i += 1
            text = clean_inline(' '.join(quote))
            pdf.set_font('body', 'I', 10)
            pdf.set_text_color(68, 68, 68)
            old_y = pdf.get_y()
            pdf.set_x(30)
            pdf.multi_cell(pdf.w - 52, 5.2, text)
            new_y = pdf.get_y()
            pdf.set_draw_color(170, 170, 170)
            pdf.set_line_width(0.8)
            pdf.line(26, old_y - 1, 26, new_y + 1)
            pdf.set_line_width(0.2)
            pdf.ln(3)
            continue

        # --- Bullet list ---
        if re.match(r'^(\s*)[-*]\s', line):
            pdf.set_font('body', '', 10)
            pdf.set_text_color(26, 26, 26)
            while i < len(lines) and re.match(r'^(\s*)[-*]\s', lines[i]):
                indent = len(lines[i]) - len(lines[i].lstrip())
                text = clean_inline(re.sub(r'^(\s*)[-*]\s+', '', lines[i]).strip())
                x_off = 26 + (indent // 2) * 6
                pdf.set_x(x_off)
                pdf.cell(4, 5.2, '\u2022')
                pdf.set_x(x_off + 5)
                pdf.multi_cell(pdf.w - x_off - 27, 5.2, text)
                pdf.ln(1)
                i += 1
            pdf.ln(2)
            continue

        # --- Numbered list ---
        if re.match(r'^\d+\.\s', line):
            pdf.set_font('body', '', 10)
            pdf.set_text_color(26, 26, 26)
            num = 1
            while i < len(lines) and re.match(r'^\d+\.\s', lines[i]):
                text = clean_inline(re.sub(r'^\d+\.\s+', '', lines[i]).strip())
                pdf.set_x(26)
                pdf.cell(8, 5.2, f'{num}.')
                pdf.set_x(34)
                pdf.multi_cell(pdf.w - 56, 5.2, text)
                pdf.ln(1)
                num += 1
                i += 1
            pdf.ln(2)
            continue

        # --- Metadata bold lines ---
        if line.startswith('**') and '**' in line[2:]:
            text = clean_inline(line.strip())
            pdf.set_font('body', 'B', 10)
            pdf.set_text_color(26, 26, 26)
            pdf.multi_cell(0, 5.5, text)
            pdf.ln(1)
            i += 1
            continue

        # --- $$-math blocks (render as plain text) ---
        if line.strip().startswith('$$'):
            i += 1
            continue

        # --- Paragraph ---
        if line.strip():
            para = []
            while (i < len(lines) and lines[i].strip()
                   and not lines[i].startswith('#')
                   and not lines[i].startswith('```')
                   and not (lines[i].strip().startswith('|') and '|' in lines[i])
                   and not lines[i].strip().startswith('>')
                   and not lines[i].strip() == '---'
                   and not re.match(r'^[-*]\s', lines[i])
                   and not re.match(r'^\d+\.\s', lines[i])
                   and not lines[i].startswith('**')
                   and not lines[i].strip().startswith('$$')):
                para.append(lines[i].strip())
                i += 1
            text = clean_inline(' '.join(para))
            pdf.set_font('body', '', 10)
            pdf.set_text_color(26, 26, 26)
            pdf.multi_cell(0, 5.2, text)
            pdf.ln(2)
            continue

        i += 1

    pdf.output(OUTPUT_PDF)
    print(f"PDF generated: {OUTPUT_PDF}")
    print(f"File size: {os.path.getsize(OUTPUT_PDF):,} bytes")
    print(f"Pages: {pdf.page_no()}")


if __name__ == '__main__':
    print(f"Reading: {INPUT_MD}")
    with open(INPUT_MD, 'r', encoding='utf-8') as f:
        md_text = f.read()
    build_pdf(md_text)
