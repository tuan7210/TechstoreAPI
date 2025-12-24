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
import unicodedata

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
EMBED_MODEL = os.environ.get("EMBED_MODEL", "sentence-transformers/paraphrase-multilingual-MiniLM-L12-v2")
OPENAI_API_KEY = os.environ.get("OPENAI_API_KEY")
OPENAI_EMBED_MODEL = os.environ.get("OPENAI_EMBED_MODEL", "text-embedding-3-small")
ENABLE_RERANK = os.environ.get("ENABLE_RERANK", "false").lower() in ("1", "true", "yes")
CROSS_ENCODER_MODEL = os.environ.get("CROSS_ENCODER_MODEL", "cross-encoder/ms-marco-MiniLM-L-6-v2")
RERANK_POOL = int(os.environ.get("RERANK_POOL", "0"))  # if 0 will default to top_k*2

# LLM Configuration
LLM_MODEL_ID = os.environ.get("LLM_MODEL_ID", "TinyLlama/TinyLlama-1.1B-Chat-v1.0")
CACHE_DIR = os.path.join(os.path.dirname(__file__), "hf_cache")
ENABLE_LOCAL_LLM = os.environ.get("ENABLE_LOCAL_LLM", "false").lower() in ("1", "true", "yes")

# Optional: extra out-of-domain keywords, comma-separated (e.g., "oto,xe may,xe tai")
OOD_EXTRA = [k.strip().lower() for k in os.environ.get("OOD_EXTRA", "").split(",") if k.strip()]


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



# _normalize is now replaced by normalize_for_match for intent/filter only


def is_out_of_domain(query: str) -> Optional[str]:
    """
    Detect queries clearly outside Techstore's catalog (e.g., vehicles, real estate services).
    Returns a short label if matched, else None.
    Extend with OOD_EXTRA env (comma-separated tokens) if needed.
    """
    q = normalize_for_match(query)

    ood_groups = [
        ("xe cộ", [
            "oto", "o to", "xe hoi", "xe may", "xe may dien", "motor", "motorbike", "xe mo to",
            "scooter", "xe tay ga", "xe so", "xe tai", "truck", "bus", "xe khach", "xe dap", "bicycle", "car", "vehicle"
        ]),
        ("bất động sản", ["bat dong san", "nha dat", "can ho", "chung cu", "biet thu", "dat nen"]),
        ("thời trang", ["quan ao", "thoi trang", "giay dep", "giay sneaker", "ao", "quan", "vay", "tui xach"]),
    ]

    # Merge extra tokens to the first group by default
    if OOD_EXTRA:
        ood_groups[0] = (ood_groups[0][0], ood_groups[0][1] + OOD_EXTRA)

    for label, keywords in ood_groups:
        if any(k in q for k in keywords):
            return label
    return None

def detect_intent(query: str) -> str:
    """
    Simple Rule-based Intent Classification.
    The order of checks is important.
    """
    # Normalize: lower-case and remove diacritics to improve matching
    q = normalize_for_match(query)

    # helper to check any keyword present
    def has_any(keywords: List[str]) -> bool:
        return any(k in q for k in keywords)

    # Expanded keyword lists (normalized, Vietnamese no-diacritics variants included)
    gaming_keywords = [
        "game", "gaming", "choi game", "do hoa", "nang", "lol", "pubg", "valorant", "gta", "render",
        "fps", "rtx", "gtx"
    ]
    if has_any(gaming_keywords):
        return "GAMING"

    office_keywords = ["van phong", "mong nhe", "sinh vien", "nhe", "di dong", "word", "excel", "office"]
    if has_any(office_keywords):
        return "OFFICE"

    phone_keywords = [
        "dien thoai", "smartphone", "phone", "mobile", "android", "ios",
        "iphone", "samsung", "galaxy", "pixel", "oppo", "xiaomi",
        "realme", "vivo", "nokia", "chup anh", "selfie"
    ]
    if has_any(phone_keywords):
        return "PHONE"

    tablet_keywords = ["may tinh bang", "tablet", "ipad", "tab"]
    if has_any(tablet_keywords):
        return "TABLET"

    accessory_keywords = [
        "phu kien", "accessory", "tai nghe", "headphone", "earbuds", "loa", "ban phim", "keyboard",
        "sac", "charger", "cap", "cable", "chuot", "mouse", "pin du phong", "power bank"
    ]
    if has_any(accessory_keywords):
        return "ACCESSORY"

    laptop_keywords = [
        "laptop", "may tinh xach tay", "notebook", "ultrabook", "macbook", "dell", "asus", "lenovo", "hp", "msi", "acer"
    ]
    if has_any(laptop_keywords):
        # gaming already caught above; choose LAPTOP as broader intent
        return "LAPTOP"

    # New intents
    camera_keywords = ["camera", "chup anh", "zoom", "camera truoc", "camera sau"]
    if has_any(camera_keywords):
        return "CAMERA"

    battery_keywords = ["pin", "mah", "thoi luong pin", "pin khoe"]
    if has_any(battery_keywords):
        return "BATTERY"

    display_keywords = ["man hinh", "screen", "do phan giai", "oled", "lcd", "amoled"]
    if has_any(display_keywords):
        return "DISPLAY"

    storage_keywords = ["ocung", "ssd", "hdd", "gb", "tb", "bo nho" ]
    if has_any(storage_keywords):
        return "STORAGE"

    # Business / enterprise
    business_keywords = ["doanh nghiep", "business", "quan li", "server"]
    if has_any(business_keywords):
        return "BUSINESS"

    return "GENERAL"


@app.post("/search", response_model=SearchResponse)
def search(req: SearchRequest) -> SearchResponse:
    q = req.query.strip()
    if not q:
        raise HTTPException(status_code=400, detail="Empty query")

    try:
        embed_text = normalize_for_embed(q)
        q_emb = _embedder.encode([embed_text])
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
        # Convert distance to similarity (1 - distance)
        similarity = 1 - dist if dist is not None else None
        interim.append(
            SearchResult(
                id=_id,
                product_id=pid,
                chunk_index=meta.get("chunk_index"),
                score=similarity,
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

    def norm(text: str) -> str:
        return normalize_for_match(text)
    filtered = []
    
    for r in results:
        spec_text = norm(r.spec_text or "")
        cat_name = norm(r.category_name or "")
        name = norm(r.name or "")
        
        # --- STRICT FILTERING BASED ON INTENT ---
        
        # Default assumption is that the item is valid unless a filter removes it.
        is_valid = True

        if intent == "PHONE":
            cat_ok = any(k in cat_name for k in ["dien thoai", "smartphone", "phone", "mobile"])
            brand_ok = any(k in name for k in ["iphone", "samsung", "galaxy", "pixel", "oppo", "xiaomi", "vivo", "realme", "nokia"]) or any(k in spec_text for k in ["android", "ios"])
            signal_ok = (
                any(k in spec_text for k in ["sim", "lte", "5g", "gsm"]) or
                re.search(r"\b\d{4,6}\s?mah\b", spec_text) is not None or
                re.search(r"\b\d{1,3}\s?mp\b", spec_text) is not None
            )
            # If category isn't a phone, require BOTH brand cue and phone-specific signals
            if not (cat_ok or (brand_ok and signal_ok)):
                is_valid = False
        elif intent == "TABLET":
            if not any(k in cat_name for k in ["may tinh bang", "tablet", "ipad", "tab"]):
                is_valid = False
        elif intent == "ACCESSORY":
            if not any(k in cat_name for k in ["phu kien", "accessory", "chuot", "mouse", "tai nghe", "ban phim", "loa"]):
                is_valid = False
        elif intent == "LAPTOP":
            if not any(k in cat_name for k in ["laptop", "may tinh xach tay", "notebook", "ultrabook", "macbook"]):
                is_valid = False
        elif intent == "GAMING":
            # 1. Must be a Laptop
            if not any(k in cat_name for k in ["laptop", "may tinh xach tay", "notebook", "ultrabook", "macbook"]):
                is_valid = False
            else:
                # 2. Must have Discrete GPU (or gaming keyword in name)
                has_discrete = any(k in spec_text for k in ["rtx", "gtx", "radeon", "geforce", "nvidia", "amd rx", "discrete"])
                gaming_name = "gaming" in name or any(b in name for b in ["legion", "strix", "tuf gaming", "nitro", "omen", "predator", "aorus"])
                if not (has_discrete or gaming_name):
                    is_valid = False
        elif intent == "OFFICE":
            # 1. Must be a Laptop
            if not any(k in cat_name for k in ["laptop", "may tinh xach tay", "notebook", "ultrabook", "macbook"]):
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
    # -1. Out-of-domain guardrail
    ood = is_out_of_domain(req.query)
    if ood:
        return ChatResponse(
            answer=(
                f"Dạ, hiện tại Techstore không kinh doanh nhóm sản phẩm {ood}. "
                "Anh/chị có thể hỏi giúp em về laptop, điện thoại, máy tính bảng, phụ kiện công nghệ ạ."
            ),
            context=[]
        )
    # 0. Intent Detection
    intent = detect_intent(req.query)
    print(f"[chat] Query: '{req.query}' -> Intent: {intent}")

    # 1. Retrieval
    # Fetch more candidates to allow for filtering & backfill
    # Use a larger pool to improve diversity for strict filters
    initial_pool = max(req.top_k * 4, 20)
    search_req = SearchRequest(query=req.query, top_k=initial_pool)
    search_res = search(search_req)
    
    # 2. Filtering with Intent
    filtered_results = filter_results(req.query, search_res.results, intent=intent)
    
    # 3. Backfill if not enough results after strict filtering
    final_results = list(filtered_results[:req.top_k])

    if len(final_results) < req.top_k:
        # Secondary pass: softer category/name cues depending on intent
        needed = req.top_k - len(final_results)
        seen_ids = {r.id for r in final_results}

        def add_if(pred):
            nonlocal needed
            for r in search_res.results:
                if needed <= 0:
                    break
                if r.id in seen_ids:
                    continue
                name = (r.name or "")
                cat = (r.category_name or "")
                spec = (r.spec_text or "")
                if pred(name, cat, spec):
                    final_results.append(r)
                    seen_ids.add(r.id)
                    needed -= 1

        def is_phone_candidate(n: str, c: str, s: str) -> bool:
            nn = (n or "").lower(); cc = (c or "").lower(); ss = (s or "").lower()
            cc = unicodedata.normalize('NFKD', cc); cc = ''.join([ch for ch in cc if not unicodedata.combining(ch)])
            ss = unicodedata.normalize('NFKD', ss); ss = ''.join([ch for ch in ss if not unicodedata.combining(ch)])
            cat_ok = any(k in cc for k in ["dien thoai", "smartphone", "phone", "mobile"])
            brand_ok = any(k in nn for k in ["iphone", "samsung", "galaxy", "pixel", "oppo", "xiaomi", "vivo", "realme", "nokia"]) or any(k in ss for k in ["android", "ios"])
            signal_ok = (
                any(k in ss for k in ["sim", "lte", "5g", "gsm"]) or
                re.search(r"\b\d{4,6}\s?mah\b", ss) is not None or
                re.search(r"\b\d{1,3}\s?mp\b", ss) is not None
            )
            return cat_ok or (brand_ok and signal_ok)

        if intent == "GAMING":
            add_if(lambda n, c, s: any(k in n.lower() for k in ["gaming", "legion", "strix", "nitro", "omen", "predator"]))
        elif intent == "PHONE":
            add_if(lambda n, c, s: is_phone_candidate(n, c, s))
        elif intent == "OFFICE":
            add_if(lambda n, c, s: any(k in c.lower() for k in ["laptop", "ultrabook"]))

        # Final fallback: do NOT add unrelated items; if không đủ, giữ nguyên số lượng hiện có
    
    if not final_results:
        return ChatResponse(
            answer="Dạ, em rất tiếc nhưng hiện tại em không tìm thấy sản phẩm nào phù hợp với yêu cầu của anh/chị trong hệ thống ạ.",
            context=[]
        )

    # Determine verbose preference from user query (if user asks for details)
    def detect_verbose(query: str) -> bool:
        q = query.lower()
        verbose_triggers = ["chi tiet", "thong so", "cau hinh", "detail", "spec", "đầy đủ", "mô tả", "mo ta"]
        return any(t in q for t in verbose_triggers)

    verbose = detect_verbose(req.query)

    # --- RAG LITE MODE ---
    # Skip heavy LLM generation
    # Pass intent + verbose flag to template generator
    answer = generate_answer_lite(final_results, intent=intent, verbose=verbose)
    
    return ChatResponse(answer=answer, context=final_results)


if __name__ == "__main__":  # pragma: no cover
    import uvicorn

    host = os.environ.get("SERVICE_HOST", "0.0.0.0")
    port = int(os.environ.get("SERVICE_PORT", "8000"))
    # Pass app instance directly to avoid ModuleNotFoundError when running script directly
    uvicorn.run(app, host=host, port=port)