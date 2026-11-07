"""Check a new PR's English/ASCII subject; event metadata is always data, never code."""
import argparse
import json
from pathlib import Path
import re
import sys


def title_error(title):
    # This is a portable subject convention, not a semantic language detector.
    # Unicode remains welcome in discussion, bodies and translated documents.
    if not isinstance(title, str) or not title or title != title.strip():
        return "Use a nonempty English PR title without surrounding whitespace."
    if any(ord(char) < 32 or ord(char) > 126 for char in title):
        return "Use printable ASCII for the English PR title; put translations in the body."
    if not re.search(r"[A-Za-z]{2,}", title):
        return "Describe the change in English words in the PR title."
    return None


def verify_event(event):
    if not isinstance(event, dict):
        raise ValueError("Expected a GitHub event object")
    if "pull_request" not in event:
        return False  # Push and manual runs have no new PR title to check.
    pull = event["pull_request"]
    if not isinstance(pull, dict):
        raise ValueError("Expected pull_request metadata")
    error = title_error(pull.get("title"))
    if error:
        raise ValueError(error)
    return True


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("event", type=Path, help="GitHub event JSON path")
    args = parser.parse_args()
    try:
        checked = verify_event(json.loads(args.event.read_text(encoding="utf-8")))
    except (OSError, ValueError) as error:
        # Do not echo untrusted titles, event payloads or private filesystem paths.
        print("PR title verification failed. Use a nonempty printable-ASCII English subject.", file=sys.stderr)
        return 1
    print("English PR subject convention passed." if checked else "No PR title in this event; not applicable.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
