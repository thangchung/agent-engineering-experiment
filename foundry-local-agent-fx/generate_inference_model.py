# generate_inference_model.py
# This script generates the inference_model.json file for the Qwen2.5-1.5B-Instruct model.
import json
import os
from transformers import AutoTokenizer

model_path = "models/Qwen2.5-1.5B-Instruct"
model_name = "Qwen2.5-1.5B-Instruct"

tokenizer = AutoTokenizer.from_pretrained(model_path)
chat = [
    {"role": "system", "content": "You are a helpful assistant."},
    {"role": "user", "content": "{Content}"},
]

template = tokenizer.apply_chat_template(chat, tokenize=False, add_generation_prompt=True)

json_template = {
    "Name": model_name,
    "PromptTemplate": {
        "assistant": "{Content}",
        "prompt": template
    }
}

json_file = os.path.join(model_path, "inference_model.json")

with open(json_file, "w") as f:
    json.dump(json_template, f, indent=2)

print(f"Created {json_file}")
print(f"\nPrompt template:\n{template}")
