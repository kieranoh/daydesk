import json
import os
from pathlib import Path
import tempfile
import subprocess
import sys
import unittest
from datetime import date
from unittest.mock import patch

import backend
import cache_store
import daydesk


class CacheTests(unittest.TestCase):
    def setUp(self):
        self.folder=tempfile.TemporaryDirectory(prefix="daydesk-cache-test-"); self.addCleanup(self.folder.cleanup)
        env=patch.dict(os.environ,{"DAYDESK_CACHE_DIR":self.folder.name,"DAYDESK_DATA_DIR":self.folder.name}); env.start(); self.addCleanup(env.stop)
        settings=patch.object(daydesk,"SETTINGS",Path(self.folder.name)/"settings.json"); settings.start(); self.addCleanup(settings.stop)

    def test_encrypted_roundtrip_corrupt_cache_is_miss(self):
        value={"private":"개인 일정", "empty":[],"flag":True}
        cache_store.write("timetree","a",value)
        self.assertEqual(cache_store.read("timetree","a")[0],value)
        file=cache_store.path("timetree","a")
        self.assertNotIn("개인 일정".encode(),file.read_bytes())
        file.write_bytes(b"incomplete")
        self.assertIsNone(cache_store.read("timetree","a"))

    def test_failed_atomic_replace_keeps_previous_snapshot(self):
        cache_store.write("todo","a",{"version":1})
        with patch.object(cache_store.os,"replace",side_effect=OSError("disk busy")):
            with self.assertRaises(OSError): cache_store.write("todo","a",{"version":2})
        self.assertEqual(cache_store.read("todo","a")[0],{"version":1})

    def test_calendar_refresh_prefetches_adjacent_months_and_removes_deleted_events(self):
        event={"id":"x","title":"일정","start_at":"2026-11-03T01:00:00Z","end_at":"2026-11-03T02:00:00Z","all_day":False}
        with patch.object(backend,"load_events",return_value=([event],[],"")) as load:
            value,_=backend.sync_months(None,"fixture",date(2026,11,3))
            self.assertEqual(len(value["model"]["events"]),1)
            self.assertEqual(load.call_count,5)
            self.assertIsNotNone(cache_store.read("timetree","fixture|2026-10"))
            self.assertIsNotNone(cache_store.read("timetree","fixture|2026-12"))
            self.assertIsNotNone(cache_store.read("timetree","fixture|2026-05"))
            self.assertIsNotNone(cache_store.read("timetree","fixture|2027-05"))
        with patch.object(backend,"load_events",return_value=([],[],"")):
            backend.sync_months(None,"fixture",date(2026,11,3))
        self.assertEqual(cache_store.read("timetree","fixture|2026-11")[0]["model"]["events"],[])

    def test_thirteen_month_prefetch_reuses_master_and_publishes_current_first(self):
        class Client:
            def __init__(self): self.calls=[]
            def call(self,name,**args):
                self.calls.append((name,args))
                if name=="get_calendar_labels": return {"labels":[]}
                if args.get("expand_recurring") is False:
                    return {"events":[{"id":"r","title":"반복","start_at":"2026-11-03T01:00:00Z","end_at":"2026-11-03T02:00:00Z","all_day":False,"has_recurrence":True}]}
                return {"events":[]}
        client=Client(); early=[]
        cache_store.write("timetree","fixture|2020-01",{"preserve":"이전 저장"})
        def publish(value,checked):
            early.append(cache_store.read("timetree","fixture|2026-05") is None)
            self.assertEqual(value["model"]["period"],"2026년 11월")
        backend.sync_months(client,"fixture",date(2026,11,3),publish)
        self.assertEqual(early,[True])
        self.assertEqual(sum(name=="get_calendar_labels" for name,_ in client.calls),1)
        self.assertEqual(sum(args.get("expand_recurring") is False for _,args in client.calls),1)
        self.assertEqual(sum("start_after" in args for _,args in client.calls),5)
        self.assertEqual(cache_store.read("timetree","fixture|2020-01")[0]["preserve"],"이전 저장")

    def test_calendar_read_after_restart_uses_disk_without_connection(self):
        cache_store.write("timetree","calendars",{"calendars":[{"id":"fixture","name":"캘린더"}]})
        with patch.object(backend,"load_events",return_value=([],[],"")):
            backend.sync_months(None,"fixture",date(2026,11,3))
        args=daydesk.parser().parse_args(["timetree","events","--date","2026-10-03","--calendar","fixture"])
        with patch.object(daydesk,"TimeTree") as Client:
            result=daydesk.timetree_command(args)
            self.assertTrue(result["cached"])
            Client.return_value.start.assert_not_called()

    def test_backend_serves_saved_month_with_no_credentials_and_keeps_cache_on_refresh_failure(self):
        cache_store.write("timetree","calendars",{"calendars":[{"id":"fixture","name":"캘린더"}]})
        with patch.object(backend,"load_events",return_value=([],[],"")):
            backend.sync_months(None,"fixture",date(2026,11,3))
        env=os.environ|{"CODEX_HOME":self.folder.name}
        request={"id":1,"calendar_id":"fixture","anchor":"2026-11-03","view":"month"}
        for force,kind in ((False,"view"),(True,"error")):
            result=subprocess.run([sys.executable,str(Path(backend.__file__))],input=json.dumps(request|{"force":force})+"\n",capture_output=True,text=True,encoding="utf-8",env=env,timeout=10)
            self.assertEqual(result.returncode,0,result.stderr)
            rows=[json.loads(line) for line in result.stdout.splitlines()]
            self.assertTrue(any(r["kind"]==kind and r.get("id")==1 for r in rows))
        self.assertIsNotNone(cache_store.read("timetree","fixture|2026-11"))

    def test_truncated_refresh_preserves_complete_snapshot(self):
        with patch.object(backend,"load_events",return_value=([],[],"")):
            backend.sync_months(None,"fixture",date(2026,11,3))
        before=cache_store.read("timetree","fixture|2026-11")[0]
        with patch.object(backend,"load_events",return_value=([],[],"한도 경고")):
            value,_=backend.sync_months(None,"fixture",date(2026,11,3))
        self.assertEqual(cache_store.read("timetree","fixture|2026-11")[0],before)
        self.assertIn("이전 저장",value["warning"])

    def test_legacy_mcp_pause_does_not_block_firebase_cli(self):
        (daydesk.SETTINGS.parent/"todo-panel.json").write_text(json.dumps({"ResumeOn":"2099-01-01"}))
        args=daydesk.parser().parse_args(["todo","list","--date","2026-11-03"])
        with patch.object(daydesk,"todo_native",return_value={"todos":[],"cached":True}) as remote:
            result=daydesk.todo_command(args)
            self.assertTrue(result["cached"])
            remote.assert_called_once_with(["list","--date","2026-11-03","--view","day"])

    def test_edit_delete_forward_to_firebase_and_notify_widget(self):
        for arguments in (["edit","--id","x","--content","수정","--date","2026-11-04"],["delete","--id","x"]):
            args=daydesk.parser().parse_args(["todo",*arguments])
            with patch.object(daydesk,"todo_native",return_value={"saved":True}) as native, patch.object(daydesk,"rpc") as rpc:
                daydesk.todo_command(args)
                native.assert_called_once_with(arguments)
                rpc.assert_called_once_with({"op":"todo-cache"})

    def test_manual_sync_interval_is_explicit_and_validated(self):
        args=daydesk.parser().parse_args(["settings","--sync-minutes","0"])
        with patch.object(daydesk,"control",return_value={}) as control:
            daydesk.run(args)
            control.assert_called_once_with({"op":"settings","sync_minutes":0},launch=True)


if __name__=="__main__": unittest.main()
