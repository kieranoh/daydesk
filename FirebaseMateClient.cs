using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using D=System.Collections.Generic.Dictionary<string,object>;

sealed class Todo { public string Id,Content,Category,GoalId,Reminder; public DateTime Date; public bool Done; }
sealed class Goal { public string Id,Title; }
sealed class WeekData { public List<Todo> Todos; public List<Goal> Goals; public string Source,Notice; }
sealed class FirebaseError : Exception {
    internal int Status; internal string Code; internal bool Firestore;
    internal FirebaseError(int status,string code,bool firestore=true) : base("Todo Mate 연결 오류 ("+status+", "+code+"). 로그인 상태와 네트워크를 확인해 주세요.") { Status=status; Code=code; Firestore=firestore; }
}
sealed class MateClient : IDisposable {
    internal const string Database="https://firestore.googleapis.com/v1/projects/mate-914f3/databases/(default)/documents";
    internal static readonly string Folder=Path.Combine(AppPaths.DataRoot,"firebase");
    readonly HttpClient http;
    readonly string authPath,apiKey;
    readonly JavaScriptSerializer json=new JavaScriptSerializer { MaxJsonLength=16000000 };
    D session;
    string boundUid;
    bool needsLogin;
    readonly string folder;
    TodoReadFallback fallback;
    internal TodoReadFallback Reads { get { return fallback ?? (fallback=new TodoReadFallback(folder,AccountKey)); } }
    internal object ReadStatus { get { return Reads.Status(); } }
    public bool Connected { get { ReadSession(); return session!=null && !needsLogin; } }
    internal string AccountKey { get { ReadSession(); return session==null ? "disconnected" : Hash(Str(session,"uid")); } }
    internal static string Hash(string value) { using(var sha=SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-","").ToLowerInvariant(); }
    internal static string Str(D value,string key) { object v; return value.TryGetValue(key,out v) ? Convert.ToString(v,CultureInfo.InvariantCulture) : ""; }
    public MateClient(string folder,HttpMessageHandler handler=null,string key=null) {
        this.folder=folder;
        ServicePointManager.SecurityProtocol|=SecurityProtocolType.Tls12;
        authPath=Path.Combine(folder,"firebase-auth.bin");
        apiKey=key ?? Str(json.Deserialize<D>(File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"firebase-public.json"))),"api_key");
        if(string.IsNullOrWhiteSpace(apiKey)) throw new InvalidOperationException("Firebase 공개 설정을 찾지 못했습니다.");
        http=new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect=false }) { Timeout=TimeSpan.FromSeconds(30) };
        ReadSession();
    }
    void ReadSession() {
        var saved=SnapshotStore.ReadFile(authPath);
        try { session=saved==null ? null : json.Deserialize<D>(saved.Payload); if(session!=null && (Str(session,"uid")=="" || Str(session,"refresh_token")=="")) session=null; }
        catch { session=null; }
        if(session!=null) {
            if(boundUid!=null && boundUid!=Str(session,"uid")) { session=null; return; }
            boundUid=Str(session,"uid");
        }
    }
    async Task<IDisposable> AuthGate() {
        return await TodoSnapshots.EnterGate("Local\\DaydeskFirebaseAuth."+Hash(authPath));
    }
    void SaveSession(D result,bool refresh,string expectedUid) {
        string uid=Str(result,refresh ? "user_id" : "localId");
        if(uid=="" || (expectedUid!=null && uid!=expectedUid)) throw new InvalidOperationException("로그인 계정이 변경되어 연결을 중단했습니다. 다시 로그인해 주세요.");
        session=new D { {"uid",uid},{"id_token",Str(result,refresh ? "id_token" : "idToken")},{"refresh_token",Str(result,refresh ? "refresh_token" : "refreshToken")},
            {"expires",DateTime.UtcNow.AddSeconds(double.Parse(Str(result,refresh ? "expires_in" : "expiresIn"),CultureInfo.InvariantCulture)-90).ToString("o")} };
        if(Str(session,"id_token")=="" || Str(session,"refresh_token")=="") throw new InvalidOperationException("로그인 응답이 올바르지 않습니다.");
        SnapshotStore.WriteFile(authPath,json.Serialize(session));
        boundUid=uid; needsLogin=false;
        if(!refresh) fallback=null;
    }
    public async Task Login(string email,string password) {
        RequireText(email,"이메일"); RequireText(password,"비밀번호");
        using(await AuthGate()) {
            var result=(D)await Send("POST","https://identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key="+apiKey,
                new StringContent(json.Serialize(new { email=email.Trim(),password=password,returnSecureToken=true }),Encoding.UTF8,"application/json"),null);
            SaveSession(result,false,null);
        }
    }
    async Task<string> Token(bool force=false) {
        using(await AuthGate()) {
            ReadSession(); if(session==null) throw new InvalidOperationException("Firebase 계정 연결이 필요합니다. daydesk todo login을 실행해 주세요.");
            DateTime expires;
            if(!force && DateTime.TryParse(Str(session,"expires"),CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out expires) && expires.ToUniversalTime()>DateTime.UtcNow) return Str(session,"id_token");
            string uid=Str(session,"uid");
            D result;
            try { result=(D)await Send("POST","https://securetoken.googleapis.com/v1/token?key="+apiKey,
                new FormUrlEncodedContent(new D { {"grant_type","refresh_token"},{"refresh_token",Str(session,"refresh_token")} }.Select(p=>new KeyValuePair<string,string>(p.Key,(string)p.Value))),null); }
            catch(FirebaseError error) { if(error.Status==400 || error.Status==401) needsLogin=true; throw; }
            SaveSession(result,true,uid); return Str(session,"id_token");
        }
    }
    async Task<object> Send(string method,string url,HttpContent content,string token) {
        using(var request=new HttpRequestMessage(new HttpMethod(method),url)) {
            request.Content=content;
            if(token!=null) request.Headers.Authorization=new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",token);
            using(var response=await http.SendAsync(request)) {
                string body=await response.Content.ReadAsStringAsync(); object decoded=null;
                if(body.Length>0) { try { decoded=json.DeserializeObject(body); } catch { if(response.IsSuccessStatusCode) throw new InvalidOperationException("서버 응답을 읽지 못했습니다. 새로고침으로 반영 여부를 확인해 주세요."); } }
                if(!response.IsSuccessStatusCode) {
                    string code="REQUEST_FAILED"; var root=decoded as D; object e;
                    if(root!=null && root.TryGetValue("error",out e) && e is D) { code=Str((D)e,"status"); if(code=="") code=Str((D)e,"message").Split(' ')[0]; }
                    throw new FirebaseError((int)response.StatusCode,code,url.StartsWith(Database,StringComparison.Ordinal));
                }
                return decoded;
            }
        }
    }
    async Task<object> Request(string method,string url,object body=null) {
        string token=await Token();
        try { return await Send(method,url,body==null ? null : new StringContent(json.Serialize(body),Encoding.UTF8,"application/json"),token); }
        catch(FirebaseError error) {
            if(error.Status!=401) throw;
        }
        token=await Token(true);
        return await Send(method,url,body==null ? null : new StringContent(json.Serialize(body),Encoding.UTF8,"application/json"),token);
    }
    static object Decode(object value) {
        var d=(D)value; object v;
        foreach(string key in new[]{"stringValue","integerValue","doubleValue","booleanValue","timestampValue"}) if(d.TryGetValue(key,out v)) return v;
        if(d.ContainsKey("nullValue")) return null;
        return null;
    }
    internal static D Fields(D doc) { return ((D)doc["fields"]).ToDictionary(p=>p.Key,p=>Decode(p.Value)); }
    static object Encode(object value) {
        if(value==null) return new D { {"nullValue",null} };
        if(value is bool) return new D { {"booleanValue",value} };
        if(value is long || value is int) return new D { {"integerValue",Convert.ToString(value,CultureInfo.InvariantCulture)} };
        return new D { {"stringValue",Convert.ToString(value,CultureInfo.InvariantCulture)} };
    }
    static object Body(D fields) { return new { fields=fields.ToDictionary(p=>p.Key,p=>Encode(p.Value)) }; }
    internal static long Stamp(DateTime day) { return (long)(DateTime.SpecifyKind(day.Date,DateTimeKind.Utc)-new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc)).TotalMilliseconds; }
    static long Now { get { return (long)(DateTime.UtcNow-new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc)).TotalMilliseconds; } }
    internal static string Id(string id) { RequireText(id,"ID"); if(id=="." || id==".." || id.Contains("/") || id.Any(char.IsControl) || Encoding.UTF8.GetByteCount(id)>1500) throw new ArgumentException("올바른 ID를 지정해 주세요."); return Uri.EscapeDataString(id); }
    void Owned(D doc,string collection,string owner) {
        string prefix=Database+"/"+collection+"/",name=Str(doc,"name");
        // REST document names omit the service URL.
        string resource=prefix.Substring("https://firestore.googleapis.com/v1/".Length);
        if(!name.StartsWith(resource,StringComparison.Ordinal) || name.Substring(resource.Length).Contains("/") || Str(Fields(doc),owner)!=Str(session,"uid")) throw new InvalidOperationException("본인 계정의 항목이 아니어서 요청을 중단했습니다.");
    }
    static object Filter(string field,string op,object value) { return new { fieldFilter=new { field=new { fieldPath=field },op=op,value=Encode(value) } }; }
    async Task<List<D>> Query(string collection,string owner,params object[] extra) {
        await Token();
        var filters=new List<object> { Filter(owner,"EQUAL",Str(session,"uid")) }; filters.AddRange(extra);
        var result=(IEnumerable)await Request("POST",Database+":runQuery",new { structuredQuery=new { from=new[]{new { collectionId=collection }},where=new { compositeFilter=new { op="AND",filters=filters } },limit=10000 } });
        var docs=new List<D>();
        foreach(D row in result) { object doc; if(row.TryGetValue("document",out doc)) { Owned((D)doc,collection,owner); docs.Add((D)doc); } }
        if(docs.Count>=10000) throw new InvalidOperationException("조회 결과가 너무 많습니다. 더 짧은 기간으로 조회해 주세요.");
        return docs;
    }
    internal static Todo ToTodo(D doc) {
        D f=Fields(doc); string name=Str(doc,"name"); object done;
        string reminder=Str(f,"remindAt"); long ms;
        if(long.TryParse(reminder,out ms)) reminder=new DateTimeOffset(1970,1,1,0,0,0,TimeSpan.Zero).AddMilliseconds(ms).ToOffset(TimeSpan.FromHours(9)).ToString("yyyy-MM-ddTHH:mm");
        return new Todo { Id=name.Substring(name.LastIndexOf('/')+1),Content=Str(f,"content"),GoalId=Str(f,"goalID"),Date=new DateTime(1970,1,1).AddMilliseconds(long.Parse(Str(f,"date"),CultureInfo.InvariantCulture)).Date,Done=f.TryGetValue("isDone",out done) && done is bool && (bool)done,Reminder=reminder };
    }
    public async Task<List<Goal>> LoadGoals() {
        if(!Connected) throw new InvalidOperationException("Firebase 계정 연결이 변경되었거나 만료됐습니다. 계정 연결을 확인해 주세요.");
        return await Reads.Goals(LoadGoalsFirebase);
    }
    async Task<List<Goal>> LoadGoalsFirebase() {
        var docs=await Query("Goal","userID");
        var goals=docs.OrderBy(d=> { double n; return double.TryParse(Str(Fields(d),"priority"),NumberStyles.Any,CultureInfo.InvariantCulture,out n) ? n : 0; })
            .Where(d=>Str(Fields(d),"isDeleted")!="True").Select(d=>new Goal { Id=Str(d,"name").Split('/').Last(),Title=Str(Fields(d),"title") }).ToList();
        Reads.RememberGoals(goals); return goals;
    }
    public async Task<WeekData> LoadRange(DateTime start,DateTime end) {
        start=start.Date; end=end.Date;
        if(end<start || (end-start).TotalDays>62) throw new ArgumentException("조회 기간은 1~63일입니다.");
        if(!Connected) throw new InvalidOperationException("Firebase 계정 연결이 변경되었거나 만료됐습니다. 계정 연결을 확인해 주세요.");
        return await Reads.Range(start,end,()=>LoadRangeFirebase(start,end));
    }
    async Task<WeekData> LoadRangeFirebase(DateTime start,DateTime end) {
        var goals=await LoadGoalsFirebase(); List<D> docs=null;
        try { docs=await Query("TodoItem","writerID",Filter("date","GREATER_THAN_OR_EQUAL",Stamp(start)),Filter("date","LESS_THAN_OR_EQUAL",Stamp(end))); }
        catch(FirebaseError error) {
            if(error.Code!="FAILED_PRECONDITION") throw;
        }
        if(docs==null) { docs=new List<D>(); for(var day=start;day<=end;day=day.AddDays(1)) docs.AddRange(await Query("TodoItem","writerID",Filter("date","EQUAL",Stamp(day)))); }
        var todos=docs.Select(ToTodo).ToList();
        foreach(var t in todos) { var g=goals.FirstOrDefault(x=>x.Id==t.GoalId); t.Category=g==null ? "할 일" : g.Title; }
        return new WeekData { Todos=todos,Goals=goals };
    }
    public async Task<Todo> Get(string id) { Id(id); var doc=(D)await Request("GET",Database+"/TodoItem/"+Id(id)); Owned(doc,"TodoItem","writerID"); return ToTodo(doc); }
    async Task<Goal> ResolveGoal(string value) {
        RequireText(value,"카테고리"); var goals=await LoadGoalsFirebase(); var matches=goals.Where(g=>g.Id==value).ToList();
        if(matches.Count==0) matches=goals.Where(g=>g.Title==value).ToList();
        if(matches.Count!=1) throw new ArgumentException("본인 카테고리를 ID로 지정해 주세요. 이름이 중복될 수 있습니다."); return matches[0];
    }
    public async Task<Todo> Add(string goal,string content,DateTime date,bool done=false,DateTime? reminder=null) {
        RequireText(content,"내용"); var category=await ResolveGoal(goal); string uid=Str(session,"uid"),id=uid+Guid.NewGuid().ToString("N");
        var fields=new D { {"id",id},{"writerID",uid},{"content",content.Trim()},{"date",Stamp(date)},{"goalID",category.Id},{"createTime",Now},{"isDone",false},{"doneTime",null},
            {"hasPhoto",false},{"hasTimer",false},{"isMemoPublic",false},{"likes",null},{"likesTotalCount",0},{"likesTotalSenderIDs",null},{"memo",null},{"photoURL",null},{"remindAt",null},{"routineID",null},{"spentTime",null},{"timer",null} };
        if(done) { fields["isDone"]=true; fields["doneTime"]=Now; }
        if(reminder.HasValue) fields["remindAt"]=ReminderStamp(reminder.Value);
        var doc=(D)await Request("POST",Database+"/TodoItem?documentId="+Id(id),Body(fields)); Owned(doc,"TodoItem","writerID"); var todo=ToTodo(doc); todo.Category=category.Title; return todo;
    }
    static long ReminderStamp(DateTime value) { return (long)(new DateTimeOffset(DateTime.SpecifyKind(value,DateTimeKind.Unspecified),TimeSpan.FromHours(9)).ToUniversalTime()-new DateTimeOffset(1970,1,1,0,0,0,TimeSpan.Zero)).TotalMilliseconds; }
    public async Task<Todo> Update(string id,string content=null,DateTime? date=null,string goal=null,bool? done=null,bool changeReminder=false,DateTime? reminder=null) {
        Id(id); if(content!=null) RequireText(content,"내용");
        Goal category=goal==null ? null : await ResolveGoal(goal);
        var fields=new D(); if(content!=null) fields["content"]=content.Trim(); if(date.HasValue) fields["date"]=Stamp(date.Value); if(category!=null) fields["goalID"]=category.Id;
        if(done.HasValue) { fields["isDone"]=done.Value; if(done.Value) { fields["doneTime"]=Now; fields["hasTimer"]=false; fields["timer"]=null; } }
        if(changeReminder) fields["remindAt"]=reminder.HasValue ? (object)ReminderStamp(reminder.Value) : null;
        if(fields.Count==0) throw new ArgumentException("변경할 내용을 지정해 주세요.");
        var old=(D)await Request("GET",Database+"/TodoItem/"+Id(id)); Owned(old,"TodoItem","writerID");
        string version=Str(old,"updateTime"); if(version=="") throw new InvalidOperationException("항목 버전을 확인하지 못했습니다.");
        string query="?"+string.Join("&",fields.Keys.Select(k=>"updateMask.fieldPaths="+k))+"&currentDocument.updateTime="+Uri.EscapeDataString(version);
        var doc=(D)await Request("PATCH",Database+"/TodoItem/"+Id(id)+query,Body(fields)); Owned(doc,"TodoItem","writerID"); var todo=ToTodo(doc); if(category!=null) todo.Category=category.Title; return todo;
    }
    public Task<Todo> SetDone(string id,bool done) { return Update(id,done:done); }
    public Task<Todo> SetReminder(string id,DateTime date) { return Update(id,changeReminder:true,reminder:date); }
    public async Task Delete(string id) {
        Id(id); var doc=(D)await Request("GET",Database+"/TodoItem/"+Id(id)); Owned(doc,"TodoItem","writerID");
        string version=Str(doc,"updateTime"); if(version=="") throw new InvalidOperationException("항목 버전을 확인하지 못했습니다.");
        await Request("DELETE",Database+"/TodoItem/"+Id(id)+"?currentDocument.updateTime="+Uri.EscapeDataString(version));
    }
    public static void RequireText(string value,string label) { if(string.IsNullOrWhiteSpace(value)) throw new ArgumentException(label+"을(를) 입력해 주세요."); }
    public static DateTime WeekStart(DateTime day) { return day.Date.AddDays(-(int)day.DayOfWeek); }
    public static DateTime PeriodStart(DateTime day,bool month) { return month ? WeekStart(new DateTime(day.Year,day.Month,1)) : WeekStart(day); }
    public static DateTime PeriodEnd(DateTime day,bool month) { return month ? WeekStart(new DateTime(day.Year,day.Month,1).AddMonths(1).AddDays(-1)).AddDays(6) : WeekStart(day).AddDays(6); }
    public void Dispose() { http.Dispose(); }
}
