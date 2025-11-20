# Ingestion: Product Extract

Small extractor to pull product data from MySQL and write JSONL for vectorization.

## What it does
- Connects to MySQL using env vars (or .env if present)
- Detects optional `use_case` and `usp` columns dynamically
- Parses `specifications` JSON safely
- Writes `ingestion/output/products.jsonl`
- Builds content text (name, brand, category, description, use_case, usp, specifications)
- Optional: chunks content and embeds into ChromaDB with metadata

## Env vars
- `DB_HOST` (default: localhost)
- `DB_PORT` (default: 3306)
- `DB_USER` (required)
- `DB_PASSWORD` (required)
- `DB_NAME` (default: tech_store)
- `OUTPUT_PATH` (default: ingestion/output/products.jsonl)
 - `ENABLE_EMBED` (default: false) — set to true to embed into Chroma
 - `CHROMA_PATH` (default: ingestion/chroma)
 - `COLLECTION_NAME` (default: products)
 - `EMBED_MODEL` (default: sentence-transformers/all-MiniLM-L6-v2)
 - `EMBED_BATCH` (default: 64)
 - `CHUNK_SIZE` (default: 1200), `CHUNK_OVERLAP` (default: 150)
 - `OPENAI_API_KEY` (optional) — if provided and sentence-transformers not installed, can use OpenAI embeddings

## Install deps
```bash
# Windows PowerShell example:
python -m pip install -r ingestion/requirements.txt
```

## Run (extract only)
```bash
# Windows PowerShell example:
python ingestion/extract_products.py
```

The output file will contain one JSON object per line:
```json
{"product_id": 1, "name": "iPhone 15 128GB", "description": "...", "brand": "Apple", "use_case": "", "usp": "", "specifications": {"screen": "6.1 inch ..."}}
```

## Run with embedding to Chroma
```bash
$env:ENABLE_EMBED="true"
python ingestion/extract_products.py
```
This will create or reuse a collection under `ingestion/chroma` and upsert chunked documents with metadata.

## Start semantic search service (microservice)
This tiny FastAPI service queries the persisted Chroma collection so the .NET backend can retrieve top‑k results semantically.

### Env vars
- `CHROMA_PATH` (default: ingestion/chroma)
- `COLLECTION_NAME` (default: products)
- `EMBED_MODEL` (default: sentence-transformers/all-MiniLM-L6-v2)
- `OPENAI_API_KEY` (optional; fallback for embeddings if ST not available)
- `OPENAI_EMBED_MODEL` (default: text-embedding-3-small)
- `SERVICE_HOST` (default: 0.0.0.0)
- `SERVICE_PORT` (default: 8000)

### Run (Windows PowerShell)
```powershell
# ensure embeddings exist (ENABLE_EMBED=true run done at least once)
# start the microservice
python -m uvicorn ingestion.search_service:app --host 0.0.0.0 --port 8000
```

### cURL quick test (optional)
```powershell
curl -X POST http://localhost:8000/search -H "Content-Type: application/json" -d '{"query":"laptop gaming mỏng nhẹ","top_k":5}'
```

The .NET API should call this service at `http://localhost:8000/search` (configurable via `SEARCH_SERVICE_URL`).
