using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Web;
using System.Xml.Linq;
using _4_Tell.CommonTools;
using _4_Tell.Logs;
using _4_Tell.IO;

namespace _4_Tell.CartExtractors
{
	public class CatalogHandler : DataHandlerBase
	{
		private Dictionary<string, ParentItem> _parentProducts;  //key is product id 

		/// <summary>
		/// ParentItem is used to map child price ranges to the parent 
		/// </summary>
		private class ParentItem
		{
			public string Id;
			public int Inventory;
			public float Price;
			public float SalePrice;
			public float ListPrice;
			public float Cost;
			public float TopPrice;
			public float TopSalePrice;
			public float TopListPrice;
			public float TopCost;
			public float TopRating;
			public float RatingSum;
			public int RatingCount;

			public ParentItem()
			{
				Id = null;
				Inventory = RatingCount = 0;
				Price = TopPrice = SalePrice = TopSalePrice = ListPrice = TopListPrice = Cost = TopCost = TopRating = RatingSum = 0F;
			}
		}

		/// <summary>
		/// ChildItem is a simple struct to hold child details to accumulate into ParentItem
		/// </summary>
		private struct ChildItem
		{
			public string Id;
			public int Inventory;
			public float Price;
			public float SalePrice;
			public float ListPrice;
			public float Cost;
			public float Rating;
		}


		public CatalogHandler(SiteRules rules, CartExtractor cart, ExtractorProgress progress)
			: base(rules, cart, progress, DataGroup.Catalog)
		{
		}

		public override void Reset()
		{
			base.Reset();
			_parentProducts = new Dictionary<string, ParentItem>();
		}

		/// <summary>
		/// Add a set of rows to the catalog
		/// Only parents will be added to the catalog (unless _rules force ), but critical child item data will be aggregated
		/// </summary>
		/// <param name="data"></param>
		/// <param name="dataIncludesHeader"></param>
		/// <returns></returns>
		public int AddData(List<List<string>> data, bool dataIncludesHeader)
		{
			if (Header == null && !dataIncludesHeader) 
				throw new Exception("Header has not beed defined");
			if (data == null || data.Count < 1)
				return Rows.Count;

			if (dataIncludesHeader)
			{
				Header = data[0].Select(x => x.Replace(" ", "")).ToList(); //cannot have spaces in header names
				data.RemoveAt(0);
				if (_rules.CartType.Equals(CartType.Magento)) //special handling for Magento plugin bug
					Header = Header.Distinct().ToList();
				_rules.Fields.SetFieldHeaderIndices(DataGroup.Catalog, Header);
			}
			var headColCount = Header.Count;

			//set the indices for the fields involved in child item data aggregation
			var iPId = _rules.Fields.GetHeaderIndex(FieldName.ProductId);
			if (iPId < 0) throw new Exception("Catalog feed header does not include a product id\nHeader = " + Header);
			var iParent = _rules.Fields.GetHeaderIndex(FieldName.ParentId);
			var iPrice = _rules.Fields.GetHeaderIndex(FieldName.Price);
			var iSale = _rules.Fields.GetHeaderIndex(FieldName.SalePrice);
			var iList = _rules.Fields.GetHeaderIndex(FieldName.ListPrice);
			var iCost = _rules.Fields.GetHeaderIndex(FieldName.Cost);
			var iInv = _rules.Fields.GetHeaderIndex(FieldName.Inventory);
			var iRate = _rules.Fields.GetHeaderIndex(FieldName.Rating);

			_progress.UpdateTask(-1, -1, "Getting child items");
			//examine each row and capture parent-child relationships
			//children are added to replacements and (usually) omitted from the catalog
			//also remove rows that do not have the correct number of columns
			//two objects are created: the Rows (list of lists) that holds all fields for each item; 
			//and the ParentProducts (dictionary) that accumulates the price and inventory data for all children 
			var errors = 0;
			var childCount = 0;
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
					var id = cols[iPId];  //already checked >= 0 above
					if (id.Length < 1)
					{
						//data error --each row must have a product id
						errors++;
						continue;
					}
					var parentId = Input.GetColVal(cols, iParent);

#if DEBUG
					var breakHere = false;
					if (!string.IsNullOrEmpty(parentId) && TableAccess.Instance.DebugIds.Contains(parentId))
						breakHere = true;
					else if (TableAccess.Instance.DebugIds.Contains(id))
						breakHere = true;
#endif
					// track all item details in parent (even if this is the parent) 
					// parent logic will clean up any pricing or stock issues
					var child = new ChildItem
					{
						Id = id,
						Price = Input.SafeFloatConvert(Input.GetColVal(cols, iPrice)),
						SalePrice = Input.SafeFloatConvert(Input.GetColVal(cols, iSale)),
						ListPrice = Input.SafeFloatConvert(Input.GetColVal(cols, iList)),
						Cost = Input.SafeFloatConvert(Input.GetColVal(cols, iCost)),
						Inventory = Input.SafeIntConvert(Input.GetColVal(cols, iInv)),
						Rating = Input.SafeFloatConvert(Input.GetColVal(cols, iRate))
					};
					string salePrice;
					AddOrUpdateParent(parentId, child, out salePrice);
					if (iSale >= 0) cols[iSale] = salePrice;

					if (string.IsNullOrEmpty(parentId) || child.Id.Equals(parentId))
					{
						Rows.Add(cols); //parent item found
						continue;
					}
					++childCount;
					//check for replacement rules
					ApplyReplacementRules(cols, parentId);

					//_progress.UpdateTask(childCount);

					//check for migration mapping
					if (_rules.MigrationRules != null) _rules.MigrationRules.MapItem(id, Header, cols);

					if (_rules.IncludeChildrenInCatalog)
						Rows.Add(cols);
				}
				catch (Exception ex)
				{
					errors++;
				}
			}
			return Rows.Count;
		}

		public override string WriteTable(out int itemCount)
		{
			//set column indices for each catalog field
			var iPId = _rules.Fields.GetHeaderIndex(FieldName.ProductId);
			var iPrice = _rules.Fields.GetHeaderIndex(FieldName.Price);
			var iSale = _rules.Fields.GetHeaderIndex(FieldName.SalePrice);
			var iList = _rules.Fields.GetHeaderIndex(FieldName.ListPrice);
			var iCost = _rules.Fields.GetHeaderIndex(FieldName.Cost);
			var iInv = _rules.Fields.GetHeaderIndex(FieldName.Inventory);
			var iRate = _rules.Fields.GetHeaderIndex(FieldName.Rating);
			var iName = _rules.Fields.GetHeaderIndex(FieldName.Name);
			var iAtt1 = _rules.UseDepartmentsAsCategories
										? _rules.Fields.GetHeaderIndex(FieldName.Department)
										: _rules.Fields.GetHeaderIndex(FieldName.Att1Id);
			var iAtt2 = _rules.Fields.GetHeaderIndex(FieldName.Att2Id);
			var iFilt = _rules.Fields.GetHeaderIndex(FieldName.Filter);
			var iLink = _rules.Fields.GetHeaderIndex(FieldName.Link);
			var iImag = _rules.Fields.GetHeaderIndex(FieldName.ImageLink);
			var iCode = _rules.Fields.GetHeaderIndex(FieldName.StandardCode);
			var iVis = _rules.Fields.GetHeaderIndex(FieldName.Visible);
			var iDep = _rules.Fields.GetHeaderIndex(FieldName.Department); 

			//set the image format
			string imageLinkFormat = null;
			if (!string.IsNullOrEmpty(_rules.ImageLinkFormat))
				imageLinkFormat = _rules.ImageLinkFormat;
			else if (!string.IsNullOrEmpty(_rules.ImageLinkBaseUrl)) //convert base url to a format
			{
				imageLinkFormat = _rules.ImageLinkBaseUrl + "/{0}"; //add the relative image address to the end 
			}

			//parse the json data into product records
			_progress.StartTask("Parsing data", "items", null, Rows.Count);
			var errors = 0;
			var products = new List<ProductRecord>();
			for (var i = 0; i < Rows.Count; i++)
			{
				var cols = Rows[i];
				try
				{
#if DEBUG
					var breakHere = false;
					if (TableAccess.Instance.DebugIds.Contains(cols[iPId]))
						breakHere = true;
#endif

					var catIds = Input.GetColVal(cols, iAtt1);
					if (!string.IsNullOrEmpty(_rules.CategorySeparator))
						catIds = catIds.Replace(_rules.CategorySeparator, ",");

					//assign values from the indexes created above
					var p = new ProductRecord
					{
						ProductId = Input.GetColVal(cols, iPId),
						Name = _cart.CleanUpTitle(Input.GetColVal(cols, iName)),
						Att1Id = _rules.Fields.Att1Enabled ? catIds : "",
						Att2Id = _rules.Fields.Att2Enabled ? Input.GetColVal(cols, iAtt2) : "",
						Filter = Input.GetColVal(cols, iFilt),
						Price = Input.GetColVal(cols, iPrice),
						SalePrice = Input.GetColVal(cols, iSale),
						ListPrice = Input.GetColVal(cols, iList),
						Cost = Input.GetColVal(cols, iCost),
						Link = Input.GetColVal(cols, iLink),
						ImageLink = Input.GetColVal(cols, iImag),
						Rating = Input.GetColVal(cols, iRate),
						StandardCode = Input.GetColVal(cols, iCode),
						Visible = Input.GetColVal(cols, iVis),
						Inventory = Input.GetColVal(cols, iInv)
					};

					if (_rules.MapCategoriesToFilters) p.Filter = catIds;
					if (p.Link.Length > 0) p.Link = p.Link.Replace("\\/", "/"); //unescape slashes
					if (p.ImageLink.Length > 0)
					{
						p.ImageLink = p.ImageLink.Replace("\\/", "/"); //unescape slashes
						var start = p.ImageLink.IndexOf("//");
						if (start > 0) p.ImageLink = p.ImageLink.Substring(start); //remove protocol
						if (imageLinkFormat != null)
							p.ImageLink = string.Format(imageLinkFormat, p.ImageLink);
					}

					//create an XElement for ApplyAltPrices and ApplyRules
					//var productX = new XElement("product");
					//for (var j = 0; j < cols.Count; j++)
					//{
					//  try
					//  {
					//    var value = cols[j];
					//    //convert department ids to names
					//    if (_rules.ExportDepartmentNames && j == iDep && _cart.Departments.Any())
					//    {
					//      var idList = value.Split(new[] { ',' });
					//      string name;
					//      var nameList = idList.Select(x => _cart.Departments.TryGetValue(x, out name) ? name : x);
					//      value = nameList.Aggregate((w, z) => string.Format("{0},{1}", w, z));
					//    }

					//    //productX will contain all fields retrieved, even if not part of standard list
					//    //this is important to enable _rules to use other fields
					//    productX.Add(new XElement(Header[j], value));
					//  }
					//  catch
					//  {
					//    errors++;
					//  }
					//}

					//get parent inventory, price ranges and additional alternate prices
					ApplyAltPricesAndLinks(ref p, ref cols);
					if (_rules.MapStockToVisibility && !string.IsNullOrEmpty(p.Inventory) && p.Inventory.Equals("0"))
						p.Visible = "0";

					ApplyRules(ref p, cols);
					products.Add(p);
				}
				catch
				{
					errors++;
				}
				_progress.UpdateTask(products.Count, -1, null, string.Format("{0} errors", errors));
			}
			if (products.Count < 1)
				throw new Exception("Catalog feed has no data");
			itemCount = products.Count;
			_progress.EndTask(itemCount);
			if (_rules.ApiMinimumCatalogSize > itemCount)
				throw new Exception("Catalog size is less than the minimum required in the site _rules");

			if (_migrationSlave) return ""; //don't need to save catalog for migration slaves (MigrationMap created in Apply_rules)

			_progress.UpdateTable(itemCount, -1, "Writing table");
			return TableAccess.Instance.WriteTable(_rules.Alias, CartExtractor.CatalogFilename, products);
		}

		private void AddOrUpdateParent(string parentId, ChildItem child, out string salePrice)
		{
			salePrice = "";
			if (string.IsNullOrEmpty(child.Id)) return;

			if (string.IsNullOrEmpty(parentId)) parentId = child.Id;

			//special handling in case parent id is a list --use only first id for replacements
			var parentList = new List<string> { parentId };
			if (parentId.Contains(","))
			{
				parentList = parentId.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).ToList();
				parentId = parentList[0];
			}
			if (!child.Id.Equals(parentId)) //&& parentList.Count == 1) //TODO: should we skip if there are multiple parents
				_cart.Replacements.Add(new ReplacementRecord(child.Id, parentId));

			//remove invalid sale prices (common issue)
			if (child.SalePrice >= child.Price) child.SalePrice = 0F;
			salePrice = child.SalePrice > 0 ? child.SalePrice.ToString("F2") : ""; // validated sale price is passed back out

			foreach (var p in parentList) //usually only one
			{
				//parent item accumulates the price range of the children
				ParentItem parent;
				if (!_parentProducts.TryGetValue(p, out parent))
				{
					parent = new ParentItem { Id = p };
					_parentProducts.Add(p, parent);
				}

				if (child.Inventory > 0 || _rules.IgnoreStockInPriceRange)
				{
					if (child.Inventory > 0)
						parent.Inventory += child.Inventory; //ignore negative stock values
					if (child.Price > 0)
					{
						if (parent.Price.Equals(0) || parent.Price > child.Price) parent.Price = child.Price;
						if (parent.TopPrice < child.Price) parent.TopPrice = child.Price;
					}
					if (child.SalePrice > 0)
					{
						if (parent.SalePrice.Equals(0) || parent.SalePrice > child.SalePrice) parent.SalePrice = child.SalePrice;
						if (parent.TopSalePrice < child.SalePrice) parent.TopSalePrice = child.SalePrice;
					}
					if (child.ListPrice > 0)
					{
						if (parent.ListPrice.Equals(0) || parent.ListPrice > child.ListPrice) parent.ListPrice = child.ListPrice;
						if (parent.TopListPrice < child.ListPrice) parent.TopListPrice = child.ListPrice;
					}
					if (child.Cost > 0)
					{
						if (parent.Cost.Equals(0) || parent.Cost > child.Cost) parent.Cost = child.Cost;
						if (parent.TopCost < child.Cost) parent.TopCost = child.Cost;
					}
				}
				if (child.Rating > 0)
				{
					if (parent.TopRating < child.Rating) parent.TopRating = child.Rating;
					parent.RatingSum += child.Rating;
					parent.RatingCount++;
				}
				_parentProducts[p] = parent;
			}
		}

		/// <summary>
		/// Check all site rules that pertain to catalog items (exclusions, filters ,etc).
		/// Note that an XElement is always used even if the input format is not xml.
		/// This allows all fields to be analyzed, even if they are not part of the standard list.
		/// Also note that for efficiency, exclusion tests are stopped as soon as one is found to be true.
		/// This means that each item will only show one exclusion cause even if it matches more than one rule.
		/// That could be changed in the future if it is deemed worth the performance trade-off.
		/// </summary>
		/// <param name="p"></param>
		/// <param name="product"></param>
		public void ApplyRules(ref ProductRecord p, List<string> data)
		{
			try
			{
				if (_rules.ReverseVisibleFlag)
				{
					if (p.Visible.Equals("0")
						|| p.Visible.Equals("false", StringComparison.OrdinalIgnoreCase)
						|| p.Visible.Equals("no", StringComparison.OrdinalIgnoreCase))
						p.Visible = "1";
					else
						p.Visible = "0";
				}
#if DEBUG
				var breakHere = false;
				if (TableAccess.Instance.DebugIds.Contains(p.ProductId))
					breakHere = true;
#endif
				//first check for excluded categories
				var excluded = false;
				if (_rules.CategoryRules.AnyExcluded(p.Att1Id))
				{
					_cart.Exclusions.Add(new ExclusionRecord(p.ProductId));
					_cart.AddExclusionCause(p.ProductId, CartExtractor.ExcludedCategoryCause);
					excluded = true;
				}

				//then check all other exclusion rules (note: item replacements are handled in GetReplacements()
				else if (_rules.ExclusionRules != null)
				{
					foreach (var c in _rules.ExclusionRules.Where(c => c.Compare(Input.GetValue(Header, data, c.ResultField))))
					{
						_cart.Exclusions.Add(new ExclusionRecord(p.ProductId));
						_cart.AddExclusionCause(p.ProductId, c.Name);
						excluded = true;
						break;
					}
				}
				if (!excluded && _rules.ExclusionSet != null)
				{
					List<string> matchingNames;
					excluded = _rules.ExclusionSet.Evaluate(Header, data, out matchingNames);
					if (excluded)
						foreach (var name in matchingNames)
							_cart.AddExclusionCause(p.ProductId, name);
				}

				//exclude items that do not have images if AllowMissingPhotos is false (which is the default)
				//this is checked last so that hidden and out-of-stock items don't count toward missing image count
				if (!excluded && String.IsNullOrEmpty(p.ImageLink) && !_rules.AllowMissingPhotos)
				{
					//provides a means for clients to exclude items if image link empty
					_cart.Exclusions.Add(new ExclusionRecord(p.ProductId));
					_cart.AddExclusionCause(p.ProductId, CartExtractor.MissingImageCause);
					excluded = true;
				}

				//remove ignored categories
				p.Att1Id = _rules.CategoryRules.RemoveIgnored(p.Att1Id);

				//apply filters
				if (_rules.FiltersOn)
				{
					//check category rules
					var filters = _rules.CategoryRules.AnyFiltered(p.Att1Id);
					if (_rules.CategoryRules.AnyUniversal(p.Att1Id))
						filters.Add(_rules.UniversalFilterName);

					//check filter rules
					if (_rules.FilterRules != null && _rules.FilterRules.Any())
					{
						var matches = _rules.FilterRules.Where(c => c.Compare(Input.GetValue(Header, data, c.ResultField)))
																							.Select(c => c.Name);
						if (matches.Any())
							filters.AddRange(matches);
					}

					//check filter parsing rules
					if (_rules.FilterParsingRules != null)
					{
						foreach (var f in _rules.FilterParsingRules)
						{
							List<string> results;
							if (f.ApplyRules(Header, data, out results))
								filters.AddRange(results);
						}
					}

					//combine any filters found
					if (filters.Count > 0)
					{
						if (p.Filter.Length > 0) filters.AddRange(p.Filter.Split(new[] { ',' })); //combine first so we can remove duplicates
						p.Filter = filters.Distinct().Aggregate((w, j) => String.Format("{0},{1}", w, j));
					}
					if (p.Filter.Length < 1) //if no matches then assume universal
						p.Filter = _rules.UniversalFilterName;
				}

				//check for full catalog replacement 
				ApplyReplacementRules(data);

				//check for Featured recs
				if (_rules.FeaturedCrossSellOn)
					_cart.FeaturedCrossSells.AddRecords(p.ProductId, Header, data, _rules.FeaturedCrossSellRules);
				if (_rules.FeaturedUpSellOn)
					_cart.FeaturedUpSells.AddRecords(p.ProductId, Header, data, _rules.FeaturedUpSellRules);

				//check for migration mapping
				if (_rules.MigrationRules != null)
					_rules.MigrationRules.MapItem(p.ProductId, Header, data);
			}
			catch (Exception ex)
			{
				if (BoostLog.Instance != null)
					BoostLog.Instance.WriteEntry(EventLogEntryType.Information, "Error applying rules", ex, _cart.Alias);
			}
		}

		private void ApplyReplacementRules(List<string> data, string newId = null)
		{
			if (_rules.ReplacementRules == null || _rules.ReplacementRules.Count < 1
					|| !_rules.ReplacementRules[0].Type.Equals(ReplacementCondition.RepType.Catalog))
				return;

			var oldId = Input.GetValue(Header, data, _rules.ReplacementRules[0].OldResultField);
			if (string.IsNullOrEmpty(newId))
				newId = Input.GetValue(Header, data, _rules.ReplacementRules[0].NewResultField);

			if (!String.IsNullOrEmpty(oldId) && !String.IsNullOrEmpty(newId)
					&& !_cart.Replacements.Any(r => r.OldId.Equals(oldId))) //can only have one replacement for each item
				_cart.Replacements.Add(new ReplacementRecord(oldId, newId));
		}

		public void ApplyAltPricesAndLinks(ref ProductRecord p, ref List<string> data)
		{
			if (p == null) return;

			//setup list to record alternate prices
			var altPriceList = new List<string>();

			//map child price ranges gathered above to parents
			var id = p.ProductId;
#if DEBUG
			var breakHere = false;
			if (TableAccess.Instance.DebugIds.Contains(id))
				breakHere = true;
#endif
			ParentItem parent;
			if (_parentProducts.TryGetValue(id, out parent))
			{
				//if parent has no inventory, check accumulated inventory of children
				if (Input.SafeIntConvert(p.Inventory) < 1)
				{
					p.Inventory = parent.Inventory.ToString("N");
					var fieldName = _rules.Fields.GetName(FieldName.Inventory);
					if (!string.IsNullOrEmpty(fieldName))
					{
						var index = Header.FindIndex(x => x.Equals(fieldName, StringComparison.OrdinalIgnoreCase));
						if (index > -1 && index < data.Count)
							data[index] = p.Inventory; //adjust for other later rules
					}
					//product.SetElementValue(_rules.Fields.GetName(FieldName.Inventory), p.Inventory);
				}

				//if parent has no rating, apply child ratings (leave blank for no ratings)
				if (string.IsNullOrEmpty(p.Rating))
				{
					if (_rules.UseAverageChildRating)
					{
						if (parent.RatingSum > 0 && parent.RatingCount > 0)
						{
							var average = parent.RatingSum / (float)(parent.RatingCount);
							p.Rating = average.ToString("F2");
						}
					}
					else if (parent.TopRating > 0)
						p.Rating = parent.TopRating.ToString("F2");
				}

				//standard prices contain low end of ranges from child prices
				bool showSalePrice = false;
				if (parent.Price > 0)
				{
					p.Price = parent.Price.ToString("F2");
					var fieldName = _rules.Fields.GetName(FieldName.Price);
					if (!string.IsNullOrEmpty(fieldName))
					{
						var index = Header.FindIndex(x => x.Equals(fieldName, StringComparison.OrdinalIgnoreCase));
						if (index > -1 && index < data.Count)
							data[index] = p.Price; //adjust for other later rules
					}
					//product.SetElementValue(_rules.Fields.GetName(FieldName.Price), p.Price);

					//always use parent sale price when using parent price
					if (parent.SalePrice > 0)
					{
						if (parent.SalePrice < parent.Price)
						{
							p.SalePrice = parent.SalePrice.ToString("F2");
							showSalePrice = true;
						}
						else p.SalePrice = _rules.HiddenSalePriceText;
					}
					else
						p.SalePrice = "";

					fieldName = _rules.Fields.GetName(FieldName.SalePrice);
					if (!string.IsNullOrEmpty(fieldName))
					{
						var index = Header.FindIndex(x => x.Equals(fieldName, StringComparison.OrdinalIgnoreCase));
						if (index > -1 && index < data.Count)
							data[index] = p.SalePrice; //adjust for other later rules
					}
					//product.SetElementValue(_rules.Fields.GetName(FieldName.SalePrice), p.SalePrice);

				}
				if (parent.ListPrice > 0)
				{
					p.ListPrice = parent.ListPrice.ToString("F2");
					var fieldName = _rules.Fields.GetName(FieldName.ListPrice);
					if (!string.IsNullOrEmpty(fieldName))
					{
						var index = Header.FindIndex(x => x.Equals(fieldName, StringComparison.OrdinalIgnoreCase));
						if (index > -1 && index < data.Count)
							data[index] = p.ListPrice; //adjust for other later rules
					}
					//product.SetElementValue(_rules.Fields.GetName(FieldName.ListPrice), p.ListPrice);
				}
				if (parent.Cost > 0)
				{
					p.Cost = parent.Cost.ToString("F2");
					var fieldName = _rules.Fields.GetName(FieldName.Cost);
					if (!string.IsNullOrEmpty(fieldName))
					{
						var index = Header.FindIndex(x => x.Equals(fieldName, StringComparison.OrdinalIgnoreCase));
						if (index > -1 && index < data.Count)
							data[index] = p.Cost; //adjust for other later rules
					}
					//product.SetElementValue(_rules.Fields.GetName(FieldName.Cost), p.Cost);
				}

				//alternate prices contain top end of ranges
				//NOTE: Altprices are not added to the product XElement so they cannot be used in rules
				altPriceList.Add(parent.TopPrice > parent.Price ? parent.TopPrice.ToString("F2") : "");
				altPriceList.Add(showSalePrice && parent.TopSalePrice > parent.SalePrice ? parent.TopSalePrice.ToString("F2") : "");
				altPriceList.Add(parent.TopListPrice > parent.ListPrice ? parent.TopListPrice.ToString("F2") : "");
				altPriceList.Add(parent.TopCost > parent.Cost ? parent.TopCost.ToString("F2") : "");
			}

			//process any AltPrice fields
			//var productX = product; //must make copy to pass in lambda
			var d = data; //must make copy to pass in lambda
			var fields = _rules.Fields.GetAltFields(AltFieldGroup.AltPriceFields);
			if (fields != null && fields.Any())
			{
				while (altPriceList.Count < 4)
					altPriceList.Add("");
				altPriceList.AddRange(fields.Select(t => CartExtractor.FormatPrice(Input.GetValue(Header, d, t))));
			}
			if (altPriceList.Any(x => !string.IsNullOrEmpty(x)))
				_cart.AltPrices.Add(p.ProductId, altPriceList);

			//process any AltPage fields
			fields = _rules.Fields.GetAltFields(AltFieldGroup.AltPageFields);
			if (fields != null && fields.Any())
			{
				var altPageList = new List<string>();
				altPageList.AddRange(fields.Select(t => Input.GetValue(Header, d, t)));
				if (altPageList.Any(x => !string.IsNullOrEmpty(x)))
					_cart.AltPageLinks.Add(p.ProductId, altPageList);
			}

			//process any altImage fields
			fields = _rules.Fields.GetAltFields(AltFieldGroup.AltImageFields);
			if (fields != null && fields.Any())
			{
				var altImageList = new List<string>();
				altImageList.AddRange(fields.Select(t => Input.GetValue(Header, d, t)));
				if (altImageList.Any(x => !string.IsNullOrEmpty(x)))
					_cart.AltImageLinks.Add(p.ProductId, altImageList);
			}

			//process any altTitle fields
			fields = _rules.Fields.GetAltFields(AltFieldGroup.AltTitleFields);
			if (fields != null && fields.Any())
			{
				var altTitleList = new List<string>();
				altTitleList.AddRange(fields.Select(t => Input.GetValue(Header, d, t)));
				if (altTitleList.Any(x => !string.IsNullOrEmpty(x)))
					_cart.AltTitles.Add(p.ProductId, altTitleList);
			}
		}

	}
}