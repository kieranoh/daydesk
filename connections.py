"""Read-only credential check. Credentials arrive through stdin and are never logged."""
import hashlib
import json
import sys
from calendar_data import TimeTree,ConnectionError

def check(value):
    if value.get("action")!="test-timetree":
        from timetree_write import run
        return run(value)
    email=value.get("email","").strip()
    password=value.get("password","")
    client=TimeTree()
    try:
        client.start({"email":email,"password":password})
        calendars=client.call("list_calendars").get("calendars",[])
        return {"connected":True,"account_key":hashlib.sha256(email.lower().encode()).hexdigest(),"calendars":[{"id":str(c["id"]),"name":str(c["name"])} for c in calendars]}
    finally:
        client.close()

if __name__=="__main__":
    try:
        value=json.loads(sys.stdin.read().lstrip("\ufeff"))
        print(json.dumps(check(value),ensure_ascii=False))
    except Exception as error:
        message=str(error) if isinstance(error,ConnectionError) else "TimeTree 연결을 확인하지 못했습니다. 이메일·비밀번호와 네트워크를 확인해 주세요."
        print(json.dumps({"error":message},ensure_ascii=False))
        sys.exit(1)
