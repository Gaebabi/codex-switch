"""Regression coverage for PR event parsing and untrusted subject handling."""
import importlib.util
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location("pr_title", Path(__file__).with_name("verify-pr-title.py"))
policy = importlib.util.module_from_spec(spec)
spec.loader.exec_module(policy)


class TitlePolicyTests(unittest.TestCase):
    def test_english_subject_styles(self):
        for title in ("[1/1 OSS governance] Improve maintainer guidance", "fix(windows): recover usage refresh", "Bump Pillow from 12.0.0 to 13.0.0"):
            with self.subTest(title=title):
                self.assertTrue(policy.verify_event({"pull_request": {"title": title}}))

    def test_nonenglish_missing_and_control_characters(self):
        for title in (None, 42, "", "  Fix title", "Fix title ", "123.4", "제목 개선", "Fix title\nrun code", "Fix\x1b[31m title", "Fix\u202e title"):
            with self.subTest(title=title):
                with self.assertRaises(ValueError):
                    policy.verify_event({"pull_request": {"title": title}})

    def test_shell_like_title_is_inert_data(self):
        with tempfile.TemporaryDirectory() as folder:
            marker = Path(folder) / "should-not-exist"
            title = f'Fix $(touch "{marker}") `touch "{marker}"`; echo unchanged'
            # Portable path spelling is sufficient; the value never reaches a shell.
            self.assertTrue(policy.verify_event({"pull_request": {"title": title}}))
            self.assertFalse(marker.exists())

    def test_push_manual_and_bad_payloads(self):
        self.assertFalse(policy.verify_event({"ref": "refs/heads/main"}))
        self.assertFalse(policy.verify_event({"inputs": {}}))
        for event in (None, [], {"pull_request": None}, {"pull_request": {}}, {"pull_request": []}):
            with self.assertRaises(ValueError):
                policy.verify_event(event)


if __name__ == "__main__":
    unittest.main()
