using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.Http;
using System.Net;
using System.Configuration;
using System.IO;
using System.IO.Compression;
using Newtonsoft.Json;
using SimbaForex2.Models.OandaModel;

namespace SimbaForex2
{
    public static class OandaConn
    {
        static string setting;// = ConfigurationManager.AppSettings["Setting"].ToString();
        static string accountId;//  = ConfigurationManager.AppSettings[setting + "AccountId"].ToString();
        static string accessToken;//  = ConfigurationManager.AppSettings[setting + "AcccessToken"].ToString();
        static string url;//  = ConfigurationManager.AppSettings[setting + "URL"].ToString();
        static string v3Accounts = "/v3/accounts/";
        static string v3Instruments = "/v3/instruments/";
        static object locker = new object();
        static HttpWebResponse response;
        static object MakeRequest(string url, object[] parameters)
        {
            ServicePointManager.DefaultConnectionLimit = 20;
            lock (locker)
            {
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = parameters[0].ToString();
                request.Headers[HttpRequestHeader.Authorization] = "Bearer " + parameters[1].ToString();
                request.Headers[HttpRequestHeader.AcceptEncoding] = "gzip, deflate";
                request.ContentType = "application/json";
                try
                {
                    if (parameters[0].ToString().Equals("GET"))
                    {
                        response = (HttpWebResponse)request.GetResponse();
                        {
                            Stream stream = response.GetResponseStream();
                            if (response.Headers.Keys.Cast<string>().ToList().Contains("Content-Encoding"))
                            {
                                if (response.Headers["Content-Encoding"].Equals("gzip"))
                                {
                                    stream = new GZipStream(stream, CompressionMode.Decompress);
                                }
                                if (response.Headers["Content-Encoding"].Equals("deflate"))
                                {
                                    stream = new DeflateStream(stream, CompressionMode.Decompress);
                                }
                            }
                            return stream;
                        }
                    }
                    else
                    {
                        var encoding = new ASCIIEncoding();
                        if (parameters[0].ToString().Equals("POST"))
                        {
                            var bytes = encoding.GetBytes(parameters[2].ToString());
                            request.ContentLength = Convert.ToInt64(bytes.Length);
                            using (var putStream = request.GetRequestStream())
                            {
                                putStream.Write(bytes, 0, bytes.Length);
                                response = (HttpWebResponse)request.GetResponse();
                            }
                        }
                        else
                        {
                            response = (HttpWebResponse)request.GetResponse();
                        }
                        return true;
                    }
                }
                catch (Exception e)
                {
                    using (SimbaForex2.Models.Concrete.SimbaForex2Context context = new Models.Concrete.SimbaForex2Context())
                    {
                        context.HttpExceptionLogs.Add(new Models.HttpExceptionLog
                        {
                            DateEntered = DateTime.Now,
                            ExceptionMessage = e.Message
                        });
                        context.SaveChanges();
                    }
                    return false;
                }
            }
        }
        static string FormatDate(DateTime dte)
        {
            dte = dte.AddHours(1);
            return dte.Year.ToString() + "-" + dte.Month.ToString() + "-" + dte.Day.ToString() + "T" + dte.Hour.ToString() + "%3A" + dte.Minute.ToString() + "%3A" + dte.Second.ToString() + "Z";
        }
        static T JsonResponse<T>(Stream stream)
        {
            StreamReader reader;
            if (stream is GZipStream)
            {
                reader = new StreamReader(stream as GZipStream);
            }
            else if (stream is DeflateStream)
            {
                reader = new StreamReader(stream as DeflateStream);
            }
            else
            {
                reader = new StreamReader(stream);
            }
            var json = reader.ReadToEnd();
            var result = JsonConvert.DeserializeObject<T>(json);
            response.Close();
            return result;
        }
        static object Prices(string instrument)
        {
            var request = url + v3Accounts + accountId + "/pricing?instruments=" + instrument + "%2CUSD_CAD";
            var response = MakeRequest(request, new object[] { "GET" });
            bool success = true;
            if (Boolean.TryParse(response.ToString(), out success))
                return null;
            return JsonResponse<dynamic>(response as Stream);
        }
        public static CandlesResponse HistoricalCandlesData(Tuple<string, string, string> args, string instrument, int count, string granularity, DateTime start)
        {
            string url = args.Item1;
            string accountId = args.Item2;
            string accessToken = args.Item3;
            var request = url + v3Instruments + instrument + "/candles?count=" + count.ToString() + "&granularity=" + granularity + "&price=M&from=" + FormatDate(start);
            var response = MakeRequest(request, new object[] { "GET", accessToken });
            bool success = true;
            if (Boolean.TryParse(response.ToString(), out success))
                return null;
            return JsonResponse<CandlesResponse>(response as Stream);
        }
        public static CandlesResponse HistoricalDataWeek(Tuple<string, string, string> args, string instrument, DateTime start)
        {
            string url = args.Item1;
            string accountId = args.Item2;
            string accessToken = args.Item3;
            var request = url + v3Instruments + instrument + "/candles?count=3600&granularity=M2&price=M&from=" + FormatDate(start);
            var response = MakeRequest(request, new object[] { "GET", accessToken });
            bool success = true;
            if (Boolean.TryParse(response.ToString(), out success))
                return null;
            return JsonResponse<CandlesResponse>(response as Stream);
        }
            public static CandlesResponse CandlesData(Tuple<string, string, string> args, string instrument, int count, string granularity)
        {
            string url = args.Item1;
            string accountId = args.Item2;
            string accessToken = args.Item3;
            var request = url + v3Instruments + instrument + "/candles?count=" + count.ToString() + "&granularity=" + granularity + "&price=M";
            var response = MakeRequest(request, new object[] { "GET", accessToken });
            bool success = true;
            if (Boolean.TryParse(response.ToString(), out success))
                return null;
            return JsonResponse<CandlesResponse>(response as Stream);
        }

        static public AccountResponse AccountSummary(Tuple<string, string, string> args)
        {
            string url = args.Item1;
            string accountId = args.Item2;
            string accessToken = args.Item3;
            var request = url + v3Accounts + accountId + "/summary";
            var response = MakeRequest(request, new object[] { "GET", accessToken });
            bool success = true;
            if (Boolean.TryParse(response.ToString(), out success))
                return null;
            return JsonResponse<AccountResponse>(response as Stream);
        }
        public static AccountResponse AccountDetails(Tuple<string, string, string> args)
        {
            string url = args.Item1;
            string accountId = args.Item2;
            string accessToken = args.Item3;
            var request = url + v3Accounts + accountId;
            var response = MakeRequest(request, new object[] { "GET", accessToken });
            bool success = true;
            if (Boolean.TryParse(response.ToString(), out success))
                return null;
            return JsonResponse<AccountResponse>(response as Stream);
        }


        public static void LimitOrder(Tuple<string, string, string> args, string instrument, bool longShort, int units, decimal price)
        {
            price = (instrument.Contains("JPY") || instrument.Contains("SEK") || instrument.Contains("TRY") ? Math.Round(price, 2) : Math.Round(price, 5));
            string url = args.Item1;
            string accountId = args.Item2;
            string accessToken = args.Item3;
            var request = url + "/v3/accounts/" + accountId + "/orders";
            var reqBody = "{\"order\":{\"price\": \""
                + price.ToString()
                + "\",\"type\":\"LIMIT\",\"instrument\":\""
                + instrument
                + "\",\"units\":\""
                + ((longShort ? 1 : -1) * units).ToString()
                + "\",\"timeInForce\":\"GTC\",\"positionFill\":\"DEFAULT\"}}";
            var responseX = MakeRequest(request, new object[] { "POST", accessToken, reqBody });
            response.Close();
        }
        static void StopOrder(string instrument, bool longShort, int units, decimal price)
        {
            var request = url + "/v3/accounts/" + accountId + "/orders";
            var reqBody = "{\"order\":{\"price\": \""
                + price.ToString()
                + "\",\"type\":\"MARKET_IF_TOUCHED\",\"instrument\":\""
                + instrument
                + "\",\"units\":\""
                + units.ToString()
                + "\",\"timeInForce\":\"GTC\",\"positionFill\":\"DEFAULT\"}}";
            var responseX = MakeRequest(request, new object[] { "POST", reqBody });
            response.Close();
        }
        public static void MarketOrder(Tuple<string, string, string> args, string instrumentX, bool longshort, int units)
        {
            object o = new { order = new { units = units * (longshort ? 1 : -1), instrument = instrumentX, timeInForce = "FOK", type = "MARKET", positionFill = "DEFAULT" }, units = units * (longshort ? 1 : -1), instrument = instrumentX };
            string order = JsonConvert.SerializeObject(o);
            //string json = String.Format("{\"order\": {\"units\": \""+units*(longshort? 1: -1)+"\",\"instrument\": \"{1}\",\"timeInForce\": \"FOK\",\"type\": \"MARKET\",\"positionFill\": \"DEFAULT\"}}", units * (longshort ? 1 : -1), instrument);
            var responseX = MakeRequest(args.Item1 + v3Accounts + args.Item2 + "/orders", new object[] { "POST", args.Item3, order });
            response.Close();
        }
        public static void CloseTrade(Tuple<string, string, string> args, long tradeId)
        {
            var responseX = MakeRequest(args.Item1 + v3Accounts + args.Item2 + "/trades/" + tradeId.ToString() + "/close",
                new object[] { "PUT", args.Item3 });
            response.Close();
        }
        public static void CancelOrder(Tuple<string, string, string> args, long orderId)
        {
            var responseX = MakeRequest(args.Item1 + v3Accounts + args.Item2 + "/orders/" + orderId.ToString() + "/cancel",
                new object[] { "PUT", args.Item3 });
            response.Close();
        }

        public static InstrumentsResponse GetInstruments(Tuple<string, string, string> args)
        {
            string url = args.Item1;
            string accountId = args.Item2;
            string accessToken = args.Item3;
            var request = url + v3Accounts + accountId + "/instruments";
            var response = MakeRequest(request, new object[] { "GET", accessToken });
            bool success = true;
            if (Boolean.TryParse(response.ToString(), out success))
                return null;
            return JsonResponse<InstrumentsResponse>(response as Stream);

        }
        public static List<object> NewsData(Tuple<string, string, string> args)
        {
            string url = "https://api-fxtrade.oanda.com/labs/v1/calendar?period=604800";
            string accessToken = args.Item3;
            var response = MakeRequest(url, new object[] { "GET", accessToken });
            return JsonResponse<List<object>>(response as Stream);
        }

    }
}
