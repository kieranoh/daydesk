"""Calendar synchronization over private pipes, backed by encrypted month snapshots."""
import json
import queue
import sys
import threading
from datetime import timezone
import cache_store
from datetime import date, datetime, timedelta
from calendar_data import TimeTree, ConnectionError, KST, PALETTE, event_span, event_style, visible_days, month_layout, events_on, lanes


def load_events(client, calendar_id, start, end, source=None):
    source = {} if source is None else source
    if "labels" not in source:
        source["labels"] = client.call("get_calendar_labels", calendar_id=calendar_id)["labels"]
    if "master" not in source:
        source["master"] = client.call("get_events", calendar_id=calendar_id, include_memos=False, expand_recurring=False)["events"]
    labels, master = source["labels"], source["master"]
    events = [e for e in master if not e.get("has_recurrence")]
    recurring = [e for e in master if e.get("has_recurrence")]
    warning = ""
    if recurring:
        duration = max(event_span(e)[1] - event_span(e)[0] for e in recurring)
        lo = datetime.combine(start, datetime.min.time(), KST) - duration - timedelta(days=1)
        hi = datetime.combine(end + timedelta(days=1), datetime.min.time(), KST)
        expanded = client.call("get_events", calendar_id=calendar_id, include_memos=False,
                              start_after=int(lo.timestamp() * 1000), start_before=int(hi.timestamp() * 1000))
        events.extend(e for e in expanded["events"] if e.get("has_recurrence"))
        if expanded.get("truncated_series"):
            warning = "일부 반복 일정은 표시 한도에 도달했습니다."
    lo = datetime.combine(start, datetime.min.time(), KST)
    hi = datetime.combine(end, datetime.min.time(), KST)
    return [e for e in events if event_span(e)[0] < hi and event_span(e)[1] > lo], labels, warning


def model_for(events, labels, anchor, view):
    colors = {x["id"]: "#{:06x}".format((x["color"] if x.get("color") is not None else x.get("default_color") or 0) & 0xFFFFFF) for x in labels}
    normalized = {}
    for event in events:
        start, end = event_span(event)
        color = colors.get(event.get("label_id"), PALETTE[((event.get("label_id") or 1) - 1) % 10])
        background, foreground = event_style(event, color)
        normalized[id(event)] = dict(id=event.get("uuid") or event.get("id"),uuid=event.get("uuid"),source_start=event["start_at"],has_recurrence=bool(event.get("has_recurrence")),label_id=event.get("label_id"),title=event["title"],start=start.isoformat(),end=end.isoformat(),
            all_day=event["all_day"], color=color, background=background, foreground=foreground,
            location=event.get("location") or "", note=event.get("note") or "", url=event.get("url") or "")
    days = visible_days(anchor, view)
    weeks = []
    if view == "month":
        for offset in range(0, len(days), 7):
            placements = [dict(first=a, last=b, row=row, event=normalized[id(e)])
                          for a, b, row, e in month_layout(events, days[offset:offset + 7])]
            weeks.append(placements)
    blocks = []
    if view == "week":
        for column, day in enumerate(days):
            lo = datetime.combine(day, datetime.min.time(), KST)
            hi = lo + timedelta(days=1)
            items = []
            for event in events_on(events, day):
                if not event["all_day"]:
                    start, end = event_span(event)
                    a, b = (max(start, lo) - lo).total_seconds() / 3600, (min(end, hi) - lo).total_seconds() / 3600
                    items.append((a, max(a + .6, b), event))
            blocks.extend(dict(column=column, start=a, end=b, lane=lane, count=count, event=normalized[id(e)])
                          for a, b, e, lane, count in lanes(items))
    return dict(days=[d.isoformat() for d in days], weeks=weeks, blocks=blocks, events=list(normalized.values()),
                period=f"{anchor.year}년 {anchor.month}월" if view == "month" else f"{days[0]:%Y.%m.%d} – {days[-1]:%m.%d}")


def emit(value):
    print(json.dumps(value, ensure_ascii=False), flush=True)


def month_at(day, delta=0):
    serial = day.year * 12 + day.month - 1 + delta
    return date(serial // 12, serial % 12 + 1, 1)


def sync_months(client, calendar_id, anchor, on_current=None):
    with cache_store.service_lock("timetree"):
        return _sync_months(client, calendar_id, anchor, on_current)


def _sync_months(client, calendar_id, anchor, on_current):
    source = {}
    result = None
    current_updated = datetime.now(timezone.utc)
    # Publish the visible month first; populate the other months without blocking navigation.
    for offsets in ((-1,0,1),(-4,-3,-2),(2,3,4),(-6,-5),(5,6)):
        months = []
        for delta in offsets:
            try:
                months.append(month_at(anchor, delta))
            except ValueError:
                continue
        if not months:
            continue
        start = visible_days(months[0], "month")[0]
        end = visible_days(months[-1], "month")[-1] + timedelta(days=1)
        try:
            events, labels, warning = load_events(client, calendar_id, start, end, source)
        except Exception:
            if result is None:
                raise
            result = result | {"warning":"현재 달은 저장했습니다. 일부 다른 달은 연결 오류로 다음 동기화 때 다시 확인합니다."}
            break
        updated = datetime.now(timezone.utc)
        for month in sorted(months, key=lambda value:(value!=month_at(anchor),value)):
            days = visible_days(month, "month")
            lo = datetime.combine(days[0], datetime.min.time(), KST)
            hi = datetime.combine(days[-1] + timedelta(days=1), datetime.min.time(), KST)
            month_events, month_labels, month_warning = events, labels, warning
            if warning and len(months)>1:
                month_events, month_labels, month_warning = load_events(client, calendar_id, days[0], days[-1]+timedelta(days=1), source)
            selected = [e for e in month_events if event_span(e)[0] < hi and event_span(e)[1] > lo]
            value = {"model": model_for(selected, month_labels, month, "month"), "warning": month_warning}
            key = f"{calendar_id}|{month:%Y-%m}"
            previous = cache_store.read("timetree", key)
            if month_warning and previous:
                value = previous[0] | {"warning":month_warning+" 이전 저장 일정을 유지합니다."}
            else:
                cache_store.write("timetree", key, value, updated)
            if month == month_at(anchor):
                result, current_updated = value, updated
                if on_current:
                    on_current(result,current_updated)
    return result, current_updated


def main():
    sys.stdin.reconfigure(encoding="utf-8")
    sys.stdout.reconfigure(encoding="utf-8")
    requests = queue.Queue()
    def read():
        for line in sys.stdin:
            try:
                requests.put(json.loads(line))
            except ValueError:
                continue
        requests.put(None)
    threading.Thread(target=read, daemon=True).start()
    saved = cache_store.read("timetree", "calendars")
    client, calendars = None, saved[0].get("calendars", []) if saved else []
    if calendars:
        emit(dict(kind="ready", calendars=calendars, cached=True))
    def connected():
        nonlocal client
        if client is None:
            client = TimeTree()
            client.start()
        return client
    request = {"id": 0}
    try:
        while request is not None:
            try:
                if not calendars:
                    calendars = connected().call("list_calendars")["calendars"]
                    if not calendars:
                        raise ConnectionError("표시할 TimeTree 캘린더가 없습니다.")
                    cache_store.write("timetree", "calendars", {"calendars": calendars})
                    emit(dict(kind="ready", calendars=calendars, cached=False))
                if "anchor" in request:
                    anchor = date.fromisoformat(request["anchor"])
                    view = request["view"]
                    if view != "month":
                        raise ValueError("Unknown view")
                    force = bool(request.get("force"))
                    if force:
                        calendars = connected().call("list_calendars")["calendars"]
                        cache_store.write("timetree", "calendars", {"calendars": calendars})
                        emit(dict(kind="calendars", calendars=calendars))
                    calendar_id = request.get("calendar_id") or calendars[0]["id"]
                    if calendar_id not in {c["id"] for c in calendars}:
                        raise ValueError("Unknown calendar")
                    saved = cache_store.read("timetree", f"{calendar_id}|{anchor:%Y-%m}")
                    if saved and not force:
                        value, updated = saved
                        emit(dict(kind="view", id=request["id"], **value, updated_at=updated.isoformat(), cached=True))
                    else:
                        def publish(value, updated):
                            emit(dict(kind="view", id=request["id"], **value, updated_at=updated.isoformat(), cached=False))
                        value, updated = sync_months(connected(), calendar_id, anchor, publish)
                        if value["warning"]:
                            publish(value,updated)
            except Exception as error:
                message = str(error) if isinstance(error, ConnectionError) else "일정을 처리하지 못했습니다. 새로고침으로 다시 시도해 주세요."
                emit(dict(kind="error", id=request["id"], message=message))
                if client:
                    client.close()
                client = None
            request = requests.get()
            while request is not None and not requests.empty():
                next_request = requests.get_nowait()
                if next_request is None:
                    requests.put(None)
                    break
                request = next_request
    finally:
        if client:
            client.close()


if __name__ == "__main__":
    main()
