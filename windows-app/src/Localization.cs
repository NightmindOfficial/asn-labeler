using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Web.Script.Serialization;

namespace AsnLabeler
{
    static class Loc
    {
        public static string Language = "de";
        static readonly Dictionary<string, string> English = Load();
        static Dictionary<string, string> Load()
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("AsnLabeler.en.json"))
            using (var reader = new StreamReader(stream))
                return new JavaScriptSerializer().Deserialize<Dictionary<string, string>>(reader.ReadToEnd());
        }
        public static string T(string german)
        {
            string english;
            return Language == "en" && English.TryGetValue(german, out english) ? english : german;
        }
        public static string ApiError(string code, string detail)
        {
            if (Language != "en") return detail;
            if (code != null && code.StartsWith("paperless_"))
                return "Paperless request failed. Check the service diagnostics. (" + code + ")";
            return T(detail ?? "Anfrage nicht bestätigt.");
        }
    }
}
