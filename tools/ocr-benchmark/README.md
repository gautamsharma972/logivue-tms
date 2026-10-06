# OCR benchmark

Synthetic delivery challans with known answers, to measure the paper-POD reader (see `docs/POD_DELIVERY_MANAGEMENT_INTEGRATION.md`, "OCR").

```bash
cd tools/ocr-benchmark
python3 gen.py                  # needs Pillow; writes s01…s11 (jpg/pdf) and truth.json
node run.mjs report.json        # API on http://localhost:5080 with Deliveries:Ocr:Enabled and the model pulled
```

The samples cover a clean table, a free-text letter, a skewed noisy scan, a low-resolution fax, a handwritten form, a phone photo, a scanned PDF, a Hindi/English form,
a page with most fields missing, a stamp over the text and an all-handwritten note. The last run also uploads the clean sample against a delivery whose system quantity differs, to see the mismatch caught.
These are generated pages, not real challans: they say how the reader behaves, not how accurate it is on your paper.
