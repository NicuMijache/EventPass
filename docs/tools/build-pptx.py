# Construiește prezentare.pptx din imaginile slide-urilor și notele vorbitorului (apelat de build-slides.mjs).
# Utilizare: python build-pptx.py <folder cu slide-NN.png și notes.json> <fișier .pptx>
import json
import sys
from pathlib import Path

from pptx import Presentation
from pptx.util import Emu, Inches

src, out = Path(sys.argv[1]), Path(sys.argv[2])
notes = json.loads((src / "notes.json").read_text(encoding="utf-8"))
images = sorted(src.glob("slide-*.png"))
assert len(images) == len(notes), f"{len(images)} imagini, dar {len(notes)} note"

prs = Presentation()
prs.slide_width, prs.slide_height = Inches(13.333), Inches(7.5)  # 16:9
blank = prs.slide_layouts[6]
for image, note in zip(images, notes):
    slide = prs.slides.add_slide(blank)
    slide.shapes.add_picture(str(image), Emu(0), Emu(0), width=prs.slide_width, height=prs.slide_height)
    text = note["notes"]
    if note.get("time"):
        text = f"[{note['time']}] {text}"
    slide.notes_slide.notes_text_frame.text = text

prs.core_properties.title = "EventPass — prezentare"
prs.core_properties.author = "Nicu Mijache"
prs.save(out)
print(f"✔ {out.name}: {len(images)} slide-uri, cu notele vorbitorului")
