#!/usr/bin/env python3
"""Checks for the art pipeline. Run: python3 -m unittest discover -s tools/art"""
import json
import os
import sys
import tempfile
import unittest

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import build_prompts  # noqa: E402
import generate_art  # noqa: E402


class BuildPromptsTests(unittest.TestCase):
    def setUp(self):
        with open(build_prompts.CONTENT, encoding="utf-8") as f:
            self.content = json.load(f)

    def test_one_job_per_card_and_set(self):
        keys = {j["key"] for j in build_prompts.build()["jobs"]}
        self.assertEqual(keys, {c["artKey"] for c in self.content["cards"]} | {"sets/" + s["artKey"] for s in self.content["sets"]})

    def test_prompts_never_name_the_subject(self):
        # Names would push models to render text or a real namesake; appearance comes from art_direction.json.
        jobs = {j["key"]: j for j in build_prompts.build()["jobs"]}
        subjects = {s["id"]: s for s in self.content["subjects"]}
        for card in self.content["cards"]:
            name = subjects[card["subjectId"]]["name"]
            self.assertNotIn(name, jobs[card["artKey"]]["prompt"], card["id"])

    def test_variants_only_for_parallels_in_the_set(self):
        sets = {s["id"]: s for s in self.content["sets"]}
        cards = {c["artKey"]: c for c in self.content["cards"]}
        variants = [j for j in build_prompts.build(include_variants=True)["jobs"] if j["kind"] == "variant"]
        self.assertTrue(variants)
        for j in variants:
            base, parallel = j["key"].rsplit("_", 1)
            self.assertIn(parallel, sets[cards[base]["setId"]]["parallelIds"])

    def test_committed_prompts_are_up_to_date(self):
        with open(build_prompts.OUT, encoding="utf-8") as f:
            committed = json.load(f)
        fresh = build_prompts.build(include_variants=any(j["kind"] == "variant" for j in committed["jobs"]))
        self.assertEqual(committed, fresh, "re-run tools/art/build_prompts.py")


class ProcessTests(unittest.TestCase):
    def test_masters_are_cropped_to_the_frame(self):
        from PIL import Image

        with tempfile.TemporaryDirectory() as tmp:
            generate_art.MASTERS = os.path.join(tmp, "masters")
            generate_art.ART_OUT = os.path.join(tmp, "out")
            os.makedirs(os.path.join(generate_art.MASTERS, "fh26"))
            Image.new("RGB", (1024, 1024)).save(generate_art.master_path("fh26/001"))
            out = generate_art.process("fh26/001", [512, 720])
            with Image.open(out) as img:
                self.assertEqual(img.size, (512, 720))


if __name__ == "__main__":
    unittest.main()
