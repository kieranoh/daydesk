using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using D=System.Collections.Generic.Dictionary<string,object>;

static class AppPaths {
    internal static string Root { get { return AppDomain.CurrentDomain.BaseDirectory; } }
    internal static string DataRoot { get { return Environment.GetEnvironmentVariable("DAYDESK_DATA_DIR") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TimeTreeWidget"); } }
    internal static bool Packaged { get { return File.Exists(Path.Combine(Root,"standalone.json")); } }
    internal static string InstanceSuffix {
        get { string profile=Environment.GetEnvironmentVariable("DAYDESK_DATA_DIR"); if(profile==null) return ""; using(var hash=SHA256.Create()) return "."+BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(profile))).Replace("-","").ToLowerInvariant().Substring(0,12); }
    }
    internal static string CommandPipe { get { return "TimeTreeWidget.Commands"+InstanceSuffix; } }
    internal static string Python { get { string path=Path.Combine(Root,"runtime","python","python.exe"); return File.Exists(path) ? path : "python.exe"; } }
    internal static string TreeAuth { get { return Path.Combine(DataRoot,"timetree-auth.bin"); } }
    internal static D TreeConnection() {
        var saved=SnapshotStore.ReadFile(TreeAuth);
        try { return saved==null ? new D() : new JavaScriptSerializer().Deserialize<D>(saved.Payload); } catch { return new D(); }
    }
    internal static bool TreeConnected { get { var value=TreeConnection(); object connected; return value.TryGetValue("connected",out connected) && connected is bool && (bool)connected; } }
    internal static string TreeCacheKey(string key) {
        var value=TreeConnection();
        if(value.ContainsKey("account_key")) return Convert.ToString(value["account_key"])+"|"+key;
        return Packaged ? "disconnected|"+key : key;
    }
    internal static string McpFolder {
        get {
            string own=Path.Combine(DataRoot,"mcp");
            if(Packaged || Environment.GetEnvironmentVariable("DAYDESK_DATA_DIR")!=null || File.Exists(Path.Combine(own,"playmcp-token.bin"))) return own;
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TodoMateWidget");
        }
    }
    internal static D Defaults() {
        try { return new JavaScriptSerializer().Deserialize<D>(File.ReadAllText(Path.Combine(Root,"defaults.json"))); } catch { return new D(); }
    }
}
