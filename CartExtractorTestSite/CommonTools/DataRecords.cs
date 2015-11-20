//XElement
	//StringBuilder
	//Thread

using System;
using System.Collections.Generic;
using System.Linq;
using _4_Tell.IO;

namespace _4_Tell.CommonTools
{
	#region Enums

	public enum LookupType
	{
		UserIds,
		ItemIds,
		Att1Ids,
		Att2Ids,
		CartIds,
		ClickIds,
	}

	public enum FillMode
	{
		None,
		Crowd,
		Genomic,
		TopSell,
		All,
		Strict //restricted to only the exact type requested
	}

	public enum ResultTypes
	{
		// these are the result type's that the calling program can request using an integer
		// corresponding to the position in this list (starting with zero for the first position)
		CrossSell,
		Personalized,
		Blended,
		Similar,
		TopSell,
		TopSellAtt1,
		FeaturedCrossSell,
		FeaturedUpSell,
		Featured,
		FeaturedAtt1,
		RecentPurchase,
		RecentCart,
		RecentBrowse,
		Influencers
	};


	public enum OldResultTypesV1_1
	{
		// these are the old result type's used by version 1.1, passed as an integer
		// corresponding to the position in this list (starting with zero for the first position)
		Best,
		CrossSell,
		CategoricalCrossSell,
		UpSell,
		Similar,
		TopSell

		//BestTop,
		//TopSellCatagory,
		//TopSellBrand,
	};

	#endregion

	#region RecFilter Params
	public struct ServiceParamsValidated
	{
		//internally calculated
		//public BoostSite Site; //PARAM_CHANGE --move to separate global
		public ClickRecord.pageType PageType;
		public List<string> ProductList;
		public List<string> CartList;
		public List<string> BlockList;
		public List<string> ClickList;
		public List<string> UserList;
		public List<string> Att1List;
		public List<int> ProductIndex;
		public List<int> Att1Index;
		public List<int> CartIndex;
		public List<int> ClickIndex;
		public List<int> UserIndex;
		//public int NumResultLists;  //PARAM_CHANGE --use _outParams.Count instead
		public ResultDef ResultDef;  //PARAM_CHANGE: List<ResultDef> ResultDefs

		public int NumProducts
		{
			get { return ProductList == null ? 0 : ProductList.Count; }
		}

		public int NumAtt1Ids
		{
			get { return Att1List == null ? 0 : Att1List.Count; }
		}

		public int NumCustomers
		{
			get { return UserList == null ? 0 : UserList.Count; }
		}

		/// <summary>
		/// Initialize a set of output params with the resultDef and pageType. 
		/// All lists will be initialized.
		/// </summary>
		/// <param name="resultDef"></param>
		/// <param name="pageType"></param>
		public ServiceParamsValidated(ResultDef resultDef, ClickRecord.pageType pageType)
		{
			ResultDef = resultDef != null ? new ResultDef(resultDef) : new ResultDef();
			PageType = pageType;
			ProductList = new List<string>();
			Att1List = new List<string>();
			CartList = new List<string>();
			ClickList = new List<string>();
			UserList = new List<string>();
			BlockList = new List<string>();
			ProductIndex = new List<int>();
			Att1Index = new List<int>();
			CartIndex = new List<int>();
			ClickIndex = new List<int>();
			UserIndex = new List<int>();
		}

		/// <summary>
		/// Crate a copy of the output params with the option to override the resultDef
		/// All lists will be initialized.
		/// </summary>
		/// <param name="source"></param>
		/// <param name="resultDef"></param>
		public ServiceParamsValidated(ServiceParamsValidated source, ResultDef resultDef = null)
		{
			PageType = source.PageType;
			ProductList = source.ProductList == null || source.ProductList.Count < 1 ? new List<string>() : new List<string>(source.ProductList);
			Att1List = source.Att1List == null || source.Att1List.Count < 1 ? new List<string>() : new List<string>(source.Att1List);
			CartList = source.CartList == null || source.CartList.Count < 1 ? new List<string>() : new List<string>(source.CartList);
			ClickList = source.ClickList == null || source.ClickList.Count < 1 ? new List<string>() : new List<string>(source.ClickList);
			UserList = source.UserList == null || source.UserList.Count < 1 ? new List<string>() : new List<string>(source.UserList);
			BlockList = source.BlockList == null || source.BlockList.Count < 1 ? new List<string>() : new List<string>(source.BlockList);
			ProductIndex = source.ProductIndex == null || source.ProductIndex.Count < 1 ? new List<int>() : new List<int>(source.ProductIndex);
			Att1Index = source.Att1Index == null || source.Att1Index.Count < 1 ? new List<int>() : new List<int>(source.Att1Index);
			CartIndex = source.CartIndex == null || source.CartIndex.Count < 1 ? new List<int>() : new List<int>(source.CartIndex);
			ClickIndex = source.ClickIndex == null || source.ClickIndex.Count < 1 ? new List<int>() : new List<int>(source.ClickIndex);
			UserIndex = source.UserIndex == null || source.UserIndex.Count < 1 ? new List<int>() : new List<int>(source.UserIndex);
			ResultDef = resultDef != null ? new ResultDef(resultDef) :
				source.ResultDef != null ? new ResultDef(source.ResultDef) :
				new ResultDef();
		}
	}

	public class ResultDef
	{
		public bool Block { get; private set; }
		public ResultTypes ResultType { get; private set; }
		public FillMode RecipeFillMode { get; private set; }
		public int NumResults { get; set; }
		public int StartPos { get; set; }
		public int TotalResults { get; set; }

		public ResultDef()
		{
			//set defaults
			ResultType = ResultTypes.CrossSell;
			RecipeFillMode = FillMode.All;
			NumResults = 1;
			StartPos = 1;
			TotalResults = 1;
			Block = false;
		}

		public ResultDef(ResultTypes resultType, FillMode recipeFillMode, int numResults, int startPos = 1, bool block = false)
		{
			ResultType = resultType;
			RecipeFillMode = recipeFillMode;
			NumResults = numResults;
			StartPos = startPos > 0 ? startPos : 1;
			TotalResults = StartPos + NumResults - 1;
			Block = block;
		}

		public ResultDef(ResultDef source)
		{
			ResultType = source.ResultType;
			RecipeFillMode = source.RecipeFillMode;
			NumResults = source.NumResults;
			StartPos = source.StartPos > 0 ? source.StartPos : 1;
			TotalResults = StartPos + NumResults - 1;
			Block = source.Block;
		}
	}

	public struct ServiceParams
	{
		//user input
		//public string ClientAlias;
		public string PageCode;
#if !CART_EXTRACTOR_TEST_SITE
        public List<BoostDataContracts.ToutParams> Touts;
#endif
		public string ProductIDs; // Null if not known, comma separated list if more than one
		public string Att1IDs; // Null if not known, comma separated list if more than one
		public string CartIDs; // comma separated list of IDs that are in the cart, null if none 
		public string BlockIDs; // comma separated list of IDs that should not be returned, null if none 
		public string BlockResults; // comma separated list of other results that should be added to the blockIDs 
		public string ClickStreamIDs; // comma separated list of IDs for the last few items clicked, null if none 
		public string CustomerID; // Null if not known
		public List<int> NumResults;
		public List<int> StartPosition; // first position = 1. Requires a list of results that is startPosition - 1 + numResults
		public List<int> ResultTypeInt;
		public List<string> RecipeFillMode;	// this replaces fillTopSell. Modes are "none", "genomic", "topsell" (default is topSell === fillTopSell=true. fillTopSell=false is none)
		public bool FillTopSell; // true or false to fill with top selling items
	}
	#endregion

	#region DataRecord Classes

	public class AttributeRecord
	{
		public string Id { get; set; }
		public string Name { get; set; }

		public AttributeRecord()
		{
		}

		public AttributeRecord(string id, string name)
		{
			Id = id;
			Name = name;
		}

		public AttributeRecord(string[] data)
		{
			//no need to check lenth each time --if length is too short it will throw IndexOutOfRange
			//if (data.Count != 2) throw new ArgumentException("data");

			Id = data[0];
			Name = data[1];
		}

		public string ToString(string delimiter = "\t", string newLine = "\r\n")
		{
			return string.Format("{2}{0}{3}{1}", delimiter, newLine, Id, Name);
		}

		static public string Header(string delimiter = "\t", string newLine = "\r\n")
		{
			return string.Format("ID{0}Name{1}", delimiter, newLine);
		}
	}

	public class ProductRecord
	{
		public string ProductId { get; set; }
		public string Name { get; set; }
		public string Att1Id { get; set; }
		public string Att2Id { get; set; }
		public string Price { get; set; }
		public string SalePrice { get; set; }
		public string ListPrice { get; set; }
		public string Cost { get; set; }
		public string Visible { get; set; }
		public string Inventory { get; set; }
		public string Filter { get; set; }
		public string Link { get; set; }
		public string ImageLink { get; set; }
		public string Rating { get; set; }
		public string StandardCode { get; set; }

		public ProductRecord()
		{
		}

		public ProductRecord(string[] data)
		{
			//Note: if length is too short it will throw IndexOutOfRange

			ProductId = data[0];
			Name = data[1];
			Att1Id = data[2];
			Att2Id = data[3];
			Price = data[4];
			SalePrice = data[5];
			ListPrice = data[6];
			Cost = data[7];												 
			Visible = data[8];
			Inventory = data[9];
			Filter = data[10];
			Link = data[11];
			ImageLink = data[12];
			Rating = data[13];
			StandardCode = data[14];
		}

		public string ToString(string delimiter = "\t", string newLine = "\r\n")
		{
			return string.Format("{2}{0}{3}{0}{4}{0}{5}{0}{6}{0}{7}{0}{8}{0}{9}{0}{10}{0}{11}{0}{12}{0}{13}{0}{14}{0}{15}{0}{16}{1}",
													 delimiter, newLine, ProductId, Name, Att1Id, Att2Id, Price, SalePrice, ListPrice, Cost, 
													 Visible, Inventory, Filter, Link, ImageLink, Rating, StandardCode);
		}

		public static string Header(string delimiter = "\t", string newLine = "\r\n")
		{
			return
				string.Format(
					"ProductId{0}Name{0}Att1Id{0}Att2Id{0}Price{0}SalePrice{0}ListPrice{0}Cost{0}Visible{0}Inventory{0}Filter{0}Link{0}ImageLink{0}Rating{0}StandardCode{1}",
					delimiter, newLine);
		}
	}

	public class InventoryRecord
	{
		public string ProductId { get; set; }
		public int Quantity { get; set; }

		public InventoryRecord(string id, string quantity)
		{
			ProductId = id;
			Quantity = Input.SafeIntConvert(quantity);
		}

		public InventoryRecord(string[] data)
		{
			//no need to check lenth each time --if length is too short it will throw IndexOutOfRange
			//if (data.Count != 2) throw new ArgumentException("data");

			ProductId = data[0];
			Quantity = Input.SafeIntConvert(data[1]);
		}

		public string ToString(string delimiter = "\t", string newLine = "\r\n")
		{
			return string.Format("{2}{0}{3}{1}",
				delimiter, newLine, ProductId, Quantity);
		}

		static public string Header(string delimiter = "\t", string newLine = "\r\n")
		{
			return string.Format("ProductId{0}Quantity{1}",
				delimiter, newLine);
		}
	}

	public class SalesRecord
	{
		public string ProductId { get; set; }
		public string CustomerId { get; set; }
		public string Quantity { get; set; }
		public string Date { get; set; }
    public string OrderId { get; set; }
    public int Key { get; set; }

		public SalesRecord() 
		{
			ProductId = "";
			CustomerId = "";
			Quantity = "";
			Date = "";
			OrderId = "";
			Key = 0;
		}

		/// <summary>
		/// used to read sales files created by the extractor
		/// </summary>
		/// <param name="data"></param>
		public SalesRecord(string[] data) 
		{
			if (data.Length != 4)
			{
				ProductId = "";
				CustomerId = "";
				Quantity = "";
				Date = "";
				OrderId = "";
				Key = 0;
				return;
			}

			ProductId = data[0];
			CustomerId = data[1];
			Quantity = data[2];
			Date = data[3];
			OrderId = "";
			Key = 0;
		}

		public string ToString(string delimiter = "\t", string newLine = "\r\n")
		{
			return string.Format("{2}{0}{3}{0}{4}{0}{5}{1}",
				delimiter, newLine, ProductId, CustomerId, Quantity, Date);
		}

		static public string Header(string delimiter = "\t", string newLine = "\r\n")
		{
			return string.Format("ProductId{0}CustomerId{0}Quantity{0}Date{1}",
				delimiter, newLine);
		}
	}

	public class CustomerInfo
	{
		public string Id { get; set; }
		public string Email { get; set; }
		public string AltIds { get; set; } //csv list
		public string Details { get; set; } //json object with name/value pairs

		public CustomerInfo()
		{
		}

		public CustomerInfo(string id, string email, string altIds, List<string> header, List<string> data)
		{
			Id = id;
			Email = email;
			AltIds = altIds;
			SetDetails(header, data);
		}

		public CustomerInfo(CustomerRecord record)
		{
			ParseRecord(record);
		}

		public void ParseRecord(CustomerRecord record)
		{
			Id = record.CustomerId;
			Email = record.Email;
			AltIds = record.AlternativeIDs;

			SetDetails(new List<string> { "Name", "Address", "City", "State", "PostalCode", "Country", "Gender", "Birthday", "AgeRange", "Persona", "DoNotTrack" },
								new List<string> { record.Name, record.Address, record.City, record.State, record.PostalCode, record.Country, record.Gender, record.Birthday, record.AgeRange, record.Persona, record.DoNotTrack });
		}

		public void SetDetails(List<string> header, List<string> data)
		{
			if (header == null || !header.Any(x => !string.IsNullOrEmpty(x))
				|| data == null || !data.Any(x => !string.IsNullOrEmpty(x))) 
			{
				Details = "";
				return;
			}

			//var details = new List<KeyValuePair<string, string>>();
			var details = new Dictionary<string, string>();
			var lastIndex = data.Count - 1;
			for (var i = 0; i < header.Count && i < data.Count; i++)
			{
				var key = header[i];
				var value = data[i];
				if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(value)) continue;
				//details.Add(new KeyValuePair<string, string>(key, value));
				try
				{
					details.Add(key, value);
				}
				catch (Exception ex)
				{ } //duplicate column headers are not allowed
			}
			Details = TableAccess.JsonSerialize(details);
			//Details = "{" + details.Aggregate("", (c, w) => string.Format("{0},{1}", c, string.Format("\"{0}\":\"{1}\"",w.Key, w.Value))) + "}";
		}
	}

	public class CustomerRecord
	{
		public string CustomerId { get; set; }
		public string Name { get; set; }
		public string Email { get; set; }
		public string Address { get; set; }
		public string City { get; set; }
		public string State { get; set; }
		public string PostalCode { get; set; }
		public string Country { get; set; }
		public string Phone { get; set; }
		public string Gender { get; set; }
		public string Birthday { get; set; }
		public string AgeRange { get; set; }
		public string Persona { get; set; }
		public string AlternativeIDs { get; set; }
		public string DoNotTrack { get; set; }

		public string ToString(string delimiter = "\t", string newLine = "\r\n")
		{
			return string.Format("{2}{0}{3}{0}{4}{0}{5}{0}{6}{0}{7}{0}{8}{0}{9}{0}{10}{1}",
				delimiter, newLine, CustomerId, Name, Email, Address, City, State, PostalCode, Country, Gender, Birthday, AgeRange, Persona, AlternativeIDs, DoNotTrack);
		}

		static public string Header(string delimiter = "\t", string newLine = "\r\n")
		{
			return string.Format("CustomerId{0}Name{0}Email{0}Address{0}City{0}State{0}PostalCode{0}Country{0}Gender{0}Birthday{0}AgeRange{0}Persona{0}AlternativeIDs{0}DoNotTrack{1}",
				delimiter, newLine);
		}
	}

	public class EmailOrderRecord
	{
		public string Alias { get; set; }
		public string OrderId { get; set; }
		public string ProductId { get; set; }
		public string CustomerId { get; set; }
		public int Quantity { get; set; }

		public string ToString(string delimiter = "\t", string newLine = "\r\n")
		{
			return string.Format("{2}{0}{3}{0}{4}{0}{5}{0}{6}{1}",
				delimiter, newLine, Alias, OrderId, ProductId, CustomerId, Quantity);
		}

		static public string Header(string delimiter = "\t", string newLine = "\r\n")
		{
			return string.Format("Alias{0}OrderId{0}ProductId{0}CustomerId{0}Quantity{1}",
				delimiter, newLine);
		}
	}

	public class ExclusionRecord
	{
		public string Id { get; set; }

		public ExclusionRecord(string id)
		{
			Id = id;
		}

		public ExclusionRecord(string[] data)
		{
			//no need to check lenth each time --if length is too short it will throw IndexOutOfRange
			//if (data.Count != 1) throw new ArgumentException("data");

			Id = data[0];
		}

		public string ToString(string delimiter = "\t", string newLine = "\r\n")
		{
			return string.Format("{2}{1}", delimiter, newLine, Id);
		}

		static public string Header(string delimiter = "\t", string newLine = "\r\n")
		{
			return string.Format("Id{1}", delimiter, newLine);
		}
	}

	public class ReplacementRecord
	{
		public string OldId { get; set; }
		public string NewId { get; set; }

		public ReplacementRecord(string oldId, string newId)
		{
			OldId = oldId;
			NewId = newId;
		}

		public ReplacementRecord(string[] data)
		{
			//no need to check lenth each time --if length is too short it will throw IndexOutOfRange
			//if (data.Count != 2) throw new ArgumentException("data");

			OldId = data[0];
			NewId = data[1];
		}

		public string ToString(string delimiter = "\t", string newLine = "\r\n")
		{
			return string.Format("{2}{0}{3}{1}", delimiter, newLine, OldId, NewId);
		}

		static public string Header(string delimiter = "\t", string newLine = "\r\n")
		{
			return string.Format("OldId{0}NewId{1}", delimiter, newLine);
		}

		public static List<string> Headers
		{
			get { return new List<string> {"OldId", "NewId"}; }
		}
	}

	public class MigrationMapRecord
	{
		public string ProductId { get; set; }
		public string MapFieldValue { get; set; }

		public MigrationMapRecord(string productId, string mapFieldValue)
		{
			ProductId = productId;
			MapFieldValue = mapFieldValue;
		}

		public MigrationMapRecord(string[] data)
		{
			//if length is too short it will throw IndexOutOfRange (by design)
			ProductId = data[0];
			MapFieldValue = data[1];
		}

		public string ToString(string delimiter = "\t", string newLine = "\r\n")
		{
			return string.Format("{2}{0}{3}{1}", delimiter, newLine, ProductId, MapFieldValue);
		}

		static public string Header(string delimiter = "\t", string newLine = "\r\n")
		{
			return string.Format("ProductId{0}MapFieldValue{1}", delimiter, newLine);
		}

		public static List<string> Headers
		{
			get { return new List<string> { "ProductId", "MapFieldValue" }; }
		}
	}

	public class GeneratorFeaturedRec : IComparable
	{
		public string PrimaryId { get; set; }
		public string RecommendedId { get; set; }
		public int Ranking { get; set; }

		public string ToString(string delimiter = "\t", string newLine = "\r\n")
		{
			//depricated when generator switched to rankings
			//float likelihood = Ranking == 0 ? 0 : (100F - Ranking) / 100F;
			//return string.Format("{2}{0}{3}{0}{4}{1}", delimiter, newLine, PrimaryId, RecommendedId, likelihood);

			return string.Format("{2}{0}{3}{0}{4}{1}", delimiter, newLine, PrimaryId, RecommendedId, Ranking); 
		}

		static public string Header(string delimiter = "\t", string newLine = "\r\n")
		{
			//depricated when generator switched to rankings
			//return string.Format("ProductId{0}RecId{0}Likelihood{1}", delimiter, newLine);
			
			return string.Format("ProductId{0}RecId{0}Ranking{1}", delimiter, newLine);
		}

		// Implement IComparable CompareTo method - provide default sort order.
		int IComparable.CompareTo(object obj)
		{
			var a = (GeneratorFeaturedRec)obj;
			if (!PrimaryId.Equals(a.PrimaryId))
				throw new Exception("Primary Ids must match before sorting");
			return Ranking < a.Ranking ? -1 : Ranking > a.Ranking ? 1 : 0;
		}
	}

	public class GeneratorFeaturedTopSellRec : IComparable
	{
		public string ProductId { get; set; }
		public int Ranking { get; set; }

		public string ToString(string delimiter = "\t", string newLine = "\r\n")
		{
			return string.Format("{2}{0}{3}{1}", delimiter, newLine, ProductId, Ranking);
		}

		static public string Header(string delimiter = "\t", string newLine = "\r\n")
		{
			return string.Format("ProductId{0}Ranking{1}", delimiter, newLine);
		}

		// Implement IComparable CompareTo method - provide default sort order.
		int IComparable.CompareTo(object obj)
		{
			var a = (GeneratorFeaturedTopSellRec)obj;
			return Ranking < a.Ranking ? -1 : Ranking > a.Ranking ? 1 : 0;
		}
	}

	#endregion

} //namespace