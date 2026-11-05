#!/usr/bin/env python3
"""Check relative Markdown links and release metadata without game references."""
import configparser
from pathlib import Path
import re
from urllib.parse import unquote, urlsplit

ROOT = Path(__file__).resolve().parent.parent
LINK = re.compile(r'!?\[[^\]]*\]\(([^\s)]+)\)')


def local_links(text):
    text = re.sub(r'^```.*?^```[^\n]*$', '', text, flags=re.M | re.S)
    for target in LINK.findall(text):
        parts = urlsplit(target)
        if not parts.scheme and not parts.netloc:
            yield unquote(parts.path), unquote(parts.fragment)


def anchors(text):
    result = set()
    for heading in re.findall(r'^#{1,6}\s+(.+)$', text, re.M):
        slug = re.sub(r'[^\w\- ]', '', heading.lower()).replace(' ', '-')
        result.add(slug)
    return result


def check_links(contents):
    """contents maps archive-style relative paths to bytes, without filesystem access."""
    import posixpath
    errors = []
    for name, data in contents.items():
        if not name.endswith('.md'):
            continue
        for target, fragment in local_links(data.decode('utf-8')):
            path = posixpath.normpath(posixpath.join(posixpath.dirname(name), target)) if target else name
            if path not in contents:
                errors.append(f'{name}: missing link target {target}')
            elif fragment and path.endswith('.md') and fragment not in anchors(contents[path].decode('utf-8')):
                errors.append(f'{name}: missing heading {target}#{fragment}')
    return errors


def main():
    import package_release
    contents = {path.relative_to(ROOT).as_posix(): path.read_bytes() for path in package_release.source_files()}
    errors = check_links(contents)
    snapshot = configparser.ConfigParser(interpolation=None)
    snapshot.read(ROOT / 'BetterCollectionManager.cfg', encoding='utf-8')
    metadata = (ROOT / 'ModMetadata.cs').read_text(encoding='utf-8')
    for field, key in [('Guid', 'Guid'), ('Name', 'Name'), ('Version', 'Version'),
                       ('RepositoryUrl', 'DownloadLink'), ('Description', 'Description')]:
        value = re.search(r'const string ' + field + r' = "([^"]+)";', metadata)
        if not value or snapshot.get('General', key, fallback=None) != value.group(1):
            errors.append(f'Firestone metadata mismatch: {key}')
    if snapshot.get('General', 'Guid') != 'BetterCollectionManager':
        errors.append('Firestone GUID must match DLL and CFG basename')
    if errors:
        raise SystemExit('\n'.join(errors))
    print(f'Checked documentation links and release {package_release.release_version()} metadata.')


if __name__ == '__main__':
    main()
