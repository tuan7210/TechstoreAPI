#!/usr/bin/env python3
"""
Lightweight semantic search microservice for Techstore.
- Loads persistent Chroma collection built by extract_products.py
- Computes query embeddings (SentenceTransformer or OpenAI) and returns top-k chunks

Environment variables:
- CHROMA_PATH (default: ingestion/chroma)
- COLLECTION_NAME (default: products)
- EMBED_MODEL (default: sentence-transformers/all-MiniLM-L6-v2)
- OPENAI_API_KEY (optional; used if SentenceTransformer not available and you want OpenAI embeddings)
- OPENAI_EMBED_MODEL (default: text-embedding-3-small)
- SERVICE_HOST (default: 0.0.0.0)
- SERVICE_PORT (default: 8000)

Run:
- pip install -r ingestion/requirements.txt
- python -m uvicorn ingestion.search_service:app --host 0.0.0.0 --port 8000
"""
import os
import sys
from typing import Any, Dict, List, Optional

from fastapi import FastAPI, HTTPException
from pydantic import BaseModel, Field

# Chroma & embeddings
try:
    import chromadb  # type: ignore
except Exception as e:  # pragma: no cover
    print(f"[search] chromadb not available: {e}", file=sys.stderr)
    chromadb = None

try:
    from sentence_transformers import SentenceTransformer  # type: ignore
except Exception:
    SentenceTransformer = None  # type: ignore

try:
    from chromadb.utils import embedding_functions  # type: ignore
except Exception:
    embedding_functions = None  # type: ignore

CHROMA_PATH = os.environ.get("CHROMA_PATH", os.path.join("ingestion", "chroma"))
COLLECTION_NAME = os.environ.get("COLLECTION_NAME", "products")
EMBED_MODEL = os.environ.get("EMBED_MODEL", "sentence-transformers/all-MiniLM-L6-v2")
OPENAI_API_KEY = os.environ.get("OPENAI_API_KEY")
OPENAI_EMBED_MODEL = os.environ.get("OPENAI_EMBED_MODEL", "text-embedding-3-small")

# Prepare embedding function for queries
class QueryEmbedder:
    def __init__(self) -> None:
        self._st: Optional[Any] = None
        if SentenceTransformer is not None:
            try:
                self._st = SentenceTransformer(EMBED_MODEL)
                print(f"[search] Using SentenceTransformer model: {EMBED_MODEL}")
            except Exception as e:
                print(f"[search] Failed to load SentenceTransformer: {e}", file=sys.stderr)
                self._st = None
        if self._st is None and OPENAI_API_KEY and embedding_functions:
            print("[search] Fallback to OpenAI embeddings for query side")
            self._openai_fn = embedding_functions.OpenAIEmbeddingFunction(
                api_key=OPENAI_API_KEY,
                model_name=OPENAI_EMBED_MODEL,
            )
        else:
            self._openai_fn = None

    def encode(self, texts: List[str]) -> List[List[float]]:
        if self._st is not None:
            return self._st.encode(texts, normalize_embeddings=True).tolist()
        if self._openai_fn is not None:
            return self._openai_fn(texts)  # type: ignore
        raise RuntimeError("No embedding function available. Install sentence-transformers or set OPENAI_API_KEY.")


# FastAPI app
app = FastAPI(title="Techstore Semantic Search Service", version="0.1.0")


class SearchRequest(BaseModel):
    query: str = Field(..., min_length=1)
    top_k: int = Field(5, ge=1, le=20)
    # Optional filters in future (e.g., category_id, price range)


class SearchResult(BaseModel):
    id: str
    product_id: Optional[int] = None
    chunk_index: Optional[int] = None
    score: Optional[float] = None  # distance or similarity (collection-dependent)
    name: Optional[str] = None
    brand: Optional[str] = None
    category_name: Optional[str] = None
    price: Optional[float] = None
    image_url: Optional[str] = None
    document: Optional[str] = None
    metadata: Dict[str, Any] = {}


class SearchResponse(BaseModel):
    success: bool
    results: List[SearchResult]


# Initialize Chroma client/collection and embedder once
if chromadb is None:  # pragma: no cover
    raise RuntimeError("chromadb is required. Please install it with `pip install chromadb`. ")

_client = chromadb.PersistentClient(path=CHROMA_PATH)
_collection = _client.get_or_create_collection(name=COLLECTION_NAME)
_embedder = QueryEmbedder()
print(f"[search] Loaded collection '{COLLECTION_NAME}' from {CHROMA_PATH}")


@app.post("/search", response_model=SearchResponse)
def search(req: SearchRequest) -> SearchResponse:
    q = req.query.strip()
    if not q:
        raise HTTPException(status_code=400, detail="Empty query")

    try:
        q_emb = _embedder.encode([q])
    except Exception as e:
        raise HTTPException(status_code=500, detail=f"Embedding error: {e}")

    try:
        res = _collection.query(
            query_embeddings=q_emb,
            n_results=req.top_k,
            include=["metadatas", "documents", "distances", "embeddings"],
        )
    except Exception as e:
        raise HTTPException(status_code=500, detail=f"Vector DB error: {e}")

    # Chroma returns lists per query; we have single query
    ids = res.get("ids", [[]])[0]
    metas = res.get("metadatas", [[]])[0]
    docs = res.get("documents", [[]])[0]
    dists = res.get("distances", [[]])[0]

    results: List[SearchResult] = []
    for i, _id in enumerate(ids):
        meta = metas[i] if i < len(metas) else {}
        doc = docs[i] if i < len(docs) else None
        dist = dists[i] if i < len(dists) else None
        # Extract friendly fields
        pid = None
        try:
            pid = int(meta.get("product_id")) if meta.get("product_id") is not None else None
        except Exception:
            pass
        results.append(
            SearchResult(
                id=_id,
                product_id=pid,
                chunk_index=meta.get("chunk_index"),
                score=dist,
                name=meta.get("name"),
                brand=meta.get("brand"),
                category_name=meta.get("category_name"),
                price=meta.get("price"),
                image_url=meta.get("image_url"),
                document=doc,
                metadata=meta or {},
            )
        )

    return SearchResponse(success=True, results=results)


if __name__ == "__main__":  # pragma: no cover
    import uvicorn

    host = os.environ.get("SERVICE_HOST", "0.0.0.0")
    port = int(os.environ.get("SERVICE_PORT", "8000"))
    uvicorn.run("ingestion.search_service:app", host=host, port=port, reload=False)