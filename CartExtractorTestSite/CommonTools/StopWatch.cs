using System;
using System.Collections.Generic;

namespace _4_Tell.CommonTools
{
	public class StopWatch
	{
		private bool _showMilliseconds;
		private long _startTime = 0;
		private long _endTime = 0;
		private readonly List<long> _laps = new List<long>();

		public bool Started { get; private set; }
		public const int MaxLapCounters = 10;

		public StopWatch(bool start = false, bool showMilliseconds = true)
		{
			_showMilliseconds = showMilliseconds;
			Reset();
			if (start) Start();
		}

		public void Reset()
		{
			_startTime = 0;
			_endTime = 0;
			Started = false;
		}

		public void Start()
		{
			_startTime = DateTime.Now.Ticks;
			Started = true;
		}

		public string Lap(int lapnum = 1)
		{
			if (!Started) return "not started";
			if (lapnum > MaxLapCounters) return "exceded max lap counters";

			_endTime = DateTime.Now.Ticks;
			if (lapnum < 1) lapnum = 1;
			while (_laps.Count < lapnum) _laps.Add(_startTime);
			var thislap = _laps[lapnum - 1];
			var lapTime = _endTime - thislap;
			_laps[lapnum - 1] = _endTime;
			return Format(lapTime);
		}

		public string Stop()
		{
			if (!Started) return "not started";

			_endTime = DateTime.Now.Ticks;
			Started = false;
			return Format(_endTime - _startTime);
		}

		public string CurrentTotal
		{
			get
			{
				if (Started) return Format(DateTime.Now.Ticks - _startTime);
				return TotalTime;
			}
		}

		//gets frmatted total time for period up to last Lap or Stop command
		//must call Lap or start first
		public string TotalTime
		{
			get { return Format(_endTime - _startTime); }
		}

		public double ElapsedMilliseconds
		{
			get { return ((_endTime - _startTime)/TimeSpan.TicksPerMillisecond); }
		}

		public double ElapsedSeconds
		{
			get { return (ElapsedMilliseconds/1000); }
		}

		public double ElapsedMinutes
		{
			get { return (ElapsedSeconds/60); }
		}

		public double ElapsedHours
		{
			get { return (ElapsedMinutes/60); }
		}

		private string Format(long ticks)
		{
			var ms = (int) (ticks/TimeSpan.TicksPerMillisecond);
			var s = ms/1000;
			var m = s/60;
			s %= 60;
			ms %= 1000;
			return _showMilliseconds ?
				string.Format("{0:0}:{1:00}.{2:000}", m, s, ms)
				: string.Format("{0:0}:{1:00}", m, s);
		}
	}
}