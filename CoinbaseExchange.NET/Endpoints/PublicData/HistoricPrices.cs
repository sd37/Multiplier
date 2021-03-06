﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CoinbaseExchange.NET.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using CoinbaseExchange.NET.Utilities;

namespace CoinbaseExchange.NET.Endpoints.PublicData
{

    public class CandleData
    {
        public DateTime Time { get; set; }
        public string Low { get; set; }
        public string High { get; set; }
        public decimal LocalAvg { get; set; }
        public string Open { get; set; }
        public decimal Close { get; set; }
        public string Volume { get; set; }

    }

    class HistoricPriceRequest : ExchangeRequestBase
    {
        public HistoricPriceRequest(string endpointAddress) : base("GET")
        {
            this.RequestUrl = endpointAddress;
        }
    }

    public class HistoricPrices : ExchangeClientBase
    {

        //public HistoricPrices(): base()
        //{
        //}

        public async Task<IEnumerable<CandleData>> GetPrices(string product, string startTime="", string endTime = "", string granularity="")
        {
            StringBuilder urlBuilder = new StringBuilder();

            urlBuilder.Append(string.Format(@"products/{0}/candles", product));
            string endpoint = string.Format(urlBuilder.ToString());


            if (!string.IsNullOrWhiteSpace(startTime))
            {
                urlBuilder.Append(string.Format(@"?start={0}&end={1}", startTime, endTime));
                endpoint = urlBuilder.ToString();
            }

            if (!string.IsNullOrWhiteSpace(granularity))
            {
                if (endpoint.Contains("?"))
                    urlBuilder.Append(string.Format(@"&granularity={0}", granularity));
                else
                    urlBuilder.Append(string.Format(@"?granularity={0}", granularity));
                endpoint = urlBuilder.ToString();
            }

            HistoricPriceRequest request = new HistoricPriceRequest(endpoint);


            ExchangeResponse genericResponse = null; 

            try
            {
                genericResponse = await this.GetResponse(request);
            }
            catch (Exception ex)
            {
                Logger.WriteLog("Error getting historic data prices: " + ex.Message + " InnerEx msg:" + ex.InnerException.Message); 
                throw new Exception("HistoricDataError");
            }

            

            IEnumerable<CandleData> result;
            if (genericResponse.IsSuccessStatusCode)
            {
                var json = genericResponse.ContentBody;

                //Logger.WriteLog(json);



                //JArray job = new JArray(str);
                //JArray jObject = new JArray();
                //JObject.Parse(json);
                //var prices = jObject.First.Select(x => (JArray)x).ToArray();

                //var a = await Task.Factory.StartNew(() => JsonConvert.DeserializeObject<CandleData>(json));

                result = await Task.Factory.StartNew(() => JsonConvert.DeserializeObject<List<JArray>>(json).Select((x) => new CandleData
                {
                    Time = DateTimeUtilities.DateTimeFromUnixTimestampSeconds((x[0].Value<string>())).ToLocalTime(),
                    Low = x[1].Value<string>(),
                    High = x[2].Value<string>(),
                    Open = x[3].Value<string>(),
                    Close = x[4].Value<decimal>(),
                    Volume = x[5].Value<string>(),
                    LocalAvg = ((x[1].Value<decimal>() + x[2].Value<decimal>()) / 2)
                }));

            }
            else
            {
                
                throw new Exception("ExchangeRequestError");
            }

            return result;
        }

        public void parsePriceJson(string json)
        {
            //var json = "{[1505969580,50.98,51.03,50.99,51.03,77.14769553000001]}";
            //var str = @"[[ 1415398768, 0.32, 4.2, 0.35, 4.2, 12.3 ],[1505969580,50.98,51.03,50.99,51.03,77.14769553000001]]";

            //JArray job = new JArray(str);
            //var prices = JArray.Parse(str); //job.First.Select(x => (JArray)x).ToArray();
            //var p = JsonConvert.DeserializeObject<List<JArray>>(json).Select((x) => new CandleData {
            //    Time = x[0].Value<string>(),
            //    Low = x[1].Value<string>(),
            //    High = x[2].Value<string>(),
            //    Open = x[3].Value<string>(),
            //    Close = x[4].Value<string>(),
            //    Volume = x[5].Value<string>(),
            //    Average = 0
            //});
            


        }


    }
}
