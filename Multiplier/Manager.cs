﻿using CoinbaseExchange.NET.Core;
using CoinbaseExchange.NET.Data;
using CoinbaseExchange.NET.Endpoints.MyOrders;
using CoinbaseExchange.NET.Endpoints.PublicData;
using CoinbaseExchange.NET.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Multiplier
{



    class Manager
    {

        private ContextValues CurContextValues ;

        private TradeStrategyBase currentTradeStrategy;

        private string MyPassphrase { get; set; }
        private string MyKey { get; set; }
        private string MySecret { get; set; }


        private MovingAverage SmaLarge { get; set; }

        private MovingAverage SmaSmall { get; set; }

        private CBAuthenticationContainer MyAuth { get; set; }
        private MyOrderBook MyProductOrderBook;

        TickerClient ProductTickerClient;

        private bool userStartedTrading;

        public EventHandler CurrentActionChangedEvent;

        public EventHandler TickerPriceUpdateEvent;
        public EventHandler OrderFilledEvent;
        public EventHandler SmaLargeUpdateEvent;
        public EventHandler SmaSmallUpdateEvent;
        public EventHandler BuySellBufferChangedEvent;
        public EventHandler BuySellAmountChangedEvent;

        public EventHandler AutoTradingStartedEvent;
        public EventHandler AutoTradingStoppedEvent;

        public EventHandler TickerConnectedEvent;
        public EventHandler TickerDisConnectedEvent;

        public EventHandler PriceBelowAverageEvent;
        public EventHandler PriceAboveAverageEvent;

        public Manager(string inputProductName, string PassPhrase, string Key, string Secret)
        {

            //CurContextValues.ProductName = inputProductName;
            MyPassphrase = PassPhrase;
            MyKey = Key;
            MySecret = Secret;
            //InitializeManager();


        }

        public async void InitializeManager(string productName)
        {
            await Task.Factory.StartNew(() => InitManager(productName));
            await Task.Factory.StartNew(() => CheckTicker());
        }


        public void autoTradeStartEventHandler(object sender, EventArgs args)
        {
            AutoTradingStartedEvent?.Invoke(this, EventArgs.Empty);
        }

        public void autoTradeStopEventHandler(object sender, EventArgs args)
        {
            AutoTradingStoppedEvent?.Invoke(this, EventArgs.Empty);
        }

        public void StartTrading_BySelling()
        {
            currentTradeStrategy.StartTrading_BySelling();
        }
        public void StartTrading_ByBuying()
        {
            currentTradeStrategy.StartTrading_ByBuying();
        }


        private void InitManager(string ProductName)
        {


            //try to get ticker first
            

            try
            {
                Logger.WriteLog("Initializing ticker");
                ProductTickerClient = new TickerClient(ProductName);
            }
            catch (Exception ex)
            {
                Logger.WriteLog("Manager cant initialize ticker");
                return;
            }


            ProductTickerClient.PriceUpdated += ProductTickerClient_UpdateHandler;



            ProductTickerClient.TickerConnectedEvent += TickerConnectedHandler;
            ProductTickerClient.TickerDisconnectedEvent += TickerDisconnectedHandler;

            MyAuth = new CBAuthenticationContainer(MyKey, MyPassphrase, MySecret);
            MyProductOrderBook = new MyOrderBook(MyAuth, ProductName, ref ProductTickerClient);
            MyProductOrderBook.OrderUpdateEvent += FillUpdateEventHandler;

            CurContextValues = new ContextValues(ref MyProductOrderBook, ref ProductTickerClient);
            CurContextValues.ProductName = ProductName;

            CurContextValues.AutoTradingStartEvent += autoTradeStartEventHandler;
            CurContextValues.AutoTradingStopEvent += autoTradeStopEventHandler; 

            ////update ui with initial prices
            ProductTickerClient_UpdateHandler(this, new TickerMessage(ProductTickerClient.CurrentPrice));

            SmaLarge = new MovingAverage(ref ProductTickerClient, ProductName, CurContextValues.CurrentLargeSmaTimeInterval, CurContextValues.CurrentLargeSmaSlices);
            SmaLarge.MovingAverageUpdated += SmaLargeChangedEventHandler;

            SmaSmall = new MovingAverage(ref ProductTickerClient, ProductName, CurContextValues.CurrentSmallSmaTimeInterval, CurContextValues.CurrentSmallSmaSlices);
            SmaSmall.MovingAverageUpdated += SmaSmallChangedEventHandler;

            Logger.WriteLog(string.Format("{0} manager started", ProductName));


            //currentTradeStrategy = new TradeStrategyA(ref CurContextValues);
            //currentTradeStrategy = new TradeStrategyB(ref CurContextValues);
            //currentTradeStrategy = new TradeStrategyC(ref CurContextValues);
            //currentTradeStrategy = new TradeStrategyB(ref CurContextValues);
            currentTradeStrategy = new TradeStrategyD(ref CurContextValues);

            currentTradeStrategy.CurrentActionChangedEvent += CUrrentActionChangeEventHandler;

            UpdateBuySellAmount(0.01m); //default
            UpdateBuySellBuffer(0.03m); //default 

            

        }

        private void TickerDisconnectedHandler(object sender, EventArgs e)
        {
            Logger.WriteLog("Ticker disconnected... pausing all buy / sell");

            if (userStartedTrading)
            {
                CurContextValues.StartAutoTrading = false;
                //AutoTradingStoppedEvent?.Invoke(this, EventArgs.Empty);
            }

            TickerDisConnectedEvent?.Invoke(this, EventArgs.Empty);

        }

        private void TickerConnectedHandler(object sender, EventArgs args)
        {
            Logger.WriteLog("Ticker connected... resuming buy sell");

            //Logger.WriteLog("SMA updating starts here");
            //Task.Run(()=> UpdateSmaParameters(SmaTimeInterval, SmaSlices, true)).Wait();
            var x = UpdateLargeSmaParameters(CurContextValues.CurrentLargeSmaTimeInterval, CurContextValues.CurrentLargeSmaSlices, true);

            x.Wait();

            Logger.WriteLog("SMA updating ends here");
            //System.Threading.Thread.Sleep(2 * 1000);

            Logger.WriteLog("waiting 8 sec for sma to update");
            Thread.Sleep(8 * 1000);
            Logger.WriteLog("buy sell resumed here");

            if (userStartedTrading)
            {
                CurContextValues.StartAutoTrading = true;
                AutoTradingStartedEvent?.Invoke(this, EventArgs.Empty);
            }

            TickerConnectedEvent?.Invoke(this, EventArgs.Empty);
        }


        private void FillUpdateEventHandler(object sender, EventArgs args)
        {

            var filledOrder = ((OrderUpdateEventArgs)args);

            if (filledOrder.side == "UNKNOWN")
            {
                Logger.WriteLog("Order not filled properly, retrying last buy / sell action ");

                if (CurContextValues.CurrentAction == "BUY")
                {
                    //StartTrading_BySelling();
                    StartTrading_BySelling(); //start by selling since last failed action was sell
                }
                else if (CurContextValues.CurrentAction == "SELL")
                {
                    //StartTrading_BySelling();
                    StartTrading_ByBuying(); //start by buying since last failed action was buy
                }

            }
            else
            {
                Logger.WriteLog(string.Format("{0} order of {1} {2} ({3}) filled @{4}", filledOrder.side, filledOrder.fillSize, filledOrder.ProductName, filledOrder.OrderId, filledOrder.filledAtPrice));
            }


            //MessageBox.Show(string.Format("{0} order ({1}) filled @{2} ", filledOrder.side, filledOrder.OrderId, filledOrder.filledAtPrice));



            if (filledOrder.side == "buy")
            {
                CurContextValues.BuyOrderFilled = true;
                CurContextValues.SellOrderFilled = false;

                CurContextValues.WaitingBuyFill = false;
                CurContextValues.WaitingSellFill = true;
            }
            else if (filledOrder.side == "sell")
            {
                CurContextValues.SellOrderFilled = true;
                CurContextValues.BuyOrderFilled = false;

                CurContextValues.WaitingSellFill = false;
                CurContextValues.WaitingBuyFill = true;
            }




            //CurContextValues.WaitingBuyOrSell = false;

            //set buy sell waiting flag to off only when there are no order in the orderbook 
            if (CurContextValues.MyOrderBook.MyChaseOrderList.Count == 0) 
                CurContextValues.WaitingBuyOrSell = false;


            if (CurContextValues.ForceSold) 
            {
                ForcedOrderFilledEventArgs tempArgs = new ForcedOrderFilledEventArgs();
                tempArgs.filledAtPrice = filledOrder.filledAtPrice;
                tempArgs.fillFee = filledOrder.fillFee;
                tempArgs.fillSize = filledOrder.fillSize;
                tempArgs.Message = filledOrder.Message;
                tempArgs.OrderId = filledOrder.OrderId;
                tempArgs.ProductName = filledOrder.ProductName;
                tempArgs.side = filledOrder.side;
                tempArgs.ForcedOrder = true;
                //var temp = (ForcedOrderFilledEventArgs)filledOrder;
                tempArgs.ForcedOrder = true;
                CurContextValues.ForceSold = false;
                OrderFilledEvent?.Invoke(this, tempArgs);
            }
            else
            {
                OrderFilledEvent?.Invoke(this, filledOrder);
            }



            //this.Dispatcher.Invoke(() => updateListView(filledOrder));
        }

        public async Task<bool> StopAndCancel()
        {
            await currentTradeStrategy.CancelCurrentTradeAction();
            //test.Wait();

            if (CurContextValues.StartAutoTrading == false)
            {
                Logger.WriteLog("Auto trading stopped");
            }

            if (CurContextValues.MyOrderBook.MyChaseOrderList.Count() == 0)
            {
                CurContextValues.WaitingBuyOrSell = false;
            }

            return true;

        }


        public async void ForceSellAtNow()
        {

            

            try
            {
                Logger.WriteLog(string.Format("Placing Sell at NOW order "));
                CurContextValues.StartAutoTrading = false; //pause auto trading
                AutoTradingStoppedEvent?.Invoke(this, EventArgs.Empty);

                CurContextValues.WaitingBuyOrSell = true;

                var ForceOrderResult = await MyProductOrderBook.PlaceNewOrder("sell", CurContextValues.ProductName, 
                    CurContextValues.BuySellAmount.ToString(), CurContextValues.CurrentBufferedPrice.ToString(), true);
                CurContextValues.ForceSold = true;
                //ForceOrderResult.Wait();

                


            }
            catch (Exception ex)
            {
                var msg = ex.Message + "\n";
                while (ex.InnerException != null)
                {
                    ex = ex.InnerException;
                    msg = msg + ex.Message;
                }

                //Logger.WriteLog("Error Selling: \n" + msg);



                //setNextActionTo_Sell();


                //WaitingBuyFill = false;
                //WaitingBuyOrSell = false; //set wait flag to false to place new order

                ////simulate last cross time so sells immidiately instead of waiting. since error occured
                //LastCrossTime = DateTime.UtcNow.ToLocalTime().AddMinutes((-1 * WaitTimeAfterCrossInMin) - 1);
            }






        }


        private void ProductTickerClient_UpdateHandler(object sender, EventArgs args)
        {
            var tickerData = (TickerMessage)args;


            //CurContextValues.LastTickerUpdateTime = DateTime.UtcNow.ToLocalTime();

            //CurContextValues.CurrentRealtimePrice = tickerData.RealTimePrice;



            TickerPriceUpdateEvent?.Invoke(this, tickerData);


            var curTime = DateTime.UtcNow.ToLocalTime();
            var timeSinceLastTick = (curTime.Subtract(CurContextValues.LastTickerUpdateTime)).TotalMilliseconds;

            if (timeSinceLastTick < (100)) //wait 5 sec before next price change. origianlly 3 sec
            {
                return;
            }
            //else
            //{
            //    lastTickTIme = DateTime.UtcNow;
            //}
            CurContextValues.CurrentRealtimePrice = tickerData.RealTimePrice;
            CurContextValues.LastTickerUpdateTime = DateTime.UtcNow.ToLocalTime();

            if (CurContextValues.ForceSold)
            {
                if (CurContextValues.CurrentBufferedPrice <= (CurContextValues.CurrentLargeSmaPrice - CurContextValues.PriceBuffer)) // price below average 
                {

                    PriceBelowAverageEvent?.Invoke(this, EventArgs.Empty);

                    CurContextValues.ForceSold = false;

                    if (userStartedTrading)
                    {
                        StartTrading_ByBuying();
                    }
                }

            }


            if (CurContextValues.StartAutoTrading)
            {
                if ((CurContextValues.LastTickerUpdateTime - CurContextValues.LastBuySellTime).TotalMilliseconds < 1000)
                {
                    //Logger.WriteLog("price skipped: " + CurrentRealtimePrice);
                    return;
                }

                CurContextValues.CurrentBufferedPrice = CurContextValues.CurrentRealtimePrice;

                var secSinceLastCrosss = (DateTime.UtcNow.ToLocalTime() - CurContextValues.LastLargeSmaCrossTime).TotalSeconds;
                if (secSinceLastCrosss < (CurContextValues.WaitTimeAfterLargeSmaCrossInMin * 60))
                {
                    //if the last time prices crossed is < 2 min do nothing
                    //Logger.WriteLog(string.Format("Waiting {0} sec before placing any new order", Math.Round((sharedWaitTimeAfterCrossInMin * 60) - secSinceLastCrosss)));
                    return;
                }


                if (!CurContextValues.WaitingBuyOrSell)
                {
                    CurContextValues.LastBuySellTime = DateTime.UtcNow.ToLocalTime();

                    //buysell();
                    currentTradeStrategy.Trade();

                }
                else
                {
                    Logger.WriteLog("Buy/sell already in progress");
                }



                ////////////Testing needed!!!

                ////////////if the order book doesnt have any orders and yet the waitingbuysell flag is on then there must have been an error
                ////////////reset the flag to false; 
                //////////if (CurContextValues.MyOrderBook.MyChaseOrderList.Count == 0 && CurContextValues.WaitingBuyOrSell)
                //////////{
                //////////    Logger.WriteLog("Orderbook has no orders but WaitingBuyOrSell flag is set to on, resetting flag to off");
                //////////    Logger.WriteLog("WARNING: check next BUY or SELL action is correct");

                //////////    CurContextValues.WaitingBuyOrSell = false;
                //////////}


            }


        }


        private void CUrrentActionChangeEventHandler(object sender, EventArgs args)
        {
            CurrentActionChangedEvent?.Invoke(this, args);
        }

        public async Task<bool> UpdateLargeSmaParameters(int InputTimerInterval, int InputSlices, bool forceRedownload = false)
        {

            try
            {
                var x = await Task.Run(() => SmaLarge.updateValues(InputTimerInterval, InputSlices, forceRedownload));
                if (x == true) //wait for task to complete above
                {
                    //done;
                }

                return true;
            }
            catch (Exception ex)
            {
                Logger.WriteLog("Error occured while updating SMA parameters: " + ex.Message);
                return false;
            }

        }

        public async Task<bool> UpdateSmallSmaParameters(int InputTimerInterval, int InputSlices, bool forceRedownload = false)
        {

            try
            {
                var x = await Task.Run(() => SmaSmall.updateValues(InputTimerInterval, InputSlices, forceRedownload));
                if (x == true) //wait for task to complete above
                {
                    //Logger.WriteLog(x.ToString()); //done;
                }


                //temporary solution
                var smallestSmaInterval = InputTimerInterval;
                var smallestSmaSlices = Math.Round(InputSlices / 2.0m, 0);
                await Task.Run(()=> currentTradeStrategy.updateSmallestSma(smallestSmaInterval, Convert.ToInt32(smallestSmaSlices)));
                

                return true;
            }
            catch (Exception ex)
            {
                Logger.WriteLog("Error occured while updating SMA parameters: " + ex.Message);
                return false;
            }

        }


        private void SmaLargeChangedEventHandler(object sender, EventArgs args)
        {

            var currentSmaData = (MAUpdateEventArgs)args;
            decimal newSmaPrice = currentSmaData.CurrentMAPrice;

            CurContextValues.CurrentLargeSmaPrice = newSmaPrice;




            CurContextValues.CurrentLargeSmaSlices = currentSmaData.CurrentSlices;// InputSlices;
            CurContextValues.CurrentLargeSmaTimeInterval = currentSmaData.CurrentTimeInterval; // InputTimerInterval;
            CurContextValues.WaitTimeAfterLargeSmaCrossInMin = CurContextValues.CurrentLargeSmaTimeInterval;


            var msg = string.Format("Large SMA updated: {0} (Time interval: {1} Slices: {2})", newSmaPrice, 
                CurContextValues.CurrentLargeSmaTimeInterval, CurContextValues.CurrentLargeSmaSlices);
            Logger.WriteLog(msg);


            SmaLargeUpdateEvent?.Invoke(this, currentSmaData);
        }

        private void SmaSmallChangedEventHandler(object sender, EventArgs args)
        {

            var currentSmaData = (MAUpdateEventArgs)args;
            decimal newSmaPrice = currentSmaData.CurrentMAPrice;

            CurContextValues.CurrentSmallSmaPrice = newSmaPrice;

            CurContextValues.CurrentSmallSmaSlices = currentSmaData.CurrentSlices;// InputSlices;
            CurContextValues.CurrentSmallSmaTimeInterval = currentSmaData.CurrentTimeInterval; // InputTimerInterval;
            CurContextValues.WaitTimeAfterSmallSmaCrossInMin = CurContextValues.CurrentSmallSmaTimeInterval;


            var msg = string.Format("Small SMA updated: {0} (Time interval: {1} Slices: {2})", newSmaPrice, 
                CurContextValues.CurrentSmallSmaTimeInterval, CurContextValues.CurrentSmallSmaSlices);
            Logger.WriteLog(msg);


            SmaSmallUpdateEvent?.Invoke(this, currentSmaData);
        }

        public void UpdateBuySellBuffer(decimal newPriceBuffer, bool useInverse = false)
        {

            decimal tempValue = newPriceBuffer; 

            if (useInverse)
            {
                if(newPriceBuffer > 0) //posible div by zero error 
                    tempValue = 1 / (newPriceBuffer / 50); 
            }

            CurContextValues.PriceBuffer = Math.Round(tempValue, 4);

            var msg = "Buy Sell Price buffer updated to " + CurContextValues.PriceBuffer.ToString();
            Logger.WriteLog(msg);

            BuySellBufferChangedEvent?.Invoke(this, new BuySellBufferUpdateArgs { NewBuySellBuffer = CurContextValues.PriceBuffer });

            //Dispatcher.Invoke(() => lblBuySellBuffer.Content = sharedPriceBuffer.ToString());

        }


        public void UpdateBuySellAmount(decimal amount)
        {
            if (amount <= 0)
            {
                Logger.WriteLog("buy sell amount cannot be less than or equal to 0");
                return;
            }

            CurContextValues.BuySellAmount = amount;


            var msg = "Buy sell amount changed to: " + CurContextValues.BuySellAmount.ToString();
            Logger.WriteLog(msg);

            BuySellAmountChangedEvent?.Invoke(this, new BuySellAmountUpdateArgs { NewBuySellAmount = CurContextValues.BuySellAmount });
        }


        void CheckTicker()
        {

            while(true)
            {
                Logger.WriteLog("Checking Ticker");
                var curTime = DateTime.UtcNow.ToLocalTime();
                var timeDiff = curTime.Subtract(CurContextValues.LastTickerUpdateTime).TotalSeconds; // (curTime - CurContextValues.LastTickerUpdateTime).Seconds;
                var maxInactiveTime = 120;
                if (timeDiff >= maxInactiveTime)
                {
                    Logger.WriteLog(string.Format("Ticker inactive for more than {0} seconds, closing and reconnecting now", maxInactiveTime));
                    ProductTickerClient?.CloseAndReconnect();
                }
                Thread.Sleep(maxInactiveTime * 1000);
                
            }
        }
    }




    public class BuySellBufferUpdateArgs : EventArgs { public decimal NewBuySellBuffer { get; set; } }
    public class BuySellAmountUpdateArgs : EventArgs { public decimal NewBuySellAmount { get; set; } }

    public class ActionChangedArgs : EventArgs
    {
        public string CurrentAction { get; set; }
        public ActionChangedArgs(string curAction)
        {
            if (curAction == "NOT_SET")
                CurrentAction = "NOT SET";
            else
                CurrentAction = curAction;
        }
    }

    public class ForcedOrderFilledEventArgs: OrderUpdateEventArgs {public bool ForcedOrder { get; set; } }

    public class SmaParamUpdateArgs : EventArgs
    {
        public int NewTimeinterval { get; set; }
        public decimal NewSlices { get; set; }
    }

    public class ContextValues
    {

        //common 
        public List<String> TradeActionList { get; set; }

        public TickerClient CurrentTicker;

        public string ProductName { get; set; }
        public string CurrentAction { get; set; }

        public decimal CurrentRealtimePrice { get; set; }
        public decimal CurrentBufferedPrice { get; set; }

        public decimal MaxBuy { get; set; }
        public decimal MaxSell { get; set; }
        public decimal BuySellAmount { get; set; }
        public decimal PriceBuffer { get; set; }

        public bool BuyOrderFilled { get; set; }
        public bool SellOrderFilled { get; set; }

        public bool WaitingSellFill { get; set; }
        public bool WaitingBuyFill { get; set; }
        public bool WaitingBuyOrSell { get; set; }


        public DateTime LastTickerUpdateTime { get; set; }
        public DateTime LastBuySellTime { get; set; }
        public DateTime LastLargeSmaCrossTime { get; set; }
        public DateTime LastSmallSmaCrossTime { get; set; }

        public EventHandler AutoTradingStartEvent;
        public EventHandler AutoTradingStopEvent;

        public MyOrderBook MyOrderBook { get; set; }

        public bool UserStartedTrading { get; set; }


        private bool _startAutoTrading; 
        public bool StartAutoTrading
        {
            get { return _startAutoTrading; }
            set
            {
                _startAutoTrading = value;
                if (value == true)
                {
                    AutoTradingStartEvent?.Invoke(this, EventArgs.Empty);
                    //Logger.WriteLog("Setting autotrasing to ON");
                }
                else
                {
                    AutoTradingStopEvent?.Invoke(this, EventArgs.Empty);
                    //Logger.WriteLog("Setting autotrasing to OFF");
                }
            }
        }


        public bool ForceSold { get; set; }
        

        //large sma
        public decimal CurrentLargeSmaPrice { get; set; }
        public int CurrentLargeSmaSlices { get; set; }
        public int CurrentLargeSmaTimeInterval { get; set; }
        public double WaitTimeAfterLargeSmaCrossInMin { get; set; }


        //small sma
        public decimal CurrentSmallSmaPrice { get; set; }
        public int CurrentSmallSmaSlices { get; set; }
        public int CurrentSmallSmaTimeInterval { get; set; }
        public double WaitTimeAfterSmallSmaCrossInMin { get; set; }


        public ContextValues(ref MyOrderBook orderBook, ref TickerClient inputTicker)
        {
            MyOrderBook = orderBook;

            CurrentTicker = inputTicker; 

            UserStartedTrading = false;
            TradeActionList = new List<string>();
            ForceSold = false;

            BuySellAmount = 0.01m;//default

            CurrentLargeSmaTimeInterval = 1; //default
            CurrentLargeSmaSlices = 40; //default


            CurrentSmallSmaTimeInterval = 1; //default
            CurrentSmallSmaSlices = 20; //default

            PriceBuffer = 0.05m; //default

            CurrentRealtimePrice = 0;
            CurrentBufferedPrice = 0;
            CurrentLargeSmaPrice = 0;

            BuyOrderFilled = false;
            SellOrderFilled = false;

            WaitingSellFill = false;
            WaitingBuyFill = false;
            WaitingBuyOrSell = false;

            WaitTimeAfterLargeSmaCrossInMin = CurrentLargeSmaTimeInterval; //buy sell every time interval

            MaxBuy = 5;
            MaxSell = 5;

            StartAutoTrading = false;
            //AutoTradingStoppedEvent?.Invoke(this, EventArgs.Empty);


            LastLargeSmaCrossTime = DateTime.UtcNow.ToLocalTime();
            LastSmallSmaCrossTime = DateTime.UtcNow.ToLocalTime();

        }

        


    }


}









