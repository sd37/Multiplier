﻿using CoinbaseExchange.NET.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using CoinbaseExchange.NET.Endpoints.MyOrders;
using System.Diagnostics;
using CoinbaseExchange.NET.Utilities;
using System.Threading;


namespace CoinbaseExchange.NET.Endpoints.Fills
{


    public class Fill
    {
        public string TradeId { get; set; }
        public string ProductId { get; set; }
        public string Price { get; set; }
        public string Size { get; set; }
        public string OrderId { get; set; }
        public DateTime Time { get; set; }
        public string Fee { get; set; }
        public bool Settled { get; set; }
        public string Side { get; set; }
        public string Liquidity { get; set; }

        public string OrderStatus { get; set; }
        public Fill() { }

        public Fill(Order order)
        {
            TradeId = order.Id;
            ProductId = order.Product_id;
            Price = order.Price;
            Size = order.Filled_size;
            OrderId = order.Id;
            Time = DateTime.UtcNow.ToLocalTime();
            Fee = order.Fill_fees; //unknonw fee since order doesnt contain fee info
            Settled = true;
            Side = order.Side;
            OrderStatus = order.Status;
            Liquidity = "UNKNOWN";

        } //empty ocn

        public Fill(JToken jToken)
        {
            this.TradeId = jToken["trade_id"].Value<string>();
            this.ProductId = jToken["product_id"].Value<string>();
            this.Price = jToken["price"].Value<string>();
            this.Size = jToken["size"].Value<string>();
            this.OrderId = jToken["order_id"].Value<string>();
            this.Time = jToken["created_at"].Value<DateTime>();
            this.Fee = jToken["fee"].Value<string>();
            this.Settled = jToken["settled"].Value<bool>();
            this.Side = jToken["side"].Value<string>();
            this.Liquidity = jToken["liquidity"].Value<string>();

        }
    }


    public class GetFillsRequest : ExchangePageableRequestBase
    {
        public GetFillsRequest(string endpoint) : base("GET")
        {
            this.RequestUrl = endpoint;
        }
    }

    public class FillResponse : ExchangePageableResponseBase
    {
        public List<Fill> Fills { get; private set; }

        public FillResponse(ExchangeResponse response) : base(response)
        {
            if (!response.IsSuccessStatusCode)
                throw new Exception("FillRequestError");

            var json = response.ContentBody;
            var jArray = JArray.Parse(json);
            Fills = jArray.Select(elem => new Fill(elem)).ToList();
        }
    }

    public class FillEventArgs : EventArgs
    {
        public Fill filledOrder;
        
    }


    public class FillsClient : ExchangeClientBase
    {
        private static object _watchListLock = new object();
        public EventHandler FillUpdated;
        public List<MyOrder> FillWatchList { get; set; }

        private MyOrderBook myActiveOrderBook; 

        public static bool IsBusy_TrackIngOrder;

        public bool BusyCheckingOrder; 

        public FillsClient(CBAuthenticationContainer authenticationContainer, MyOrderBook orderBook) : base(authenticationContainer)
        {
            myActiveOrderBook = orderBook;
            FillWatchList = orderBook.MyChaseOrderList ;
            IsBusy_TrackIngOrder = false;
            BusyCheckingOrder = false;
            //startTracker();
        }

        public async void startTracker()
        {
            //await Task.Factory.StartNew(() => FillTracker(onFillUpdate));
            await Task.Run(() => TrackOrder());

        }


        public async Task<FillResponse> GetFills()
        {
            var endpoint = String.Format("/fills");
            var request = new GetFillsRequest(endpoint);

            ExchangeResponse response = null;
            try
            {
                response = await this.GetResponse(request);
            }
            catch (Exception)
            {
                throw new Exception("GetFillsError");
            }

            var accountHistoryResponse = new FillResponse(response);
            return accountHistoryResponse;
        }

        public async Task<FillResponse> GetFillStatus(string orderId)
        {
            var endpoint = string.Format(@"/fills?order_id={0}", orderId);
            var request = new GetFillsRequest(endpoint);


            ExchangeResponse response = null;
            try
            {
                response = await this.GetResponse(request);
            }
            catch (Exception)
            {
                throw new Exception("GetFillsStatusError");
            }


            var orderStats = new FillResponse(response);

            return orderStats;
        }


        public async Task<Fill> GetOrderStatus(string orderId)
        {


            //myActiveOrderBook.GetSingleOrder

            var doneOrder = myActiveOrderBook.GetSingleOrder(orderId);
            doneOrder.Wait();

            Fill fakeFilList = null;
            if (doneOrder.Result != null)
            {
                fakeFilList = new Fill(doneOrder.Result);
            }

            return fakeFilList;
        }


        public void OrderFilledEvent(List<Fill> FillList)
        {
            var fillDetails = FillList.FirstOrDefault();
            Int32 orderIndex = -1;
            try
            {
                orderIndex = FillWatchList.FindIndex((o) => o.OrderId == fillDetails.OrderId);
            }
            catch (Exception)
            {
                Logger.WriteLog(string.Format("Order {0} not found in watch list!",fillDetails.OrderId));
                return;
            }

            decimal totalFilled = 0;


            //test
            //var testFill = FillList.First();//new Fill(null);
            //testFill = FillList.First();
            ////testFill.
            //FillList.Add(testFill);



            //reset the filled size assuming all the split orders are retunred
            if (FillList.Count > 0)
            {
                FillWatchList[orderIndex].FilledSize = 0.00m; 
                foreach (var curFill in FillList)
                {
                    totalFilled += Convert.ToDecimal(curFill.Size);
                    FillWatchList[orderIndex].FilledSize = FillWatchList[orderIndex].FilledSize 
                        + Convert.ToDecimal(curFill.Size);
                }


            }
            else
            {
                //error condition where there is no data in filled order
                return;
            }




            //check if ordered size and fill size match, 
            //if it does then remove from watch list
            //else mark it was partially filled

            var majorityFillSize = FillWatchList[orderIndex].ProductSize * 1;//0.99m;

            if (FillWatchList[orderIndex].FilledSize == majorityFillSize) //if filled size is more than 90%
            {
                //BusyCheckingOrder = false;
                //myActiveOrderBook.RemoveFromOrderList(fillDetails.OrderId);

                

                Logger.WriteLog("Order filled with following sizes:");
                FillList.ForEach((f) => Logger.WriteLog(f.Size + " @" + f.Price + " (fee: " + f.Fee + ")"));

                //add the list of filled sizes to fill list if the filledList does not already contain it
                foreach (var f in FillList)
                {
                    if (!(FillWatchList[orderIndex].FilledList.Any((e)=>e.OrderId == f.OrderId && e.Side == f.Side && e.Fee == f.Fee))) //if list does not already contain item then add
                    {
                        FillWatchList[orderIndex].FilledList.Add(f);
                    }
                }

                //blindly adds all filled even if already exists in list 
                //FillWatchList[orderIndex].FilledList.AddRange(FillList);

                if (fillDetails.Price == null) //indicates a market order
                {
                    try
                    {
                        var orderStat = GetFillStatus(fillDetails?.OrderId).Result;
                        if (orderStat.Fills.Count > 0)
                            fillDetails.Price = orderStat.Fills.FirstOrDefault().Price; 
                    }
                    catch (Exception)
                    {
                        Logger.WriteLog("Error getting fill response");
                    }
                }


                var filledSizeSum = 0.0m;
                var filledAmountSum = 0.0m;
                var filledFeeSum = 0.0m;
                var filledAtAvgPrice = 0.0m;

                Logger.WriteLog("All fills:");
                foreach (var fillDet in FillWatchList[orderIndex].FilledList)
                {
                    filledSizeSum += Convert.ToDecimal(fillDet.Size);
                    filledAmountSum += Convert.ToDecimal(fillDet.Size) * Convert.ToDecimal(fillDet.Price);
                    filledFeeSum += Convert.ToDecimal(fillDet.Fee);
                    Logger.WriteLog(fillDet.Size + " @" + fillDet.Price + " (fee: " + fillDet.Fee + ")");
                }

                if (filledSizeSum > 0)
                    filledAtAvgPrice = filledAmountSum / filledSizeSum;

                //fillDetails.Size = totalFilled.ToString();
                fillDetails.Size = filledSizeSum.ToString();
                fillDetails.Price = filledAtAvgPrice.ToString();
                fillDetails.Fee = filledFeeSum.ToString();

                FillWatchList.RemoveAll(x => x.OrderId == fillDetails.OrderId);
                FillUpdated?.Invoke(this, new FillEventArgs { filledOrder = fillDetails });


                

            }
            else
            {
                //cancel the order here the first time and retry to fill unfilled amount in OrderBook

                var myCurrentOrder = FillWatchList[orderIndex];

                //when checking the same order after the first time
                //if not (partially filled or Cancel_error)
                //skip if market order
                if (!(FillWatchList[orderIndex].Status == "PARTIALLY_FILLED" 
                    || FillWatchList[orderIndex].Status == "CANCEL_ERROR" 
                    || FillWatchList[orderIndex].OrderType == "market"))
                {

                    ////add the list of filled sizes to fill list
                    //FillWatchList[orderIndex].FilledList.AddRange(FillList);

                    //add the list of filled sizes to fill list if the filledList does not already contain it
                    foreach (var f in FillList)
                    {
                        if (!(FillWatchList[orderIndex].FilledList.Any((e) => e.OrderId == f.OrderId && e.Side == f.Side && e.Fee == f.Fee))) //if list does not already contain item then add
                        {
                            FillWatchList[orderIndex].FilledList.Add(f);
                        }
                    }


                    Logger.WriteLog(string.Format("{0} order({1}) of {2} {3} filled partially with following sizes:",
                        fillDetails.Side, fillDetails.OrderId, FillWatchList[orderIndex].ProductSize,
                        fillDetails.ProductId));

                    FillList.ForEach((f) => Logger.WriteLog(f.Size + " @" + f.Price + " (fee: " + f.Fee + ")"));

                    try
                    {
                        Logger.WriteLog("Cancelling remainder of partially filled order: " + myCurrentOrder.OrderId);


                        if (myCurrentOrder.OrderType == "market")
                        {
                            Logger.WriteLog("market order! cant cancel");
                        }
                        var cancelledOrder = myActiveOrderBook.CancelSingleOrder(myCurrentOrder.OrderId).Result;
                        if (cancelledOrder.Count > 0)
                            FillWatchList[orderIndex].Status = "PARTIALLY_FILLED";
                    }
                    catch (Exception)
                    {
                        Logger.WriteLog("Error cancelling partially filled order, marking as CANCEL_ERROR " + myCurrentOrder.OrderId);
                        // this will stop from entering this method 
                        //also it will be tried to be cancelled in cancelAndReorder where 
                        //it will fail and a new order will be placed
                        FillWatchList[orderIndex].Status = "CANCEL_ERROR";
                    }


                }

                //set the current order in the list to partially filled
                //FillWatchList[orderIndex].Status = "PARTIALLY_FILLED";

            }


            //fillDetails.Size = totalFilled.ToString(); //modify the filled size with the total instead of the first size in list

            //BusyCheckingOrder = false;

            //notify only if fully filled?
            //FillUpdated?.Invoke(this, new FillEventArgs { filledOrder = fillDetails });

        }


        private void TrackOrder()
        {


            if (IsBusy_TrackIngOrder)
            {
                Logger.WriteLog("already busy tracking orders");
                return;
            }
                


            Logger.WriteLog("Checking fill status...");

            if (FillWatchList.Count() > 0)
                IsBusy_TrackIngOrder = true;

            int counter = 0;

            while (FillWatchList.Count() > 0)
            {

                //if ticker is offline then return

                //not to overwhelm the logger 
                counter++;
                if (counter % 10 == 0)
                {
                    //Logger.WriteLog(string.Format("Watching {0} order(s)", FillWatchList.Count()));

                    //Logger.WriteLog(FillWatchList.FirstOrDefault().OrderId);
                    try
                    {

                        FillWatchList.ForEach((x) => Logger.WriteLog(string.Format("{0} -> {1} {2} {3} @{4}",
                            x.OrderId, x.Side, x.ProductSize, x.Productname, x.UsdPrice)));
                    }
                    catch (Exception ex)
                    {
                        Logger.WriteLog("Error writing watch list (" + ex.Message + ")");
                    };
                }

                if (counter >= int.MaxValue - 100)
                    counter = 0; 


                if (myActiveOrderBook.BusyWithWithCancelReorder)
                    Logger.WriteLog("Waiting for cancel and reorder to finish");


                //wait until cancel and reorder is done in orderbook 
                int cancelReorderWaitCount = 0;
                while (myActiveOrderBook.BusyWithWithCancelReorder)
                {
                    Thread.Sleep(50);
                    //cancelReorderWaitCount += 1;
                }



                BusyCheckingOrder = true; 

                for (int i = 0; i < FillWatchList.Count; i++)
                {
                    if (FillWatchList.Count == 0)
                        break;

                    //var orderStat = await GetFillStatus(FillWatchList.ElementAt(i)?.OrderId);
                    var currentOrder = FillWatchList.ElementAt(i);


                    ////////order checking method 1
                    //////FillResponse orderStat = null;
                    //////try
                    //////{
                    //////    orderStat = GetFillStatus(currentOrder?.OrderId).Result;
                    //////}
                    //////catch (Exception)
                    //////{
                    //////    Logger.WriteLog("Error getting fill response");
                    //////}

                    //////if (orderStat?.Fills.Count > 0)
                    //////{
                    //////    //BusyCheckingOrder = false;
                    //////    OrderFilledEvent(orderStat.Fills);
                    //////}



                    ////order checking method 2
                    Fill orderStat = null;
                    try
                    {
                        orderStat = GetOrderStatus(currentOrder?.OrderId).Result;
                    }
                    catch (Exception ex)
                    {
                        Logger.WriteLog("Error getting order status response for current market order");
                    }

                    if (orderStat != null)
                    {

                        if (currentOrder.OrderType != "market")
                        {
                            //for limit order
                            if (Convert.ToDecimal(orderStat.Size) > 0)
                            {
                                var tempList = new List<Fill>();
                                tempList.Add(orderStat);
                                OrderFilledEvent(tempList);
                            }
                        }
                        else
                        {
                            if (orderStat.OrderStatus != "done")
                            {
                                //check the market order status 2 times 
                                int checkCount = 0;
                                while (orderStat.OrderStatus != "done")
                                {
                                    if (checkCount > 2)
                                        break;

                                    try
                                    {
                                        orderStat = GetOrderStatus(currentOrder?.OrderId).Result;
                                    }
                                    catch (Exception ex)
                                    {
                                        Logger.WriteLog("Error getting order status response for current market order");
                                    }

                                    Thread.Sleep(200);
                                    checkCount++;
                                }

                                if (orderStat.OrderStatus == "done")
                                {
                                    var tempList = new List<Fill>();
                                    tempList.Add(orderStat);
                                    OrderFilledEvent(tempList);
                                }

                            }
                            else
                            {
                                var tempList = new List<Fill>();
                                tempList.Add(orderStat);
                                OrderFilledEvent(tempList);
                            }
                        }


                    }


                    Thread.Sleep(300);

                }

                BusyCheckingOrder = false;
                //await Task.Delay(1000); //check fills every 1 sec
                //Thread.Sleep(500);
            }

            IsBusy_TrackIngOrder = false;


            //return true;
        }


    }
}
