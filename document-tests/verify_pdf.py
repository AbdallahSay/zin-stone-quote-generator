"""Verify actual PDF text and render each page for review (PyMuPDF 1.26.7)."""
import json
import pathlib
import sys
import unicodedata
import fitz

root = pathlib.Path(sys.argv[1])
results = []
for case in json.loads((root / 'manifest.json').read_text(encoding='utf-8-sig')):
    pdf = root / (case['id'] + '.pdf')
    with fitz.open(pdf) as doc:
        text = unicodedata.normalize('NFKC', '\n'.join(p.get_text() for p in doc))
        attention = next(line for line in text.splitlines() if 'عناية' in line)
        assert 'عناية ' + case['title'] in attention, (case['id'], attention)
        if case['title'] != 'المهندس':
            assert 'عناية المهندس :' not in attention, (case['id'], attention)
        # PDF extraction can reorder RTL name words; verify every word is retained.
        assert all(word in text for word in case['name'].split()), case['id']
        assert len(doc) == 1, (case['id'], 'Unexpected page count')
        doc[0].get_pixmap(matrix=fitz.Matrix(1.3, 1.3)).save(root / (case['id'] + '.png'))
        results.append({'case': case['id'], 'title': case['title'], 'attention': attention, 'pages': len(doc), 'pass': True})
(root / 'pdf-verification.json').write_text(json.dumps(results, ensure_ascii=False, indent=2), encoding='utf-8')
print(f'PASS: {len(results)} PDFs have correct extracted Arabic titles/names and one page; previews rendered.')
