using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

sealed class Todo
{
    public string Id, Content, Category, GoalId;
    public DateTime Date;
    public bool Done;
    public string Reminder;
}

sealed class Goal
{
    public string Id, Title;
}

sealed class WeekData
{
    public List<Todo> Todos;
    public List<Goal> Goals;
}

sealed class MateClient : IDisposable
{
    const string Gateway = "https://playmcp.kakao.com/mcp";
    const string Exchange = "https://playmcp.kakao.com/api/v1/auths/otts:exchange";
    const string TokenEndpoint = "https://playauth.kakao.com/playmcp/oauth2/token";
    const string ClientId = "HElMUWdVoroTsrXxezeTSemg8gXzzCKWARb5MJux8gY";
    readonly HttpClient http;
    readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024 };
    readonly string tokenPath;
    Dictionary<string, object> tokens;
    string sessionId, loadTool, updateTool, rangeTool, goalsTool, addTool, reminderTool;
    readonly List<object> toolSchemas = new List<object>();
    string protocolVersion = "2025-06-18";
    int requestId;
    public bool Connected { get { return tokens != null; } }

    public MateClient(string folder, HttpMessageHandler handler = null)
    {
        http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(30) };
        tokenPath = Path.Combine(folder, "playmcp-token.bin");
        ReadTokens();
    }

    void ReadTokens()
    {
        if (File.Exists(tokenPath))
        {
            try
            {
                tokens = json.Deserialize<Dictionary<string, object>>(Encoding.UTF8.GetString(
                    ProtectedData.Unprotect(File.ReadAllBytes(tokenPath), null, DataProtectionScope.CurrentUser)));
                if (tokens == null || !tokens.ContainsKey("access_token") || !tokens.ContainsKey("refresh_token")) tokens = null;
            }
            catch (CryptographicException) { tokens = null; }
            catch (ArgumentException) { tokens = null; }
        }
    }

    public static string ExtractOneTimeToken(string input)
    {
        var match = Regex.Match(input, "[\"']?oneTimeToken[\"']?\\s*[:=]\\s*[\"']?([A-Za-z0-9._~+/=-]+)", RegexOptions.IgnoreCase);
        string value = match.Success ? match.Groups[1].Value : input.Trim();
        if (value.Length < 16 || value.Length > 4096 || !Regex.IsMatch(value, "^[A-Za-z0-9._~+/=-]+$"))
            throw new InvalidOperationException("PlayMCP의 ‘OpenClaw와 연결’에서 생성한 연결 프롬프트 또는 일회용 토큰을 붙여 넣어 주세요.");
        return value;
    }

    public async Task Connect(string prompt)
    {
        string oneTimeToken = ExtractOneTimeToken(prompt);
        using (var response = await http.PostAsync(Exchange, JsonContent(new { tokenValue = oneTimeToken })))
        {
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException("일회용 연결 토큰을 사용할 수 없습니다. PlayMCP에서 새 연결 프롬프트를 생성해 주세요. (HTTP " + (int)response.StatusCode + ")");
            var data = json.Deserialize<Dictionary<string, object>>(await response.Content.ReadAsStringAsync());
            var access = Dict(data["accessToken"]);
            var refresh = Dict(data["refreshToken"]);
            var next = new Dictionary<string, object> { { "access_token", access["tokenValue"] }, { "refresh_token", refresh["tokenValue"] } };
            if (string.IsNullOrWhiteSpace(Convert.ToString(next["access_token"])) || string.IsNullOrWhiteSpace(Convert.ToString(next["refresh_token"])))
                throw new InvalidOperationException("연결 응답에 인증 정보가 없습니다.");
            tokens = next;
            sessionId = loadTool = updateTool = rangeTool = goalsTool = null;
            SaveTokens();
        }
    }

    void SaveTokens()
    {
        byte[] data = ProtectedData.Protect(Encoding.UTF8.GetBytes(json.Serialize(tokens)), null, DataProtectionScope.CurrentUser);
        string temporary = tokenPath + ".tmp";
        File.WriteAllBytes(temporary, data);
        if (File.Exists(tokenPath)) File.Replace(temporary, tokenPath, null);
        else File.Move(temporary, tokenPath);
    }

    async Task RefreshToken()
    {
        // Widget and CLI share rotating credentials; refresh them one process at a time.
        using (var gate = new System.Threading.Semaphore(1, 1, "Local\\TodoMateTokenRefresh"))
        {
            if (!await Task.Run(() => gate.WaitOne(30000))) throw new InvalidOperationException("다른 Todo Mate 연결이 인증을 갱신하고 있습니다. 잠시 후 다시 시도해 주세요.");
            try
            {
                string previous = Convert.ToString(tokens["access_token"]);
                ReadTokens();
                if (!Connected) throw new InvalidOperationException("계정 연결 정보를 읽지 못했습니다. 다시 연결해 주세요.");
                if (Convert.ToString(tokens["access_token"]) != previous) return;
                await RefreshTokenCore();
            }
            finally { gate.Release(); }
        }
    }

    async Task RefreshTokenCore()
    {
        using (var response = await http.PostAsync(TokenEndpoint, new FormUrlEncodedContent(new Dictionary<string, string> {
            { "grant_type", "refresh_token" }, { "client_id", ClientId }, { "refresh_token", Convert.ToString(tokens["refresh_token"]) }
        })))
        {
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException("계정 연결이 만료되었습니다. ‘계정 연결’에서 다시 연결해 주세요.");
            var next = json.Deserialize<Dictionary<string, object>>(await response.Content.ReadAsStringAsync());
            if (!next.ContainsKey("access_token")) throw new InvalidOperationException("인증 갱신 응답이 올바르지 않습니다.");
            tokens["access_token"] = next["access_token"];
            if (next.ContainsKey("refresh_token")) tokens["refresh_token"] = next["refresh_token"];
            SaveTokens();
        }
    }

    StringContent JsonContent(object value) { return new StringContent(json.Serialize(value), Encoding.UTF8, "application/json"); }

    async Task<Dictionary<string, object>> Rpc(string method, object parameters, bool notification = false)
    {
        if (!Connected) throw new InvalidOperationException("먼저 ‘계정 연결’을 진행해 주세요.");
        int id = ++requestId;
        var payload = new Dictionary<string, object> { { "jsonrpc", "2.0" }, { "method", method }, { "params", parameters } };
        if (!notification) payload["id"] = id;
        for (int attempt = 0; attempt < 2; attempt++)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Post, Gateway))
            {
                request.Content = JsonContent(payload);
                request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + Convert.ToString(tokens["access_token"]));
                request.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");
                request.Headers.TryAddWithoutValidation("MCP-Protocol-Version", protocolVersion);
                if (sessionId != null) request.Headers.TryAddWithoutValidation("Mcp-Session-Id", sessionId);
                using (var response = await http.SendAsync(request))
                {
                    if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0) { await RefreshToken(); continue; }
                    if (!response.IsSuccessStatusCode)
                    {
                        if (response.StatusCode == HttpStatusCode.NotFound) sessionId = loadTool = updateTool = rangeTool = goalsTool = null;
                        throw new InvalidOperationException("공식 연동 서버에 연결하지 못했습니다. (HTTP " + (int)response.StatusCode + ") ‘새로고침’으로 다시 시도해 주세요.");
                    }
                    IEnumerable<string> sessionHeaders;
                    if (response.Headers.TryGetValues("Mcp-Session-Id", out sessionHeaders)) sessionId = sessionHeaders.First();
                    if (notification) return null;
                    var result = ParseRpc(await response.Content.ReadAsStringAsync(), id);
                    if (method == "initialize" && result.ContainsKey("protocolVersion")) protocolVersion = Convert.ToString(result["protocolVersion"]);
                    return result;
                }
            }
        }
        throw new InvalidOperationException("계정 인증을 갱신하지 못했습니다.");
    }

    public static Dictionary<string, object> ParseRpc(string text, int id)
    {
        var serializer = new JavaScriptSerializer();
        var candidates = new List<string>();
        if (text.TrimStart().StartsWith("{")) candidates.Add(text);
        else
        {
            var data = new StringBuilder();
            foreach (string line in (text.Replace("\r\n", "\n") + "\n\n").Split('\n'))
            {
                if (line.StartsWith("data:")) data.AppendLine(line.Substring(5).TrimStart());
                else if (line.Length == 0 && data.Length > 0) { candidates.Add(data.ToString()); data.Clear(); }
            }
        }
        foreach (string candidate in candidates)
        {
            var envelope = serializer.Deserialize<Dictionary<string, object>>(candidate);
            if (!envelope.ContainsKey("id") || Convert.ToString(envelope["id"]) != id.ToString()) continue;
            if (envelope.ContainsKey("error")) throw new InvalidOperationException("공식 연동 요청이 거부되었습니다. Todo Mate 계정 연결과 이용 조건을 확인해 주세요.");
            if (!envelope.ContainsKey("result")) break;
            return Dict(envelope["result"]);
        }
        throw new InvalidOperationException("공식 연동 응답 형식을 확인하지 못했습니다.");
    }

    async Task Initialize()
    {
        if (loadTool != null && updateTool != null && rangeTool != null && goalsTool != null) return;
        await Rpc("initialize", new { protocolVersion = "2025-06-18", capabilities = new { }, clientInfo = new { name = "todo-mate-desktop-widget", version = "0.2.0" } });
        await Rpc("notifications/initialized", new { }, true);
        var names = new List<string>();
        toolSchemas.Clear();
        string cursor = null;
        var seen = new HashSet<string>();
        do
        {
            var result = await Rpc("tools/list", cursor == null ? (object)new { } : new { cursor = cursor });
            foreach (var item in (IEnumerable)result["tools"])
            {
                string name = Convert.ToString(Dict(item)["name"]);
                names.Add(name);
                if (name.IndexOf("TodoMate", StringComparison.OrdinalIgnoreCase) >= 0) toolSchemas.Add(item);
            }
            cursor = result.ContainsKey("nextCursor") ? Convert.ToString(result["nextCursor"]) : null;
            if (cursor != null && !seen.Add(cursor)) throw new InvalidOperationException("도구 목록의 페이지 정보가 올바르지 않습니다.");
        } while (!string.IsNullOrEmpty(cursor));
        loadTool = FindTool(names, "loadTodoItems");
        updateTool = FindTool(names, "updateTodoItem");
        rangeTool = FindTool(names, "loadTodoItemsByDateRange");
        goalsTool = FindTool(names, "loadGoals");
        addTool = FindTool(names, "addTodoItem");
        reminderTool = FindTool(names, "updateTodoItemRemindAt");
    }

    static string FindTool(List<string> names, string method)
    {
        var matches = names.Where(n => n == method || (n.IndexOf("TodoMate", StringComparison.OrdinalIgnoreCase) >= 0 && n.EndsWith(method, StringComparison.Ordinal))).ToList();
        if (matches.Count != 1) throw new InvalidOperationException("PlayMCP 도구함에 ‘투두메이트’를 추가하고 계정 연결을 완료해 주세요.");
        return matches[0];
    }

    async Task<object> Call(string name, object arguments)
    {
        var result = await Rpc("tools/call", new { name = name, arguments = arguments });
        if (result.ContainsKey("isError") && result["isError"] is bool && (bool)result["isError"])
            throw new InvalidOperationException("Todo Mate 요청을 처리하지 못했습니다. PlayMCP에서 계정 연결과 프리미엄 필요 여부를 확인해 주세요.");
        if (result.ContainsKey("structuredContent")) return CheckSuccess(result["structuredContent"]);
        if (result.ContainsKey("content"))
            foreach (var block in (IEnumerable)result["content"])
            {
                var part = Dict(block);
                if (part.ContainsKey("text"))
                {
                    string value = Convert.ToString(part["text"]);
                    try { return CheckSuccess(json.DeserializeObject(value)); }
                    catch (ArgumentException) { return value; }
                }
            }
        throw new InvalidOperationException("Todo Mate의 데이터 응답이 비어 있습니다.");
    }

    internal static object CheckSuccess(object value)
    {
        var result = value as Dictionary<string, object>;
        if (result != null && result.ContainsKey("success") && result["success"] is bool && !(bool)result["success"])
            throw new InvalidOperationException(result.ContainsKey("message") ? Convert.ToString(result["message"]) : "Todo Mate 요청이 거부되었습니다.");
        return value;
    }

    public async Task<List<Todo>> Load(DateTime date)
    {
        await Initialize();
        object data = await Call(loadTool, new { year = date.Year, month = date.Month, day = date.Day });
        return ParseTodos(data);
    }

    public async Task SetDone(string id, bool done)
    {
        RequireText(id, "할 일 ID");
        await Initialize();
        await Call(updateTool, new { todo_id = id, is_done = done });
    }

    internal static void RequireText(string text, string name)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException(name + "을(를) 입력해 주세요.");
    }

    public async Task<object> Add(string goalTitle, string content, DateTime date)
    {
        RequireText(goalTitle, "카테고리"); RequireText(content, "할 일 내용");
        var goals = await LoadGoals();
        if (goals.Count(g => g.Title == goalTitle) != 1)
            throw new InvalidOperationException("동일한 이름의 카테고리가 없거나 여러 개입니다. 원본 앱에서 카테고리 이름을 확인해 주세요.");
        return await Call(addTool, new { goal_title = goalTitle, content = content.Trim(), year = date.Year, month = date.Month, day = date.Day });
    }

    public async Task<object> SetReminder(string id, DateTime when)
    {
        RequireText(id, "할 일 ID");
        await Initialize();
        return await Call(reminderTool, new { todo_id = id, year = when.Year, month = when.Month, day = when.Day, hour = when.Hour, minute = when.Minute });
    }

    public async Task<List<object>> Tools() { await Initialize(); return toolSchemas; }

    public static DateTime WeekStart(DateTime date) { return date.Date.AddDays(-(int)date.DayOfWeek); }
    public static DateTime PeriodStart(DateTime date, bool month)
    { return month ? WeekStart(new DateTime(date.Year, date.Month, 1)) : WeekStart(date); }
    public static DateTime PeriodEnd(DateTime date, bool month)
    { return month ? WeekStart(new DateTime(date.Year, date.Month, DateTime.DaysInMonth(date.Year, date.Month))).AddDays(6) : WeekStart(date).AddDays(6); }

    public Task<WeekData> LoadWeek(DateTime selected) { return LoadRange(WeekStart(selected), WeekStart(selected).AddDays(6)); }

    public Task<WeekData> LoadCalendar(DateTime selected, bool month) { return LoadRange(PeriodStart(selected, month), PeriodEnd(selected, month)); }

    public async Task<WeekData> LoadRange(DateTime start, DateTime end)
    {
        if (end < start || (end - start).TotalDays > 61) throw new ArgumentException("조회 범위는 1~62일이어야 합니다.");
        await Initialize();
        var result = Dict(await Call(rangeTool, new { start_year = start.Year, start_month = start.Month, start_day = start.Day,
            end_year = end.Year, end_month = end.Month, end_day = end.Day, limit = 1000 }));
        var todos = ParseTodos(result);
        if (todos.Any(t => t.Date < start || t.Date > end)) throw new InvalidOperationException("조회 범위와 다른 날짜의 할 일이 반환되었습니다.");
        if (result.ContainsKey("total_count") && Convert.ToInt32(result["total_count"]) > todos.Count)
            throw new InvalidOperationException("할 일 목록 일부를 가져오지 못했습니다. 기간을 줄여 다시 조회해 주세요.");
        var goals = await LoadGoals();
        foreach (var todo in todos)
        {
            var goal = goals.FirstOrDefault(g => g.Id == todo.GoalId);
            todo.Category = goal == null ? "할 일" : string.IsNullOrWhiteSpace(goal.Title) ? "기본" : goal.Title;
        }
        return new WeekData { Todos = todos, Goals = goals };
    }

    public async Task<List<Goal>> LoadGoals()
    {
        await Initialize();
        var categories = Dict(await Call(goalsTool, new { }));
        var goals = new List<Goal>();
        foreach (object entry in (IEnumerable)categories["goals"])
        {
            var goal = Dict(entry);
            string id = Convert.ToString(goal["id"]);
            if (string.IsNullOrWhiteSpace(id)) throw new InvalidOperationException("카테고리 ID가 비어 있습니다.");
            goals.Add(new Goal { Id = id, Title = Convert.ToString(goal["title"]) });
        }
        return goals;
    }

    public static List<Todo> ParseTodos(object value)
    {
        var list = new List<Todo>();
        Visit(value, list);
        if (list.Select(t => t.Id).Distinct().Count() != list.Count) throw new InvalidOperationException("중복된 할 일 ID가 있어 변경을 중단했습니다.");
        return list;
    }

    static void Visit(object value, List<Todo> list)
    {
        var record = value as Dictionary<string, object>;
        if (record != null)
        {
            string idKey = record.ContainsKey("todo_id") ? "todo_id" : "id";
            if (record.ContainsKey("content") && record.ContainsKey(idKey))
            {
                string doneKey = record.ContainsKey("is_done") ? "is_done" : "isDone";
                if (!record.ContainsKey(doneKey) || !(record[doneKey] is bool)) throw new InvalidOperationException("할 일의 완료 상태 형식이 달라 변경을 중단했습니다.");
                string id = Convert.ToString(record[idKey]);
                if (string.IsNullOrWhiteSpace(id)) throw new InvalidOperationException("할 일 ID가 비어 있습니다.");
                string category = record.ContainsKey("goal_title") ? Convert.ToString(record["goal_title"]) : record.ContainsKey("goalTitle") ? Convert.ToString(record["goalTitle"]) : "";
                DateTime date = record.ContainsKey("date") ? new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(Convert.ToInt64(record["date"])).Date : DateTime.MinValue;
                list.Add(new Todo { Id = id, Content = Convert.ToString(record["content"]), Category = category, Done = (bool)record[doneKey],
                    GoalId = record.ContainsKey("goalID") ? Convert.ToString(record["goalID"]) : "", Date = date,
                    Reminder = record.ContainsKey("formattedRemindAt") ? Convert.ToString(record["formattedRemindAt"]) : "" });
            }
            else
            {
                if (record.Count > 0 && !record.Values.Any(v => v is IDictionary || (v is IEnumerable && !(v is string))))
                    throw new InvalidOperationException("할 일 응답 형식을 아직 지원하지 않습니다.");
                foreach (object child in record.Values) if (child is IDictionary || (child is IEnumerable && !(child is string))) Visit(child, list);
            }
        }
        else if (value is IEnumerable && !(value is string)) foreach (object child in (IEnumerable)value) Visit(child, list);
        else throw new InvalidOperationException("할 일 목록 응답 형식을 확인하지 못했습니다.");
    }

    static Dictionary<string, object> Dict(object value)
    {
        var result = value as Dictionary<string, object>;
        if (result == null) throw new InvalidOperationException("공식 서버 응답 형식이 올바르지 않습니다.");
        return result;
    }

    static object Shape(object value)
    {
        var record = value as Dictionary<string, object>;
        if (record != null) return record.ToDictionary(pair => pair.Key, pair => Shape(pair.Value));
        if (value is IEnumerable && !(value is string)) return ((IEnumerable)value).Cast<object>().Take(1).Select(Shape).ToArray();
        return value == null ? "null" : value.GetType().Name;
    }

    public void Dispose() { http.Dispose(); }
}
