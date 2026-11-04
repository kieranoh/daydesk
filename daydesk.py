"""TimeTree + Todo Mate desktop controls. Uses the existing local account connections."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import queue
import subprocess
import sys
import threading
import time
from datetime import date, datetime, timedelta

from calendar_data import KST, TimeTree, visible_days, ConnectionError as TimeTreeError
from backend import sync_months, month_at
import cache_store

ROOT = Path(__file__).resolve().parent
SETTINGS = cache_store.data_root()/"settings.json"
TODO = ROOT / "FirebaseTodo.exe"
PIPE = r"\\.\pipe\TimeTreeWidget.Commands"
if "DAYDESK_DATA_DIR" in os.environ:
    PIPE += "."+hashlib.sha256(os.environ["DAYDESK_DATA_DIR"].encode()).hexdigest()[:12]


class Parser(argparse.ArgumentParser):
    def error(self, message):
        raise ValueError(message)


def iso_day(value):
    try:
        parsed = date.fromisoformat(value)
        if parsed.isoformat() == value:
            return value
    except ValueError:
        pass
    raise argparse.ArgumentTypeError("날짜는 YYYY-MM-DD 형식입니다.")


def transparency(value):
    try:
        number = float(value)
        if 0 <= number <= 95:
            return number
    except ValueError:
        pass
    raise argparse.ArgumentTypeError("투명도는 0~95입니다.")


def todo_width(value):
    try:
        number = int(value)
        if 260 <= number <= 600:
            return number
    except ValueError:
        pass
    raise argparse.ArgumentTypeError("Todo Mate 폭은 260~600px입니다.")

def tree_time(value):
    try:
        if len(value)==10: iso_day(value)
        elif datetime.strptime(value,"%Y-%m-%dT%H:%M").strftime("%Y-%m-%dT%H:%M")!=value: raise ValueError()
        return value
    except (ValueError,argparse.ArgumentTypeError):
        raise argparse.ArgumentTypeError("날짜·시간은 YYYY-MM-DD 또는 YYYY-MM-DDTHH:MM 형식입니다.") from None


def parser():
    p = Parser(prog="daydesk", description="TimeTree · Todo Mate 위젯 관리 (날짜·시간: 한국 기준)",
               epilog="모든 명령에 --json을 붙이면 JSON으로 출력합니다. 예: daydesk todo list --json")
    sub = p.add_subparsers(dest="command", required=True)
    sub.add_parser("status", help="위젯 상태·자동 실행 상태 확인")
    opening = sub.add_parser("open", help="위젯 실행/복원")
    opening.add_argument("--date", type=iso_day)
    sub.add_parser("close", help="저장 중인 작업이 없을 때 위젯 종료")
    sub.add_parser("refresh", help="두 서비스 새로고침")
    settings = sub.add_parser("settings", help="설정 조회/변경; 실행 중인 화면에도 즉시 반영")
    settings.add_argument("--transparency", type=transparency, help="배경 투명도 0~95; 유리빛 65, 선명하게 45")
    settings.add_argument("--pin", choices=("on", "off"), help="항상 위에 표시")
    settings.add_argument("--pin-back", choices=("on", "off"), help="다른 프로그램 뒤로 고정")
    settings.add_argument("--sync-minutes", type=int, choices=(0,15,30,60), help="두 서비스 자동 동기화 간격; 0은 수동 새로고침만")
    settings.add_argument("--todo-width", type=todo_width, metavar="260~600", help="Todo Mate 영역의 가로 폭(px)")
    settings.add_argument("--todo-visible", choices=("on", "off"), help="Todo Mate 사이드 패널 표시·숨기기")
    settings.add_argument("--recommended", action="store_true", help="저장된 추천 설정으로 즉시 복원")
    auto = sub.add_parser("autostart", help="Windows 로그인 시 자동 실행")
    auto.add_argument("mode", choices=("on", "off", "status"))
    tree = sub.add_parser("timetree", help="TimeTree 일정 조회·추가·수정·삭제")
    tree_sub = tree.add_subparsers(dest="action", required=True)
    tree_calendars = tree_sub.add_parser("calendars", help="캘린더 이름과 ID")
    tree_calendars.add_argument("--refresh", action="store_true", help="저장된 목록 대신 서버에서 다시 조회")
    events = tree_sub.add_parser("events", help="선택한 날짜/주/월 일정")
    events.add_argument("--date", type=iso_day)
    events.add_argument("--view", choices=("day", "week", "month"), default="day")
    events.add_argument("--calendar", help="캘린더 ID 또는 정확한 이름; 생략 시 위젯의 선택 사용")
    events.add_argument("--refresh", action="store_true", help="서버에서 다시 조회하여 저장")
    for action,help_text in (("add","일정 추가"),("edit","일정 수정"),("delete","일정 삭제")):
        command=tree_sub.add_parser(action,help=help_text)
        command.add_argument("--calendar",help="캘린더 ID 또는 정확한 이름")
        if action!="add":
            command.add_argument("--id",required=True,help="events에서 확인한 일정 UUID")
            command.add_argument("--occurrence",help="반복 회차의 start 값 (시간대 포함 ISO 형식); 선택한 회차만 변경")
        if action=="delete": command.add_argument("--yes",action="store_true",required=True,help="삭제 확인")
        else:
            command.add_argument("--title",required=action=="add")
            command.add_argument("--start",type=tree_time,required=action=="add",help="한국 시간 YYYY-MM-DDTHH:MM; 종일이면 YYYY-MM-DD")
            command.add_argument("--end",type=tree_time,required=action=="add",help="종일 일정은 마지막 날짜를 포함합니다.")
            command.add_argument("--all-day",action=argparse.BooleanOptionalAction,default=None)
            command.add_argument("--label",type=int,choices=range(1,11),help="일정 색상 라벨 ID")
            for field in ("note","location","url"): command.add_argument("--"+field)
    todo = sub.add_parser("todo", help="Todo Mate Firebase 조회·추가·수정·삭제·완료",
                          epilog="Firebase 직접 연결. 저장 응답이 끊기면 list --refresh로 반영 여부를 확인해 주세요.")
    todo_sub = todo.add_subparsers(dest="action", required=True)
    listing = todo_sub.add_parser("list", help="할 일 조회 (ID 포함)")
    listing.add_argument("--date", type=iso_day)
    listing.add_argument("--view", choices=("day", "week", "month"), default="day")
    listing.add_argument("--refresh", action="store_true", help="서버에서 다시 조회하여 저장")
    todo_goals = todo_sub.add_parser("goals", help="현재 카테고리 조회")
    todo_goals.add_argument("--refresh", action="store_true")
    add = todo_sub.add_parser("add", help="할 일 추가")
    add.add_argument("--goal", required=True)
    add.add_argument("--content", required=True)
    add.add_argument("--date", type=iso_day)
    done = todo_sub.add_parser("done", help="완료; --undo로 완료 취소")
    done.add_argument("--id", required=True)
    done.add_argument("--undo", action="store_true")
    reminder = todo_sub.add_parser("remind", help="알림 시간 변경 (한국 시간)")
    reminder.add_argument("--id", required=True)
    reminder.add_argument("--at", required=True, metavar="YYYY-MM-DDTHH:MM")
    todo_sub.add_parser("login", help="Firebase 계정 연결 창 열기")
    todo_sub.add_parser("status", help="Firebase 연결 상태")
    edit = todo_sub.add_parser("edit", help="내용·날짜·카테고리 수정")
    edit.add_argument("--id", required=True)
    edit.add_argument("--content")
    edit.add_argument("--date", type=iso_day)
    edit.add_argument("--goal", help="카테고리 ID 또는 고유한 이름")
    edit.add_argument("--clear-reminder", action="store_true")
    delete = todo_sub.add_parser("delete", help="본인 할 일 삭제")
    delete.add_argument("--id", required=True)
    editor = todo_sub.add_parser("editor", help="기존 기본 브라우저에서 Todo Mate 열기")
    editor.add_argument("--task", default="", help="위젯에 표시할 할 일 찾기 안내 (사이트 자동 검색은 지원하지 않음)")
    return p


def saved():
    try:
        value = json.loads(SETTINGS.read_text(encoding="utf-8-sig"))
        return value if isinstance(value, dict) else {}
    except (OSError, ValueError):
        return {}


def rpc(request):
    result = queue.Queue()
    def worker():
        try:
            with open(PIPE, "r+b", buffering=0) as pipe:
                pipe.write((json.dumps(request, ensure_ascii=False) + "\n").encode("utf-8"))
                line = bytearray()
                while len(line) < 65536:
                    char = pipe.read(1)
                    if char == b"\n":
                        reply = json.loads(line)
                        if not reply.get("ok"):
                            raise ValueError(reply.get("error", "위젯 명령을 처리하지 못했습니다."))
                        result.put(reply["result"])
                        return
                    if not char:
                        break
                    line.extend(char)
                raise ValueError("위젯 응답을 읽지 못했습니다. status로 확인해 주세요.")
        except Exception as error:
            result.put(error)
    threading.Thread(target=worker, daemon=True).start()
    try:
        value = result.get(timeout=10)
    except queue.Empty:
        raise TimeoutError("위젯 응답 시간이 초과되었습니다. status로 적용 여부를 확인해 주세요.") from None
    if isinstance(value, Exception):
        raise value
    return value


def control(request, launch=False):
    try:
        return rpc(request)
    except FileNotFoundError:
        if not launch:
            raise ValueError("위젯이 실행 중이 아닙니다. daydesk open으로 실행해 주세요.") from None
    exe = ROOT / "TimeTreeWidget.exe"
    if not exe.is_file():
        raise ValueError("TimeTreeWidget.exe가 없습니다. build.ps1로 빌드해 주세요.")
    python = Path(sys.executable).with_name("python.exe")
    subprocess.Popen([str(exe), str(python)], cwd=ROOT,
                     creationflags=subprocess.DETACHED_PROCESS | subprocess.CREATE_NEW_PROCESS_GROUP)
    deadline = time.monotonic() + 10
    while time.monotonic() < deadline:
        try:
            return rpc(request)
        except FileNotFoundError:
            time.sleep(.1)
    raise TimeoutError("위젯 실행이 지연되고 있습니다. 잠시 후 status로 확인해 주세요.")


def startup(mode):
    # Use Windows' actual redirected Startup folder rather than guessing its path.
    script = ROOT / "startup.ps1"
    powershell=Path(os.environ["SystemRoot"])/"System32/WindowsPowerShell/v1.0/powershell.exe"
    process = subprocess.run([str(powershell), "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass",
                              "-File", str(script), mode], capture_output=True, timeout=20,
                             creationflags=subprocess.CREATE_NO_WINDOW)
    if process.returncode:
        raise ValueError("자동 실행 설정을 처리하지 못했습니다. 시작프로그램 바로가기를 확인해 주세요.")
    return {"autostart": process.stdout.strip().endswith(b"True")}


def todo_native(command):
    if not TODO.is_file():
        raise ValueError("FirebaseTodo.exe를 찾지 못했습니다. build.ps1로 빌드해 주세요.")
    try:
        process = subprocess.run([str(TODO), *command], stdin=subprocess.DEVNULL, capture_output=True,
                                 text=True, encoding="utf-8-sig", timeout=150, creationflags=subprocess.CREATE_NO_WINDOW)
    except subprocess.TimeoutExpired:
        raise TimeoutError("Todo Mate 응답이 지연됩니다. list로 반영 여부를 확인한 뒤 재시도해 주세요.") from None
    if process.returncode:
        try:
            message = json.loads(process.stderr)["error"]
        except (ValueError, KeyError):
            message = "Todo Mate 명령을 처리하지 못했습니다. 계정 연결을 확인해 주세요."
        raise ValueError(message)
    return json.loads(process.stdout)


def todo_command(args):
    if args.action == "login":
        subprocess.Popen([str(ROOT / "FirebaseAccount.exe")], stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL,
                         stderr=subprocess.DEVNULL, creationflags=subprocess.CREATE_NO_WINDOW)
        return {"login_window": True, "message": "Firebase 계정 연결 창에서 직접 로그인해 주세요."}
    today = datetime.now(KST).date().isoformat()
    if args.action == "list":
        command = ["list", "--date", args.date or today, "--view", args.view]
        if args.refresh:
            command.append("--refresh")
    elif args.action == "goals":
        command = ["goals"] + (["--refresh"] if args.refresh else [])
    elif args.action == "add":
        command = ["add", "--goal", args.goal, "--content", args.content, "--date", args.date or today]
    elif args.action == "done":
        command = ["edit", "--id", args.id, "--done", "false" if args.undo else "true"]
    elif args.action == "remind":
        try:
            when = datetime.strptime(args.at, "%Y-%m-%dT%H:%M")
            if when.strftime("%Y-%m-%dT%H:%M") != args.at:
                raise ValueError()
        except ValueError:
            raise ValueError("알림 시간은 YYYY-MM-DDTHH:MM 형식입니다.") from None
        command = ["edit", "--id", args.id, "--remind-at", args.at]
    elif args.action == "edit":
        command = ["edit", "--id", args.id]
        for key in ("content", "date", "goal"):
            value = getattr(args, key)
            if value is not None:
                command.extend(["--" + key, value])
        if args.clear_reminder:
            command.append("--clear-reminder")
        if len(command) == 3:
            raise ValueError("변경할 내용을 지정해 주세요.")
    elif args.action == "delete":
        command = ["delete", "--id", args.id]
    else:
        command = ["status"]
    result = todo_native(command)
    if args.action in ("add", "edit", "delete", "done", "remind"):
        try:
            rpc({"op": "todo-cache"})
        except (OSError, ValueError, TimeoutError):
            pass  # The remote write succeeded; a sleeping widget reads the shared cache on startup.
    return result


def timetree_command(args):
    client = TimeTree()
    try:
        cached = cache_store.read("timetree","calendars")
        calendars = cached[0]["calendars"] if cached else []
        if not calendars or getattr(args,"refresh",False):
            client.start()
            calendars = client.call("list_calendars")["calendars"]
            cache_store.write("timetree","calendars",{"calendars":calendars})
        if args.action == "calendars":
            return calendars
        key = args.calendar or saved().get("calendar_id")
        matches = [c for c in calendars if str(c["id"]) == str(key) or c["name"] == key] if key else calendars
        if len(matches) != 1:
            raise ValueError("캘린더를 하나로 정할 수 없습니다. timetree calendars에서 확인 후 --calendar를 지정해 주세요.")
        if args.action in ("add","edit","delete"):
            from timetree_write import event_form,perform
            if args.action=="edit" and all(getattr(args,key) is None for key in ("title","start","end","all_day","label","note","location","url")):
                raise ValueError("변경할 일정 내용을 지정해 주세요.")
            if client.process is None: client.start()
            calendar_id=str(matches[0]["id"])
            key=None if args.action=="add" else {"uuid":args.id,"start":args.occurrence} if args.occurrence else {"uuid":args.id}
            info=event_form(client,calendar_id,key,datetime.now(KST).date().isoformat())
            form=info['form']
            if args.action!="delete":
                for field in ("title","note","location","url"):
                    if getattr(args,field) is not None: form[field]=getattr(args,field)
                if args.all_day is not None: form['all_day']=args.all_day
                elif args.action=="add": form['all_day']=len(args.start)==10
                if args.label is not None: form['label_id']=args.label
                for field in ("start","end"):
                    value=getattr(args,field)
                    if value is None: continue
                    if form['all_day']:
                        if len(value)!=10: raise ValueError("종일 일정 날짜는 YYYY-MM-DD 형식입니다.")
                        form[field+'_date']=value
                    else:
                        if len(value)!=16: raise ValueError("시간 일정은 YYYY-MM-DDTHH:MM 형식입니다.")
                        form[field+'_date'],form[field+'_time']=value.split('T')
            result=perform(client,dict(action={'add':'event-add','edit':'event-update','delete':'event-delete'}[args.action],calendar_id=calendar_id,
                event=info['event'],version=info['version'],form=form,confirm_delete=args.action=='delete'))
            try: rpc({"op":"timetree-cache"})
            except (OSError,ValueError,TimeoutError): pass
            return result
        day = date.fromisoformat(args.date) if args.date else datetime.now(KST).date()
        if args.view == "day":
            start = end = day
        elif args.view == "week":
            days = visible_days(day, "week")
            start, end = days[0], days[-1]
        else:
            start = day.replace(day=1)
            end = (start.replace(day=28) + timedelta(days=4)).replace(day=1) - timedelta(days=1)
        cached = cache_store.read("timetree",f"{matches[0]['id']}|{day:%Y-%m}")
        if cached and not args.refresh:
            value, checked = cached
        else:
            if client.process is None:
                client.start()
            value, checked = sync_months(client,matches[0]["id"],day)
        lo = datetime.combine(start,datetime.min.time(),KST)
        hi = datetime.combine(end+timedelta(days=1),datetime.min.time(),KST)
        normalized = [e for e in value["model"]["events"] if datetime.fromisoformat(e["start"]) < hi and datetime.fromisoformat(e["end"]) > lo]
        normalized.sort(key=lambda e: (e["start"], e["title"]))
        return {"calendar": matches[0]["name"], "start": start.isoformat(), "end": end.isoformat(),
                "events": normalized, "warning": value["warning"],"cached":bool(cached and not args.refresh),"updated_at":checked.isoformat()}
    finally:
        client.close()


def run(args):
    if args.command == "autostart":
        return startup(args.mode)
    if args.command == "timetree":
        return timetree_command(args)
    if args.command == "todo":
        if args.action == "editor":
            return control({"op": "editor", "task": args.task}, launch=True)
        return todo_command(args)
    if args.command == "settings" and args.pin=='on' and args.pin_back=='on':
        raise ValueError("항상 위와 가장 뒤 고정은 동시에 켤 수 없습니다.")
    if args.command == "settings" and args.recommended and any(getattr(args,k) is not None for k in ("transparency","pin","pin_back","sync_minutes","todo_width","todo_visible")):
        raise ValueError("추천 설정은 다른 설정 옵션과 함께 적용할 수 없습니다.")
    if args.command == "status" or (args.command == "settings" and not args.recommended and all(getattr(args, k) is None for k in ("transparency", "pin", "pin_back","sync_minutes","todo_width","todo_visible"))):
        try:
            state = rpc({"op": "status"})
        except FileNotFoundError:
            prefs = saved()
            state = {"running": False, "view": "month",
                     "transparency": prefs.get("background_transparency", 65), "pin": prefs.get("pin", False),"pin_back":prefs.get("pin_back",False),"sync_minutes":prefs.get("sync_minutes",30),"todo_width":prefs.get("todo_width",300),"todo_visible":prefs.get("todo_visible",True)}
        return state | startup("status")
    request = {"op": args.command}
    for key in ("date", "transparency", "pin", "pin_back", "sync_minutes","todo_width","todo_visible"):
        value = getattr(args, key, None)
        if value is not None:
            request[key] = (value == "on") if key in ("pin","pin_back","todo_visible") else value
    if getattr(args,"recommended",False):
        request["recommended"]=True
    result = control(request, launch=args.command in ("open", "settings"))
    return {"closing": True} if args.command == "close" else result


def clean(value):
    return " ".join("".join(c for c in str(value) if c.isprintable() or c in "\r\n\t").split())


def display(value):
    if isinstance(value, dict) and value.get("opened") == "browser":
        return "기본 브라우저에서 Todo Mate를 열었습니다. 편집 후 위젯으로 돌아오시면 갱신됩니다."
    if isinstance(value, dict) and "running" in value:
        rows = ["위젯: " + ("실행 중" if value["running"] else "종료됨"),
                "보기: " + {"month": "월간", "week": "주간"}.get(value["view"], value["view"]),
                f"배경 투명도: {value['transparency']:g}%", "항상 위: " + ("켜짐" if value["pin"] else "꺼짐"),"가장 뒤 고정: "+("켜짐" if value.get("pin_back") else "꺼짐")]
        if "date" in value:
            rows.append("선택 날짜: " + value["date"])
        if "autostart" in value:
            rows.append("자동 실행: " + ("켜짐" if value["autostart"] else "꺼짐"))
        if "sync_minutes" in value:
            rows.append("동기화: " + (f"{value['sync_minutes']}분마다" if value["sync_minutes"] else "수동 새로고침만"))
        if "todo_width" in value:
            rows.append(f"Todo Mate 폭: {value['todo_width']:g}px")
        if "todo_visible" in value:
            rows.append("Todo Mate 표시: " + ("켜짐" if value["todo_visible"] else "꺼짐"))
        return "\n".join(rows)
    if isinstance(value, dict) and "autostart" in value:
        return "자동 실행: " + ("켜짐" if value["autostart"] else "꺼짐")
    if isinstance(value, dict) and value.get("closing"):
        return "위젯을 종료합니다."
    if isinstance(value, dict) and "todos" in value:
        return "\n".join(f"{'[완료]' if t['done'] else '[ ]'} {t['date']} {clean(t['category'])} · {clean(t['content'])}  (ID: {clean(t['id'])})" for t in value["todos"]) or "할 일이 없습니다."
    if isinstance(value, dict) and "events" in value:
        return "\n".join([f"{clean(value['calendar'])} · {value['start']} ~ {value['end']}"] +
                         [f"{e['start'][:16]}  {clean(e['title'])}" for e in value["events"]] +
                         ([clean(value["warning"])] if value["warning"] else []))
    if isinstance(value, list):
        return "\n".join(f"{clean(item.get('name', item.get('title', '')))}  (ID: {clean(item['id'])})" for item in value) or "항목이 없습니다."
    return json.dumps(value, ensure_ascii=False, indent=2)


def main(argv=None):
    argv = list(sys.argv[1:] if argv is None else argv)
    as_json = "--json" in argv
    argv = [a for a in argv if a != "--json"]
    for stream in (sys.stdout, sys.stderr):
        if hasattr(stream, "reconfigure"):
            stream.reconfigure(encoding="utf-8", errors="replace")
    try:
        p = parser()
        if not argv:
            p.print_help()
            return 0
        result = run(p.parse_args(argv))
        print(json.dumps(result, ensure_ascii=False) if as_json else display(result))
        return 0
    except (OSError, ValueError, TimeoutError, subprocess.SubprocessError, TimeTreeError) as error:
        message = str(error)
        print(json.dumps({"error": message}, ensure_ascii=False) if as_json else "오류: " + clean(message), file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
