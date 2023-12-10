﻿using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using System.Collections.Generic;
using System.Web;
using Newtonsoft.Json.Linq;
using System.Linq;
using Lampac.Engine.CORE;
using Lampac.Models.LITE.Alloha;
using Online;
using Shared.Engine.CORE;

namespace Lampac.Controllers.LITE
{
    public class Alloha : BaseOnlineController
    {
        ProxyManager proxyManager = new ProxyManager("alloha", AppInit.conf.Alloha);

        [HttpGet]
        [Route("lite/alloha")]
        async public Task<ActionResult> Index(string imdb_id, long kinopoisk_id, string title, string original_title, int serial, string original_language, int year, string t, int s = -1)
        {
            if (!AppInit.conf.Alloha.enable)
                return OnError("disable");

            var result = await search(imdb_id, kinopoisk_id, title, serial, original_language, year);
            if (result.data == null)
                return OnError("data", proxyManager, result.refresh_proxy);

            JToken data = result.data;

            bool firstjson = true;
            string html = "<div class=\"videos__line\">";
            string defaultargs = $"&imdb_id={imdb_id}&kinopoisk_id={kinopoisk_id}&title={HttpUtility.UrlEncode(title)}&original_title={HttpUtility.UrlEncode(original_title)}&serial={serial}&year={year}&original_language={original_language}";

            if (result.category_id is 1 or 3)
            {
                #region Фильм
                foreach (var translation in data.Value<JObject>("translation_iframe").ToObject<Dictionary<string, Dictionary<string, object>>>())
                {
                    string link = $"{host}/lite/alloha/video?t={translation.Key}" + defaultargs;
                    string streamlink = $"{link.Replace("/video", "/video.m3u8")}&play=true";

                    html += "<div class=\"videos__item videos__movie selector " + (firstjson ? "focused" : "") + "\" media=\"\" data-json='{\"method\":\"call\",\"url\":\"" + link + "\",\"stream\":\"" + streamlink + "\",\"voice_name\":\"" + translation.Value["quality"].ToString() + "\"}'><div class=\"videos__item-imgbox videos__movie-imgbox\"></div><div class=\"videos__item-title\">" + translation.Value["name"].ToString() + "</div></div>";
                    firstjson = false;
                }
                #endregion
            }
            else
            {
                #region Сериал
                if (s == -1)
                {
                    foreach (var season in data.Value<JObject>("seasons").ToObject<Dictionary<string, object>>().Reverse())
                    {
                        string link = $"{host}/lite/alloha?s={season.Key}" + defaultargs;

                        html += "<div class=\"videos__item videos__season selector " + (firstjson ? "focused" : "") + "\" data-json='{\"method\":\"link\",\"url\":\"" + link + "\"}'><div class=\"videos__season-layers\"></div><div class=\"videos__item-imgbox videos__season-imgbox\"><div class=\"videos__item-title videos__season-title\">" + $"{season.Key} сезон" + "</div></div></div>";
                        firstjson = false;
                    }
                }
                else
                {
                    #region Перевод
                    string activTranslate = t;

                    foreach (var episodes in data.Value<JObject>("seasons").GetValue(s.ToString()).Value<JObject>("episodes").ToObject<Dictionary<string, Episode>>().Select(i => i.Value.translation))
                    {
                        foreach (var translation in episodes)
                        {
                            if (html.Contains(translation.Value.translation) || translation.Value.translation.ToLower().Contains("субтитры"))
                                continue;

                            if (string.IsNullOrWhiteSpace(activTranslate))
                                activTranslate = translation.Key;

                            string link = $"{host}/lite/alloha?s={s}&t={translation.Key}" + defaultargs;

                            html += "<div class=\"videos__button selector " + (activTranslate == translation.Key ? "active" : "") + "\" data-json='{\"method\":\"link\",\"url\":\"" + link + "\"}'>" + translation.Value.translation + "</div>";
                        }
                    }

                    html += "</div><div class=\"videos__line\">";
                    #endregion

                    foreach (var episode in data.Value<JObject>("seasons").GetValue(s.ToString()).Value<JObject>("episodes").ToObject<Dictionary<string, Episode>>().Reverse())
                    {
                        if (!string.IsNullOrWhiteSpace(activTranslate) && !episode.Value.translation.ContainsKey(activTranslate))
                            continue;

                        string link = $"{host}/lite/alloha/video?t={activTranslate}&s={s}&e={episode.Key}" + defaultargs;
                        string streamlink = $"{link.Replace("/video", "/video.m3u8")}&play=true";

                        html += "<div class=\"videos__item videos__movie selector " + (firstjson ? "focused" : "") + "\" media=\"\" s=\"" + s + "\" e=\"" + episode.Key + "\" data-json='{\"method\":\"call\",\"url\":\"" + link + "\",\"stream\":\"" + streamlink + "\",\"title\":\"" + $"{title ?? original_title} ({episode.Key} серия)" + "\"}'><div class=\"videos__item-imgbox videos__movie-imgbox\"></div><div class=\"videos__item-title\">" + $"{episode.Key} серия" + "</div></div>";
                        firstjson = false;
                    }
                }
                #endregion
            }

            return Content(html + "</div>", "text/html; charset=utf-8");
        }


        #region Video
        [HttpGet]
        [Route("lite/alloha/video")]
        [Route("lite/alloha/video.m3u8")]
        async public Task<ActionResult> Video(string imdb_id, long kinopoisk_id, string title, string original_title, string t, int s, int e, bool play)
        {
            if (!AppInit.conf.Alloha.enable)
                return OnError("disable");

            string userIp = HttpContext.Connection.RemoteIpAddress.ToString();
            if (AppInit.conf.Alloha.localip || AppInit.conf.Alloha.streamproxy)
            {
                userIp = await mylocalip();
                if (userIp == null)
                    return OnError("userIp");
            }

            string memKey = $"alloha:view:stream:{imdb_id}:{kinopoisk_id}:{t}:{s}:{e}:{userIp}";
            if (!memoryCache.TryGetValue(memKey, out (string m3u8, string subtitle) _cache))
            {
                #region url запроса
                string uri = $"{AppInit.conf.Alloha.linkhost}/link_file.php?secret_token={AppInit.conf.Alloha.secret_token}&imdb={imdb_id}&kp={kinopoisk_id}";

                uri += $"&ip={userIp}&translation={t}";

                if (s > 0)
                    uri += $"&season={s}";

                if (e > 0)
                    uri += $"&episode={e}";
                #endregion

                string json = await HttpClient.Get(uri, timeoutSeconds: 8, proxy: proxyManager.Get());
                if (json == null || !json.Contains("\"status\":\"success\""))
                    return OnError("json");

                _cache.m3u8 = Regex.Match(json.Replace("\\", ""), "\"playlist_file\":\"\\{[^\\}]+\\}(https?://[^;\"]+\\.m3u8)").Groups[1].Value;
                if (string.IsNullOrWhiteSpace(_cache.m3u8))
                {
                    _cache.m3u8 = Regex.Match(json.Replace("\\", ""), "\"playlist_file\":\"(https?://[^;\"]+\\.m3u8)").Groups[1].Value;
                    if (string.IsNullOrWhiteSpace(_cache.m3u8))
                        return OnError("m3u8");
                }

                string subtitle = Regex.Match(json.Replace("\\", ""), "\"subtitle\":\"(https?://[^;\" ]+)").Groups[1].Value;
                if (!string.IsNullOrWhiteSpace(subtitle) && subtitle.Contains(".vtt"))
                    _cache.subtitle = "{\"label\": \"По умолчанию\",\"url\": \"" + subtitle + "\"}";

                memoryCache.Set(memKey, _cache, cacheTime(10));
            }

            if (play)
                return Redirect(HostStreamProxy(AppInit.conf.Alloha, _cache.m3u8));

            return Content("{\"method\":\"play\",\"url\":\"" + HostStreamProxy(AppInit.conf.Alloha, _cache.m3u8) + "\",\"title\":\"" + (title ?? original_title) + "\", \"subtitles\": [" + _cache.subtitle + "]}", "application/json; charset=utf-8");
        }
        #endregion

        #region search
        async ValueTask<(bool refresh_proxy, int category_id, JToken data)> search(string imdb_id, long kinopoisk_id, string title, int serial, string original_language, int year)
        {
            string memKey = $"alloha:view:{kinopoisk_id}:{imdb_id}";
            if (0 >= kinopoisk_id && string.IsNullOrEmpty(imdb_id))
                memKey = $"alloha:viewsearch:{title}:{serial}:{original_language}:{year}";

            if (!memoryCache.TryGetValue(memKey, out (int category_id, JToken data) res))
            {
                if (memKey.Contains(":viewsearch:"))
                {
                    if (string.IsNullOrWhiteSpace(title) || year == 0)
                        return default;

                    var root = await HttpClient.Get<JObject>($"{AppInit.conf.Alloha.apihost}/?token={AppInit.conf.Alloha.token}&name={HttpUtility.UrlEncode(title)}&list={(serial == 1 ? "serial" : "movie")}", timeoutSeconds: 8, proxy: proxyManager.Get());
                    if (root == null || !root.ContainsKey("data"))
                        return (true, 0, null);

                    foreach (var item in root.Value<JArray>("data"))
                    {
                        if (item.Value<string>("name")?.ToLower()?.Trim() == title.ToLower())
                        {
                            int y = item.Value<int>("year");
                            if (y > 0 && (y == year || y == (year - 1) || y == (year + 1)))
                            {
                                if (original_language == "ru" && item.Value<string>("country")?.ToLower() != "россия")
                                    continue;

                                res.data = item;
                                res.category_id = item.Value<int>("category_id");
                                break;
                            }
                        }
                    }

                    if (res.data == null)
                        return default;
                }
                else
                {
                    var root = await HttpClient.Get<JObject>($"{AppInit.conf.Alloha.apihost}/?token={AppInit.conf.Alloha.token}&kp={kinopoisk_id}&imdb={imdb_id}", timeoutSeconds: 8, proxy: proxyManager.Get());
                    if (root == null || !root.ContainsKey("data"))
                        return (true, 0, null);

                    res.data = root.GetValue("data");
                    res.category_id = res.data.Value<int>("category");
                }

                memoryCache.Set(memKey, res, cacheTime(40));
            }

            return (false, res.category_id, res.data);
        }
        #endregion
    }
}
