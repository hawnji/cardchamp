#!/usr/bin/env python3
"""Generates card art from tools/art/prompts.json and imports it into the Unity project.

Run this on your own computer (it needs an image provider and an API key), then commit the
images it writes to Assets/CardVault/Resources/Art/. The game ships them, so no network is
needed at runtime.

    pip install -r tools/art/requirements.txt
    python3 tools/art/build_prompts.py
    python3 tools/art/generate_art.py --limit 3          # try a few first (ComfyUI running locally)
    python3 tools/art/generate_art.py                    # everything still missing or changed

Providers (--provider): comfyui (free, your own GPU, the default), cloudflare (free daily
allowance; set CLOUDFLARE_ACCOUNT_ID and CLOUDFLARE_API_TOKEN) and openai (paid; set
OPENAI_API_KEY). See tools/art/README.md.

Full-size originals are kept in tools/art/masters/ (not committed). Game-ready images are
centre-cropped to the card frame and saved as JPEG; Unity compresses them to ASTC on import.
tools/art/art_manifest.json records which prompt produced each image, so re-running only
generates jobs that are new or whose prompt changed.
"""
import argparse
import base64
import json
import os
import random
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
from concurrent.futures import ThreadPoolExecutor, as_completed

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, "..", ".."))
PROMPTS = os.path.join(HERE, "prompts.json")
MANIFEST = os.path.join(HERE, "art_manifest.json")
MASTERS = os.path.join(HERE, "masters")
ART_OUT = os.path.join(ROOT, "Assets", "CardVault", "Resources", "Art")
JPEG_QUALITY = 90


class ProviderError(Exception):
    def __init__(self, message, retryable=False):
        super().__init__(message)
        self.retryable = retryable


def http_json(url, payload, headers, timeout=300):
    req = urllib.request.Request(url, data=json.dumps(payload).encode("utf-8"), method="POST")
    req.add_header("Content-Type", "application/json")
    for k, v in headers.items():
        req.add_header(k, v)
    try:
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            return json.loads(resp.read().decode("utf-8"))
    except urllib.error.HTTPError as e:
        body = e.read().decode("utf-8", "replace")[:500]
        raise ProviderError("HTTP %d: %s" % (e.code, body), retryable=e.code == 429 or e.code >= 500)
    except (urllib.error.URLError, TimeoutError) as e:
        raise ProviderError(str(e), retryable=True)


class OpenAIProvider:
    """OpenAI Images API (paid). Outputs belong to you under OpenAI's terms, so they can ship in a commercial game."""

    name = "openai"

    def __init__(self, args):
        self.key = os.environ.get("OPENAI_API_KEY")
        if not self.key:
            raise SystemExit("Set OPENAI_API_KEY first (https://platform.openai.com/api-keys).")
        self.model = args.model or "gpt-image-1"
        self.quality = args.quality

    def generate(self, job):
        portrait = job["size"][1] >= job["size"][0]
        payload = {
            "model": self.model,
            # The Images API has no negative prompt, so the exclusions are written into the prompt.
            "prompt": job["prompt"] + " Do not include: " + job["negative"] + ".",
            "size": "1024x1536" if portrait else "1536x1024",
            "quality": self.quality,
            "n": 1,
        }
        data = http_json("https://api.openai.com/v1/images/generations", payload,
                         {"Authorization": "Bearer " + self.key})
        try:
            return base64.b64decode(data["data"][0]["b64_json"])
        except (KeyError, IndexError, TypeError):
            raise ProviderError("unexpected response: %s" % json.dumps(data)[:300])

    def describe(self):
        return "%s/%s/%s" % (self.name, self.model, self.quality)


class CloudflareProvider:
    """FLUX.1 schnell on Cloudflare Workers AI. Its daily free allowance covers part of the batch per day;
    schnell is Apache 2.0, so the images can ship commercially. Output is square and gets cropped."""

    name = "cloudflare"

    def __init__(self, args):
        self.account = os.environ.get("CLOUDFLARE_ACCOUNT_ID")
        self.token = os.environ.get("CLOUDFLARE_API_TOKEN")
        if not self.account or not self.token:
            raise SystemExit("Set CLOUDFLARE_ACCOUNT_ID and CLOUDFLARE_API_TOKEN (a token with Workers AI permission).")
        self.model = args.model or "@cf/black-forest-labs/flux-1-schnell"

    def generate(self, job):
        url = "https://api.cloudflare.com/client/v4/accounts/%s/ai/run/%s" % (self.account, self.model)
        data = http_json(url, {"prompt": job["prompt"] + " Do not include: " + job["negative"] + ".", "steps": 8},
                         {"Authorization": "Bearer " + self.token})
        try:
            return base64.b64decode(data["result"]["image"])
        except (KeyError, TypeError):
            # A spent daily allowance is not worth retrying today; the next run picks up where this one stopped.
            raise ProviderError("unexpected response: %s" % json.dumps(data)[:300])

    def describe(self):
        return "%s/%s" % (self.name, self.model)


class ComfyUIProvider:
    """A ComfyUI server on your own machine. Free; uses whatever checkpoint the workflow loads.

    The workflow is a ComfyUI "API format" export with placeholders: {{prompt}}, {{negative}},
    {{width}}, {{height}}, {{seed}} and {{checkpoint}}. The bundled one is a plain SDXL graph.
    """

    name = "comfyui"
    # SDXL-native resolutions closest to the card (1:1.4) and banner frames.
    PORTRAIT = (832, 1216)
    LANDSCAPE = (1536, 640)

    def __init__(self, args):
        self.url = (args.comfy_url or "http://127.0.0.1:8188").rstrip("/")
        self.checkpoint = args.model or "sd_xl_base_1.0.safetensors"
        with open(args.workflow or os.path.join(HERE, "comfyui_workflow_sdxl.json"), encoding="utf-8") as f:
            self.workflow = json.load(f)

    def _fill(self, node, values):
        if isinstance(node, dict):
            return {k: self._fill(v, values) for k, v in node.items()}
        if isinstance(node, list):
            return [self._fill(v, values) for v in node]
        if isinstance(node, str):
            if node in values:
                return values[node]
            for k, v in values.items():
                if isinstance(v, str):
                    node = node.replace(k, v)
        return node

    def _get(self, path):
        try:
            with urllib.request.urlopen(self.url + path, timeout=60) as resp:
                return resp.read()
        except urllib.error.URLError as e:
            raise ProviderError("ComfyUI at %s: %s" % (self.url, e), retryable=True)

    def generate(self, job):
        w, h = self.PORTRAIT if job["size"][1] >= job["size"][0] else self.LANDSCAPE
        values = {"{{prompt}}": job["prompt"], "{{negative}}": job["negative"], "{{width}}": w, "{{height}}": h,
                  # Seeded from the prompt hash so a re-run reproduces the same image.
                  "{{seed}}": int(job["hash"][:12], 16), "{{checkpoint}}": self.checkpoint}
        queued = http_json(self.url + "/prompt", {"prompt": self._fill(self.workflow, values)}, {})
        prompt_id = queued["prompt_id"]
        deadline = time.time() + 900
        while time.time() < deadline:
            history = json.loads(self._get("/history/" + prompt_id).decode("utf-8"))
            if prompt_id in history:
                for output in history[prompt_id].get("outputs", {}).values():
                    for image in output.get("images", []):
                        query = urllib.parse.urlencode({k: image.get(k, "") for k in ("filename", "subfolder", "type")})
                        return self._get("/view?" + query)
                raise ProviderError("ComfyUI finished without an image; check the server console")
            time.sleep(1.5)
        raise ProviderError("ComfyUI timed out", retryable=True)

    def describe(self):
        return "%s/%s" % (self.name, self.checkpoint)


PROVIDERS = {"openai": OpenAIProvider, "cloudflare": CloudflareProvider, "comfyui": ComfyUIProvider}


def load_json(path, default):
    if not os.path.exists(path):
        return default
    with open(path, encoding="utf-8") as f:
        return json.load(f)


def save_json(path, data):
    tmp = path + ".tmp"
    with open(tmp, "w", encoding="utf-8") as f:
        json.dump(data, f, indent=1, sort_keys=True)
        f.write("\n")
    os.replace(tmp, path)


def master_path(key):
    return os.path.join(MASTERS, *key.split("/")) + ".png"


def output_path(key):
    return os.path.join(ART_OUT, *key.split("/")) + ".jpg"


def process(key, size):
    """Centre-crops the master to the frame's aspect ratio and writes the game-ready JPEG."""
    from PIL import Image, ImageOps

    with Image.open(master_path(key)) as img:
        fitted = ImageOps.fit(img.convert("RGB"), tuple(size), method=Image.LANCZOS, centering=(0.5, 0.4))
    out = output_path(key)
    os.makedirs(os.path.dirname(out), exist_ok=True)
    fitted.save(out, "JPEG", quality=JPEG_QUALITY, optimize=True, progressive=False)
    return out


def generate_one(provider, job, retries=4):
    delay = 2.0
    for attempt in range(retries + 1):
        try:
            png = provider.generate(job)
            break
        except ProviderError as e:
            if not e.retryable or attempt == retries:
                raise
            time.sleep(delay + random.random())
            delay *= 2
    path = master_path(job["key"])
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "wb") as f:
        f.write(png)
    return process(job["key"], job["size"])


def select_jobs(jobs, manifest, args):
    chosen = []
    for job in jobs:
        if args.only and not any(job["key"].startswith(p) for p in args.only):
            continue
        if args.kind and job["kind"] not in args.kind:
            continue
        done = manifest.get(job["key"], {}).get("hash") == job["hash"] and os.path.exists(output_path(job["key"]))
        if done and not args.force:
            continue
        chosen.append(job)
    return chosen[: args.limit] if args.limit else chosen


def main(argv):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--provider", choices=sorted(PROVIDERS), default="comfyui",
                        help="comfyui (free, your GPU), cloudflare (free daily tier) or openai (paid)")
    parser.add_argument("--model", help="model id, or the checkpoint file for comfyui (default: provider's recommended model)")
    parser.add_argument("--quality", default="medium", choices=["low", "medium", "high"],
                        help="OpenAI quality tier; medium is a good balance of cost and detail")
    parser.add_argument("--comfy-url", help="ComfyUI server (default http://127.0.0.1:8188)")
    parser.add_argument("--workflow", help="ComfyUI API-format workflow with {{placeholders}}")
    parser.add_argument("--only", nargs="*", help="art key prefixes to include, e.g. fh26/ sets/")
    parser.add_argument("--kind", nargs="*", choices=["card", "set", "variant"], help="job kinds to include")
    parser.add_argument("--limit", type=int, help="stop after this many images")
    parser.add_argument("--workers", type=int, default=3, help="parallel requests")
    parser.add_argument("--force", action="store_true", help="regenerate even if the prompt is unchanged")
    parser.add_argument("--dry-run", action="store_true", help="list what would be generated and exit")
    parser.add_argument("--process-only", action="store_true",
                        help="re-crop existing masters into game images without calling the provider")
    args = parser.parse_args(argv)

    prompts = load_json(PROMPTS, None)
    if prompts is None:
        raise SystemExit("Run tools/art/build_prompts.py first.")
    manifest = load_json(MANIFEST, {})

    if args.process_only:
        count = 0
        for job in prompts["jobs"]:
            if os.path.exists(master_path(job["key"])):
                process(job["key"], job["size"])
                count += 1
        print("processed %d masters" % count)
        return

    jobs = select_jobs(prompts["jobs"], manifest, args)
    if args.dry_run or not jobs:
        for job in jobs:
            print("%-8s %s" % (job["kind"], job["key"]))
        print("%d image(s) to generate" % len(jobs))
        return

    provider = PROVIDERS[args.provider](args)
    print("generating %d image(s) with %s" % (len(jobs), provider.describe()))
    failures = []
    with ThreadPoolExecutor(max_workers=max(1, args.workers)) as pool:
        futures = {pool.submit(generate_one, provider, job): job for job in jobs}
        for i, fut in enumerate(as_completed(futures), 1):
            job = futures[fut]
            try:
                out = fut.result()
            except Exception as e:  # keep going; failed jobs are retried on the next run
                failures.append(job["key"])
                print("[%d/%d] FAILED %s: %s" % (i, len(jobs), job["key"], e))
                continue
            manifest[job["key"]] = {"hash": job["hash"], "provider": provider.describe(),
                                    "generatedUtc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())}
            save_json(MANIFEST, manifest)
            print("[%d/%d] %s -> %s" % (i, len(jobs), job["key"], os.path.relpath(out, ROOT)))

    if failures:
        print("%d failed; run the same command again to retry: %s" % (len(failures), " ".join(failures)))
        sys.exit(1)


if __name__ == "__main__":
    main(sys.argv[1:])
