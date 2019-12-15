using System;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;
using RestSharp;
using SimbaForceLibrary;
using Newtonsoft.Json;
using System.Net;
using System.IO;
using System.Collections.Generic;
using SimbaForceLibrary.Models;

namespace SimbaForceTriggers
{
    public static class StockTrigger
    {
        [FunctionName("StockTrigger")]
        public static void Run([TimerTrigger("0 0 0 * * 1-5")]TimerInfo myTimer, ILogger log)
        {
            string url = "https://mlexec.azurewebsites.net";
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(String.Format("{0}{1}?code={2}", url, "/api/stocksrecommendation", "eJI5RqgSR9u9PBlvPb6emwf0CsEs8/bkGmSu2s0gjdz635i7L2MmuQ=="));
            request.Timeout = 10000000;
            try
            {
                HttpWebResponse response = (HttpWebResponse)request.GetResponse();
                using (Stream stream = response.GetResponseStream())
                {
                    string content = new StreamReader(stream).ReadToEnd();
                    var record = content;
                    
                    var entry = new StockPredictionRecord
                    {
                        recordId = Convert.ToInt32(CosmosHelper.NextId("Records", "recordId")),
                        entryTime = DateTime.Now,
                        recordType = "StockPrediction",
                        record = record,
                        id = Guid.NewGuid().ToString()
                    };
                    var resp = CosmosHelper.InsertOrMerge(entry, "Records", id:entry.recordId.ToString()).Result;
                } 
            }
            catch (Exception e) 
            {
            }
        }
    }
}
