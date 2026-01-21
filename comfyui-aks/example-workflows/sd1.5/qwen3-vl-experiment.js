"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
var zod_1 = require("zod");
var RequestSchema = zod_1.z.object({
    image: zod_1.z
        .string()
        .describe("Input image as URL or base64 encoded string"),
    model_name: zod_1.z
        .enum([
        "Qwen3-VL-8B-Instruct",
        "Qwen2.5-VL-7B-Instruct",
        "Qwen2.5-VL-3B-Instruct",
        "Qwen2-VL-7B-Instruct",
        "Qwen2-VL-2B-Instruct",
    ])
        .optional()
        .default("Qwen3-VL-8B-Instruct")
        .describe("Qwen VL model to use for vision-language tasks"),
    quantization: zod_1.z
        .enum(["None (FP16)", "4bit", "8bit"])
        .optional()
        .default("None (FP16)")
        .describe("Quantization mode for the model"),
    device: zod_1.z
        .enum(["auto", "cuda", "cpu"])
        .optional()
        .default("auto")
        .describe("Device to run the model on"),
    use_flash_attention: zod_1.z
        .boolean()
        .optional()
        .default(false)
        .describe("Whether to use flash attention for faster inference"),
    dtype: zod_1.z
        .enum(["auto", "float16", "bfloat16", "float32"])
        .optional()
        .default("auto")
        .describe("Data type for model computation"),
    task_type: zod_1.z
        .enum([
        "🖼️ Tags",
        "📝 Caption",
        "🔍 Detailed Caption",
        "📦 Object Detection",
        "💬 Chat",
    ])
        .optional()
        .default("🖼️ Tags")
        .describe("Type of vision-language task to perform"),
    prompt: zod_1.z
        .string()
        .optional()
        .default("Spotting \"PURE MALT LAGER\" in the image with line-level, and output in JSON format as [{'bbox_2d': [x1, y1, x2, y2], 'text_content': 'text'}, ...].")
        .describe("Custom prompt for the vision-language model"),
    max_new_tokens: zod_1.z
        .number()
        .int()
        .min(1)
        .max(4096)
        .optional()
        .default(64)
        .describe("Maximum number of tokens to generate"),
    temperature: zod_1.z
        .number()
        .min(0)
        .max(2)
        .optional()
        .default(0.2)
        .describe("Temperature for text generation"),
    top_p: zod_1.z
        .number()
        .min(0)
        .max(1)
        .optional()
        .default(0.9)
        .describe("Top-p (nucleus) sampling parameter"),
    top_k: zod_1.z
        .number()
        .int()
        .min(1)
        .max(100)
        .optional()
        .default(2)
        .describe("Top-k sampling parameter"),
    repetition_penalty: zod_1.z
        .number()
        .min(1)
        .max(2)
        .optional()
        .default(1.2)
        .describe("Repetition penalty for text generation"),
    min_pixels: zod_1.z
        .number()
        .int()
        .optional()
        .default(16)
        .describe("Minimum pixels for image processing"),
    keep_model_loaded: zod_1.z
        .boolean()
        .optional()
        .default(true)
        .describe("Whether to keep the model loaded in memory"),
    seed: zod_1.z
        .number()
        .int()
        .optional()
        .default(function () { return Math.floor(Math.random() * 1000000000000000); })
        .describe("Seed for random number generation"),
    upscale_method: zod_1.z
        .enum(["nearest-exact", "bilinear", "area", "bicubic", "lanczos"])
        .optional()
        .default("nearest-exact")
        .describe("Method for image upscaling"),
    megapixels: zod_1.z
        .number()
        .min(0.1)
        .max(10)
        .optional()
        .default(1)
        .describe("Target megapixels for image scaling"),
});
function generateWorkflow(input) {
    return {
        "1": {
            inputs: {
                image: input.image,
            },
            class_type: "LoadImage",
            _meta: {
                title: "Load Image",
            },
        },
        "10": {
            inputs: {
                upscale_method: input.upscale_method,
                megapixels: input.megapixels,
                image: ["1", 0],
            },
            class_type: "ImageScaleToTotalPixels",
            _meta: {
                title: "ImageScaleToTotalPixels",
            },
        },
        "3": {
            inputs: {
                model_name: input.model_name,
                quantization: input.quantization,
                device: input.device,
                use_flash_attention: input.use_flash_attention,
                dtype: input.dtype,
                task_type: input.task_type,
                prompt: input.prompt,
                max_new_tokens: input.max_new_tokens,
                temperature: input.temperature,
                top_p: input.top_p,
                top_k: input.top_k,
                repetition_penalty: input.repetition_penalty,
                min_pixels: input.min_pixels,
                keep_model_loaded: input.keep_model_loaded,
                seed: input.seed,
                image: ["10", 0],
            },
            class_type: "AILab_QwenVL_Advanced",
            _meta: {
                title: "Qwen VL Advanced",
            },
        },
        "9": {
            inputs: {
                filename_prefix: "QwenVL",
                images: ["1", 0],
            },
            class_type: "SaveImage",
            _meta: {
                title: "Save Image",
            },
        },
    };
}
var workflow = {
    RequestSchema: RequestSchema,
    generateWorkflow: generateWorkflow,
    summary: "Qwen3 VL Experiment",
    description: "Process an image using Qwen3 Vision-Language model for tasks like tagging, captioning, object detection, and chat",
};
exports.default = workflow;
