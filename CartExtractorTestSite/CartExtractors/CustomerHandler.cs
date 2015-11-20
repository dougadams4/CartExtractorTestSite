using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Xml.Linq;
using _4_Tell.CommonTools;
using _4_Tell.Logs;
using _4_Tell.IO;

namespace _4_Tell.CartExtractors
{
	public class CustomerHandler : DataHandlerBase
	{

		public CustomerHandler(SiteRules rules, CartExtractor cart, ExtractorProgress progress)
			: base(rules, cart, progress, DataGroup.Customers)
		{
		}

		public override string WriteTable(out int itemCount)
		{
			itemCount = -1;
			if (!_exportDateInitialized)
				throw new Exception("Customer export date was not initialized.");				
			if (Rows.Count < 1)
				return "no data";

			_progress.StartTask("Parsing data", "items", null, Rows.Count);

			var iCId = _rules.Fields.GetHeaderIndex(FieldName.CustomerId);
			if (iCId < 0)
				return string.Format("bad header: {0}", Header.Aggregate((w, j) => string.Format("{0},{1}", w, j)));

			var errors = 0;
			var colCount = Header.Count;
			var data = new List<List<string>> { Header };
			foreach (var cols in Rows)
			{
				if (cols.Count != colCount)
				{
					//data error --rows must match header
					errors++;
					continue;
				}
				if (cols[iCId].Length < 1)
				{
					//data error --each row must have a customerId
					errors++;
					continue;
				}
				data.Add(cols);
			}
			_progress.EndTask(data.Count);
			if (data.Count < 2) //first row is header
				return "no data";

			itemCount = data.Count;
			_progress.UpdateTable(itemCount, -1);

#if !CART_EXTRACTOR_TEST_SITE
			if (_rules.TrackShopperActivity) //report customer data to SA service
			{
				_progress.UpdateTable(-1, -1, "Reporting to Shopper Activity Service");
				DataLogProxy.Instance.ReportCustomerInfo(_rules.Alias, data);
			}
#endif
			_progress.UpdateTable(-1, -1, "Writing table");
			var filename = String.Format(CartExtractor.CustomerFilenameFormat, _exportDate.ToString("yyyy-MM"));
			return TableAccess.Instance.WriteTable(_rules.Alias, filename, data);
		}
		
	}
}