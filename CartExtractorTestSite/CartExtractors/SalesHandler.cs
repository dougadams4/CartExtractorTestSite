using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Xml.Linq;
using _4_Tell.CommonTools;
using _4_Tell.IO;

namespace _4_Tell.CartExtractors
{
	public class SalesHandler : DataHandlerBase
	{

		public SalesHandler(SiteRules rules, CartExtractor cart, ExtractorProgress progress)
			: base(rules, cart, progress, DataGroup.Sales)
		{
		}

		public override string WriteTable(out int itemCount)
		{
			itemCount = -1;
			if (!_exportDateInitialized)
				throw new Exception("Sales export date was not initialized.");				
			if (Rows.Count < 1)
				return "no data";

			_progress.StartTask("Parsing data", "items", null, Rows.Count);

			var iPId = _rules.Fields.GetHeaderIndex(FieldName.OrderProductId); 
			var iCId = _rules.Fields.GetHeaderIndex(FieldName.OrderCustomerId);
			var iQuan = _rules.Fields.GetHeaderIndex(FieldName.OrderQuantity); 
			var iDate = _rules.Fields.GetHeaderIndex(FieldName.OrderDate); 
			var indexes = new[] { iPId, iCId, iQuan, iDate };
			if (indexes.Min<int>() < 0)
				return string.Format("bad header: {0}", Header.Aggregate((w, j) => string.Format("{0},{1}", w, j)));
			var minCols = indexes.Max<int>();

			var sales = new List<SalesRecord>();
			try
			{
				sales.AddRange(from cols in Rows
											 where (cols.Count > 0 && cols.Count > minCols)
												 select new SalesRecord
												 {
													 ProductId = cols[iPId],
													 CustomerId = cols[iCId],
													 Quantity = cols[iQuan],
													 Date = cols[iDate],
												 });
				DateTime testDate;
				sales = sales.Where(s => !Input.TryGetDate(out testDate, s.Date, _rules.OrderDateReversed)
											|| (testDate.Year.Equals(_exportDate.Year) && testDate.Month.Equals(_exportDate.Month)))
											.ToList();
			}
			catch (Exception ex)
			{
				return ex.Message;
			}
			if (sales.Count < 1)
				return "no data";

			//Migration slaves need to map each product id in sales to its replacement id
			_cart.MigrateSlaveOrders(ref sales);

			itemCount = sales.Count;
			var filename = String.Format(CartExtractor.SalesFilenameFormat, _exportDate.ToString("yyyy-MM"));
			_progress.UpdateTable(itemCount, -1, "Writing table");
			return TableAccess.Instance.WriteTable(_rules.Alias, filename, sales);
		}
		
	}
}