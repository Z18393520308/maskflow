FROM pytorch/pytorch:2.9.1-cuda12.8-cudnn9-runtime

ENV PYTHONDONTWRITEBYTECODE=1
ENV PYTHONUNBUFFERED=1
ENV SAM3_MODEL=/models/sam3.pt
ENV SAM3_MAX_INFERENCE_SIDE=1024
ENV SAM3_REQUIRE_INTERNAL_KEY=true

WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends git libglib2.0-0 libgl1 \
    && rm -rf /var/lib/apt/lists/*

COPY requirements-docker.txt .
RUN pip install --no-cache-dir -r requirements-docker.txt

# Pre-cache CLIP ViT-B-32 for automatic category labeling on /api/segment (auto mode).
RUN python - <<'PY'
import hashlib
import urllib.request
from pathlib import Path

expected = "40d365715913c9da98579312b702a82c18be219cc2a73407c4526f58eba950af"
url = f"https://openaipublic.azureedge.net/clip/models/{expected}/ViT-B-32.pt"
dest = Path.home() / ".cache" / "clip" / "ViT-B-32.pt"
dest.parent.mkdir(parents=True, exist_ok=True)
if not dest.exists():
    print("Downloading CLIP ViT-B-32.pt...")
    urllib.request.urlretrieve(url, dest)
digest = hashlib.sha256(dest.read_bytes()).hexdigest()
if digest != expected:
    raise SystemExit(f"CLIP checksum mismatch: {digest}")
print(f"CLIP model ready at {dest}")
PY
RUN python -c "import clip; clip.load('ViT-B/32', device='cpu')"

COPY app ./app

EXPOSE 8001

CMD ["uvicorn", "app.main:app", "--host", "0.0.0.0", "--port", "8001"]
