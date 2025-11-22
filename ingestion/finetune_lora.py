#!/usr/bin/env python3
"""
This repository is now RAG-only.
Fine-tuning (LoRA) has been disabled to keep the stack simple.
Use:
  - ingestion/extract_products.py to prepare data and (optionally) embed to Chroma
  - ingestion/search_service.py to serve semantic retrieval for the .NET backend
"""
import sys

if __name__ == "__main__":
    print("[RAG-only] Fine-tuning is disabled. Use retrieval service + ChatController.")
    sys.exit(0)
