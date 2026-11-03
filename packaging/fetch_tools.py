"""Fetch vendor runtimes and a portable installer compiler into the build directory."""
from concurrent.futures import ThreadPoolExecutor
import hashlib
import json
from pathlib import Path
import urllib.request

ROOT=Path(__file__).resolve().parents[1]
DOWNLOADS=ROOT/'build/downloads'
FILES={
    'python.zip':'https://www.python.org/ftp/python/3.13.17/python-3.13.17-embed-amd64.zip',
    'node.zip':'https://nodejs.org/download/release/v22.23.4/node-v22.23.4-win-x64.zip',
    'node-sha256.txt':'https://nodejs.org/download/release/v22.23.4/SHASUMS256.txt',
    'innosetup.exe':'https://github.com/jrsoftware/issrc/releases/download/is-7_1_1/innosetup-7.1.1-x64.exe',
}
def fetch(item):
    name,url=item; target=DOWNLOADS/name
    if not target.exists():
        request=urllib.request.Request(url,headers={'User-Agent':'Daydesk-build/1.1'})
        with urllib.request.urlopen(request,timeout=90) as response, (DOWNLOADS/(name+'.tmp')).open('wb') as output:
            while chunk:=response.read(1024*1024): output.write(chunk)
        (DOWNLOADS/(name+'.tmp')).replace(target)
    return {'file':name,'bytes':target.stat().st_size,'sha256':hashlib.sha256(target.read_bytes()).hexdigest(),'source':url}

if __name__=='__main__':
    DOWNLOADS.mkdir(parents=True,exist_ok=True)
    with ThreadPoolExecutor(max_workers=4) as pool: records=list(pool.map(fetch,FILES.items()))
    expected=next(line.split()[0] for line in (DOWNLOADS/'node-sha256.txt').read_text().splitlines() if line.endswith('  node-v22.23.4-win-x64.zip'))
    assert next(r['sha256'] for r in records if r['file']=='node.zip')==expected,'Node vendor checksum mismatch'
    (DOWNLOADS/'sources.json').write_text(json.dumps(records,indent=2),encoding='utf-8')
    print(json.dumps([{'file':r['file'],'bytes':r['bytes']} for r in records]))
