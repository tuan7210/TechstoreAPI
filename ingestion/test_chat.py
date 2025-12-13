import requests
import json
import sys

# Cấu hình API
API_URL = "http://localhost:8000/chat"

def test_question(question):
    print(f"\n{'='*50}")
    print(f"❓ Câu hỏi: {question}")
    print(f"{'-'*50}")
    
    payload = {
        "query": question,
        "top_k": 3
    }
    
    try:
        response = requests.post(API_URL, json=payload)
        
        if response.status_code == 200:
            data = response.json()
            print(f"🤖 AI Trả lời:\n{data['answer']}")
            print(f"\n(Dựa trên {len(data['context'])} sản phẩm tìm thấy)")
        else:
            print(f"❌ Lỗi API ({response.status_code}): {response.text}")
            
    except requests.exceptions.ConnectionError:
        print("❌ Không thể kết nối đến Server. Bạn đã chạy 'python ingestion/search_service.py' chưa?")
    except Exception as e:
        print(f"❌ Lỗi: {e}")

if __name__ == "__main__":
    print("🚀 Bắt đầu test Chatbot AI...")
    
    # Danh sách câu hỏi test
    questions = [
        "Tư vấn cho tôi một chiếc laptop gaming cấu hình mạnh",
        "Tôi muốn tìm điện thoại chụp ảnh đẹp, pin trâu",
        "Có máy tính nào mỏng nhẹ cho văn phòng không?"
    ]
    
    for q in questions:
        test_question(q)
