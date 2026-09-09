"""Build Gialora-Documentation.docx from ARCHITECTURE.md.

A small, purpose-built Markdown -> Word converter. It handles exactly the subset
of Markdown used in the documentation: headings, paragraphs, bullet and numbered
lists, fenced code blocks, pipe tables, horizontal rules, and inline **bold** /
`code` / *italic*.

Run:  py docs/build_docx.py
"""

import re
import sys
from pathlib import Path

from docx import Document
from docx.enum.table import WD_TABLE_ALIGNMENT
from docx.enum.text import WD_ALIGN_PARAGRAPH
from docx.oxml import OxmlElement
from docx.oxml.ns import qn
from docx.shared import Pt, RGBColor, Inches

# Mediterranean palette, matching the application's own design tokens.
OLIVE = RGBColor(0x43, 0x52, 0x2F)
TERRACOTTA = RGBColor(0xA4, 0x47, 0x2A)
INK = RGBColor(0x2C, 0x2A, 0x26)
INK_SOFT = RGBColor(0x5F, 0x5A, 0x51)
CODE_BG = "F2EEE4"
RULE_GREY = "D8D0C0"

DOCS = Path(__file__).resolve().parent
SOURCE = DOCS / "ARCHITECTURE.md"
TARGET = DOCS / "Gialora-Documentation.docx"


# ---------------------------------------------------------------- helpers

def shade(element, fill):
    """Apply a background fill to a paragraph or table cell."""
    shd = OxmlElement("w:shd")
    shd.set(qn("w:val"), "clear")
    shd.set(qn("w:fill"), fill)
    element.get_or_add_pPr().append(shd) if element.tag.endswith("}p") else element.append(shd)


def shade_cell(cell, fill):
    tc_pr = cell._tc.get_or_add_tcPr()
    shd = OxmlElement("w:shd")
    shd.set(qn("w:val"), "clear")
    shd.set(qn("w:fill"), fill)
    tc_pr.append(shd)


def paragraph_border(paragraph, edge="bottom", size=6, color=RULE_GREY):
    p_pr = paragraph._p.get_or_add_pPr()
    borders = OxmlElement("w:pBdr")
    element = OxmlElement(f"w:{edge}")
    element.set(qn("w:val"), "single")
    element.set(qn("w:sz"), str(size))
    element.set(qn("w:space"), "4")
    element.set(qn("w:color"), color)
    borders.append(element)
    p_pr.append(borders)


INLINE = re.compile(r"(\*\*.+?\*\*|`[^`]+`|\*[^*]+\*)")


def add_inline(paragraph, text, base_size=None, base_color=None):
    """Render inline markup into runs on an existing paragraph."""
    # Unescape the pipes used inside table cells.
    text = text.replace("\\|", "|")

    for part in INLINE.split(text):
        if not part:
            continue

        if part.startswith("**") and part.endswith("**") and len(part) > 4:
            run = paragraph.add_run(part[2:-2])
            run.bold = True
        elif part.startswith("`") and part.endswith("`") and len(part) > 2:
            run = paragraph.add_run(part[1:-1])
            run.font.name = "Consolas"
            run.font.color.rgb = TERRACOTTA
            run.font.size = Pt((base_size or 10.5) - 0.5)
        elif part.startswith("*") and part.endswith("*") and len(part) > 2:
            run = paragraph.add_run(part[1:-1])
            run.italic = True
        else:
            run = paragraph.add_run(part)

        if base_size and not run.font.size:
            run.font.size = Pt(base_size)
        if base_color and run.font.color.rgb is None:
            run.font.color.rgb = base_color

    return paragraph


# ---------------------------------------------------------------- styles

def configure_styles(doc):
    normal = doc.styles["Normal"]
    normal.font.name = "Calibri"
    normal.font.size = Pt(10.5)
    normal.font.color.rgb = INK
    normal.paragraph_format.space_after = Pt(8)
    normal.paragraph_format.line_spacing = 1.15

    headings = {
        "Heading 1": (20, OLIVE, 18, 8),
        "Heading 2": (15, OLIVE, 14, 6),
        "Heading 3": (12.5, INK, 10, 4),
        "Heading 4": (11, INK_SOFT, 8, 3),
    }

    for name, (size, color, before, after) in headings.items():
        style = doc.styles[name]
        style.font.name = "Georgia"
        style.font.size = Pt(size)
        style.font.bold = True
        style.font.color.rgb = color
        style.paragraph_format.space_before = Pt(before)
        style.paragraph_format.space_after = Pt(after)
        style.paragraph_format.keep_with_next = True

    for section in doc.sections:
        section.left_margin = Inches(1.0)
        section.right_margin = Inches(1.0)
        section.top_margin = Inches(0.9)
        section.bottom_margin = Inches(0.9)


def add_footer_page_numbers(doc):
    footer = doc.sections[0].footer.paragraphs[0]
    footer.alignment = WD_ALIGN_PARAGRAPH.CENTER

    run = footer.add_run()
    for instr in ('begin', 'PAGE', 'end'):
        element = OxmlElement("w:fldChar") if instr in ("begin", "end") else OxmlElement("w:instrText")
        if instr in ("begin", "end"):
            element.set(qn("w:fldCharType"), instr)
        else:
            element.set(qn("xml:space"), "preserve")
            element.text = " PAGE "
        run._r.append(element)

    run.font.size = Pt(9)
    run.font.color.rgb = INK_SOFT


# ---------------------------------------------------------------- blocks

def add_code_block(doc, lines):
    for line in lines:
        paragraph = doc.add_paragraph()
        paragraph.paragraph_format.space_after = Pt(0)
        paragraph.paragraph_format.space_before = Pt(0)
        paragraph.paragraph_format.left_indent = Inches(0.2)
        shade(paragraph._p, CODE_BG)

        run = paragraph.add_run(line if line else " ")
        run.font.name = "Consolas"
        run.font.size = Pt(9)
        run.font.color.rgb = INK

    doc.add_paragraph().paragraph_format.space_after = Pt(4)


def split_row(line):
    """Split a pipe table row, honouring escaped pipes."""
    body = line.strip().strip("|")
    cells, current, escaped = [], "", False

    for ch in body:
        if escaped:
            current += "\\" + ch if ch == "|" else ch
            escaped = False
        elif ch == "\\":
            escaped = True
        elif ch == "|":
            cells.append(current.strip())
            current = ""
        else:
            current += ch

    cells.append(current.strip())
    return cells


def add_table(doc, rows):
    header, body = rows[0], rows[1:]
    table = doc.add_table(rows=1, cols=len(header))
    table.style = "Table Grid"
    table.alignment = WD_TABLE_ALIGNMENT.LEFT

    for index, text in enumerate(header):
        cell = table.rows[0].cells[index]
        cell.text = ""
        paragraph = cell.paragraphs[0]
        paragraph.paragraph_format.space_after = Pt(2)
        paragraph.paragraph_format.space_before = Pt(2)
        run = add_inline(paragraph, text).runs
        for r in run:
            r.bold = True
            r.font.size = Pt(9.5)
        shade_cell(cell, "EDE7DA")

    for line in body:
        cells = table.add_row().cells
        for index, text in enumerate(line[:len(header)]):
            cell = cells[index]
            cell.text = ""
            paragraph = cell.paragraphs[0]
            paragraph.paragraph_format.space_after = Pt(2)
            paragraph.paragraph_format.space_before = Pt(2)
            add_inline(paragraph, text, base_size=9.5)

    doc.add_paragraph().paragraph_format.space_after = Pt(4)


def add_rule(doc):
    paragraph = doc.add_paragraph()
    paragraph.paragraph_format.space_before = Pt(6)
    paragraph.paragraph_format.space_after = Pt(10)
    paragraph_border(paragraph)


# ---------------------------------------------------------------- driver

def convert(markdown, doc):
    lines = markdown.replace("\r\n", "\n").split("\n")
    index = 0
    first_heading = True

    while index < len(lines):
        line = lines[index]
        stripped = line.strip()

        # Fenced code block
        if stripped.startswith("```"):
            index += 1
            block = []
            while index < len(lines) and not lines[index].strip().startswith("```"):
                block.append(lines[index])
                index += 1
            index += 1
            add_code_block(doc, block)
            continue

        # Table
        if stripped.startswith("|") and index + 1 < len(lines) and set(lines[index + 1].strip()) <= set("|-: "):
            rows = [split_row(stripped)]
            index += 2  # skip the separator row
            while index < len(lines) and lines[index].strip().startswith("|"):
                rows.append(split_row(lines[index].strip()))
                index += 1
            add_table(doc, rows)
            continue

        # Horizontal rule
        if stripped in ("---", "***", "___"):
            add_rule(doc)
            index += 1
            continue

        # Heading
        match = re.match(r"^(#{1,4})\s+(.*)$", stripped)
        if match:
            level = len(match.group(1))
            text = match.group(2)

            if first_heading and level == 1:
                paragraph = doc.add_paragraph()
                paragraph.alignment = WD_ALIGN_PARAGRAPH.CENTER
                run = paragraph.add_run(text)
                run.font.name = "Georgia"
                run.font.size = Pt(28)
                run.font.bold = True
                run.font.color.rgb = OLIVE
                paragraph.paragraph_format.space_after = Pt(4)
                first_heading = False
            else:
                paragraph = doc.add_heading(level=level)
                add_inline(paragraph, text)
            index += 1
            continue

        # Bullet list
        if re.match(r"^[-*]\s+", stripped):
            paragraph = doc.add_paragraph(style="List Bullet")
            paragraph.paragraph_format.space_after = Pt(3)
            add_inline(paragraph, re.sub(r"^[-*]\s+", "", stripped))
            index += 1
            continue

        # Numbered list
        if re.match(r"^\d+\.\s+", stripped):
            paragraph = doc.add_paragraph(style="List Number")
            paragraph.paragraph_format.space_after = Pt(3)
            add_inline(paragraph, re.sub(r"^\d+\.\s+", "", stripped))
            index += 1
            continue

        # Blank line
        if not stripped:
            index += 1
            continue

        # Paragraph — join wrapped lines until a blank or a new block starts.
        buffer = [stripped]
        index += 1
        while index < len(lines):
            nxt = lines[index].strip()
            if not nxt or nxt.startswith(("#", "|", "```", "- ", "* ", "---")) or re.match(r"^\d+\.\s", nxt):
                break
            buffer.append(nxt)
            index += 1

        text = " ".join(buffer)
        paragraph = doc.add_paragraph()

        # A lone italic line directly under the title reads as a subtitle.
        if text.startswith("Version ") or text.startswith("Mediterranean meal planning"):
            paragraph.alignment = WD_ALIGN_PARAGRAPH.CENTER
            run = paragraph.add_run(text)
            run.font.color.rgb = INK_SOFT
            run.font.size = Pt(11)
        else:
            add_inline(paragraph, text)


def main():
    if not SOURCE.exists():
        sys.exit(f"Missing {SOURCE}")

    doc = Document()
    configure_styles(doc)
    add_footer_page_numbers(doc)
    convert(SOURCE.read_text(encoding="utf-8"), doc)

    try:
        doc.save(TARGET)
        written = TARGET
    except PermissionError:
        # Word holds an exclusive lock on an open document. Rather than failing
        # the build, write alongside it and say what to do.
        written = TARGET.with_name(f"{TARGET.stem}-new{TARGET.suffix}")
        doc.save(written)
        print(f"'{TARGET.name}' is open in Word, so it could not be replaced.")
        print(f"Close it and delete it, then rename '{written.name}' - or just re-run this script.")

    print(f"Wrote {written} ({written.stat().st_size:,} bytes)")


if __name__ == "__main__":
    main()
