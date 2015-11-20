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
	public sealed class XmlFeedExtractor : CartExtractor
	{
		private static Timer _cleanupTimer;

		#region FileReaders
		//private IEnumerable<SalesRecord> m_orderHistory;

		//private IEnumerable<SalesRecord> OrderHistory
		//{
		//  get
		//  {
		//    if (m_orderHistory == null)
		//    {
		//      m_orderHistory = LoadTabDelimitedFile(DataPath + OrderDetailsFileName)
		//        .Select(order => new SalesRecord
		//                          {
		//                            OrderId = order["ORDER_ID"],
		//                            ProductId = order["PROD_CODE"],
		//                            CustomerId = GetHash(order["BILL_EMAIL"]),
		//                            Quantity = order["PROD_QUANT"],
		//                            Date = order["ORDER_DATE"]
		//                          }).ToList();
		//    }
		//    return m_orderHistory;
		//  }
		//}

		//private readonly IEnumerable<Dictionary<string, string>> m_categoryDictionary;
		//public IEnumerable<Dictionary<string, string>> Catalog
		//{
		//  get
		//  {
		//    //If no live catalog avaialable, look for a catalog.xml file instead
		//    //WARNING: Delete catalog.xml once live catalog is available so connection issue doesn't cause a revert to old data
		//    if (m_catalogDictionary == null)
		//      m_catalogDictionary = LoadTabDelimitedFile(m_productsFilePath);
		//    if (m_categoryDictionary == null)
		//      m_categoryDictionary = LoadTabDelimitedFile(m_categoriesFilePath);

		//    return m_catalogDictionary;
		//  }
		//  set { m_catalogDictionary = value; }
		//}

		//private XElement m_catalogXml = null;

		//public XElement Catalog
		//{
		//  get
		//  {
		//    var progress = string.Empty;
		//    m_catalogXml = json.RequestFromService(MivaMerchantJsonBridge.DataGroup.Catalog, ref progress);

		//    //If no live catalog avaialable, look for a catalog.xml file instead
		//    //WARNING: Delete catalog.xml once live catalog is available so connection issue doesn't cause a revert to old data

		//    //if (m_catalogXml == null)
		//    //    m_catalogXml = XElement.Load(DataPath + CatalogExportFileName);
		//    ////if (m_categoryXml == null)
		//    ////  m_categoryXml = LoadXmlFile(DataPath + CategoriesFileName);
		//    //if (m_categoryDictionary == null)
		//    //    m_categoryDictionary = LoadTabDelimitedFile(m_categoriesFilePath);

		//    return m_catalogXml;
		//  }
		//  set { m_catalogXml = value; }
		//}
		#endregion

		public XmlFeedExtractor(SiteRules rules)
			: base(rules)
		{
			//determine feed type
			SetFeedTypes();

			//if api key is not provided, assume license key instead
			if (string.IsNullOrEmpty(Rules.ApiKey))
			{
				Rules.ApiKey = ClientData.Instance.GetServiceKey(Alias);
			}
		}

		#region Overrides of CartExtractor

		public override bool ValidateCredentials(out string status)
		{
			throw new NotImplementedException();
		}

		//protected override void FillDefaultFieldNames()
		//{
		//  //cannot have spaces in the names
		//  //set defaults for fields names not set in client details
		//  Rules.SetDefaultField(SiteRules.FieldName.ProductGroupId, "Product");
		//  Rules.SetDefaultField(SiteRules.FieldName.ProductId, "ProductID");
		//  Rules.SetDefaultField(SiteRules.FieldName.Name, "Name");
		//  Rules.SetDefaultField(SiteRules.FieldName.Att1Id, "CategoryIDs");
		//  Rules.SetDefaultField(SiteRules.FieldName.Att2Id, "BrandID");
		//  Rules.SetDefaultField(SiteRules.FieldName.Price, "Price");
		//  Rules.SetDefaultField(SiteRules.FieldName.SalePrice, "SalePrice");
		//  Rules.SetDefaultField(SiteRules.FieldName.ListPrice, "ListPrice");
		//  Rules.SetDefaultField(SiteRules.FieldName.Cost, "Cost");
		//  Rules.SetDefaultField(SiteRules.FieldName.OnSale, "OnSale");
		//  Rules.SetDefaultField(SiteRules.FieldName.Rating, "Rating");
		//  Rules.SetDefaultField(SiteRules.FieldName.StandardCode, "StandardCode");
		//  Rules.SetDefaultField(SiteRules.FieldName.Link, "Link");
		//  Rules.SetDefaultField(SiteRules.FieldName.ImageLink, "ImageLink");
		//  Rules.SetDefaultField(SiteRules.FieldName.Visible, "Visible");
		//  Rules.SetDefaultField(SiteRules.FieldName.Inventory, "Inventory");
		//  Rules.SetDefaultField(SiteRules.FieldName.OrderGroupId, "Order");
		//  Rules.SetDefaultField(SiteRules.FieldName.OrderId, "Id");
		//  Rules.SetDefaultField(SiteRules.FieldName.OrderProductId, "ProductId");
		//  Rules.SetDefaultField(SiteRules.FieldName.OrderCustomerId, "CustomerId");
		//  Rules.SetDefaultField(SiteRules.FieldName.OrderQuantity, "Quantity");
		//  Rules.SetDefaultField(SiteRules.FieldName.OrderDate, "Date");
		//  Rules.SetDefaultField(SiteRules.FieldName.Att1NameGroupId, "Category");
		//  Rules.SetDefaultField(SiteRules.FieldName.Att1NameId, "Id");
		//  Rules.SetDefaultField(SiteRules.FieldName.Att1NameName, "Name");
		//  Rules.SetDefaultField(SiteRules.FieldName.Att2NameGroupId, "Brand");
		//  Rules.SetDefaultField(SiteRules.FieldName.Att2NameId, "Id");
		//  Rules.SetDefaultField(SiteRules.FieldName.Att2NameName, "Name");
		//  Rules.SetDefaultField(SiteRules.FieldName.CustomerGroupId, "Customer");
		//  Rules.SetDefaultField(SiteRules.FieldName.CustomerId, "CustomerId");
		//}

		protected override void ReleaseCartData()
		{
			//Timer will get restarted after each extraction is complete. 
			//If no new extraction events occur before the timer fires then the feedclient will be deleted
			if (_feedClient != null)
			{
				_cleanupTimer = new Timer {Interval = 600 * 1000}; //10 min converted to ms
				_cleanupTimer.Elapsed += new ElapsedEventHandler(OnCleanupTimer);
				_cleanupTimer.Enabled = true;
			}
		}

		private void OnCleanupTimer(object sender, ElapsedEventArgs elapsedEventArgs)
		{
			_feedClient = null;
			_combinedFeed = null;
			_lastFeedTime = DateTime.MinValue;
		}

		protected override string GetInventory(out int itemCount)
		{
			throw new NotImplementedException();
		}

		public override void LogSalesOrder(string orderID)
		{
			throw new NotImplementedException();
		}

		protected override string GetSalesMonth(DateTime exportDate, string filename, out int rowCount)
		{
			Progress.UpdateTable(-1, -1, "Extracting");
			rowCount = 0;

			var sales = new List<SalesRecord>();
			try
			{
				XDocument xdoc;
				GetFeedData(out xdoc, DataGroup.Sales, exportDate);
				var tag = Rules.Fields.GetName(FieldName.OrderGroupId);
				if (string.IsNullOrEmpty(tag))
					return "no order group xml tag defined";
				XName name = tag;
				var data = xdoc.Descendants(name);
				if (!data.Any()) 
					return "no data";

				var oTag = Rules.Fields.GetName(FieldName.OrderId);
				if (string.IsNullOrEmpty(oTag))
					return "orderId xml tag is undefined";
				var cTag = Rules.Fields.GetName(FieldName.OrderCustomerId);
				if (string.IsNullOrEmpty(cTag))
					return "order-customerId xml tag is undefined";
				var dTag = Rules.Fields.GetName(FieldName.OrderDate);
				if (string.IsNullOrEmpty(dTag))
					return "order-date xml tag is undefined";
				var pTag = Rules.Fields.GetName(FieldName.OrderProductId);
				if (string.IsNullOrEmpty(pTag))
					return "order-productId xml tag is undefined";
				var qTag = Rules.Fields.GetName(FieldName.OrderQuantity);
				if (string.IsNullOrEmpty(qTag))
					return "order-quantity xml tag is undefined";
				var odTag = Rules.Fields.GetName(FieldName.OrderDetailsGroupId); //not required

				foreach (var order in data)
				{
					var oId = Input.GetValue(order, oTag);
					var cId = Input.GetValue(order, cTag);
					if (cId == "0") cId = String.Format("Ord{0}", oId);
					DateTime testDate;
					var date = Input.GetDateString(order, dTag, Rules.OrderDateReversed);
					if (DateTime.TryParse(date, out testDate)
							&& (!testDate.Year.Equals(exportDate.Year) || !testDate.Month.Equals(exportDate.Month)))
						continue;

					IEnumerable<XElement> details;
					if (!string.IsNullOrEmpty(odTag))
						details = order.Descendants(odTag);
					else
						details = new List<XElement> { order };

					foreach (var item in details)
					{
						var quantity = Input.GetValue(item, qTag);
						if (quantity.Equals("0")) continue;
						var pId = Input.GetValue(item, pTag);

						sales.Add(new SalesRecord
							{
								OrderId = oId,
								CustomerId = cId,
								Date = date,
								ProductId = pId,
								Quantity = quantity
							});
					}
				}
			}
			catch (Exception e)
			{
				Debug.WriteLine(e.Message);
			}
			if (sales.Count < 1)
				return "no data";

			//Migration slaves need to map each product id in sales to its replacement id
			MigrateSlaveOrders(ref sales);

			rowCount = sales.Count;
			Progress.UpdateTable(rowCount, -1, "Writing table");
			return TableAccess.Instance.WriteTable(Alias, filename, sales);
		}

		protected override string GetCustomers(DateTime exportDate, string filename, out int itemCount)
		{
			if (!Rules.ExtractCustomerData)
			{
				itemCount = -1;
				return "rule is turned off";
			}
			XDocument doc;
			GetFeedData(out doc, DataGroup.Customers, DateTime.Now);

			throw new NotImplementedException();
		}

		protected override string GetAtt1Names(out int rowCount)
		{
			if (!Rules.Fields.Att1Enabled)
			{
				rowCount = -1;
				return "rule is turned off";
			}
			var fields = new List<FieldName>
					{
						FieldName.Att1NameGroupId,
						FieldName.Att1NameId,
						FieldName.Att1NameName
					};
			return GetAttNames(DataGroup.CategoryNames, "Attribute1Names.txt", fields, out rowCount);
		}

		protected override string GetAtt2Names(out int rowCount)
		{
			if (!Rules.Fields.Att2Enabled)
			{
				rowCount = -1;
				return "rule is turned off";
			}
			var fields = new List<FieldName>
					{
						FieldName.Att2NameGroupId,
						FieldName.Att2NameId,
						FieldName.Att2NameName
					};
			return GetAttNames(DataGroup.ManufacturerNames, "Attribute2Names.txt", fields, out rowCount);
		}

		private string GetAttNames(DataGroup group, string filename, List<FieldName> fields, out int rowCount)
		{
			Progress.UpdateTable(-1, -1, "Extracting");

			var attributes = new List<AttributeRecord>();
			rowCount = 0;
			try
			{
				XDocument xdoc;
				GetFeedData(out xdoc, group, DateTime.Now);
				var tag = Rules.Fields.GetName(fields[0]);
				if (string.IsNullOrEmpty(tag))
					return "group xml tag is undefined";
				XName name = tag;
				var data = xdoc.Descendants(name);
				if (!data.Any()) 
					return "no data";

				var idTag = Rules.Fields.GetName(fields[1]);
				if (string.IsNullOrEmpty(idTag))
					return "id xml tag is undefined";
				var nameTag = Rules.Fields.GetName(fields[2]);
				if (string.IsNullOrEmpty(nameTag))
					return "name xml tag is undefined";

				attributes.AddRange(data.Select(order => new AttributeRecord
					{
						Id = Input.GetValue(order, idTag),
						Name = CleanUpTitle(Input.GetValue(order, nameTag)),
					}));
			}
			catch (Exception e)
			{
				Debug.WriteLine(e.Message);
			}
			rowCount = attributes.Count;
			if (rowCount < 1)
				return "no data";

			Progress.UpdateTable(rowCount, -1, "Writing table");
			return TableAccess.Instance.WriteTable(Alias, filename, attributes);
		}

		protected override string GetCatalog(out int itemCount)
		{
			//extra fields are any fields defined in ClientSettings fieldNames or rules that are not in the standard feed
			// check rules and combine with active fields
			var extraList = Rules.GetRuleFields().Union(Rules.Fields.GetActiveFields(DataGroup.Catalog))
													.Except(Rules.Fields.GetStandardFields(DataGroup.Catalog), StringComparer.OrdinalIgnoreCase).ToList();
			//then convert into a comma-separated query string
			var extraFields = string.Empty;
			if (extraList.Count > 0)
				extraFields = extraList.Aggregate((w, j) => string.Format("{0},{1}", w, j));

			Progress.StartTask("Retrieving data", "items");
			itemCount = 0;
			XDocument xdoc;
			GetFeedData(out xdoc, DataGroup.Catalog, DateTime.Now, 1, 0, extraFields);
			if (xdoc == null) 
				throw new Exception("no data");

			var tag = Rules.Fields.GetName(FieldName.ProductGroupId);
			if (string.IsNullOrEmpty(tag))
				throw new Exception("no product group xml tag defined");
			XName name = tag;
			var data = xdoc.Descendants(name);
			var rowCount = data.Count();
			if (rowCount < 1) 
				throw new Exception("no data");

			//capture tag names once to speed parsing
			var pTag = Rules.Fields.GetName(FieldName.ProductId);
			if (string.IsNullOrEmpty(pTag)) //product id is the only required field
				throw new Exception("order-productId xml tag is undefined");

			//set booleans to reduce lookups 
			var nTag = Rules.Fields.GetName(FieldName.Name);
			var bName = !string.IsNullOrEmpty(nTag);
			var a1Tag = Rules.Fields.GetName(FieldName.Att1Id);
			var bAtt1 = !string.IsNullOrEmpty(a1Tag);
			var a2Tag = Rules.Fields.GetName(FieldName.Att2Id);
			var bAtt2 = !string.IsNullOrEmpty(a2Tag);
			var fTag = Rules.Fields.GetName(FieldName.Filter);
			var bFilt = !string.IsNullOrEmpty(fTag);
			var prTag = Rules.Fields.GetName(FieldName.Price);
			var bPrice = !string.IsNullOrEmpty(prTag);
			var sprTag = Rules.Fields.GetName(FieldName.SalePrice);
			var bSale = !string.IsNullOrEmpty(sprTag);
			var lprTag = Rules.Fields.GetName(FieldName.ListPrice);
			var bList = !string.IsNullOrEmpty(lprTag);
			var cprTag = Rules.Fields.GetName(FieldName.Cost);
			var bCost = !string.IsNullOrEmpty(cprTag);
			var lTag = Rules.Fields.GetName(FieldName.Link);
			var bLink = !string.IsNullOrEmpty(lTag);
			var imTag = Rules.Fields.GetName(FieldName.ImageLink);
			var bImageLink = !string.IsNullOrEmpty(imTag);
			var rTag = Rules.Fields.GetName(FieldName.Rating);
			var bRating = !string.IsNullOrEmpty(rTag);
			var scTag = Rules.Fields.GetName(FieldName.StandardCode);
			var bCode = !string.IsNullOrEmpty(scTag);
			var vTag = Rules.Fields.GetName(FieldName.Visible);
			var bVisible = !string.IsNullOrEmpty(vTag);
			var inTag = Rules.Fields.GetName(FieldName.Inventory);
			var bInventory = !string.IsNullOrEmpty(inTag);

			Products = new List<ProductRecord>();
			//var errors = 0;
			try
			{
				//create product records
				foreach (var item in data)
				{
					var p = new ProductRecord
						{
							ProductId = Input.GetValue(item, pTag),
							Name = bName ? CleanUpTitle(Input.GetValue(item, nTag)) : "",
							Att1Id = bAtt1 && Rules.Fields.Att1Enabled ? Input.GetValue(item, a1Tag) : "",
							Att2Id = bAtt2 && Rules.Fields.Att2Enabled ? Input.GetValue(item, a2Tag) : "",
							Filter = bFilt ? Input.GetValue(item, fTag) : "",
							Price = bPrice ? Input.GetValue(item, prTag) : "",
							SalePrice = bSale ? Input.GetValue(item, sprTag) : "",
							ListPrice = bList ? Input.GetValue(item, lprTag) : "",
							Cost = bCost ? Input.GetValue(item, cprTag) : "",
							//TopPrice = bPriceT ? Input.GetValue(item, prtTag) : "",
							//TopSalePrice = bSaleT ? Input.GetValue(item, sprtTag) : "",
							//TopListPrice = bListT ? Input.GetValue(item, lprtTag) : "",
							//TopCost = bCostT ? Input.GetValue(item, cprtTag) : "",
							Link = bLink ? Input.GetValue(item, lTag) : "",
							ImageLink = bImageLink ? Input.GetValue(item, imTag) : "",
							Rating = bRating ? Input.GetValue(item, rTag) : "",
							StandardCode = bCode ? Input.GetValue(item, scTag) : "",
							Visible = bVisible ? Input.GetValue(item, vTag) : "",
							Inventory = bInventory ? Input.GetValue(item, inTag) : "",
						};

					if (Rules.MapCategoriesToFilters) p.Filter = Input.GetValue(item, a1Tag);

					//note: xml feed extractor does not yet support parent/child relationships so no ParentProducts will exist
					var productX = item; //must make copy to pass by ref
					ApplyAltPricesAndLinks(ref p, ref productX);

					ApplyRules(ref p, productX);
					Products.Add(p);
					Progress.UpdateTask(Products.Count); //, -1, null, string.Format("{0} errors", errors));
				}
			}
			catch (Exception e)
			{
				Debug.WriteLine(e.Message);
			}
			itemCount = Products.Count;
			Progress.EndTask(itemCount);

			if (_migrationSlave) return ""; //don't need to save catalog for migration slaves (MigrationMap created in AllpyRules)

			Progress.UpdateTable(itemCount, -1, "Writing table");
			return TableAccess.Instance.WriteTable(Alias, CatalogFilename, Products);
		}

		//public override List<string> GetAllQueryableFieldNames(DataGroup @group)
		//{
		//  var fields = Rules.GetActiveFields(group).Union(Rules.GetStandardFields(group)).ToList();
		//  return fields;
		//}

		#endregion



		#region Utilities

		//private static IEnumerable<Dictionary<string, string>> LoadTabDelimitedFile(string path)
		//{
		//  var contents = new List<Dictionary<string, string>>();
		//  var keys = new List<string>();

		//  using (var sr = new StreamReader(path))
		//  {
		//    string line;
		//    if (!string.IsNullOrEmpty(line = sr.ReadLine()))
		//      keys.AddRange(line.Split(new[] {'\t'}, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()));

		//    var keyCount = keys.Count();
		//    while (!string.IsNullOrEmpty(line = sr.ReadLine()))
		//    {
		//      var content = new Dictionary<string, string>();
		//      var values = line.Replace("<br>", "").Split(new[] {'\t'});

		//      var columnCount = values.Length;
		//      if (columnCount > keyCount)
		//      {
		//        //Extra tabs in the description column cause problems 
		//        var extraColumns = values.Length - keyCount;
		//        for (var i = 8; i < keyCount; i++) //descrptions are in column 7
		//          values[i] = values[i + extraColumns];
		//        columnCount = keyCount;
		//      }

		//      for (var i = 0; i < columnCount; i++)
		//      {
		//        content.Add(keys[i], values[i].Trim());
		//      }

		//      contents.Add(content);
		//    }
		//  }

		//  return contents;
		//}

		//private static IEnumerable<XElement> LoadXmlFile(string path)
		//{
		//  var catalogDoc = new XDocument();
		//  IEnumerable<XElement> resultXml = null;

		//  catalogDoc = XDocument.Load(path);
		//  resultXml = catalogDoc.Ancestors();

		//  return resultXml;
		//}


		#endregion
	}

}

//END namespace