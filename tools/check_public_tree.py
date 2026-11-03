"""Check tracked source files before publication; never print matched secrets."""
import json
from pathlib import Path
import re
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]
IGNORED = {'.git', 'build', 'dist', 'runtime', 'node_modules', '__pycache__', '.venv'}
PRIVATE_NAMES = {'settings.json', 'todo-panel.json', 'connection-status.json', 'standalone.json'}
PRIVATE_SUFFIXES = {'.bin', '.pem', '.key', '.pfx', '.log', '.lnk', '.exe', '.dll', '.pdb'}
PATTERNS = {
    'GitHub token': re.compile(rb'gh[pousr]_[A-Za-z0-9]{30,}'),
    'private key': re.compile(rb'-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----'),
    'signed JWT': re.compile(rb'eyJ[A-Za-z0-9_-]{15,}\.[A-Za-z0-9_-]{15,}\.[A-Za-z0-9_-]{15,}'),
    'Windows user path': re.compile(rb'[A-Za-z]:[\\/]Users[\\/][A-Za-z0-9_.-]+[\\/]'),
}


def source_files():
    if (ROOT / '.git').exists():
        result = subprocess.run(['git', 'ls-files', '-z'], cwd=ROOT, check=True, capture_output=True)
        return [ROOT / name.decode('utf-8') for name in result.stdout.split(b'\0') if name]
    return [p for p in ROOT.rglob('*') if p.is_file()
            and not IGNORED.intersection(p.relative_to(ROOT).parts)
            and not ('vendor' in p.parts and 'dist' in p.parts)
            and p.suffix not in {'.pyc', '.map'}]


def inspect(relative, data):
    issues = []
    if (IGNORED.intersection(relative.parts) or relative.name in PRIVATE_NAMES
            or relative.suffix.lower() in PRIVATE_SUFFIXES
            or relative.name.startswith('.env') and relative.name != '.env.example'
            or any(word in relative.name.lower() for word in ('service-account', 'credentials'))):
        issues.append('private/generated file')
    for label, pattern in PATTERNS.items():
        if pattern.search(data):
            issues.append(label)
    return issues


def main():
    # Keep the checker effective when its patterns or filename rules change.
    assert inspect(Path('auth.bin'), b'x')
    assert inspect(Path('code.py'), ('ghp_' + 'x' * 36).encode())
    assert not inspect(Path('firebase-public.json'), b'{"project_id":"example"}')
    files = source_files()
    problems = [(str(p.relative_to(ROOT)), inspect(p.relative_to(ROOT), p.read_bytes())) for p in files]
    problems = [(p, issues) for p, issues in problems if issues]
    config = json.loads((ROOT / 'firebase-public.json').read_text(encoding='utf-8-sig'))
    assert set(config) == {'project_id', 'api_key'} and config['project_id'] == 'mate-914f3'
    for path, issues in problems:
        print(f'{path}: {", ".join(issues)} (matched values withheld)')
    print(json.dumps({'passed': not problems, 'source_files': len(files), 'findings': len(problems)}))
    return bool(problems)


if __name__ == '__main__':
    sys.exit(main())
