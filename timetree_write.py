"""TimeTree writes with validation, occurrence scope and shared encrypted cache refresh."""
from collections import Counter
from datetime import date, datetime, timedelta, timezone

import cache_store
from calendar_data import TimeTree, ConnectionError, KST, event_span, stamp, visible_days, event_version as version
from backend import load_events, model_for, month_at


class InputError(ConnectionError):
    pass


def form_for(event):
    start,end=event_span(event)
    if event['all_day']: end-=timedelta(days=1)
    return dict(title=event['title'],all_day=event['all_day'],start_date=start.date().isoformat(),
        start_time=start.strftime('%H:%M'),end_date=end.date().isoformat(),end_time=end.strftime('%H:%M'),
        label_id=event.get('label_id') or 1,note=event.get('note') or '',location=event.get('location') or '',url=event.get('url') or '')


def cached_event_form(request):
    calendar_id=request['calendar_id']
    anchor=date.fromisoformat(request.get('anchor') or request['date'])
    saved=cache_store.read('timetree',f'{calendar_id}|{anchor:%Y-%m}')
    if not saved: return None
    model=saved[0].get('model',{})
    key=request.get('event')
    if key:
        matches=[e for e in model.get('events',[]) if (e.get('uuid') or e.get('id'))==(key.get('uuid') or key.get('id')) and e.get('source_start')==key.get('source_start')]
        if len(matches)!=1: return None
        event=matches[0]
        end=stamp(event['end'])-timedelta(days=1) if event['all_day'] else stamp(event['end'])
        form=form_for(dict(event,start_at=event['start'],end_at=end.isoformat(),start_timezone='Asia/Seoul',end_timezone='Asia/Seoul'))
        result=dict(form=form,event=dict(uuid=event.get('uuid') or event['id'],source_start=event['source_start']),version=event.get('edit_version',''),original_form=form.copy(),recurring=bool(event.get('has_recurrence')))
    else:
        day=date.fromisoformat(request['date']).isoformat()
        result=dict(form=dict(title='',all_day=True,start_date=day,end_date=day,start_time='09:00',end_time='10:00',label_id=1,note='',location='',url=''),event=None,version='',recurring=False)
    labels=cache_store.read('timetree',calendar_id+'|labels')
    labels=labels[0]['labels'] if labels else model.get('labels')
    result.update(labels=labels or [],labels_cached=labels is not None,cached=True)
    return result


def find_event(client,calendar_id,key):
    if not isinstance(key,dict): raise InputError('편집할 일정을 선택해 주세요.')
    events=client.call('get_events',calendar_id=calendar_id,include_memos=False,expand_recurring=False)['events']
    identity=key.get('uuid') or key.get('id')
    if identity:
        found=[e for e in events if str(e.get('uuid') or e.get('id'))==str(identity)]
    else:
        # Migrate older snapshots that did not retain event UUIDs; never guess among duplicates.
        found=[e for e in events if e['title']==key.get('title') and not e.get('has_recurrence') and event_span(e)[0]==stamp(key['start'])]
    if len(found)!=1: raise InputError('일정이 변경되었거나 식별할 수 없습니다. 새로고침한 뒤 다시 열어 주세요.')
    event=found[0]
    if event.get('has_recurrence'):
        if not key.get('source_start') and not key.get('start'): raise InputError('반복 일정은 선택한 회차의 시작 시간이 필요합니다.')
        at=stamp(key.get('source_start') or key['start'])
        duration=event_span(event)[1]-event_span(event)[0]
        expanded=client.call('get_events',calendar_id=calendar_id,include_memos=False,
            start_after=int((at-duration-timedelta(days=1)).timestamp()*1000),start_before=int((at+timedelta(days=2)).timestamp()*1000))
        found=[e for e in expanded['events'] if e.get('uuid')==event.get('uuid') and
            (stamp(e['start_at'])==at if key.get('source_start') else event_span(e)[0]==at)]
        if len(found)!=1: raise InputError('선택한 반복 회차를 찾지 못했습니다. 새로고침한 뒤 다시 열어 주세요.')
        event=found[0]
    return event


def event_form(client,calendar_id,key,selected):
    labels=client.call('get_calendar_labels',calendar_id=calendar_id)['labels']
    if key:
        event=find_event(client,calendar_id,key)
        return dict(labels=labels,form=form_for(event),event=dict(uuid=event['uuid'],source_start=event['start_at']),
            version=version(event),recurring=bool(event.get('has_recurrence')))
    day=date.fromisoformat(selected)
    form=dict(title='',all_day=True,start_date=day.isoformat(),end_date=day.isoformat(),start_time='09:00',end_time='10:00',label_id=1,note='',location='',url='')
    return dict(labels=labels,form=form,event=None,version='',recurring=False)


def validate(form):
    if not isinstance(form,dict): raise InputError('일정 내용을 입력해 주세요.')
    if not isinstance(form.get('title'),str) or not form['title'].strip(): raise InputError('일정 제목을 입력해 주세요.')
    if type(form.get('all_day')) is not bool or type(form.get('label_id')) is not int or not 1<=form['label_id']<=10:
        raise InputError('종일 여부와 일정 색상을 확인해 주세요.')
    for field in ('note','location','url'):
        if not isinstance(form.get(field,''),str): raise InputError('일정 내용을 확인해 주세요.')
    try:
        start_day=date.fromisoformat(form['start_date']); end_day=date.fromisoformat(form['end_date'])
        if form['all_day']:
            start=datetime.combine(start_day,datetime.min.time(),timezone.utc)
            end=datetime.combine(end_day,datetime.min.time(),timezone.utc)
            if end<start: raise ValueError()
            zone='UTC'
        else:
            start=datetime.strptime(form['start_date']+'T'+form['start_time'],'%Y-%m-%dT%H:%M').replace(tzinfo=KST)
            end=datetime.strptime(form['end_date']+'T'+form['end_time'],'%Y-%m-%dT%H:%M').replace(tzinfo=KST)
            if start.strftime('%H:%M')!=form['start_time'] or end.strftime('%H:%M')!=form['end_time'] or end<=start: raise ValueError()
            zone='Asia/Seoul'
    except (KeyError,ValueError,TypeError): raise InputError('시작·종료 날짜와 시간을 확인해 주세요. 종료 시간은 시작 시간보다 늦어야 합니다.') from None
    return dict(title=form['title'],all_day=form['all_day'],start_at=int(start.timestamp()*1000),end_at=int(end.timestamp()*1000),
        start_timezone=zone,end_timezone=zone,label_id=form['label_id'],note=form.get('note',''),location=form.get('location',''),url=form.get('url',''))


def refresh_cached(client,calendar_id,anchor):
    months={month_at(anchor)}
    folder=cache_store.root()/'timetree'
    for file in folder.glob('*.bin'):
        saved=cache_store.read_file(file)
        if not saved: continue
        days=saved[0].get('model',{}).get('days',[])
        if not days: continue
        try: month=date.fromisoformat(Counter(d[:7] for d in days).most_common(1)[0][0]+'-01')
        except (ValueError,TypeError): continue
        if file==cache_store.path('timetree',f'{calendar_id}|{month:%Y-%m}'): months.add(month)
    source={}; warning=''
    for month in sorted(months,key=lambda m:(m!=month_at(anchor),m)):
        try:
            days=visible_days(month,'month')
            events,labels,notice=load_events(client,calendar_id,days[0],days[-1]+timedelta(days=1),source)
            if notice: warning='서버 저장은 완료되었습니다. 일부 반복 일정은 새로고침으로 확인해 주세요.'; continue
            value=dict(model=model_for(events,labels,month,'month'),warning='')
            key=f'{calendar_id}|{month:%Y-%m}'; previous=cache_store.read('timetree',key)
            if not previous or previous[0]!=value: cache_store.write('timetree',key,value)
        except Exception:
            warning='서버 저장은 완료되었습니다. 일부 캐시 갱신에 실패했습니다. 새로고침해 주세요.'
    return warning


def perform(client,request):
    action=request.get('action'); calendar_id=request.get('calendar_id')
    if not isinstance(calendar_id,str) or not calendar_id.strip(): raise InputError('TimeTree 캘린더를 선택해 주세요.')
    if action=='event-form': return event_form(client,calendar_id,request.get('event'),request.get('date',date.today().isoformat()))
    if action not in ('event-add','event-update','event-delete'): raise InputError('지원하지 않는 일정 작업입니다.')
    fields=validate(request.get('form')) if action!='event-delete' else None
    if action=='event-delete' and request.get('confirm_delete') is not True: raise InputError('일정 삭제를 확인해 주세요.')
    with cache_store.service_lock('timetree'):
        original=None
        if action!='event-add':
            original=find_event(client,calendar_id,request.get('event'))
            if (request.get('version')!=version(original) if request.get('version') else request.get('original_form')!=form_for(original)):
                raise InputError('다른 곳에서 변경된 일정입니다. 새로고침한 뒤 편집창을 다시 열어 주세요.')
            identity=dict(calendar_id=calendar_id,event_uuid=original['uuid'])
            recurring=bool(original.get('has_recurrence'))
            if recurring: identity.update(occurrence_start_at=original['start_at'],scope='this')
        if action=='event-delete':
            tool='delete_event_occurrence' if recurring else 'delete_event'; arguments=identity
            anchor=event_span(original)[0].date()
        elif action=='event-add':
            tool='create_event'; arguments=dict(calendar_id=calendar_id,**fields); anchor=date.fromisoformat(request['form']['start_date'])
        else:
            before=form_for(original); after=request['form']; changes={}
            for field in ('title','label_id','note','location','url'):
                if after.get(field,'')!=before[field]: changes[field]=fields[field]
            if recurring and after['all_day']!=before['all_day']: raise InputError('반복 회차의 종일 여부는 TimeTree 원본 앱에서 변경해 주세요.')
            time_fields=('all_day','start_date','end_date') if after['all_day'] else ('all_day','start_date','end_date','start_time','end_time')
            if any(after[field]!=before[field] for field in time_fields):
                changes.update({k:fields[k] for k in ('all_day','start_at','end_at','start_timezone','end_timezone')})
                if recurring: changes.pop('all_day')
            if not changes: return dict(saved=True,changed=False,date=after['start_date'],cache_warning='')
            tool='update_event_occurrence' if recurring else 'update_event'; arguments=dict(identity,**changes); anchor=date.fromisoformat(after['start_date'])
        try:
            result=client.call(tool,**arguments)
            if result.get('success') is not True: raise ConnectionError('서버의 저장 응답을 확인하지 못했습니다.')
        except Exception:
            # Never retry an ambiguous write; the server may already have accepted it.
            raise InputError('저장 응답을 확인하지 못했습니다. 중복 실행하지 마시고 창을 닫은 뒤 새로고침으로 반영 여부를 확인해 주세요.') from None
        try: warning=refresh_cached(client,calendar_id,anchor)
        except Exception: warning='서버 저장은 완료되었습니다. 캐시 갱신을 위해 새로고침해 주세요.'
        return dict(saved=True,changed=True,deleted=action=='event-delete',date=anchor.isoformat(),event=result.get('event'),cache_warning=warning)


def run(request):
    if request.get('action')=='event-form':
        try: cached=cached_event_form(request)
        except (KeyError,TypeError,ValueError): cached=None
        if cached is not None: return cached
    client=TimeTree()
    try:
        client.start()
        if request.get('action')=='event-labels':
            labels=client.call('get_calendar_labels',calendar_id=request['calendar_id'])['labels']
            cache_store.write('timetree',request['calendar_id']+'|labels',dict(labels=labels))
            return dict(labels=labels)
        return perform(client,request)
    finally: client.close()
