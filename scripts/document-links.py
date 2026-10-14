"""Extract repository Markdown links and GitHub-compatible heading anchors."""
import html
import re
import unicodedata


def without_fences(source):
    lines = []
    fence = None
    for line in source.splitlines():
        marker = re.match(r'^\s{0,3}(`{3,}|~{3,})', line)
        if marker:
            run = marker[1]
            if fence is None:
                fence = run
            elif run[0] == fence[0] and len(run) >= len(fence):
                fence = None
            continue
        if fence is None:
            lines.append(line)
    return '\n'.join(lines)


def anchors(source):
    source = without_fences(source)
    result = set(re.findall(r'\b(?:id|name)=["\']([^"\']+)["\']', source))
    headings = re.findall(r'^\s{0,3}#{1,6}\s+(.+?)\s*#*\s*$', source, re.M)
    headings += re.findall(r'<h[1-6]\b[^>]*>(.*?)</h[1-6]>', source, re.I)
    counts = {}
    for heading in headings:
        text = re.sub(r'!?(?:\[([^\]]+)\])\([^)]*\)', r'\1', heading)
        text = html.unescape(re.sub(r'<[^>]+>', '', text)).lower()
        slug = ''.join(c for c in text if c in '_- ' or unicodedata.category(c)[0] in 'LNM').replace(' ', '-')
        index = counts.get(slug, 0)
        counts[slug] = index + 1
        result.add(slug if index == 0 else f'{slug}-{index}')
    return result


def targets(source):
    source = without_fences(source)
    inline = re.findall(r'\[[^\]\n]*\]\((<[^>]+>|[^)\s]+)(?:\s+"[^"]*")?\)', source)
    inline += re.findall(r'<(?:img|a)\b[^>]*(?:src|href)=["\']([^"\']+)["\']', source)
    references = re.findall(r'^\s{0,3}\[[^\]]+\]:\s*(<[^>]+>|\S+)', source, re.M)
    return [html.unescape(t.removeprefix('<').removesuffix('>')) for t in inline + references]
