// Today's weather for HP 娘's card. Where to look it up (设置 → 天气位置):
//   location — Windows location services (Wi-Fi based, not fooled by a VPN); sent rounded to 0.1° (~10 km)
//   ip       — the internet IP's location from ip.sb (the service MyDockFinder uses); a VPN changes it
//   manual   — a city picked by name (OpenStreetMap Nominatim search)
// Forecast: Open-Meteo. Place names: Nominatim reverse lookup. All free, no account or key.
using System;
using System.Net;
using System.Linq;
using System.Device.Location;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Web.Script.Serialization;

namespace HPControlPanel
{
    public class WeatherInfo
    {
        public string Place;            // 东京 / 新宿区 ... (may be null)
        public int Code = -1;           // WMO weather code
        public int Now, Min, Max;
        public string Error;            // shown instead when set
        public string Sky { get { return Weather.Describe(Code); } }   // 晴 / 多云 / Light rain ...
        public bool Rain { get { return Code >= 51 && Code <= 67 || Code >= 80 && Code <= 82 || Code >= 95; } }
        public bool Snow { get { return Code >= 71 && Code <= 77 || Code == 85 || Code == 86; } }
        public bool Clear { get { return Code == 0 || Code == 1; } }
        public string Text
        {
            get
            {
                if (Error != null) return L.S("天气：", "Weather: ") + Error;
                return (Place != null ? Place + " · " : "") + Sky + "  " + Min + "–" + Max + "°C  · " + L.S("现在 ", "now ") + Now + "°C";
            }
        }
    }

    public class Place
    {
        public string Name, Detail;     // "新宿", "新宿区 · 日本"
        public double Lat, Lon;
        public override string ToString() { return Detail.Length > 0 ? Name + L.S("（", " (") + Detail + L.S("）", ")") : Name; }
    }

    public static class Weather
    {
        const string UserAgent = "HPControlPanel/1.0 (personal desktop app)";   // required by Nominatim's usage policy
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        static Dictionary<string, object> GetJson(string url)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            using (var wc = new WebClient { Encoding = System.Text.Encoding.UTF8 })
            {
                wc.Headers[HttpRequestHeader.UserAgent] = UserAgent;
                wc.Headers[HttpRequestHeader.AcceptLanguage] = PlaceLanguage;
                var o = new JavaScriptSerializer().DeserializeObject(wc.DownloadString(url));
                return o as Dictionary<string, object> ?? new Dictionary<string, object> { { "list", o } };
            }
        }

        // Place names in the interface language where OpenStreetMap has them.
        static string PlaceLanguage { get { return L.En ? "en" : L.Tw ? "zh-TW,zh-Hant,zh" : "zh-CN,zh"; } }

        static string Str(Dictionary<string, object> d, string key)
        {
            object v; return d != null && d.TryGetValue(key, out v) && v != null ? Convert.ToString(v, Inv).Trim() : "";
        }

        public static WeatherInfo Fetch(Settings s)
        {
            double lat, lon;
            string place = null;
            try
            {
                if (s.WeatherMode == "manual")
                {
                    if (string.IsNullOrEmpty(s.WeatherPlace)) return new WeatherInfo { Error = L.S("请在设置里选择城市", "pick a city in Settings") };
                    lat = s.WeatherLat; lon = s.WeatherLon; place = s.WeatherPlace;
                }
                else if (s.WeatherMode == "ip")
                {
                    var ip = GetJson("https://api.ip.sb/geoip");
                    lat = Convert.ToDouble(ip["latitude"], Inv); lon = Convert.ToDouble(ip["longitude"], Inv);
                    place = ReverseName(lat, lon) ?? Str(ip, "city");
                }
                else
                {
                    using (var w = new GeoCoordinateWatcher(GeoPositionAccuracy.Default))
                    {
                        if (!w.TryStart(false, TimeSpan.FromSeconds(10)) || w.Permission == GeoPositionPermission.Denied)
                            return new WeatherInfo { Error = L.S("Windows 定位未开启", "Windows location is off") };
                        var until = DateTime.Now.AddSeconds(20);
                        while (w.Position.Location.IsUnknown && DateTime.Now < until) System.Threading.Thread.Sleep(500);
                        var loc = w.Position.Location;
                        if (loc.IsUnknown) return new WeatherInfo { Error = NoLocation };
                        lat = Math.Round(loc.Latitude, 1); lon = Math.Round(loc.Longitude, 1);
                    }
                    place = ReverseName(lat, lon);
                }
            }
            catch (Exception e) { App.Log(e); return new WeatherInfo { Error = NoLocation }; }

            try
            {
                string url = string.Format(Inv,
                    "https://api.open-meteo.com/v1/forecast?latitude={0:F2}&longitude={1:F2}&current=temperature_2m,weather_code" +
                    "&daily=temperature_2m_max,temperature_2m_min&timezone=auto&forecast_days=1", lat, lon);
                var root = GetJson(url);
                var cur = (Dictionary<string, object>)root["current"];
                var daily = (Dictionary<string, object>)root["daily"];
                Func<object, int> num = o => (int)Math.Round(Convert.ToDouble(o, Inv));
                return new WeatherInfo
                {
                    Place = string.IsNullOrEmpty(place) ? null : place,
                    Code = num(cur["weather_code"]),
                    Now = num(cur["temperature_2m"]),
                    Min = num(((IList)daily["temperature_2m_min"])[0]),
                    Max = num(((IList)daily["temperature_2m_max"])[0]),
                };
            }
            catch (Exception e) { App.Log(e); return new WeatherInfo { Error = L.S("暂时获取不到", "暫時取得不到", "unavailable right now") }; }
        }

        static string NoLocation { get { return L.S("暂时拿不到位置", "暫時取得不到位置", "location unavailable"); } }

        // City-level name for coordinates, in the interface language where OpenStreetMap has one.
        static string ReverseName(double lat, double lon)
        {
            try
            {
                var r = GetJson(string.Format(Inv, "https://nominatim.openstreetmap.org/reverse?format=jsonv2&zoom=10&accept-language={0}&lat={1:F2}&lon={2:F2}", Uri.EscapeDataString(PlaceLanguage), lat, lon));
                var a = r.ContainsKey("address") ? r["address"] as Dictionary<string, object> : null;
                foreach (var k in new[] { "city", "town", "county", "city_district", "state" })
                {
                    string v = Str(a, k);
                    if (v.Length > 0) return v;
                }
                return Str(r, "name").Length > 0 ? Str(r, "name") : null;
            }
            catch { return null; }
        }

        // City search for the manual mode. Only places (no stations or buildings).
        public static List<Place> Search(string query)
        {
            var list = new List<Place>();
            var r = GetJson("https://nominatim.openstreetmap.org/search?format=jsonv2&limit=8&addressdetails=1&layer=address&accept-language=" + Uri.EscapeDataString(PlaceLanguage) + "&q=" + Uri.EscapeDataString(query));
            foreach (Dictionary<string, object> item in (IEnumerable)r["list"])
            {
                var a = item.ContainsKey("address") ? item["address"] as Dictionary<string, object> : null;
                string name = Str(item, "name");
                if (name.Length == 0) continue;
                var detail = new List<string>();
                foreach (var k in new[] { "city", "county", "state", "country" })
                {
                    string v = Str(a, k);
                    if (v.Length > 0 && v != name && !detail.Contains(v)) detail.Add(v);
                }
                list.Add(new Place
                {
                    Name = name,
                    Detail = string.Join(" · ", detail),
                    Lat = Convert.ToDouble(item["lat"], Inv),
                    Lon = Convert.ToDouble(item["lon"], Inv),
                });
            }
            return list;
        }

        // WMO weather interpretation codes used by Open-Meteo.
        public static string Describe(int code)
        {
            if (code == 0) return L.S("晴", "Clear");
            if (code == 1) return L.S("晴间多云", "Mostly clear");
            if (code == 2) return L.S("多云", "Partly cloudy");
            if (code == 3) return L.S("阴", "Overcast");
            if (code == 45 || code == 48) return L.S("雾", "Fog");
            if (code >= 51 && code <= 57) return L.S("毛毛雨", "Drizzle");
            if (code == 61 || code == 80) return L.S("小雨", "Light rain");
            if (code == 63 || code == 81) return L.S("中雨", "Rain");
            if (code == 65 || code == 82) return L.S("大雨", "Heavy rain");
            if (code == 66 || code == 67) return L.S("冻雨", "Freezing rain");
            if (code >= 71 && code <= 77) return L.S("雪", "Snow");
            if (code == 85 || code == 86) return L.S("阵雪", "Snow showers");
            if (code >= 95) return L.S("雷雨", "Thunderstorm");
            return "—";
        }
    }
}
