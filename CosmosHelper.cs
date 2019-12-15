using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using Microsoft.Azure.Cosmos;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SimbaForceLibrary.Models;

namespace SimbaForceLibrary
{
    public static class CosmosHelper
    {
        static string uri = "https://simbaforce.documents.azure.com:443/";
        static string key = "FQR2VLuyy8Fu7nk0KIFqzVnC4kLplCzukoPkCpmjC2TC3wCLrNwGW0NPNi4zvXHEykKH1iFsiPZQasHRu1f3xw==";
        static CosmosClient cosmosClient = new CosmosClient(uri, key);
        static Database database = cosmosClient.GetDatabase("SimbaForex");
        static async Task<dynamic> GetFirstResult(QueryDefinition queryDefinition, Container container)
        {
            FeedIterator<dynamic> feedIterator = container.GetItemQueryIterator<dynamic>(queryDefinition);
            List<dynamic> users = new List<dynamic>();
            while (feedIterator.HasMoreResults)
            {
                FeedResponse<dynamic> feedResponse = await feedIterator.ReadNextAsync();
                foreach (dynamic response in feedResponse)
                {
                    users.Add(response);
                }
            }
            return users[0] ?? null;
        }
        static async Task<List<T>> GetAllResults<T>(QueryDefinition queryDefinition, Container container)
        {
            FeedIterator<T> feedIterator = container.GetItemQueryIterator<T>(queryDefinition);
            List<T> items = new List<T>();
            while (feedIterator.HasMoreResults)
            {
                FeedResponse<T> feedResponse = await feedIterator.ReadNextAsync();
                foreach ( dynamic response in feedResponse)
                {
                    items.Add(response);
                }
            }
            return items;
        }
        public static dynamic GrabUserId(string userName, string userPassword)
        {
            Container container = database.GetContainer("UserCredentials");
            QueryDefinition queryDefinition = new QueryDefinition(
                String.Format("SELECT * FROM UC WHERE UC.salt = '{0}'",
                Convert.ToBase64String(Encoding.UTF8.GetBytes(userName))+
                Convert.ToBase64String(Encoding.UTF8.GetBytes(userPassword))));
            return GetFirstResult(queryDefinition, container);
        }
        public static dynamic GrabUser(string userId)
        {
            Container container = database.GetContainer("Users");
            QueryDefinition queryDefinition = new QueryDefinition(String.Format("SELECT * FROM U WHERE U.userId = '{0}'", userId));
            return GetFirstResult(queryDefinition, container);
        }
        public static List<TimerSetting> GrabTimerAccounts(string timerFrequency, string mode = null)
        {
            Container container = database.GetContainer("Timers");
            string query = String.Format("SELECT * FROM T WHERE T.frequency = '{0}' and T.timerSetting.enabled = 1", timerFrequency);
            if (mode != null)
            {
                query = String.Format("SELECT * FROM T WHERE T.frequency = '{0}' and T.timerSetting.enabled = 1 and T.mode = '{1}'", timerFrequency, mode);
            }
            QueryDefinition queryDefinition = new QueryDefinition(query);
            return GetAllResults<TimerSetting>(queryDefinition, container).Result;
        }
        public static List<T> GrabQueryFromTable<T>(string query,string table)
        {
            Container container = database.GetContainer(table);
            QueryDefinition queryDefinition = new QueryDefinition(query);
            return GetAllResults<T>(queryDefinition, container).Result;
        }
        public static async Task<ItemResponse<T>> InsertOrMerge<T>(T insertion, string table, string id = null, string partition_id = null, bool upsert = false)
        {
            Container container = database.GetContainer(table);
            if (upsert)
                return await container.UpsertItemAsync<T>(insertion, new PartitionKey(partition_id));
            try
            {
                return await container.CreateItemAsync<T>(insertion);
            } catch(CosmosException ce)
            {

            }
            return null;
        }
        public static string NextId(string tableName, string idName = null)
        {
            string nextOne = "0";
            try
            {
                string nextOneA = GrabQueryFromTable<dynamic>(String.Format("SELECT TOP 1 T.{0} FROM T order by T.{0} DESC", idName ?? "id"), tableName).First<dynamic>()[idName ?? "id"];
                if (nextOneA.Length > 0)
                {
                    nextOne = nextOneA;
                }
            }
            catch (Exception e) { }
            return (Convert.ToInt32(nextOne) + 1).ToString();
        }
    }
}
