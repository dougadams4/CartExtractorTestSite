using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Timers;
using System.Xml.Linq;
using _4_Tell.CommonTools;
using _4_Tell.IO;
using _4_Tell.Logs;

namespace _4_Tell.CartExtractors
{
	public sealed class TestExtractor : CartExtractor
	{
		public TestExtractor(SiteRules rules)
			: base(rules)
		{
			//determine feed type
			SetFeedTypes();

			//if api key is not provided, assume license key instead
			if (string.IsNullOrEmpty(Rules.ApiKey))
			{
				//default to service key based on license
				Rules.ApiKey = ClientData.Instance.GetServiceKey(Alias);
			}
		}

		#region Overrides of CartExtractor

		public override bool ValidateCredentials(out string status)
		{
			throw new NotImplementedException();
		}


		protected override void ReleaseCartData()
		{
			//no data to release
		}

		public override void LogSalesOrder(string orderID)
		{
		}

		protected override string GetInventory(out int itemCount)
		{
			itemCount = 100;
			return "complete";
		}

		protected override string GetSalesMonth(DateTime exportDate, string filename, out int itemCount)
		{
			itemCount = 100;
			return "complete";
		}

		protected override string GetCustomers(DateTime exportDate, string filename, out int itemCount)
		{
			itemCount = 100;
			return "complete";
		}

		protected override string GetAtt1Names(out int itemCount)
		{
			if (!Rules.Fields.Att1Enabled || Rules.UseDepartmentsAsCategories)
			{
				itemCount = -1;
				return "rule is turned off";
			}
			itemCount = 100;
			return "complete";
		}

		protected override string GetAtt2Names(out int itemCount)
		{
			itemCount = -1;
			if (!Rules.Fields.Att2Enabled)
				return "rule is turned off";
			if (!Rules.ExtractAtt2Names)
				return "export is not required";
			itemCount = 100;
			return "complete";
		}

		/// <summary>
		/// override to get department names
		/// currently this is only used in ADNSF sites
		/// </summary>
		/// <param name="rowCount"></param>
		/// <returns></returns>
		protected override string GetDepartmentNames(out int itemCount)
		{
			itemCount = -1;
			if (!Rules.ExportDepartmentNames && !Rules.UseDepartmentsAsCategories)
				return "export is not required";
			itemCount = 100;
			return "complete";
		}

		protected override string GetCatalog(out int itemCount)
		{
			itemCount = 100;
			return "complete";
		}

		#endregion

	}

}

//END namespace