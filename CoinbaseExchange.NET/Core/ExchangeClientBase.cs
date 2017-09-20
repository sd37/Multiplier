﻿using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CoinbaseExchange.NET.Core
{
    public abstract class ExchangeClientBase
    {
        public string API_ENDPOINT_URL;
        private const string ContentType = "application/json";

        private bool UsePublicExchange; 

        private readonly CBAuthenticationContainer _authContainer;

        public ExchangeClientBase(CBAuthenticationContainer authContainer, 
            string apiEndPoint = "https://api.gdax.com/")
        {
            API_ENDPOINT_URL = apiEndPoint;
            _authContainer = authContainer;
            UsePublicExchange = false;
        }

        public ExchangeClientBase(string apiEndPoint = "https://api.gdax.com/")
        {
            API_ENDPOINT_URL = apiEndPoint;
            //_authContainer = new CBAuthenticationContainer();
            UsePublicExchange = true;
        }

        protected async Task<ExchangeResponse> GetResponse(ExchangeRequestBase request)
        {
            var relativeUrl = request.RequestUrl;
            var absoluteUri = new Uri(new Uri(API_ENDPOINT_URL), relativeUrl);

            var timestamp = (request.TimeStamp).ToString(System.Globalization.CultureInfo.InvariantCulture);
            var body = request.RequestBody;
            var method = request.Method;
            var url = absoluteUri.ToString();

            String passphrase = "";
            String apiKey = "";
            // Caution: Use the relative URL, *NOT* the absolute one.
            var signature = "";

            using (var httpClient = new HttpClient())
            {
                HttpResponseMessage response;

                if (!UsePublicExchange)
                {
                    passphrase = _authContainer.Passphrase;
                    apiKey = _authContainer.ApiKey;
                    signature = _authContainer.ComputeSignature(timestamp, relativeUrl, method, body);

                    httpClient.DefaultRequestHeaders.Add("CB-ACCESS-KEY", apiKey);
                    httpClient.DefaultRequestHeaders.Add("CB-ACCESS-SIGN", signature);
                    httpClient.DefaultRequestHeaders.Add("CB-ACCESS-TIMESTAMP", timestamp);
                    httpClient.DefaultRequestHeaders.Add("CB-ACCESS-PASSPHRASE", passphrase);
                }


                httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0");

                switch(method)
                {
                    case "GET":
                        response = await httpClient.GetAsync(absoluteUri);
                        break;
                    case "POST":

                        ////jStr.Append()
                        //var requestString = string.Format("");

                        //JObject jObj = new JObject(
                        //    new JProperty(
                        //        "size", "1.0"),
                        //    new JProperty(
                        //        "price", "1.0"),
                        //    new JProperty(
                        //        "side", "buy"),
                        //    new JProperty(
                        //        "product_id","LTC-USD")
                        //    );

                        //body = jObj.ToString();

                        //var values = new Dictionary<string, string>
                        //{
                        //   { "size", "1.0" },
                        //   { "price", "1.0" },
                        //   { "side", "buy" },
                        //   { "product_id", "LTC-USD" },
                        //};

                        //var content = new FormUrlEncodedContent(values);



                        var requestBody = new StringContent(body, Encoding.UTF8, "application/json");
                        response = httpClient.PostAsync(absoluteUri, requestBody).Result;
                        break;
                    case "DELETE":
                        //var requestBody = new StringContent(body, Encoding.UTF8, "application/json");
                        response = await httpClient.DeleteAsync(absoluteUri);
                        break;
                    default:
                        throw new NotImplementedException("The supplied HTTP method is not supported: " + method ?? "(null)");
                }


                var contentBody = await response.Content.ReadAsStringAsync();
                var headers = response.Headers.AsEnumerable();
                var statusCode = response.StatusCode;
                var isSuccess = response.IsSuccessStatusCode;

                var genericExchangeResponse = new ExchangeResponse(statusCode, isSuccess, headers, contentBody);
                return genericExchangeResponse;
            }
        }

    }
}