using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Xml.Linq;
using _4_Tell.DashService;
using _4_Tell.CommonTools;
using _4_Tell.IO;

namespace _4_Tell.CartExtractors
{
	public sealed class TabbedFeedExtractor : CartExtractor
	{
		public TabbedFeedExtractor(SiteRules rules)
			: base(rules)
		{
		}

		#region Overrides of CartExtractor

		public override bool ValidateCredentials(out string status)
		{
			throw new NotImplementedException();
		}

		protected override void FillDefaultFieldNames()
		{
			//set defaults for fields names not set in client details
			Rules.SetDefaultField(SiteRules.FieldName.ProductId, "ProdID");
			Rules.SetDefaultField(SiteRules.FieldName.Name, "OfferName");
			Rules.SetDefaultField(SiteRules.FieldName.Att1Id, "CategoryID");
			Rules.SetDefaultField(SiteRules.FieldName.Att2Id, "Manufacturer"); // "Brand"
			Rules.SetDefaultField(SiteRules.FieldName.Price, "CurrentPrice");
			Rules.SetDefaultField(SiteRules.FieldName.SalePrice, "SalePrice");
			Rules.SetDefaultField(SiteRules.FieldName.ListPrice, "");
			Rules.SetDefaultField(SiteRules.FieldName.Cost, "");
			Rules.SetDefaultField(SiteRules.FieldName.OnSale, "");
			Rules.SetDefaultField(SiteRules.FieldName.Filter, "");
			Rules.SetDefaultField(SiteRules.FieldName.Rating, "");
			Rules.SetDefaultField(SiteRules.FieldName.StandardCode, "MPN");
			Rules.SetDefaultField(SiteRules.FieldName.Link, "ActionURL");
			Rules.SetDefaultField(SiteRules.FieldName.ImageLink, "ReferenceThumbURL");
			Rules.SetDefaultField(SiteRules.FieldName.Visible, "");
			Rules.SetDefaultField(SiteRules.FieldName.Inventory, "InStock");
		}

		protected override void ReleaseCartData()
		{
			//no local data to release
		}

		protected override string GetInventory(out int itemCount)
		{
			throw new NotImplementedException();
		}

		public override void LogSalesOrder(string orderId)
		{
			throw new NotImplementedException();
		}

		protected override string GetCatalog(out int itemCount)
		{
			Progress.UpdateTable(-1, -1, "Extracting");
			itemCount = 0;

			//get product count
			var pXml = XDocument.Load(Rules.CatalogFeedUrl);
			//var pXml = XDocument.Load("C:\\\\ProgramData\\4-Tell2.0\\CookDrct\\upload\\cookstest_optimize.xml");

			if (pXml.Root == null) throw new Exception("Error retrieving the catalog feed.");
			var catalogXml = pXml.Root.Descendants("Offer");
			if (catalogXml == null) throw new Exception("The catalog feed does not conatin any products.");

			var pRows = catalogXml.Count();
			Progress.UpdateTable(pRows, 0, "Parsing data");

			Products = new List<ProductRecord>();
			try
			{
				//now get each page of products
				var errors = 0;
				foreach (var product in catalogXml)
				{
					try
					{
						var p = new ProductRecord
											{
												ProductId = Input.GetValue(product, Rules.FieldNames[(int)SiteRules.FieldName.ProductId]),
												Name = CleanUpTitle(Input.GetValue(product, Rules.FieldNames[(int)SiteRules.FieldName.Name])),
												Att1Id = Input.GetValue(product, Rules.FieldNames[(int)SiteRules.FieldName.Att1Id]),
												Att2Id = Input.GetValue(product, Rules.FieldNames[(int)SiteRules.FieldName.Att2Id]),
												Price = Input.GetValue(product, Rules.FieldNames[(int)SiteRules.FieldName.Price]),
												SalePrice = Input.GetValue(product, Rules.FieldNames[(int)SiteRules.FieldName.SalePrice]),
												ListPrice = Input.GetValue(product, Rules.FieldNames[(int)SiteRules.FieldName.ListPrice]),
												Cost = Input.GetValue(product, Rules.FieldNames[(int)SiteRules.FieldName.Cost]),
												Filter = Input.GetValue(product, Rules.FieldNames[(int)SiteRules.FieldName.Filter]),
												Rating = Input.GetValue(product, Rules.FieldNames[(int)SiteRules.FieldName.Rating]),
												StandardCode = Input.GetValue(product, Rules.FieldNames[(int)SiteRules.FieldName.StandardCode]),
												Link = StripUriPrefix(Input.GetValue(product, Rules.FieldNames[(int)SiteRules.FieldName.Link])),
												ImageLink = Input.GetValue(product, Rules.FieldNames[(int)SiteRules.FieldName.ImageLink]),
												Visible = Input.GetValue(product, Rules.FieldNames[(int)SiteRules.FieldName.Visible]),
												Inventory = Input.GetValue(product, Rules.FieldNames[(int)SiteRules.FieldName.Inventory])
																			.Equals("In Stock", StringComparison.OrdinalIgnoreCase)
																			? "1"
																			: "0"
											};

						if (string.IsNullOrEmpty(p.Visible))
							p.Visible = "1";
						if (p.Inventory.Equals("0")) p.Visible = "0"; //make sure out-of-stock items are marked not visible

						//check category conditions, exclusions, and filters
						ApplyRules(ref p, product);

						Products.Add(p);
					}
					catch (Exception ex)
					{
						Debug.WriteLine(ex.Message);
						errors++;
					}
					Progress.UpdateTask(Products.Count, -1, null, string.Format("{0} errors", errors));
				}
			}
			catch (Exception ex)
			{
				Debug.WriteLine(ex.Message);
			}
			if (Products.Count < 1)
				throw new Exception("Unable to extract catalog");
			itemCount = Products.Count;
			Progress.EndTask(itemCount);

			if (_migrationSlave) return ""; //don't need to save catalog for migration slaves (MigrationMap created in AllpyRules)

			Progress.UpdateTable(itemCount, -1, "Writing table");
			return TableAccess.Instance.WriteTable(Alias, CatalogFilename, Products);
		}

		/// <summary>
		///  GetSalesMonth not used on CV3
		/// </summary>
		/// <param name="exportDate"></param>
		/// <param name="filename"></param>
		/// <param name="rowCount"></param>
		/// <returns></returns>
		protected override string GetSalesMonth(DateTime exportDate, string filename, out int rowCount)
		{
			throw new NotImplementedException();
		}

		protected override string GetCustomers(DateTime exportDate, string filename, out int itemCount)
		{
			throw new NotImplementedException();
		}

		protected override string GetAtt1Names(out int rowCount)
		{
			rowCount = 0;
			return "not used";
		}

		protected override string GetAtt2Names(out int rowCount)
		{
			rowCount = 0;
			return "not used";
		}

		public override List<string> GetAllQueryableFieldNames(DataGroup @group)
		{
			var fields = new List<string>
			             	{
											"SKU","ProdName","ProdDescription","Brand","Manufacturer","IsRetail","RetailPrice","RetailPriceCat","IsWholesale","WholesalePrice","WholesaleQty","ProdStatus","ProdInventory","OutOfStockPoint","ProdOnOrder","AltID","VendorID","DescriptionHeader","ProductURLName","SpecialPrice","IsSpecialOngoing","SpecialStart","SpecialEnd","SpecialText","BackorderedDate","IgnoreBackorder","MinimumQuantity","MaximumQuantity","QuantityInSet","NumIterationsDisplayed","DisplayWeight","ActualWeight","Unit","ImageSetThumb","ImageSetLarge","ImageSetPopup","ImageSetType","ImageSetAttributeName","ImageSetTitle","ImageSetEditType","IsInactive","IsOutOfSeason","IsTaxExempt","HasTextField","IsHidden","IsFeatured","IsNew","IsGoogleCheckoutExempt","IsComparable","IsContentOnly","IsInventoryExempt","IsSubscription","IsDonation","SubscriptionPrice","HasElectronicDelivery","ElectronicDeliveryLink","ElectronicDeliveryDaysAvailable","ElectronicDeliveryDescription","ElectronicDeliveryEditType","IsKit","KitProductSKUs","ShipPreference","ByShipperMethod","ByShipperPreference","ByShipperFixedShipping","FixedShipping","ShipsInOwnBox","PackageLength","PackageWidth","PackageHeight","FreightClass","IsGiftCertificate","GiftCertificateDaysAvailable","GiftCertificateValue","Rating","Keywords","MetaKeywords","MetaTitle","MetaDescription","CategoryIDs","DefaultCategoryID","Custom","Template","DependencySKUs","DependencyType","AdditionalProdSKUs","RelatedProdSKUs","GiftWrap","GiftWrapName","GiftWrapSKU","GiftWrapAmount","CategoryFilter","CategoryFilterValue","CategoryFilterSortValue","ParentSKU","ChildImage","IsRewardsEligible","RewardsPoints","SubProductAttributes","IsAttribute","AttributeTitle1","AttributeTitle2","AttributeTitle3","AttributeTitle4","AttributeSKU","Attribute1","Attribute2","Attribute3","Attribute4","Attribute1Code","Attribute2Code","Attribute3Code","Attribute4Code","AttributePrice","AttributePriceCat","AttributeSpecialPrice","IsAttributeSpecialOngoing","AttributeSpecialStart","AttributeSpecialEnd","AttributeBackorderedDate","AttributeIgnoreBackorder","AttributeInventory","AttributeOutOfStockPoint","AttributeOnOrder","IsAttributeInactive","AttributeStatus","IsAttributeGiftCertificate","AttributeGiftCertificateDaysAvailable","AttributeGiftCertificateValue","IsAttributeSubscription","AttributeSubscriptionPrice","IsAttributeDonation","AttributeRewardsPoints"
			             	};

			return fields;
		}

		#endregion

		#region Utilities

		#endregion
	}
}