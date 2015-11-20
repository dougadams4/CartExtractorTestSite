using System;
using System.Collections.Generic;

namespace _4_Tell.CommonTools
{
		public enum QDupRule
		{
			ThowException,
			ReQueue, //moves position
			ReQueueOnPriority,
			Ignore,
			Allow
		}
		public enum QPriority
		{
			Low,
			Medium,
			High
		}

	/// <summary>
	/// Implements a FIFO queue using an ordered list 
	/// where each element in the queue also has anassociated site alias.
	/// This allows site-based queries for status and queue position.
	/// Also there are rule options for what to do in case of duplicate queueing
	/// </summary>
	/// <typeparam name="T"></typeparam>
	internal class BoostQueue<T>
	{
		#region Internal Params
		private struct QItem
		{
			public string Alias;
			public T Item;
			public QPriority Priority;
		}
		private readonly List<QItem> _queue;
		private QDupRule _dupRule;
		#endregion

		#region Public Params
		public int Count { get { return _queue.Count; } }
		#endregion

		public BoostQueue(QDupRule dupRule = QDupRule.ThowException)
		{
			_queue = new List<QItem>();
			_dupRule = dupRule;
		}

		public void Enqueue(string alias, T item, QPriority priority = QPriority.Medium)
		{
			lock (_queue)
			{
				var position = GetQPosition(alias);
				if (position >= 0)
				{
					switch (_dupRule)
					{
						case QDupRule.ThowException:
							throw new Exception(alias + " is already in the queue");
						case QDupRule.ReQueue:
							_queue.RemoveAt(position);
							break;
						case QDupRule.ReQueueOnPriority:
							if (priority <= _queue[position].Priority) return; //ignore
							_queue.RemoveAt(position);
							break;
						case QDupRule.Ignore:
							return;
						case QDupRule.Allow:
							break;
						default:
							throw new ArgumentOutOfRangeException();
					}
				}
				_queue.Add(new QItem {Alias = alias, Item = item, Priority = priority});
			}
		}

		public T Dequeue()
		{
			lock (_queue)
			{
				if (_queue.Count < 1) throw new Exception("Queue is empty");

				var qItem = _queue[0]; 
				_queue.RemoveAt(0);
				return qItem.Item;
			}
		}

		//note: the returned value can only be used if _queue is locked by calling method
		public int GetQPosition(string alias)
		{
			try
			{
				if (_queue.Count < 1) return -1;
				return _queue.FindIndex(0, x => x.Alias.Equals(alias));
			}
			catch (Exception)
			{
				return -1;
			}
		}

		public bool RemoveFirst(string alias)
		{
			try
			{
				lock (_queue)
				{
					var p = GetQPosition(alias);
					if (p < 0) return false;
					_queue.RemoveAt(p);
					return true;
				}
			}
			catch (Exception)
			{
				return false;
			}

		}
	}
}