// ============================================================================
// GoldDCA Pro — Deepali Kerai
// Version: 1.5 (cTrader)   Date: 2026-04-23
// Platform: cTrader / cAlgo (C#)
// ============================================================================
// CHANGELOG:
// v1.5-cT (2026-04-23) — Added COOLDOWN + DD TIGHTENING system
//                         Tracks consecutive stop-outs within a time window
//                         1 recent stop → skip next session
//                         2 recent stops → pause 24h + halve DD next attempt
//                         3 recent stops → pause 72h
//                         Winning basket resets counters
//                         Dashboard shows cooldown status
//
// v1.4-cT (2026-04-23) — FIXED equity stop logic — now uses "Max Drawdown %"
// v1.3-cT (2026-04-23) — Added TIER-BASED DYNAMIC RISK system
// v1.2-cT (2026-04-23) — Port from MQL5 v1.2 to cTrader cAlgo
// ============================================================================

using System;
using System.Collections.Generic;
using cAlgo.API;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;

namespace cAlgo.Robots
{
    [Robot(AccessRights = AccessRights.None, AddIndicators = true)]
    public class GoldDCAPro : Robot
    {
        // --- GRID SETTINGS ---
        [Parameter("Take Profit (pips, single pos)", DefaultValue = 300, Group = "Grid Settings")]
        public int TakeProfitPips { get; set; }

        [Parameter("Grid Step (pips)", DefaultValue = 390, Group = "Grid Settings")]
        public int GridStepPips { get; set; }

        [Parameter("Min Basket Profit Buffer (pips)", DefaultValue = 120, Group = "Grid Settings")]
        public int MinProfitPips { get; set; }

        // --- TIER SYSTEM ---
        [Parameter("Use Tier System", DefaultValue = true, Group = "Tier System")]
        public bool UseTierSystem { get; set; }

        // Tier 1 — Aggressive (small account, grow fast)
        [Parameter("T1 Upper Balance (£)", DefaultValue = 5000, Group = "Tier 1 - Aggressive")]
        public double T1Upper { get; set; }

        [Parameter("T1 Start Lots", DefaultValue = 0.02, Group = "Tier 1 - Aggressive")]
        public double T1StartLots { get; set; }

        [Parameter("T1 Max Lots", DefaultValue = 0.10, Group = "Tier 1 - Aggressive")]
        public double T1MaxLots { get; set; }

        [Parameter("T1 Max Drawdown %", DefaultValue = 30.0, Group = "Tier 1 - Aggressive")]
        public double T1MaxDD { get; set; }

        [Parameter("T1 Max Positions", DefaultValue = 5, Group = "Tier 1 - Aggressive")]
        public int T1MaxPositions { get; set; }

        // Tier 2 — Balanced
        [Parameter("T2 Upper Balance (£)", DefaultValue = 15000, Group = "Tier 2 - Balanced")]
        public double T2Upper { get; set; }

        [Parameter("T2 Start Lots", DefaultValue = 0.03, Group = "Tier 2 - Balanced")]
        public double T2StartLots { get; set; }

        [Parameter("T2 Max Lots", DefaultValue = 0.15, Group = "Tier 2 - Balanced")]
        public double T2MaxLots { get; set; }

        [Parameter("T2 Max Drawdown %", DefaultValue = 25.0, Group = "Tier 2 - Balanced")]
        public double T2MaxDD { get; set; }

        [Parameter("T2 Max Positions", DefaultValue = 4, Group = "Tier 2 - Balanced")]
        public int T2MaxPositions { get; set; }

        // Tier 3 — Protective
        [Parameter("T3 Upper Balance (£)", DefaultValue = 30000, Group = "Tier 3 - Protective")]
        public double T3Upper { get; set; }

        [Parameter("T3 Start Lots", DefaultValue = 0.05, Group = "Tier 3 - Protective")]
        public double T3StartLots { get; set; }

        [Parameter("T3 Max Lots", DefaultValue = 0.20, Group = "Tier 3 - Protective")]
        public double T3MaxLots { get; set; }

        [Parameter("T3 Max Drawdown %", DefaultValue = 20.0, Group = "Tier 3 - Protective")]
        public double T3MaxDD { get; set; }

        [Parameter("T3 Max Positions", DefaultValue = 3, Group = "Tier 3 - Protective")]
        public int T3MaxPositions { get; set; }

        // Tier 4 — Conservative (income mode)
        [Parameter("T4 Start Lots", DefaultValue = 0.05, Group = "Tier 4 - Conservative")]
        public double T4StartLots { get; set; }

        [Parameter("T4 Max Lots", DefaultValue = 0.20, Group = "Tier 4 - Conservative")]
        public double T4MaxLots { get; set; }

        [Parameter("T4 Max Drawdown %", DefaultValue = 15.0, Group = "Tier 4 - Conservative")]
        public double T4MaxDD { get; set; }

        [Parameter("T4 Max Positions", DefaultValue = 3, Group = "Tier 4 - Conservative")]
        public int T4MaxPositions { get; set; }

        // --- COOLDOWN SYSTEM ---
        [Parameter("Use Cooldown System", DefaultValue = true, Group = "Cooldown System")]
        public bool UseCooldown { get; set; }

        [Parameter("Stop-out Tracking Window (hours)", DefaultValue = 72, Group = "Cooldown System")]
        public int TrackingWindowHours { get; set; }

        [Parameter("1-Stop: Skip Next Session", DefaultValue = true, Group = "Cooldown System")]
        public bool Skip1Stop { get; set; }

        [Parameter("2-Stop: Pause Hours", DefaultValue = 24, Group = "Cooldown System")]
        public int Pause2StopHours { get; set; }

        [Parameter("2-Stop: DD Multiplier (0.5 = halve DD)", DefaultValue = 0.5, Group = "Cooldown System")]
        public double DD2StopMultiplier { get; set; }

        [Parameter("3-Stop: Pause Hours", DefaultValue = 72, Group = "Cooldown System")]
        public int Pause3StopHours { get; set; }

        // --- LEGACY (used only when UseTierSystem = false) ---
        [Parameter("Legacy Start Lots", DefaultValue = 0.01, Group = "Legacy (if tiers off)")]
        public double LegacyStartLots { get; set; }

        [Parameter("Legacy Max Lots", DefaultValue = 0.04, Group = "Legacy (if tiers off)")]
        public double LegacyMaxLots { get; set; }

        [Parameter("Legacy Max Drawdown %", DefaultValue = 50.0, Group = "Legacy (if tiers off)")]
        public double LegacyMaxDD { get; set; }

        [Parameter("Legacy Max Positions", DefaultValue = 5, Group = "Legacy (if tiers off)")]
        public int LegacyMaxPositions { get; set; }

        // --- SESSION FILTER ---
        [Parameter("Asian Start (UTC hour)", DefaultValue = 0, Group = "Session Filter")]
        public int AsianStart { get; set; }

        [Parameter("Asian End (UTC hour)", DefaultValue = 8, Group = "Session Filter")]
        public int AsianEnd { get; set; }

        [Parameter("London Start (UTC hour)", DefaultValue = 8, Group = "Session Filter")]
        public int LondonStart { get; set; }

        [Parameter("London End (UTC hour)", DefaultValue = 14, Group = "Session Filter")]
        public int LondonEnd { get; set; }

        [Parameter("Close Hour (UTC)", DefaultValue = 16, Group = "Session Filter")]
        public int CloseHour { get; set; }

        // --- TREND FILTER ---
        [Parameter("Use Trend Filter", DefaultValue = true, Group = "Trend Filter")]
        public bool UseTrendFilter { get; set; }

        [Parameter("Trend EMA Period", DefaultValue = 200, Group = "Trend Filter")]
        public int TrendEMAPeriod { get; set; }

        [Parameter("Trend Timeframe", DefaultValue = "Hour", Group = "Trend Filter")]
        public TimeFrame TrendTimeframe { get; set; }

        // --- VISUAL DEBUG ---
        [Parameter("Show EMA Line", DefaultValue = true, Group = "Visual Debug")]
        public bool ShowEMALine { get; set; }

        [Parameter("Show Trend Markers", DefaultValue = true, Group = "Visual Debug")]
        public bool ShowTrendMarks { get; set; }

        [Parameter("EMA Color", DefaultValue = "Orange", Group = "Visual Debug")]
        public string EMAColor { get; set; }

        [Parameter("Allowed Marker Color", DefaultValue = "Lime", Group = "Visual Debug")]
        public string AllowedColor { get; set; }

        [Parameter("Blocked Marker Color", DefaultValue = "Red", Group = "Visual Debug")]
        public string BlockedColor { get; set; }

        // --- RISK PROTECTION ---
        [Parameter("Use Daily Target", DefaultValue = false, Group = "Risk Protection")]
        public bool UseDailyTarget { get; set; }

        [Parameter("Daily Target ($)", DefaultValue = 50.0, Group = "Risk Protection")]
        public double DailyTarget { get; set; }

        // --- EXPERT SETTINGS ---
        [Parameter("Label", DefaultValue = "GoldDCAPro", Group = "Expert Settings")]
        public string Label { get; set; }

        [Parameter("Slippage (pips)", DefaultValue = 30, Group = "Expert Settings")]
        public int SlippagePips { get; set; }

        // --- STATE ---
        private DateTime _dayStart;
        private bool _dailyTargetHit;
        private bool _scenarioClosed;
        private ExponentialMovingAverage _trendEma;
        private Bars _trendBars;
        private int _markerCounter;
        private int _currentTier = 0;  // tracks last known tier (0 = unset)

        // --- COOLDOWN STATE ---
        private List<DateTime> _recentStopOuts = new List<DateTime>();
        private DateTime _cooldownUntil = DateTime.MinValue;
        private bool _skipNextSession = false;
        private int _lastSessionHour = -1;
        private bool _wasInSessionLastCheck = false;

        // --- TIER SETTINGS HOLDER ---
        private class TierSettings
        {
            public int TierNumber;
            public string Name;
            public double StartLots;
            public double MaxLots;
            public double MaxDDPct;   // % loss before closing (e.g. 30 = close at 30% drawdown)
            public int MaxPositions;
        }

        // --- LIFECYCLE ---
        protected override void OnStart()
        {
            _dayStart = Server.Time.Date;
            _dailyTargetHit = false;
            _scenarioClosed = false;
            _markerCounter = 0;

            if (UseTrendFilter)
            {
                _trendBars = MarketData.GetBars(TrendTimeframe);
                _trendEma = Indicators.ExponentialMovingAverage(_trendBars.ClosePrices, TrendEMAPeriod);
            }

            var tier = GetCurrentTier();
            _currentTier = tier.TierNumber;
            Print("[GoldDCA Pro v1.5-cT] Started on {0} | Balance: £{1:F2} | Tier: {2} ({3})",
                  Symbol.Name, Account.Balance, tier.TierNumber, tier.Name);
        }

        protected override void OnStop()
        {
            foreach (var obj in Chart.Objects)
            {
                if (obj.Name.StartsWith("GDP_"))
                    Chart.RemoveObject(obj.Name);
            }
        }

        protected override void OnBar()
        {
            DrawEMASegment();
        }

        protected override void OnTick()
        {
            ResetDayIfNeeded();

            // Get current tier settings — re-evaluated every tick
            var tier = GetCurrentTier();

            // Prune old stop-outs outside tracking window
            PruneOldStopOuts();

            // Calculate effective DD (may be tightened by cooldown rules)
            double effectiveDD = GetEffectiveMaxDD(tier);

            // Log tier changes
            if (tier.TierNumber != _currentTier)
            {
                Print("[GoldDCA Pro] ⚡ TIER CHANGE: Tier {0} → Tier {1} ({2}) | Balance: £{3:F2}",
                      _currentTier, tier.TierNumber, tier.Name, Account.Balance);
                _currentTier = tier.TierNumber;
            }

            // --- DRAWDOWN STOP (tier-driven + cooldown tightening) ---
            // Close basket if equity has dropped by more than effectiveDD from balance.
            if (Account.Balance > 0 && Account.Equity < Account.Balance * (1.0 - effectiveDD / 100.0))
            {
                double lossPct = (Account.Balance - Account.Equity) / Account.Balance * 100.0;
                CloseAllPositions(string.Format("Max drawdown hit ({0:F1}% loss, limit: {1}%)",
                                  lossPct, effectiveDD));
                RegisterStopOut();
                _dailyTargetHit = true;
                UpdateDashboard(tier);
                return;
            }

            // --- DAILY CLOSE TIME ---
            if (IsDailyCloseTime())
            {
                if (CountBuyPositions() > 0)
                {
                    CloseAllPositions("Daily close time reached");
                    _scenarioClosed = true;
                }
                _dailyTargetHit = true;
                UpdateDashboard(tier);
                return;
            }

            if (_dailyTargetHit)
            {
                UpdateDashboard(tier);
                return;
            }

            double floatingProfit = GetFloatingProfit();

            if (UseDailyTarget && floatingProfit >= DailyTarget)
            {
                CloseAllPositions("Daily profit target reached");
                _dailyTargetHit = true;
                UpdateDashboard(tier);
                return;
            }

            // --- SCAN BUY POSITIONS ---
            double buyPriceMax = 0, buyPriceMin = 0;
            double buyVolMax = 0, buyVolMin = 0;
            int? tkMax = null, tkMin = null;
            int b = 0;

            foreach (var pos in Positions)
            {
                if (pos.Label != Label) continue;
                if (pos.SymbolName != Symbol.Name) continue;
                if (pos.TradeType != TradeType.Buy) continue;

                double op = pos.EntryPrice;
                double vol = pos.VolumeInUnits;
                b++;

                if (op > buyPriceMax || buyPriceMax == 0)
                {
                    buyPriceMax = op; buyVolMax = vol; tkMax = pos.Id;
                }
                if (op < buyPriceMin || buyPriceMin == 0)
                {
                    buyPriceMin = op; buyVolMin = vol; tkMin = pos.Id;
                }
            }

            if (b == 0)
                _scenarioClosed = false;

            double averageBuyPrice = 0;
            if (b >= 2)
            {
                double weighted = (buyPriceMax * buyVolMax + buyPriceMin * buyVolMin) /
                                  (buyVolMax + buyVolMin);
                averageBuyPrice = weighted + MinProfitPips * Symbol.PipSize;
            }

            // Next lot — tier-driven
            double nextLots = (buyVolMin == 0) ? tier.StartLots : VolumeToLots(buyVolMin) * 2;
            if (tier.MaxLots > 0 && nextLots > tier.MaxLots) nextLots = tier.MaxLots;
            double nextVolume = Symbol.QuantityToVolumeInUnits(nextLots);
            nextVolume = Symbol.NormalizeVolumeInUnits(nextVolume, RoundingMode.ToNearest);

            if (nextVolume < Symbol.VolumeInUnitsMin) return;
            if (nextVolume > Symbol.VolumeInUnitsMax) return;

            bool inSession = IsInSession();
            bool trendUp = IsTrendUp();

            // Detect winning basket completion — reset cooldown counters
            // (if basket closed naturally with profit, positions go from b>0 to b=0)
            if (_wasInSessionLastCheck && b == 0 && Account.Equity > Account.Balance)
            {
                ResetCooldownOnWin();
            }
            _wasInSessionLastCheck = (b > 0);

            // Session boundary — handle "skip next session" flag
            int currentHour = Server.Time.Hour;
            if (currentHour != _lastSessionHour)
            {
                // New hour — check if entering a new session
                bool wasInSession = (_lastSessionHour >= 0) &&
                    ((_lastSessionHour >= AsianStart && _lastSessionHour < AsianEnd) ||
                     (_lastSessionHour >= LondonStart && _lastSessionHour < LondonEnd));

                if (inSession && !wasInSession && _skipNextSession)
                {
                    Print("[GoldDCA Pro] 🚫 Skipping this session (1-stop rule)");
                    _skipNextSession = false;  // consume the skip
                    _scenarioClosed = true;    // block entries this session
                }

                _lastSessionHour = currentHour;
            }

            // Cooldown pause check
            bool inCooldownPause = Server.Time < _cooldownUntil;

            // --- OPEN NEW BUY LEVELS ---
            if (inSession && !_scenarioClosed && !inCooldownPause && b < tier.MaxPositions)
            {
                if (b == 0 && Bars.ClosePrices.Last(1) > Bars.OpenPrices.Last(1))
                {
                    if (trendUp)
                    {
                        DrawTrendMarker(true, Symbol.Ask);
                        var result = ExecuteMarketOrder(TradeType.Buy, Symbol.Name, nextVolume, Label, null, null);
                        if (!result.IsSuccessful)
                            Print("[GoldDCA Pro] Buy error: {0}", result.Error);
                    }
                    else
                    {
                        DrawTrendMarker(false, Symbol.Ask);
                    }
                }

                if (b > 0 && b < tier.MaxPositions)
                {
                    if ((buyPriceMin - Symbol.Ask) > (GridStepPips * Symbol.PipSize))
                    {
                        var result = ExecuteMarketOrder(TradeType.Buy, Symbol.Name, nextVolume, Label, null, null);
                        if (!result.IsSuccessful)
                            Print("[GoldDCA Pro] DCA Buy error: {0}", result.Error);
                    }
                }
            }

            // --- MANAGE TAKE PROFITS ---
            foreach (var pos in Positions)
            {
                if (pos.Label != Label) continue;
                if (pos.SymbolName != Symbol.Name) continue;
                if (pos.TradeType != TradeType.Buy) continue;

                if (b == 1 && pos.TakeProfit == null)
                {
                    double tpPrice = Symbol.Ask + TakeProfitPips * Symbol.PipSize;
                    ModifyPosition(pos, pos.StopLoss, tpPrice);
                }

                if (b >= 2)
                {
                    if (pos.Id == tkMax || pos.Id == tkMin)
                    {
                        if (Symbol.Bid < averageBuyPrice &&
                            (pos.TakeProfit == null || Math.Abs(pos.TakeProfit.Value - averageBuyPrice) > Symbol.PipSize * 0.1))
                        {
                            ModifyPosition(pos, pos.StopLoss, averageBuyPrice);
                        }
                    }
                    else
                    {
                        if (pos.TakeProfit != null)
                            ModifyPosition(pos, pos.StopLoss, null);
                    }
                }
            }

            UpdateDashboard(tier);
        }

        // --- COOLDOWN LOGIC ---

        private void PruneOldStopOuts()
        {
            if (!UseCooldown) return;
            var cutoff = Server.Time.AddHours(-TrackingWindowHours);
            _recentStopOuts.RemoveAll(t => t < cutoff);
        }

        private void RegisterStopOut()
        {
            if (!UseCooldown) return;

            _recentStopOuts.Add(Server.Time);
            int stopCount = _recentStopOuts.Count;

            Print("[GoldDCA Pro] ⚠ Stop-out registered. Count in last {0}h: {1}",
                  TrackingWindowHours, stopCount);

            if (stopCount >= 3)
            {
                _cooldownUntil = Server.Time.AddHours(Pause3StopHours);
                _skipNextSession = false;
                Print("[GoldDCA Pro] 🛑 3-STOP COOLDOWN: Paused until {0}", _cooldownUntil);
            }
            else if (stopCount >= 2)
            {
                _cooldownUntil = Server.Time.AddHours(Pause2StopHours);
                _skipNextSession = false;
                Print("[GoldDCA Pro] ⏸ 2-STOP COOLDOWN: Paused until {0} | Next DD will be tightened",
                      _cooldownUntil);
            }
            else if (stopCount >= 1 && Skip1Stop)
            {
                _skipNextSession = true;
                Print("[GoldDCA Pro] ⏭ 1-STOP: Skipping next session");
            }
        }

        private void ResetCooldownOnWin()
        {
            if (!UseCooldown) return;
            if (_recentStopOuts.Count == 0 && !_skipNextSession && _cooldownUntil <= Server.Time)
                return; // nothing to reset

            Print("[GoldDCA Pro] ✅ Winning basket — cooldown counters reset");
            _recentStopOuts.Clear();
            _skipNextSession = false;
            _cooldownUntil = DateTime.MinValue;
        }

        private double GetEffectiveMaxDD(TierSettings tier)
        {
            if (!UseCooldown) return tier.MaxDDPct;

            // If 2+ recent stop-outs, tighten DD
            if (_recentStopOuts.Count >= 2)
            {
                return tier.MaxDDPct * DD2StopMultiplier;
            }
            return tier.MaxDDPct;
        }

        // --- TIER LOGIC ---

        private TierSettings GetCurrentTier()
        {
            // If tiers disabled, return legacy settings as "Tier 0"
            if (!UseTierSystem)
            {
                return new TierSettings
                {
                    TierNumber = 0,
                    Name = "Legacy",
                    StartLots = LegacyStartLots,
                    MaxLots = LegacyMaxLots,
                    MaxDDPct = LegacyMaxDD,
                    MaxPositions = LegacyMaxPositions
                };
            }

            double balance = Account.Balance;

            if (balance < T1Upper)
            {
                return new TierSettings
                {
                    TierNumber = 1,
                    Name = "Aggressive",
                    StartLots = T1StartLots,
                    MaxLots = T1MaxLots,
                    MaxDDPct = T1MaxDD,
                    MaxPositions = T1MaxPositions
                };
            }
            else if (balance < T2Upper)
            {
                return new TierSettings
                {
                    TierNumber = 2,
                    Name = "Balanced",
                    StartLots = T2StartLots,
                    MaxLots = T2MaxLots,
                    MaxDDPct = T2MaxDD,
                    MaxPositions = T2MaxPositions
                };
            }
            else if (balance < T3Upper)
            {
                return new TierSettings
                {
                    TierNumber = 3,
                    Name = "Protective",
                    StartLots = T3StartLots,
                    MaxLots = T3MaxLots,
                    MaxDDPct = T3MaxDD,
                    MaxPositions = T3MaxPositions
                };
            }
            else
            {
                return new TierSettings
                {
                    TierNumber = 4,
                    Name = "Conservative",
                    StartLots = T4StartLots,
                    MaxLots = T4MaxLots,
                    MaxDDPct = T4MaxDD,
                    MaxPositions = T4MaxPositions
                };
            }
        }

        // --- HELPERS ---

        private bool IsTrendUp()
        {
            if (!UseTrendFilter) return true;
            if (_trendEma == null || _trendBars == null) return true;
            if (_trendBars.ClosePrices.Count < 2) return true;

            double emaVal = _trendEma.Result.Last(1);
            double closeVal = _trendBars.ClosePrices.Last(1);

            if (double.IsNaN(emaVal) || double.IsNaN(closeVal)) return true;

            return closeVal > emaVal;
        }

        private bool IsInSession()
        {
            int hour = Server.Time.Hour;
            if (hour >= AsianStart && hour < AsianEnd) return true;
            if (hour >= LondonStart && hour < LondonEnd) return true;
            return false;
        }

        private bool IsDailyCloseTime()
        {
            return Server.Time.Hour >= CloseHour;
        }

        private void ResetDayIfNeeded()
        {
            var today = Server.Time.Date;
            if (today > _dayStart)
            {
                _dayStart = today;
                _dailyTargetHit = false;
                _scenarioClosed = false;
                Print("[GoldDCA Pro] New day — daily variables reset");
            }
        }

        private double GetFloatingProfit()
        {
            double total = 0;
            foreach (var pos in Positions)
            {
                if (pos.Label != Label) continue;
                if (pos.SymbolName != Symbol.Name) continue;
                total += pos.NetProfit;
            }
            return total;
        }

        private int CountBuyPositions()
        {
            int count = 0;
            foreach (var pos in Positions)
            {
                if (pos.Label != Label) continue;
                if (pos.SymbolName != Symbol.Name) continue;
                if (pos.TradeType != TradeType.Buy) continue;
                count++;
            }
            return count;
        }

        private void CloseAllPositions(string reason)
        {
            var toClose = new List<Position>();
            foreach (var pos in Positions)
            {
                if (pos.Label != Label) continue;
                if (pos.SymbolName != Symbol.Name) continue;
                toClose.Add(pos);
            }
            foreach (var pos in toClose)
                ClosePosition(pos);

            Print("[GoldDCA Pro] All positions closed — reason: {0}", reason);
        }

        private double VolumeToLots(double volumeInUnits)
        {
            return Symbol.VolumeInUnitsToQuantity(volumeInUnits);
        }

        // --- VISUALS ---

        private void DrawEMASegment()
        {
            if (!ShowEMALine || _trendEma == null || _trendBars == null) return;
            if (Bars.Count < 2) return;

            try
            {
                double emaNow = _trendEma.Result.Last(0);
                double emaPrev = _trendEma.Result.Last(1);
                if (double.IsNaN(emaNow) || double.IsNaN(emaPrev)) return;

                DateTime tNow = Bars.OpenTimes.Last(0);
                DateTime tPrev = Bars.OpenTimes.Last(1);

                string name = "GDP_EMA_" + tPrev.Ticks.ToString();

                Chart.DrawTrendLine(
                    name,
                    tPrev, emaPrev,
                    tNow, emaNow,
                    ParseColor(EMAColor, Color.Orange),
                    2,
                    LineStyle.Solid
                );
            }
            catch (Exception ex)
            {
                Print("[GoldDCA Pro] DrawEMASegment error: {0}", ex.Message);
            }
        }

        private void DrawTrendMarker(bool allowed, double price)
        {
            if (!ShowTrendMarks) return;

            try
            {
                _markerCounter++;
                string name = "GDP_MARK_" + _markerCounter.ToString();

                DateTime t = Server.Time;
                string text = allowed ? "▲" : "✕";
                Color color = allowed
                    ? ParseColor(AllowedColor, Color.Lime)
                    : ParseColor(BlockedColor, Color.Red);

                Chart.DrawText(name, text, t, price, color);
            }
            catch (Exception ex)
            {
                Print("[GoldDCA Pro] DrawTrendMarker error: {0}", ex.Message);
            }
        }

        private Color ParseColor(string name, Color fallback)
        {
            try { return Color.FromName(name); }
            catch { return fallback; }
        }

        // --- DASHBOARD ---

        private void UpdateDashboard(TierSettings tier)
        {
            string session = IsInSession() ? "IN SESSION" : "OUT OF SESSION";
            string trend;
            if (!UseTrendFilter) trend = "DISABLED";
            else if (IsTrendUp()) trend = "UP";
            else trend = "DOWN";

            int b = CountBuyPositions();
            double effectiveDD = GetEffectiveMaxDD(tier);

            // Cooldown status
            string cooldownStatus = "NORMAL";
            if (UseCooldown)
            {
                if (Server.Time < _cooldownUntil)
                {
                    double hoursLeft = (_cooldownUntil - Server.Time).TotalHours;
                    cooldownStatus = string.Format("PAUSED ({0:F1}h left)", hoursLeft);
                }
                else if (_skipNextSession)
                {
                    cooldownStatus = "SKIP NEXT SESSION";
                }
                else if (_recentStopOuts.Count > 0)
                {
                    cooldownStatus = string.Format("WARN ({0} recent stops)", _recentStopOuts.Count);
                }
            }

            string dash = "════ GoldDCA Pro v1.5-cT ════\n";
            dash += string.Format("Tier:        {0} ({1})\n", tier.TierNumber, tier.Name);
            dash += string.Format("  Max DD:    {0}%", tier.MaxDDPct);
            if (Math.Abs(effectiveDD - tier.MaxDDPct) > 0.01)
                dash += string.Format(" → {0:F1}% (tightened)", effectiveDD);
            dash += "\n";
            dash += string.Format("  Start Lot: {0}\n", tier.StartLots);
            dash += string.Format("  Max Lot:   {0}\n", tier.MaxLots);
            dash += string.Format("  Max Pos:   {0}\n", tier.MaxPositions);
            dash += "─────────────────────────\n";
            dash += string.Format("Cooldown:    {0}\n", cooldownStatus);
            dash += string.Format("Session:     {0}\n", session);
            dash += string.Format("Trend:       {0}\n", trend);
            dash += string.Format("Positions:   {0}/{1}\n", b, tier.MaxPositions);
            dash += string.Format("Floating:    ${0:F2}\n", GetFloatingProfit());
            dash += string.Format("Equity:      ${0:F2}\n", Account.Equity);
            dash += string.Format("Balance:     ${0:F2}\n", Account.Balance);
            dash += string.Format("Close at:    {0}:00 UTC\n", CloseHour);

            Chart.DrawStaticText("GDP_Dashboard", dash, VerticalAlignment.Top, HorizontalAlignment.Left, Color.White);
        }
    }
}