using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Xml.Linq;
using _4_Tell.CommonTools;
using _4_Tell.IO;

namespace _4_Tell.CartExtractors
{
	public class InventoryHandler : DataHandlerBase
	{
		public InventoryHandler(SiteRules rules, CartExtractor cart, ExtractorProgress progress)
			: base(rules, cart, progress, DataGroup.Inventory)
		{
		}

		public override string WriteTable(out int itemCount)
		{
			itemCount = -1;

			var iPId = _rules.Fields.GetHeaderIndex(FieldName.InventoryProductId);
			var iQuan = _rules.Fields.GetHeaderIndex(FieldName.InventoryQuantity);
			var indexes = new[] { iPId, iQuan };
			if (indexes.Min<int>() < 0)
				return string.Format("(bad header: {0})", Header.Aggregate((w, j) => string.Format("{0},{1}", w, j)));
			var maxIndex = indexes.Max<int>();

			_progress.StartTask("Parsing data", "items", null, Rows.Count);
			var data = new List<InventoryRecord>();
			try
			{
				data.AddRange(from cols in Rows
												where cols.Count() > maxIndex
												select new InventoryRecord(cols[iPId], cols[iQuan]));
			}
			catch (Exception ex)
			{
				return ex.Message;
			}
			_progress.EndTask(-1, "completed");
			if (data.Count < 1)
				return "no data";

			itemCount = data.Count;
			_progress.UpdateTable(itemCount, -1, "Writing table");
			var status = TableAccess.Instance.WriteTable(_rules.Alias, CartExtractor.InventoryFilename, data);
			_progress.UpdateTable(itemCount, -1, status);

			//Now the real work begins --convert new inventory data to DynamicUpdate Exclusions
			ProcessInventory(data);

			return status;
		}

		protected void ProcessInventory(List<InventoryRecord> data)
		{
			while (true) //single-pass loop to allow breaks
			{
				//Review exclusion rules to see if inventory is considered
				_progress.StartTask("Identifying inventory rules", "rules");
				if (_rules.ExclusionRules == null || !_rules.ExclusionRules.Any()) break;
				var inventoryField = _rules.Fields.GetName(FieldName.Inventory);
				var rules = _rules.ExclusionRules.Where(c => c.QueryField.Equals(inventoryField));
				var exSetFields = _rules.ExclusionSet == null ? null : _rules.ExclusionSet.GetFields().Where(x => x.Equals(inventoryField)); ;
				_progress.EndTask(rules.Count(), "completed");
				if (!rules.Any() && (exSetFields == null || !exSetFields.Any())) break;

				//read current exclusion file and remove items that are already excluded
				_progress.StartTask("Reading existing exclusions", "items");
				//more efficient here to just get the raw list instead of the List<ExclusionRecord>
				List<string> oldExclusions;
				var oldList = TableAccess.Instance.ReadTable(CartExtractor.ExclusionFilename, _rules.Alias, 1, 1, 2);
				if (oldList == null || oldList.Count < 2)
					oldExclusions = new List<string>();
				else
				{
					oldList.RemoveAt(0); //remove header
					oldExclusions = oldList.Select(x => x[0]).ToList();
				}
				_progress.EndTask(oldExclusions.Count, "completed");

				//read the current replacements file and map all child inventory to parents
				_progress.StartTask("Finding active parents", "parents");
				List<InventoryRecord> parentData;
				//more efficient here to just get the raw list instead of the List<ReplacementRecord>
				var replacements = TableAccess.Instance.ReadTable(CartExtractor.ReplacementFilename, _rules.Alias, 2, 2, 2);
				if (replacements == null || replacements.Count < 2)
					//no children so just remove old exclusions from the list
					parentData = data.Where(d => !oldExclusions.Contains(d.ProductId)).ToList();
				else
				{
					replacements.RemoveAt(0); //remove header

					//create parent lookup dictionary
					var parentLookup = new Dictionary<string, List<string>>();
					foreach (var r in replacements)
					{
						List<string> kids;
						if (parentLookup.TryGetValue(r[1], out kids) && !kids.Contains(r[0]))
						{
							kids.Add(r[0]);
							parentLookup[r[1]] = kids;
						}
						else
							parentLookup.Add(r[1], new List<string> { r[0] });
					}
					_progress.UpdateTask(0, parentLookup.Count);
					var childIds = replacements.Select(x => x[0]).ToList();

					//reduce inventory data into active parents and possible children (Linq version was MUCH slower)
					parentData = new List<InventoryRecord>();
					var childData = new List<InventoryRecord>();
					foreach (var d in data)
					{
						if (childIds.Contains(d.ProductId))
							childData.Add(d);
						else if (!oldExclusions.Contains(d.ProductId))
							parentData.Add(d);
					}

					//now sum up child inventories and add to parents
					for (var i = 0; i < parentData.Count; i++)
					{
						_progress.UpdateTask(i, parentData.Count);
						List<string> kids;
						if (parentLookup.TryGetValue(parentData[i].ProductId, out kids))
						{
							foreach (var k in kids)
							{
								var index = childData.FindIndex(x => x.ProductId.Equals(k));
								if (index < 0 || childData[index].Quantity < 1) continue; //ignore negative values
								parentData[i].Quantity += childData[index].Quantity;
							}
						}
					}
				}
				_progress.EndTask(parentData.Count, "completed");

				//now find items that match the exclusion rule
				_progress.StartTask("Applying exclusion rules", "items");
				var newExclusions = new List<string>();
				newExclusions.AddRange(from e in parentData where rules.Any(c => c.Compare(e.Quantity)) select e.ProductId);
				_progress.EndTask(newExclusions.Count, "completed");

				//Now save the new exclusion list to the BoostSite's DynamicUpdate object
				if (newExclusions.Any())
				{
					_progress.StartTask("Saving new exclusions", "items");
					var count = ClientData.Instance.SetExclusions(_rules.Alias, newExclusions);
					_progress.EndTask(count, "completed");
				}

				//read catalog file and update inventories
				_progress.StartTask("Updating catalog inventories", "items");
				List<ProductRecord> catalog;
				if (!TableAccess.Instance.ReadTable(CartExtractor.CatalogFilename, _rules.Alias, out catalog))
					throw new Exception("Catalog does not exist.");
				var changed = false;
				foreach (var p in catalog)
				{
					var index = data.FindIndex(x => x.ProductId.Equals(p.ProductId));
					if (index < 0) continue;
					var newInventory = data[index].Quantity.ToString("N0");
					if (!p.Inventory.Equals(newInventory))
					{
						p.Inventory = newInventory;
						changed = true;
					}
				}
				var status = "completed";
				if (changed)
					status = TableAccess.Instance.WriteTable(_rules.Alias, CartExtractor.CatalogFilename, catalog);
				_progress.EndTask(catalog.Count, status);
				break;
			}


		}

	}
}