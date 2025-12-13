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
- ENABLE_RERANK (default: false) enable cross-encoder reranking
- CROSS_ENCODER_MODEL (default: cross-encoder/ms-marco-MiniLM-L-6-v2)
- RERANK_POOL (default: 0 => auto top_k*2 up to 50)

Supports .env loading (project root or ingestion/.env) so you don't have to export vars every run.

Run:
- pip install -r ingestion/requirements.txt
- python -m uvicorn ingestion.search_service:app --host 0.0.0.0 --port 8000
"""
import os
import sys
from typing import Any, Dict, List, Optional
import re

from fastapi import FastAPI, HTTPException, Response
from fastapi.middleware.cors import CORSMiddleware

# Optional .env loading so user doesn't need to retype env vars each session
try:  # pragma: no cover
    from dotenv import load_dotenv  # type: ignore
    load_dotenv()  # root .env
    here = os.path.dirname(__file__)
    env_path = os.path.join(here, ".env")
    if os.path.exists(env_path):
        load_dotenv(dotenv_path=env_path, override=False)
except Exception:
    pass
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

# LLM & Generation
try:
    import torch
    from transformers import AutoModelForCausalLM, AutoTokenizer, BitsAndBytesConfig
except ImportError:
    torch = None
    AutoModelForCausalLM = None

# Import templates
try:
    from ingestion.response_templates import generate_answer_lite
except ImportError:
    # Fallback if running directly inside ingestion folder
    try:
        from response_templates import generate_answer_lite
    except ImportError:
        print("[warn] Could not import response_templates. Using default lite generator.")
        def generate_answer_lite(results):
            return "Template error."

CHROMA_PATH = os.environ.get("CHROMA_PATH", os.path.join("ingestion", "chroma"))
COLLECTION_NAME = os.environ.get("COLLECTION_NAME", "products")
EMBED_MODEL = os.environ.get("EMBED_MODEL", "sentence-transformers/all-MiniLM-L6-v2")
OPENAI_API_KEY = os.environ.get("OPENAI_API_KEY")
OPENAI_EMBED_MODEL = os.environ.get("OPENAI_EMBED_MODEL", "text-embedding-3-small")
ENABLE_RERANK = os.environ.get("ENABLE_RERANK", "false").lower() in ("1", "true", "yes")
CROSS_ENCODER_MODEL = os.environ.get("CROSS_ENCODER_MODEL", "cross-encoder/ms-marco-MiniLM-L-6-v2")
RERANK_POOL = int(os.environ.get("RERANK_POOL", "0"))  # if 0 will default to top_k*2

# LLM Configuration
LLM_MODEL_ID = os.environ.get("LLM_MODEL_ID", "TinyLlama/TinyLlama-1.1B-Chat-v1.0")
CACHE_DIR = os.path.join(os.path.dirname(__file__), "hf_cache")
ENABLE_LOCAL_LLM = os.environ.get("ENABLE_LOCAL_LLM", "false").lower() in ("1", "true", "yes")

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

# Add CORS middleware to allow requests from frontend
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],  # Allows all origins
    allow_credentials=True,
    allow_methods=["*"],  # Allows all methods
    allow_headers=["*"],  # Allows all headers
)

@app.get('/favicon.ico', include_in_schema=False)
async def favicon():
    return Response(status_code=204)



class SearchRequest(BaseModel):
    query: str = Field(..., min_length=1)
    top_k: int = Field(5, ge=1, le=20)
    # Optional filters in future (e.g., category_id, price range)


class ChatRequest(BaseModel):
    query: str = Field(..., min_length=1)
    top_k: int = Field(3, ge=1, le=10) # Giảm mặc định xuống 3 để nhẹ hơn


class SearchResult(BaseModel):
    id: str
    product_id: Optional[int] = None
    chunk_index: Optional[int] = None
    score: Optional[float] = None  # distance or similarity (collection-dependent)
    cross_score: Optional[float] = None  # score from cross-encoder reranker
    name: Optional[str] = None
    brand: Optional[str] = None
    category_name: Optional[str] = None
    price: Optional[float] = None
    image_url: Optional[str] = None
    document: Optional[str] = None
    use_case: Optional[str] = None
    usp: Optional[str] = None
    spec_text: Optional[str] = None
    metadata: Dict[str, Any] = {}


class SearchResponse(BaseModel):
    success: bool
    results: List[SearchResult]


class ChatResponse(BaseModel):
    answer: str
    context: List[SearchResult]


# Initialize Chroma client/collection and embedder once
if chromadb is None:  # pragma: no cover
    raise RuntimeError("chromadb is required. Please install it with `pip install chromadb`. ")

_client = chromadb.PersistentClient(path=CHROMA_PATH)
_collection = _client.get_or_create_collection(name=COLLECTION_NAME)
_embedder = QueryEmbedder()
_cross_encoder = None
if ENABLE_RERANK:
    try:
        from sentence_transformers import CrossEncoder  # type: ignore
        _cross_encoder = CrossEncoder(CROSS_ENCODER_MODEL)
        print(f"[search] Rerank enabled with model: {CROSS_ENCODER_MODEL}")
    except Exception as e:
        print(f"[search] Failed to load cross-encoder: {e}", file=sys.stderr)
        _cross_encoder = None
print(f"[search] Loaded collection '{COLLECTION_NAME}' from {CHROMA_PATH}")

# Initialize Local LLM
_llm_model = None
_llm_tokenizer = None

def load_llm():
    global _llm_model, _llm_tokenizer
    if not ENABLE_LOCAL_LLM or not torch or not AutoModelForCausalLM:
        print("[search] Local LLM disabled or dependencies missing.")
        return

    print(f"[search] Loading local LLM: {LLM_MODEL_ID}...")
    try:
        bnb_config = BitsAndBytesConfig(
            load_in_4bit=True,
            bnb_4bit_quant_type="nf4",
            bnb_4bit_compute_dtype=torch.bfloat16,
            bnb_4bit_use_double_quant=True,
        )
        _llm_tokenizer = AutoTokenizer.from_pretrained(LLM_MODEL_ID, cache_dir=CACHE_DIR)
        _llm_model = AutoModelForCausalLM.from_pretrained(
            LLM_MODEL_ID,
            quantization_config=bnb_config,
            device_map="auto",
            cache_dir=CACHE_DIR,
            trust_remote_code=True
        )
        print("[search] Local LLM loaded successfully.")
    except Exception as e:
        print(f"[search] Failed to load local LLM: {e}")

# Load LLM on startup if enabled
if ENABLE_LOCAL_LLM:
    load_llm()

def detect_intent(query: str) -> str:
    """
    Simple Rule-based Intent Classification.
    The order of checks is important.
    """
    q_lower = query.lower()
    
    # Specific intents first
    gaming_keywords = ["game", "gaming", "chơi game", "đồ họa", "nặng", "lol", "pubg", "valorant", "gta", "render"]
    if any(k in q_lower for k in gaming_keywords):
        return "GAMING"
        
    office_keywords = ["văn phòng", "mỏng nhẹ", "sinh viên", "nhẹ", "di động", "word", "excel"]
    if any(k in q_lower for k in office_keywords):
        return "OFFICE"

    # Broader category intents
    phone_keywords = ["điện thoại", "phone", "iphone", "samsung", "galaxy", "pixel", "oppo", "xiaomi", "chụp ảnh", "selfie"]
    if any(k in q_lower for k in phone_keywords):
        return "PHONE"

    tablet_keywords = ["máy tính bảng", "tablet", "ipad", "tab"]
    if any(k in q_lower for k in tablet_keywords):
        return "TABLET"

    accessory_keywords = ["phụ kiện", "accessory", "tai nghe", "headphone", "sạc", "charger", "cáp", "cable", "chuột", "mouse", "pin dự phòng"]
    if any(k in q_lower for k in accessory_keywords):
        return "ACCESSORY"
        
    laptop_keywords = ["laptop", "máy tính xách tay", "macbook", "dell", "asus"]
    if any(k in q_lower for k in laptop_keywords):
        return "LAPTOP"
        
    return "GENERAL"


@app.post("/search", response_model=SearchResponse)
def search(req: SearchRequest) -> SearchResponse:
    q = req.query.strip()
    if not q:
        raise HTTPException(status_code=400, detail="Empty query")

    try:
        q_emb = _embedder.encode([q])
    except Exception as e:
        raise HTTPException(status_code=500, detail=f"Embedding error: {e}")

    pool_size = req.top_k
    if ENABLE_RERANK:
        pool_size = RERANK_POOL if RERANK_POOL > 0 else min(req.top_k * 2, 50)

    try:
        res = _collection.query(
            query_embeddings=q_emb,
            n_results=pool_size,
            include=["metadatas", "documents", "distances"],
        )
    except Exception as e:
        raise HTTPException(status_code=500, detail=f"Vector DB error: {e}")

    ids = res.get("ids", [[]])[0]
    metas = res.get("metadatas", [[]])[0]
    docs = res.get("documents", [[]])[0]
    dists = res.get("distances", [[]])[0]

    interim: List[SearchResult] = []
    for i, _id in enumerate(ids):
        meta = metas[i] if i < len(metas) else {}
        doc = docs[i] if i < len(docs) else None
        dist = dists[i] if i < len(dists) else None
        pid = None
        try:
            pid = int(meta.get("product_id")) if meta.get("product_id") is not None else None
        except Exception:
            pass
        interim.append(
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
                use_case=meta.get("use_case"),
                usp=meta.get("usp"),
                spec_text=meta.get("spec_text"),
                metadata=meta or {},
            )
        )

    if ENABLE_RERANK and _cross_encoder is not None and interim:
        try:
            # Prepare (query, passage) pairs
            pairs = [(q, r.document or r.name or "") for r in interim]
            scores = _cross_encoder.predict(pairs)
            for r, s in zip(interim, scores):
                r.cross_score = float(s)
            # Sort by cross_score desc (fallback to original distance if None)
            interim.sort(key=lambda x: (x.cross_score if x.cross_score is not None else -1.0), reverse=True)
        except Exception as e:
            print(f"[search] Rerank error: {e}", file=sys.stderr)

    # Truncate to requested top_k
    final = interim[:req.top_k]
    return SearchResponse(success=True, results=final)


def filter_results(query: str, results: List[SearchResult], intent: str = "GENERAL") -> List[SearchResult]:
    """
    Apply hard filters based on Intent and Query.
    """
    q_lower = query.lower()
    filtered = []
    
    for r in results:
        spec_text = (r.spec_text or "").lower()
        cat_name = (r.category_name or "").lower()
        name = (r.name or "").lower()
        
        # --- STRICT FILTERING BASED ON INTENT ---
        
        # Default assumption is that the item is valid unless a filter removes it.
        is_valid = True

        if intent == "PHONE":
            if "điện thoại" not in cat_name:
                is_valid = False
        elif intent == "TABLET":
            if "máy tính bảng" not in cat_name and "tablet" not in cat_name:
                is_valid = False
        elif intent == "ACCESSORY":
            if "phụ kiện" not in cat_name:
                is_valid = False
        elif intent == "LAPTOP":
            if "laptop" not in cat_name:
                is_valid = False
        elif intent == "GAMING":
            # 1. Must be a Laptop
            if "laptop" not in cat_name:
                is_valid = False
            else:
                # 2. Must have Discrete GPU
                has_discrete = any(k in spec_text for k in ["rtx", "gtx", "radeon", "discrete", "nvidia", "amd"])
                is_integrated = any(k in spec_text for k in ["integrated", "onboard", "intel iris", "uhd graphics"])
                if is_integrated and not has_discrete:
                    is_valid = False
        elif intent == "OFFICE":
            # 1. Must be a Laptop
            if "laptop" not in cat_name:
                is_valid = False
            else:
                # 2. Exclude heavy gaming laptops
                if "gaming" in name or "gaming" in cat_name:
                    is_valid = False
                # 3. Should be lightweight
                match = re.search(r"weight\D*(\d+\.?\d*)\s*kg", spec_text)
                if match:
                    try:
                        weight = float(match.group(1))
                        if weight >= 2.0:
                            is_valid = False
                    except ValueError:
                        pass

        if is_valid:
            filtered.append(r)
        
    return filtered

def format_context(results: List[SearchResult]) -> str:
    """
    Convert search results into a human-readable text format for the LLM.
    """
    context_text = ""
    for item in results:
        price_str = f"{item.price:,.0f} VNĐ" if item.price else "Liên hệ"
        context_text += f"""
    - Sản phẩm: {item.name}
    - Giá bán: {price_str}
    - Đặc điểm nổi bật: {item.usp or 'N/A'}
    - Thông số kỹ thuật: {item.spec_text or 'N/A'}
    - Mô tả: {item.document or item.use_case or 'N/A'}
    ----------------
    """
    return context_text

def construct_prompt(query: str, context_text: str) -> str:
    """
    Build the system prompt with the context and user query.
    """
    return f"""Vai trò: Bạn là trợ lý ảo tư vấn bán hàng chuyên nghiệp của Techstore. Bạn thân thiện, nhiệt tình và am hiểu công nghệ.
Nhiệm vụ: Trả lời câu hỏi của khách hàng dựa DUY NHẤT trên thông tin được cung cấp trong phần [DỮ LIỆU SẢN PHẨM] bên dưới.
Quy tắc An toàn (Bắt buộc tuân thủ):
Chống ảo giác: Nếu thông tin khách hỏi KHÔNG có trong [DỮ LIỆU SẢN PHẨM], bạn phải trả lời: "Dạ hiện tại em chưa có thông tin chính xác về vấn đề này, để em kiểm tra lại và báo anh/chị sau ạ." TUYỆT ĐỐI KHÔNG tự bịa ra thông số.
Trung thực về giá: Không được tự ý giảm giá hay bịa ra chương trình khuyến mãi nếu dữ liệu không ghi.
Giọng văn: Trả lời ngắn gọn, súc tích (dưới 100 từ nếu không cần thiết dài hơn). Sử dụng kính ngữ (Dạ, Vâng, ạ) phù hợp với tiếng Việt.
Tư vấn: Nếu tìm thấy nhiều sản phẩm, hãy tóm tắt ưu điểm của sản phẩm phù hợp nhất với câu hỏi của khách.

[DỮ LIỆU SẢN PHẨM - CONTEXT]:
{context_text}

[CÂU HỎI CỦA KHÁCH HÀNG]:
{query}
"""

def generate_answer(prompt: str) -> str:
    """
    Generate answer using local LLM or fallback.
    """
    if _llm_model and _llm_tokenizer:
        try:
            inputs = _llm_tokenizer(prompt, return_tensors="pt").to(_llm_model.device)
            outputs = _llm_model.generate(
                **inputs, 
                max_new_tokens=128, # Giảm từ 256 xuống 128 để trả lời nhanh hơn
                do_sample=True, 
                temperature=0.5, # Giảm nhiệt độ để AI tập trung hơn, ít lan man
                top_p=0.9,
                repetition_penalty=1.1
            )
            response = _llm_tokenizer.decode(outputs[0], skip_special_tokens=True)
            # Remove the prompt from the response if it's included
            if response.startswith(prompt):
                response = response[len(prompt):].strip()
            return response
        except Exception as e:
            return f"Lỗi khi tạo câu trả lời: {e}"
    else:
        return "Hệ thống chưa tải được mô hình AI (Local LLM). Vui lòng kiểm tra cấu hình hoặc sử dụng API."



@app.post("/chat", response_model=ChatResponse)
def chat(req: ChatRequest):
    # 0. Intent Detection
    intent = detect_intent(req.query)
    print(f"[chat] Query: '{req.query}' -> Intent: {intent}")

    # 1. Retrieval
    # Fetch more candidates (top_k * 3) to allow for filtering
    search_req = SearchRequest(query=req.query, top_k=req.top_k * 3)
    search_res = search(search_req)
    
    # 2. Filtering with Intent
    filtered_results = filter_results(req.query, search_res.results, intent=intent)
    
    # 3. Re-ranking (Simple heuristic: prioritize exact matches or specific brands if mentioned)
    # For now, just take the top_k from filtered
    final_results = filtered_results[:req.top_k]
    
    if not final_results:
        return ChatResponse(
            answer="Dạ, em rất tiếc nhưng hiện tại em không tìm thấy sản phẩm nào phù hợp với yêu cầu của anh/chị trong hệ thống ạ.",
            context=[]
        )

    # --- RAG LITE MODE ---
    # Skip heavy LLM generation
    # Pass intent to template generator
    answer = generate_answer_lite(final_results, intent=intent)
    
    return ChatResponse(answer=answer, context=final_results)


if __name__ == "__main__":  # pragma: no cover
    import uvicorn

    host = os.environ.get("SERVICE_HOST", "0.0.0.0")
    port = int(os.environ.get("SERVICE_PORT", "8000"))
    # Pass app instance directly to avoid ModuleNotFoundError when running script directly
    uvicorn.run(app, host=host, port=port)