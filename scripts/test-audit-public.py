"""Exercise publication checks against disposable Git repositories, including rejected input."""
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

SCRIPT = Path(__file__).with_name("audit-public.py")


class PublicationAudit(unittest.TestCase):
    def setUp(self):
        self.fixture = tempfile.TemporaryDirectory(prefix="public-audit-")
        self.root = Path(self.fixture.name)
        self.git("init", "-b", "main")
        self.git("config", "user.name", "Audit Fixture")
        self.git("config", "user.email", "fixture@example.invalid")

    def tearDown(self):
        self.fixture.cleanup()

    def git(self, *args):
        subprocess.run(["git", "-C", str(self.root), *args], check=True, capture_output=True)

    def track(self, name, text):
        path = self.root / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(text, encoding="utf-8")
        self.git("add", name)

    def audit(self, tree=True):
        arguments = [sys.executable, str(SCRIPT), "--root", str(self.root)]
        if tree:
            arguments.append("--tree-only")
        result = subprocess.run(arguments, capture_output=True, text=True, check=False)
        return result.returncode, json.loads(result.stdout)

    def test_clean_unicode_source_and_example_identity(self):
        self.track("docs.md", "English / 한국어 / 日本語 / 中文 fixture@example.invalid\n")
        code, report = self.audit()
        self.assertEqual(code, 0)
        self.assertEqual(report["findings"], [])

    def test_yaml_and_editor_settings_are_scanned_without_echoing_token(self):
        token = "ghp_" + "A" * 36
        self.track(".github/workflows/verify.yml", "value: " + token)
        self.track(".editorconfig", "value = " + token)
        code, report = self.audit()
        self.assertEqual(code, 1)
        self.assertEqual({item["path"] for item in report["findings"]},
                         {".github/workflows/verify.yml", ".editorconfig"})
        self.assertNotIn(token, json.dumps(report))

    def test_private_files_and_generated_directories_are_rejected(self):
        self.track("auth.json", "{}")
        self.track("artifacts/report.json", "{}")
        code, report = self.audit()
        self.assertEqual(code, 1)
        self.assertEqual({item["kind"] for item in report["findings"]},
                         {"private-data-file", "generated-or-private-directory"})

    def test_unknown_email_is_rejected(self):
        self.track("docs.md", "fixture@" + "private-domain.com")
        code, report = self.audit()
        self.assertEqual(code, 1)
        self.assertEqual(report["findings"][0]["kind"], "non-example-email")

    def test_removed_secret_remains_visible_in_history(self):
        self.track("config.toml", 'token = "' + "ghp_" + "A" * 36 + '"')
        self.git("commit", "-m", "Synthetic rejected content")
        self.track("config.toml", "# removed\n")
        self.git("commit", "-m", "Synthetic cleanup")
        code, report = self.audit(tree=False)
        self.assertEqual(code, 1)
        self.assertGreater(report["reachableBlobs"], 0)
        self.assertTrue(any(item["path"].startswith("history:") for item in report["findings"]))


if __name__ == "__main__":
    unittest.main()
