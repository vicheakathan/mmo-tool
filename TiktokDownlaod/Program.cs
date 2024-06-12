using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using System.Xml;
using System.Xml.Schema;
using HtmlAgilityPack;
using Newtonsoft.Json.Linq;

namespace SnaptikDownloader
{
    class Program
    {
        private const string host = "https://snaptik.app";
        private static readonly HttpClient client = new HttpClient();
        private static readonly Regex urlMobileRegex = new Regex(@"(?:^http[s]\:\/\/vt\.tiktok\.com\/(?<id>[\w+]*))");
        private static readonly Regex urlWebRegex = new Regex(@"^https:\/\/www\.tiktok\.com\/@[^\/]+\/video\/\d+");
        private static readonly Regex usernameRegex = new Regex(@"^(?:@)?([a-zA-Z0-9_\.]{2,24})$");
        private static readonly string outputDirectory = @"downloadedVideos";

        static async Task Main(string[] args)
        {
            #region
            Console.ForegroundColor = ConsoleColor.DarkGreen;
            Console.WriteLine(@" _____ _ _    _        _     ______                    _                 _ 
|_   _(_) |  | |      | |    |  _  \                  | |               | |
  | |  _| | _| |_ ___ | | __ | | | |_____      ___ __ | | ___   __ _  __| |
  | | | | |/ / __/ _ \| |/ / | | | / _ \ \ /\ / / '_ \| |/ _ \ / _` |/ _` |
  | | | |   <| || (_) |   <  | |/ / (_) \ V  V /| | | | | (_) | (_| | (_| |
  \_/ |_|_|\_\\__\___/|_|\_\ |___/ \___/ \_/\_/ |_| |_|_|\___/ \__,_|\__,_|
                                                                           
                                                                           
");
            Console.ForegroundColor = ConsoleColor.White;
            #endregion
            Thread.Sleep(1000);

            List<string> urls = await ReadUrlsFromFile("link.txt");
            int videoCount = 1;
            if (urls != null && urls.Any())
            {
                Console.WriteLine($"Total download videos: {urls.Count()}");
                Thread.Sleep(1000);

                foreach (var url in urls)
                {
                    if (Regex.Match(url, @"https:\/\/www\.tiktok\.com\/.+").Success)
                    {
                        Console.ForegroundColor = ConsoleColor.DarkYellow;
                        Console.WriteLine($"Starting download {videoCount} of {urls.Count()} videos");
                        Console.ForegroundColor = ConsoleColor.White;

                        try
                        {
                            await ProcessURL(url.ToString());
                        }
                        catch (Exception ex)
                        {
                            Console.ForegroundColor = ConsoleColor.DarkRed;
                            Console.WriteLine($"Error downloading video: {ex.Message}. Skipping...");
                            Console.ForegroundColor = ConsoleColor.White;
                            continue; // Skip to the next URL
                        }

                        videoCount++;
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.DarkRed;
                        Console.WriteLine($"Invalid URL: {url}. Skipping...");
                        Console.ForegroundColor = ConsoleColor.White;
                    }
                }
            }
            else
            {
                Console.WriteLine("No valid URLs found in the 'link.txt' file.");
            }

            await File.WriteAllTextAsync("link.txt", string.Empty);
        }

        static async Task<List<string>> ReadUrlsFromFile(string filePath)
        {
            List<string> urls = new List<string>();

            try
            {
                using (StreamReader sr = new StreamReader(filePath))
                {
                    string line;
                    while ((line = await sr.ReadLineAsync()) != null)
                    {
                        urls.Add(line.Trim());
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading URLs from file: {ex.Message}");
                return null;
            }

            return urls;
        }

        static async Task ProcessURL(string url)
        {
            var options = new Options()
            {
                url = url,
                render = true
            };

            var res = await GetVideoData(options);

            if (res.status && res.data != null)
            {
                await DownloadFileAsync(res.data);
                
                Console.ForegroundColor = ConsoleColor.DarkGreen;
                Console.WriteLine("Video downloaded successfully.\n");
                Console.ForegroundColor = ConsoleColor.White;
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.DarkRed;
                Console.WriteLine(res.message);
                Console.ForegroundColor = ConsoleColor.White;
            }
        }

        static async Task DownloadFileAsync(string url)
        {
            try
            {
                if (!Directory.Exists(outputDirectory))
                {
                    Directory.CreateDirectory(outputDirectory);
                }

                string outputPath = outputDirectory + "/" + DateTime.Now.ToString("yyyyMMddHHmmss") + ".mp4";
                var response = await client.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    var fileStream = new FileStream(outputPath, FileMode.Create);
                    await response.Content.CopyToAsync(fileStream);
                }
                else
                {
                    Console.WriteLine($"Failed to download video. Status code: {response.Content}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error while downloading... ", ex.Message);
            }
        }

        static async Task<Response> GetVideoData(Options opts)
        {
            Response response = new Response();

            try
            {
                var url = (string)opts.url;
                var render = (bool)opts.render;
                var validate = IsTiktokUrl(url);

                if (validate.status)
                {
                    var token = await GetToken(opts);
                    if (token.status)
                    {
                        var form = new MultipartFormDataContent();
                        form.Add(new StringContent(url), "url");
                        form.Add(new StringContent(token.token), "token");

                        var page = await client.PostAsync($"{host}/abc2.php", form);
                        var responseContent = await page.Content.ReadAsStringAsync();

                        var innerHtml = GetInnerHtml(responseContent);

                        if (innerHtml.status)
                        {
                            HtmlDocument doc = new HtmlDocument();
                            doc.LoadHtml(innerHtml.result);
                            var firstATag = doc.DocumentNode.SelectSingleNode("//a");
                            string videoUrl = firstATag.GetAttributeValue("href", "");

                            //var info = await GetWebpageInfo(url);

                            //Console.ForegroundColor = ConsoleColor.DarkCyan;
                            //Console.WriteLine("===================================================");
                            //Console.WriteLine($"ID: {info.id}");
                            //Console.WriteLine($"Description: {info.desc}");
                            //Console.WriteLine($"Size: {info.size} MB");
                            //Console.WriteLine("===================================================");
                            //Console.ForegroundColor = ConsoleColor.White;

                            response.data = videoUrl;
                            response.status = innerHtml.status;

                            return response;
                        }
                        else
                        {
                            response.status = innerHtml.status;
                            response.message = innerHtml.message;

                            return response;
                        }
                    }
                    else
                    {
                        response.message = token.token;

                        return response;
                    }
                }
                else
                {
                    response.message = validate.message;

                    return response;
                }
            }
            catch (Exception ex1)
            {
                response.message = ex1.Message;

                return response;
            }
        }

        static (bool status, string result, string message) GetInnerHtml(string html)
        {
            if (html == null)
            {
                return (false, "", "no supplied obfuscated html data");
            }

            var paramsMatch = new Regex(
                @"\(""(.*?)"",(.*?),""(.*?)"",(.*?),(.*?),(.*?)\)",
                RegexOptions.IgnoreCase
            ).Match(html);

            if (paramsMatch.Success) {
                var parameters = paramsMatch.Groups.Cast<Group>().Skip(1).Select<Group, object>(g =>
                {
                    var value = g.Value;
                    if (int.TryParse(value, out int num))
                    {
                        return num;
                    }
                    return value;
                }).ToArray();

                var h = (string)parameters[0];
                var u = (string)parameters[1].ToString();
                var n = ((string)parameters[2]).ToCharArray(); // Convert string to char array
                var t = (int)parameters[3];
                var e = (int)parameters[4];
                var r = (string)parameters[5].ToString();

                string alpa = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ+/";

                string Decode(string d, int ee, int f)
                {
                    var g = alpa.ToCharArray();
                    var h = g.Take(ee).ToArray();
                    var i = g.Take(f).ToArray();
                    var j = d.Reverse().Select((b, c) => h.Contains(b) ? h.ToList().IndexOf(b) * Math.Pow(ee, c) : 0).Sum();
                    var k = "";
                    while (j > 0)
                    {
                        k = i[(int)(j % f)] + k;
                        j = (j - (j % f)) / f;
                    }
                    return k;
                }

                r = "";
                for (int i = 0; i < h.Length; i++)
                {
                    var s = "";
                    while (h[i] != n[e])
                    {
                        s += h[i];
                        i++;
                    }
                    foreach (var j in n)
                    {
                        s = s.Replace(j.ToString(), Array.IndexOf(n, j).ToString());
                    }
                    r += (char)(Convert.ToInt32(Decode(s, e, 10)) - t);
                }

                var contentMatch = new Regex(@"\.innerHTML\s=\s""([^>*].+?)"";\s").Match(Uri.UnescapeDataString(r));

                if (contentMatch.Success)
                {
                    return (true, contentMatch.Groups[1].Value.Replace("\\", ""), "");
                }
                else
                {
                    return (false, "", "couldn't deobfuscate html data...");
                }
            }
            else
            {
                return (false, "", "malformed html obfuscated data...");
            }
        }

        public static (bool status, string message) IsTiktokUrl(string url)
        {
            if (!string.IsNullOrEmpty(url))
            {
                if (urlMobileRegex.IsMatch(url) || urlWebRegex.IsMatch(url))
                {
                    return (true, null);
                }
                return (false, "Invalid TikTok video URL!");
            }
            return (false, "No supplied URL...");
        }

        static async Task<(bool status, string token)> GetToken(Options opts)
        {
            //string host = opts.host;
            HttpResponseMessage response;
            string page;

            response = await client.GetAsync(host);
            page = await response.Content.ReadAsStringAsync();

            Regex tokenRegex = host.Contains("ssstik") ?
                new Regex(@"(?:""tt\:'([\w]*?)'"";|s_tt\s=\s'([\w]*?)')", RegexOptions.IgnoreCase) :
                new Regex(@"name\=\""_?token\""\svalue\=\""([^>*]+?)""", RegexOptions.IgnoreCase);

            Match tokenMatch = tokenRegex.Match(page);
            if (tokenMatch.Success)
            {
                string token = tokenMatch.Groups[1].Success ? tokenMatch.Groups[1].Value : tokenMatch.Groups[2].Value;
                return (true, token);
            }
            else
            {
                Console.WriteLine($"Couldn't get token from::{host}");
                return (false, "regex not match with token form!");
            }
        }

        static async Task<WebPageInfo> GetWebpageInfo(string url)
        {
            WebPageInfo info = new WebPageInfo();
            try
            {
                string page;
                try
                {
                    page = await client.GetStringAsync(url);
                }
                catch (HttpRequestException ex)
                {
                    info.message = ex.Message;
                    info.status = false;

                    return info;
                }

                var univDataMatch = Regex.Match(page, "(?<=script\\sid\\=\"__UNIVERSAL_DATA_FOR_REHYDRATION__\"[^>+]*?\">)([^>+].*?)(?=</script>)");

                if (univDataMatch.Success)
                {
                    var univData = JObject.Parse(univDataMatch.Groups[1].Value)["__DEFAULT_SCOPE__"]["webapp.video-detail"];

                    if (univData != null)
                    {
                        if (univData.Value<int>("statusCode") == 0)
                        {
                            var itemStruct = univData["itemInfo"]["itemStruct"];

                            info.status = true;
                            info.id = itemStruct["id"].Value<string>();
                            info.desc = itemStruct["imagePost"] != null ? $"{itemStruct["imagePost"]["title"]} {itemStruct["desc"]}" : itemStruct["desc"].Value<string>();

                            long bytes = 0;
                            var responseJson = JObject.Parse(itemStruct["video"].ToString());
                            if (responseJson["video"] != null)
                            {
                                bytes = itemStruct["video"]["bitrateInfo"][0]["PlayAddr"]["DataSize"].Value<long>();
                            }
                            double x = Math.Round(bytes / 1024.0 / 1024.0, 2);

                            info.size = x;

                            return info;
                        }
                        else
                        {
                            info.status = false;
                            info.message = "Error while parsing data";

                            return info;
                        }
                    }
                    else
                    {
                        info.status = false;
                        info.message = "Couldn't find video details!";

                        return info;
                    }
                }
                else
                {
                    info.status = false;
                    info.message = "Couldn't find video Universal data!";

                    return info;
                }
            }
            catch (Exception ex)
            {
                info.status = false;
                info.message = ex.Message;

                return info;
            }
        }
    }

    class Options
    {
        public string url { get; set; }

        public string host { get; set;}

        public bool render { get; set;}
    }

    class Response
    {
        public bool status { get; set; }

        public string message { get; set; }

        public string data { get; set; }
    }

    class WebPageInfo
    {
        public bool status { get; set; }

        public string message { get; set; }

        public string desc { get; set; }

        public string id { get; set; }

        public double size { get; set; }
    }
}
