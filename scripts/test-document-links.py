"""Regression checks for document links missed by an existence-only audit."""
import importlib.util
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location('document_links', Path(__file__).with_name('document-links.py'))
links = importlib.util.module_from_spec(spec)
spec.loader.exec_module(links)


class DocumentLinks(unittest.TestCase):
    def test_github_headings(self):
        self.assertEqual(links.anchors('# Mac\n## Mac\n## **한국어** 설정!\n'), {'mac', 'mac-1', '한국어-설정'})

    def test_explicit_html_anchors(self):
        self.assertIn('installation', links.anchors('<a id="installation"></a>'))

    def test_heading_collision_and_setext(self):
        self.assertEqual(links.anchors('# Mac\n## Mac-1\nMac\n---\n'), {'mac', 'mac-1', 'mac-2'})

    def test_missing_fragment_is_not_accepted(self):
        self.assertNotIn('windows', links.anchors('# Mac'))

    def test_fence_with_info_is_not_a_closing_marker(self):
        self.assertEqual(links.targets('```text\n```example\n[Fake](missing.md)\n```\n[Real](README.md)'), ['README.md'])

    def test_reference_and_html_targets(self):
        self.assertEqual(links.targets('[Mac][setup]\n[setup]: INSTALL.md#mac\n<img src="demo.png">\n'), ['demo.png', 'INSTALL.md#mac'])

    def test_fenced_examples_are_not_navigation(self):
        source = '~~~markdown\n# Fake\n[Broken](missing.md)\n~~~\n## Real\n[Good](INSTALL.md#mac)'
        self.assertEqual(links.targets(source), ['INSTALL.md#mac'])
        self.assertEqual(links.anchors(source), {'real'})


if __name__ == '__main__':
    unittest.main()
