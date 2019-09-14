﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Authentication;
using System.Threading.Tasks;
using Wabbajack.Common;
using WebSocketSharp;

namespace Wabbajack
{
    internal class NexusAPI
    {
        public static string GetNexusAPIKey()
        {
            var fi = new FileInfo("nexus.key_cache");
            if (fi.Exists && fi.LastWriteTime > DateTime.Now.AddHours(-72)) return File.ReadAllText("nexus.key_cache");

            var guid = Guid.NewGuid();
            var _websocket = new WebSocket("wss://sso.nexusmods.com")
            {
                SslConfiguration =
                {
                    EnabledSslProtocols = SslProtocols.Tls12
                }
            };

            var api_key = new TaskCompletionSource<string>();
            _websocket.OnMessage += (sender, msg) => { api_key.SetResult(msg.Data); };

            _websocket.Connect();
            _websocket.Send("{\"id\": \"" + guid + "\", \"appid\": \"" + Consts.AppName + "\"}");

            Process.Start($"https://www.nexusmods.com/sso?id={guid}&application=" + Consts.AppName);

            api_key.Task.Wait();
            var result = api_key.Task.Result;
            File.WriteAllText("nexus.key_cache", result);
            return result;
        }

        private static HttpClient BaseNexusClient(string apikey)
        {
            var _baseHttpClient = new HttpClient();

            _baseHttpClient.DefaultRequestHeaders.Add("User-Agent", Consts.UserAgent);
            _baseHttpClient.DefaultRequestHeaders.Add("apikey", apikey);
            _baseHttpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            _baseHttpClient.DefaultRequestHeaders.Add("Application-Name", Consts.AppName);
            _baseHttpClient.DefaultRequestHeaders.Add("Application-Version",
                $"{Assembly.GetEntryAssembly().GetName().Version}");
            return _baseHttpClient;
        }

        public static string GetNexusDownloadLink(NexusMod archive, string apikey, bool cache = false)
        {
            if (cache && TryGetCachedLink(archive, apikey, out var result)) return result;

            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            var client = BaseNexusClient(apikey);
            string url;
            var get_url_link = string.Format(
                "https://api.nexusmods.com/v1/games/{0}/mods/{1}/files/{2}/download_link.json",
                ConvertGameName(archive.GameName), archive.ModID, archive.FileID);
            using (var s = client.GetStreamSync(get_url_link))
            {
                url = s.FromJSON<List<DownloadLink>>().First().URI;
                return url;
            }
        }

        private static bool TryGetCachedLink(NexusMod archive, string apikey, out string result)
        {
            if (!Directory.Exists(Consts.NexusCacheDirectory))
                Directory.CreateDirectory(Consts.NexusCacheDirectory);


            var path = Path.Combine(Consts.NexusCacheDirectory,
                $"link-{archive.GameName}-{archive.ModID}-{archive.FileID}.txt");
            if (!File.Exists(path) || DateTime.Now - new FileInfo(path).LastWriteTime > new TimeSpan(24, 0, 0))
            {
                File.Delete(path);
                result = GetNexusDownloadLink(archive, apikey);
                File.WriteAllText(path, result);
                return true;
            }

            result = File.ReadAllText(path);
            return true;
        }

        public static string ConvertGameName(string gameName)
        {
            if (gameName == "SkyrimSE") return "skyrimspecialedition";
            if (gameName == "FalloutNV") return "newvegas";
            return gameName.ToLower();
        }


        public static UserStatus GetUserStatus(string apikey)
        {
            var url = "https://api.nexusmods.com/v1/users/validate.json";
            var client = BaseNexusClient(apikey);

            using (var s = client.GetStreamSync(url))
            {
                return s.FromJSON<UserStatus>();
            }
        }


        public static NexusFileInfo GetFileInfo(NexusMod mod, string apikey)
        {
            var url =
                $"https://api.nexusmods.com/v1/games/{ConvertGameName(mod.GameName)}/mods/{mod.ModID}/files/{mod.FileID}.json";
            var client = BaseNexusClient(apikey);

            using (var s = client.GetStreamSync(url))
            {
                return s.FromJSON<NexusFileInfo>();
            }
        }

        public static ModInfo GetModInfo(NexusMod archive, string apikey)
        {
            if (!Directory.Exists(Consts.NexusCacheDirectory))
                Directory.CreateDirectory(Consts.NexusCacheDirectory);

            var path = Path.Combine(Consts.NexusCacheDirectory, $"mod-info-{archive.GameName}-{archive.ModID}.json");
            if (File.Exists(path))
                return path.FromJSON<ModInfo>();


            var url =
                $"https://api.nexusmods.com/v1/games/{ConvertGameName(archive.GameName)}/mods/{archive.ModID}.json";
            var client = BaseNexusClient(apikey);

            using (var s = client.GetStreamSync(url))
            {
                var result = s.FromJSON<ModInfo>();
                result.ToJSON(path);
                return result;
            }
        }


        public static EndorsementResponse EndorseMod(NexusMod mod, string apikey)
        {
            Utils.Status($"Endorsing ${mod.GameName} - ${mod.ModID}");
            var url =
                $"https://api.nexusmods.com/v1/games/{ConvertGameName(mod.GameName)}/mods/{mod.ModID}/endorse.json";
            var client = BaseNexusClient(apikey);

            var content = new FormUrlEncodedContent(new Dictionary<string, string> {{"version", mod.Version}});

            using (var s = client.PostStreamSync(url, content))
            {
                return s.FromJSON<EndorsementResponse>();
            }
        }

        private class DownloadLink
        {
            public string URI { get; set; }
        }


        public class UserStatus
        {
            public string email;
            public bool is_premium;
            public bool is_supporter;
            public string key;
            public string name;
            public string profile_url;
            public string user_id;
        }

        public class NexusFileInfo
        {
            public ulong category_id;
            public string category_name;
            public string changelog_html;
            public string description;
            public string external_virus_scan_url;
            public ulong file_id;
            public string file_name;
            public bool is_primary;
            public string mod_version;
            public string name;
            public ulong size;
            public ulong size_kb;
            public DateTime uploaded_time;
            public ulong uploaded_timestamp;
            public string version;
        }

        public class ModInfo
        {
            public string author;
            public string uploaded_by;
            public string uploaded_users_profile_url;
        }
    }

    public class EndorsementResponse
    {
        public string message;
        public string status;
    }
}