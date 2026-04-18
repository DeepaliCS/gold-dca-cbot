using System;
using System.Linq;
using cAlgo.API;

namespace cAlgo.Robots

// test 1233
{
    [Robot(AccessRights = AccessRights.None, AddIndicators = true)]
    public class GoldDcaBot : Robot
    {
        [Parameter("Volume (Lots)", DefaultValue = 0.01, MinValue = 0.01, Step = 0.01)]
        public double VolumeInLots { get; set; }

        [Parameter("Dip Trigger (Pips)", DefaultValue = 50, MinValue = 1)]
        public double DipTriggerPips { get; set; }

        [Parameter("Profit Target", DefaultValue = 10.0, MinValue = 0.1)]
        public double ProfitTarget { get; set; }

        [Parameter("Lookback Bars", DefaultValue = 20, MinValue = 2)]
        public int HighLookbackBars { get; set; }

        [Parameter("Bot Label", DefaultValue = "GoldDCA")]
        public string BotLabel { get; set; }

        private DateTime _lastHeartbeat = DateTime.MinValue;

        protected override void OnStart()
        {
            Print("=== Bot started VERSION 2 at {0} | Balance: {1:F2} ===", 
                Server.Time, Account.Balance);
        }



        protected override void OnTick()
        {

            // Heartbeat — prints every 60 seconds so we can see bot is alive
            if ((Server.Time - _lastHeartbeat).TotalSeconds >= 60)
            {
                Print("[Heartbeat] {0} | Bid: {1} | Ask: {2}", 
                    Server.Time.ToString("HH:mm:ss"), Symbol.Bid, Symbol.Ask);
                _lastHeartbeat = Server.Time;
            }

            var myPositions = GetMyPositions();

            // Exit: close if profit target hit
            if (myPositions.Any() && myPositions.Sum(p => p.NetProfit) >= ProfitTarget)
            {
                foreach (var pos in myPositions) ClosePosition(pos);
                Print("Basket closed at target");
                return;
            }

            // Entry: buy on dip if no open position
            if (!myPositions.Any())
            {
                var high = GetRecentHigh();
                var dipPips = (high - Symbol.Ask) / Symbol.PipSize;

                if (dipPips >= DipTriggerPips)
                {
                    var units = Symbol.QuantityToVolumeInUnits(VolumeInLots);
                    var result = ExecuteMarketOrder(TradeType.Buy, SymbolName, units, BotLabel);
                    if (result.IsSuccessful)
                        Print("BUY at {0}, dip {1:F1} pips", result.Position.EntryPrice, dipPips);
                }
            }
        }

        private Position[] GetMyPositions() =>
            Positions.Where(p => p.SymbolName == SymbolName 
                              && p.Label == BotLabel
                              && p.TradeType == TradeType.Buy).ToArray();

        private double GetRecentHigh()
        {
            double high = double.MinValue;
            int bars = Math.Min(HighLookbackBars, Bars.HighPrices.Count);
            for (int i = 1; i <= bars; i++)
                if (Bars.HighPrices.Last(i) > high) high = Bars.HighPrices.Last(i);
            return high;
        }

        protected override void OnStop() => Print("Bot stopped");
    }
}
// MARKER 1776526735
