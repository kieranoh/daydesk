import copy
from datetime import date,datetime,timezone
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

import backend
import cache_store
import daydesk
import timetree_write as tree


class FakeClient:
    def __init__(self):
        self.event=dict(uuid='fixture-uuid',title='회의',start_at='2026-11-03T01:00:00Z',end_at='2026-11-03T02:00:00Z',
            all_day=False,label_id=2,note='원래 메모',location='',url='',has_recurrence=False,recurrences=[],attendees=[7],alerts=[10],updated_at='2026-11-01T00:00:00Z')
        self.calls=[]; self.fail=False
    def call(self,name,**kwargs):
        self.calls.append((name,copy.deepcopy(kwargs)))
        if name=='get_calendar_labels': return {'labels':[{'id':2,'name':'연구','color':0x2ecc87}]}
        if name=='get_events':
            event=copy.deepcopy(self.event)
            if 'start_before' in kwargs and event.get('has_recurrence'):
                event['start_at']='2026-11-10T01:00:00Z'; event['end_at']='2026-11-10T02:00:00Z'
            return {'events':[event] if event else []}
        if self.fail: raise TimeoutError('synthetic timeout')
        return {'success':True,'event':copy.deepcopy(self.event)}


class TreeWriteTests(unittest.TestCase):
    def setUp(self):
        self.folder=tempfile.TemporaryDirectory(prefix='daydesk-write-test-'); self.addCleanup(self.folder.cleanup)
        env=patch.dict(os.environ,{'DAYDESK_DATA_DIR':self.folder.name,'DAYDESK_CACHE_DIR':str(Path(self.folder.name)/'cache')}); env.start(); self.addCleanup(env.stop)
        self.client=FakeClient()
    def request(self,action='event-update',key=None):
        info=tree.event_form(self.client,'calendar-a',key or {'uuid':'fixture-uuid'},'2026-11-03')
        return dict(action=action,calendar_id='calendar-a',event=info['event'],form=info['form'],version=info['version'],confirm_delete=action=='event-delete')
    def writes(self): return [(n,a) for n,a in self.client.calls if n.startswith(('create_','update_','delete_'))]

    def test_edit_sends_only_changes_and_preserves_other_event_fields(self):
        request=self.request(); request['form']['title']='수정한 회의'
        with patch.object(tree,'refresh_cached',return_value=''):
            result=tree.perform(self.client,request)
        self.assertTrue(result['saved'])
        self.assertEqual(self.writes(),[('update_event',{'calendar_id':'calendar-a','event_uuid':'fixture-uuid','title':'수정한 회의'})])

    def test_unchanged_edit_does_not_write(self):
        self.assertFalse(tree.perform(self.client,self.request())['changed'])
        self.assertEqual(self.writes(),[])

    def test_same_day_all_day_uses_inclusive_utc_midnight(self):
        form=self.request()['form']; form.update(all_day=True,start_date='2026-11-03',end_date='2026-11-03')
        value=tree.validate(form)
        self.assertEqual(value['start_at'],value['end_at'])
        self.assertEqual(value['start_at'],int(datetime(2026,11,3,tzinfo=timezone.utc).timestamp()*1000))

    def test_timed_event_uses_korean_time(self):
        value=tree.validate(self.request()['form'])
        self.assertEqual(value['start_at'],int(datetime(2026,11,3,1,tzinfo=timezone.utc).timestamp()*1000))
        self.assertEqual(value['start_timezone'],'Asia/Seoul')

    def test_invalid_times_never_write(self):
        for changes in ({'end_time':'09:00'},{'title':' '},{'label_id':True},{'start_time':'27:00'}):
            request=self.request(); request['form'].update(changes)
            with self.assertRaises(tree.InputError): tree.perform(self.client,request)
        self.assertEqual(self.writes(),[])

    def test_stale_event_is_not_overwritten(self):
        request=self.request(); request['form']['title']='수정'; self.client.event['note']='다른 곳에서 수정'
        with self.assertRaisesRegex(tree.InputError,'다른 곳'): tree.perform(self.client,request)
        self.assertEqual(self.writes(),[])

    def test_recurring_edit_changes_only_selected_occurrence(self):
        self.client.event.update(has_recurrence=True,recurrences=['RRULE:FREQ=WEEKLY'])
        request=self.request(key={'uuid':'fixture-uuid','start':'2026-11-10T10:00:00+09:00'}); request['form']['note']='이 회차 메모'
        with patch.object(tree,'refresh_cached',return_value=''): tree.perform(self.client,request)
        self.assertEqual(self.writes(),[('update_event_occurrence',dict(calendar_id='calendar-a',event_uuid='fixture-uuid',occurrence_start_at='2026-11-10T01:00:00Z',scope='this',note='이 회차 메모'))])

    def test_recurring_delete_is_confirmed_and_scoped(self):
        self.client.event.update(has_recurrence=True,recurrences=['RRULE:FREQ=WEEKLY'])
        request=self.request('event-delete',{'uuid':'fixture-uuid','start':'2026-11-10T10:00:00+09:00'})
        request['confirm_delete']=False
        with self.assertRaises(tree.InputError): tree.perform(self.client,request)
        self.assertEqual(self.writes(),[])
        request['confirm_delete']=True
        with patch.object(tree,'refresh_cached',return_value=''): tree.perform(self.client,request)
        self.assertEqual(self.writes()[0][0],'delete_event_occurrence'); self.assertEqual(self.writes()[0][1]['scope'],'this')

    def test_ambiguous_write_is_never_retried(self):
        request=self.request('event-add'); self.client.fail=True
        with patch.object(tree,'refresh_cached') as refresh:
            with self.assertRaisesRegex(tree.InputError,'중복 실행'): tree.perform(self.client,request)
        self.assertEqual(len(self.writes()),1); refresh.assert_not_called()

    def test_acknowledged_write_keeps_success_when_cache_refresh_fails(self):
        request=self.request(); request['form']['title']='수정'
        with patch.object(tree,'refresh_cached',side_effect=OSError('synthetic disk error')):
            result=tree.perform(self.client,request)
        self.assertTrue(result['saved']); self.assertTrue(result['cache_warning'])

    def test_refresh_updates_old_cached_months_and_preserves_other_calendars(self):
        for calendar in ('calendar-a','calendar-b'):
            for month in (date(2024,1,1),date(2026,11,1)):
                cache_store.write('timetree',f'{calendar}|{month:%Y-%m}',dict(model=backend.model_for([],[],month,'month'),warning='old'))
        with patch.object(tree,'load_events',return_value=([],[],'')):
            tree.refresh_cached(self.client,'calendar-a',date(2026,12,1))
        self.assertEqual(cache_store.read('timetree','calendar-a|2024-01')[0]['warning'],'')
        self.assertEqual(cache_store.read('timetree','calendar-b|2024-01')[0]['warning'],'old')
        self.assertIsNotNone(cache_store.read('timetree','calendar-a|2026-12'))

    def test_uuid_and_occurrence_metadata_survive_native_model(self):
        event=self.client.event
        value=backend.model_for([event],[],date(2026,11,3),'month')['events'][0]
        self.assertEqual(value['id'],event['uuid']); self.assertEqual(value['source_start'],event['start_at'])

    def test_cli_write_options_and_delete_confirmation(self):
        p=daydesk.parser()
        args=p.parse_args(['timetree','add','--title','회의','--start','2026-11-05T09:00','--end','2026-11-05T10:00'])
        self.assertEqual(args.title,'회의')
        with self.assertRaises(ValueError): p.parse_args(['timetree','delete','--id','fixture-uuid'])
        args=p.parse_args(['timetree','delete','--id','fixture-uuid','--yes'])
        self.assertTrue(args.yes)


if __name__=='__main__': unittest.main()
