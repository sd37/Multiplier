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
using CoinbaseExchange.NET.Endpoints.Funds;
using Simulator;

namespace Multiplier
{



    class Manager
    {

        private ContextValues CurContextValues ;

        private TradeStrategyBase currentTradeStrategy;

        private string MyPassphrase { get; set; }
        private string MyKey { get; set; }
        private string MySecret { get; set; }

        private string ConfigStrategyInUse { get; set; }

        private MovingAverage DisplaySma { get; set; }

        //private MovingAverage SmaSmall { get; set; }

        private CBAuthenticationContainer MyAuth { get; set; }
        private MyOrderBook MyProductOrderBook;

        TickerClient ProductTickerClient;

        //private bool userStartedTrading;

        public EventHandler CurrentActionChangedEvent;

        public EventHandler FundsUpdatedEvent;

        public EventHandler TickerPriceUpdateEvent;
        public EventHandler OrderFilledEvent;
        public EventHandler DisplaySmaUpdateEvent;
        //public EventHandler SmaSmallUpdateEvent;
        public EventHandler BuySellBufferChangedEvent;
        public EventHandler BuySellAmountChangedEvent;

        public EventHandler AutoTradingStartedEvent;
        public EventHandler AutoTradingStoppedEvent;

        public EventHandler TickerConnectedEvent;
        public EventHandler TickerDisConnectedEvent;

        public EventHandler PriceBelowAverageEvent;
        public EventHandler PriceAboveAverageEvent;

        private bool isBuysyDisposingCurStrategy;

        //large sma
        public decimal CurrentDisplaySmaPrice { get; set; }
        public int CurrentDisplaySmaSlices { get; set; }
        public int CurrentDisplaySmaTimeInterval { get; set; }
        //public double WaitTimeAfterLargeSmaCrossInMin { get; set; }

        private bool AvoidExchangeFees;

        public Manager(string inputProductName, string PassPhrase, string Key, string Secret)
        {

            //CurContextValues.ProductName = inputProductName;
            MyPassphrase = PassPhrase;
            MyKey = Key;
            MySecret = Secret;
            //InitializeManager();
            isBuysyDisposingCurStrategy = false;

        }

        public async void InitializeManager(string productName, IntervalValues intervalValues = null)
        {
            await Task.Factory.StartNew(() => InitManager(productName, intervalValues));
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


        private void InitManager(string ProductName, IntervalValues intervalValues)
        {


            //try to get ticker first



            //Logger.WriteLog("Init Manager Thread ID: " + Thread.CurrentThread.ManagedThreadId.ToString());

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

            AvoidExchangeFees = true;

            ProductTickerClient.TickerConnectedEvent += TickerConnectedHandler;
            ProductTickerClient.TickerDisconnectedEvent += TickerDisconnectedHandler;

            MyAuth = new CBAuthenticationContainer(MyKey, MyPassphrase, MySecret);
            MyProductOrderBook = new MyOrderBook(MyAuth, ProductName, ref ProductTickerClient);
            MyProductOrderBook.OrderUpdateEvent += FillUpdateEventHandler;




            CurContextValues = new ContextValues(ref MyProductOrderBook, ref ProductTickerClient);
            CurContextValues.ProductName = ProductName;

            CurContextValues.Auth = MyAuth;

            CurContextValues.AutoTradingStartEvent += autoTradeStartEventHandler;
            CurContextValues.AutoTradingStopEvent += autoTradeStopEventHandler; 

            ////update ui with initial prices
            ProductTickerClient_UpdateHandler(this, new TickerMessage(ProductTickerClient.CurrentPrice));


            CurrentDisplaySmaTimeInterval = intervalValues.LargeIntervalInMin;
            CurrentDisplaySmaSlices = intervalValues.LargeSmaSlices; //default large sma slice value 




            //SmaSmall = new MovingAverage(ref ProductTickerClient, ProductName, CurContextValues.CurrentSmallSmaTimeInterval, CurContextValues.CurrentSmallSmaSlices);
            //SmaSmall.MovingAverageUpdated += SmaSmallChangedEventHandler;

            Logger.WriteLog(string.Format("{0} manager started", ProductName));


            //currentTradeStrategy = new TradeStrategyA(ref CurContextValues);
            //currentTradeStrategy = new TradeStrategyB(ref CurContextValues);
            //currentTradeStrategy = new TradeStrategyC(ref CurContextValues);
            //currentTradeStrategy = new TradeStrategyB(ref CurContextValues);
            //currentTradeStrategy = new TradeStrategyD(ref CurContextValues);

            if (intervalValues == null)
                intervalValues = new IntervalValues(30, 15, 5);//IntervalValues(5, 3, 1);

            //currentTradeStrategy = new TradeStrategyE(ref CurContextValues, intervalValues);

            CreateUpdateStrategyInstance(intervalValues, true).Wait();

            currentTradeStrategy.CurrentActionChangedEvent += CUrrentActionChangeEventHandler;




            //save the interval values for later use
            //CurContextValues.CurrentIntervalValues = intervalValues; 
            SetCurrentIntervalValues(intervalValues);


            DisplaySma = new MovingAverage(ref ProductTickerClient, ProductName, CurrentDisplaySmaTimeInterval, CurrentDisplaySmaSlices);
            DisplaySma.MovingAverageUpdatedEvent += DisplaySmaChangedEventHandler;

            UpdateDisplaySmaParameters(CurrentDisplaySmaTimeInterval, CurrentDisplaySmaSlices);

            UpdateBuySellAmount(0.01m); //default
            //UpdateBuySellBuffer(0.03m); //default 


            try
            {
                UpdateFunds();
            }
            catch (Exception)
            {
                Logger.WriteLog("Error getting available funds details, please check your gdax credentials");
            }

            AppSettings.MajorSettingsChangEvent += MajorSettingsChangedEventHandler;

            //writeCurrentStrategySmaValues();



            //ShowGraph(gra);


        }


        public void ShowGraph(GraphWindow graphWindow)
        {





            //graphWindow.Show();






            Simulator1 S = null;
            var pl = 0.0;
            Task.Run(() =>
            {

                var settings = AppSettings.GetStrategySettings2("macd_small");

                S = new Simulator1(CurContextValues.ProductName,
                settings.time_interval,
                settings.slow_sma,
                settings.fast_sma, 
                true);


                graphWindow.FillInitialVaues(DateTime.Now.AddDays(-17), DateTime.Now.AddHours(12),
                    settings.time_interval, settings.slow_sma, settings.fast_sma, settings.signal);


                pl = S.Calculate(DateTime.Now.AddDays(-17),
                        DateTime.Now.AddHours(12), settings.signal, true, true);

                S.Dispose();

            }).Wait();


            graphWindow.DrawSeriesSim1(S.CurResultsSeriesList, S.CurResultCrossList, pl);
            //Thread thread = new Thread(() =>
            //{




            //});
            //thread.SetApartmentState(ApartmentState.STA);
            //thread.Start();


            //thread.Join();


        }


        public void SetCurrentIntervalValues(IntervalValues inputValues)
        {
            if (CurContextValues != null)
                CurContextValues.CurrentIntervalValues = inputValues; 
        }

        private void writeCurrentStrategySmaValues()
        {
            if (currentTradeStrategy == null)
                return;

            var smaList = currentTradeStrategy.GetSmaList();

            if (smaList != null)
            {
                foreach (SmaValues sma in smaList)
                {
                    Logger.WriteLog(string.Format("Largest Sma (interval: {0} min, Slices: {1}): {2}", sma.CommonSmaInterval, sma.LargeSmaSlice, sma.largestSmaPrice));
                    Logger.WriteLog(string.Format("Medium Sma (interval: {0} min, Slices: {1}): {2}", sma.CommonSmaInterval, sma.MedSmaSlice, sma.mediumSmaPrice));
                    Logger.WriteLog(string.Format("Smallest Sma (interval: {0} min, Slices: {1}): {2}\n", sma.CommonSmaInterval, sma.SmallSmaSlice, sma.smallestSmaPrice));
                }
            }

        }



        public void TickerDisconnectedHandler(object sender, EventArgs e)
        {
            Logger.WriteLog("Ticker disconnected... pausing all buy / sell");

            if (CurContextValues.UserStartedTrading)
            {
                CurContextValues.StartAutoTrading = false;
                //AutoTradingStoppedEvent?.Invoke(this, EventArgs.Empty);
            }

            TickerDisConnectedEvent?.Invoke(this, EventArgs.Empty);

        }




        public void MajorSettingsChangedEventHandler(object sender, EventArgs args)
        {
            Logger.WriteLog("Settings change detected by manager");


            Task.Run(()=> 
            {
                if (CurContextValues.WaitingBuyOrSell)
                {
                    Logger.WriteLog("waiting for buy/sell to complete bewfore updating settings");
                    Thread.Sleep(5000);
                }
            }).Wait();

            if (!CurContextValues.WaitingBuyOrSell)
            {
                //update strategy settings

                var updateIntervalResult = CreateUpdateStrategyInstance(CurContextValues.CurrentIntervalValues);
                updateIntervalResult.Wait();
            }

        }



        public void TickerConnectedHandler(object sender, EventArgs args)
        {

            while (isBuysyDisposingCurStrategy)
            {
                Logger.WriteLog("Already busy updating intervals after reconnect, waiting for current operation to complete...");
                return;
            }


            //check open orders 

            MyProductOrderBook.SyncOpenOrders();

            if (MyProductOrderBook.MyChaseOrderList.Count == 0)
            {
                Logger.WriteLog("Resetting WaitingBuySell flag to False since no more active orders in list");
                CurContextValues.WaitingBuyOrSell = false;
            }


            Logger.WriteLog("Ticker connected... resuming buy sell");

            //Logger.WriteLog("SMA updating starts here");
            //Task.Run(()=> UpdateSmaParameters(SmaTimeInterval, SmaSlices, true)).Wait();


            //clear serverdata, a redownload will be forced
            MovingAverage.SharedRawExchangeData.Clear();


            var updateIntervalResult = CreateUpdateStrategyInstance(CurContextValues.CurrentIntervalValues);
            updateIntervalResult.Wait();

            Logger.WriteLog("SMA updating ends here");
            //System.Threading.Thread.Sleep(2 * 1000);



            if (CurContextValues.UserStartedTrading)
            {
                Logger.WriteLog("waiting 5 sec for data refresh");
                Thread.Sleep(5 * 1000);
                Logger.WriteLog("buy sell resumed here");

                //check if any orders exists in order list
                //set buy sell waiting flag to off only when there are no order in the orderbook 
                if (CurContextValues.MyOrderBook.MyChaseOrderList.Count == 0)
                    CurContextValues.WaitingBuyOrSell = false;

                CurContextValues.StartAutoTrading = true;
                AutoTradingStartedEvent?.Invoke(this, EventArgs.Empty);
            }

            TickerConnectedEvent?.Invoke(this, EventArgs.Empty);

            UpdateFunds();

            

        }

        public async Task<bool> CreateUpdateStrategyInstance(IntervalValues intervalValues, bool CreateNewInstance = false)
        {

            if (isBuysyDisposingCurStrategy)
            {
                Logger.WriteLog("Already busy updating interval, please wait for current operation to complete.");
                return false;
            }

            isBuysyDisposingCurStrategy = true;

            if (!CreateNewInstance) //instance exists
            {
                try
                {
                    currentTradeStrategy.Dispose();
                }
                catch (Exception ex)
                {
                    Logger.WriteLog("Error disposing strategy, continuing to create new instance" + ex.Message);

                    ////dont stop creating new isntace even if erro occurs
                    //remnants of previous sma may be active eg an un disposed sma 
                    //return false;
                }
                finally
                {
                    currentTradeStrategy = null;
                }
            }



            if (CurContextValues.UserStartedTrading)
                CurContextValues.StartAutoTrading = false;


            ConfigStrategyInUse =  Properties.Settings.Default.StrategyInUse;

            await Task.Run(() =>
            {
                //currentTradeStrategy = new TradeStrategyE(ref CurContextValues, intervalValues);

                switch (ConfigStrategyInUse)
                {
                    case "E":
                        //////Logger.WriteLog("Setting Current Strategy to E");
                        //////currentTradeStrategy = new TradeStrategyE(ref CurContextValues, intervalValues);
                        //////break;

                        Logger.WriteLog("Setting Current Strategy to MACD");


                        var gSettings = AppSettings.GetGeneralSettings();

                        try
                        {
                            currentTradeStrategy = new MacdStrategy(ref CurContextValues, intervalValues);
                            
                        }
                        catch (Exception ex)
                        {
                            Logger.WriteLog("Cant initialize current strategy, please check settings json file... exiting");
                            System.Environment.Exit(0);
                            //throw ex;
                        }

                        
                        break;

                    case "F":
                        Logger.WriteLog("Setting Current Strategy to F");
                        currentTradeStrategy = new TradeStrategyF(ref CurContextValues, intervalValues);
                        break;
                    default:
                        Logger.WriteLog("Using Default Strategy: F");
                        currentTradeStrategy = new TradeStrategyF(ref CurContextValues, intervalValues);
                        break;
                }

                //Logger.WriteLog("Waiting 20 sec for data download to complete ");
                //Thread.Sleep(20 * 1000);
            }).ContinueWith((t) => t.Wait());

            //await createNewTradeInstance(intervalValues);


            var x = UpdateDisplaySmaParameters(CurrentDisplaySmaTimeInterval, CurrentDisplaySmaSlices, true);
            x.Wait();

            isBuysyDisposingCurStrategy = false;
            Logger.WriteLog("Done updating intervals");

            writeCurrentStrategySmaValues();

            return true;
        }

        public void setAvoidExFeesVar(bool inputValue)
        {
            AvoidExchangeFees = inputValue;
            Logger.WriteLog("Avoid exchange fees (post only) is set to " + inputValue.ToString());
            MyProductOrderBook.setAvoidFeeVar(inputValue);
        }


        public async Task<bool> UpdateDisplaySmaParameters(int InputTimerInterval, int InputSlices, bool forceRedownload = false)
        {
            if (DisplaySma == null)
            {
                return false;
            }

            try
            {
                var x = await Task.Run(() => DisplaySma.updateValues(InputTimerInterval, InputSlices, forceRedownload));
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

        private void FillUpdateEventHandler(object sender, EventArgs args)
        {

            //set buy sell waiting flag to off only when there are no order in the orderbook 
            if (CurContextValues.MyOrderBook.MyChaseOrderList.Count == 0)
                CurContextValues.WaitingBuyOrSell = false;

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

                //AppSettings.SaveUpdateStrategySetting("macd", "last_buy_price", filledOrder.filledAtPrice);

                var allStrategies = AppSettings.GetAllStrategies();
                foreach (var s in allStrategies)
                {
                    AppSettings.SaveUpdateStrategySetting(s.StrategyName, "last_buy_price", filledOrder.filledAtPrice);
                }

                AppSettings.Reloadsettings();
            }
            else if (filledOrder.side == "sell")
            {
                CurContextValues.SellOrderFilled = true;
                CurContextValues.BuyOrderFilled = false;

                CurContextValues.WaitingSellFill = false;
                CurContextValues.WaitingBuyFill = true;

                //AppSettings.SaveUpdateStrategySetting("macd", "last_sell_price", filledOrder.filledAtPrice);

                var allStrategies = AppSettings.GetAllStrategies();
                foreach (var s in allStrategies)
                {
                    AppSettings.SaveUpdateStrategySetting(s.StrategyName, "last_sell_price", filledOrder.filledAtPrice);
                }

                AppSettings.Reloadsettings();
            }




            //CurContextValues.WaitingBuyOrSell = false;

            ////set buy sell waiting flag to off only when there are no order in the orderbook 
            //if (CurContextValues.MyOrderBook.MyChaseOrderList.Count == 0) 
            //    CurContextValues.WaitingBuyOrSell = false;


            //if (CurContextValues.ForceSold) 
            //{
            //    ForcedOrderFilledEventArgs tempArgs = new ForcedOrderFilledEventArgs();
            //    tempArgs.filledAtPrice = filledOrder.filledAtPrice;
            //    tempArgs.fillFee = filledOrder.fillFee;
            //    tempArgs.fillSize = filledOrder.fillSize;
            //    tempArgs.Message = filledOrder.Message;
            //    tempArgs.OrderId = filledOrder.OrderId;
            //    tempArgs.ProductName = filledOrder.ProductName;
            //    tempArgs.side = filledOrder.side;
            //    tempArgs.ForcedOrder = true;
            //    //var temp = (ForcedOrderFilledEventArgs)filledOrder;
            //    tempArgs.ForcedOrder = true;
            //    CurContextValues.ForceSold = false;
            //    OrderFilledEvent?.Invoke(this, tempArgs);
            //}
            //else
            //{
            //    OrderFilledEvent?.Invoke(this, filledOrder);
            //}

            OrderFilledEvent?.Invoke(this, filledOrder);

            //update the funds
            UpdateFunds();

            //this.Dispatcher.Invoke(() => updateListView(filledOrder));
        }

        public async Task<bool> StopAndCancel()
        {
            if (CurContextValues.MyOrderBook.MyChaseOrderList.Count() > 0)
            {
                await currentTradeStrategy.CancelCurrentTradeAction();

            }

            CurContextValues.StartAutoTrading = false;

            //test.Wait();

            if (CurContextValues.StartAutoTrading == false)
            {
                Logger.WriteLog("Auto trading stopped");
            }

            if (CurContextValues.MyOrderBook.MyChaseOrderList.Count() == 0)
            {
                //Logger.WriteLog("No orders in order list, setting WaitingBuyOrSell flag to false");
                CurContextValues.WaitingBuyOrSell = false;
            }

            currentTradeStrategy.SetCurrentAction("NOT_SET");

            return true;

        }


        public async Task<bool> ForceSellAtNow(bool marketOrder = false)
        {

            try
            {
                Logger.WriteLog(string.Format("Placing Sell at NOW order "));
                CurContextValues.StartAutoTrading = false; //pause auto trading
                //AutoTradingStoppedEvent?.Invoke(this, EventArgs.Empty);

                CurContextValues.WaitingBuyOrSell = true;

                while (CurContextValues.CurrentBufferedPrice == 0)
                {
                    Logger.WriteLog("waiting for currrent buffered price ");
                    await Task.Run(() => Thread.Sleep(500)).ContinueWith((t) => t.Wait());
                }

                ////var ForceOrderResult = await MyProductOrderBook.PlaceNewOrder("sell", CurContextValues.ProductName, 
                ////    CurContextValues.BuySellAmount.ToString(), CurContextValues.CurrentBufferedPrice.ToString(), true);

                if (marketOrder)
                {
                    var ForceOrderResult = MyProductOrderBook.PlaceNewOrder("sell", CurContextValues.ProductName,
                        CurContextValues.BuySellAmount.ToString(), CurContextValues.CurrentBufferedPrice.ToString(), true, "market");
                    ForceOrderResult.Wait();
                }
                else
                {
                    var ForceOrderResult = MyProductOrderBook.PlaceNewOrder("sell", CurContextValues.ProductName,
                        CurContextValues.BuySellAmount.ToString(), CurContextValues.CurrentBufferedPrice.ToString(), true);
                    ForceOrderResult.Wait();
                }


                //Logger.WriteLog(x.Result.Id);
                //CurContextValues.ForceSold = true;
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
            }

            return true;
        }

        public async Task<bool> ForceBuyAtNow(bool marketOrder = false)
        {

            try
            {
                Logger.WriteLog(string.Format("Placing Buy at NOW order "));
                CurContextValues.StartAutoTrading = false; //pause auto trading
                //AutoTradingStoppedEvent?.Invoke(this, EventArgs.Empty);

                CurContextValues.WaitingBuyOrSell = true;

                while (CurContextValues.CurrentBufferedPrice == 0)
                {
                    Logger.WriteLog("waiting for currrent buffered price ");
                    await Task.Run(()=> Thread.Sleep(500)).ContinueWith((t)=>t.Wait());
                }

                if (marketOrder)
                {
                    var ForceOrderResult = MyProductOrderBook.PlaceNewOrder("buy", CurContextValues.ProductName,
                        CurContextValues.BuySellAmount.ToString(), CurContextValues.CurrentBufferedPrice.ToString(), true, "market");
                    ForceOrderResult.Wait();
                }
                else
                {
                    var ForceOrderResult = MyProductOrderBook.PlaceNewOrder("buy", CurContextValues.ProductName,
                        CurContextValues.BuySellAmount.ToString(), CurContextValues.CurrentBufferedPrice.ToString(), true);
                    ForceOrderResult.Wait();
                }


                //CurContextValues.ForceSold = true;
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
            }

            return true;

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

            //wtf does the following needed for?
            //if (CurContextValues.ForceSold)
            //{
            //    if (CurContextValues.CurrentBufferedPrice <= (CurrentDisplaySmaPrice - CurContextValues.PriceBuffer)) // price below average 
            //    {

            //        PriceBelowAverageEvent?.Invoke(this, EventArgs.Empty);

            //        CurContextValues.ForceSold = false;

            //        if (CurContextValues.UserStartedTrading)
            //        {
            //            StartTrading_ByBuying();
            //        }
            //    }

            //}


            if ((CurContextValues.LastTickerUpdateTime - CurContextValues.LastBuySellTime).TotalMilliseconds < 1000)
            {
                //Logger.WriteLog("price skipped: " + CurrentRealtimePrice);
                return;
            }

            CurContextValues.CurrentBufferedPrice = CurContextValues.CurrentRealtimePrice;



            if (CurContextValues.StartAutoTrading)
            {
                ////////var secSinceLastCrosss = (DateTime.UtcNow.ToLocalTime() - CurContextValues.LastLargeSmaCrossTime).TotalSeconds;
                ////////if (secSinceLastCrosss < (WaitTimeAfterLargeSmaCrossInMin * 60))
                ////////{
                ////////    //if the last time prices crossed is < 2 min do nothing
                ////////    //Logger.WriteLog(string.Format("Waiting {0} sec before placing any new order", Math.Round((sharedWaitTimeAfterCrossInMin * 60) - secSinceLastCrosss)));
                ////////    return;
                ////////}


                if (!CurContextValues.WaitingBuyOrSell)
                {
                    CurContextValues.LastBuySellTime = DateTime.UtcNow.ToLocalTime();

                    //buysell();
                    currentTradeStrategy?.Trade();

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




        private void DisplaySmaChangedEventHandler(object sender, EventArgs args)
        {

            var currentSmaData = (MAUpdateEventArgs)args;
            decimal newSmaPrice = currentSmaData.CurrentMAPrice;

            CurrentDisplaySmaPrice = newSmaPrice;




            CurrentDisplaySmaSlices = currentSmaData.CurrentSlices;// InputSlices;
            CurrentDisplaySmaTimeInterval = currentSmaData.CurrentTimeInterval; // InputTimerInterval;
            //WaitTimeAfterLargeSmaCrossInMin = CurrentDisplaySmaTimeInterval;


            var msg = string.Format("Display SMA updated: {0} (Time interval: {1} Slices: {2})", newSmaPrice, 
                CurrentDisplaySmaTimeInterval, CurrentDisplaySmaSlices);
            Logger.WriteLog(msg);


            DisplaySmaUpdateEvent?.Invoke(this, currentSmaData);
        }


        public void UpdateFunds()
        {

            Funds f = new Funds(MyAuth, CurContextValues.ProductName);
            var af = f.GetAvailableFunds();

            if (af == null)
            {
                Logger.WriteLog("An error occured in getting funds");
                return;
            }

            Logger.WriteLog(String.Format("Funds Updated: \n\tavailable {0}: {1}\n\tavailable {2}: {3}",
                "USD $", af.AvailableDollars.ToString(), f.ProductName, af.AvailableProduct));

            FundsUpdatedEvent?.Invoke(this, new FundsEventArgs { AvaFunds = af});
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
                var maxInactiveTime = 240; // 4 minutes 
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
    public class FundsEventArgs : EventArgs { public AvailableFunds AvaFunds { get; set; } }

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

    //public class ForcedOrderFilledEventArgs: OrderUpdateEventArgs {public bool ForcedOrder { get; set; } }

    public class SmaParamUpdateArgs : EventArgs
    {
        public int NewTimeinterval { get; set; }
        public decimal NewSlices { get; set; }
    }


    public class IntervalValues
    {
        public int LargeIntervalInMin { get; set; }
        public int MediumIntervalInMin { get; set; }
        public int SmallIntervalInMin { get; set; }

        public int LargeSmaSlices { get; set; }
        public int MediumSmaSlice { get; set; }
        public int SmallSmaSlices { get; set; }

        public IntervalValues(int largeInterval, int mediumInterval, int smallInterval)
        {
            try
            {
                if (Properties.Settings.Default.UseUISmaValues == false)
                {
                    Properties.Settings.Default.Reload();

                    Logger.WriteLog("UseUISmaValues is set to false. Using config file sma values.");
                    var configLargeInterval = Convert.ToInt16(Properties.Settings.Default.CommonLargeInterval);
                    var configMediumInterval = Convert.ToInt16(Properties.Settings.Default.ComonMediumInterval);
                    var configSmallInterval = Convert.ToInt16(Properties.Settings.Default.CommonSmallInterval);

                    var configLargeSmaSlice = Convert.ToInt16(Properties.Settings.Default.CommonLargeSlices);
                    var configMediumSmaSlice = Convert.ToInt16(Properties.Settings.Default.CommonMediumSlices);
                    var configSmallSmaSlice = Convert.ToInt16(Properties.Settings.Default.CommonSmallSlices);

                    LargeIntervalInMin = configLargeInterval;
                    MediumIntervalInMin = configMediumInterval;
                    SmallIntervalInMin = configSmallInterval;

                    //defaults 
                    LargeSmaSlices = configLargeSmaSlice;
                    MediumSmaSlice = configMediumSmaSlice;
                    SmallSmaSlices = configSmallSmaSlice;

                    return;
                }
            }
            catch (Exception)
            {
                Logger.WriteLog("Error reading config file for sma values, using ui defaults");
            }


            LargeIntervalInMin = largeInterval;
            MediumIntervalInMin = mediumInterval;
            SmallIntervalInMin = smallInterval;

            //defaults 
            LargeSmaSlices = 60;
            MediumSmaSlice = 55;
            SmallSmaSlices = 28;
        }
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

        public CBAuthenticationContainer Auth { get; set; }

        public DateTime LastTickerUpdateTime { get; set; }
        public DateTime LastBuySellTime { get; set; }
        public DateTime LastLargeSmaCrossTime { get; set; }
        //public DateTime LastSmallSmaCrossTime { get; set; }

        public EventHandler AutoTradingStartEvent;
        public EventHandler AutoTradingStopEvent;

        public MyOrderBook MyOrderBook { get; set; }

        public bool UserStartedTrading { get; set; }

        public IntervalValues CurrentIntervalValues;


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


        //public bool ForceSold { get; set; }
        

        public double WaitTimeAfterBigSmaCrossInMin { get; set; }




        public ContextValues(ref MyOrderBook orderBook, ref TickerClient inputTicker)
        {
            MyOrderBook = orderBook;

            CurrentTicker = inputTicker; 

            UserStartedTrading = false;
            TradeActionList = new List<string>();
            //ForceSold = false;

            BuySellAmount = 0.01m;//default

            //CurrentLargeSmaTimeInterval = 3; //default
            //CurrentLargeSmaSlices = 60; //default

            CurrentAction = "NOT_SET";

            //CurrentSmallSmaTimeInterval = 3; //default
            //CurrentSmallSmaSlices = 55; //default

            PriceBuffer = 0.05m; //default

            CurrentRealtimePrice = 0;
            CurrentBufferedPrice = 0;
            //CurrentLargeSmaPrice = 0;

            BuyOrderFilled = false;
            SellOrderFilled = false;

            WaitingSellFill = false;
            WaitingBuyFill = false;
            WaitingBuyOrSell = false;

            WaitTimeAfterBigSmaCrossInMin = 5; //buy sell every time interval

            MaxBuy = 5;
            MaxSell = 5;

            StartAutoTrading = false;
            //AutoTradingStoppedEvent?.Invoke(this, EventArgs.Empty);


            LastLargeSmaCrossTime = DateTime.UtcNow.ToLocalTime();
            //LastSmallSmaCrossTime = DateTime.UtcNow.ToLocalTime();

        }

        


    }


}









