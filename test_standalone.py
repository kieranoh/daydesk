import json
import io
import os
import runpy
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
from contextlib import redirect_stdout
import cache_store
import calendar_data

class StandaloneTests(unittest.TestCase):
    def setUp(self):
        self.folder=tempfile.TemporaryDirectory(prefix='daydesk-standalone-test-'); self.addCleanup(self.folder.cleanup)
        self.root=Path(self.folder.name)
        env=patch.dict(os.environ,{'DAYDESK_DATA_DIR':str(self.root/'data'),'DAYDESK_CACHE_DIR':str(self.root/'cache'),'CODEX_HOME':str(self.root/'no-codex')}); env.start(); self.addCleanup(env.stop)
        self.marker=self.root/'app/standalone.json'; self.marker.parent.mkdir(); self.marker.write_text('{}')
        module=patch.object(calendar_data,'__file__',str(self.marker.parent/'calendar_data.py')); module.start(); self.addCleanup(module.stop)

    def test_packaged_app_does_not_read_codex_or_start_without_credentials(self):
        with patch.object(calendar_data.Path,'read_text',side_effect=AssertionError('Developer config must not be read')) as codex, patch.object(calendar_data.subprocess,'Popen') as process:
            with self.assertRaisesRegex(calendar_data.ConnectionError,'계정 연결'):
                calendar_data.TimeTree().start()
            codex.assert_not_called(); process.assert_not_called()

    def test_packaged_connection_uses_bundled_node_and_supplied_credentials(self):
        for relative in ('runtime/node/node.exe','runtime/timetree/timetree.cjs'):
            path=self.marker.parent/relative; path.parent.mkdir(parents=True,exist_ok=True); path.touch()
        client=calendar_data.TimeTree()
        with patch.object(calendar_data.Path,'read_text',side_effect=AssertionError('Developer config must not be read')) as codex, patch.object(calendar_data.subprocess,'Popen') as process, patch.object(client,'rpc'):
            process.return_value.stdout=[]
            client.start({'email':'synthetic@example.invalid','password':'synthetic-test-only'})
            args=process.call_args.args[0]; env=process.call_args.kwargs['env']
            self.assertEqual(Path(args[0]),self.marker.parent/'runtime/node/node.exe')
            self.assertEqual(env['TIMETREE_EMAIL'],'synthetic@example.invalid')
            self.assertEqual(env['TIMETREE_PASSWORD'],'synthetic-test-only')
            codex.assert_not_called()

    def test_timetree_caches_are_isolated_on_account_change_and_disconnect(self):
        auth=cache_store.data_root()/'timetree-auth.bin'
        cache_store.write_file(auth,{'connected':True,'account_key':'first'})
        cache_store.write('timetree','calendars',{'calendars':[{'id':'private-fixture'}]})
        cache_store.write_file(auth,{'connected':True,'account_key':'second'})
        self.assertIsNone(cache_store.read('timetree','calendars'))
        cache_store.write_file(auth,{'connected':False,'account_key':'disconnected'})
        self.assertIsNone(cache_store.read('timetree','calendars'))
        with self.assertRaises(calendar_data.ConnectionError): calendar_data.TimeTree().start()
        cache_store.write_file(auth,{'connected':True,'account_key':'first'})
        self.assertEqual(cache_store.read('timetree','calendars')[0]['calendars'][0]['id'],'private-fixture')

    def test_connection_helper_accepts_windows_utf8_bom(self):
        request='\ufeff'+json.dumps({'action':'test-timetree','email':'fixture@example.invalid','password':'synthetic-only'})
        output=io.StringIO()
        with patch('sys.stdin',io.StringIO(request)), patch.object(calendar_data,'TimeTree') as client, redirect_stdout(output):
            client.return_value.call.return_value={'calendars':[{'id':'fixture','name':'Fixture'}]}
            runpy.run_path(str(Path(__file__).with_name('connections.py')),run_name='__main__')
        self.assertTrue(json.loads(output.getvalue())['connected'])
        client.return_value.close.assert_called_once()

if __name__=='__main__': unittest.main()
