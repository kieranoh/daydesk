"""Create a clean release payload from an explicit allowlist, never from a workspace glob."""
import hashlib
import json
from pathlib import Path
import shutil
import urllib.request
import zipfile

ROOT=Path(__file__).resolve().parents[1]
BUILD=ROOT/'build'
STAGE=BUILD/'bundle'
NATIVE=BUILD/'native'
ALLOW=(
    'TimeTreeWidget.exe','TimeTreeWidget.exe.config','FirebaseTodo.exe','FirebaseTodo.exe.config',
    'FirebaseAccount.exe','FirebaseAccount.exe.config','daydesk.exe',
    'backend.py','calendar_data.py','cache_store.py','connections.py','timetree_write.py','daydesk.py','startup.ps1',
    'calendar.ico','firebase-public.json',
)

def stage():
    STAGE.mkdir(parents=True,exist_ok=True)
    # A dedicated staging tree is rebuilt so deleted development files cannot survive into a release.
    resolved=STAGE.resolve()
    assert resolved.parent==BUILD.resolve() and resolved.name=='bundle'
    for item in STAGE.iterdir():
        if item.is_dir(): shutil.rmtree(item)
        else: item.unlink()
    for name in ALLOW:
        source=NATIVE/name if (NATIVE/name).is_file() else ROOT/name
        shutil.copy2(source,STAGE/name)
    runtime=STAGE/'runtime'; python=runtime/'python'; node=runtime/'node'; tree=runtime/'timetree'
    for directory in (python,node,tree,STAGE/'licenses'): directory.mkdir(parents=True,exist_ok=True)
    with zipfile.ZipFile(BUILD/'downloads/python.zip') as archive: archive.extractall(python)
    with zipfile.ZipFile(BUILD/'downloads/node.zip') as archive:
        for suffix in ('node.exe','LICENSE'):
            member=next(n for n in archive.namelist() if n.endswith('/'+suffix))
            (node/suffix).write_bytes(archive.read(member))
    pth=next(python.glob('python*._pth'))
    original=pth.read_text(encoding='utf-8-sig').splitlines()
    paths=[line for line in original if line and not line.startswith('#') and line!='import site']
    pth.write_text('\n'.join(paths+[r'..\..'])+'\n',encoding='utf-8')
    shutil.copy2(BUILD/'timetree.cjs',tree/'timetree.cjs')
    source=ROOT/'vendor/timetree-mcp'
    shutil.copy2(source/'LICENSE',STAGE/'licenses/TimeTree-MCP.txt')
    # Carry licenses for the packages actually included by esbuild.
    meta=json.loads((BUILD/'timetree-bundle.json').read_text())
    packages={}
    for filename in meta['inputs']:
        file=(ROOT/filename).resolve()
        for parent in file.parents:
            if (parent/'package.json').is_file() and 'node_modules' in parent.parts:
                name=json.loads((parent/'package.json').read_text()).get('name',parent.name)
                packages[name]=parent
                break
    for name,package in packages.items():
        for license in package.glob('*'):
            if license.is_file() and license.name.lower().startswith(('license','copying','notice')):
                shutil.copy2(license,STAGE/'licenses'/(name.replace('/','_').replace('@','')+'-'+license.name))
    # The embeddable interpreter needs IANA time-zone data on Windows.
    metadata=json.load(urllib.request.urlopen('https://pypi.org/pypi/tzdata/json',timeout=30))
    wheel=next(file for file in metadata['urls'] if file['filename'].endswith('.whl'))
    target=BUILD/'downloads'/wheel['filename']
    if not target.exists(): target.write_bytes(urllib.request.urlopen(wheel['url'],timeout=30).read())
    assert hashlib.sha256(target.read_bytes()).hexdigest()==wheel['digests']['sha256']
    with zipfile.ZipFile(target) as archive:
        for member in archive.namelist():
            if member.startswith('tzdata/') and not member.endswith('/'):
                destination=python/member; destination.parent.mkdir(parents=True,exist_ok=True); destination.write_bytes(archive.read(member))
            elif '/licenses/' in member and not member.endswith('/'):
                (STAGE/'licenses'/('tzdata-'+Path(member).name)).write_bytes(archive.read(member))
    # Release defaults come from versioned settings, never the builder's private profile.
    recommendation=json.loads((ROOT/'defaults.json').read_text(encoding='utf-8'))
    defaults={'view':'month','width':960,'height':700,'autostart':True,'background_transparency':recommendation.get('background_transparency',65),'pin':recommendation.get('pin',False),'pin_back':recommendation.get('pin_back',False),'sync_minutes':recommendation.get('sync_minutes',30),'todo_width':recommendation.get('todo_width',300),'todo':{'ShowAllCategories':recommendation.get('todo',{}).get('ShowAllCategories',False),'HiddenGoals':[]}}
    (STAGE/'defaults.json').write_text(json.dumps(defaults,ensure_ascii=False,indent=2),encoding='utf-8')
    (STAGE/'standalone.json').write_text(json.dumps({'name':'Daydesk','version':'1.3.0','platform':'windows-x64'}),encoding='utf-8')
    records={str(file.relative_to(STAGE)).replace('\\','/'):{'sha256':hashlib.sha256(file.read_bytes()).hexdigest(),'bytes':file.stat().st_size} for file in STAGE.rglob('*') if file.is_file()}
    (BUILD/'payload-manifest.json').write_text(json.dumps(records,indent=2),encoding='utf-8')
    print(json.dumps({'payload_files':len(records),'payload_bytes':sum(r['bytes'] for r in records.values()),'bundled_tzdata':metadata['info']['version']}))

if __name__=='__main__': stage()
