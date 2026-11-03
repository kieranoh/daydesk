"""Reject private files and locally known credentials before producing the installer."""
import hashlib
import json
import os
from pathlib import Path
import sys
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
import cache_store
from stage import STAGE,BUILD,ALLOW

def audit():
    allowed=set(ALLOW)|{'defaults.json','standalone.json'}
    forbidden={'cache','qa','.codex','__pycache__','.git'}
    needles=[]
    tree=cache_store.read_file(cache_store.data_root()/'timetree-auth.bin')
    if tree:
        for key in ('email','password','account_key'):
            if tree[0].get(key): needles.append(('timetree-'+key,str(tree[0][key])))
    firebase=cache_store.read_file(cache_store.data_root()/'firebase/firebase-auth.bin')
    if firebase:
        for key in ('id_token','refresh_token','uid'):
            if firebase[0].get(key): needles.append(('firebase-'+key,str(firebase[0][key])))
    legacy=Path(os.environ['LOCALAPPDATA'])/'TodoMateWidget/playmcp-token.bin'
    if legacy.exists():
        try:
            values=json.loads(cache_store.crypt(legacy.read_bytes(),False).decode('utf-8-sig'))
            for key in ('access_token','refresh_token'):
                if values.get(key): needles.append(('mcp-'+key,str(values[key])))
        except (OSError,ValueError): pass
    needles += [('build-user-path',str(Path.home())),('build-user-path',str(Path.home()).replace('\\','/'))]
    scanned=0; total=0
    for file in STAGE.rglob('*'):
        if not file.is_file(): continue
        relative=file.relative_to(STAGE)
        assert not forbidden.intersection(relative.parts),f'Forbidden payload directory: {relative}'
        assert relative.parts[0] in ('runtime','licenses') or (len(relative.parts)==1 and file.name in allowed),f'Unlisted payload file: {relative}'
        assert file.suffix.lower() not in ('.bin','.log','.lnk','.toml','.map'),f'Private/development file in payload: {relative}'
        data=file.read_bytes(); scanned+=1; total+=len(data)
        if relative.parts[0]=='runtime' and relative.parts[1] in ('python','node'): continue
        for label,value in needles:
            if len(value)<8: continue
            for encoding in ('utf-8','utf-16le'):
                assert value.encode(encoding) not in data,f'Private value ({label}) found in {relative}; value withheld'
    defaults=json.loads((STAGE/'defaults.json').read_text())
    assert not defaults.get('calendar_id') and defaults.get('todo',{}).get('HiddenGoals')==[]
    assert not (STAGE/'settings.json').exists()
    report={'passed':True,'allowlist_enforced':True,'private_files_excluded':True,'known_credentials_checked':len(needles),'files':scanned,'bytes':total}
    (BUILD/'payload-audit.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
    print(json.dumps(report))

if __name__=='__main__': audit()
