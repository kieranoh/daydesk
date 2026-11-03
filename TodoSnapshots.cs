using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using D=System.Collections.Generic.Dictionary<string,object>;

sealed class SavedTodos {
    internal WeekData Data;
    internal DateTime Start,End,Updated;
}
static class TodoSnapshots {
    sealed class Lease : IDisposable {
        internal Semaphore Gate;
        public void Dispose() { Gate.Release(); Gate.Dispose(); }
    }
    internal static async Task<IDisposable> Enter() {
        return await EnterGate("Local\\DaydeskCache.todo");
    }
    internal static async Task<IDisposable> EnterGate(string name) {
        var gate=new Semaphore(1,1,name);
        if(!await Task.Run(()=>gate.WaitOne(150000))) { gate.Dispose(); throw new TimeoutException("다른 동기화가 진행 중입니다. 잠시 후 다시 시도해 주세요."); }
        return new Lease { Gate=gate };
    }
    static JavaScriptSerializer Json { get { return new JavaScriptSerializer { MaxJsonLength=16000000 }; } }
    internal static string AccountKey="disconnected";
    static string Key(DateTime day) { return AccountKey+"|"+day.ToString("yyyy-MM"); }
    static DateTime Day(object value) { return DateTime.ParseExact(Convert.ToString(value),"yyyy-MM-dd",CultureInfo.InvariantCulture); }
    static IEnumerable<D> Rows(D value,string key) { return ((IEnumerable)value[key]).Cast<object>().Cast<D>(); }
    internal static string Payload(WeekData data,DateTime start,DateTime end) {
        return Json.Serialize(new {
            account_key=AccountKey,
            source=data.Source ?? "firebase",notice=data.Notice ?? "",
            start=start.ToString("yyyy-MM-dd"),end=end.ToString("yyyy-MM-dd"),
            todos=data.Todos.Select(t=>new { id=t.Id,content=t.Content ?? "",goal_id=t.GoalId,date=t.Date.ToString("yyyy-MM-dd"),done=t.Done,reminder=t.Reminder ?? "" }),
            goals=data.Goals.Select(g=>new { id=g.Id,title=g.Title ?? "" })
        });
    }
    static SavedTodos Decode(Snapshot saved) {
        if(saved==null) return null;
        try {
            var value=Json.Deserialize<D>(saved.Payload);
            if(MateClient.Str(value,"account_key")!=AccountKey) return null;
            var goals=Rows(value,"goals").Select(g=>new Goal { Id=Convert.ToString(g["id"]),Title=Convert.ToString(g["title"]) }).ToList();
            var todos=Rows(value,"todos").Select(t=>new Todo { Id=Convert.ToString(t["id"]),Content=Convert.ToString(t["content"]),GoalId=Convert.ToString(t["goal_id"]),Date=Day(t["date"]),Done=(bool)t["done"],Reminder=Convert.ToString(t["reminder"]) }).ToList();
            if(todos.Select(t=>t.Id).Distinct().Count()!=todos.Count || goals.Select(g=>g.Id).Distinct().Count()!=goals.Count) return null;
            foreach(var todo in todos) { var goal=goals.FirstOrDefault(g=>g.Id==todo.GoalId); todo.Category=goal==null ? "할 일" : goal.Title; }
            return new SavedTodos { Data=new WeekData { Todos=todos,Goals=goals,Source=MateClient.Str(value,"source")=="mcp" ? "mcp" : "firebase",Notice=MateClient.Str(value,"notice") },Start=Day(value["start"]),End=Day(value["end"]),Updated=saved.Updated };
        } catch { return null; }
    }
    internal static SavedTodos Read(DateTime day) {
        return new[]{0,-1,1}.Select(delta=>Decode(SnapshotStore.Read("todo",Key(day.AddMonths(delta)))))
            .Where(s=>s!=null && day.Date>=s.Start && day.Date<=s.End).OrderByDescending(s=>s.Updated).FirstOrDefault();
    }
    static IEnumerable<string> Files() { string folder=Path.Combine(SnapshotStore.Root,"todo"); return Directory.Exists(folder) ? Directory.GetFiles(folder,"*.bin") : new string[0]; }
    internal static void MergeRange(WeekData fresh,DateTime start,DateTime end) {
        foreach(string file in Files()) {
            var saved=Decode(SnapshotStore.ReadFile(file)); if(saved==null) continue;
            var items=saved.Data.Todos.Where(t=>t.Date<start || t.Date>end).ToList();
            items.AddRange(fresh.Todos.Where(t=>t.Date>=saved.Start && t.Date<=saved.End));
            var merged=new WeekData { Todos=items.GroupBy(t=>t.Id).Select(g=>g.Last()).ToList(),Goals=fresh.Goals,Source=saved.Data.Source,Notice=saved.Data.Notice };
            string payload=Payload(merged,saved.Start,saved.End);
            if(payload!=Payload(saved.Data,saved.Start,saved.End)) SnapshotStore.WriteFile(file,payload,saved.Updated);
        }
    }
    internal static void SaveMonth(WeekData fresh,DateTime selected,DateTime start,DateTime end) {
        MergeRange(fresh,start,end);
        SnapshotStore.Write("todo",Key(selected),Payload(fresh,start,end));
        SaveGoals(fresh.Goals);
    }
    internal static void SaveGoals(List<Goal> goals) { SnapshotStore.Write("todo",AccountKey+"|goals",Json.Serialize(new { goals=goals.Select(g=>new { id=g.Id,title=g.Title }) })); }
    internal static List<Goal> ReadGoals() {
        var saved=SnapshotStore.Read("todo",AccountKey+"|goals"); if(saved==null) return null;
        try { return Rows(Json.Deserialize<D>(saved.Payload),"goals").Select(g=>new Goal { Id=Convert.ToString(g["id"]),Title=Convert.ToString(g["title"]) }).ToList(); } catch { return null; }
    }
    internal static void SaveDay(WeekData fresh,DateTime day) {
        MergeRange(fresh,day.Date,day.Date);
        if(Read(day)==null) SnapshotStore.Write("todo",Key(day),Payload(fresh,day.Date,day.Date));
    }
    internal static void Patch(Todo updated) {
        foreach(string file in Files()) {
            var saved=Decode(SnapshotStore.ReadFile(file)); if(saved==null) continue;
            string before=Payload(saved.Data,saved.Start,saved.End);
            int index=saved.Data.Todos.FindIndex(t=>t.Id==updated.Id);
            bool existed=saved.Data.Todos.RemoveAll(t=>t.Id==updated.Id)>0;
            bool belongs=updated.Date>=saved.Start && updated.Date<=saved.End;
            if(belongs) {
                if(index>=0) saved.Data.Todos.Insert(Math.Min(index,saved.Data.Todos.Count),updated); else saved.Data.Todos.Add(updated);
            }
            string after=Payload(saved.Data,saved.Start,saved.End);
            if((existed || belongs) && before!=after) SnapshotStore.WriteFile(file,after,saved.Updated);
        }
    }
    internal static void Remove(string id) {
        foreach(string file in Files()) {
            var saved=Decode(SnapshotStore.ReadFile(file)); if(saved!=null && saved.Data.Todos.RemoveAll(t=>t.Id==id)>0) SnapshotStore.WriteFile(file,Payload(saved.Data,saved.Start,saved.End),saved.Updated);
        }
    }
}
