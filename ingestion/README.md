
# TechStore AI Ingestion & Semantic Search Pipeline

This module powers the AI product search and chat for TechStore. It extracts product data from MySQL, builds a semantic vector store (ChromaDB), and serves fast, relevant search for the AI chatbox and backend.

---

## Features
- Extracts product data from MySQL and writes to `ingestion/output/products.jsonl` (one product per line, JSON format)
- Supports dynamic fields: `use_case`, `usp`, and nested `specifications`
- Embeds product content into ChromaDB for semantic search (RAG)
- Metadata includes: name, brand, category, price, image_url, use_case, usp, spec_text
- FastAPI microservice for semantic search (used by .NET backend)
- Supports both local sentence-transformers and OpenAI embeddings
- Optional: Cross-Encoder re-ranking for higher answer quality

---

## 1. Setup & Dependencies

```powershell
python -m pip install -r ingestion/requirements.txt
```

---

## 2. Extract Products from MySQL

Configure your DB connection in `ingestion/.env` (recommended):

```
DB_HOST=localhost
DB_PORT=3306
DB_USER=your_user
DB_PASSWORD=your_password
DB_NAME=tech_store
OUTPUT_PATH=ingestion/output/products.jsonl
ENABLE_EMBED=false
```

Extract only (no embedding):
```powershell
python ingestion/extract_products.py
```

You can override env vars inline:
```powershell
$env:DB_USER="root"; $env:DB_PASSWORD="yourpass"; python ingestion/extract_products.py
```

Output: Each line in `ingestion/output/products.jsonl` is a product object:
```json
{"product_id": 1, "name": "iPhone 15 128GB", "description": "...", "brand": "Apple", ...}
```

---

## 3. Embed Products to Chroma (Vector Store)

Set `ENABLE_EMBED=true` in `.env` or as an env var:
```powershell
$env:ENABLE_EMBED="true"
python ingestion/extract_products.py
```
or, if you already have a products.jsonl:
```powershell
python ingestion/embed_existing_products.py
```

Key embedding env vars:
- `CHROMA_PATH` (default: ingestion/chroma)
- `COLLECTION_NAME` (default: products)
- `EMBED_MODEL` (default: sentence-transformers/paraphrase-multilingual-MiniLM-L12-v2)
- `OPENAI_API_KEY` (optional, fallback for embeddings)

---

## 4. Start the Semantic Search Service

This FastAPI microservice serves semantic search for the .NET backend and chatbox.

```powershell
python -m uvicorn ingestion.search_service:app --host 0.0.0.0 --port 8000
```

### Service Env Vars
- `CHROMA_PATH`, `COLLECTION_NAME`, `EMBED_MODEL`, `OPENAI_API_KEY`, `OPENAI_EMBED_MODEL`
- `SERVICE_HOST`, `SERVICE_PORT` (default: 0.0.0.0:8000)
- `ENABLE_RERANK` (default: false)
- `CROSS_ENCODER_MODEL`, `RERANK_POOL`

#### Enable Cross-Encoder Re-Rank (optional)
```powershell
$env:ENABLE_RERANK="true"
$env:CROSS_ENCODER_MODEL="cross-encoder/ms-marco-MiniLM-L-6-v2"
python -m uvicorn ingestion.search_service:app --host 0.0.0.0 --port 8000
```

#### Quick Test
```powershell
curl -X POST http://localhost:8000/search -H "Content-Type: application/json" -d '{"query":"laptop gaming mỏng nhẹ","top_k":5}'
```

---

## 5. Workflow: Update Data & Re-Embed

1. Update your MySQL product data and images
2. Export fresh `products.jsonl` (step 2)
3. (Optional) Remove old Chroma vector store:
	```powershell
	Remove-Item -Recurse -Force .\ingestion\chroma
	```
4. Re-embed:
	```powershell
	python ingestion/embed_existing_products.py
	```
5. Restart the semantic search service

---

## 6. RAG-Only (No Fine-Tuning Needed)

- No LLM fine-tuning or Q&A datasets required
- All answers are generated via Retrieval-Augmented Generation (RAG) using up-to-date product data

---

## 7. Advanced: Metadata & Performance

- Each vector includes rich metadata: `use_case`, `usp`, `spec_text`, `image_url`, etc.
- Cross-Encoder re-rank improves answer quality but increases latency (keep `top_k` small for best UX)
- If latency is high, disable re-rank or reduce `RERANK_POOL`

---

## 8. Troubleshooting

- If images do not display, ensure the `image_url` in `products.jsonl` matches the filename in your backend image storage and is served by your API
- Always re-embed after updating product data or images

---

## 9. References

- [ChromaDB Documentation](https://docs.trychroma.com/)
- [sentence-transformers](https://www.sbert.net/)
- [FastAPI](https://fastapi.tiangolo.com/)
- [Uvicorn](https://www.uvicorn.org/)
