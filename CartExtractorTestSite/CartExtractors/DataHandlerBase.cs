using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Web;
using System.Xml.Linq;
using _4_Tell.CommonTools;
using _4_Tell.IO;
using _4_Tell.Logs;

namespace _4_Tell.CartExtractors
{
	public abstract class DataHandlerBase
	{
		protected SiteRules _rules;
		protected CartExtractor _cart;
		protected ExtractorProgress _progress;
		protected DataGroup _group;
		protected DateTime _exportDate;
		protected bool _exportDateInitialized;
		protected bool _migrationSlave;
		public List<string> Header { get; protected set; }
		public List<List<string>> Rows { get; protected set; }

		public DataHandlerBase(SiteRules rules, CartExtractor cart, ExtractorProgress progress, DataGroup group)
		{
			_cart = cart;
			_rules = rules;
			_progress = progress;
			_group = group;
			_exportDate = DateTime.Now;
			_exportDateInitialized = false;
			_migrationSlave = _rules.MigrationRules != null && _rules.MigrationRules.Enabled
												&& !_rules.MigrationRules.IsMigrationMaster;
			Reset();
		}

		public void Reset(DateTime exportDate)
		{
			_exportDate = exportDate;
			_exportDateInitialized = true;
			Header = null;
			Rows = new List<List<string>>();
		}

		public virtual void Reset()
		{
			if (_group != DataGroup.Sales && _group != DataGroup.Customers) //export date must be set separately
				_exportDate = DateTime.Now;
			Header = null;
			Rows = new List<List<string>>();
		}

		/// <summary>
		/// Generic data extractor 
		/// using relevant site rules and GetFeedData to pull the data 
		/// and using TableAccess to store it
		/// Carts can override GetFeedData, GetQueryParams, and or GetApiUrl to alter the behavior
		/// </summary>
		/// <param name="itemCount"></param>
		/// <returns></returns>
		public string GetData(out int itemCount, int maxRows = 5000)
		{
			Reset();
			itemCount = 0;
			var totalExpected = 0;
			var totalCount = 0;
			try
			{
				_progress.StartTask("Retrieving data", "items");
				var extraFields = (_group == DataGroup.Catalog) ? _cart.GetExtraFields() : "";

				//check and see if we can get the data count
				_progress.UpdateTable(-1, -1, "Getting count");
				totalExpected = _cart.GetRowCount(_group, _exportDate);
				_progress.UpdateTask(-1, totalExpected > 1 ? totalExpected - 1 : -1, "Extracting");
				var minRange = 1; // _rules.ApiVersion < 3 ? -1 : 1; //older API used IdRange instead of RowRange
				var rowsPerRequest = _cart.GetRowsPerRequest(_group, totalExpected, maxRows);

				//Request the first set of data
				List<List<string>> data = null;
				_cart.GetFeedData(out data, _group, _exportDate, minRange, rowsPerRequest, extraFields);
				if (data.Count < 2)
				{
					if (data.Count > 0 && BoostLog.Instance != null) //possibly the whole feed was saved to one data row
					{
						var details = data[0].Aggregate("", (c, w) => string.Format("{0},{1}", c, w));
						if (details.Length > 250)
							details = details.Substring(0, 100) + " <...> " + details.Substring(details.Length - 100, 100);
						BoostLog.Instance.WriteEntry(EventLogEntryType.Warning,
							string.Format("{0} feed has only one row", _group.ToString()), details, _rules.Alias);
					}
					throw new Exception("no data");
				}
				var newCount = data.Count - 1; //first set must include header row
				totalCount = newCount;
				_progress.UpdateTask(totalCount, -1, "Parsing data");
				var parentCount	= AddData(data, true);
				if (_group == DataGroup.Catalog)
					_progress.UpdateTask(-1, -1, "",
						string.Format("Storing child data: ({0:N0} parents, {1:N0} children)", parentCount, totalCount - parentCount));


				do // request more data from the feed
				{
					//see if there are more to parse
					if (rowsPerRequest < 1
							|| (newCount > rowsPerRequest && !_rules.ApiAllowExtraRows)
							|| (newCount < rowsPerRequest - _rules.ApiExtraHeaders - 1))
						break; //no more data to get

					minRange += rowsPerRequest;
					_progress.UpdateTask(totalCount, -1, "Requesting data");
					_cart.GetFeedData(out data, _group, _exportDate, minRange, rowsPerRequest, extraFields);
					if (!_rules.ApiHeaderIsOnlyOnFirstRow)
					{
						if (data.Count < 2) break; //only the header
						data.RemoveAt(0); //remove the header row
					}
					else if (data.Count < 1) break; //no more data received
					newCount = data.Count;
					totalCount += newCount;
					_progress.UpdateTask(totalCount, -1, "Parsing data");


					parentCount = AddData(data, false);
					if (_group == DataGroup.Catalog)
						_progress.UpdateTask(-1, -1, "",
							string.Format("Storing child data: ({0:N0} parents, {1:N0} children)", parentCount, totalCount - parentCount));
				} while (true);
			}
			catch (Exception ex)
			{
				if (_group == DataGroup.Catalog) throw ex;
				if (BoostLog.Instance != null)
					BoostLog.Instance.WriteEntry(EventLogEntryType.Warning,
						string.Format("{0} feed error", _group.ToString()), ex, _rules.Alias);

				return ex.Message;
			}
			finally
			{
				_progress.EndTask(totalCount);
			}

			//totalCount checks
			switch (_group)
			{
				case DataGroup.Catalog:
					if (!_rules.AllowLowerCatalogCount && totalCount < totalExpected - 1) //sometimes the count includes the header row
						throw new Exception(string.Format("{0} feed ({1:N0}) does not match expected count ({2:N0})",
																							_group.ToString(), totalCount, totalExpected));
					if (_rules.ApiMinimumCatalogSize > totalCount)
						throw new Exception("Catalog size is less than the minimum required in the site rules");
					break;
				case DataGroup.Sales:
					if (!_rules.AllowLowerSalesCount && totalCount < totalExpected - 1)
						throw new Exception(string.Format("{0} feed ({1:N0}) does not match expected count ({2:N0})",
																							_group.ToString(), totalCount, totalExpected));
					break;
				case DataGroup.Customers:
					if (!_rules.AllowLowerCustomerCount && totalCount < totalExpected - 1)
						throw new Exception(string.Format("{0} feed ({1:N0}) does not match expected count ({2:N0})",
																							_group.ToString(), totalCount, totalExpected));
					break;
			}
			return ProcessData(out itemCount);
		}

		public virtual int AddData(List<List<string>> data, bool dataIncludesHeader)
		{
			if (_group == DataGroup.Catalog)
				return _cart.Catalog.AddData(data, dataIncludesHeader);

			if (Header == null && !dataIncludesHeader)
				throw new Exception("Header has not beed defined");
			if (data == null || data.Count < 1)
				return Rows.Count;

			if (dataIncludesHeader)
			{
				Header = data[0].Select(x => x.Replace(" ", "")).ToList(); //cannot have spaces in header names
				data.RemoveAt(0);
				_rules.Fields.SetFieldHeaderIndices(_group, Header);
			}
			var headColCount = Header.Count;
			var rowCount = data.Count;

			var errors = 0;
			foreach (var cols in data) //each row of data contains a list of column values
			{
				try
				{
					if (cols.Count != headColCount)
					{
						if (cols.Count.Equals(headColCount - 1)) //sometimes the final column will be truncated if empty
							cols.Add("");
						else //data error --rows must match header
						{
							errors++;
							continue;
						}
					}
					Rows.Add(cols);
				}
				catch (Exception ex)
				{
					errors++;
				}
			}
			return Rows.Count;
		}

		public string ProcessData(out int itemCount, List<List<string>> data = null)
		{
			itemCount = -1;
			if (data != null)
			{
				Reset();
				AddData(data, true);
			}

			if (Header == null)
				throw new Exception(string.Format("{0} header has not beed defined", _group.ToString()));
			if (Rows.Count < 1)
				return "no data";

			return WriteTable(out itemCount);
		}

		public abstract string WriteTable(out int itemCount);
	}
}