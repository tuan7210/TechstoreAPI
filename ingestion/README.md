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

## Thiết lập thông tin kết nối MySQL (khuyên dùng .env khi dev)
Script `extract_products.py` sẽ tự động đọc file `.env` (nhờ `python-dotenv`). Bạn tạo file `ingestion/.env` với nội dung ví dụ:

```
DB_HOST=localhost
DB_PORT=3306
DB_USER=your_user
DB_PASSWORD=your_password
DB_NAME=tech_store
ENABLE_EMBED=true
CHROMA_PATH=ingestion/chroma
COLLECTION_NAME=products
EMBED_MODEL=sentence-transformers/all-MiniLM-L6-v2
```

Sau đó chỉ cần chạy:
```powershell
python ingestion/extract_products.py
```

Bạn vẫn có thể override nhanh bằng biến môi trường tạm thời trong PowerShell:
```powershell
$env:ENABLE_EMBED="true"; $env:DB_USER="root"; $env:DB_PASSWORD="24102003"; python ingestion/extract_products.py
```
Nhưng cách dùng `.env` thuận tiện hơn, ít phải gõ lại và tránh lộ mật khẩu trong lịch sử terminal screenshot.

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
- `ENABLE_RERANK` (default: false) — bật Cross-Encoder re-rank sau truy vấn vector
- `CROSS_ENCODER_MODEL` (default: cross-encoder/ms-marco-MiniLM-L-6-v2)
- `RERANK_POOL` (default: 0) — số lượng kết quả thô ban đầu để re-rank (0 = tự động lấy top_k*2, tối đa 50)

### Run (Windows PowerShell)
```powershell
# ensure embeddings exist (ENABLE_EMBED=true run done at least once)
# start the microservice
python -m uvicorn ingestion.search_service:app --host 0.0.0.0 --port 8000
```

#### Bật re-rank (tùy chọn)
```powershell
$env:ENABLE_RERANK="true"
$env:CROSS_ENCODER_MODEL="cross-encoder/ms-marco-MiniLM-L-6-v2"
python -m uvicorn ingestion.search_service:app --host 0.0.0.0 --port 8000
```
Re-rank sẽ: lấy thêm pool kết quả (ví dụ top_k*2), tính điểm lại bằng mô hình cross-encoder rồi sắp xếp theo `cross_score`.

### cURL quick test (optional)
```powershell
curl -X POST http://localhost:8000/search -H "Content-Type: application/json" -d '{"query":"laptop gaming mỏng nhẹ","top_k":5}'
```

The .NET API should call this service at `http://localhost:8000/search` (configurable via `SEARCH_SERVICE_URL`).

## RAG‑only mode (no fine‑tuning)
This project now uses Retrieval‑Augmented Generation only:

- Extract products and (optionally) embed to Chroma.
- Start the FastAPI search service.
- The .NET backend composes a strict RAG prompt and calls a chat model/API.

No fine‑tuning scripts or Q&A datasets are required anymore. If you previously created `ingestion/datasets/*` or used LoRA scripts, you can safely ignore them.

### Embed without database
If you already have `ingestion/output/products.jsonl` and only need to build the Chroma vector store (no MySQL connection), run:
```powershell
python ingestion/embed_existing_products.py
```
Set env vars (optional): `PRODUCTS_PATH`, `CHROMA_PATH`, `COLLECTION_NAME`, `EMBED_MODEL`.

## Nâng cấp retrieval (đã triển khai)
- Metadata lưu thêm: `use_case`, `usp`, `spec_text` (thông số đã flatten) cho từng chunk.
- Search service trả về các trường này để backend dựng context giàu cấu trúc.
- Tuỳ chọn re-rank Cross-Encoder: bật bằng `ENABLE_RERANK=true`.

### Lưu ý hiệu năng
- Cross-Encoder sẽ chậm hơn (thường +20–80ms tuỳ số lượng pool). Giữ `top_k` nhỏ (3–8) để trải nghiệm tốt.
- Nếu latency quá cao: tắt re-rank (`ENABLE_RERANK=false`) hoặc giảm `RERANK_POOL`.
