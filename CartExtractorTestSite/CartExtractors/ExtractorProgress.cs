using System;
using System.Collections.Generic;
using System.Linq;
using _4_Tell.CommonTools;

namespace _4_Tell.CartExtractors
{
	/// <summary>
	/// Class to track and display extractor progress. 
	/// To use, first call Start to begin the process and give it a title. 
	/// Then use StartTable, UpdateTable, and EndTable to track export status for a table. 
	/// If there are subtasks in a table export process, use StartTask, UpdateTask, and EndTask 
	/// to track each task.
	/// ExtraStatus can be used to add a comment below all other status messages.
	/// Elapsed time will be automatically tracked.
	/// Request Text to display current status.
	/// </summary>
	public class ExtractorProgress
	{
		private struct TableDef
		{
			public string Name;
			public string Status;
			public string ItemType;
			public int TotalCount;
			public int CurrentCount;
			public string TimeElapsed;
			public bool SubTask;

			public void Clear(bool subtask = false)
			{
				Name = "";
				Status = "";
				ItemType = "";
				TotalCount = -1;
				CurrentCount = -1;
				TimeElapsed = "";
				SubTask = subtask;
			}

			public override string  ToString()
			{
				var count = TotalCount < 0 ? "" : string.Format(" ({0:N0} {1})", TotalCount, ItemType);
				return string.Format("{0}{1}: {2}{3}{4}", SubTask ? "\t" : "", Name, Status, count, Environment.NewLine);
			}
		}

		private string _mainTitle;
		private string _startTime;
		private List<TableDef> _completedTables;
		private List<TableDef> _subTasks;
		private TableDef _currentTable;
		private TableDef _currentTask;
		private readonly StopWatch _overallTimer;
		private readonly StopWatch _tableTimer;
		private readonly StopWatch _taskTimer;
		private bool _abort;
		private ExtractorProgress _migrationProgress = null;

		public enum ProgressState
		{
			InProgress,
			Success,
			Failure,
			Canceled,
			Unknown
		}
		public ProgressState State { get; private set; }
		public bool Started { get { return _overallTimer.Started; } }
		public bool IsMigrating { get; set; }
		public bool IsSlave { get; set; }
		public string ExtraStatus { get; private set; }
		public string Text
		{
			get //current status display is calculated each time it is requested
			{
				if (string.IsNullOrEmpty(_mainTitle)) return "";

				//main title and start time
				var text = _mainTitle + Environment.NewLine;
				if (!string.IsNullOrEmpty(_startTime))
					text += _startTime + Environment.NewLine;
				text += Environment.NewLine;

				//completed tables
				if (_completedTables.Count > 0) 
					text += _completedTables.Aggregate("", (current, t) => current + t.ToString());

				//current table
				if (!string.IsNullOrEmpty(_currentTable.Name))
				{
					text += string.Format("{0}: {1}...", _currentTable.Name, _currentTable.Status);
					if (_currentTable.TotalCount >= 0 && _currentTable.CurrentCount >= 0)
						text += string.Format("({0:N0} completed of {1:N0} {2})",
						                      _currentTable.CurrentCount, _currentTable.TotalCount, _currentTable.ItemType);
					else if (_currentTable.TotalCount >= 0)
						text += string.Format("({0:N0} {1})", _currentTable.TotalCount, _currentTable.ItemType);
					else if (_currentTable.CurrentCount >= 0)
						text += string.Format("({0:N0} {1} completed)", _currentTable.CurrentCount, _currentTable.ItemType);
					text += Environment.NewLine;
				}

				//completed sub-tasks for this table
				if (_subTasks.Count > 0)
					text += _subTasks.Aggregate("", (current, t) => current + t.ToString());

				//current sub-task
				if (!string.IsNullOrEmpty(_currentTask.Name))
				{
					text += "\t" + _currentTask.Name + ": ";
					if (_currentTask.TotalCount >= 0 && _currentTask.CurrentCount >= 0)
						text += string.Format("({0:N0} completed of {1:N0} {2})",
						                      _currentTask.CurrentCount, _currentTask.TotalCount, _currentTask.ItemType);
					else if (_currentTask.TotalCount >= 0)
						text += string.Format("({0:N0} {1})", _currentTask.TotalCount, _currentTask.ItemType);
					else if (_currentTask.CurrentCount >= 0)
						text += string.Format("({0:N0} {1} completed)", _currentTask.CurrentCount, _currentTask.ItemType);
				}

				//optional migration status
				if (IsMigrating && !IsSlave && _migrationProgress != null)
				{
					text += Environment.NewLine + _migrationProgress.Text;
				}
				//optional extra status field
				if (!string.IsNullOrEmpty(ExtraStatus))
					text += string.Format("{0}\t{1}", Environment.NewLine, ExtraStatus);


				//elapsed time
				if (!IsSlave)
				{
					text += string.Format("{0}{0}Time elapsed{0}Total: {1}", Environment.NewLine, _overallTimer.CurrentTotal);
					if (_tableTimer.Started)
						text += string.Format("{0}This table: {1}", Environment.NewLine, _tableTimer.CurrentTotal);
					if (_taskTimer.Started)
						text += string.Format("{0}This task: {1}", Environment.NewLine, _taskTimer.CurrentTotal);
				}
				return text;
			}
		}


		public ExtractorProgress(bool isSlave = false)
		{
			_currentTable = new TableDef();
			_currentTask = new TableDef();
			_overallTimer = new StopWatch(false, false);
			_tableTimer = new StopWatch(false, false);
			_taskTimer = new StopWatch(false, false);
			_abort = false;
			IsSlave = isSlave;
			IsMigrating = false;
			Reset();
		}

		public void Reset()
		{
			State = ProgressState.Unknown;
			_mainTitle = "";
			_startTime = "";
			_currentTable.Clear();
			_currentTask.Clear(true);
			_overallTimer.Reset();
			_tableTimer.Reset();
			_taskTimer.Reset();
			_completedTables = new List<TableDef>();
			_subTasks = new List<TableDef>();
			_subTasks = new List<TableDef>();
			ExtraStatus = "";
		}

		public void Abort()
		{
			_abort = true;
		}

		private void CheckAbort()
		{
			if (!_abort) return;

			_abort = false;
			System.Threading.Thread.CurrentThread.Abort();
			State = ProgressState.Canceled;
		}

		/// <summary>
		/// Start the overall timer, but do not start any operations
		/// This method is optional as starting an operation will also start the overall timer
		/// </summary>
		/// <param name="mainTitle"></param>
		public void Start(string mainTitle = "")
		{
			CheckAbort();
			Reset();
			_mainTitle = mainTitle;
			_overallTimer.Start();
			var now = DateTime.Now;
			_startTime = string.Format("Started {0} at {1}", now.ToShortDateString(), now.ToShortTimeString());
			State = ProgressState.InProgress;
		}

		public void End(bool success, string status = null)
		{
			CheckAbort();
			if (!_overallTimer.Started)
				throw new Exception("Cannot end ExtractorProgress when it has not been started.");
			if (_taskTimer.Started)
				EndTask();
			if (_tableTimer.Started)
				EndTable();

			ExtraStatus = status;
			_overallTimer.Stop();
			State = success? ProgressState.Success : ProgressState.Failure;
		}

		/// <summary>
		/// Start a new table
		/// </summary>
		/// <param name="table">Name of the table</param>
		/// <param name="itemType">Name for the items retrieved</param>
		/// <param name="status"></param>
		public void StartTable(string table, string itemType, string status = "")
		{
			CheckAbort();
			if (string.IsNullOrEmpty(table) || itemType == null) //item type can be empty string but not null
				throw new Exception("Cannot start a new table. All parameters must be defined.");

			if (!_overallTimer.Started) _overallTimer.Start();
			if (_tableTimer.Started)
				EndTable();
			_tableTimer.Start();
			_currentTable.Name = table;
			_currentTable.ItemType = itemType;
			_currentTable.Status = status;
			_currentTable.SubTask = false;
		}

		public void UpdateTable(int totalCount, int currentCount, string status = null)
		{
			CheckAbort();
			if (totalCount >= 0)
				_currentTable.TotalCount = totalCount;
			if (currentCount >= 0)
				_currentTable.CurrentCount = currentCount;
			if (status != null)
				_currentTable.Status = status;
		}

		public void EndTable(int finalCount = -1, string status = null)
		{
			CheckAbort();
			if (_taskTimer.Started)
				EndTask();

			_tableTimer.Stop();
			if (status == null)
			{
				if (finalCount < 0) status = "Incomplete";
				else status = "Completed";
			}
			_currentTable.Status = status;
			_currentTable.TimeElapsed = _tableTimer.TotalTime;
			if (finalCount >= 0)
				_currentTable.TotalCount = finalCount;
			_completedTables.Add(_currentTable);
			
			_currentTable.Clear();
			_subTasks = new List<TableDef>();
			ExtraStatus = null;
		}

		/// <summary>
		/// Start an operation
		/// </summary>
		/// <param name="taskName">Name of the task to perform</param>
		/// <param name="itemType"></param>
		/// <param name="tableStatus">message to update the main table status</param>
		public void StartTask(string taskName, string itemType, string tableStatus = null, int totalCount = -1)
		{
			CheckAbort();
			if (string.IsNullOrEmpty(taskName) || string.IsNullOrEmpty(itemType))
				throw new Exception("Cannot start a new task. Task name and item type must be defined.");

			if (!_overallTimer.Started || !_tableTimer.Started)
				throw new Exception("Cannot start a new task before starting a table.");

			if (_taskTimer.Started)
				EndTask();
			_taskTimer.Start();
			_currentTask.Name = taskName;
			_currentTask.ItemType = itemType;
			if (tableStatus != null)
				_currentTable.Status = tableStatus;
			if (totalCount >= 0)
				_currentTask.TotalCount = totalCount;
		}

		public void UpdateTask(int currentCount, int totalCount = -1, string tableStatus = null, string extraStatus = null)
		{
			CheckAbort();
			if (!_taskTimer.Started) 
				throw new Exception("There is no task to update");

			if (totalCount >= 0)
				_currentTask.TotalCount = totalCount;
			if (currentCount >= 0)
				_currentTask.CurrentCount = currentCount;
			if (tableStatus != null)
				_currentTable.Status = tableStatus;
			if (extraStatus != null)
				ExtraStatus = extraStatus;
		}

		public void EndTask(int finalCount = -1, string status = null)
		{
			CheckAbort();
			if (!_taskTimer.Started)
				throw new Exception("There is no task to end");

			_taskTimer.Stop();
			if (finalCount >= 0)
				_currentTask.TotalCount = finalCount;
			if (status == null)
			{
				if (_currentTask.TotalCount < 0) status = "Incomplete";
				else status = "Completed";
			}
			_currentTask.Status = status;
			_currentTask.TimeElapsed = _taskTimer.TotalTime;
			_subTasks.Add(_currentTask);

			_currentTask.Clear(true);
			ExtraStatus = "";
		}

		public void SetMigrationProgress(ExtractorProgress mp)
		{
			_migrationProgress = mp; //TODO:confirm that this is a reference and not a copy
		}

	}
}