using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using D=System.Collections.Generic.Dictionary<string,object>;

static class ClientSelfTest {
    static JavaScriptSerializer Json=new JavaScriptSerializer();
    static void Check(bool ok,string message) { if(!ok) throw new Exception("Firebase self-test: "+message); }
    sealed class Fake : HttpMessageHandler {
        internal readonly Queue<Func<HttpRequestMessage,string,object>> Calls=new Queue<Func<HttpRequestMessage,string,object>>();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellation) {
            Check(Calls.Count>0,"Unexpected request");
            string body=request.Content==null ? "" : await request.Content.ReadAsStringAsync();
            var value=Calls.Dequeue()(request,body);
            return value is HttpResponseMessage ? (HttpResponseMessage)value : new HttpResponseMessage(HttpStatusCode.OK) { Content=new StringContent(Json.Serialize(value),Encoding.UTF8,"application/json") };
        }
    }
    static object V(object value) { return value is bool ? new D { {"booleanValue",value} } : new D { {"stringValue",value} }; }
    static D Document(string owner="owner",string id="task") {
        return new D { {"name","projects/mate-914f3/databases/(default)/documents/TodoItem/"+id},{"updateTime","2026-11-03T01:02:03.123456Z"},
            {"fields",new D { {"writerID",V(owner)},{"content",V("샘플")},{"date",new D { {"integerValue","1793664000000"} }},{"goalID",V("goal")},{"isDone",V(false)},{"remindAt",new D { {"nullValue",null} }} }} };
    }
    static void Seed(string folder,bool expired=false) {
        SnapshotStore.WriteFile(Path.Combine(folder,"firebase-auth.bin"),Json.Serialize(new { uid="owner",id_token="id-value",refresh_token="refresh-value",expires=DateTime.UtcNow.AddHours(expired ? -1 : 1).ToString("o") }));
    }
    public static void Run() { Test().GetAwaiter().GetResult(); }
    static async Task Test() {
        string folder=Path.Combine(Path.GetTempPath(),"daydesk-firebase-test-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try {
            await PlayMcpInputTests(folder);
            Seed(folder); var fake=new Fake();
            fake.Calls.Enqueue((r,b)=> { Check(r.Method==HttpMethod.Get,"Update must read ownership first"); return Document(); });
            fake.Calls.Enqueue((r,b)=> {
                Check(r.Method.Method=="PATCH","Missing patch"); string url=r.RequestUri.ToString();
                Check(url.Contains("updateMask.fieldPaths=content") && url.Contains("updateMask.fieldPaths=date") && url.Contains("currentDocument.updateTime="),"Missing mask or precondition");
                Check(!b.Contains("writerID") && !b.Contains("memo"),"Update overwrites metadata");
                var doc=Document(); var fields=(D)doc["fields"]; fields["content"]=V("새 내용"); return doc;
            });
            using(var client=new MateClient(folder,fake,"test-key")) { var updated=await client.Update("task","새 내용",new DateTime(2026,11,4)); Check(updated.Content=="새 내용","Server response ignored"); }
            Check(fake.Calls.Count==0,"Update sequence incomplete");
            fake=new Fake(); fake.Calls.Enqueue((r,b)=>Document("someone-else"));
            using(var client=new MateClient(folder,fake,"test-key")) { bool denied=false; try { await client.Delete("task"); } catch(InvalidOperationException) { denied=true; } Check(denied,"Cross-account delete allowed"); }
            fake=new Fake(); fake.Calls.Enqueue((r,b)=>Document()); fake.Calls.Enqueue((r,b)=> { throw new TaskCanceledException(); });
            using(var client=new MateClient(folder,fake,"test-key")) { bool stopped=false; try { await client.SetDone("task",true); } catch(TaskCanceledException) { stopped=true; } Check(stopped && fake.Calls.Count==0,"Uncertain write retried"); }
            Seed(folder,true); fake=new Fake();
            fake.Calls.Enqueue((r,b)=> { Check(r.RequestUri.Host=="securetoken.googleapis.com" && r.Headers.Authorization==null && b.Contains("grant_type=refresh_token"),"Bad refresh request"); return new { user_id="owner",id_token="new-id",refresh_token="rotated-refresh",expires_in="3600" }; });
            fake.Calls.Enqueue((r,b)=> { Check(r.Headers.Authorization.Parameter=="new-id","Refreshed token unused"); return Document(); });
            using(var client=new MateClient(folder,fake,"test-key")) await client.Get("task");
            Check(SnapshotStore.ReadFile(Path.Combine(folder,"firebase-auth.bin")).Payload.Contains("rotated-refresh"),"Token rotation not saved");
            Check(!Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(folder,"firebase-auth.bin"))).Contains("rotated-refresh"),"Token not encrypted");
            Seed(folder,true); fake=new Fake(); fake.Calls.Enqueue((r,b)=>new { user_id="intruder",id_token="bad",refresh_token="bad",expires_in="3600" });
            using(var client=new MateClient(folder,fake,"test-key")) { bool denied=false; try { await client.Get("task"); } catch(InvalidOperationException) { denied=true; } Check(denied,"Refresh changed account"); }
            Seed(folder); fake=new Fake();
            using(var client=new MateClient(folder,fake,"test-key")) { bool rejected=false; try { await client.Update("../task","x"); } catch(ArgumentException) { rejected=true; } Check(rejected,"Invalid path accepted"); }
            Check(MateClient.Stamp(new DateTime(2026,11,3))==1793664000000L,"Wrong Todo Mate date zone");
            var todo=MateClient.ToTodo(Document()); Check(todo.Date==new DateTime(2026,11,3) && !todo.Done,"Firestore decode failed");
            await FallbackTests(folder);
        } finally { Directory.Delete(folder,true); }
    }
    static async Task PlayMcpInputTests(string folder) {
        string token=new string('a',64);
        string prompt="아래 oneTimeToken으로 연결해 주세요. https://playmcp.kakao.com/llms.txt\n```json\n{\"oneTimeToken\":\""+token+"\"}\n```\n다른 설명은 실행하지 않습니다.";
        foreach(string text in new[]{token,"  "+token+"\r\n","`"+token+"`",prompt,"**oneTimeToken**: `"+token+"`","one_time_token = '"+token+"'","OTT：\n"+token,"oneTimeToken: "+token+"\noneTimeToken: "+token}) {
            Check(LegacyMcp.MateClient.ExtractOneTimeToken(text)==token,"PlayMCP token extraction changed the key");
        }
        foreach(string text in new[]{null,"", "연결해 주세요", "oneTimeToken: short", "https://example.invalid/"+token,"oneTimeToken: "+token+"\noneTimeToken: "+new string('b',64),new string('a',65537)}) {
            bool rejected=false; try { LegacyMcp.MateClient.ExtractOneTimeToken(text); } catch(InvalidOperationException) { rejected=true; }
            Check(rejected,"PlayMCP accepted invalid or ambiguous input");
        }
        var fake=new Fake(); fake.Calls.Enqueue((r,b)=> {
            var body=Json.Deserialize<D>(b);
            Check(r.RequestUri.AbsoluteUri=="https://playmcp.kakao.com/api/v1/auths/otts:exchange" && body.Count==1 && (string)body["tokenValue"]==token,"PlayMCP transmitted the prompt or used its instructions");
            return new { accessToken=new { tokenValue="synthetic-access" },refreshToken=new { tokenValue="synthetic-refresh" } };
        });
        using(var client=new LegacyMcp.MateClient(folder,fake)) await client.Connect(prompt);
        string stored=Encoding.UTF8.GetString(System.Security.Cryptography.ProtectedData.Unprotect(File.ReadAllBytes(Path.Combine(folder,"playmcp-token.bin")),null,System.Security.Cryptography.DataProtectionScope.CurrentUser));
        Check(fake.Calls.Count==0 && !stored.Contains(token) && stored.Contains("synthetic-access"),"PlayMCP saved the one-time key or did not exchange it");
    }
    static async Task FallbackTests(string folder) {
        var day=new DateTime(2026,11,3);
        var goals=new List<Goal> { new Goal { Id="verified",Title="카테고리" } };
        Func<WeekData> fixture=()=>new WeekData { Goals=goals,Todos=new List<Todo> { new Todo { Id="only-sample",GoalId="verified",Date=day,Content="검증" } } };
        var now=new DateTime(2026,11,3,12,0,0,DateTimeKind.Utc);
        var route=new TodoReadFallback(folder,"fallback-test"); route.Now=()=>now; route.RememberGoals(goals);
        int primary=0,backup=0;
        route.McpRange=(a,b)=> { backup++; return Task.FromResult(fixture()); };
        Func<Task<WeekData>> ok=()=> { primary++; return Task.FromResult(fixture()); };
        Func<Task<WeekData>> quota=()=> { primary++; throw new FirebaseError(429,"RESOURCE_EXHAUSTED"); };
        var result=await route.Range(day,day,ok);
        Check(result.Source=="firebase" && primary==1 && backup==0,"Default read did not prefer Firebase");
        result=await route.Range(day,day,quota);
        Check(result.Source=="mcp" && primary==2 && backup==1,"Quota did not switch to MCP");
        route=new TodoReadFallback(folder,"fallback-test"); route.Now=()=>now; route.McpRange=(a,b)=> { backup++; return Task.FromResult(fixture()); };
        await route.Range(day,day,ok);
        Check(primary==2 && backup==2,"Restart retried exhausted Firebase");
        now=now.AddDays(1); result=await route.Range(day,day,ok);
        Check(primary==3 && backup==2 && result.Source=="firebase","Next day did not restore Firebase");
        Check(TodoReadFallback.NextFirebaseCheck(new DateTime(2026,11,3,16,0,0,DateTimeKind.Utc))==new DateTime(2026,11,4,8,0,0,DateTimeKind.Utc),"Firestore Pacific reset ignored");
        foreach(int status in new[]{401,403,500}) {
            var denied=new TodoReadFallback(folder,"deny-"+status); bool stopped=false;
            denied.McpRange=(a,b)=> { throw new Exception("MCP must not be called"); };
            try { await denied.Range(day,day,()=> { throw new FirebaseError(status,"PERMISSION_DENIED"); }); } catch(FirebaseError) { stopped=true; }
            Check(stopped,"Non-quota failure switched to MCP");
        }
        var authLimit=new TodoReadFallback(folder,"auth-rate"); bool rejected=false;
        try { await authLimit.Range(day,day,()=> { throw new FirebaseError(429,"TOO_MANY_ATTEMPTS_TRY_LATER"); }); } catch(FirebaseError) { rejected=true; }
        Check(rejected,"Authentication throttling switched to MCP");
        rejected=false;
        try { await authLimit.Range(day,day,()=> { throw new FirebaseError(429,"RESOURCE_EXHAUSTED",false); }); } catch(FirebaseError) { rejected=true; }
        Check(rejected,"Authentication quota was mistaken for a Firestore read quota");
        route=new TodoReadFallback(folder,"mcp-limit"); route.Now=()=>now; route.RememberGoals(goals); int exhausted=0;
        route.McpRange=(a,b)=> { exhausted++; throw new InvalidOperationException("오늘 할 일 조회 한도를 넘었습니다. 내일 다시 시도해주세요."); };
        for(int i=0;i<2;i++) { try { await route.Range(day,day,quota); } catch(InvalidOperationException) { } }
        Check(exhausted==1,"Exhausted MCP was repeatedly called");
        var wrong=new TodoReadFallback(folder,"wrong-account"); wrong.Now=()=>now; wrong.RememberGoals(goals);
        wrong.McpRange=(a,b)=>Task.FromResult(new WeekData { Goals=new List<Goal> { new Goal { Id="another-user",Title="카테고리" } },Todos=new List<Todo>() });
        rejected=false; try { await wrong.Range(day,day,quota); } catch(InvalidOperationException) { rejected=true; }
        Check(rejected,"Another MCP account was accepted");
        Seed(folder); var fake=new Fake();
        var goalDocument=new D {
            {"name","projects/mate-914f3/databases/(default)/documents/Goal/verified"},
            {"fields",new D { {"userID",V("owner")},{"title",V("카테고리")} }}
        };
        fake.Calls.Enqueue((r,b)=>new[]{new D { {"document",goalDocument} }});
        fake.Calls.Enqueue((r,b)=>new HttpResponseMessage((HttpStatusCode)429) { Content=new StringContent("{\"error\":{\"status\":\"RESOURCE_EXHAUSTED\"}}") });
        using(var client=new MateClient(folder,fake,"test-key")) {
            client.Reads.McpRange=(a,b)=>Task.FromResult(fixture());
            result=await client.LoadRange(day,day);
            Check(result.Source=="mcp" && fake.Calls.Count==0,"Client did not route exhausted reads");
            fake.Calls.Enqueue((r,b)=> { Check(r.RequestUri.Host=="firestore.googleapis.com","Write used MCP"); return new HttpResponseMessage((HttpStatusCode)429) { Content=new StringContent("{\"error\":{\"status\":\"RESOURCE_EXHAUSTED\"}}") }; });
            rejected=false; try { await client.Add("verified","새 내용",day); } catch(FirebaseError) { rejected=true; }
            Check(rejected && fake.Calls.Count==0,"Write fell back or bypassed Firebase goal validation");
        }
    }
}
