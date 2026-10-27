# Card art pipeline

Card art is generated ahead of time and shipped with the game, so pulls show real artwork with
no network connection. Every athlete and character is fictional; prompts never use subject
names and always exclude text, logos and real-person likenesses.

| File | What it is |
| --- | --- |
| `art_direction.json` | The style for each set, a fixed description of every subject (so they look the same on every card), poses, element effects and rarity treatments. Edit this to change the look. |
| `build_prompts.py` | Combines the game content with the art direction into `prompts.json`. |
| `prompts.json` | One job per image: 102 cards and 7 set banners (plus 1/1 variants with `--variants`). Committed so prompts can be reviewed. |
| `generate_art.py` | Sends the jobs to an image provider, keeps the originals in `masters/` (not committed) and writes game-ready JPEGs to `Assets/CardVault/Resources/Art/`. |
| `art_manifest.json` | Written by `generate_art.py`: which prompt and provider made each image. Commit it with the images. |
| `comfyui_workflow_sdxl.json` | The default ComfyUI graph (SDXL). |

## Run it

On your own computer, from the repo root:

```sh
pip install -r tools/art/requirements.txt
python3 tools/art/build_prompts.py
python3 tools/art/generate_art.py --provider <provider> --limit 3   # check the look first
python3 tools/art/generate_art.py --provider <provider>             # the rest
```

Re-running only generates images that are missing or whose prompt changed, so you can stop and
resume at any time. Useful flags: `--only fh26/ sets/` (key prefixes), `--kind card|set|variant`,
`--force`, `--dry-run`, and `--process-only` (re-crop masters without generating).

Commit `Assets/CardVault/Resources/Art/` and `tools/art/art_manifest.json` when you're happy.

## Providers

| `--provider` | Cost | Setup | Commercial use |
| --- | --- | --- | --- |
| `comfyui` (default) | Free | [ComfyUI](https://github.com/comfyanonymous/ComfyUI) running locally, an NVIDIA GPU with 8 GB+ VRAM (or Apple Silicon, slower), and `sd_xl_base_1.0.safetensors` in `ComfyUI/models/checkpoints/` | SDXL's CreativeML Open RAIL++-M license allows it |
| `cloudflare` | Free daily allowance, then paid | Cloudflare account; set `CLOUDFLARE_ACCOUNT_ID` and `CLOUDFLARE_API_TOKEN` (Workers AI permission). Runs FLUX.1 schnell. When the day's allowance runs out, jobs fail; run again the next day | FLUX.1 schnell is Apache 2.0 |
| `openai` | Paid, roughly $0.04 to $0.17 per image by `--quality` | Set `OPENAI_API_KEY` | You own the outputs under OpenAI's terms |

Check each provider's current pricing and terms before a big run.

ComfyUI options: `--comfy-url` (default `http://127.0.0.1:8188`), `--model` for a different
checkpoint file, and `--workflow` for your own graph. Export it with **Save (API Format)** and
put `{{prompt}}`, `{{negative}}`, `{{width}}`, `{{height}}`, `{{seed}}` and `{{checkpoint}}` where
the values go. Seeds come from the prompt hash, so re-runs reproduce the same image.

## How the game uses it

- Cards load `Resources/Art/{artKey}` (for example `fh26/001`), set pages load `Resources/Art/sets/{artKey}`.
- A parallel can have its own art at `{artKey}_{parallelId}` (for example `fh26/001_black1`); other parallels reuse the base art with their foil frame and shine.
- A card with no image falls back to the procedural team/element colours, so partial art is fine.
- `CardArtImporter` (Editor) imports these images without mipmaps at their exact size and compresses them to ASTC 6x6 on Android and iOS, about 160 KB per card in memory and roughly 17 MB in total for all 102 cards. **Card Vault > Report Missing Card Art** lists any gaps.
