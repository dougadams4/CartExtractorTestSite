using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using _4_Tell.CommonTools;
using _4_Tell.Logs;
using _4_Tell.IO;

namespace _4_Tell.CartExtractors
{
	public class CatalogMigration
	{
		public const int MigrationCutoffDay = 20; //portion of the month where new data will trump old data
		public const string MigrationFullMapFilename = "MigrationFullMap.txt";  //OldId<tab>NewId<cr-lf>
		public const string MigrationMasterMapFilename = "MigrationMasterMap.txt";  //ProductId<tab>MapFieldValue<cr-lf>
		public const string MigrationSlaveMapFilename = "MigrationSlaveMap.txt";  //ProductId<tab>MapFieldValue<cr-lf>

		public string Alias { get; private set; }
		public bool Enabled { get; private set; }
		public bool Use4TellCatalog { get; private set; }
		public bool Use4TellSales { get; private set; }
		public SiteRules OldRules { get; private set; }
		public string MigrationAlias { get; set; }
		public string MapFromField { get; private set; }
		public string MapToField { get; private set; }
		public DateTime StartDate { get; private set; }
		public bool IsMigrationMaster { get; private set; }
		public int Tier { get; private set; }
		public int ValidMonths { get; private set; }
		public Dictionary<string, string> MigrationSubMap = null;	//each master and slave will fill the sub map
		public Dictionary<string, string> MigrationFullMap = null; //only the slave will fill the full map so it can be used to map slave sales data
		public string MapStatus
		{
			get { return (MigrationSubMap != null && MigrationSubMap.Any()) ? "Migration Map Created" : "Migration Map is empty"; }
		}
		public ExtractorProgress Progress; 
		protected CartExtractor _migrationCart = null;


		#region Initialize
		public CatalogMigration(CatalogMigration m, bool isMaster = true)
		{
			SetParams(m.Alias, m.Tier, m.StartDate, m.MapFromField, m.MapToField, m.Enabled, 
												isMaster, m.ValidMonths, m.Use4TellCatalog, m.Use4TellSales);
		}

		public CatalogMigration(string alias, int tier, DateTime startDate, string fromField, string toField,  bool enabled, 
														bool isMaster, int validMonths, bool use4TellCatalog = false, bool use4TellSales = false)
		{
			SetParams(alias, tier, startDate, fromField, toField, enabled, isMaster, validMonths, use4TellCatalog, use4TellSales);
		}
	
		private void SetParams(string alias, int tier, DateTime startDate, string fromField, string toField, bool enabled, 
											bool isMaster, int validMonths, bool use4TellCatalog = false, bool use4TellSales = false)
		{
			Alias = alias;
			Tier = tier;
			StartDate = startDate;
			MapFromField = fromField;
			MapToField = toField;
			IsMigrationMaster = isMaster;
			Enabled = enabled;
			ValidMonths = validMonths;
			Use4TellCatalog = use4TellCatalog;
			Use4TellSales = use4TellSales;

			if (!enabled)
			{
				OldRules = null;
				return;
			}
			if (OldRules == null && ClientData.Instance != null) //null during service initialization
			{
				InitRules(alias, tier, validMonths);
			}
		}

		public void InitRules(string alias, int tier, int validMonths)
		{
			//setup rules
			XElement oldRulesXml = null;
			if (!Use4TellCatalog || !Use4TellSales)
				oldRulesXml = ClientData.Instance.ReadSiteRules(alias, null, "Migration");
			InitRules(oldRulesXml, alias, tier, validMonths);
		}

		public void InitRules(XElement oldRulesXml, string alias, int tier, int validMonths)
		{
			//setup rules
			if (oldRulesXml != null)
			{
				MigrationAlias = Input.GetValue(oldRulesXml, "alias");
				OldRules = new SiteRules(alias, tier, oldRulesXml);
			}
			else if (Use4TellCatalog && Use4TellSales) //create generic TabbedFeed rules to read 4-Tell files
			{
				MigrationAlias = alias;
				OldRules = new SiteRules(alias, "", (BoostTier)tier, CartType.TabbedFeed, "", false);
				OldRules.ApiExtraHeaders = 1;
				OldRules.CatalogFeedUrl = "file:Catalog.txt";
				var cartRules = OldRules.ReadCartRules(CartType.TabbedFeed);
				OldRules.Fields.InitializeFields(cartRules, true);
			}
			else
			{
				Enabled = false;
				return;
			}
			CheckDate(validMonths);
		}

		public void CheckDate(int validMonths) //only keep active while new sales data is incomplete
		{
			if (StartDate.AddMonths(validMonths) < DateTime.Now)	
				Enabled = false;
		}
		#endregion

		public void BeginMapping(ExtractorProgress progress)
		{
			Progress = progress;
			if (Enabled)
				MigrationSubMap = new Dictionary<string, string>();
		}

		public void ProcessMap()
		{
			if (!Enabled || !IsMigrationMaster) return;

			_migrationCart = CartExtractor.GetCart(OldRules);
			if (_migrationCart == null)
			{
				if (BoostLog.Instance != null)
					BoostLog.Instance.WriteEntry(EventLogEntryType.Error, "Unable to create migration cart", "", Alias);
				return;
			}
			_migrationCart.SetMigrationSlave(this);
			Progress.SetMigrationProgress(_migrationCart.Progress);
			Progress.IsMigrating = true;

			//if there is a ToField then Master Map was created when master catalog was pulled
			if (!string.IsNullOrEmpty(MapToField))
			{
				Progress.StartTable("Master Migration Map", "items");
				if (MigrationSubMap == null || !MigrationSubMap.Any())
					Progress.EndTable(0, "none");
				else
					Progress.EndTable(MigrationSubMap.Count, "completed");
			}

			//If there is a FromField then need to pull the Slave Catalog to get the Slave Map
			if (!string.IsNullOrEmpty(MapFromField))
			{
				Progress.StartTable("Slave Migration Map", "items");
				_migrationCart.GetData(CartExtractor.ExtractType.Catalog);
				var slaveMap = _migrationCart.Rules.MigrationRules.MigrationSubMap;
				if (slaveMap == null || !slaveMap.Any())
					Progress.EndTable(0, "none");
				else
					Progress.EndTable(slaveMap.Count, "completed");
			}

			//Replacements are created by combining master and slave maps or by using an optional direct map file
			Progress.StartTable("Full Migration Map", "items");
			int itemCount;
			var status = CreateFullMap(out itemCount);
			Progress.EndTable(itemCount, status);

			//Now get sales data 
			// --dates will be limited by the migration start date 
			// --sales data product ids will be replaced automatically
			Progress.StartTable("Migration Sales", "records");
			if (Use4TellSales)
				GetAllSales(ref itemCount);
			else
			{
				_migrationCart.GetData(CartExtractor.ExtractType.Sales);
				itemCount = _migrationCart.TotalSales;
			}
			Progress.EndTable(itemCount, "Complete");

			//Now get clickstream data 
			// --dates will be limited by the migration start date 
			// --clickstream data product ids will be replaced using the migration map
			Progress.StartTable("Migration ClickStream", "records");
			GetAllClickStream(ref itemCount);
			Progress.EndTable(itemCount, "Complete");

			//TODO: can't really migrate customers unless we are going to use an alternate id based on address or creditcard, etc
			//if (_migrationCart.Rules.ExtractCustomerData)
			//{
			//  Progress.StartTable("Migration Customers", "records");
			//  _migrationCart.GetData(CartExtractor.ExtractType.Customers);
			//  itemCount = _migrationCart.TotalCustomers;
			//  Progress.EndTable(itemCount, "Complete");
			//}

			Progress.IsMigrating = false;
		}

		public void GetAllSales(ref int totalSales)
		{
			totalSales = 0;
			var firstDate = DateTime.Now.AddMonths(-1 * _migrationCart.Rules.SalesMonthsToExport);
			var lastDate = StartDate; //migration start date
			var exportDate = new DateTime(firstDate.Year, firstDate.Month, 1);
			while (exportDate < lastDate)
			{
				//don't replace final month if new data is more relevant
				if (lastDate.Day < MigrationCutoffDay
						&& exportDate.Year == lastDate.Year && exportDate.Month == lastDate.Month) break;

				//read sales data for this month (if it exists)
				var filename = String.Format(CartExtractor.SalesFilenameFormat, exportDate.ToString("yyyy-MM"));
				totalSales += ProcessSalesFile(filename);

				//read actions data for this month (if it exists)
				filename = String.Format(CartExtractor.ActionsFilenameFormat, exportDate.ToString("yyyy-MM"));
				totalSales += ProcessSalesFile(filename);

				//move forward one month for next loop
				exportDate = exportDate.AddMonths(1);
			}
		}

		private int ProcessSalesFile(string filename)
		{
			List<SalesRecord> sales;
			if (!TableAccess.Instance.ReadTable(filename, Alias, out sales, true) || !sales.Any())
				return 0;

			Progress.StartTask("Migration Sales " + filename, "items");
			MigrateSlaveOrders(ref sales);
			var status = "";
			if (sales.Any())
			{
				Progress.UpdateTask(sales.Count, -1, "Writing table");
				status = TableAccess.Instance.WriteTable(Alias, filename, sales);
			}
			Progress.EndTask(sales.Count, status);
			return sales.Count;
		}

		/// <summary>
		/// Read old ClickStream files and map them to the new catalog
		/// </summary>
		/// <param name="itemCount"></param>
		public void GetAllClickStream(ref int totalClicks)
		{
			totalClicks = 0;
			var now = DateTime.Now;
			var sundayOffset = (7 - (int)(now.DayOfWeek)) % 7; //Monday offset = 6, Tuesday = 5, etc.
			var firstDate = now.AddDays(sundayOffset  - (7 * _migrationCart.Rules.ClickStreamWeeksToExport));
			var lastDate = StartDate; //migration start date
			var exportDate = firstDate;
			while (exportDate < lastDate)
			{
				//read click stream data for this week (if it exists)
				var filename = String.Format(CartExtractor.ClickStreamFilenameFormat, exportDate.ToString("yyyy-MM-dd"));
				List<ClickRecord> clicks = null;
				if (TableAccess.Instance.ReadTable(filename, Alias, out clicks, true))
				{
					Progress.StartTask("Migration ClickStream " + filename, "items");
					MigrateSlaveClickStream(ref clicks);
					var status = "";
					if (clicks.Any())
					{
						totalClicks += clicks.Count;
						Progress.UpdateTask(clicks.Count, -1, "Writing table");
						status = TableAccess.Instance.WriteTable(Alias, filename, clicks);
					}
					Progress.EndTask(clicks == null? 0 : clicks.Count, status);
				}

				//move forward one week for next loop
				exportDate = exportDate.AddDays(7);
			}
		}

		public void MigrateSlaveOrders(ref List<SalesRecord> orders)
		{
			if (!Enabled || MigrationFullMap == null || !MigrationFullMap.Any())
				return;

			var newOrders = new List<SalesRecord>();
			foreach (SalesRecord o in orders)
			{
				string newId;
				if (!MigrationFullMap.TryGetValue(o.ProductId, out newId)) continue;

				newOrders.Add(new SalesRecord
				{
					ProductId = newId,
					OrderId = o.OrderId,
					CustomerId = o.CustomerId,
					Quantity = o.Quantity,
					Date = o.Date,
					Key = o.Key
				});
			}
#if DEBUG
			if (BoostLog.Instance != null)
				BoostLog.Instance.WriteEntry(EventLogEntryType.Information, string.Format("Migrated {0} orders of {1}", newOrders.Count, orders.Count), "", Alias);
#endif
			orders = newOrders;
		}
		
		public void MigrateSlaveClickStream(ref List<ClickRecord> clicks)
		{
			if (!Enabled || MigrationFullMap == null || !MigrationFullMap.Any())
				return;

			var newClicks = new List<ClickRecord>();
			foreach (ClickRecord c in clicks)
			{
				string newId;
				if (!MigrationFullMap.TryGetValue(c.ProductId, out newId)) continue;

				newClicks.Add(new ClickRecord
				{
					ProductId = newId,
					CustomerId = c.CustomerId,
					PageType = c.PageType,
					Date = c.Date,
				});
			}
#if DEBUG
			if (BoostLog.Instance != null)
				BoostLog.Instance.WriteEntry(EventLogEntryType.Information, string.Format("Migrated {0} clicks of {1}", newClicks.Count, clicks.Count), "", Alias);
#endif
			clicks = newClicks;
		}

		/// <summary>
		/// Create a mapping for a single item in the SubMap for either the slave or master cart. 
		/// This is called by the GetCatalog method in each cart implementation
		/// This overloaded version takes column data and converts it to an xml element
		/// </summary>
		/// <param name="productId"></param>
		/// <param name="header"></param>
		/// <param name="columns"></param>
		public void MapItem(string productId, List<string> header, List<string> columns)
		{
			if (MigrationSubMap == null || !Enabled) return;

			//create an XElement 
			var productX = new XElement("product");
			for (var j = 0; j < header.Count && j < columns.Count; j++)
				productX.Add(new XElement(header[j], columns[j]));

			MapItem(productId, productX);
		}

		/// <summary>
		/// Create a mapping for a single item in the SubMap for either the slave or master cart. 
		/// This is called by the GetCatalog method in each cart implementation
		/// </summary>
		/// <param name="productId"></param>
		/// <param name="header"></param>
		/// <param name="columns"></param>
		public void MapItem(string productId, XElement product)
		{
			if (MigrationSubMap == null || !Enabled) return;

			var field = IsMigrationMaster ? MapToField : MapFromField;
			if (string.IsNullOrEmpty(field)) return;
			field = Input.RemoveTablePrefix(field);
			var key = Input.GetValue(product, field);
			string value;
			if (string.IsNullOrEmpty(key)
				|| (!IsMigrationMaster && MigrationSubMap.TryGetValue(productId, out value))
				|| (IsMigrationMaster && MigrationSubMap.TryGetValue(key, out value)))
				return;

			//mapping goes from slave-id to slave-key to master-key to master-id
			if (IsMigrationMaster) MigrationSubMap.Add(key, productId);
			else MigrationSubMap.Add(productId, key);
		}

		/// <summary>
		/// Create the Full Map to convert old sales data to the new catalog. 
		/// If the old catalog has child items, each child id needs to be mapped to the new parent. 
		/// </summary>
		/// <param name="itemCount"></param>
		/// <returns></returns>
		private string CreateFullMap(out int itemCount)
		{
			//Logic: 
			//	MigrationFullMap is the master map. This maps ids from the slave product ids to the master product ids
			//  This map is only created on the slave cart so that it can be used to migrate the slave's sales data.
			//	There are two MigrationSubMaps, one for each cart
			//  The slave submap maps ids from the slave product id to the MapToField
			//  The master submap maps ids from the MapFromField to the master product id 
			//  Manually-generated files can all be supplied to replace, override, or add to the auto-generated maps
			itemCount = -1;
			if (_migrationCart == null || _migrationCart.Rules == null || _migrationCart.Rules.MigrationRules == null)
				return "Error: Migration cart is not initialized";

			var slave = _migrationCart.Rules.MigrationRules; //get local reference for clarity
			try
			{
				var debugTxt = new StringBuilder();
				//full map is stored on the slave cart so it can be used to map the slave sales data
				slave.MigrationFullMap = new Dictionary<string, string>();
				if (MigrationSubMap == null) MigrationSubMap = new Dictionary<string, string>();
				if (slave.MigrationSubMap == null) slave.MigrationSubMap = new Dictionary<string, string>();

				//First: look for a manually-generated MigrationFullMap.txt file to add directly to the parent id map
				//	this skips the MapFromField and MapToField and goes straight from old id to new id
				var tempCount = 0;
				var manReplacements = TableAccess.Instance.ReadTable(MigrationFullMapFilename, Alias, 2, 2, 2);
				if (manReplacements != null && manReplacements.Count > 1)
				{
					debugTxt.Append("DirectMap");
					var header = manReplacements[0].ToList();
					var desiredHeader = ReplacementRecord.Headers;
					var iOldId = Input.GetHeaderPosition(header, desiredHeader[0]);
					var iNewId = Input.GetHeaderPosition(header, desiredHeader[1]);
					if (iOldId > -1 && iNewId > -1)
					{
						var minLength = (int)Math.Max(iOldId, iNewId) + 1;
						manReplacements.RemoveAt(0); //remove header
						foreach (var r in manReplacements)
						{
							if (r.Length < minLength) continue;
							string value;
							if (!slave.MigrationFullMap.TryGetValue(r[iOldId], out value))
							{
								slave.MigrationFullMap.Add(r[iOldId], r[iNewId]);
								Progress.UpdateTable(-1, ++tempCount);
								continue;
							}
							if (value.Equals(r[iNewId])) continue;
							debugTxt.Append(string.Format("\nMapping from {0} to {1} prevented mapping to {2}", r[iOldId], value, r[iNewId]));
						}
					}
					debugTxt.Append("\n\n");
				}

				//Second: look for a manually-generated MigrationMasterMap.txt and MigrationSlaveMap.txt files 
				//				mapping goes from slave-id to slave-field to master-field to master-id
				manReplacements = TableAccess.Instance.ReadTable(MigrationSlaveMapFilename, Alias, 2, 2, 2);
				if (manReplacements != null && manReplacements.Count > 1)
				{
					debugTxt.Append("SlaveMap");
					var header = manReplacements[0].ToList();
					var desiredHeader = MigrationMapRecord.Headers;
					var iPId = Input.GetHeaderPosition(header, desiredHeader[0]);
					var iFieldVal = Input.GetHeaderPosition(header, desiredHeader[1]);
					manReplacements.RemoveAt(0); //remove header
					foreach (var r in manReplacements)
					{
						string value;
						if (!slave.MigrationSubMap.TryGetValue(r[iPId], out value))
						{
							slave.MigrationSubMap.Add(r[iPId], r[iFieldVal]);
							continue;
						}
						if (value.Equals(r[iPId])) continue;
						debugTxt.Append(string.Format("\nMapping from {0} to {1} changed to {2}", r[iPId], value, r[iFieldVal]));
						slave.MigrationSubMap[r[iPId]] = r[iFieldVal]; //manual map trumps automated
					}
					debugTxt.Append("\n\n");
				}
				manReplacements = TableAccess.Instance.ReadTable(MigrationMasterMapFilename, Alias, 2, 2, 2);
				if (manReplacements != null && manReplacements.Count > 1)
				{
					debugTxt.Append("MasterMap");
					var header = manReplacements[0].ToList();
					var desiredHeader = MigrationMapRecord.Headers;
					var iPId = Input.GetHeaderPosition(header, desiredHeader[0]);
					var iFieldVal = Input.GetHeaderPosition(header, desiredHeader[1]);
					manReplacements.RemoveAt(0); //remove header
					foreach (var r in manReplacements)
					{
						string id;
						if (!MigrationSubMap.TryGetValue(r[iFieldVal], out id))
						{
							MigrationSubMap.Add(r[iFieldVal], r[iPId]);
							continue;
						}
						if (id.Equals(r[iPId])) continue;
						debugTxt.Append(string.Format("\nMapping from {0} to {1} changed to {2}", r[iFieldVal], id, r[iPId]));
						MigrationSubMap[r[iFieldVal]] = r[iPId]; //manual map trumps automated
					}
					debugTxt.Append("\n\n");
				}

				//Third: review the two migration maps to do the indirect mapping using the MapFromField and MapToField
				//			mapping goes from slave-id to slave-field to master-field to master-id
				debugTxt.Append("FullMap");
				var missingRep = new List<string>();
				foreach (var m in slave.MigrationSubMap)
				{
					string newId, testVal;
					if (!MigrationSubMap.TryGetValue(m.Value, out newId))
					{
						if (!missingRep.Contains(m.Value)) missingRep.Add(m.Value);
						continue;
					}
					if (slave.MigrationFullMap.TryGetValue(m.Key, out testVal))
					{
						debugTxt.Append(string.Format("\nMapping from {0} to {1} blocked new mapping to {2}", m.Key, testVal, newId));
						continue;
					}
					slave.MigrationFullMap.Add(m.Key, newId); //add new mapping
					Progress.UpdateTable(-1, ++tempCount);
				}
				//reference the full map from the master for use with clickstream mapping
				MigrationFullMap = slave.MigrationFullMap;

				debugTxt.Append("\n\n");
#if DEBUG
				//write out individual maps for debugging
				var map = slave.MigrationSubMap.Select(x => new List<string> { x.Key, x.Value }).ToList();
				map.Insert(0, MigrationMapRecord.Headers);
				TableAccess.Instance.WriteTable(Alias, "Debug_SlaveMap.txt", map);
				map = MigrationSubMap.Select(x => new List<string> { x.Value, x.Key }).ToList();
				map.Insert(0, MigrationMapRecord.Headers);
				TableAccess.Instance.WriteTable(Alias, "Debug_MasterMap.txt", map);
				map = slave.MigrationFullMap.Select(x => new List<string> { x.Key, x.Value }).ToList();
				map.Insert(0, ReplacementRecord.Headers);
				TableAccess.Instance.WriteTable(Alias, "Debug_FullMap.txt", map);
#endif
				if (!slave.MigrationFullMap.Any()) return "no mappings found";

				//Forth: add _migrationCart.Replacements (if any) to the new map
				if (_migrationCart.Replacements != null)
				{
					debugTxt.Append("Slave Replacements");
					foreach (var r in _migrationCart.Replacements)
					{
						string value;
						if (slave.MigrationFullMap.TryGetValue(r.OldId, out value))
						{
							debugTxt.Append(string.Format("\nMapping from {0} to {1} blocked new mapping using {2}", r.OldId, value, r.NewId));
							continue;
						}
						if (!slave.MigrationFullMap.TryGetValue(r.NewId, out value))
						{
							if (!missingRep.Contains(r.NewId)) missingRep.Add(r.NewId);
							continue;
						}
						slave.MigrationFullMap.Add(r.OldId, value); //add replacement child to full map
					}
					debugTxt.Append("\n\n");
				}

#if DEBUG
				if (missingRep.Any())
				{
					debugTxt.Append("The following slave products were not found in full map:\n\t");
					debugTxt.Append(missingRep.Aggregate((w, j) => String.Format("{0}\n\t{1}", w, j)));
				}
				//write out manual override notices and rewrite adjusted full map
				if (BoostLog.Instance != null)
					BoostLog.Instance.WriteEntry(EventLogEntryType.Information, debugTxt.ToString(), "", Alias);
				map = slave.MigrationFullMap.Select(x => new List<string> { x.Key, x.Value }).ToList();
				map.Insert(0, ReplacementRecord.Headers);
				TableAccess.Instance.WriteTable(Alias, "Debug_FullMap.txt", map);
#endif
				itemCount = slave.MigrationFullMap.Count;
				Progress.UpdateTable(-1, itemCount);
				return "completed";
			}
			catch (Exception ex)
			{
				return "Error: " + Input.GetExMessage(ex);
			}
		}

	}
}