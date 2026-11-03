import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile

root=Path(__file__).resolve().parents[1]/'build/bundle'
folder=Path(tempfile.mkdtemp(prefix='daydesk-empty-profile-'))
env=os.environ.copy()
env.update(DAYDESK_DATA_DIR=str(folder),CODEX_HOME=str(folder/'no-codex'),PATH=str(Path(os.environ['SystemRoot'])/'System32'))
env.pop('TIMETREE_EMAIL',None); env.pop('TIMETREE_PASSWORD',None)
try:
    results=[]
    for command in (
        [str(root/'daydesk.exe'),'todo','status','--json'],
        [str(root/'runtime/python/python.exe'),'-X','utf8','-B',str(root/'daydesk.py'),'todo','status','--json'],
        [str(root/'runtime/python/python.exe'),'-X','utf8','-B','-c',"import zoneinfo,json,calendar_data; print(json.dumps(dict(timezone=str(zoneinfo.ZoneInfo('America/New_York')))))"],
        [str(root/'runtime/python/python.exe'),'-X','utf8','-B','-c',"import json,timetree_write; print(json.dumps(dict(timetree_writes_bundled=True)))"],
    ):
        p=subprocess.run(command,env=env,capture_output=True,text=True,encoding='utf-8-sig',timeout=20)
        results.append({'program':Path(command[0]).name,'returncode':p.returncode,'stdout':p.stdout,'stderr':p.stderr})
        assert p.returncode==0,p.stderr
        json.loads(p.stdout)
    print(json.dumps(results,ensure_ascii=False))
finally:
    assert folder.parent==Path(tempfile.gettempdir()) and folder.name.startswith('daydesk-empty-profile-')
    shutil.rmtree(folder)
