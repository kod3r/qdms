﻿// -----------------------------------------------------------------------
// <copyright file="MyExtensions.cs" company="">
// Copyright 2013 Alexander Soffronow Pagonidis
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using NodaTime;

namespace QDMS
{
    public static class MyExtensions
    {
        /// <summary>
        /// Finds the index of the first occurence of an item matching the provided predicate
        /// </summary>
        /// <returns>The index if found, -1 if not found.</returns>
        public static int IndexOf<T>(this IEnumerable<T> data, Predicate<T> predicate)
        {
            if (data == null) throw new ArgumentNullException("data");
            if (predicate == null) throw new ArgumentNullException("predicate");

            int count = 0;
            foreach (var i in data)
            {
                if (predicate(i)) return count;
                count++;
            }
            return -1;
        }


        /// <summary>
        /// Save a collection of OHLCBars to a file, in CSV format.
        /// </summary>
        public static void ToCSVFile(this IEnumerable<OHLCBar> data, string filePath)
        {
            using (System.IO.StreamWriter file = new System.IO.StreamWriter(filePath))
            {
                //write header first
                var headerFields = new List<string>
                {
                    "Date",
                    "Time",
                    "Open",
                    "High",
                    "Low",
                    "Close",
                    "Volume",
                    "Open Interest",
                    "Dividend",
                    "Split",
                    "AdjOpen",
                    "AdjHigh",
                    "AdjLow",
                    "AdjClose"
                };
                string header = string.Join(",", headerFields);
                file.WriteLine(header);

                foreach (OHLCBar bar in data)
                {
                    file.WriteLine("{0:yyyy-MM-dd},{1:HH:mm:ss.fff},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13}",
                        bar.Date,
                        bar.Date,
                        bar.Open,
                        bar.High,
                        bar.Low,
                        bar.Close,
                        bar.Volume,
                        bar.OpenInterest,
                        bar.Dividend,
                        bar.Split,
                        bar.AdjOpen,
                        bar.AdjHigh,
                        bar.AdjLow,
                        bar.AdjClose);
                }
            }
        }

        public static DateTime ToDateTime(this LocalDate dt)
        {
            return new DateTime(dt.Year, dt.Month, dt.Day);
        }

        public static DateTime ToDateTime(this LocalDateTime dt)
        {
            return new DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, dt.Second, dt.Millisecond);
        }

        /// <summary>
        /// Gets the "ending" session's closing time, one for each day of the week.
        /// </summary>
        public static Dictionary<int, TimeSpan> SessionEndTimesByDay(this Instrument instrument)
        {
            Dictionary<int, TimeSpan> sessionEndTimes = new Dictionary<int, TimeSpan>();
            if (instrument.Sessions == null) return sessionEndTimes;

            var dotwValues = MyUtils.GetEnumValues<DayOfTheWeek>();

            foreach (DayOfTheWeek d in dotwValues)
            {
                if (instrument.Sessions.Any(x => x.ClosingDay == d && x.IsSessionEnd))
                {
                    var endTime = instrument.Sessions.First(x => x.ClosingDay == d && x.IsSessionEnd).ClosingTime;
                    sessionEndTimes.Add((int)d, endTime);
                }
                else
                {
                    sessionEndTimes.Add((int)d, TimeSpan.FromSeconds(0));
                }
            }
            return sessionEndTimes;
        }
    }
}
