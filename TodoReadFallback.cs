using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using D=System.Collections.Generic.Dictionary<string,object>;

sealed class TodoReadFallback {
    readonly string folder,account;
    readonly JavaScriptSerializer json=new JavaScriptSerializer();
    internal Func<DateTime> Now=()=>DateTime.UtcNow;
    internal Func<Task<List<Goal>>> McpGoals;
    internal Func<DateTime,DateTime,Task<WeekData>> McpRange;
    internal string LastProvider="firebase";
    string StatePath { get { return Path.Combine(folder,"read-route."+account+".bin"); } }
    string IdentityPath { get { return Path.Combine(folder,"mcp-identity."+account+".bin"); } }
    internal TodoReadFallback(string folder,string account) {
        this.folder=folder; this.account=account;
        McpGoals=async delegate {
            using(var client=LegacyClient()) return (await client.LoadGoals()).Select(g=>new Goal { Id=g.Id,Title=g.Title }).ToList();
        };
        McpRange=async delegate(DateTime start,DateTime end) {
            using(var client=LegacyClient()) {
                var result=await client.LoadRange(start,end);
                return new WeekData { Goals=result.Goals.Select(g=>new Goal { Id=g.Id,Title=g.Title }).ToList(),
                    Todos=result.Todos.Select(t=>new Todo { Id=t.Id,Content=t.Content,Category=t.Category,GoalId=t.GoalId,Date=t.Date,Done=t.Done,Reminder=t.Reminder }).ToList() };
            }
        };
    }
    static LegacyMcp.MateClient LegacyClient() {
        var client=new LegacyMcp.MateClient(AppPaths.McpFolder);
        if(!client.Connected) { client.Dispose(); throw new InvalidOperationException("MCP 보조 계정 연결이 없습니다. 기존 Todo Mate 위젯에서 PlayMCP 연결을 확인해 주세요."); }
        return client;
    }
    D State() {
        var saved=SnapshotStore.ReadFile(StatePath);
        try { return saved==null ? new D() : json.Deserialize<D>(saved.Payload); } catch { return new D(); }
    }
    static DateTime Until(D state,string key) {
        DateTime value;
        return DateTime.TryParse(MateClient.Str(state,key),CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out value) ? value.ToUniversalTime() : DateTime.MinValue;
    }
    void Save(D state) { SnapshotStore.WriteFile(StatePath,json.Serialize(state)); }
    static DateTime KstMidnight(DateTime utc) { return new DateTimeOffset(utc).ToOffset(TimeSpan.FromHours(9)).Date.AddDays(1).AddHours(-9); }
    internal static DateTime NextFirebaseCheck(DateTime utc) {
        DateTime kst=DateTime.SpecifyKind(KstMidnight(utc),DateTimeKind.Utc);
        var pacific=TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");
        DateTime reset=TimeZoneInfo.ConvertTimeToUtc(TimeZoneInfo.ConvertTimeFromUtc(utc,pacific).Date.AddDays(1),pacific);
        // Try the primary again on the Korean day boundary and the documented Firestore daily reset.
        return reset<kst ? reset : kst;
    }
    internal void RememberGoals(List<Goal> goals) {
        if(goals.Select(g=>g.Id).Distinct().Count()!=goals.Count || goals.Any(g=>string.IsNullOrWhiteSpace(g.Id))) throw new InvalidOperationException("Firebase 카테고리 ID를 확인하지 못했습니다.");
        SnapshotStore.WriteFile(IdentityPath,json.Serialize(new { ids=goals.Select(g=>g.Id).OrderBy(id=>id).ToArray() }));
    }
    void CheckGoals(List<Goal> goals) {
        var saved=SnapshotStore.ReadFile(IdentityPath); HashSet<string> verified;
        try { verified=saved==null ? null : new HashSet<string>(((IEnumerable)json.Deserialize<D>(saved.Payload)["ids"]).Cast<object>().Select(Convert.ToString)); }
        catch { verified=null; }
        if(verified==null || verified.Count==0 || goals.Any(g=>string.IsNullOrWhiteSpace(g.Id)) || goals.Select(g=>g.Id).Distinct().Count()!=goals.Count || !verified.SetEquals(goals.Select(g=>g.Id)))
            throw new InvalidOperationException("Firebase와 MCP의 본인 카테고리가 일치하는지 확인하지 못했습니다. 계정 또는 카테고리가 변경됐다면 Firebase 조회 복구 후 다시 확인해 주세요. 기존 캐시는 유지됩니다.");
    }
    internal object Status() {
        var state=State();
        return new { enabled=true,primary="firebase",read_provider=Until(state,"firebase_retry_at")>Now() ? "mcp" : "firebase",firebase_retry_at=MateClient.Str(state,"firebase_retry_at"),mcp_retry_at=MateClient.Str(state,"mcp_retry_at"),mcp_connection_saved=File.Exists(Path.Combine(AppPaths.McpFolder,"playmcp-token.bin")) };
    }
    async Task<T> Read<T>(Func<Task<T>> primary,Func<Task<T>> backup) {
        var state=State(); DateTime now=Now();
        if(Until(state,"firebase_retry_at")<=now) {
            try {
                T result=await primary(); LastProvider="firebase";
                if(state.ContainsKey("firebase_retry_at")) { state.Remove("firebase_retry_at"); Save(state); }
                return result;
            } catch(FirebaseError error) {
                if(!error.Firestore || error.Status!=429 || error.Code!="RESOURCE_EXHAUSTED") throw;
                state["firebase_retry_at"]=NextFirebaseCheck(now).ToString("o"); Save(state);
            }
        }
        if(Until(state,"mcp_retry_at")>now) throw new InvalidOperationException("Firebase 조회 제한으로 MCP를 사용 중이며, MCP의 오늘 조회 한도도 소진됐습니다. 저장된 캐시는 계속 사용할 수 있습니다.");
        try { T result=await backup(); LastProvider="mcp"; return result; }
        catch(Exception error) {
            if(error.Message.Contains("오늘 할 일 조회 한도")) { state["mcp_retry_at"]=DateTime.SpecifyKind(KstMidnight(now),DateTimeKind.Utc).ToString("o"); Save(state); }
            throw new InvalidOperationException("Firebase 조회 한도/용량 제한으로 MCP 보조 조회를 시도했지만 실패했습니다. "+error.Message+" 기존 캐시는 유지됩니다.");
        }
    }
    internal Task<List<Goal>> Goals(Func<Task<List<Goal>>> primary) {
        return Read(primary,async delegate { var goals=await McpGoals(); CheckGoals(goals); return goals; });
    }
    internal async Task<WeekData> Range(DateTime start,DateTime end,Func<Task<WeekData>> primary) {
        var result=await Read(primary,async delegate {
            var data=await McpRange(start,end); CheckGoals(data.Goals);
            if(data.Todos.Select(t=>t.Id).Distinct().Count()!=data.Todos.Count || data.Todos.Any(t=>string.IsNullOrWhiteSpace(t.Id) || t.Date<start || t.Date>end || !data.Goals.Any(g=>g.Id==t.GoalId)))
                throw new InvalidOperationException("MCP 할 일의 ID·날짜·카테고리 검증에 실패했습니다.");
            return data;
        });
        result.Source=LastProvider; result.Notice=LastProvider=="mcp" ? "Firebase 조회 제한 · MCP 보조 조회" : ""; return result;
    }
}
