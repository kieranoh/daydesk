"""Atomic, Windows-user encrypted snapshots shared with the native widget."""
import ctypes
from ctypes import wintypes
import hashlib
import json
import os
from pathlib import Path
import tempfile
from contextlib import contextmanager
from datetime import datetime, timezone


class Blob(ctypes.Structure):
    _fields_ = [("size", wintypes.DWORD), ("data", ctypes.POINTER(ctypes.c_ubyte))]


@contextmanager
def service_lock(area):
    kernel = ctypes.WinDLL("kernel32", use_last_error=True)
    kernel.CreateSemaphoreW.argtypes = [ctypes.c_void_p, wintypes.LONG, wintypes.LONG, wintypes.LPCWSTR]
    kernel.CreateSemaphoreW.restype = wintypes.HANDLE
    kernel.WaitForSingleObject.argtypes = [wintypes.HANDLE, wintypes.DWORD]
    kernel.ReleaseSemaphore.argtypes = [wintypes.HANDLE, wintypes.LONG, ctypes.c_void_p]
    kernel.CloseHandle.argtypes = [wintypes.HANDLE]
    handle = kernel.CreateSemaphoreW(None, 1, 1, "Local\\DaydeskCache."+area)
    if not handle:
        raise ctypes.WinError(ctypes.get_last_error())
    acquired = False
    try:
        acquired = kernel.WaitForSingleObject(handle, 150000) == 0
        if not acquired:
            raise TimeoutError("다른 동기화가 진행 중입니다. 잠시 후 다시 시도해 주세요.")
        yield
    finally:
        if acquired:
            kernel.ReleaseSemaphore(handle, 1, None)
        kernel.CloseHandle(handle)


def crypt(data, protect):
    buffer = ctypes.create_string_buffer(data)
    source = Blob(len(data), ctypes.cast(buffer, ctypes.POINTER(ctypes.c_ubyte)))
    target = Blob()
    api = ctypes.WinDLL("crypt32", use_last_error=True)
    if protect:
        ok = api.CryptProtectData(ctypes.byref(source), None, None, None, None, 1, ctypes.byref(target))
    else:
        ok = api.CryptUnprotectData(ctypes.byref(source), None, None, None, None, 1, ctypes.byref(target))
    if not ok:
        raise ctypes.WinError(ctypes.get_last_error())
    try:
        return ctypes.string_at(target.data, target.size)
    finally:
        kernel = ctypes.WinDLL("kernel32", use_last_error=True)
        kernel.LocalFree.argtypes = [ctypes.c_void_p]
        kernel.LocalFree(ctypes.cast(target.data, ctypes.c_void_p))


def data_root():
    return Path(os.environ.get("DAYDESK_DATA_DIR",str(Path(os.environ["LOCALAPPDATA"])/"TimeTreeWidget")))


def root():
    return Path(os.environ.get("DAYDESK_CACHE_DIR",str(data_root()/"cache")))


def path(area, key):
    if area not in ("timetree", "todo"):
        raise ValueError("Unknown cache area")
    if area=="timetree":
        saved=read_file(data_root()/"timetree-auth.bin")
        if saved and saved[0].get("account_key"):
            key=saved[0]["account_key"]+"|"+key
        elif (Path(__file__).resolve().parent/"standalone.json").exists():
            key="disconnected|"+key
    return root() / area / (hashlib.sha256(key.encode("utf-8")).hexdigest() + ".bin")


def read(area, key):
    return read_file(path(area, key))


def read_file(target):
    try:
        record = json.loads(crypt(Path(target).read_bytes(), False).decode("utf-8-sig"))
        data = json.loads(record["payload"])
        updated = datetime.fromisoformat(record["updated_at"].replace("Z", "+00:00"))
        if not isinstance(data, dict) or updated.tzinfo is None:
            return None
        return data, updated
    except (OSError, ValueError, KeyError, TypeError):
        return None


def write(area, key, data, updated=None):
    write_file(path(area, key), data, updated)


def write_file(target, data, updated=None):
    target = Path(target)
    target.parent.mkdir(parents=True, exist_ok=True)
    stamp = (updated or datetime.now(timezone.utc)).isoformat()
    payload = json.dumps(data, ensure_ascii=False, separators=(",", ":"))
    record = json.dumps({"payload": payload, "updated_at": stamp}, ensure_ascii=False).encode("utf-8")
    encrypted = crypt(record, True)
    name = None
    try:
        with tempfile.NamedTemporaryFile(dir=target.parent, delete=False) as file:
            name = file.name
            file.write(encrypted)
            file.flush()
            os.fsync(file.fileno())
        os.replace(name, target)
    finally:
        if name and os.path.exists(name):
            os.unlink(name)
