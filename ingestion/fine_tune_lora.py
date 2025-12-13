#!/usr/bin/env python3
"""
Fine-tune a language model using LoRA on a product Q&A dataset.

This script performs the following steps:
1.  Loads product data from a JSONL file.
2.  Generates a question-answering (Q&A) dataset in the Alpaca format.
3.  Loads a pre-trained causal language model and tokenizer from Hugging Face.
4.  Applies Low-Rank Adaptation (LoRA) for parameter-efficient fine-tuning.
5.  Uses the SFTTrainer from TRL to train the model.
6.  Saves the trained LoRA adapter model to a specified output directory.

Environment variables:
- BASE_MODEL: The Hugging Face model identifier for the base model.
  (default: "microsoft/Phi-3-mini-4k-instruct")
- INPUT_PATH: Path to the input JSONL file with product data.
  (default: "ingestion/output/products.jsonl")
- OUTPUT_DIR: Directory to save the final LoRA adapter.
  (default: "ingestion/output/lora-checkpoints/final")
- CHECKPOINT_DIR: Directory for saving intermediate checkpoints.
  (default: "ingestion/output/lora-checkpoints")
"""
import json
import os
import sys
from typing import List, Dict, Any

try:
    import torch
    from datasets import Dataset
    from peft import LoraConfig, get_peft_model, prepare_model_for_kbit_training
    from transformers import (
        AutoModelForCausalLM,
        AutoTokenizer,
        BitsAndBytesConfig,
        TrainingArguments,
    )
    from trl import SFTTrainer
except ImportError:
    print("Dependencies not installed. Please run:", file=sys.stderr)
    print("pip install torch transformers datasets peft bitsandbytes trl accelerate", file=sys.stderr)
    sys.exit(1)

# --- Configuration ---
# Use a smaller, capable model. TinyLlama is a good choice for memory constraints.
# If you have more VRAM, you could try larger models.
BASE_MODEL = os.environ.get("BASE_MODEL", "TinyLlama/TinyLlama-1.1B-Chat-v1.0")
INPUT_PATH = os.environ.get("INPUT_PATH", os.path.join("ingestion", "output", "products.jsonl"))
OUTPUT_DIR = os.environ.get("OUTPUT_DIR", os.path.join("ingestion", "output", "lora-checkpoints", "final"))
CHECKPOINT_DIR = os.environ.get("CHECKPOINT_DIR", os.path.join("ingestion", "output", "lora-checkpoints"))

# Define a cache directory inside the project on D: drive to avoid filling up C: drive
CACHE_DIR = os.path.join(os.path.dirname(__file__), "hf_cache")
os.makedirs(CACHE_DIR, exist_ok=True)
print(f"Using cache directory: {os.path.abspath(CACHE_DIR)}")

# --- 1. Dataset Generation ---

def flatten_specifications(specs: Any) -> str:
    """Converts a JSON specification object into a human-readable string."""
    if not specs or not isinstance(specs, dict):
        return "Không có thông số chi tiết."
    
    parts = []
    for key, value in specs.items():
        # Clean up key names for readability
        clean_key = key.replace("_", " ").capitalize()
        parts.append(f"- {clean_key}: {value}")
    return "\n".join(parts)

def generate_qa_prompt(product: Dict[str, Any]) -> Dict[str, str]:
    """
    Creates a structured Q&A prompt in Alpaca format for a given product.
    """
    name = product.get("name", "sản phẩm")
    brand = product.get("brand", "Không rõ")
    category = product.get("category_name", "Không rõ")
    description = product.get("description", "Không có mô tả.")
    
    price_raw = product.get("price")
    price = 0.0
    try:
        if price_raw is not None:
            price = float(price_raw)
    except (ValueError, TypeError):
        price = 0.0 # Keep it 0 if conversion fails

    use_case = product.get("use_case", "")
    usp = product.get("usp", "")
    specs = flatten_specifications(product.get("specifications"))

    # Create a varied set of questions
    questions = [
        f"Cho tôi biết về {name}?",
        f"Thông tin chi tiết về {name} của {brand}?",
        f"Mô tả sản phẩm {name}.",
        f"Giá của {name} là bao nhiêu?",
        f"{name} có những điểm gì nổi bật?",
        f"Sản phẩm {name} phù hợp cho ai?",
    ]
    
    # Select a question based on product ID to get some variety
    question = questions[product.get("product_id", 0) % len(questions)]

    # Create a detailed, human-like answer
    answer_parts = [
        f"Chào bạn, đây là thông tin chi tiết về sản phẩm **{name}**:",
        f"- **Thương hiệu**: {brand}",
        f"- **Danh mục**: {category}",
        f"- **Giá bán**: {price:,.0f} VNĐ" if price > 0 else "- **Giá bán**: Vui lòng liên hệ.",
        f"\n**Mô tả chung**:\n{description}",
    ]
    if use_case:
        answer_parts.append(f"\n**Đối tượng sử dụng**:\n{use_case}")
    if usp:
        answer_parts.append(f"\n**Điểm nổi bật chính**:\n{usp}")
    if specs:
        answer_parts.append(f"\n**Thông số kỹ thuật**:\n{specs}")

    answer = "\n".join(answer_parts)

    # Alpaca format
    return {
        "text": f"<s><|user|>\n{question}<|end|>\n<|assistant|>\n{answer}<|end|></s>"
    }

def load_and_prepare_dataset(path: str) -> Dataset:
    """Loads products from JSONL and converts them into a Q&A dataset."""
    if not os.path.exists(path):
        print(f"Error: Input file not found at {path}", file=sys.stderr)
        print("Please run `extract_products.py` first to generate the product data.", file=sys.stderr)
        sys.exit(1)
        
    with open(path, "r", encoding="utf-8") as f:
        products = [json.loads(line) for line in f]

    qa_data = [generate_qa_prompt(p) for p in products]
    return Dataset.from_list(qa_data)

# --- 2. Model Training ---

def train():
    """
    Main function to set up and run the fine-tuning process.
    """
    print(f"Starting fine-tuning process with base model: {BASE_MODEL}")

    # --- Load Dataset ---
    print(f"Loading and preparing dataset from: {INPUT_PATH}")
    dataset = load_and_prepare_dataset(INPUT_PATH)
    print(f"Dataset prepared with {len(dataset)} Q&A pairs.")

    # --- Quantization Configuration ---
    # Use 4-bit quantization to reduce memory usage
    bnb_config = BitsAndBytesConfig(
        load_in_4bit=True,
        bnb_4bit_quant_type="nf4",
        bnb_4bit_compute_dtype=torch.bfloat16,
        bnb_4bit_use_double_quant=True,
    )

    # --- Load Base Model ---
    print("Loading base model and tokenizer...")
    model = AutoModelForCausalLM.from_pretrained(
        BASE_MODEL,
        quantization_config=bnb_config,
        torch_dtype=torch.bfloat16,
        device_map="auto",
        trust_remote_code=True,
        cache_dir=CACHE_DIR, # Use the specified cache directory
        low_cpu_mem_usage=True, # Use less memory when loading the model
    )
    model.config.use_cache = False
    model = prepare_model_for_kbit_training(model)

    tokenizer = AutoTokenizer.from_pretrained(BASE_MODEL, trust_remote_code=True, cache_dir=CACHE_DIR)
    tokenizer.pad_token = tokenizer.eos_token
    tokenizer.padding_side = "right"

    # --- LoRA Configuration ---
    lora_config = LoraConfig(
        r=16,
        lora_alpha=32,
        lora_dropout=0.05,
        bias="none",
        task_type="CAUSAL_LM",
        # Apply LoRA to all linear layers for better performance
        target_modules=["q_proj", "k_proj", "v_proj", "o_proj", "gate_proj", "up_proj", "down_proj"],
    )
    model = get_peft_model(model, lora_config)
    print("LoRA configured. Trainable parameters:")
    model.print_trainable_parameters()

    # --- Training Arguments ---
    training_args = TrainingArguments(
        output_dir=CHECKPOINT_DIR,
        per_device_train_batch_size=1,
        gradient_accumulation_steps=8,
        num_train_epochs=3,
        learning_rate=2e-4,
        fp16=True,
        logging_steps=10,
        save_total_limit=2,
        save_strategy="epoch",
        optim="paged_adamw_8bit",
        lr_scheduler_type="cosine",
        warmup_ratio=0.05,
        report_to="none", # "tensorboard" or "wandb" if you want to log
    )

    # --- Initialize Trainer ---
    trainer = SFTTrainer(
        model=model,
        train_dataset=dataset,
        peft_config=lora_config,
        args=training_args,
    )

    # --- Start Training ---
    print("Starting model training...")
    # Start a fresh training run from scratch
    trainer.train()
    print("Training finished.")

    # --- Save Final Model ---
    print(f"Saving final LoRA adapter to {OUTPUT_DIR}")
    os.makedirs(OUTPUT_DIR, exist_ok=True)
    trainer.model.save_pretrained(OUTPUT_DIR)
    tokenizer.save_pretrained(OUTPUT_DIR)
    print("Fine-tuning complete!")

if __name__ == "__main__":
    train()
