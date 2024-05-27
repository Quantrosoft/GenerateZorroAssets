using System;
using System.Collections.Generic;
using cAlgo.API;
using cAlgo.API.Internals;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Globalization;

namespace cAlgo.Robots
{
   [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.FullAccess)]
   public class cBot : Robot
   {
      #region History
      readonly string Version = "GenAssets V1.0";
      // V1.0    25.05.24    HMz created from
      #endregion

      #region Parameters
      [Parameter("Launch Debugger", DefaultValue = false)]
      public bool IsLaunchDebugger { get; set; }

      [Parameter()]
      public string ZorroHistoryPath { get; set; }

      [Parameter()]
      public string ExcludeCsv { get; set; }
      #endregion

      #region Enums, consts, structs, classes
      class SymInfo
      {
         public Symbol Symbol;
         public double SpreadSum;
         public double AvgSpread;
         public int SpreadTickCount;
      }
      #endregion

      #region Members
      private List<SymInfo> mSymInfos = new List<SymInfo>();
      private List<List<List<string>>> mInfoLists = new List<List<List<string>>>();
      private string[] mExcludeSplit;
      private int mSymbolCount;
      readonly CultureInfo UsCulture = new CultureInfo("en-US");
      #endregion

      #region OnStart
      protected override void OnStart()
      {
         if (IsLaunchDebugger) Debugger.Launch();

         Print("Number of Symbols: " + Symbols.Count);
         Print("Please wait a few minutes to load the symbols...");
      }
      #endregion

      #region OnTick
      protected override void OnTick()
      {
         if (mSymbolCount < Symbols.Count)
         {
            mExcludeSplit = ExcludeCsv.Split(",");
            Symbol sym = null;

            var isExcluded = false;
            foreach (var ex in mExcludeSplit)
               if ("" != ex && Symbols[mSymbolCount].ToLower().Contains(ex.ToLower()))
               {
                  Print((mSymbolCount + 1).ToString() + " " + Symbols[mSymbolCount] + " excluded");
                  isExcluded = true;
                  break;
               }

            if (!isExcluded)
            {
               Print((mSymbolCount + 1).ToString() + " " + Symbols[mSymbolCount]);
               sym = Symbols.GetSymbol(Symbols[mSymbolCount]);

               if (sym.IsTradingEnabled && !Double.IsNaN(sym.Spread))  // some sybols have a Spread of NaN ?!)
                  mSymInfos.Add(new SymInfo
                  {
                     Symbol = sym
                  });
               else
                  Print((mSymbolCount + 1).ToString() + " " + Symbols[mSymbolCount] + " invalid");
            }

            mSymbolCount++;
         }
         else
         {
            if (Time.Hour >= 6)
               if (Time.Hour <= 14)
                  for (int i = 0; i < mSymInfos.Count; i++)
                  {
                     mSymInfos[i].SpreadSum += mSymInfos[i].Symbol.Spread;
                     mSymInfos[i].SpreadTickCount++;

                     mSymInfos[i].AvgSpread = mSymInfos[i].SpreadSum / mSymInfos[i].SpreadTickCount;
                  }

            if (mSymInfos[mSymInfos.Count - 1].SpreadTickCount == 100)
            {
               // write Zorro Assets file i.e Assets_Pepperstone_Live.csv
               var zorroAssetsPath = Path.Combine(ZorroHistoryPath, "Assets_"
                  + Account.BrokerName + "_" + (Account.IsLive ? "Live" : "Demo") + ".csv");
               var zorroAssetsWriter = new StreamWriter(File.OpenWrite(zorroAssetsPath));

               zorroAssetsWriter.WriteLine("Name,Price,Spread,RollLong,RollShort,PIP,PIPCost,MarginCost,Market,Multiplier,Commission,Symbol,Leverage,Lotsize,Base,Quote");

               for (int i = 0; i < mSymInfos.Count; i++)
               {
                  var digits = mSymInfos[i].Symbol.Digits;
                  var line = mSymInfos[i].Symbol.Name                                        // User symbol name
                     + "," + mSymInfos[i].Symbol.Bid.ToString($"F{digits}", UsCulture)       // Price
                     + "," + mSymInfos[i].AvgSpread.ToString($"F{digits + 1}", UsCulture)    // Spread
                     + "," + mSymInfos[i].Symbol.SwapLong.ToString($"F{5}", UsCulture)       // Swap long
                     + "," + mSymInfos[i].Symbol.SwapShort.ToString($"F{5}", UsCulture)      // Swap short
                     + "," + mSymInfos[i].Symbol.PipSize.ToString($"F{digits}", UsCulture)   // Pip size
                     + "," + (mSymInfos[i].Symbol.PipValue * mSymInfos[i].Symbol.LotSize)    // Value of 1 pip profit or loss per lot
                        .ToString($"F{8}", UsCulture)
                     + "," + mSymInfos[i].Symbol.GetEstimatedMargin(TradeType.Buy, mSymInfos[i].Symbol.LotSize)   // Margin
                        .ToString($"F{2}", UsCulture)
                     // Market open hours ZZZ:HHMM-HHMM, for instance EST:0930-1545
                     + ",UTC:" + mSymInfos[i].Symbol.MarketHours.Sessions[1].StartTime.ToString(@"hh\:mm")
                        + "-" + mSymInfos[i].Symbol.MarketHours.Sessions[1].EndTime.ToString(@"hh\:mm")
                     + "," + mSymInfos[i].Symbol.VolumeInUnitsMin.ToString($"F{8}", UsCulture)  // Min volume
                     + "," + mSymInfos[i].Symbol.Commission.ToString($"F{2}", UsCulture)     // Commission
                     + "," + mSymInfos[i].Symbol                                             // Broker symbol Name
                     + "," + (int)mSymInfos[i].Symbol.DynamicLeverage[0].Leverage            // Leverage
                     + "," + mSymInfos[i].Symbol.LotSize.ToString($"F{2}", UsCulture)        // LotSize
                     + "," + mSymInfos[i].Symbol.BaseAsset                                   // Base currency
                     + "," + mSymInfos[i].Symbol.QuoteAsset;                                 // Quote currency

                  zorroAssetsWriter.WriteLine(line);
               }

               zorroAssetsWriter.Close();
               Stop();
            }
         }
         #region Comment
         if (RunningMode.VisualBacktesting == RunningMode
            || RunningMode.RealTime == RunningMode)
         {
            var myComm = "Current UTC: " + Time.ToString("dd.MM.yyyy HH:mm:ss")
               + "\nCount waiting for " + Symbols.Count + " symbols: " + mSymbolCount + " " + Symbols[mSymbolCount - 1]
               + "\nCount waiting for 100 ticks: " + mSymInfos[mSymInfos.Count - 1].SpreadTickCount;
            Chart.DrawStaticText("Comment",
               myComm,
               VerticalAlignment.Top,
               HorizontalAlignment.Left,
               Chart.ColorSettings.ForegroundColor);
         }
         #endregion
      }
      #endregion

      #region OnStop
      protected override void OnStop()
      {
      }
      #endregion
   }
}
// end of file
