//#define WEB

using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using _4_Tell.CommonTools;
#if INCLUDE_REPLICATOR
using _4_Tell.IO;
#endif
//OperationContext
	//MessageProperties RemoteEndpointMessageProperty


namespace _4_Tell.Logs
{

	public sealed class BoostLog //BoostLog is a singleton
	{
		#region Internal Parameters

#if INCLUDE_REPLICATOR
		private static Replicator _replicator;
#endif
#if !USAGE_READONLY
        private static DataLogBase _dataLog;
#endif
		private static readonly object _errWriteLock = new object();

		//event log parameters
		private static EventLog _eventLog = null;
		private static string _gmailUsername = "";
		private static string _gmailPassword = "";
		private static string _gmailToAddress = "";
		private static string _supportAddress = "support@4-tell.com";
		private static ReportType _adminReportType = ReportType.ServiceError;
		//private static ReportType _clientReportType = ReportType.None;
		private static List<string> _logBlockList = null; //list of clients to block from logging
		//private string m_errSuffix = "";
		private static readonly BoostLog _instance = new BoostLog();
		
		#endregion

		#region External Parameters

		public static BoostLog Instance
		{
			get { return _instance; }
		}

		public string ServerId { get; private set; }

		public readonly ReportSubscriptions Subscriptions;
		#endregion

		//main constructor is private because BoostLog is a singleton
		//use BoostLog.Instance instead of new BoostLog()
		private BoostLog()
		{
			ServerId = "";
			var source = "4-Tell Boost";
			var logMsg = "";

			try
			{
				//start logging to system event log
				source = ConfigurationManager.AppSettings.Get("LogSource") ?? "Unidentified";
				logMsg = "4-Tell Event Log started for " + source;
				_eventLog = new EventLog("4-Tell") {Source = source};

				//Read Gmail settings from web.config
				_gmailUsername = ConfigurationManager.AppSettings.Get("GmailUsername");
				_gmailPassword = ConfigurationManager.AppSettings.Get("GmailPassword");
				_gmailToAddress = ConfigurationManager.AppSettings.Get("GmailToAdress");
				ServerId = ConfigurationManager.AppSettings.Get("ServerId");
				var level = ConfigurationManager.AppSettings.Get("AdminReportLevel");
				_adminReportType = GetReportLevel(level);
				var admin = new UserContact {Name = "Admin", Email = _gmailToAddress};

				//Block logging for certain clients
#if INCLUDE_REPLICATOR
				if (TableAccess.Instance != null)
				{
					_logBlockList = TableAccess.Instance.LogBlockList; //ConfigurationManager.AppSettings.Get("LogBlockList");
					if (_logBlockList != null && _logBlockList.Any())
					{
						logMsg += "\nLogging will be blocked for the following clients:\n"
										+ _logBlockList.Aggregate((current, alias) => current + (alias + "\n"));
					}
				}

				//log any replicator startup issues
				logMsg += "\nInitializing Replicator";
				_replicator = Replicator.Instance;
				if (_replicator == null)
				{
					logMsg += "\nReplicator instance is null";
					Thread.Sleep(100);
					_replicator = Replicator.Instance;
				}
				if (_replicator != null)
				{

					logMsg += "\nInitializing DataLog";
					//log any usage log issues
					_dataLog = DataLogProxy.Instance;
					if (_dataLog == null)
					{
						logMsg += "\nDataLog instance is null";
						Thread.Sleep(100);
						_dataLog = DataLogProxy.Instance;
					}
					if (_dataLog != null)
					{
						if (!string.IsNullOrEmpty(_dataLog.ErrorText))
						{
							_eventLog.WriteEntry(_dataLog.ErrorText, EventLogEntryType.Warning);
						}
					}
				}
#endif
				_eventLog.WriteEntry(logMsg, EventLogEntryType.Information);
			}
			catch (Exception ex)
			{
				var errMsg = "Initialization Error for " + source + " Log: " + ex.Message;
				if (ex.InnerException != null)
					errMsg += "\nInner Exception: " + ex.InnerException.Message;
				if (string.IsNullOrEmpty(ex.StackTrace))
					errMsg += "\nStack Trace:\n" + ex.StackTrace;
				if (logMsg != null)
					errMsg += "\nLog Message = " + logMsg;
				if (_eventLog != null)
					_eventLog.WriteEntry(errMsg, EventLogEntryType.Error);
			}
		}

		public void SetBlockList(List<string> blockList)
		{
			_logBlockList = blockList;
		}

		#region Error Logging

		public void WriteEntry(EventLogEntryType type, string message, Exception ex, string clientAlias = "", bool supportAlert = false)
		{
			var errMsg = message; 
			var details = "\nException: " + ex.Message;;
			if (ex.InnerException != null)
				details += "\nInner Exception: " + ex.InnerException.Message;
			if (!string.IsNullOrEmpty(ex.StackTrace))
				details += "\nStack Trace:\n" + ex.StackTrace;

			WriteEntry(type, errMsg, details, clientAlias, supportAlert);
		}

		public void WriteEntry(EventLogEntryType type, string message, string preDetails, Exception ex, string clientAlias = "", bool supportAlert = false)
		{
			var errMsg = message;
			var details = preDetails ?? "";
			details += "\nException: " + ex.Message;
			if (ex.InnerException != null)
				details += "\nInner Exception: " + ex.InnerException.Message;
			if (!string.IsNullOrEmpty(ex.StackTrace))
				details += "\nStack Trace:\n" + ex.StackTrace;

			WriteEntry(type, errMsg, details, clientAlias, supportAlert);
		}

		public void WriteEntry(EventLogEntryType type, string message, string details = "", string clientAlias = "", bool supportAlert = false)
		{
			//if client alias sent in then add it to the message
			if (string.IsNullOrEmpty(clientAlias)) clientAlias = "";
			else details += "\nClientAlias = " + clientAlias;

			//get the request method and parameters
			details += GetSuffix();

			var error = new BoostError
				{
					Time = DateTime.Now, 
					Message = message, 
					Details = details, 
					Type = type, 
					Alias = clientAlias,
					SupportAlert = supportAlert
				};
			ThreadPool.QueueUserWorkItem(e => WriteEntry((BoostError) e), error);

			Debug.WriteLine(message);
		}

		//private Error Log function to be run in a separate thread
		private void WriteEntry(BoostError error)
		{
			if (error == null || error.Message.Length < 1) return; //nothing to do

			//truncate if too long
			const int logLen = 10000;
			error.Message = CheckLength(error.Message, logLen);
			error.Details = CheckLength(error.Details, logLen);

			if ((_logBlockList != null) && (!string.IsNullOrEmpty(error.Alias)))
			{
				//block this message from the log (if any clients are generating too many errors)
				if (_logBlockList.Any(alias => alias.Equals(error.Alias)))
					return;
			}

			lock (_errWriteLock)
			{
				//log all messages (unless blocked above)
				if (_eventLog != null)
					_eventLog.WriteEntry(error.Message + Environment.NewLine + error.Details, error.Type);


				//email certain messages using ClientPOC settings per user
				List<UserContact> users = null;
				var subject = "";
				var sendAdmin = true;
				//var sendClient = false;
				switch (error.Type)
				{
					case EventLogEntryType.Error:
						subject = "Error";
						sendAdmin = ((int) _adminReportType <= (int) (ReportType.ServiceError));
						if (Subscriptions != null)
							users = Subscriptions.GetUsers(error.Alias, ReportType.ServiceError);
#if !USAGE_READONLY
                        if (_dataLog != null)
						{
							ThreadPool.QueueUserWorkItem(a => _dataLog.AddError((string) a), error.Alias); //add to error tally
							ThreadPool.QueueUserWorkItem(e => _dataLog.SetLastError((BoostError) e), error); //replace last error
						}
#endif
						break;
					case EventLogEntryType.Warning:
						subject = "Warning";
						sendAdmin = ((int) _adminReportType <= (int) (ReportType.ServiceWarning));
						if (Subscriptions != null)
							users = Subscriptions.GetUsers(error.Alias, ReportType.ServiceWarning);
#if !USAGE_READONLY
                        if (_dataLog != null)
						{
							ThreadPool.QueueUserWorkItem(a => _dataLog.AddWarning((string) a), error.Alias); //add to Warning tally
							ThreadPool.QueueUserWorkItem(e => _dataLog.SetLastError((BoostError) e), error); //replace last error
						}
#endif
                        break;
					case EventLogEntryType.Information:
						subject = "Status Update";
						sendAdmin = ((int) _adminReportType <= (int) (ReportType.ServiceInfo));
						if (Subscriptions != null)
							users = Subscriptions.GetUsers(error.Alias, ReportType.ServiceInfo);
						break;
					default:
						subject = "Unknown EventType";
						sendAdmin = true;
						break;
				}

				if (users != null && users.Any())
				{
					const string preMessage = "This is an auto-generated email from the 4-Tell Boost service."
					                          + "If you would rather not receive these email notices, please adjust "
					                          + "your configuration settings or contact us at support@4-tell.com\n\n";
					foreach (var user in users)
					{
						try
						{
#if !CART_EXTRACTOR_TEST_SITE
                            GmailMessage.SendFromGmail(_gmailUsername, _gmailPassword, user.Email,
							                                 subject, preMessage + error.Message, ServerId, true);
#endif
						}
						catch (Exception ex)
						{
							var errMsg = string.Format("Error sending email to {0} <{1}>\n{2}\n\nOriginal message to send:\n{3}{4}",
							                           user.Name, user.Email, ex.Message, preMessage, error.Message);
							if (_eventLog != null)
								_eventLog.WriteEntry(errMsg, EventLogEntryType.Error);
						}
					}

					//always send admin messages that are sent to clients
					var emailList = "";
					var first = true;
					foreach (var user in users)
					{
						if (first) first = false;
						else emailList += ", ";
						emailList += string.Format("{0} <{1}>", user.Name, user.Email);
					}
					error.Message += "\n\nThis message was emailed to: " + emailList;
					sendAdmin = true;
				}

				subject = string.Format("{0}{1}", string.IsNullOrEmpty(error.Alias) ? "" : error.Alias + " ", subject); 
				if (sendAdmin && !string.IsNullOrEmpty(_gmailToAddress))
				{
					try
					{
#if !CART_EXTRACTOR_TEST_SITE
                        GmailMessage.SendFromGmail(_gmailUsername, _gmailPassword, _gmailToAddress,
						                                 subject, error.Message, ServerId, true);
#endif
					}
					catch (Exception ex)
					{
						var errMsg = string.Format("Error sending email to {0}\n{1}\n\nOriginal message to send:\n{2}",
																			 _gmailToAddress, ex.Message, error.Message);
						if (_eventLog != null)
							_eventLog.WriteEntry(errMsg, EventLogEntryType.Error);
					}
				}
				if (error.SupportAlert)
				{
					try
					{
						var errMsg = string.Format("{0}\n\n{1}", error.Message, error.Details);
#if !CART_EXTRACTOR_TEST_SITE
                        GmailMessage.SendFromGmail(_gmailUsername, _gmailPassword, _supportAddress,
																						 subject, errMsg, ServerId, true);
#endif
					}
					catch (Exception ex)
					{
						var errMsg = string.Format("Error sending email to {0}\n{1}\n\nOriginal message to send:\n{2}",
																			 _supportAddress, ex.Message, error.Message);
						if (_eventLog != null)
							_eventLog.WriteEntry(errMsg, EventLogEntryType.Error);
					}
				}
			} //end errWritesLock
		}

		private string CheckLength(string text, int maxLen)
		{
			if (text == null || text.Length <= maxLen) return text;
			return text.Trim().Substring(0, maxLen);
		}

		public static ReportType GetReportLevel(string level)
		{
			if (string.IsNullOrEmpty(level)) return ReportType.None;

			level = level.ToLower();
			if (level.Equals("all")) return ReportType.All;
			if (level.Equals("information")) return ReportType.ServiceInfo;
			if (level.Equals("warning")) return ReportType.ServiceWarning;
			if (level.Equals("error")) return ReportType.ServiceError;
			if (level.Equals("info")) return ReportType.ServiceInfo;
			return ReportType.None;
		}

		#endregion

		#region Utilities

		private string GetSuffix()
		{
			var suffix = "";
#if WEB
			try
			{
				var wh = new WebHelper();
				var wc = wh.GetContextOfRequest();
				suffix = wc.ToString();

				//get memory usage
				Process proc = Process.GetCurrentProcess();
				suffix += "\nCurrent RAM usage: " + proc.PrivateMemorySize64.ToString("N0");
			}
			catch (Exception ex)
			{
				suffix += "\n\nError getting context: " + ex.Message;
			}
#endif
			return suffix;
		}

		public static string ToCsv(IEnumerable<string> list)
		{
			if (list == null || !list.Any()) return "";
			return list.Aggregate((c, w) => string.Format("{0},{1}", c, w));
		}

		public static string ToCsv(IEnumerable<KeyValuePair<string, string>> list)
		{
			if (list == null || !list.Any()) return "";
			return list.Aggregate("", (c, w) => string.Format("{0}, {1}: {2}", c, w.Key, w.Value));
		}

		#endregion
	}
}