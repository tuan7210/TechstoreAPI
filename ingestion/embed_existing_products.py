#!/usr/bin/env python3
"""
Embed existing products JSONL into Chroma for RAG (no DB required).
Requires products JSONL exported earlier (default: ingestion/output/products.jsonl)
Environment variables:
  PRODUCTS_PATH (default: ingestion/output/products.jsonl)
  CHROMA_PATH   (default: ingestion/chroma)
  COLLECTION_NAME (default: products)
  EMBED_MODEL   (default: sentence-transformers/all-MiniLM-L6-v2)
  EMBED_BATCH   (default: 64)
  OPENAI_API_KEY (optional fallback if sentence-transformers unavailable)
  OPENAI_EMBED_MODEL (default: text-embedding-3-small)
Run (PowerShell):
  python ingestion/embed_existing_products.py
"""
import os, json, sys, decimal, re, unicodedata
from typing import Any, Dict, List

try:
    import chromadb  # type: ignore
    from chromadb.utils import embedding_functions  # type: ignore
except Exception as e:
    sys.exit(f"[error] chromadb not available: {e}. Install requirements first.")

try:
    from sentence_transformers import SentenceTransformer  # type: ignore
except Exception:
    SentenceTransformer = None  # type: ignore

# --- NORMALIZATION UTILS ---
def normalize_for_embed(text: str) -> str:
    if not text:
        return ""
    text = unicodedata.normalize("NFC", text)
    text = text.lower()
    text = re.sub(r"\s+", " ", text).strip()
    return text

def normalize_for_match(text: str) -> str:
    t = unicodedata.normalize("NFKD", text or "")
    t = "".join(c for c in t if not unicodedata.combining(c))
    return t.lower()

PRODUCTS_PATH = os.environ.get("PRODUCTS_PATH", os.path.join("ingestion", "output", "products.jsonl"))
CHROMA_PATH = os.environ.get("CHROMA_PATH", os.path.join("ingestion", "chroma"))
COLLECTION_NAME = os.environ.get("COLLECTION_NAME", "products")
EMBED_MODEL = os.environ.get("EMBED_MODEL", "sentence-transformers/paraphrase-multilingual-MiniLM-L12-v2")
OPENAI_API_KEY = os.environ.get("OPENAI_API_KEY")
OPENAI_EMBED_MODEL = os.environ.get("OPENAI_EMBED_MODEL", "text-embedding-3-small")
BATCH = int(os.environ.get("EMBED_BATCH", "64"))
CHUNK_SIZE = 99999  # No chunking, 1 product = 1 embedding
CHUNK_OVERLAP = 0

if not os.path.exists(PRODUCTS_PATH):
    sys.exit(f"[error] Products file not found: {PRODUCTS_PATH}. Run extract_products.py first (ENABLE_EMBED optional).")

# Load rows
rows: List[Dict[str, Any]] = []
with open(PRODUCTS_PATH, "r", encoding="utf-8") as f:
    for line in f:
        line = line.strip()
        if not line:
            continue
        try:
            rows.append(json.loads(line))
        except Exception:
            continue

if not rows:
    sys.exit("[error] No product rows loaded.")

print(f"[info] Loaded {len(rows)} products from {PRODUCTS_PATH}")

# Flatten specs
import json as _json

def flatten_spec_for_text(spec: Any) -> str:
    if spec is None:
        return ""
    if isinstance(spec, dict):
        parts = []
        for k, v in spec.items():
            try:
                if isinstance(v, (dict, list)):
                    v_str = _json.dumps(v, ensure_ascii=False)
                else:
                    v_str = str(v)
                parts.append(f"{k}: {v_str}")
            except Exception:
                parts.append(f"{k}: {v}")
        return "; ".join(parts)
    if isinstance(spec, list):
        try:
            return "; ".join([_json.dumps(x, ensure_ascii=False) if isinstance(x, (dict, list)) else str(x) for x in spec])
        except Exception:
            return "; ".join([str(x) for x in spec])
    if isinstance(spec, str):
        return spec
    return str(spec)


def build_content(row: Dict[str, Any]) -> str:
    name = (row.get("name") or "").strip()
    brand = (row.get("brand") or "").strip()
    category = (row.get("category_name") or "").strip()
    description = (row.get("description") or "").strip()
    use_case = (row.get("use_case") or "").strip()
    usp = (row.get("usp") or "").strip()
    spec_text = flatten_spec_for_text(row.get("specifications"))
    header = name
    if brand:
        header += f" - {brand}"
    if category:
        header += f" | Danh mục: {category}"
    parts = [header]
    if description: parts.append(description)
    if use_case: parts.append(f"Mục đích sử dụng: {use_case}")
    if usp: parts.append(f"Điểm nổi bật: {usp}")
    if spec_text: parts.append(f"Thông số: {spec_text}")
    return ". ".join([p for p in parts if p]).strip()


def _sanitize_value(v: Any) -> Any:
    if isinstance(v, decimal.Decimal):
        try:
            return float(v)
        except Exception:
            return str(v)
    if v is None or isinstance(v, (str, int, float, bool)):
        return v
    return str(v)


def sanitize_metadata(meta: Dict[str, Any]) -> Dict[str, Any]:
    return {k: _sanitize_value(v) for k, v in meta.items()}


def chunk_text(text: str, size: int = CHUNK_SIZE, overlap: int = CHUNK_OVERLAP):
    if not text: return []
    chunks = []
    start = 0
    n = len(text)
    while start < n:
        end = min(n, start + size)
        chunks.append(text[start:end])
        if end == n: break
        start = max(end - overlap, start + 1)
    return chunks

client = chromadb.PersistentClient(path=CHROMA_PATH)

# Xóa collection nếu đã tồn tại để đảm bảo dữ liệu mới nhất
try:
    client.delete_collection(name=COLLECTION_NAME)
    print(f"[info] Deleted existing collection '{COLLECTION_NAME}' for clean embed.")
except Exception:
    pass
collection = client.get_or_create_collection(name=COLLECTION_NAME)

# Embedding function
ef = None
try:
    if SentenceTransformer is not None:
        st_model = SentenceTransformer(EMBED_MODEL)
        class STEmbed:
            def __call__(self, texts: List[str]):
                return st_model.encode(texts, normalize_embeddings=True).tolist()
        ef = STEmbed()
    elif OPENAI_API_KEY and embedding_functions:
        ef = embedding_functions.OpenAIEmbeddingFunction(api_key=OPENAI_API_KEY, model_name=OPENAI_EMBED_MODEL)
except Exception as e:
    print(f"[warn] Failed to init embedding model: {e}")

if ef is None:
    sys.exit("[error] No embedding function available. Install sentence-transformers or set OPENAI_API_KEY.")

documents: List[str] = []
metadatas: List[Dict[str, Any]] = []
ids: List[str] = []

for row in rows:
    pid = row.get("product_id")
    content = build_content(row)
    content_norm = normalize_for_embed(content)
    meta_base = {
        "product_id": pid,
        "name": row.get("name"),
        "brand": row.get("brand"),
        "category_name": row.get("category_name"),
        "price": row.get("price"),
        "image_url": row.get("image_url"),
        "use_case": (row.get("use_case") or "").strip(),
        "usp": (row.get("usp") or "").strip(),
        "spec_text": flatten_spec_for_text(row.get("specifications"))[:1200],
    }
    # 1 product = 1 embedding
    documents.append(content_norm)
    m = dict(meta_base)
    m["chunk_index"] = 0
    metadatas.append(sanitize_metadata(m))
    ids.append(f"p{pid}_c0")
    # Log image_url để debug
    print(f"[debug] product_id={pid}, image_url={row.get('image_url')}")

if not documents:
    sys.exit("[error] No documents prepared.")

print(f"[embed] Upserting {len(documents)} chunks into '{COLLECTION_NAME}' at {CHROMA_PATH}")
os.makedirs(CHROMA_PATH, exist_ok=True)

try:
    start = 0
    total = len(documents)
    while start < total:
        end = min(total, start + BATCH)
        batch_docs = documents[start:end]
        batch_ids = ids[start:end]
        batch_metas = metadatas[start:end]
        vectors = ef(batch_docs)
        collection.add(ids=batch_ids, documents=batch_docs, metadatas=batch_metas, embeddings=vectors)
        start = end
    print("[embed] Done")
except Exception as e:
    sys.exit(f"[error] Failed during upsert: {e}")

print("[info] RAG vector store ready.")
