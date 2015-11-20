using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.ServiceModel.Web;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Serialization;
#if !CART_EXTRACTOR_TEST_SITE
using _4_Tell.BoostService;
using _4_Tell.DashService;
#endif
using _4_Tell.CartExtractors;
using _4_Tell.CommonTools;
using _4_Tell.Logs;

//XElement
	//HttpWebRequest HttpWebResponse WebException
	//Encoding
	//Stream
	//FaultException
	//WebFaultException
	//DictionaryEntry

namespace _4_Tell.IO
{
	/// <summary>
	/// Summary description for RestAccess
	/// </summary>
	public class TableAccess
	{
		public enum DataFormat
		{
			TabDelimited,
			Json,
			Xml
		}

		public string DebugText = "";
		public List<string> LogBlockList { get; private set; }
#if DEBUG
		public List<string> DebugIds { get; private set; }
#endif
		private readonly bool _saveAndReplicate;
		private readonly bool _uploadRemote;
		private List<string> _approvedIps;
		private const string _configParamsFilename = "ConfigParams.xml";
		private Dictionary<string, StreamWriter> _openTables;

		#region Instance

		private static readonly TableAccess _instance = new TableAccess();

		private TableAccess()
		{
			var temp = ConfigurationManager.AppSettings.Get("SaveLocal");
			_saveAndReplicate = temp != null && temp.Equals("true");
			temp = ConfigurationManager.AppSettings.Get("UploadRemote");
			_uploadRemote = temp != null && temp.Equals("true");
			_approvedIps = new List<string>();
			LogBlockList = new List<string>();
#if DEBUG
			DebugIds = new List<string>();
#endif
			_openTables = new Dictionary<string, StreamWriter>();
			ReloadConfigParams();
		}

	
		public static TableAccess Instance
		{
			get { return _instance; }
		}

		#endregion

		#region Whitelist

		private List<List<string>> LoadConfigParams()
		{
			var ipList = "";
			var logBlockList = "";
#if DEBUG
			var debugIds = "";
#endif

			var path = DataPath.Instance.Root + _configParamsFilename;
			try
			{
				var configParamsXml = XElement.Load(path);
				ipList = Input.GetValue(configParamsXml, "approvedDashIPs");
				logBlockList = Input.GetValue(configParamsXml, "logBlockList");
	#if DEBUG
				debugIds = Input.GetValue(configParamsXml, "debugIds");
	#endif
			}
			catch
			{
				var configParamXml = new XElement("configParams");
				if (ipList == null)
					ipList = ConfigurationManager.AppSettings.Get("ApprovedDashIPs");
				if (logBlockList == null)
					logBlockList = ConfigurationManager.AppSettings.Get("LogBlockList");
				try
				{
					configParamXml.Add(new XElement("approvedDashIPs", ipList));
					configParamXml.Add(new XElement("logBlockList", logBlockList));
	#if DEBUG
					configParamXml.Add(new XElement("debugIds", ""));
	#endif
					configParamXml.Save(path);
				}
				catch{}
			}


			//convert from comma separated list
			var configParamLists = new List<List<string>> 
			{
				string.IsNullOrEmpty(ipList) ? new List<string>() : ipList.Split(',', ' ').ToList(),
				string.IsNullOrEmpty(logBlockList) ? new List<string>() : logBlockList.Split(',', ' ').ToList(),
	#if DEBUG
				string.IsNullOrEmpty(debugIds) ? new List<string>() : debugIds.Split(',', ' ').ToList()
	#endif
			};
			return configParamLists;
		}

		public string ReloadConfigParams()
		{
			var result = "";
			try
			{
				var configParams = LoadConfigParams();
				
				//ip whitelist
				if (configParams.Count > 0)
				{
					if (configParams[0].Count == _approvedIps.Count && !configParams[0].Except(_approvedIps).Any())
					{
						result = "Whitelist was not changed.";
					}
					else
					{
						_approvedIps = configParams[0];
#if DEBUG
						result = "Whitelist updated: " + _approvedIps.Aggregate((w, j) => string.Format("{0}, {1}", w, j));
#else
						result = "Whitelist updated.";
#endif
					}
				}

				//log block list
				if (configParams.Count > 1)
				{
					if (configParams[1].Count == LogBlockList.Count && !configParams[1].Except(LogBlockList).Any())
					{
						result += "\nLogBlockList was not changed.";
					}
					else
					{
						LogBlockList = configParams[1];
						if (BoostLog.Instance != null)
							BoostLog.Instance.SetBlockList(LogBlockList);

#if DEBUG
						result += "\nLogBlockList updated: " + LogBlockList.Aggregate((w, j) => string.Format("{0}, {1}", w, j));
#else
						result += "\nLogBlockList updated.";
#endif
					}
				}

#if DEBUG
				//debug ids
				if (configParams.Count > 2)
				{
					if (configParams[2].Count == DebugIds.Count && !configParams[2].Except(DebugIds).Any())
					{
						result += "\nDebugIds were not changed.";
					}
					else
					{
						DebugIds = configParams[2];
						result += "\nDebugIds updated: " + DebugIds.Aggregate((w, j) => string.Format("{0}, {1}", w, j));
					}
				}
#endif
			}
			catch (Exception ex)
			{
				if (BoostLog.Instance != null)
					BoostLog.Instance.WriteEntry(EventLogEntryType.Error, "Error loading whitelist", ex);
				result += "An error occured during config param refresh.";
			}
			return result;
		}
		#endregion

		#region Write Tables (overloads for different data lists)

#if !USAGE_READONLY
		public string WriteTable(string alias, string dataName, List<DashPromotion> data, bool lastFile = false)
		{
			var sb = new StringBuilder(CartExtractor.CommonHeader + DashPromotion.Header());
				//need static CartExtractor.GetHeader(T)
			foreach (var a in data.Distinct())
				sb.Append(a.ToString());

			return WriteTable(alias, dataName, sb, lastFile);
		}
#endif

		public string WriteTable(string alias, string dataName, List<GeneratorFeaturedRec> data, bool lastFile = false)
		{
			var sb = new StringBuilder(CartExtractor.CommonHeader + GeneratorFeaturedRec.Header());
			foreach (var a in data.Distinct())
				sb.Append(a.ToString());

			return WriteTable(alias, dataName, sb, lastFile);
		}

		public string WriteTable(string alias, string dataName, List<GeneratorFeaturedTopSellRec> data, bool lastFile = false)
		{
			var sb = new StringBuilder(CartExtractor.CommonHeader + GeneratorFeaturedTopSellRec.Header());
			foreach (var a in data.Distinct())
				sb.Append(a.ToString());

			return WriteTable(alias, dataName, sb, lastFile);
		}

		public string WriteTable(string alias, string dataName, List<AttributeRecord> data, bool lastFile = false)
		{
			var sb = new StringBuilder(CartExtractor.CommonHeader + AttributeRecord.Header());
			foreach (var a in data.Distinct())
				sb.Append(a.ToString());

			return WriteTable(alias, dataName, sb, lastFile);
		}

		public string WriteTable(string alias, string dataName, List<InventoryRecord> data, bool lastFile = false)
		{
			var sb = new StringBuilder(CartExtractor.CommonHeader + InventoryRecord.Header());
			foreach (var a in data.Distinct())
				sb.Append(a.ToString());

			return WriteTable(alias, dataName, sb, lastFile);
		}

		public string WriteTable(string alias, string dataName, List<ProductRecord> data, bool lastFile = false)
		{
			var sb = new StringBuilder(CartExtractor.CommonHeader + ProductRecord.Header());
			foreach (var a in data.Distinct())
				sb.Append(a.ToString());

			return WriteTable(alias, dataName, sb, lastFile);
		}

		public bool WriteTableRow(string alias, string dataName, ProductRecord data)
		{
			StreamWriter file = null;
			try
			{
				if (_openTables.TryGetValue(dataName, out file))
				{
					if (file != null)
					{
						file.Write(data.ToString());
						return true;
					}
					_openTables.Remove(dataName); //remove null entry and add below
				}

				//first time so open, write the headers, and store the handle
				var path = IO.DataPath.Instance.ClientDataPath(alias, true);
				if (!Directory.Exists(path))
					Directory.CreateDirectory(path);
				file = File.CreateText(path + dataName);
				file.Write(CartExtractor.CommonHeader + ProductRecord.Header() + data.ToString());
				_openTables.Add(dataName, file);
				return true;
			}
			catch (Exception ex)
			{
				if (BoostLog.Instance != null)
					BoostLog.Instance.WriteEntry(EventLogEntryType.Warning, "Error writing row to " + dataName, ex, alias);
				if (file != null)
				{
					file.Close();
					_openTables.Remove(dataName);
				}
				return false;
			}
		}

#if !CART_EXTRACTOR_TEST_SITE
        public bool CloseTable(string alias, string dataName)
		{
			StreamWriter file = null;
			try
			{
				if (_openTables.TryGetValue(dataName, out file))
				{
					if (file != null)
					{
						file.Close();
					}
					_openTables.Remove(dataName); //remove null entry and add below
					Replicator.Instance.ReplicateDataFile(alias, true, dataName);
					return true;
				}
				return false;
			}
			catch (Exception ex)
			{
				if (BoostLog.Instance != null)
					BoostLog.Instance.WriteEntry(EventLogEntryType.Warning, "Error closing table " + dataName, ex, alias);
				return false;
			}
		}
#endif

		public string WriteTable(string alias, string dataName, List<SalesRecord> data, bool lastFile = false)
		{
			var sb = new StringBuilder(CartExtractor.CommonHeader + SalesRecord.Header());
			foreach (var a in data.Distinct())
				sb.Append(a.ToString());

			return WriteTable(alias, dataName, sb, lastFile);
		}

		public string WriteTable(string alias, string dataName, List<ClickRecord> data, bool lastFile = false)
		{
			var sb = new StringBuilder(CartExtractor.CommonHeader + ClickRecord.Header());
			foreach (var a in data.Distinct())
				sb.Append(a.ToString());

			return WriteTable(alias, dataName, sb, lastFile);
		}

		public string WriteTable(string alias, string dataName, List<CustomerRecord> data, bool lastFile = false)
		{
			var sb = new StringBuilder(CartExtractor.CommonHeader + CustomerRecord.Header());
			foreach (var a in data.Distinct())
				sb.Append(a.ToString());

			return WriteTable(alias, dataName, sb, lastFile);
		}

		public string WriteTable(string alias, string dataName, List<ExclusionRecord> data, bool lastFile = false)
		{
			var sb = new StringBuilder(CartExtractor.CommonHeader + ExclusionRecord.Header());
			foreach (var a in data.Distinct())
				sb.Append(a.ToString());

			return WriteTable(alias, dataName, sb, lastFile);
		}

		public string WriteTable(string alias, string dataName, List<ReplacementRecord> data, bool lastFile = false)
		{
			var sb = new StringBuilder(CartExtractor.CommonHeader + ReplacementRecord.Header());
			foreach (var a in data.Distinct())
				sb.Append(a.ToString());

			return WriteTable(alias, dataName, sb, lastFile);
		}

		public string WriteTable(string alias, string dataName, List<List<string>> data, bool lastFile = false)
		{
			const string columnDelim = "\t";
			const string rowDelim = "\r\n";
			var sb = new StringBuilder(CartExtractor.CommonHeader); 
			//data specific header must be the first string list in the data
			foreach (var rowList in data)
			{
				var row = rowList.Aggregate((c, j) => string.Format("{0}{2}{1}", c, j, columnDelim));
				sb.Append(row + rowDelim);
			}
			return WriteTable(alias, dataName, sb, lastFile);
		}

		public string WriteTable(string alias, string dataName, StringBuilder data, bool lastFile = false)
		{
			var result = "";

			if (_saveAndReplicate) //save file locally
			{
				try
				{
					var path = IO.DataPath.Instance.ClientDataPath(alias, true);
					if (!Directory.Exists(path))
						Directory.CreateDirectory(path);
					using (var sw = File.CreateText(path + dataName))
					{
						sw.Write(data.ToString());
					}
#if !CART_EXTRACTOR_TEST_SITE
                    Replicator.Instance.ReplicateDataFile(alias, true, dataName);
#endif
					result = "File Saved.";
				}
				catch (Exception ex)
				{
					result = ex.Message;
				}
			}

#if !CART_EXTRACTOR_TEST_SITE
            if (_uploadRemote) //upload to 4-tell service
			{
				//pre-pend header line with parameters
				data.Insert(0, alias + "\t" + dataName + "\t" + (lastFile ? "1" : "0") + "\r\n");

				//convert data to a byte array
				var bData = Encoding.UTF8.GetBytes(data.ToString());

				//upload to 4-Tell
				result += RestAccess.Instance.Get4TellResponse("UploadData/stream", "", bData, "POST");
			}
			else if (lastFile) //no upload flag so must launch generator directly
			{
				result += "\n" + RestAccess.Instance.Generate4TellTables(alias);
			}
#endif

			return result;
		}

		#endregion

		#region Read Tables

		public bool ReadTable(string filename, string alias, out List<ExclusionRecord> data)
		{
			const int minColumns = 1;
			const int maxColumns = 1;
			const int headerRows = 2; //first row is file version and date. Second row is column headings

			data = null;
			var rawData = ReadTable(filename, alias, minColumns, maxColumns, headerRows);
			if (rawData == null || rawData.Count < 2)	return false;

			data = rawData.Select(x => new ExclusionRecord(x)).ToList();
			return true;
		}

		public bool ReadTable(string filename, string alias, out List<ReplacementRecord> data)
		{
			const int minColumns = 2;
			const int maxColumns = 2;
			const int headerRows = 2; //first row is file version and date. Second row is column headings

			data = null;
			var rawData = ReadTable(filename, alias, minColumns, maxColumns, headerRows);
			if (rawData == null || rawData.Count < 2) return false;

			data = rawData.Select(x => new ReplacementRecord(x)).ToList();
			return true;
		}

		public bool ReadTable(string filename, string alias, out List<SalesRecord> data, bool migrating = false)
		{
			const int minColumns = 3;
			const int maxColumns = 5;
			const int headerRows = 2; //first row is file version and date. Second row is column headings

			data = null;
			var rawData = ReadTable(filename, alias, minColumns, maxColumns, headerRows, DataFormat.TabDelimited, !migrating, migrating);
			if (rawData == null || rawData.Count < 2) return false;

			//var header = rawData[0];
			rawData.RemoveAt(0);
			//var iOid = Array.IndexOf(header, "OrderId");
			//var iCid = Array.IndexOf(header, "CustomerId");
			//var iPid = Array.IndexOf(header, "ProductId");
			//var iQty = Array.IndexOf(header, "Quantity");
			//var iDat = Array.IndexOf(header, "Date");
			data = rawData.Where(x => x.Length == 4).Select(x => new SalesRecord(x)
																		//{
																		//  OrderId = iOid < 0 ? "" : x[iOid],
																		//  CustomerId = iCid < 0 ? "" : x[iCid],
																		//  ProductId = iPid < 0 ? "" : x[iPid],
																		//  Quantity = iQty < 0 ? "" : x[iQty],
																		//  Date = iDat < 0 ? "" : x[iDat],
																		//}
														).ToList();
			return true;
		}

		public bool ReadTable(string filename, string alias, out List<ClickRecord> data, bool migrating = false)
		{
			const int minColumns = 4;
			const int maxColumns = 4;
			const int headerRows = 2; //first row is file version and date. Second row is column headings

			data = null;
			var rawData = ReadTable(filename, alias, minColumns, maxColumns, headerRows, DataFormat.TabDelimited, !migrating, migrating);
			if (rawData == null || rawData.Count < 2) return false;

			rawData.RemoveAt(0); //ignore header
			data = rawData.Select(x => new ClickRecord(x)).ToList();
			return true;
		}

		public bool ReadTable(string filename, string alias, out List<ProductRecord> recs)
		{
			const int minColumns = 15;
			const int maxColumns = 15;
			const int headerRows = 2; //first row is file version and date. Second row is column headings

			recs = null;
			var data = ReadTable(filename, alias, minColumns, maxColumns, headerRows);
			if (data == null || data.Count < 2) return false;

			data.RemoveAt(0); //ignore header
			recs = data.Select(x => new ProductRecord(x)).ToList();
			return true;
		}

#if !CART_EXTRACTOR_TEST_SITE
        public bool ReadTable(string filename, string alias, out FeaturedRecommendations recs)
		{
			//var site = DashSiteList.Instance.Get(alias);
			//if (site == null) throw new Exception(string.Format("Cannot read manual recs. Site not found. {0}", alias));

			const int minColumns = 2; //ranking can be omitted
			const int maxColumns = 4;
			const int headerRows = 2; //first row is file version and date. Second row is column headings

			recs = null;
			var data = ReadTable(filename, alias, minColumns, maxColumns, headerRows);
			if (data == null || data.Count < 2) return false;

			recs = new FeaturedRecommendations(data);
			return true;
		}
#endif

		public bool ReadTable(string filename, string alias, out List<GeneratorFeaturedTopSellRec> recs)
		{
			const int minColumns = 1; //ranking can be omitted
			const int maxColumns = 2;
			const int headerRows = 2; //first row is file version and date. Second row is column headings

			recs = null;
			var data = ReadTable(filename, alias, minColumns, maxColumns, headerRows);
			if (data == null || data.Count < 2) return false; //none found

			//TODO: Validate header?
			var hasRankings = data[0].Length == 2;
			data.RemoveAt(0); //drop header row
			if (hasRankings)
			{
				recs = data.Select(x => new GeneratorFeaturedTopSellRec
					{
						ProductId = x[0],
						Ranking = Input.SafeIntConvert(x[1])
					}
					).ToList();
			}
			else
			{
				var rank = 1;
				recs = data.Select(x => new GeneratorFeaturedTopSellRec
					{
						ProductId = x[0],
						Ranking = (rank++)
					}
					).ToList();
			}
			return true;
		}

		public List<string[]> ReadTable(string filename, string alias, int minColumns, int maxColumns, int headerRows, DataFormat format = DataFormat.TabDelimited, bool staging = true, bool migrating = false)
		{
			var data = new List<string[]>();
			var badData = new List<string[]>();

			// Open file
			StreamReader file = null;
			try
			{
				var path = DataPath.Instance.ClientDataPath(ref alias);
				if (staging) path += "upload\\";
				if (migrating) path += "migration\\";
				file = new StreamReader(path + filename);
			}
			catch (Exception)
			{
				//It's ok for files not to exist --usually just means nothing has been added to that table yet
				if (file != null) file.Close();
				return data;
			}

			try
			{
				if (format.Equals(DataFormat.Xml))
					throw new NotImplementedException();

				//tab delimited and json can be treated the same 
				// --since we assume json will be an array of strings with one header row and multiple data rows
				string row = null;
				var delimiter = format.Equals(DataFormat.TabDelimited) ? "\t" : null;
				if (headerRows > 0)
				{
					for (var i = 0; i < headerRows; i++)
						row = file.ReadLine();
					// In the future, may want to check for version number from first header row

					// The last header row is assumed to be the column names
					if (!string.IsNullOrEmpty(row))
					{
						var headerRow = Input.SplitRow(row, delimiter).Where(x => !string.IsNullOrEmpty(x)).ToArray();
						//var headerRow = format.Equals(DataFormat.TabDelimited) ? row.Split('\t') : Input.SplitJsonRow(row).ToArray();
						data.Add(headerRow);
					}
				}

				while (true)
				{
					row = file.ReadLine();
					if (row == null)
						break; // EOF

					var rowData = Input.SplitRow(row, delimiter).ToArray();
					//var rowData = format.Equals(DataFormat.TabDelimited) ? row.Split('\t') : Input.SplitJsonRow(row).ToArray();
					if (rowData.Length < minColumns || rowData.Length > maxColumns) //ignore bad rows
					{
						badData.Add(rowData);
						continue;
					}
					data.Add(rowData);
				}

				if (badData.Count > 0)
				{
					var message = string.Format("Illegal data found in {0} for alias {1}", filename, alias);
					var details = badData.Aggregate("", (a, b) =>
					                                    string.Format("{0}, {1}", a,
					                                                  b.Aggregate("",
					                                                              (c, d) =>
					                                                              string.Format(
						                                                              "{0}, {1}", c, d))));
					if (BoostLog.Instance != null)
						BoostLog.Instance.WriteEntry(EventLogEntryType.Warning, message, details, alias);
				}
				return data;
			}
			catch (Exception)
			{
				if (BoostLog.Instance != null)
					BoostLog.Instance.WriteEntry(EventLogEntryType.Error, String.Format("Error reading file {0}", filename));
				return data;
			}
			finally
			{
				if (file != null) file.Close();
			}

		}

		public bool ReadFeedFile(string filename, string alias, out FileStream fileStream, string subfolder = null)
		{
			try
			{
				if (filename.StartsWith("file:")) filename = filename.Substring(5);
				var path = DataPath.Instance.ClientDataPath(ref alias, true, subfolder);
				//file = new StreamReader(path + filename);
				//data = file.ReadToEnd();
				fileStream = File.OpenRead(path + filename);
				return true;
			}
			catch (Exception)
			{
				//let the calling method decide whether this is an error
				fileStream = null;
				return false;
			}
		}

		public bool ReadFeedFile(string filename, string alias, out MemoryStream dataStream, string subfolder = null) 
		{
			//data = "";

			// Open file
			//StreamReader file = null;
			dataStream = new MemoryStream();
			try
			{
				if (filename.StartsWith("file:")) filename = filename.Substring(5);
				var path = DataPath.Instance.ClientDataPath(ref alias, true, subfolder);
				//file = new StreamReader(path + filename);
				//data = file.ReadToEnd();
				using (var fileStream = File.OpenRead(path + filename))
				{
					fileStream.CopyTo(dataStream);
				}
				dataStream.Seek(0, SeekOrigin.Begin);
				return true;
			}
			catch (Exception)
			{
				//let the calling method decide whether this is an error
				return false;
			}
		}
		#endregion

		#region Clear Tables

		public string ClearTable(string alias, string dataName)
		{
			try
			{
				var path = IO.DataPath.Instance.ClientDataPath(alias, true);
				if (!Directory.Exists(path) || !File.Exists(path + dataName))
					return "No data to clear";
				File.Delete(path + dataName);
				return "Data cleared";
			}
			catch (Exception ex)
			{
				return ex.Message;
			}
		}

		#endregion

		#region Serialization

		public static string XmlSerialize(Object obj, string wrappedName = null)
		{
			try
			{
				//settings
				var settings = new XmlWriterSettings
				{
					Indent = true,
					OmitXmlDeclaration = true
				};

				var serializer = new XmlSerializer(obj.GetType(), "");
				using (var stream = new StringWriter())
				{
					using (var writer = XmlWriter.Create(stream, settings))
					{
						serializer.Serialize(writer, obj);
					}
					var result = stream.ToString();
					if (!string.IsNullOrEmpty(wrappedName))
						result = string.Format("<{0}>{1}</{0}>", wrappedName, result);
					return result;
				}
			}
			catch (Exception ex)
			{
				if (BoostLog.Instance != null)
					BoostLog.Instance.WriteEntry(EventLogEntryType.Error, "Error Serializing XML", ex);
				return null;
			}
		}

		public static T XmlDeserialize<T>(string s)
 		{
			try
			{
				var locker = new object(); //TODO: What does this locker accomplish??
				using (var stringReader = new StringReader(s))
				{
					using (var reader = new XmlTextReader(stringReader))
					{
						var xmlSerializer = new XmlSerializer(typeof(T));
						lock (locker)
						{
							var item = (T)xmlSerializer.Deserialize(reader);
							return item;
						}
					}
				}
			}
			catch (Exception ex)
			{
				//not necessarily an error
				//if (BoostLog.Instance != null)
				//  BoostLog.Instance.WriteEntry(EventLogEntryType.Error, "Error Deserializing XML", ex);
				return default(T);
			}
		}

		public static string JsonSerialize(Object obj, string wrappedName = null)
		{

			try
			{
				var serializer = new System.Web.Script.Serialization.JavaScriptSerializer();
				var result = serializer.Serialize(obj);
				if (!string.IsNullOrEmpty(wrappedName))
					result = string.Format("{{\"{0}\":{1}}}", wrappedName, result);
				return result;
			}
			catch (Exception ex)
			{
				if (BoostLog.Instance != null)
					BoostLog.Instance.WriteEntry(EventLogEntryType.Error, "Error Serializing JSON", ex);
				return null;
			}
		}
    
		public static T JsonDeserialize<T>(string s)
    {
	    if (string.IsNullOrEmpty(s)) return default(T);
			try
			{
				var serializer = new System.Web.Script.Serialization.JavaScriptSerializer();
				return serializer.Deserialize<T>(s); 
			}
			catch (Exception ex)
			{
				//not necessarily an error
				//if (BoostLog.Instance != null)
				//  BoostLog.Instance.WriteEntry(EventLogEntryType.Error, "Error Deserializing JSON", ex);
				return default(T);
			}
		}
			
		public static T Deserialize<T>(string s, WebMessageFormat format)
		{
			T result;
			if (format.Equals(WebMessageFormat.Xml))
				result = XmlDeserialize<T>(s);
			else
				result = JsonDeserialize<T>(s);
			return result;
		}

		public static string DeserializeString(string s, WebMessageFormat format)
		{
			var result = Deserialize<string>(s, format);
			if (string.IsNullOrEmpty(result))
			{
				if (string.IsNullOrEmpty(s)) return s;
				result = s;
			}

			int index;
			char c;
			var start = 0;
			while ((index = result.IndexOf('\\', start)) > -1)
			{
				if (index + 1 < result.Length && result[index + 1].Equals('/'))
				{
					result = result.Replace("\\/", "/");
					start = index + 1;
					continue;
				}
				if (index + 6 >= result.Length) break;
				var test = result.Substring(index, 6);
				if (Input.TryConvert(test, out c))
					result = result.Replace(test, c.ToString(CultureInfo.InvariantCulture));
				start = index + 1;
			}
			return result;
		}

		#endregion

#if !CART_EXTRACTOR_TEST_SITE
        #region Response Formatting

		//Check for incoming format parameter and set conttext accordingly
		public WebMessageFormat CheckResponseFormat(bool ipCheck = true)
		{
			WebContextProxy wc; //throwaway placeholder
			return CheckResponseFormat(out wc, ipCheck);
		}

		public WebMessageFormat CheckResponseFormat(out WebContextProxy wc, bool ipCheck = true)
		{
			var wh = new WebHelper();
			wc = wh.GetContextOfRequest();

			var current = WebOperationContext.Current;
			if (ipCheck && !wc.IsInternal && _approvedIps != null && _approvedIps.Count > 0 && !_approvedIps.Contains(wc.IP))
			{
				if (current == null || current.IncomingRequest.UriTemplateMatch == null)
					throw new WebFaultException<string>("Boost access denied.", HttpStatusCode.Forbidden);

				var authQuery = current.IncomingRequest.UriTemplateMatch.QueryParameters["authentication"];
				if (string.IsNullOrEmpty(authQuery) || !InterServiceAuth.Authenticate(authQuery))
					throw new WebFaultException<string>("Boost access denied.", HttpStatusCode.Forbidden);
			}
			if (current != null 
				&& (wc.Verb.Equals("post", StringComparison.CurrentCultureIgnoreCase) 
						|| wc.Verb.Equals("options", StringComparison.CurrentCultureIgnoreCase)))
			{
				current.OutgoingResponse.Headers.Add("Access-Control-Allow-Origin", "*");
				current.OutgoingResponse.Headers.Add("Access-Control-Allow-Methods", "POST,GET,OPTIONS");
				current.OutgoingResponse.Headers.Add("Access-Control-Max-Age", "1000");
				var headerCollection = current.IncomingRequest.Headers;
				if (headerCollection.Count > 0)
				{
					var keys = headerCollection.Cast<object>().Select((t, i) => headerCollection.GetKey(i)).ToList();
					if (keys.Any())
					{
						if (keys.Any(x => x.Equals("Access-Control-Request-Headers")))
						{
							var values = headerCollection["Access-Control-Request-Headers"];
							if (!string.IsNullOrEmpty(values))
								current.OutgoingResponse.Headers.Add("Access-Control-Allow-Header", values);
						}
						else
						{
							var headers = keys.Aggregate((c, w) => string.Format("{0},{1}", c, w));
							current.OutgoingResponse.Headers.Add("Access-Control-Allow-Header", headers);
						}
					}
				}
				//check for OPTIONS preflight and short-circuit response
				//Question: what happens if you throw a web exception with status code 200?
				if (wc.Verb.Equals("options", StringComparison.CurrentCultureIgnoreCase))
					throw new WebFaultException<string>("", HttpStatusCode.OK);
			}

			// if a format query string parameter has been specified, set the response format to that. If no such
			// query string parameter exists the Accept header will be used
			if (current != null && current.IncomingRequest.UriTemplateMatch != null)
			{
				var formatQuery = current.IncomingRequest.UriTemplateMatch.QueryParameters["format"];
				if (!string.IsNullOrEmpty(formatQuery))
				{
					WebMessageFormat format;
					if (formatQuery.Equals("xml", StringComparison.OrdinalIgnoreCase))
						format = WebMessageFormat.Xml;
					else if (formatQuery.Equals("json", StringComparison.OrdinalIgnoreCase))
						format = WebMessageFormat.Json;
					else
						throw new WebFaultException<string>(string.Format("Unsupported format '{0}'", formatQuery),
																								HttpStatusCode.BadRequest);
					current.OutgoingResponse.Format = format;
					return format;
				}
			}

			//format not provided. Attempt to get it from context
			if (wc.ContentType.Contains("json"))
				return WebMessageFormat.Json;

			return WebMessageFormat.Xml;
		}

		#endregion
#endif
	} //class
} //namespace