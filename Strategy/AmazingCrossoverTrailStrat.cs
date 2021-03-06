#region Using declarations
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Indicator;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Strategy;
#endregion

// This namespace holds all strategies and is required. Do not change it.
namespace NinjaTrader.Strategy
{
    /// <summary>
    /// Enter the description of your strategy here
    /// </summary>
    [Description("Enter the description of your strategy here")]
    public class AmazingCrossoverTrailStrat : Strategy
    {

        #region userdefined

        #region Variables
        private int _counter = 1; // Default for Counter

        protected double _equity = 1000000; // Default for Equity
        protected double _percentRisk = 1.0; // Default for PercentRisk
        protected double _lossLevel;

        protected IOrder _entry = null, _exit = null;

        #endregion

        protected void GoFlat()
        {
            if (IsLong) _exit = ExitLong();
            if (IsShort) _exit = ExitShort();
        }

        protected int ComputeQty(double volatilityRisk)
        {
            double dollarRisk = _equity * (_percentRisk / 100.0);
            double tickRisk = Round2Tick(volatilityRisk / this.TickSize);
            double qty = (dollarRisk / (volatilityRisk * this.PointValue));

            int rounded;

            // round the shares into a lot-friendly number, applies only to stocks
            //			rounded = (int) (Math.Round(qty/100.0, 0) * 100.0);

            rounded = (int)Math.Round(qty, 0);

            //			P("vol risk=" + volatilityRisk.ToString("N2") 
            //				+ ", $ risk=" + dollarRisk.ToString("C2") 
            //				+ ", equity=" + _equity.ToString("C2")
            //				+ ", qty=" + qty.ToString("N0") 
            //				+ ", rounded=" + rounded.ToString("N0")
            //				+ ", price=" + Close[0].ToString());

            return rounded;
        }

        protected void DrawLossLevel()
        {
            if (IsFlat) return;

            Color color = Color.Black;

            if (IsLong)
                color = Color.Magenta;
            else if (IsShort)
                color = Color.Cyan;

            this.DrawDiamond("d" + CurrentBar, true, 0, _lossLevel, color);
        }

        protected bool StillHaveMoney { get { return _equity > 0; } }

        #region Helpers
        protected bool IsFlat { get { return Position.MarketPosition == MarketPosition.Flat; } }
        protected bool IsLong { get { return Position.MarketPosition == MarketPosition.Long; } }
        protected bool IsShort { get { return Position.MarketPosition == MarketPosition.Short; } }

        protected double Round2Tick(double val) { return Instrument.MasterInstrument.Round2TickSize(val); }
        protected double PointValue { get { return this.Instrument.MasterInstrument.PointValue; } }

        protected void P(string msg)
        {
            Print(Time[0].ToShortDateString() + " " + Time[0].ToShortTimeString() + "::" + msg);
        }
        #endregion

        #region Properties

        [Description("Initial Equity of the account")]
        [GridCategory("Account")]
        public double Equity
        {
            get { return _equity; }
            set { _equity = Math.Max(1, value); }
        }

        [Description("Initial Equity of the account")]
        [GridCategory("Account")]
        public double PercentRisk
        {
            get { return _percentRisk; }
            set { _percentRisk = Math.Max(0.001, value); }
        }

        [Description("A counter used for optimization runs")]
        [GridCategory("Account")]
        public int RunCounter
        {
            get { return _counter; }
            set { _counter = Math.Max(1, value); }
        }

        #endregion

        #endregion

        #region IndicatorVariables
        private int _emaSlowPeriod = 10; // Default setting for EMASlowPeriod
        private int _emaFastPeriod = 5; // Default setting for EMAFastPeriod
        private int _rsiPeriod = 10; // Default setting for RSIPeriod
        private int _adxPeriod = 10; // Default setting for ADXPeriod
        private int _atrPeriod = 10;
        private double _atrExclusionMultiplier = 1;
        private int _adxMin = 20;
        private int _rsiLower = 45;
        private int _rsiUpper = 55;
        private int _crossoverLookbackPeriod = 1;

        #endregion

        #region StrategyVariables

        private AmazingCrossoverIndi _indi;

        private int _mmProfitTicksBeforeBreakeven = 25;
        private int _mmInitialSL = 30;
        private int _mmBreakevenTicks = 2;
        private int _mmTrailTicks = 25;
        private TradeState _tradeState = TradeState.InitialStop;
        #endregion

        #region enum
        public enum TradeState
        {
            InitialStop,
            Breakeven,
            Trailing
        }
        #endregion

        /// <summary>
        /// This method is used to configure the strategy and is called once before any strategy method is called.
        /// </summary>
        protected override void Initialize()
        {
            if (_indi == null)
            {
                _indi = AmazingCrossoverIndi(_adxMin, _adxPeriod, _atrExclusionMultiplier, _atrPeriod,
                             _crossoverLookbackPeriod, _emaFastPeriod, _emaSlowPeriod, _rsiLower, _rsiPeriod,
                             _rsiUpper);
                _indi.SetupObjects();
                Add(_indi);
            }

            this.ClearOutputWindow();

            CalculateOnBarClose = true;
        }


        /// <summary>
        /// Called on each bar update event (incoming tick)
        /// </summary>
        protected override void OnBarUpdate()
        {

            if (CurrentBar <= BarsRequired) return;

            if (StillHaveMoney)
            {
                
                var x = _indi.EMAFastPlot[0];

                if (IsFlat)
                {
                    SetStopLoss(CalculationMode.Ticks, _mmInitialSL);
                    LookForTrade();
                }
                else
                {
                    ManageTrade();
                }
            }
        }

        private void LookForTrade()
        {
            if (_indi.Signal == 0) return;
            
            double risk = TickSize*_mmInitialSL;
            if (_indi.Signal == 1)
            {
                _lossLevel = Close[0] - risk;
                _entry = EnterLong(ComputeQty(risk));
            }
            else if (_indi.Signal == -1)
            {
                _lossLevel = Close[0] + risk;
                _entry = EnterShort(ComputeQty(risk));
            }
            _tradeState = TradeState.InitialStop;

        }


        private void ManageTrade()
        {

            if (IsLong)
            {
                switch (_tradeState)
                {
                    case TradeState.InitialStop:
                        // switch to breakeven if possible and start trailing
                        if (Close[0] > Position.AvgPrice + (TickSize*_mmProfitTicksBeforeBreakeven))
                        {
                            _lossLevel = Position.AvgPrice + TickSize * _mmBreakevenTicks;
                            SetStopLoss(CalculationMode.Price, _lossLevel);
                            _tradeState = TradeState.Trailing;
                        }
                        break;

                    case TradeState.Trailing:
                        if (Close[0] - TickSize*_mmTrailTicks > _lossLevel)
                        {
                            _lossLevel = Close[0] - TickSize* _mmTrailTicks;
                            SetStopLoss(CalculationMode.Price, _lossLevel);
                        }
                        break;
                }

            }
            else if (IsShort)
            {
                switch (_tradeState)
                {
                    case TradeState.InitialStop:
                        // switch to breakeven if possible and start trailing
                        if (Close[0] < Position.AvgPrice - (TickSize * _mmProfitTicksBeforeBreakeven))
                        {
                            _lossLevel = Position.AvgPrice - TickSize * _mmBreakevenTicks;
                            SetStopLoss(CalculationMode.Price, _lossLevel);
                            _tradeState = TradeState.Trailing;
                        }
                        break;

                    case TradeState.Trailing:
                        if (Close[0] + TickSize * _mmTrailTicks < _lossLevel)
                        {
                            _lossLevel = Close[0] + TickSize * _mmTrailTicks;
                            SetStopLoss(CalculationMode.Price, _lossLevel);
                        }
                        break;
                }
            }
            DrawLossLevel();
        }

        protected override void OnExecution(IExecution execution)
        {
            if (_entry == null) return;
            if (execution == null) return;
            if (execution.Order == null) return;

            bool isEntry = (_entry.Token == execution.Order.Token);
            bool isExit = !isEntry;

            if (isExit)
            {
                double diff = 0;

                IOrder exit = execution.Order;
                if (_entry.OrderAction == OrderAction.Buy)
                    diff = exit.AvgFillPrice - _entry.AvgFillPrice;
                else if (_entry.OrderAction == OrderAction.SellShort)
                    diff = _entry.AvgFillPrice - exit.AvgFillPrice;

                double profit = ((diff * this.PointValue)) * _entry.Quantity;
                _equity += profit;

                //				P("Profit=" + profit.ToString("C2") + ", Equity=" + _equity.ToString("C2"));
            }
        }



        #region Properties
        [Description("Period for the slow EMA")]
        [GridCategory("Indicator")]
        public int EMASlowPeriod
        {
            get { return _emaSlowPeriod; }
            set { _emaSlowPeriod = Math.Max(1, value); }
        }

        [Description("Period for the fast EMA")]
        [GridCategory("Indicator")]
        public int EMAFastPeriod
        {
            get { return _emaFastPeriod; }
            set { _emaFastPeriod = Math.Max(1, value); }
        }

        [Description("Period for RSI, applied to median")]
        [GridCategory("Indicator")]
        public int RSIPeriod
        {
            get { return _rsiPeriod; }
            set { _rsiPeriod = Math.Max(1, value); }
        }




        [Description("Period for RSI lower")]
        [GridCategory("Indicator")]
        public int RSILower
        {
            get { return _rsiLower; }
            set { _rsiLower = Math.Max(1, value); }
        }
        [Description("Period for RSI upper")]
        [GridCategory("Indicator")]
        public int RSIUpper
        {
            get { return _rsiUpper; }
            set { _rsiUpper = Math.Max(1, value); }
        }


        [Description("Period for ADX")]
        [GridCategory("Indicator")]
        public int ADXPeriod
        {
            get { return _adxPeriod; }
            set { _adxPeriod = Math.Max(1, value); }
        }
        [Description("Minimum for ADX")]
        [GridCategory("Indicator")]
        public int ADXMinimum
        {
            get { return _adxMin; }
            set { _adxMin = Math.Max(1, value); }
        }

        [Description("Period for ATR")]
        [GridCategory("Indicator")]
        public int ATRPeriod
        {
            get { return _atrPeriod; }
            set { _atrPeriod = Math.Max(1, value); }
        }

        [Description("ATR multiplier for exluding trades")]
        [GridCategory("Indicator")]
        public double ATRExclusionMultiplier
        {
            get { return _atrExclusionMultiplier; }
            set { _atrExclusionMultiplier = Math.Max(1, value); }
        }


        [Description("Lookback period for crossover convergence")]
        [GridCategory("Indicator")]
        public int CrossoverLookbackPeriod
        {
            get { return _crossoverLookbackPeriod; }
            set { _crossoverLookbackPeriod = Math.Max(1, value); }
        }


        [Description("Ticks in profit before moving stoploss to breakeven(-ish)")]
        [GridCategory("Money management")]
        public int ProfitTicksBeforeBreakeven
        {
            get { return _mmProfitTicksBeforeBreakeven; }
            set { _mmProfitTicksBeforeBreakeven = Math.Max(1, value); }
        }

        [Description("Initial stoploss in ticks")]
        [GridCategory("Money management")]
        public int InitialStoploss
        {
            get { return _mmInitialSL; }
            set { _mmInitialSL = Math.Max(1, value); }
        }


        [Description("Ticksbeyond breakeven to move from initial stop")]
        [GridCategory("Money management")]
        public int BreakevenTicks
        {
            get { return _mmBreakevenTicks; }
            set { _mmBreakevenTicks = Math.Max(1, value); }
        }

        [Description("Trailing stop ticks, when starting to trail from breakeven")]
        [GridCategory("Money management")]
        public int TrailTicks
        {
            get { return _mmTrailTicks; }
            set { _mmTrailTicks = Math.Max(1, value); }
        }

        #endregion
   
    }
}
