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
      private int mWriteCount;
      private int mSymbolsCount;
      private StreamWriter mZorroAssetsWriter;
      readonly CultureInfo UsCulture = new CultureInfo("en-US");
      #endregion

      #region OnStart
      protected override void OnStart()
      {
         if (IsLaunchDebugger) Debugger.Launch();
         mSymbolsCount = Symbols.Count;
         //mSymbolsCount = 10;

         Print("Number of Symbols: " + mSymbolsCount);
         Print("Please wait a few minutes to load the symbols...");
      }
      #endregion

      #region OnTick
      protected override void OnTick()
      {
         //if (Time.Hour >= 9)
         //   if (Time.Hour <= 16)
         if (mSymbolCount < mSymbolsCount)
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

               if (null != sym
                     && sym.IsTradingEnabled
                     && !Double.IsNaN(sym.Spread))  // some sybols have a Spread of NaN ?!)
                  mSymInfos.Add(new SymInfo
                  {
                     Symbol = sym
                  });
               else
               {
                  Print((mSymbolCount + 1).ToString() + " " + Symbols[mSymbolCount] + " invalid");
                  mSymbolsCount--;
               }
            }

            mSymbolCount++;
         }
         else if (mSymInfos[^1].SpreadTickCount < 100)
         {
            for (int i = 0; i < mSymInfos.Count; i++)
            {
               mSymInfos[i].SpreadSum += mSymInfos[i].Symbol.Spread;
               mSymInfos[i].SpreadTickCount++;

               mSymInfos[i].AvgSpread = mSymInfos[i].SpreadSum / mSymInfos[i].SpreadTickCount;
            }
         }
         else if (mWriteCount < mSymInfos.Count)
         {
            // write Zorro Assets file i.e Assets_Pepperstone_Live.csv
            if (null == mZorroAssetsWriter)
            {
               var zorroAssetsPath = Path.Combine(ZorroHistoryPath, "Assets_"
                  + Account.BrokerName + "_" + (Account.IsLive ? "Live" : "Demo") + ".csv");
               mZorroAssetsWriter = new StreamWriter(File.OpenWrite(zorroAssetsPath));
               mZorroAssetsWriter.WriteLine("Name,Price,Spread,RollLong,RollShort,PIP,PIPCost,MarginCost,Market,Multiplier,Commission,Symbol,Leverage,Lotsize,Base,Quote");
            }

            Print((mWriteCount + 1).ToString() + " Writing " + mSymInfos[mWriteCount].Symbol.Name);

            var digits = mSymInfos[mWriteCount].Symbol.Digits;
            var line = mSymInfos[mWriteCount].Symbol.Name                                        // User symbol name
               + "," + mSymInfos[mWriteCount].Symbol.Bid.ToString($"F{digits}", UsCulture)       // Price
               + "," + mSymInfos[mWriteCount].AvgSpread.ToString($"F{digits + 1}", UsCulture)    // Spread
               + "," + mSymInfos[mWriteCount].Symbol.SwapLong.ToString($"F{5}", UsCulture)       // Swap long
               + "," + mSymInfos[mWriteCount].Symbol.SwapShort.ToString($"F{5}", UsCulture)      // Swap short
               + "," + mSymInfos[mWriteCount].Symbol.PipSize.ToString($"F{digits}", UsCulture)   // Pip size
               + "," + (mSymInfos[mWriteCount].Symbol.PipValue * mSymInfos[mWriteCount].Symbol.LotSize)    // Value of 1 pip profit or loss per lot
                  .ToString($"F{8}", UsCulture)
               + "," + mSymInfos[mWriteCount].Symbol.GetEstimatedMargin(TradeType.Buy, mSymInfos[mWriteCount].Symbol.LotSize)   // Margin
                  .ToString($"F{2}", UsCulture)
               // Market open hours ZZZ:HHMM-HHMM, for instance EST:0930-1545
               + ",UTC:" + mSymInfos[mWriteCount].Symbol.MarketHours.Sessions[1].StartTime.ToString(@"hh\:mm")
                  + "-" + mSymInfos[mWriteCount].Symbol.MarketHours.Sessions[1].EndTime.ToString(@"hh\:mm")
               + "," + mSymInfos[mWriteCount].Symbol.VolumeInUnitsMin.ToString($"F{8}", UsCulture)  // Min volume
               + "," + mSymInfos[mWriteCount].Symbol.Commission.ToString($"F{2}", UsCulture)     // Commission
               + "," + mSymInfos[mWriteCount].Symbol                                             // Broker symbol Name
               + "," + ((mSymInfos[mWriteCount].Symbol.DynamicLeverage != null
                        && mSymInfos[mWriteCount].Symbol.DynamicLeverage.Count > 0
                           ? (int)mSymInfos[mWriteCount].Symbol.DynamicLeverage[0].Leverage       // Leverage
                           : ""))
               + "," + mSymInfos[mWriteCount].Symbol.LotSize.ToString($"F{2}", UsCulture)        // LotSize
               + "," + mSymInfos[mWriteCount].Symbol.BaseAsset                                   // Base currency
               + "," + mSymInfos[mWriteCount].Symbol.QuoteAsset;                                 // Quote currency

            mZorroAssetsWriter.WriteLine(line);
            mWriteCount++;
         }
         else
         {
            mZorroAssetsWriter.Close();
            Stop();
         }

         #region Comment
         if (RunningMode.VisualBacktesting == RunningMode
            || RunningMode.RealTime == RunningMode)
            if (mSymInfos.Count >= 1)
            {
               var myComm = "Current UTC: " + Time.ToString("dd.MM.yyyy HH:mm:ss")
                  + "\nCount waiting for " + mSymbolsCount + " symbols: " + mSymbolCount + " " + Symbols[mSymbolCount - 1]
                  + "\nCount waiting for 100 ticks: " + mSymInfos[^1].SpreadTickCount;
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
