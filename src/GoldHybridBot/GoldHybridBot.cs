using System;
using System.Linq;
using cAlgo.API;

namespace cAlgo.Robots
{
    [Robot(AccessRights = AccessRights.None, AddIndicators = true)]
    public class GoldHybridBot : Robot
    {
        // ============================================================
        // PARAMETERS
        // ============================================================
        [Parameter("Fixed Profit Target (GBP)", Group = "Exit", DefaultValue = 200, MinValue = 1)]
        public double FixedProfitTargetGbp { get; set; }

        [Parameter("Use Anchor TP (instead of fixed £)", Group = "Exit", DefaultValue = true)]
        public bool UseAnchorTp { get; set; }

        [Parameter("Equity Stop Loss (GBP)", Group = "Safety", DefaultValue = 1000, MinValue = 1)]
        public double EquityStopGbp { get; set; }

        [Parameter("Enable Debug Log", Group = "Misc", DefaultValue = true)]
        public bool EnableDebugLog { get; set; }

        [Parameter("Show Chart Label", Group = "Misc", DefaultValue = true)]
        public bool ShowChartLabel { get; set; }

        // ============================================================
        // PRIVATE STATE
        // ============================================================
        private double _startingBalance;
        private double _equityStopLevel;
        private bool _equityStopTriggered;
        private DateTime _lastHeartbeat = DateTime.MinValue;
        private DateTime _lastProjectionLog = DateTime.MinValue;
        private const string ChartLabelName = "HybridBotLabel";

        // ============================================================
        // LIFECYCLE METHODS
        // ============================================================
        protected override void OnStart()
        {
            _startingBalance = Account.Balance;
            _equityStopLevel = _startingBalance - EquityStopGbp;
            _equityStopTriggered = false;

            Print("========================================");
            Print("GoldHybridBot started — VERSION 1.1");
            Print("GoldHybridBot started");
            Print("Symbol: {0}", SymbolName);
            Print("Starting balance: £{0:F2}", _startingBalance);
            Print("Equity stop at: £{0:F2} (lose £{1} stops bot)", _equityStopLevel, EquityStopGbp);
            Print("Fixed profit target: £{0:F2}", FixedProfitTargetGbp);
            Print("Use anchor TP: {0}", UseAnchorTp);
            Print("========================================");
            Print("⚠️  You manage entries & sizing. Bot manages exits & safety.");
            Print("========================================");
        }

        protected override void OnTick()
        {
            Heartbeat();

            // Safety: equity stop — highest priority
            if (CheckEquityStop()) return;

            var positions = GetBasketPositions();
            if (!positions.Any())
            {
                ClearChartLabel();
                return;
            }

            // Update the live projection (log + chart label)
            UpdateProjection(positions);

            // Check exit conditions
            CheckExitConditions(positions);
        }

        protected override void OnStop()
        {
            ClearChartLabel();
            Print("GoldHybridBot stopped. Final balance: £{0:F2}", Account.Balance);
        }

        // ============================================================
        // EXIT LOGIC
        // ============================================================
        private void CheckExitConditions(Position[] positions)
        {
            double basketProfit = positions.Sum(p => p.NetProfit);

            // Option 1: Anchor TP triggered (price reached highest TP)
            if (UseAnchorTp)
            {
                double? anchorTp = GetAnchorTp(positions);
                if (anchorTp.HasValue && Symbol.Bid >= anchorTp.Value)
                {
                    CloseAll($"Anchor TP {anchorTp.Value:F2} reached. Basket P/L: £{basketProfit:F2}");
                    return;
                }
            }

            // Option 2: Fixed GBP target (fallback / secondary)
            if (basketProfit >= FixedProfitTargetGbp)
            {
                CloseAll($"Fixed target £{FixedProfitTargetGbp} hit. Basket P/L: £{basketProfit:F2}");
                return;
            }
        }

        // Returns the highest TP set on any open position, or null if none have a TP
        private double? GetAnchorTp(Position[] positions)
        {
            var withTp = positions.Where(p => p.TakeProfit.HasValue).ToArray();
            if (!withTp.Any()) return null;
            return withTp.Max(p => p.TakeProfit.Value);
        }

        // ============================================================
        // PROJECTION DISPLAY (log + chart)
        // ============================================================
        private void UpdateProjection(Position[] positions)
        {
            double? anchorTp = GetAnchorTp(positions);
            double currentProfit = positions.Sum(p => p.NetProfit);

            // Estimate profit if price reached anchor TP
            double? projectedProfit = null;
            if (anchorTp.HasValue)
            {
                projectedProfit = positions.Sum(p =>
                {
                    // Rough P/L estimate: (anchorTp - entry) * volume * contract size / quote
                    double priceDiff = anchorTp.Value - p.EntryPrice;
                    double pipValue = Symbol.PipValue;
                    double pipsToTp = priceDiff / Symbol.PipSize;
                    return pipsToTp * pipValue * (p.VolumeInUnits / Symbol.LotSize);
                });
            }

            // Chart label
            if (ShowChartLabel)
                DrawChartLabel(anchorTp, currentProfit, projectedProfit, positions.Length);

            // Log every 30 seconds
            if ((Server.Time - _lastProjectionLog).TotalSeconds < 30) return;
            _lastProjectionLog = Server.Time;

            if (anchorTp.HasValue)
            {
                double distancePips = (anchorTp.Value - Symbol.Bid) / Symbol.PipSize;
                Print("[Projected] Anchor TP: {0:F2} | Distance: {1:F1} pips | Current: £{2:F2} | At TP: £{3:F2}",
                      anchorTp.Value, distancePips, currentProfit, projectedProfit ?? 0);
            }
            else
            {
                Print("[Projected] No anchor TP set | Current: £{0:F2} | Fixed target: £{1:F2}",
                      currentProfit, FixedProfitTargetGbp);
            }
        }

        private void DrawChartLabel(double? anchorTp, double currentProfit, double? projectedProfit, int posCount)
        {
            string text;
            if (anchorTp.HasValue)
            {
                text = string.Format(
                    "━━━ GoldHybridBot ━━━\nPositions: {0}\nCurrent P/L: £{1:F2}\nAnchor TP: {2:F2}\nProjected at TP: £{3:F2}",
                    posCount, currentProfit, anchorTp.Value, projectedProfit ?? 0);
            }
            else
            {
                text = string.Format(
                    "━━━ GoldHybridBot ━━━\nPositions: {0}\nCurrent P/L: £{1:F2}\nNo anchor TP set\nFixed target: £{2:F2}",
                    posCount, currentProfit, FixedProfitTargetGbp);
            }

            var label = Chart.DrawStaticText(ChartLabelName, text, 
                VerticalAlignment.Top, HorizontalAlignment.Left, Color.Yellow);
            label.FontSize = 12;
        }

        private void ClearChartLabel()
        {
            Chart.RemoveObject(ChartLabelName);
        }

        // ============================================================
        // SAFETY — EQUITY STOP
        // ============================================================
        private bool CheckEquityStop()
        {
            if (_equityStopTriggered) return true;

            if (Account.Equity <= _equityStopLevel)
            {
                _equityStopTriggered = true;
                Print("========================================");
                Print("⚠️  EQUITY STOP TRIGGERED");
                Print("Equity: £{0:F2} | Threshold: £{1:F2}", Account.Equity, _equityStopLevel);
                Print("Closing all positions. Bot locked out.");
                Print("========================================");
                CloseAll("Equity stop triggered");
                return true;
            }

            return false;
        }

        // ============================================================
        // POSITION HELPERS
        // ============================================================
        // Tracks ALL open positions on this symbol, any label
        private Position[] GetBasketPositions()
        {
            return Positions
                .Where(p => p.SymbolName == SymbolName)
                .ToArray();
        }

        private void CloseAll(string reason)
        {
            var positions = GetBasketPositions();
            if (!positions.Any()) return;

            double totalProfit = positions.Sum(p => p.NetProfit);
            Print("[CLOSE ALL] {0} positions | Net P/L: £{1:F2} | Reason: {2}",
                  positions.Length, totalProfit, reason);

            foreach (var p in positions)
            {
                var result = ClosePosition(p);
                if (!result.IsSuccessful)
                    Print("[CLOSE FAILED] Position {0}: {1}", p.Id, result.Error);
            }
        }

        // ============================================================
        // HEARTBEAT
        // ============================================================
        private void Heartbeat()
        {
            if (!EnableDebugLog) return;
            if ((Server.Time - _lastHeartbeat).TotalSeconds < 60) return;

            var positions = GetBasketPositions();
            Print("[Heartbeat] {0} UTC | Bid: {1} | Equity: £{2:F2} | Positions: {3}",
                  Server.Time.ToUniversalTime().ToString("HH:mm:ss"),
                  Symbol.Bid,
                  Account.Equity,
                  positions.Length);
            _lastHeartbeat = Server.Time;
        }
    }
}
