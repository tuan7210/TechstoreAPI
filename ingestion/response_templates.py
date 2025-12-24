from typing import List, Any, Optional
import re
import random

def format_currency(amount: float) -> str:
    if not amount:
        return "Liên hệ để biết giá"
    return f"{amount:,.0f} VNĐ"

def extract_specs(spec_text: str) -> dict:
    """
    Extract common specs (CPU/GPU/RAM/Storage/Battery/Weight/Screen) using regex.
    Returns a dict with best-effort values or friendly defaults.
    """
    specs = {
        "cpu": "CPU hiệu năng cao",
        "gpu": "Card đồ họa rời",
        "ram": None,
        "storage": None,
        "battery": None,
        "weight": None,
        "screen": None,
    }
    if not spec_text:
        return specs

    s = spec_text
    # CPU
    cpu_match = re.search(r"(Core\s*i\d+[A-Za-z0-9\-]*|Ryzen\s*\d+[A-Za-z0-9\-]*|M\d+(?:\s*Pro|\s*Max)?|Apple\s*M\d+)", s, re.IGNORECASE)
    if cpu_match:
        specs["cpu"] = cpu_match.group(0)

    # GPU
    gpu_match = re.search(r"(RTX\s*\d+\w*|GTX\s*\d+|Radeon\s*RX\s*\d+\w*|Intel\s*Iris|Iris\s*Xe)", s, re.IGNORECASE)
    if gpu_match:
        specs["gpu"] = gpu_match.group(0)

    # RAM
    ram_match = re.search(r"(\d+\s?GB)\s*RAM", s, re.IGNORECASE) or re.search(r"(\d+\s?GB)\b", s, re.IGNORECASE)
    if ram_match:
        specs["ram"] = ram_match.group(1)

    # Storage
    storage_match = re.search(r"(\d+\s?GB|\d+\s?TB)\s*(SSD|HDD|NVMe)?", s, re.IGNORECASE)
    if storage_match:
        specs["storage"] = storage_match.group(0)

    # Battery (mAh)
    batt_match = re.search(r"(\d{3,5})\s?mAh", s, re.IGNORECASE)
    if batt_match:
        specs["battery"] = batt_match.group(1) + " mAh"

    # Weight
    weight_match = re.search(r"(\d+\.?\d*)\s?kg", s, re.IGNORECASE)
    if weight_match:
        specs["weight"] = weight_match.group(1) + " kg"

    # Screen size
    screen_match = re.search(r"(\d{2}(?:\.\d)?)-?inch|(\d{2}(?:\.\d)?)\s?inch", s, re.IGNORECASE)
    if screen_match:
        # pick first non-empty group
        val = screen_match.group(1) or screen_match.group(2)
        if val:
            specs["screen"] = val + " inch"

    return specs

def generate_answer_lite(results: List[Any], intent: str = "GENERAL", verbose: bool = False) -> str:
    """
    RAG Lite: Generate answer using templates based on Intent.
    """
    if not results:
        if intent != "GENERAL":
             return f"Dạ, em rất tiếc nhưng hiện tại em không tìm thấy sản phẩm nào thuộc nhóm {intent.lower()} phù hợp với yêu cầu của anh/chị ạ."
        return "Dạ, em rất tiếc nhưng hiện tại em không tìm thấy sản phẩm nào phù hợp với yêu cầu của anh/chị ạ."
    
    # Get Top 1 product
    best = results[0]
    price_str = format_currency(best.price)
    spec_text = best.spec_text or ""
    specs = extract_specs(spec_text)

    # Prepare common phrases (avoid markdown)
    product_line = f"{best.name}"
    price_line = f"Giá: {price_str}" if best.price else "Giá: Liên hệ"

    # Templates: short vs detailed
    if intent == "GAMING":
        if verbose:
            answer = (
                f"Dạ, em đề xuất {product_line}. Cấu hình có {specs.get('cpu')}, {specs.get('gpu')}. "
                f"{price_line}. "
                f"Chi tiết: RAM {specs.get('ram') or 'N/A'}, Storage {specs.get('storage') or 'N/A'}."
            )
        else:
            answer = f"Dạ, {product_line} phù hợp cho chơi game. {price_line}."

    elif intent == "OFFICE":
        if verbose:
            answer = (
                f"Dạ, {product_line} phù hợp cho công việc văn phòng: trọng lượng {specs.get('weight') or 'nhẹ'}, "
                f"màn hình {specs.get('screen') or 'kích thước phù hợp'}. {price_line}."
            )
        else:
            answer = f"Dạ, {product_line} phù hợp cho văn phòng. {price_line}."

    elif intent == "CAMERA":
        answer = f"Dạ, {product_line} có thông số camera tốt. {price_line}."

    elif intent == "BATTERY":
        answer = f"Dạ, {product_line} có pin khoảng {specs.get('battery') or 'không rõ'}. {price_line}."

    else:  # GENERAL + fallback
        if verbose:
            answer = (
                f"Dạ, em thấy {product_line}. {price_line}. "
                f"Thông số chính: CPU {specs.get('cpu')}, RAM {specs.get('ram') or 'N/A'}, "
                f"Storage {specs.get('storage') or 'N/A'}."
            )
        else:
            answer = f"Dạ, {product_line} là lựa chọn phù hợp. {price_line}."

    # Add USP or use_case if present and concise
    if not verbose and best.usp:
        answer += f" Điểm nổi bật: {best.usp}."

    # Suggest alternatives if available
    if len(results) > 1:
        others = ", ".join([r.name for r in results[1:3]])
        answer += f" Ngoài ra có: {others}."

    # Add gentle follow-up question when not verbose
    if not verbose:
        answer += " Anh/chị có muốn xem chi tiết thông số không ạ?"

    return answer
