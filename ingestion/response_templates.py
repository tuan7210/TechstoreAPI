from typing import List, Any, Optional
import re

def format_currency(amount: float) -> str:
    if not amount:
        return "Liên hệ để biết giá"
    return f"{amount:,.0f} VNĐ"

def extract_specs(spec_text: str) -> dict:
    """
    Extract CPU and GPU info from spec_text using Regex.
    """
    specs = {"cpu": "CPU hiệu năng cao", "gpu": "Card đồ họa rời"}
    if not spec_text:
        return specs
        
    # Simple Regex to find CPU (Core iX, Ryzen X, Ultra X)
    cpu_match = re.search(r"(Core\s*i\d+|Ryzen\s*\d+|Ultra\s*\d+|M\d\s*Pro|M\d\s*Max|M\d)", spec_text, re.IGNORECASE)
    if cpu_match:
        specs["cpu"] = cpu_match.group(0)
        
    # Simple Regex to find GPU (RTX, GTX, Radeon)
    gpu_match = re.search(r"(RTX\s*\d+\w*|GTX\s*\d+|Radeon\s*RX\s*\d+\w*)", spec_text, re.IGNORECASE)
    if gpu_match:
        specs["gpu"] = gpu_match.group(0)
        
    return specs

def generate_answer_lite(results: List[Any], intent: str = "GENERAL") -> str:
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
    
    # --- TEMPLATE SELECTION BASED ON INTENT ---
    
    if intent == "GAMING":
        # Extract specs for evidence
        specs = extract_specs(spec_text)
        
        answer = f"Dạ, với nhu cầu chơi game/đồ họa nặng, em thấy **{best.name}** là lựa chọn số 1 ạ.\n"
        answer += f"🚀 Cấu hình chiến: Máy được trang bị **{specs['cpu']}** và Card đồ họa **{specs['gpu']}** mạnh mẽ, giúp anh/chị chiến tốt các tựa game phổ biến.\n"
        answer += f"💰 Giá bán: {price_str}\n"
        
    elif intent == "OFFICE":
        answer = f"Dạ, để phục vụ công việc văn phòng và di chuyển, em đề xuất mẫu **{best.name}** ạ.\n"
        answer += f"💼 Đặc điểm: Thiết kế mỏng nhẹ, sang trọng và thời lượng pin tốt.\n"
        answer += f"💰 Giá bán: {price_str}\n"
        if best.usp:
            answer += f"✨ Điểm cộng: {best.usp}\n"
            
    else: # GENERAL / DEFAULT
        answer = f"Dạ, với nhu cầu của anh/chị, em thấy sản phẩm **{best.name}** là phù hợp nhất ạ.\n"
        answer += f"💰 Giá bán: {price_str}\n"
        answer += f"✨ Điểm nổi bật: {best.usp or 'Thiết kế đẹp, hiệu năng tốt'}.\n"
    
    # Common parts
    # Add usage info if available and not already covered
    if best.use_case and intent == "GENERAL":
        answer += f"💡 Phù hợp cho: {best.use_case}\n"
    
    # Suggest others
    if len(results) > 1:
        others = ", ".join([r.name for r in results[1:]])
        answer += f"\nNgoài ra, anh/chị có thể tham khảo thêm: {others}."
        
    return answer
