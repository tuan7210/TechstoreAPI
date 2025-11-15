#!/usr/bin/env python3
"""
Extract product data from MySQL for AI chatbox training / vectorization.
- Connects to MySQL using env or .env
- Detects optional columns (use_case, usp) safely
- Exports records to JSONL with parsed specifications

Environment variables:
- DB_HOST (default: localhost)
- DB_PORT (default: 3306)
- DB_USER (required)
- DB_PASSWORD (required)
- DB_NAME (default: tech_store)
- OUTPUT_PATH (default: ingestion/output/products.jsonl)

Requires: mysql-connector-python, python-dotenv (optional, for .env loading)
"""
import json
import os
import sys
from typing import Dict, List, Any, Iterable, Tuple
from datetime import date, datetime

try:
    from dotenv import load_dotenv  # type: ignore
    load_dotenv()
except Exception:
    # dotenv is optional; continue if not present
    pass

import mysql.connector  # type: ignore
from mysql.connector import Error  # type: ignore

# Optional embedding stack
try:
    import chromadb  # type: ignore
    from chromadb.utils import embedding_functions  # type: ignore
except Exception:
    chromadb = None
    embedding_functions = None
try:
    from sentence_transformers import SentenceTransformer  # type: ignore
except Exception:
    SentenceTransformer = None  # type: ignore

DEFAULT_DB_NAME = "tech_store"
DEFAULT_OUTPUT_PATH = os.environ.get("OUTPUT_PATH", os.path.join("ingestion", "output", "products.jsonl"))
DEFAULT_CHROMA_PATH = os.environ.get("CHROMA_PATH", os.path.join("ingestion", "chroma"))
DEFAULT_COLLECTION = os.environ.get("COLLECTION_NAME", "products")
ENABLE_EMBED = os.environ.get("ENABLE_EMBED", "false").lower() in ("1", "true", "yes")
CHUNK_SIZE = int(os.environ.get("CHUNK_SIZE", "1200"))
CHUNK_OVERLAP = int(os.environ.get("CHUNK_OVERLAP", "150"))


def get_db_config() -> Dict[str, Any]:
    cfg = {
        "host": os.environ.get("DB_HOST", "localhost"),
        "port": int(os.environ.get("DB_PORT", "3306")),
        "user": os.environ.get("DB_USER"),
        "password": os.environ.get("DB_PASSWORD"),
        "database": os.environ.get("DB_NAME", DEFAULT_DB_NAME),
    }
    missing = [k for k, v in cfg.items() if v in (None, "") and k in ("user", "password")]
    if missing:
        raise RuntimeError(f"Missing required DB env vars: {', '.join(missing)}. Set DB_USER and DB_PASSWORD.")
    return cfg


def open_connection():
    cfg = get_db_config()
    return mysql.connector.connect(**cfg)


def product_columns(conn) -> List[str]:
    sql = (
        "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS "
        "WHERE TABLE_SCHEMA = %s AND TABLE_NAME = 'Product'"
    )
    cur = conn.cursor()
    cur.execute(sql, (conn.database,))
    cols = [r[0] for r in cur.fetchall()]
    cur.close()
    return cols


def category_available(conn) -> bool:
    sql = (
        "SELECT 1 FROM INFORMATION_SCHEMA.TABLES "
        "WHERE TABLE_SCHEMA = %s AND TABLE_NAME = 'Category' LIMIT 1"
    )
    cur = conn.cursor()
    cur.execute(sql, (conn.database,))
    exists = cur.fetchone() is not None
    cur.close()
    return exists


def build_select(cols: List[str], *, include_category: bool) -> str:
    # Base required columns
    selected = ["product_id", "name", "description", "specifications"]

    # Add brand if present (useful context)
    if "brand" in cols:
        selected.append("brand")

    # Optional requested fields
    if "use_case" in cols:
        selected.append("use_case")
    if "usp" in cols:
        selected.append("usp")

    # Commonly useful fields if present
    for col in [
        "price",
        "original_price",
        "stock_quantity",
        "category_id",
        "image_url",
        "rating",
        "review_count",
        "is_new",
        "is_best_seller",
        "created_at",
        "updated_at",
    ]:
        if col in cols and col not in selected:
            selected.append(col)

    # Build comma-separated list
    fields = ", ".join(f"p.{c}" for c in selected)
    if include_category and "category_id" in cols:
        fields = fields + ", c.name AS category_name"
        return (
            f"SELECT {fields} FROM Product p "
            f"LEFT JOIN Category c ON p.category_id = c.category_id"
        )
    return f"SELECT {fields} FROM Product p"


def parse_spec(spec_raw: Any) -> Any:
    if spec_raw is None:
        return None
    if isinstance(spec_raw, (dict, list)):
        return spec_raw
    if isinstance(spec_raw, (bytes, bytearray)):
        try:
            return json.loads(spec_raw.decode("utf-8"))
        except Exception:
            return {"_raw": spec_raw.decode("utf-8", errors="ignore")}
    if isinstance(spec_raw, str):
        s = spec_raw.strip()
        if not s:
            return None
        try:
            return json.loads(s)
        except Exception:
            # keep raw if not valid JSON
            return {"_raw": s}
    # Fallback
    return spec_raw


def flatten_spec_for_text(spec: Any) -> str:
    """Turn specifications JSON into simple lines for text embedding."""
    if spec is None:
        return ""
    if isinstance(spec, dict):
        parts = []
        for k, v in spec.items():
            try:
                if isinstance(v, (dict, list)):
                    v_str = json.dumps(v, ensure_ascii=False)
                else:
                    v_str = str(v)
                parts.append(f"{k}: {v_str}")
            except Exception:
                parts.append(f"{k}: {v}")
        return "; ".join(parts)
    if isinstance(spec, list):
        try:
            return "; ".join([json.dumps(x, ensure_ascii=False) if isinstance(x, (dict, list)) else str(x) for x in spec])
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
    if description:
        parts.append(description)
    if use_case:
        parts.append(f"Mục đích sử dụng: {use_case}")
    if usp:
        parts.append(f"Điểm nổi bật: {usp}")
    if spec_text:
        parts.append(f"Thông số: {spec_text}")

    return ". ".join(p for p in parts if p).strip()


def make_metadata(row: Dict[str, Any]) -> Dict[str, Any]:
    keys = [
        "product_id",
        "brand",
        "category_id",
        "category_name",
        "price",
        "original_price",
        "stock_quantity",
        "rating",
        "review_count",
        "image_url",
        "is_new",
        "is_best_seller",
    ]
    meta = {k: row.get(k) for k in keys}
    return meta


def chunk_text(text: str, size: int = CHUNK_SIZE, overlap: int = CHUNK_OVERLAP) -> Iterable[str]:
    if not text:
        return []
    if size <= 0:
        return [text]
    chunks = []
    start = 0
    n = len(text)
    while start < n:
        end = min(n, start + size)
        chunks.append(text[start:end])
        if end == n:
            break
        start = max(end - overlap, start + 1)
    return chunks


def extract_products(conn) -> List[Dict[str, Any]]:
    cols = product_columns(conn)
    sql = build_select(cols, include_category=category_available(conn))

    cur = conn.cursor(dictionary=True)
    cur.execute(sql)

    records: List[Dict[str, Any]] = []
    for row in cur.fetchall():
        item: Dict[str, Any] = {
            "product_id": row.get("product_id"),
            "name": row.get("name"),
            "description": row.get("description"),
            "brand": row.get("brand"),
            # normalize optional columns to empty string if missing
            "use_case": row.get("use_case", ""),
            "usp": row.get("usp", ""),
            "specifications": parse_spec(row.get("specifications")),
            # optional rich fields
            "price": row.get("price"),
            "original_price": row.get("original_price"),
            "stock_quantity": row.get("stock_quantity"),
            "category_id": row.get("category_id"),
            "category_name": row.get("category_name"),
            "image_url": row.get("image_url"),
            "rating": row.get("rating"),
            "review_count": row.get("review_count"),
            "is_new": row.get("is_new"),
            "is_best_seller": row.get("is_best_seller"),
            "created_at": row.get("created_at"),
            "updated_at": row.get("updated_at"),
        }
        records.append(item)

    cur.close()
    return records


def write_jsonl(rows: List[Dict[str, Any]], path: str) -> None:
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8") as f:
        for r in rows:
            # default=str to handle datetime/date serialization
            f.write(json.dumps(r, ensure_ascii=False, default=str) + "\n")


def embed_and_load(rows: List[Dict[str, Any]]) -> None:
    if not ENABLE_EMBED:
        print("[embed] Skipped (ENABLE_EMBED not set to true)")
        return
    if chromadb is None:
        print("[embed] Skipped: chromadb not installed", file=sys.stderr)
        return

    # Choose embedding function
    ef = None
    model_name = os.environ.get("EMBED_MODEL", "sentence-transformers/all-MiniLM-L6-v2")
    try:
        if SentenceTransformer is not None:
            st_model = SentenceTransformer(model_name)

            class STEmbedFn:
                def __call__(self, texts: List[str]) -> List[List[float]]:
                    return st_model.encode(texts, normalize_embeddings=True).tolist()

            ef = STEmbedFn()
        else:
            # Try Chroma default embedding functions if available (e.g., OpenAI)
            openai_api_key = os.environ.get("OPENAI_API_KEY")
            if embedding_functions and openai_api_key:
                ef = embedding_functions.OpenAIEmbeddingFunction(api_key=openai_api_key, model_name=os.environ.get("OPENAI_EMBED_MODEL", "text-embedding-3-small"))
    except Exception as e:
        print(f"[embed] Failed to init embedding model: {e}", file=sys.stderr)
        ef = None

    if ef is None:
        print("[embed] No embedding function available; skipping load to Chroma", file=sys.stderr)
        return

    # Build documents + metadatas + ids with chunking
    documents: List[str] = []
    metadatas: List[Dict[str, Any]] = []
    ids: List[str] = []

    for row in rows:
        content = build_content(row)
        meta = make_metadata(row)
        pid = str(row.get("product_id"))
        for idx, chunk in enumerate(chunk_text(content)):
            if not chunk:
                continue
            documents.append(chunk)
            metachunk = dict(meta)
            metachunk["chunk_index"] = idx
            metachunk["name"] = row.get("name")
            ids.append(f"p{pid}_c{idx}")
            metadatas.append(metachunk)

    if not documents:
        print("[embed] No documents to embed")
        return

    os.makedirs(DEFAULT_CHROMA_PATH, exist_ok=True)
    client = chromadb.PersistentClient(path=DEFAULT_CHROMA_PATH)
    collection = client.get_or_create_collection(name=DEFAULT_COLLECTION)

    print(f"[embed] Upserting {len(documents)} chunks into collection '{DEFAULT_COLLECTION}' at {DEFAULT_CHROMA_PATH}")
    try:
        # Compute embeddings in batches for memory safety
        B = int(os.environ.get("EMBED_BATCH", "64"))
        start = 0
        n = len(documents)
        while start < n:
            end = min(n, start + B)
            batch_docs = documents[start:end]
            batch_ids = ids[start:end]
            batch_meta = metadatas[start:end]
            vectors = ef(batch_docs)
            collection.add(ids=batch_ids, metadatas=batch_meta, documents=batch_docs, embeddings=vectors)
            start = end
        print("[embed] Done")
    except Exception as e:
        print(f"[embed] Failed to upsert into Chroma: {e}", file=sys.stderr)


def main():
    try:
        conn = open_connection()
    except Exception as e:
        print(f"[extract] Failed to connect to DB: {e}", file=sys.stderr)
        sys.exit(1)

    try:
        rows = extract_products(conn)
        write_jsonl(rows, DEFAULT_OUTPUT_PATH)
        print(f"[extract] Exported {len(rows)} products to {DEFAULT_OUTPUT_PATH}")

        # Optional transform+embed+load
        embed_and_load(rows)
    except Error as e:
        print(f"[extract] MySQL error: {e}", file=sys.stderr)
        sys.exit(2)
    except Exception as e:
        print(f"[extract] Unexpected error: {e}", file=sys.stderr)
        sys.exit(3)
    finally:
        try:
            conn.close()
        except Exception:
            pass


if __name__ == "__main__":
    main()
