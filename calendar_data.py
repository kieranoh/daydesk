"""Read-only TimeTree desktop calendar. Python standard library only."""
import calendar
import hashlib
import json
import os
from pathlib import Path
import queue
import subprocess
import threading
import time
import cache_store
from datetime import date, datetime, timedelta, timezone
from zoneinfo import ZoneInfo, ZoneInfoNotFoundError

KST = timezone(timedelta(hours=9))
GREEN, INK, MUTED, LINE = "#2ECC87", "#242424", "#727272", "#E8E8E8"
PALETTE = ["#2ecc87", "#3dc2c8", "#47b2f7", "#948078", "#212121",
           "#e73b3b", "#f35f8c", "#fb7f77", "#fdc02d", "#b38bdc"]


def event_version(event):
    return hashlib.sha256(json.dumps(event,sort_keys=True,ensure_ascii=False).encode()).hexdigest()


def contrast(first, second):
    def luminance(color):
        rgb = [int(color[i:i+2], 16) / 255 for i in (1, 3, 5)]
        linear = [x / 12.92 if x <= .04045 else ((x + .055) / 1.055) ** 2.4 for x in rgb]
        return sum(a * b for a, b in zip(linear, (.2126, .7152, .0722)))
    a, b = sorted((luminance(first), luminance(second)))
    return (b + .05) / (a + .05)


def event_style(event, color):
    start, end = event_span(event)
    multi_day = start.date() != (end - timedelta(microseconds=1)).date()
    if event["all_day"] or multi_day:
        foreground = "#FFFFFF" if contrast(color, "#FFFFFF") >= 4.5 else INK
        return color, foreground if contrast(color, foreground) >= 4.5 else "#000000"
    background = "#" + "".join(f"{round(int(color[i:i+2], 16) * .14 + 255 * .86):02x}" for i in (1, 3, 5))
    foreground = color
    while contrast(background, foreground) < 4.5:
        foreground = "#" + "".join(f"{int(int(foreground[i:i+2], 16) * .9):02x}" for i in (1, 3, 5))
    return background, foreground


def stamp(value):
    return datetime.fromisoformat(value.replace("Z", "+00:00"))


def event_span(event):
    start, end = stamp(event["start_at"]), stamp(event["end_at"])
    if event["all_day"]:
        # TimeTree stores all-day end dates inclusively, including zero-length one-day events.
        try:
            start = start.astimezone(ZoneInfo(event.get("start_timezone") or "UTC"))
            end = end.astimezone(ZoneInfo(event.get("end_timezone") or "UTC"))
        except ZoneInfoNotFoundError:
            if (event.get("start_timezone") or "UTC") != "UTC":
                start, end = start.astimezone(KST), end.astimezone(KST)
        return (datetime.combine(start.date(), datetime.min.time(), KST),
                datetime.combine(max(start.date(), end.date()) + timedelta(days=1), datetime.min.time(), KST))
    start, end = start.astimezone(KST), end.astimezone(KST)
    return start, max(end, start + timedelta(minutes=1))


def events_on(events, day):
    lo = datetime.combine(day, datetime.min.time(), KST)
    hi = lo + timedelta(days=1)
    return sorted((e for e in events if event_span(e)[0] < hi and event_span(e)[1] > lo),
                  key=lambda e: (not e["all_day"], event_span(e)[0], e["title"]))


def visible_days(anchor, view):
    if view == "week":
        start = anchor - timedelta(days=(anchor.weekday() + 1) % 7)
        return [start + timedelta(days=i) for i in range(7)]
    return list(calendar.Calendar(firstweekday=6).itermonthdates(anchor.year, anchor.month))


def lanes(items):
    """Assign overlapping time blocks equal-width lanes, separately for each cluster."""
    result, cluster, ends = [], [], []
    for start, end, event in sorted(items, key=lambda item: (item[0], item[1])):
        if cluster and start >= max(ends):
            result.extend((a, b, e, lane, len(ends)) for a, b, e, lane in cluster)
            cluster, ends = [], []
        lane = next((i for i, value in enumerate(ends) if value <= start), len(ends))
        if lane == len(ends):
            ends.append(end)
        else:
            ends[lane] = end
        cluster.append((start, end, event, lane))
    result.extend((a, b, e, lane, len(ends)) for a, b, e, lane in cluster)
    return result


def month_layout(events, days):
    """Place an event in one continuous bar, with a free row across all covered days."""
    segments = []
    for event in events:
        covered = [i for i, day in enumerate(days) if events_on([event], day)]
        if covered:
            segments.append((covered[0], covered[-1], event))
    segments.sort(key=lambda item: (item[0] == item[1], not item[2]["all_day"],
                                    item[0], event_span(item[2])[0], item[2]["title"]))
    occupied, placements = [set() for _ in days], []
    for first, last, event in segments:
        row = 0
        while any(row in occupied[column] for column in range(first, last + 1)):
            row += 1
        for column in range(first, last + 1):
            occupied[column].add(row)
        placements.append((first, last, row, event))
    return placements


class ConnectionError(Exception):
    pass


class TimeTree:
    def __init__(self):
        self.process = None
        self.messages = queue.Queue()
        self.serial = 0

    def start(self, credentials=None):
        try:
            app_root=Path(__file__).resolve().parent
            own=cache_store.read_file(cache_store.data_root()/"timetree-auth.bin")
            if credentials is None and own:
                if not own[0].get("connected"):
                    raise ConnectionError("설정 → 계정 연결에서 TimeTree에 로그인해 주세요.")
                credentials=own[0]
            bundled_node=app_root/"runtime/node/node.exe"
            bundled_server=app_root/"runtime/timetree/timetree.cjs"
            if not credentials:
                raise ConnectionError("설정 → 계정 연결에서 TimeTree에 로그인해 주세요.")
            if (app_root/"standalone.json").exists() or (bundled_node.is_file() and bundled_server.is_file()):
                if not credentials:
                    raise ConnectionError("설정 → 계정 연결에서 TimeTree에 로그인해 주세요.")
                if not bundled_node.is_file() or not bundled_server.is_file():
                    raise ConnectionError("TimeTree 실행 파일을 찾지 못했습니다. 프로그램을 다시 설치해 주세요.")
                server={"command":str(bundled_node),"args":[str(bundled_server)]}
            else:
                development_server=app_root/"vendor/timetree-mcp/dist/index.js"
                if not development_server.is_file():
                    raise ConnectionError("TimeTree MCP를 먼저 빌드하거나 설치파일을 사용해 주세요.")
                server={"command":"node","args":[str(development_server)]}
            env = os.environ | server.get("env", {}) | {"TIMETREE_UPDATE_CHECK": "false"}
            if credentials is not None:
                if not isinstance(credentials.get("email"),str) or not isinstance(credentials.get("password"),str) or not credentials["email"].strip() or not credentials["password"]:
                    raise ConnectionError("TimeTree 이메일과 비밀번호를 입력해 주세요.")
                env|={"TIMETREE_EMAIL":credentials["email"].strip(),"TIMETREE_PASSWORD":credentials["password"]}
            self.messages = queue.Queue()
            self.process = subprocess.Popen([server["command"], *server.get("args", [])],
                stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL,
                text=True, encoding="utf-8", env=env, creationflags=subprocess.CREATE_NO_WINDOW)
        except (OSError, ValueError, KeyError, StopIteration):
            raise ConnectionError("TimeTree 연결 설정을 확인해 주세요. 설정의 계정 연결에서 다시 로그인하실 수 있습니다.") from None
        output, messages = self.process.stdout, self.messages

        def read():
            try:
                for line in output:
                    try:
                        messages.put(json.loads(line))
                    except ValueError:
                        continue
            finally:
                messages.put(None)

        threading.Thread(target=read, daemon=True).start()
        self.rpc("initialize", {"protocolVersion": "2024-11-05", "capabilities": {},
                 "clientInfo": {"name": "timetree-desktop-widget", "version": "1.0"}})
        self.send({"jsonrpc": "2.0", "method": "notifications/initialized"})

    def send(self, value):
        try:
            self.process.stdin.write(json.dumps(value) + "\n")
            self.process.stdin.flush()
        except (OSError, ValueError, AttributeError):
            raise ConnectionError("TimeTree 연결이 끊겼습니다. 새로고침으로 다시 연결해 주세요.") from None

    def rpc(self, method, params):
        self.serial += 1
        request_id = self.serial
        self.send({"jsonrpc": "2.0", "id": request_id, "method": method, "params": params})
        deadline = time.monotonic() + 90
        while True:
            try:
                response = self.messages.get(timeout=max(.01, deadline - time.monotonic()))
            except queue.Empty:
                raise ConnectionError("TimeTree 응답 시간이 초과되었습니다. 잠시 후 새로고침해 주세요.") from None
            if response is None:
                raise ConnectionError("TimeTree 연결이 종료되었습니다. 연결 설정과 네트워크를 확인해 주세요.")
            if response.get("id") != request_id:
                continue
            if "error" in response:
                raise ConnectionError("TimeTree 요청을 처리하지 못했습니다. 잠시 후 다시 시도해 주세요.")
            return response["result"]

    def call(self, name, **arguments):
        result = self.rpc("tools/call", {"name": name, "arguments": arguments})
        if result.get("isError"):
            raise ConnectionError("TimeTree에서 일정을 읽지 못했습니다. 로그인 설정과 네트워크를 확인해 주세요.")
        if "structuredContent" in result:
            return result["structuredContent"]
        try:
            return json.loads(next(x["text"] for x in result["content"] if x["type"] == "text"))
        except (ValueError, KeyError, StopIteration):
            raise ConnectionError("TimeTree 응답 형식을 읽지 못했습니다. 로컬 MCP 업데이트가 필요할 수 있습니다.") from None

    def close(self):
        process, self.process = self.process, None
        if process:
            try:
                process.terminate()
                process.wait(timeout=3)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait(timeout=3)
            finally:
                process.stdin.close()
                process.stdout.close()


