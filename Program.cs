using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using Newtonsoft.Json;
using Keras.Models;
using Keras.Layers;
using Keras.Optimizers;
using Numpy;
using RestSharp;
using System.Configuration;
namespace FinancialMachineLearning
{
    class Program
    {

        public static List<string> stocks = new List<string> { "AAPL", "JNJ", "XOM", "F" };
        public static void gatherTradingData()
        {
            foreach (string stock in stocks)
            {
                RestRequest request = new RestRequest("api/v1/history", Method.GET, DataFormat.Json);
                RestClient client = new RestClient("https://api.worldtradingdata.com");
                request.AddQueryParameter("symbol", stock);
                request.AddQueryParameter("sort", "newest");
                request.AddQueryParameter("api_token", ConfigurationManager.AppSettings["wtdkey"]);
                RestResponse response = (RestResponse)client.Execute(request);
                File.WriteAllText(String.Format(@"C:/RAR/Json/{0}.json", stock), response.Content);
            }
        }
        public static int lstmSize = 100;
        public static int lstmStep = 10;
        public static int lstmForward = 20;
        public static Tuple<NDarray,NDarray> constructNumpyArrays()
        {
            List<DateTime> dates = new List<DateTime>();
            Dictionary<DateTime, dynamic>[] rawData = new Dictionary<DateTime, dynamic>[stocks.Count];
            for(int x = 0; x< stocks.Count; x++)
            {
                Dictionary<DateTime, dynamic> dat = JsonConvert.DeserializeObject<Dictionary<DateTime, dynamic>>(
                    JsonConvert.SerializeObject(
                    JsonConvert.DeserializeObject<dynamic>(
                        (new StreamReader(
                        File.OpenRead(String.Format(@"C:/RAR/Json/{0}.json",stocks[x])))).ReadToEnd())
                            .history));
                rawData[x] = dat;
                foreach(DateTime key in dat.Keys)
                {
                    if (!dates.Contains(key))
                    {
                        dates.Add(key);
                    }
                }

            }
            List<DateTime> filteredDates = new List<DateTime>();
            foreach (DateTime date in dates)
            {
                if (rawData.All(dat=>dat.Keys.Contains(date)))
                {
                    filteredDates.Add(date);
                }
            }
            filteredDates.Sort();
            int dateCount = filteredDates.Count;
            float[,] numpy = new float[dateCount,stocks.Count*5];
            for (int x = 0; x < dateCount - 1; x++)
            {
                for (int y = 0; y < stocks.Count; y++)
                {
                    int i = y * 5;
                    numpy[x, i] = 1+((float)Convert.ToDouble(rawData[y][filteredDates[x+1]].open) - (float)Convert.ToDouble(rawData[y][filteredDates[x]].open))/ (float)Convert.ToDouble(rawData[y][filteredDates[x]].open);
                    numpy[x, i + 1] = 1+((float)Convert.ToDouble(rawData[y][filteredDates[x+1]].close)- (float)Convert.ToDouble(rawData[y][filteredDates[x]].close))/ (float)Convert.ToDouble(rawData[y][filteredDates[x]].close);
                    numpy[x, i + 2] = 1+((float)Convert.ToDouble(rawData[y][filteredDates[x+1]].high)- (float)Convert.ToDouble(rawData[y][filteredDates[x]].high))/(float)Convert.ToDouble(rawData[y][filteredDates[x]].high);
                    numpy[x, i + 3] = 1+((float)Convert.ToDouble(rawData[y][filteredDates[x+1]].low)- (float)Convert.ToDouble(rawData[y][filteredDates[x]].low))/ (float)Convert.ToDouble(rawData[y][filteredDates[x]].low);
                    numpy[x, i + 4] = (float)Convert.ToDouble(rawData[y][filteredDates[x + 1]].volume);
                }
            }
            int arsz = Convert.ToInt32(Math.Floor((decimal)(filteredDates.Count - lstmSize - lstmForward - 1) / lstmStep));
            var arrO = new NDarray[arsz];
            var arrU = new NDarray[arsz];
            for (int x = 0; x < filteredDates.Count - lstmSize - lstmForward - 1; x += lstmStep)
            {
                float[,] step = new float[lstmSize, stocks.Count * 5];
                for (int y = 0; y < lstmSize; y++)
                {
                    for (int z = 0; z < stocks.Count * 5; z++)
                    {
                        step[y, z] = numpy[x + y, z];
                    }
                }
                float[,] fStep = new float[lstmForward, stocks.Count];
                for (int y = 0; y < lstmForward; y++)
                {
                    for (int z = 0; z < stocks.Count * 5; z += 5)
                    {
                        fStep[y, z / 5] = numpy[x + lstmSize + y, z];
                    }
                }
                var f2Step = np.array(fStep);
                var f3Step = np.average(f2Step, axis: new int[] { 0 });
                if (x / lstmStep < arsz)
                {
                    arrU[x / lstmStep] = f3Step;
                    arrO[x / lstmStep] = np.array(step);
                }
            }
            return Tuple.Create(np.array(arrO),np.array(arrU));
        }

        public static Sequential constructModel(NDarray array)
        {
            var model = new Sequential();
            model.Add(new LSTM(lstmSize,return_sequences:true));
            model.Add(new LSTM(lstmSize, return_sequences: true));
            model.Add(new LSTM(lstmSize));
            model.Add(new Dense(stocks.Count));
            return model;
        }
        public static Sequential compileModel(Sequential model)
        {
            model.Compile(optimizer: new Adam(), "mean_squared_error", metrics: new string[] { "mse", "mape", "acc" });
            return model;
        }
        public static Sequential fitModel(Sequential model, Tuple<NDarray,NDarray>tup)
        {
            model.Fit(tup.Item1, tup.Item2, batch_size: 1, epochs: 10);
            return model;
        }
        static void Main(string[] args)
        {
            //gatherTradingData();
            Tuple<NDarray, NDarray> tup = constructNumpyArrays();
            Sequential trained = fitModel(compileModel(constructModel(tup.Item1)),tup);
            Console.ReadLine();
            //Dictionary<DateTime,dynamic> xom = JsonConvert.DeserializeObject<Dictionary<DateTime,dynamic>>(
            //    JsonConvert.SerializeObject(
            //    JsonConvert.DeserializeObject<dynamic>((new StreamReader(File.OpenRead(@"C:/RAR/Json/XOM.json"))).ReadToEnd()).history));
            
            Console.ReadLine();
        }
    }
}
