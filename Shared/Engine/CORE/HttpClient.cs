﻿using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Lampac.Engine.CORE
{
    public static class HttpClient
    {
        #region Handler
        static HttpClientHandler Handler(string url, WebProxy proxy)
        {
            var handler = new HttpClientHandler()
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };

            handler.ServerCertificateCustomValidationCallback += (sender, cert, chain, sslPolicyErrors) => true;

            if (proxy != null)
            {
                handler.UseProxy = true;
                handler.Proxy = proxy;
            }

            if (AppInit.conf.globalproxy != null && AppInit.conf.globalproxy.Count > 0)
            {
                foreach (var p in AppInit.conf.globalproxy)
                {
                    if (p.list == null || p.list.Count == 0 || p.pattern == null)
                        continue;

                    if (Regex.IsMatch(url, p.pattern, RegexOptions.IgnoreCase))
                    {
                        ICredentials credentials = null;

                        if (p.useAuth)
                            credentials = new NetworkCredential(p.username, p.password);

                        handler.UseProxy = true;
                        handler.Proxy = new WebProxy(p.list.OrderBy(a => Guid.NewGuid()).First(), p.BypassOnLocal, null, credentials);
                        break;
                    }
                }
            }

            return handler;
        }
        #endregion

        #region DefaultRequestHeaders
        static void DefaultRequestHeaders(System.Net.Http.HttpClient client, int timeoutSeconds, long MaxResponseContentBufferSize, string cookie, string referer, List<(string name, string val)> addHeaders)
        {
            client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);

            if (MaxResponseContentBufferSize != -1)
                client.MaxResponseContentBufferSize = MaxResponseContentBufferSize == 0 ? 10_000_000 : MaxResponseContentBufferSize; // 10MB

            client.DefaultRequestHeaders.Add("Accept-Encoding", "gzip, deflate");
            client.DefaultRequestHeaders.Add("Accept-Language", "ru-RU,ru;q=0.9,en-US;q=0.6,en;q=0.5");

            if (cookie != null)
                client.DefaultRequestHeaders.Add("cookie", cookie);

            if (referer != null)
                client.DefaultRequestHeaders.Add("referer", referer);

            bool setDefaultUseragent = true;

            if (addHeaders != null)
            {
                foreach (var item in addHeaders)
                {
                    if (item.name.ToLower() == "user-agent")
                        setDefaultUseragent = false;

                    client.DefaultRequestHeaders.Add(item.name, item.val);
                }
            }

            if (setDefaultUseragent)
                client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/116.0.0.0 Safari/537.36");
        }
        #endregion


        #region GetLocation
        async public static ValueTask<string> GetLocation(string url, string referer = null, int timeoutSeconds = 8, List<(string name, string val)> addHeaders = null, int httpversion = 1, bool allowAutoRedirect = false, WebProxy proxy = null)
        {
            try
            {
                HttpClientHandler handler = Handler(url, proxy);
                handler.AllowAutoRedirect = allowAutoRedirect;

                using (var client = new System.Net.Http.HttpClient(handler))
                {
                    DefaultRequestHeaders(client, timeoutSeconds, 2000000, null, referer, addHeaders);

                    var req = new HttpRequestMessage(HttpMethod.Get, url)
                    {
                        Version = new Version(httpversion, 0)
                    };

                    using (HttpResponseMessage response = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead))
                    {
                        string location = ((int)response.StatusCode == 301 || (int)response.StatusCode == 302 || (int)response.StatusCode == 307) ? response.Headers.Location?.ToString() : response.RequestMessage.RequestUri?.ToString();
                        location = Uri.EscapeUriString(System.Web.HttpUtility.UrlDecode(location ?? ""));

                        return string.IsNullOrWhiteSpace(location) ? null : location;
                    }
                }
            }
            catch
            {
                return null;
            }
        }
        #endregion


        #region Get
        async public static ValueTask<string> Get(string url, Encoding encoding = default, string cookie = null, string referer = null, int timeoutSeconds = 15, List<(string name, string val)> addHeaders = null, long MaxResponseContentBufferSize = 0, WebProxy proxy = null, int httpversion = 1)
        {
            return (await BaseGetAsync(url, encoding, cookie: cookie, referer: referer, timeoutSeconds: timeoutSeconds, addHeaders: addHeaders, MaxResponseContentBufferSize: MaxResponseContentBufferSize, proxy: proxy, httpversion: httpversion)).content;
        }
        #endregion

        #region Get<T>
        async public static ValueTask<T> Get<T>(string url, Encoding encoding = default, string cookie = null, string referer = null, long MaxResponseContentBufferSize = 0, int timeoutSeconds = 15, List<(string name, string val)> addHeaders = null, bool IgnoreDeserializeObject = false, WebProxy proxy = null)
        {
            try
            {
                string html = (await BaseGetAsync(url, encoding, cookie: cookie, referer: referer, MaxResponseContentBufferSize: MaxResponseContentBufferSize, timeoutSeconds: timeoutSeconds, addHeaders: addHeaders, proxy: proxy)).content;
                if (html == null)
                    return default;

                if (IgnoreDeserializeObject)
                    return JsonConvert.DeserializeObject<T>(html, new JsonSerializerSettings { Error = (se, ev) => { ev.ErrorContext.Handled = true; } });

                return JsonConvert.DeserializeObject<T>(html);
            }
            catch
            {
                return default;
            }
        }
        #endregion

        #region BaseGetAsync
        async public static ValueTask<(string content, HttpResponseMessage response)> BaseGetAsync(string url, Encoding encoding = default, string cookie = null, string referer = null, int timeoutSeconds = 15, long MaxResponseContentBufferSize = 0, List<(string name, string val)> addHeaders = null, WebProxy proxy = null, int httpversion = 1)
        {
            string loglines = string.Empty;

            try
            {
                using (var client = new System.Net.Http.HttpClient(Handler(url, proxy)))
                {
                    DefaultRequestHeaders(client, timeoutSeconds, MaxResponseContentBufferSize, cookie, referer, addHeaders);

                    var req = new HttpRequestMessage(HttpMethod.Get, url)
                    {
                        Version = new Version(httpversion, 0)
                    };

                    using (HttpResponseMessage response = await client.SendAsync(req))
                    {
                        loglines += $"StatusCode: {(int)response.StatusCode}";

                        if (response.StatusCode != HttpStatusCode.OK)
                            return (null, response);

                        using (HttpContent content = response.Content)
                        {
                            if (encoding != default)
                            {
                                string res = encoding.GetString(await content.ReadAsByteArrayAsync());
                                if (string.IsNullOrWhiteSpace(res))
                                    return (null, response);

                                loglines += "\n\n" + res;
                                return (res, response);
                            }
                            else
                            {
                                string res = await content.ReadAsStringAsync();
                                if (string.IsNullOrWhiteSpace(res))
                                    return (null, response);

                                loglines += "\n\n" + res;
                                return (res, response);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                loglines = ex.ToString();

                return (null, new HttpResponseMessage()
                {
                    StatusCode = HttpStatusCode.InternalServerError,
                    RequestMessage = new HttpRequestMessage()
                });
            }
            finally
            {
                await WriteLog(url, "GET", null, loglines);
            }
        }
        #endregion


        #region Post
        public static ValueTask<string> Post(string url, string data, string cookie = null, int MaxResponseContentBufferSize = 0, int timeoutSeconds = 15, List<(string name, string val)> addHeaders = null, WebProxy proxy = null, int httpversion = 1)
        {
            return Post(url, new StringContent(data, Encoding.UTF8, "application/x-www-form-urlencoded"), cookie: cookie, MaxResponseContentBufferSize: MaxResponseContentBufferSize, timeoutSeconds: timeoutSeconds, addHeaders: addHeaders, proxy: proxy, httpversion: httpversion);
        }

        async public static ValueTask<string> Post(string url, HttpContent data, Encoding encoding = default, string cookie = null, int MaxResponseContentBufferSize = 0, int timeoutSeconds = 15, List<(string name, string val)> addHeaders = null, WebProxy proxy = null, int httpversion = 1)
        {
            string loglines = string.Empty;

            try
            {
                using (var client = new System.Net.Http.HttpClient(Handler(url, proxy)))
                {
                    DefaultRequestHeaders(client, timeoutSeconds, MaxResponseContentBufferSize, cookie, null, addHeaders);

                    var req = new HttpRequestMessage(HttpMethod.Post, url)
                    {
                        Version = new Version(httpversion, 0),
                        Content = data
                    };

                    using (HttpResponseMessage response = await client.SendAsync(req))
                    {
                        loglines += $"StatusCode: {(int)response.StatusCode}";

                        if (response.StatusCode != HttpStatusCode.OK)
                            return null;

                        using (HttpContent content = response.Content)
                        {
                            if (encoding != default)
                            {
                                string res = encoding.GetString(await content.ReadAsByteArrayAsync());
                                if (string.IsNullOrWhiteSpace(res))
                                    return null;

                                loglines += "\n\n" + res;
                                return res;
                            }
                            else
                            {
                                string res = await content.ReadAsStringAsync();
                                if (string.IsNullOrWhiteSpace(res))
                                    return null;

                                loglines += "\n\n" + res;
                                return res;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                loglines = ex.ToString();
                return null;
            }
            finally
            {
                await WriteLog(url, "POST", data.ReadAsStringAsync().Result, loglines);
            }
        }
        #endregion

        #region Post<T>
        async public static ValueTask<T> Post<T>(string url, string data, string cookie = null, int timeoutSeconds = 15, List<(string name, string val)> addHeaders = null, Encoding encoding = default, WebProxy proxy = null, bool IgnoreDeserializeObject = false)
        {
            return await Post<T>(url, new StringContent(data, Encoding.UTF8, "application/x-www-form-urlencoded"), cookie: cookie, timeoutSeconds: timeoutSeconds, addHeaders: addHeaders, encoding: encoding, proxy: proxy, IgnoreDeserializeObject: IgnoreDeserializeObject);
        }

        async public static ValueTask<T> Post<T>(string url, HttpContent data, string cookie = null, int timeoutSeconds = 15, List<(string name, string val)> addHeaders = null, Encoding encoding = default, WebProxy proxy = null, bool IgnoreDeserializeObject = false)
        {
            try
            {
                string json = await Post(url, data, cookie: cookie, timeoutSeconds: timeoutSeconds, addHeaders: addHeaders, encoding: encoding, proxy: proxy);
                if (json == null)
                    return default;

                if (IgnoreDeserializeObject)
                    return JsonConvert.DeserializeObject<T>(json, new JsonSerializerSettings { Error = (se, ev) => { ev.ErrorContext.Handled = true; } });

                return JsonConvert.DeserializeObject<T>(json);
            }
            catch
            {
                return default;
            }
        }
        #endregion


        #region Download
        async public static ValueTask<byte[]> Download(string url, string cookie = null, string referer = null, int timeoutSeconds = 20, long MaxResponseContentBufferSize = 0, List<(string name, string val)> addHeaders = null, WebProxy proxy = null)
        {
            try
            {
                var handler = Handler(url, proxy);
                handler.AllowAutoRedirect = true;

                using (var client = new System.Net.Http.HttpClient(handler))
                {
                    DefaultRequestHeaders(client, timeoutSeconds, MaxResponseContentBufferSize, cookie, referer, addHeaders);

                    using (HttpResponseMessage response = await client.GetAsync(url))
                    {
                        if (response.StatusCode != HttpStatusCode.OK)
                            return null;

                        using (HttpContent content = response.Content)
                        {
                            byte[] res = await content.ReadAsByteArrayAsync();
                            if (res.Length == 0)
                                return null;

                            return res;
                        }
                    }
                }
            }
            catch
            {
                return null;
            }
        }
        #endregion

        #region DownloadFile
        async public static ValueTask<bool> DownloadFile(string url, string path, int timeoutSeconds = 20, List<(string name, string val)> addHeaders = null, WebProxy proxy = null)
        {
            try
            {
                var handler = Handler(url, proxy);
                handler.AllowAutoRedirect = true;

                using (var client = new System.Net.Http.HttpClient(handler))
                {
                    DefaultRequestHeaders(client, timeoutSeconds, -1, null, null, addHeaders);

                    using (var stream = await client.GetStreamAsync(url))
                    {
                        using (var fileStream = new FileStream(path, FileMode.OpenOrCreate))
                        {
                            await stream.CopyToAsync(fileStream);
                            return true;
                        }
                    }
                }
            }
            catch
            {
                return false;
            }
        }
        #endregion


        #region WriteLog
        static FileStream logFileStream = null;

        async static Task WriteLog(string url, string method, string postdata, string result)
        {
            if (!AppInit.conf.log || url.Contains("127.0.0.1"))
                return;

            string dateLog = DateTime.Today.ToString("dd.MM.yy");
            string patchlog = $"cache/logs/HttpClient_{dateLog}.log";

            if (logFileStream == null || !File.Exists(patchlog))
                logFileStream = new FileStream(patchlog, FileMode.Append, FileAccess.Write);

            string log = $"{DateTime.Now}\n{method}: {url}\n";

            if (!string.IsNullOrWhiteSpace(postdata))
                log += $"{postdata}\n";

            log += result;

            string splitline = "################################################################";
            var buffer = Encoding.UTF8.GetBytes($"\n\n\n{splitline}\n\n{log}");

            await logFileStream.WriteAsync(buffer, 0, buffer.Length);
            await logFileStream.FlushAsync();
        }
        #endregion
    }
}
