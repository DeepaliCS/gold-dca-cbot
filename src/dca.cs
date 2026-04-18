using System;
using System.Linq;
using cAlgo.API;

namespace cAlgo.Robots
{
    [Robot(AccessRights = AccessRights.None, AddIndicators = true)]
    public class GoldDcaBot : Robot
    {
        // ============================================================
        // GROUP 1: POSITION SIZING
        // ============================================================
        [Parameter("Base Volume (Lots)", Group = "Sizing", DefaultValue = 0.01, MinValue = 0.01, Step = 0.01)]
        public double BaseVolumeLots { get; set; }

        [Parameter("Volume Step (Lots)", Group = "Sizing", DefaultValue = 0.01, MinValue = 0.01, Step = 0.01)]
        public double VolumeStepLots { get; set; }

        [Parameter("Positions Per Step", Group = "Sizing", DefaultValue = 3, MinValue = 1)]
        public int PositionsPerStep { get; set; }

        // ============================================================
        // GROUP 2: ENTRY LOGIC
        // ============================================================
        [Parameter("Dip Trigger (Pips)", Group = "Entry", DefaultValue = 50, MinValue = 1)]
        public double DipTriggerPips { get; set; }

        [Parameter("DCA Spacing (Pips)", Group = "Entry", DefaultValue = 30, MinValue = 1)]
        public double DcaSpacingPips { get; set; }

        [Parameter("Lookback Bars", Group = "Entry", DefaultValue = 20, MinValue = 2)]
        public int LookbackBars { get; set; }

        // ============================================================
        // GROUP 3: EXIT LOGIC
        // ============================================================
        [Parameter("Profit Target (GBP)", Group = "Exit", DefaultValue = 200, MinValue = 1)]
        public double ProfitTargetGbp { get; set; }

        [Parameter("Max Positions Safety Cap", Group = "Exit", DefaultValue = 20, MinValue = 1)]
        public int MaxPositionsSafetyCap { get; set; }

        // ============================================================
        // GROUP 4: SAFETY
        // ============================================================
        [Parameter("Equity Stop (% of Start)", Group = "Safety", DefaultValue = 70, MinValue = 1, MaxValue = 99)]
        public double EquityStopPercent { get; set; }

        // ============================================================
        // GROUP 5: SESSION FILTERS
        // ============================================================
        [Parameter("Session Start Hour UTC", Group = "Sessions", DefaultValue = 0, MinValue = 0, MaxValue = 23)]
        public int SessionStartHourUtc { get; set; }

        [Parameter("Session End Hour UTC", Group = "Sessions", DefaultValue = 12, MinValue = 0, MaxValue = 23)]
        public int SessionEndHourUtc { get; set; }

        [Parameter("Close All Hour UTC", Group = "Sessions", DefaultValue = 20, MinValue = 0, MaxValue = 23)]
        public int CloseAllHourUtc { get; set; }

        [Parameter("Block Fridays", Group = "Sessions", DefaultValue = true)]
        public bool BlockFridays { get; set; }

        // ============================================================
        // GROUP 6: MISC
        // ============================================================
        [Parameter("Bot Label", Group = "Misc", DefaultValue = "GoldDCA")]
        public string BotLabel { get; set; }

        [Parameter("Enable Debug Log", Group = "Misc", DefaultValue = true)]
        public bool EnableDebugLog { get; set; }

        // ============================================================
        // PRIVATE STATE
        // ============================================================
        private double _startingBalance;
        private double _equityStopLevel;
        private bool _equityStopTriggered;
        private DateTime _lastHeartbeat = DateTime.MinValue;

        // ============================================================
        // LIFECYCLE METHODS
        // ============================================================
        protected override void OnStart()
        {
            _startingBalance = Account.Balance;
            _equityStopLevel = _startingBalance * (EquityStopPercent / 100.0);
            _equityStopTriggered = false;

            Print("========================================");
            Print("GoldDcaBot started");
            Print("Symbol: {0}", SymbolName);
            Print("Starting balance: £{0:F2}", _startingBalance);
            Print("Equity stop level: £{0:F2} ({1}%)", _equityStopLevel, EquityStopPercent);
            Print("Profit target: £{0:F2}", ProfitTargetGbp);
            Print("Session: {0}:00 - {1}:00 UTC", SessionStartHourUtc, SessionEndHourUtc);
            Print("Close-all time: {0}:00 UTC", CloseAllHourUtc);
            Print("Block Fridays: {0}", BlockFridays);
            Print("========================================");
        }

        protected override void OnTick()
        {
            Heartbeat();
            LogFilterStatus();

            // STAGE 4: Exit check — profit target
            // Runs every tick regardless of session (always monitor open positions)
            if (GetBasketPositions().Any() && GetBasketNetProfit() >= ProfitTargetGbp)
            {
                CloseAllBasketPositions($"Profit target £{ProfitTargetGbp} hit");
                return;
            }

            // STAGE 3: Entry logic (session-gated)
            if (IsPastCloseTime()) return;
            if (IsFriday()) return;
            if (!IsTradingSessionOpen()) return;

            // Entry: first or DCA
            if (GetBasketPositions().Any())
                TryOpenDcaEntry();
            else
                TryOpenFirstEntry();
        }
        protected override void OnStop()
        {
            Print("GoldDcaBot stopped. Final balance: £{0:F2}", Account.Balance);
        }

        // ============================================================
        // HEARTBEAT (DEBUG ONLY)
        // ============================================================
        private void Heartbeat()
        {
            if (!EnableDebugLog) return;
            if ((Server.Time - _lastHeartbeat).TotalSeconds < 60) return;

            Print("[Heartbeat] {0} UTC | Bid: {1} | Equity: £{2:F2}",
                  Server.Time.ToUniversalTime().ToString("HH:mm:ss"),
                  Symbol.Bid,
                  Account.Equity);
            _lastHeartbeat = Server.Time;
        }
        private DateTime _lastFilterLog = DateTime.MinValue;

        private void LogFilterStatus()
        {
            // Log filter status once per 5 minutes (not every tick)
            if ((Server.Time - _lastFilterLog).TotalMinutes < 5) return;

            var sessionOk = IsTradingSessionOpen();
            var isFri = IsFriday();
            var pastClose = IsPastCloseTime();

            string verdict;
            if (pastClose) verdict = "UNTRADEABLE (after daily cut-off time)";
            else if (isFri) verdict = "UNTRADEABLE (Friday)";
            else if (!sessionOk) verdict = "UNTRADEABLE (outside Asian/London session)";
            else verdict = "TRADEABLE (all filters pass)";

            Print("[Filter] {0} UTC | Session: {1} | Friday: {2} | PastClose: {3} | → {4}",
                  Server.Time.ToUniversalTime().ToString("ddd HH:mm"),
                  sessionOk ? "OK" : "closed",
                  isFri ? "YES" : "no",
                  pastClose ? "YES" : "no",
                  verdict);

            _lastFilterLog = Server.Time;
        }

        // ============================================================
        // METHOD STUBS — future stages
        // ============================================================
        // ============================================================
        // FILTERS
        // ============================================================
        private bool IsTradingSessionOpen()
        {
            var hourUtc = Server.Time.ToUniversalTime().Hour;
            return hourUtc >= SessionStartHourUtc && hourUtc < SessionEndHourUtc;
        }

        private bool IsFriday()
        {
            if (!BlockFridays) return false;
            return Server.Time.ToUniversalTime().DayOfWeek == DayOfWeek.Friday;
        }

        private bool IsPastCloseTime()
        {
            var hourUtc = Server.Time.ToUniversalTime().Hour;
            return hourUtc >= CloseAllHourUtc;
        }
        private bool CheckEquityStop() { return false; }
        private double GetRecentHigh()
        {
            double high = double.MinValue;
            int bars = Math.Min(LookbackBars, Bars.HighPrices.Count);
            for (int i = 1; i <= bars; i++)
            {
                if (Bars.HighPrices.Last(i) > high)
                    high = Bars.HighPrices.Last(i);
            }
            return high;
        }
        private void TryOpenFirstEntry()
        {
            // Only open first entry if basket is empty
            if (GetBasketPositions().Any()) return;

            var recentHigh = GetRecentHigh();
            var dipPips = (recentHigh - Symbol.Ask) / Symbol.PipSize;

            if (dipPips >= DipTriggerPips)
            {
                double volume = CalculateVolumeForPosition(1);  // always position #1
                OpenBuy(volume,
                        $"First entry — dip {dipPips:F1} pips from high {recentHigh:F2}");
            }
        }
        private void TryOpenDcaEntry()
        {
            var positions = GetBasketPositions();
            if (!positions.Any()) return;  // need a first entry to DCA from

            // Safety cap — don't add beyond max
            if (positions.Length >= MaxPositionsSafetyCap)
            {
                if (EnableDebugLog)
                    Print("[DCA skipped] Already at safety cap of {0} positions", MaxPositionsSafetyCap);
                return;
            }

            // Find the lowest entry price in the basket
            var lowestEntry = positions.Min(p => p.EntryPrice);
            var dropPips = (lowestEntry - Symbol.Ask) / Symbol.PipSize;

            if (dropPips >= DcaSpacingPips)
            {
                // Calculate the next position's size
                int nextPositionNumber = positions.Length + 1;  // 1-indexed
                double volume = CalculateVolumeForPosition(nextPositionNumber);

                OpenBuy(volume,
                        $"DCA #{nextPositionNumber} — {dropPips:F1} pips below lowest entry {lowestEntry:F2}");
            }
        }
        private double CalculateVolumeForPosition(int positionNumber)
        {
            // Every PositionsPerStep positions, size grows by VolumeStepLots
            // Position 1-3: Base
            // Position 4-6: Base + 1 step
            // Position 7-9: Base + 2 steps, etc.
            int stepsApplied = (positionNumber - 1) / PositionsPerStep;
            return BaseVolumeLots + (stepsApplied * VolumeStepLots);
        }
        private void OpenBuy(double volumeInLots, string reason)
        {
            var volumeInUnits = Symbol.QuantityToVolumeInUnits(volumeInLots);
            var result = ExecuteMarketOrder(TradeType.Buy, SymbolName, volumeInUnits, BotLabel);

            if (result.IsSuccessful)
                Print("[BUY] {0} lots @ {1:F2} — {2}", volumeInLots, result.Position.EntryPrice, reason);
            else
                Print("[BUY FAILED] {0}", result.Error);
        }
        private Position[] GetBasketPositions()
        {
            return Positions
                .Where(p => p.SymbolName == SymbolName
                         && p.Label == BotLabel
                         && p.TradeType == TradeType.Buy)
                .ToArray();
        }
        private double GetBasketNetProfit()
        {
            return GetBasketPositions().Sum(p => p.NetProfit);
        }
        private void CloseAllBasketPositions(string reason)
        {
            var positions = GetBasketPositions();
            if (!positions.Any()) return;

            var profit = GetBasketNetProfit();
            Print("[CLOSE BASKET] Closing {0} positions | Net P/L: £{1:F2} | Reason: {2}",
                  positions.Length, profit, reason);

            foreach (var pos in positions)
            {
                var result = ClosePosition(pos);
                if (!result.IsSuccessful)
                    Print("[CLOSE FAILED] Position {0}: {1}", pos.Id, result.Error);
            }
        }
    }
}