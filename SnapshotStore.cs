using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using D=System.Collections.Generic.Dictionary<string,object>;

sealed class Snapshot {
    internal string Payload;
    internal DateTime Updated;
}
static class SnapshotStore {
    internal static string Root=Path.Combine(AppPaths.DataRoot,"cache");
    internal static string FilePath(string area,string key) {
        if(area=="timetree") key=AppPaths.TreeCacheKey(key);
        using(var sha=SHA256.Create()) return Path.Combine(Root,area,BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(key))).Replace("-","").ToLowerInvariant()+".bin");
    }
    internal static Snapshot Read(string area,string key) { return ReadFile(FilePath(area,key)); }
    internal static Snapshot ReadFile(string path) {
        try {
            byte[] bytes;
            using(var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))
            using(var memory=new MemoryStream()) { file.CopyTo(memory); bytes=memory.ToArray(); }
            var json=new JavaScriptSerializer { MaxJsonLength=16000000 };
            var record=json.Deserialize<D>(Encoding.UTF8.GetString(ProtectedData.Unprotect(bytes,null,DataProtectionScope.CurrentUser)));
            return new Snapshot { Payload=(string)record["payload"],Updated=DateTime.Parse((string)record["updated_at"],CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind).ToUniversalTime() };
        } catch { return null; }
    }
    internal static void Write(string area,string key,string payload,DateTime? updated=null) { WriteFile(FilePath(area,key),payload,updated); }
    internal static void WriteFile(string path,string payload,DateTime? updated=null) {
        var json=new JavaScriptSerializer { MaxJsonLength=16000000 };
        var record=json.Serialize(new { payload=payload,updated_at=(updated ?? DateTime.UtcNow).ToUniversalTime().ToString("o",CultureInfo.InvariantCulture) });
        byte[] bytes=ProtectedData.Protect(Encoding.UTF8.GetBytes(record),null,DataProtectionScope.CurrentUser);
        Directory.CreateDirectory(Path.GetDirectoryName(path)); string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try { File.WriteAllBytes(temporary,bytes); if(File.Exists(path)) File.Replace(temporary,path,null); else File.Move(temporary,path); }
        finally { if(File.Exists(temporary)) File.Delete(temporary); }
    }
}
