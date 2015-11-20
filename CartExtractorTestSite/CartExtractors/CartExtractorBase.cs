using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using _4_Tell.CommonTools;
#if !CART_EXTRACTOR_TEST_SITE
using _4_Tell.DashService;
#endif
using _4_Tell.IO;
using _4_Tell.Logs;

namespace _4_Tell.CartExtractors
{
	/// <summary>
	/// Abstract base class for shopping cart data extractor classes
	/// </summary>
	public abstract class CartExtractor
	{
		#region Support Classes

		//FeedType Options:
		// 1. single feed uri with query parameters to allow specific requests (SingleApi --preferred)
		// 2. single feed uri with all data in separate xml sections (CombinedFeed)
		// 3. separate feed uri for each data type (IndividualFeeds)

		protected enum FeedType
		{
			SingleApi,
			CombinedFeed,
			IndividualFeeds,
			None
		}

		public enum ExtractType
		{
			None,
			Full,
			Update,
			Catalog,
			Inventory,
			Sales,
			Customers,
#if DEBUG
			Test,
#endif
			GenerateOnly
		}

		/// <summary>
		/// ParentItem is used to map child price ranges to the parent 
		/// </summary>
		public class ParentItem
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
		/// ChildItem is a simple struct to hold child details to accumulate into parent
		/// </summary>
		protected struct ChildItem
		{
			public string Id;
			public int Inventory;
			public float Price;
			public float SalePrice;
			public float ListPrice;
			public float Cost;
			public float Rating;
		}

		/// <summary>
		/// A StreamReader that excludes XML-illegal characters while reading.
		/// </summary>
		public class XmlSanitizingStream : StreamReader
		{
			/// <summary>
			/// The charactet that denotes the end of a file has been reached.
			/// </summary>
			private const int EOF = -1;

			/// <summary>Create an instance of XmlSanitizingStream.</summary>
			/// <param name="streamToSanitize">
			/// The stream to sanitize of illegal XML characters.
			/// </param>
			public XmlSanitizingStream(Stream streamToSanitize)
				: base(streamToSanitize, true)
			{ }

			/// <summary>
			/// Get whether an integer represents a legal XML 1.0 or 1.1 character. See
			/// the specification at w3.org for these characters.
			/// </summary>
			/// <param name="character"> the character to be checked for validity</param>
			/// <param name="v1_0">
			/// A switch to specify whether to use XML 1.0 character
			/// validation, or XML 1.1 character validation.
			/// </param>
			public static bool IsLegalXmlChar(int character, bool v1_0 = true)
			{
				if (!v1_0) // http://www.w3.org/TR/xml11/#charsets
				{
					return
					!(
							character <= 0x8 ||
							character == 0xB ||
							character == 0xC ||
						(character >= 0xE && character <= 0x1F) ||
						(character >= 0x7F && character <= 0x84) ||
						(character >= 0x86 && character <= 0x9F) ||
							character > 0x10FFFF
					);
				}

				return
					(
							character == 0x9 /* == '\t' == 9   */          ||
							character == 0xA /* == '\n' == 10  */          ||
							character == 0xD /* == '\r' == 13  */          ||
						(character >= 0x20 && character <= 0xD7FF) ||
						(character >= 0xE000 && character <= 0xFFFD) ||
						(character >= 0x10000 && character <= 0x10FFFF)
					);
			}

			public override int Read()
			{
				// Read each character, skipping over characters that XML has prohibited

				int nextCharacter;

				do
				{
					// Read a character

					if ((nextCharacter = base.Read()) == EOF)
					{
						// If the character denotes the end of the file, stop reading

						break;
					}
				}

				// Skip the character if it's prohibited, and try the next

				while (!IsLegalXmlChar(nextCharacter));

				return nextCharacter;
			}

			public override int Peek()
			{
				// Return the next legl XML character without reading it 

				int nextCharacter;

				do
				{
					// See what the next character is 

					nextCharacter = base.Peek();
				}
				while
				(
					// If it's prohibited XML, skip over the character in the stream
					// and try the next.

					!IsLegalXmlChar(nextCharacter) &&
					(nextCharacter = base.Read()) != EOF
				);

				return nextCharacter;

			} // method

			#region Read*() method overrides

			// The following methods are exact copies of the methods in TextReader, 
			// extracting by disassembling it in Refelctor

			public override int Read(char[] buffer, int index, int count)
			{
				if (buffer == null)
				{
					throw new ArgumentNullException("buffer");
				}
				if (index < 0)
				{
					throw new ArgumentOutOfRangeException("index");
				}
				if (count < 0)
				{
					throw new ArgumentOutOfRangeException("count");
				}
				if ((buffer.Length - index) < count)
				{
					throw new ArgumentException();
				}
				int num = 0;
				do
				{
					var num2 = Read();
					if (num2 == -1)
					{
						return num;
					}
					buffer[index + num++] = (char)num2;
				}
				while (num < count);
				return num;
			}

			public override int ReadBlock(char[] buffer, int index, int count)
			{
				int num;
				int num2 = 0;
				do
				{
					num2 += num = Read(buffer, index + num2, count - num2);
				}
				while ((num > 0) && (num2 < count));
				return num2;
			}

			public override string ReadLine()
			{
				var builder = new StringBuilder();
				while (true)
				{
					var num = Read();
					switch (num)
					{
						case -1:
							return builder.Length > 0 ? builder.ToString() : null;

						case 13:
						case 10:
							if ((num == 13) && (Peek() == 10))
							{
								Read();
							}
							return builder.ToString();
					}
					builder.Append((char)num);
				}
			}

			public override string ReadToEnd()
			{
				int num;
				var buffer = new char[0x1000];
				var builder = new StringBuilder(0x1000);
				while ((num = Read(buffer, 0, buffer.Length)) != 0)
				{
					builder.Append(buffer, 0, num);
				}
				return builder.ToString();
			}

			#endregion

		} // class

		#endregion

		#region Internal Params
		protected const string DataVersion = "3"; //data file format version
		protected const int MinLockTime = 60; //Minumum time allowed between successful extractor starts
		protected const int FastDurationLimit = 30; //longer than 30 min duration uses slow generator queue
		public const string ExcludedCategoryCause = "Excluded Category";
		public const string MissingImageCause = "Missing Image";
#if !CART_EXTRACTOR_TEST_SITE
        protected RestAccess BoostService;
#endif
		protected static readonly MD5 Hash = MD5.Create();

		protected BoostLog Log = BoostLog.Instance;

		public SiteRules Rules;
		public readonly string Alias;
		protected string DataReadPath; //can't be readonly since adjusted by SetMigrationSlave
		protected readonly string DataWritePath;
		protected bool HasCredentials;

		public CatalogHandler Catalog { get; set; }
		protected InventoryHandler Inventory { get; set; }
		protected SalesHandler Sales { get; set; }
		protected CustomerHandler Customers { get; set; }
		protected AttributeHandler CategoryNames { get; set; }
		protected AttributeHandler BrandNames { get; set; }
		protected AttributeHandler DepartmentNames { get; set; }

		protected IEnumerable<XElement> _categoryXml { get; set; }
		protected List<ProductRecord> Products { get; set; }
		public List<ExclusionRecord> Exclusions { get; private set; }
		public FeaturedRecommendations FeaturedCrossSells { get; private set; }
		public FeaturedRecommendations FeaturedUpSells { get; private set; }
		public Dictionary<string, ParentItem> ParentProducts { get; private set; }  //key is product id 
		public Dictionary<string, List<string>> AltPrices { get; private set; }  //key is product id and value is a list of alternate prices
		public Dictionary<string, List<string>> AltPageLinks { get; private set; }  //key is product id and value is a list of alternate page links
		public Dictionary<string, List<string>> AltImageLinks { get; private set; }  //key is product id and value is a list of alternate image links
		public Dictionary<string, List<string>> AltTitles { get; private set; }  //key is product id and value is a list of alternate titles
		public Dictionary<string, string> Departments { get; private set; }  //key is product id and value is a list of alternate prices
		public Dictionary<string, string> ExclusionCauses { get; private set; }
		public Dictionary<string, int> ExclusionStats { get; private set; }
		public List<ReplacementRecord> Replacements { get; set; }

		protected CartWebClient _feedClient = null;
		private Dictionary<DataGroup, FeedType> _feedTypes;
		private Dictionary<DataGroup, string> _feedUrls;
		private long _catalogFileSeekPosition;
		protected XDocument _combinedFeed = null;
		protected DateTime _lastFeedTime = DateTime.MinValue;
		protected int _feedRefreshTime = 120; //default to two hours
		//protected CartExtractor _migrationCart = null;
		protected bool _migrationSlave = false;
#if DEBUG
		protected DataGroup _lastJsonGroup = DataGroup.All;
#endif
	
		#endregion

		#region External Params

		public static string CommonHeader //first header line in export file (set date dynamically each time it is used)
		{
			get { return String.Format("Version\t{0}\t{1}\r\n", DataVersion, DateTime.Now.ToShortDateString()); }
		}
		//filenames
		//Optional Input Files
		public const string ManualReplacementFilename = "ManualReplacements.txt";  //OldId<tab>NewId<cr-lf>
		//Output Files for Generator
		public const string CatalogFilename = "Catalog.txt";
		public const string SalesFilenameFormat = "Sales-{0}.txt";
		public const string ActionsFilenameFormat = "AutoActionsLog-{0}.txt";
		public const string ClickStreamFilenameFormat = "AutoClickStreamLog-{0}.txt";
		public const string CustomerFilenameFormat = "Customers_x-{0}.txt";
		public const string Att1Filename = "Attribute1Names.txt";
		public const string Att2Filename = "Attribute2Names.txt";
		public const string ExclusionFilename = "Exclusions.txt";
		public const string ReplacementFilename = "Replacements.txt";
		public const string PromotionsFilename = "Promotions.txt";
		public const string FeaturedCrossSellFilename = "FeaturedCrossSell_x.txt";
		public const string FeaturedUpSellFilename = "FeaturedUpSell_x.txt";
		public const string FeaturedTopSellFilename = "FeaturedTopSell_x.txt";
		//Output Files for Boost Service and/or Dash Service
		public const string AltPriceFilename = "AltPrices.txt";
		public const string AltPageFilename = "AltPageLinks.txt";
		public const string AltImageFilename = "AltImageLinks.txt";
		public const string AltTitleFilename = "AltTitles.txt";
		public const string InventoryFilename = "Inventory.txt";
		public const string ExclusionCauseFilename = "ExclusionCauses.txt";
		//Internal Temporary Files
		public const string DepartmentFilename = "DepartmentNames.txt";

		public bool IsExtractorQueued { get; private set; }
		public bool IsExtracting { get; private set; }
		public int TotalSales { get; private set; }
		public int TotalCustomers { get; private set; }
		//public Dictionary<string, string> MigrationSubMap = null;
		//public Dictionary<string, string> MigrationFullMap = null;
		public ExtractorProgress Progress; 

		#endregion

		#region Base Methods

		private CartExtractor()
		{
			throw new Exception("CartExtractor cannot use a default constructor");
		}

		protected CartExtractor(SiteRules rules)
		{
			if (rules == null) throw new NoNullAllowedException("CartExtractor Rules cannot be null");
			Rules = rules;
			Alias = Rules.Alias;
			DataReadPath = IO.DataPath.Instance.ClientDataPath(ref Alias, true);
			DataWritePath = IO.DataPath.Instance.ClientDataPath(Alias, true);
			Progress = new ExtractorProgress();
			IsExtractorQueued = false;
			IsExtracting = false;

#if !CART_EXTRACTOR_TEST_SITE
			BoostService = RestAccess.Instance;
			FeaturedCrossSells = new FeaturedRecommendations();
			FeaturedUpSells = new FeaturedRecommendations();
#endif
			ExclusionStats = new Dictionary<string, int>();
			Catalog = new CatalogHandler(Rules, this, Progress);
			Inventory = new InventoryHandler(Rules, this, Progress);
			Sales = new SalesHandler(Rules, this, Progress);
			Customers = new CustomerHandler(Rules, this, Progress);
			CategoryNames = new AttributeHandler(Rules, this, Progress, DataGroup.CategoryNames);
			BrandNames = new AttributeHandler(Rules, this, Progress, DataGroup.ManufacturerNames);
			DepartmentNames = new AttributeHandler(Rules, this, Progress, DataGroup.DepartmentNames);

			//TODO: Move all below to the CatalogHandler class or depricate
			Exclusions = new List<ExclusionRecord>();
			Replacements = new List<ReplacementRecord>();
			ParentProducts = new Dictionary<string, ParentItem>();
			AltPrices = new Dictionary<string, List<string>>();
			AltPageLinks = new Dictionary<string, List<string>>();
			AltImageLinks = new Dictionary<string, List<string>>();
			AltTitles = new Dictionary<string, List<string>>();
			Departments = new Dictionary<string, string>();
			ExclusionCauses = new Dictionary<string, string>();
		}

#if !CART_EXTRACTOR_TEST_SITE
        /// <summary>
		/// Return the rules related to the cart, telling what type of extraction and upload options are valid
		/// These are general rules related to the cart type, not specific to the client.
		/// </summary>
		/// <returns>DashCartDefinition</returns>
		public static DashCartDefinition GetCartRules(CartType cartType)
		{
			//note, this is defined here instead of using an abstract method 
			//so that cart rules can exist for cart types that don't have extractors

			var rules = new DashCartDefinition{ CartId = (int)cartType, CartValue = cartType.ToString() };
			switch (cartType)
			{
				case CartType.BigCommerce:
					rules.CartDisplayName = "BigCommerce";
					rules.HasCartExtractor = true;
					rules.CanExtractCatalog = true;
					rules.CanExtractAllSales = true;
					rules.CanExtractSalesUpdate = false;
					rules.HasManualUpload = true;
					rules.HasManualUploadLink = false;
					break;
				case CartType.CommerceV3:
					rules.CartDisplayName = "Commerce V3";
					rules.HasCartExtractor = true;
					rules.CanExtractCatalog = true;
					rules.CanExtractAllSales = true;
					rules.CanExtractSalesUpdate = false;
					rules.HasManualUpload = true;
					rules.HasManualUploadLink = false;
					break;
				case CartType.MivaMerchant:
					rules.CartDisplayName = "MivaMerchant";
					rules.HasCartExtractor = true;
					rules.CanExtractCatalog = true;
					rules.CanExtractAllSales = true;
					rules.CanExtractSalesUpdate = true;
					rules.HasManualUpload = true;
					rules.HasManualUploadLink = false;
					break;
				case CartType.ThreeDCart:
					rules.CartDisplayName = "3dcart";
					rules.HasCartExtractor = true;
					rules.CanExtractCatalog = true;
					rules.CanExtractAllSales = true;
					rules.CanExtractSalesUpdate = false;
					rules.HasManualUpload = true;
					rules.HasManualUploadLink = false;
					break;
				case CartType.Volusion:
					rules.CartDisplayName = "Volusion";
					rules.HasCartExtractor = true;
					rules.CanExtractCatalog = true;
					rules.CanExtractAllSales = true;
					rules.CanExtractSalesUpdate = false;
					rules.HasManualUpload = true;
					rules.HasManualUploadLink = false;
					break;
                case CartType.Shopify:
                    rules.CartDisplayName = "Shopify";
                    rules.HasCartExtractor = true;
                    rules.CanExtractCatalog = true;
                    rules.CanExtractAllSales = true;
                    rules.CanExtractSalesUpdate = true;
                    rules.HasManualUpload = true;
                    rules.HasManualUploadLink = false;
                    break;
                case CartType.NetSuite:
                    rules.CartDisplayName = "NetSuite";
                    rules.HasCartExtractor = true;
                    rules.CanExtractCatalog = true;
                    rules.CanExtractAllSales = true;
                    rules.CanExtractSalesUpdate = true;
                    rules.HasManualUpload = true;
                    rules.HasManualUploadLink = false;
                    break;
                case CartType.JsonFeed:
					rules.CartDisplayName = "JsonFeed";
					rules.HasCartExtractor = true;
					rules.CanExtractCatalog = true;
					rules.CanExtractAllSales = true;
					rules.CanExtractSalesUpdate = true;
					rules.HasManualUpload = true;
					rules.HasManualUploadLink = false;
					break;
				case CartType.XmlFeed:
					rules.CartDisplayName = "XmlFeed";
					rules.HasCartExtractor = true;
					rules.CanExtractCatalog = true;
					rules.CanExtractAllSales = true;
					rules.CanExtractSalesUpdate = true;
					rules.HasManualUpload = true;
					rules.HasManualUploadLink = false;
					break;
				case CartType.TabbedFeed:
				case CartType.osCommerce:
				case CartType.Magento:
				case CartType.PrestaShop:
				case CartType.WebsitePipeline:
				case CartType.Other:
				default:
					rules.CartDisplayName = cartType.ToString();
					rules.HasCartExtractor = false;
					rules.CanExtractCatalog = false;
					rules.CanExtractAllSales = false;
					rules.CanExtractSalesUpdate = false;
					rules.HasManualUpload = true;
					rules.HasManualUploadLink = false;
					break;
			}
			return rules;
		}
#endif

		public bool SetRules(SiteRules rules)
		{
			if (!rules.Alias.Equals(Rules.Alias)) return false; //can't change the alias
			Rules = rules;
			//FillDefaultFieldNames();
			//Rules.InitFieldNames();
			return true;
		}

		public void GetData(SiteRules.ExtractorSchedule ut)
		{
			GetData(ut.ExtractType);
		}

		/// <summary>
		/// Extract data from the cart
		/// </summary>
		/// <param name="extractType"></param>
		/// <returns>true if successful</returns>
		public bool GetData(ExtractType extractType)
		{
			//choices are update only or full export
			//all normal exports include catalog and related files (exclusions, attNames, etc)
			//full export also includes all sales files (could be pulling from xml depending on the cart)
			//update will include the current sales month only if Rules.ExtractSalesUpdate is true for this cart

			if (Rules == null)
			{
				if (Log != null)
					Log.WriteEntry(EventLogEntryType.Information, "Attempt to GetData with Rules null", "", Alias);
				return false;
			}
			IsExtracting = true;
			var dynamicUpdate = false;
			var totalTime = new StopWatch(true);
			var pullType = "new";

			//check migration status
			_migrationSlave =  Rules.MigrationRules != null && Rules.MigrationRules.Enabled 
													&& !Rules.MigrationRules.IsMigrationMaster;
			if (_migrationSlave)
			{
				pullType = "Migration";
				Progress.IsSlave = true;
			}
			var result = String.Format("Pulling {0} data for {1} from {2}", pullType, Alias, Rules.CartName);
			var details = "";
			if (Log != null)
				Log.WriteEntry(EventLogEntryType.Information, result, "", Alias);
			Progress.Start(result);

			var catalogCount = 0;
			try
			{
				var generateTables = true;
				switch (extractType)
				{				
					case ExtractType.Full:
						if (Rules.ExtractSalesFull) GetAllSales();
						if (Rules.ExtractCustomerData) GetAllCustomers();
						catalogCount = GetCatalog(true);
						break;
					case ExtractType.Update:
						if (Rules.ExtractSalesUpdate) GetSalesUpdate();
						if (Rules.ExtractCustomerData) GetCustomerUpdate();
						catalogCount = GetCatalog();
						break;
					case ExtractType.Sales:
						if (Rules.ExtractSalesFull || Rules.ExtractSalesFromXmlFile) GetAllSales();
						else if (Rules.ExtractSalesUpdate) GetSalesUpdate();
						else throw new Exception("Sales extraction is not supported for this site.");
						break;
					case ExtractType.Customers:
						if (Rules.ExtractCustomerData) GetAllCustomers();
						else throw new Exception("Customer extraction is not supported for this site.");
						break;
					case ExtractType.Inventory:
						GetInventory();
						dynamicUpdate = true;
						generateTables = false; //inventory does not require generator
						break;
					case ExtractType.Catalog:
						catalogCount = GetCatalog();
						break;
#if DEBUG
					case ExtractType.Test:
						RunTest();
						break;
#endif
					case ExtractType.GenerateOnly:
						break;
				}
				//check exclusion causes
				var exCauses = "";
				if (ExclusionStats != null && ExclusionStats.Any())
				{
					//only save to rules when extraction is completed
					Rules.ExclusionStats = new Dictionary<string,int>(ExclusionStats);

					//exclusionStats.AddRange(dashSite.Rules.ExclusionStats.Select(c => new List<string> { c.Key, c.Value.ToString("N0") }));
					exCauses = "\n\n" + ExclusionStats
																.Aggregate("Exclusion Causes:", (a, b) => String.Format("{0}\n   {1} = {2}", a, b.Key, b.Value.ToString("N0")));

					//check for excessive missing images
					int causeCount;
					if (Log != null && ExclusionStats.TryGetValue(MissingImageCause, out causeCount)
						&& catalogCount > 0 && ((float)causeCount / catalogCount) * 100F > Rules.MissingImageThreshold) //defaults to 10%
						throw new Exception(string.Format("Error: {0} of {1} items are excluded due to missing images.",
																				causeCount.ToString("N0"), catalogCount.ToString("N0")),
																				new Exception(exCauses));
				}

				totalTime.Stop();
				if (dynamicUpdate)
				{
					Rules.LastDynamicUpdateType = extractType;
					Rules.LastDynamicUpdateTime = DateTime.Now;
				}
				else
				{
					Rules.LastExtractionType = extractType;
					Rules.LastExtractionTime = DateTime.Now;
					Rules.LastExtractorDuration = (int)Math.Ceiling(totalTime.ElapsedMinutes);
				}
				if (!_migrationSlave)
				{
#if !CART_EXTRACTOR_TEST_SITE
                    if (generateTables)
					{
						QueueGenerator();
						result = string.Format("Successful update --pending Generator task{0}", exCauses);
					}
					else
#endif
                        result = string.Format("Successful {0} update{1}", extractType, exCauses);
				}
				Progress.End(true, result);
				return true;
			}
			catch (ThreadAbortException)
			{
				result = "Update canceled by user";
				return false;
			}
			catch (Exception ex)
			{
				result = ex.Message;
				details = ex.InnerException == null ? "" : "Inner Exception: " + ex.InnerException.Message;
				if (Log != null)
					Log.WriteEntry(EventLogEntryType.Information, result, details, Alias, true); //send support alert
				return false;
			}
			finally
			{
				if (!_migrationSlave)
				{
					Rules.QueueSettings(false); //save the extraction time and duration
					ReleaseGlobalData();
					ReleaseCartData();
				}
				IsExtracting = false;
				if (Progress.Started) Progress.End(false, result);
				if (Log != null)
				{
					Log.WriteEntry(EventLogEntryType.Information, Progress.Text, details, Alias);
				}
			}
		}

#if DEBUG
		private void RunTest()
		{
			var extraFields = "p.ProductID,pv.Price,AltPrice1,AltPageLink1";
			string altFeedUrl = "http://webadapters.channeladvisor.com/CSEAdapter/Default.aspx?pid=YZP%5e%5eI%5cRA%3bHJ%40fW%2bQ%5bdwd%27QjEN%40_%25-P5%5bH%5e%25YjANr7X%60VKaC2%25%27gCM%3dj*%2f%24%5bbG%5dWQ8FMu_V-%24%5e2JbSU%3dwG";
			int _storeId = 1;
			int maxRows = 10000;
			int startingRow = 1;
			string[] rows;

			////initialize extra field list
			//List<string> extraFieldList = string.IsNullOrEmpty(extraFields) ? new List<string>() :
			//                      extraFields.Split(new[] { ',' }).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
			//initialize extra field list
			List<string> extraFieldList = new List<string>();
			if (!string.IsNullOrEmpty(extraFields))
			{
				var tempList = extraFields.Split(new[] { ',' });
				foreach (var e in tempList)
				{
					if (string.IsNullOrEmpty(e.Trim())) continue;
					extraFieldList.Add(e);
				}
			}
			//remove standard fields
			var standardFields = new List<string>
															{
																"p.ProductID", 
																"p.SKU", 
																"p.Name", 
																"p.Deleted", 
																"p.Published", 
																"pv.Price", 
																"pv.SalePrice", 
																"pv.MSRP", 
																"pv.Cost", 
																"pv.Inventory",
																"pm.ManufacturerID",
																"pc.CategoryID",
																"ps.SectionID",
																"ProductId",
																"Name",
																"Att1Id",
																"Att2Id",
																"Price",
																"SalePrice",
																"ListPrice",
																"Cost",
																"Inventory",
																"Visible",
																"Link",
																"ImageLink",
																"Rating",
																"StandardCode",
																"Departments",
																"CategoryList",
																"ManufacturerID",
																"SectionList"
															};
			extraFieldList.RemoveAll(x => standardFields.Contains(x));



				//-------begin: CA Alt Feed Handling-------
				//Read alternate eBay prices and links from separate CA feed
				string[] altHeader = null;
				var altFeedLookup = new Dictionary<string, List<string>>();
				//string altFeedUrl = AppLogic.AppConfig("4Tell.CaFeedUrl");
				var caFeedExists = !string.IsNullOrEmpty(altFeedUrl);

				if (caFeedExists)
				{
					//Note: could make these config parameters
					var colEnd = new[] { '\t' };
					var rowEnd = new[] { '\n' };
					var trimRow = new[] { '\r' };
					string feedData = null;
					using (var feed = new WebClient())
					{
						feed.Encoding = Encoding.UTF8;
						feed.BaseAddress = "";
						feed.Proxy = null;
						feed.Headers.Add("user-agent", "Mozilla/5.0 (Windows NT 6.1; rv:18.0) Gecko/20100101 Firefox/18.0");
						feed.Headers.Add("accept", "*/*");
						feed.Headers.Add("content-type", "application/json");
						using (var data = feed.OpenRead(altFeedUrl))
						{
							if (data != null)
							{
								using (var reader = new StreamReader(data))
								{
									feedData = reader.ReadToEnd();
								}
							}
						}
					}

					//parse into dictionary for lookup below
					if (!string.IsNullOrEmpty(feedData))
					{
						var feedRows = feedData.Split(rowEnd);
						var rowCount = feedRows.Length;
						if (rowCount > 1)
						{
							altHeader = feedRows[0].Trim(trimRow).Split(colEnd);
							var columnCount = altHeader.Length;
							for (var i = 1; i < rowCount; i++)
							{
								var columns = feedRows[i].Trim(trimRow).Split(colEnd);
								if (columns.Length != columnCount) continue;
								List<string> data;
								if (!altFeedLookup.TryGetValue(columns[0], out data))
								{
									data = new List<string>();
									for (var j = 1; j < columnCount; j++)
										data.Add(columns[j]);
									altFeedLookup.Add(columns[0], data);
								}
							}
						}
					}
					if (altFeedLookup.Any())
					{
						//remove alt fields from the extrafieldList
						extraFieldList.RemoveAll(x => altHeader.Contains(x));
						//and add then to the catalog header list
						//for (var i = 1; i < altHeader.Length; i++) ProductRecord.AddField(altHeader[i]);
					}
					else //no data
						caFeedExists = false;
				}
				//-------end: CA Alt Feed Handling-------


			//throw new Exception("made it here");
			//add extra fields to the output catalog header
			//foreach (var f in extraFieldList) ProductRecord.AddField(f);

			//setup database query (and adjust extraFields)
			var totalRows = 50000;// GetRowCount(DataGroup.Catalog);
			var query = "SELECT";
			if (maxRows > 0)
				query += string.Format(" TOP {0} * FROM (SELECT TOP {1}", maxRows, totalRows - startingRow);

			query += " p.ProductID, p.SKU, p.Name, p.Deleted, p.Published, pv.Price, pv.SalePrice, pv.MSRP, pv.Cost, pv.Inventory,"
								+ " isnull(pm.ManufacturerID, 0) as ManufacturerID, "
								+ " stuff((select ',' + cast(pc.CategoryID as varchar)"
								+ "    FROM ProductCategory pc WHERE p.ProductID = pc.ProductID"
								+ "    FOR xml path('')),1,1,'') as CategoryList,"
								+ " stuff((select ',' + cast(ps.SectionID as varchar)"
								+ "    FROM ProductSection ps WHERE p.ProductID = ps.ProductID"
								+ "    FOR xml path('')),1,1,'') as SectionList";
			if (extraFieldList.Any())
			{
				foreach (var f in extraFieldList) query += ", " + f;
			}
			query += " from Product p inner join ProductVariant pv on p.ProductID = pv.ProductID and pv.IsDefault = 1";
			if (_storeId > 0)
				query += string.Format(" inner join ProductStore ps on p.ProductID = ps.ProductID and ps.StoreID = {0}", _storeId);
			query += " left join ProductManufacturer pm on p.ProductID = pm.ProductID";
			if (maxRows > 0)
				query += " ORDER BY p.ProductID DESC) as sub ORDER BY sub.ProductID ASC";
			else
				query += " ORDER BY p.ProductID ASC";

		}
#endif

#if !CART_EXTRACTOR_TEST_SITE
		private void QueueGenerator()
		{
			Progress.StartTable("Generator Task", "", "");

			//base slow/fast and lockout minutes decision on combined last extractor/generator time (when they exist)
			var slow = false;
			if (Rules.LastExtractorDuration > 0 && Rules.LastGeneratorDuration > 0)
			{
				//both durations are valid so recalc settings
				var lastTime = Rules.LastExtractorDuration + Rules.LastGeneratorDuration;
				slow =  lastTime > FastDurationLimit;
				if (slow)
					Rules.ExtractorLockoutMinutes = (int)(Math.Ceiling((double)lastTime / (double)FastDurationLimit) * MinLockTime);
				else if (Rules.ExtractorLockoutMinutes > MinLockTime)
					Rules.ExtractorLockoutMinutes = MinLockTime;
			}
			else
					slow = Rules.ExtractorLockoutMinutes > 60;

			var status = (GeneratorQueue.Instance != null && GeneratorQueue.Instance.AddToQueue(Alias, true, slow)) ?
				"Queued" : "Unable to queue";

			Progress.EndTable(-1, status);
		}
#endif
        
        private void GetSalesUpdate()
		{
			var exportDate = DateTime.Now;
			var filename = String.Format(SalesFilenameFormat, exportDate.ToString("yyyy-MM"));
			Progress.StartTable(filename, "items");
			int itemCount;
			TotalSales = 0;
			var status = GetSalesMonth(exportDate, filename, out itemCount);
			Progress.EndTable(itemCount, status);
			TotalSales += itemCount;
		}

		private void GetAllSales()
		{
			var exportDate = DateTime.Now;
			var itemCount = -1;
			TotalSales = 0;

			if (_migrationSlave)
			{
				//Get old customer data from "SalesMonths" to migration date
				var firstDate = DateTime.Now.AddMonths(-1 * Rules.SalesMonthsToExport);
				var lastDate = Rules.MigrationRules.StartDate;
				exportDate = new DateTime(firstDate.Year, firstDate.Month, 1);
				while (exportDate < lastDate)
				{
					//don't replace final month if new data is more relevant
					if (lastDate.Day < CatalogMigration.MigrationCutoffDay 
							&& exportDate.Year == lastDate.Year && exportDate.Month == lastDate.Month) break;

					//create filename and add to the upload list for ConfigBoost
					var filename = String.Format(SalesFilenameFormat, exportDate.ToString("yyyy-MM"));
					itemCount = -1;
					Progress.StartTable("Migration Sales " + filename, "items");
					var status = "";
					if (Rules.MigrationRules.Use4TellSales)
					{
						List<SalesRecord> sales;
						if (TableAccess.Instance.ReadTable(filename, Alias, out sales, true))
						{
							MigrateSlaveOrders(ref sales);
							itemCount = sales.Count;
							Progress.UpdateTable(itemCount, -1, "Writing table");
							status = TableAccess.Instance.WriteTable(Alias, filename, sales);
						}
						else itemCount = 0;
					}
					else
						status = GetSalesMonth(exportDate, filename, out itemCount);
					Progress.EndTable(itemCount, status);
					TotalSales += itemCount;

					//move forward one month for next loop
					exportDate = exportDate.AddMonths(1);
				}
				return;
			}


			for (var month = 0; month < Rules.SalesMonthsToExport; month++)
			{
				//create filename and add to the upload list for ConfigBoost
				var filename = String.Format(SalesFilenameFormat, exportDate.ToString("yyyy-MM"));
				Progress.StartTable(filename, "items");
				var status = GetSalesMonth(exportDate, filename, out itemCount);
				Progress.EndTable(itemCount, status);
				TotalSales += itemCount;
				
				//move back one month for next loop
				exportDate = exportDate.AddMonths(-1);

				//check migration rules
				if (Rules.MigrationRules == null || !Rules.MigrationRules.Enabled || _migrationSlave) continue;
				var lastDate = Rules.MigrationRules.StartDate;
				if (exportDate < lastDate
					|| (exportDate.Year == lastDate.Year && exportDate.Month == lastDate.Month && lastDate.Day < CatalogMigration.MigrationCutoffDay)) 
					break; //earlier sales will be pulled from migration platform
			}
		}

		private void GetCustomerUpdate()
		{
			var exportDate = DateTime.Now;
			var filename = String.Format(CustomerFilenameFormat, exportDate.ToString("yyyy-MM"));
			Progress.StartTable(filename, "customers");
			int itemCount;
			TotalCustomers = 0;
			var status = GetCustomers(exportDate, filename, out itemCount);
			Progress.EndTable(itemCount, status);
			TotalCustomers += itemCount;
		}

		private void GetAllCustomers()
		{
			var exportDate = DateTime.Now;
			var itemCount = -1;
			TotalCustomers = 0;

			if (_migrationSlave)
			{
				//Get old customer data from "SalesMonths" to migration date
				var firstDate = DateTime.Now.AddMonths(-1 * Rules.SalesMonthsToExport);
				var lastDate = Rules.MigrationRules.StartDate;
				exportDate = new DateTime(firstDate.Year, firstDate.Month, 1);
				while (exportDate < lastDate)
				{
					//don't replace final month if new data is more relevant
					if (lastDate.Day < 15 && exportDate.Year == lastDate.Year && exportDate.Month == lastDate.Month) break;

					//create filename and add to the upload list for ConfigBoost
					var filename = String.Format(CustomerFilenameFormat, exportDate.ToString("yyyy-MM"));
					itemCount = -1;
					Progress.StartTable("Migration Customers " + filename, "items");
					var status = GetCustomers(exportDate, filename, out itemCount);
					Progress.EndTable(itemCount, status);
					TotalCustomers += itemCount;

					//move forward one month for next loop
					exportDate = exportDate.AddMonths(1);
				}
				return;
			}

			for (var month = 0; month < Rules.CustomerMonthsToExport; month++)
			{
				//create filename and add to the upload list for ConfigBoost
				var filename = String.Format(CustomerFilenameFormat, exportDate.ToString("yyyy-MM"));
				Progress.StartTable(filename, "customers");
				var status = GetCustomers(exportDate, filename, out itemCount);
				Progress.EndTable(itemCount, status);
				TotalCustomers += itemCount;

				//move back one month for next loop
				exportDate = exportDate.AddMonths(-1);

				//check migration rules
				if (Rules.MigrationRules == null || !Rules.MigrationRules.Enabled || _migrationSlave) continue;
				var lastDate = Rules.MigrationRules.StartDate;
				if (exportDate < lastDate
					|| (exportDate.Year == lastDate.Year && exportDate.Month == lastDate.Month && lastDate.Day < CatalogMigration.MigrationCutoffDay))
					break; //earlier sales will be pulled from migration platform
			}
		}

		private void GetInventory()
		{
			Progress.StartTable("Inventory", "items");
			int itemCount;
			var status = GetInventory(out itemCount);
			Progress.EndTable(itemCount, status);
		}

		protected int GetCatalog(bool fullExtraction = false)
		{
			int itemCount, catalogCount;
			string status;

			//clear the exclusion causess before catalog extraction starts
			ExclusionCauses.Clear();
			ExclusionStats.Clear();
			if (Rules.MigrationRules != null) Rules.MigrationRules.BeginMapping(Progress);
			//if (Rules.MigrationRules != null && Rules.MigrationRules.Enabled)
					//MigrationSubMap = new Dictionary<string, string>();

#if !CART_EXTRACTOR_TEST_SITE
            if (Rules.InvalidatePricesOnExtract)
				Task.Factory.StartNew(() => ClientData.Instance.InvalidatePrices(Alias));
#endif

			//------------------DepartmentNames.txt------------------
			if (Rules.ExportDepartmentNames || Rules.UseDepartmentsAsCategories)
			{
				Progress.StartTable("Department Names", "items");
				status = GetDepartmentNames(out itemCount);
				Progress.EndTable(itemCount, status);
			}

			//------------------Catalog.txt------------------
			Progress.StartTable("Catalog", "items");
			_catalogFileSeekPosition = 0L;
			status = GetCatalog(out catalogCount);
			Products = null; //release the memory
			if (_migrationSlave) status = Rules.MigrationRules.MapStatus; //adjust status for migration slave
			//if (_migrationSlave) //adjust status for migration slave
			//	status = MigrationSubMap.Any() ? "Migration Map Created" : "Migration Map is empty";
			Progress.EndTable(catalogCount, status);

			if (!_migrationSlave) //don't need additional files for migration
			{
				//------------------Exclusions.txt------------------
				Progress.StartTable("Exclusions", "items");
				status = GetExclusions(out itemCount);
				Progress.EndTable(itemCount, status);

				//------------------ExclusionCauses.txt------------------
				Progress.StartTable("ExclusionCauses", "items");
				status = GetExclusionCauses(out itemCount);
				Progress.EndTable(itemCount, status);

				//------------------Replacements.txt------------------
				Progress.StartTable("Replacements", "items");
				status = GetReplacements(out itemCount);
				Progress.EndTable(itemCount, status);

				//------------------Attribute1Items.txt------------------
				Progress.StartTable(Rules.Fields.Att1Name + " Names", "items");
				status = GetAtt1Names(out itemCount);
				Progress.EndTable(itemCount, status);

				//------------------Attribute2Items.txt------------------
				Progress.StartTable(Rules.Fields.Att2Name + " Names", "items");
				status = GetAtt2Names(out itemCount);
				Progress.EndTable(itemCount, status);

				//------------------FeaturedCrossSell.txt------------------
				Progress.StartTable("FeaturedCrossSell", "items");
				status = GetFeaturedCrossSellRecs(out itemCount);
				Progress.EndTable(itemCount, status);

				//------------------FeaturedUpSell.txt------------------
				Progress.StartTable("FeaturedUpSell", "items");
				status = GetFeaturedUpSellRecs(out itemCount);
				Progress.EndTable(itemCount, status);

				//------------------AltPrices.txt------------------
				Progress.StartTable("Alternate Prices", "items");
				status = GetAltPrices(out itemCount);
				Progress.EndTable(itemCount, status);

				//------------------AltPageLinks.txt------------------
				Progress.StartTable("Alternate Page Links", "items");
				status = GetAltPageLinks(out itemCount);
				Progress.EndTable(itemCount, status);

				//------------------AltImageLinks.txt------------------
				Progress.StartTable("Alternate Image Links", "items");
				status = GetAltImageLinks(out itemCount);
				Progress.EndTable(itemCount, status);

				//------------------AltTitles.txt------------------
				Progress.StartTable("Alternate Titles", "items");
				status = GetAltTitles(out itemCount);
				Progress.EndTable(itemCount, status);

				//------------------ConfigBoost.txt------------------
				Progress.StartTable("Config", "rows");
				status = GetConfig(out itemCount);
				Progress.EndTable(itemCount, status);
			}
			//release memory
			Exclusions = new List<ExclusionRecord>();
			Replacements = new List<ReplacementRecord>();
			FeaturedCrossSells = new FeaturedRecommendations();
			FeaturedUpSells = new FeaturedRecommendations();
			ParentProducts = new Dictionary<string, ParentItem>();
			AltPrices = new Dictionary<string, List<string>>();
			AltPageLinks = new Dictionary<string, List<string>>();
			AltImageLinks = new Dictionary<string, List<string>>();
			AltTitles = new Dictionary<string, List<string>>();
			Departments = new Dictionary<string, string>();


			//special handling for cart migration 
			if (fullExtraction && Rules.MigrationRules != null)
				Rules.MigrationRules.ProcessMap();

#if !CART_EXTRACTOR_TEST_SITE
            if (Rules.InvalidatePricesOnExtractComplete)
				Task.Factory.StartNew(() => ClientData.Instance.InvalidatePrices(Alias));
#endif
			return catalogCount;
		}

		public void SetMigrationSlave(CatalogMigration migration)
		{
			Rules.MigrationRules = new CatalogMigration(migration, false);
			//adjust data read path
			DataReadPath = IO.DataPath.Instance.ClientDataPath(Alias, true, "migration");
		}

		private string GetExclusions(out int itemCount)
		{
			//NOTE: GetCatalog must be called before GetExclusions 
			//exclusion logic is processed in ApplyRules on each product
			itemCount = -1;
			var clearTest = itemCount;
			try
			{
				if (!Rules.ExclusionsOn)
				{
					return "rule is turned off";
				}
				if (Exclusions.Count < 1)
				{
					return "no data";
				}

				itemCount = Exclusions.Count;
				clearTest = itemCount;
				Progress.UpdateTable(itemCount, -1, "Writing table");
				return TableAccess.Instance.WriteTable(Alias, ExclusionFilename, Exclusions);
			}
			catch (Exception ex)
			{
				if (BoostLog.Instance != null)
					BoostLog.Instance.WriteEntry(EventLogEntryType.Error, "Error creating Exclusions", "", ex, Alias);
				return "Error (request log for details)";
			}
			finally
			{
				if (clearTest < 1)
					TableAccess.Instance.ClearTable(Alias, ExclusionFilename);
			}
		}

		private string GetReplacements(out int itemCount)
		{
			//NOTE: GetCatalog must be called before GetReplacements 
			//most of the replacement logic is processed in ApplyRules on each product
			itemCount = -1;
			try
			{
				//First check to see if ManualReplacements exist and add them to the list
				var manReplacements = TableAccess.Instance.ReadTable(ManualReplacementFilename, Alias, 2, 2, 2);
				if (manReplacements != null && manReplacements.Count > 1)
				{
					var header = manReplacements[0].ToList();
					var desiredHeader = ReplacementRecord.Headers;
					var iOldId = Input.GetHeaderPosition(header, desiredHeader[0]);
					var iNewId = Input.GetHeaderPosition(header, desiredHeader[1]);
					manReplacements.RemoveAt(0); //remove header
					if (Replacements == null) Replacements = new List<ReplacementRecord>();
					foreach (var r in manReplacements)
					{
						Replacements.Add(new ReplacementRecord(r[iOldId], r[iNewId]));
					}
				}

				//make sure there are some replacements to save
				if (Replacements != null && Replacements.Count > 0) Rules.ReplacementsOn = true;
				if (!Rules.ReplacementsOn)
					return "rule is turned off";

				Progress.UpdateTable(-1, -1, "Exporting");
				//NOTE: full catalog replacements are handled in ApplyRules()
				if (Replacements == null) Replacements = new List<ReplacementRecord>();
				if (Rules.ReplacementRules != null)
				{
					foreach (var rc in Rules.ReplacementRules)
					{
						if (!rc.Type.Equals(ReplacementCondition.RepType.Item))
							continue;
						if (!Replacements.Any(r => r.OldId.Equals(rc.OldName))) //can only have one replacement for each item
							Replacements.Add(new ReplacementRecord(rc.OldName, rc.NewName));
						Progress.UpdateTable(-1, Replacements.Count);
					}
				}
				if (Replacements.Count < 1)
					return "no data";

				itemCount = Replacements.Count;
				Progress.UpdateTable(itemCount, -1, "Writing table");
				return TableAccess.Instance.WriteTable(Alias, ReplacementFilename, Replacements);
			}
			catch (Exception ex)
			{
				if (BoostLog.Instance != null)
					BoostLog.Instance.WriteEntry(EventLogEntryType.Error, "Error creating Replacements", "", ex, Alias);
				return "Error (request log for details)";
			}
		}

		private string GetFeaturedCrossSellRecs(out int itemCount)
		{
			itemCount = -1;
			try
			{
				if (!Rules.FeaturedCrossSellOn) 
				{
					return "rule is turned off";
				}
				if (FeaturedCrossSells.Records.Count < 1)
				{
					return "no data";
				}

				var outputRecs = FeaturedCrossSells.ToGeneratorRecs();
				itemCount = outputRecs.Count;
				Progress.UpdateTable(itemCount, -1, "Writing table");
				return TableAccess.Instance.WriteTable(Alias, FeaturedCrossSellFilename, outputRecs);
			}
			catch (Exception ex)
			{
				if (BoostLog.Instance != null)
					BoostLog.Instance.WriteEntry(EventLogEntryType.Error, "Error creating FeaturedCrossSells", "", ex, Alias);
				return "Error (request log for details)";
			}
		}

		private string GetFeaturedUpSellRecs(out int itemCount)
		{
			itemCount = -1;
			try
			{
				if (!Rules.FeaturedUpSellOn)
				{
					return "rule is turned off";
				}
				if (FeaturedUpSells.Records.Count < 1)
				{
					return "no data";
				}

				var outputRecs = FeaturedUpSells.ToGeneratorRecs();
				itemCount = outputRecs.Count;
				Progress.UpdateTable(itemCount, -1, "Writing table");
				return TableAccess.Instance.WriteTable(Alias, FeaturedUpSellFilename, outputRecs);
			}
			catch (Exception ex)
			{
				if (BoostLog.Instance != null)
					BoostLog.Instance.WriteEntry(EventLogEntryType.Error, "Error creating FeaturedCrossSells", "", ex, Alias);
				return "Error (request log for details)";
			}
		}

		private string GetAltPrices(out int itemCount)
		{
			itemCount = -1;
			try
			{
				if (!AltPrices.Any())
				{
					return "no data";
				}

				itemCount = AltPrices.Count;
				Progress.UpdateTable(itemCount, -1, "Formatting");
				var altPriceData = new List<List<string>>();

				//create header row
				var numAltPrices = AltPrices.First().Value.Count;
				var priceHeader = new List<string> { "ProductID", "TopPrice", "TopSalePrice", "TopListPrice", "TopCost" };
				for (var i = 1; i <= numAltPrices - 4; i++)
					priceHeader.Add("AltPrice" + i);
				altPriceData.Add(priceHeader);

				//add data rows
				foreach (var ap in AltPrices)
				{
					var row = new List<string> { ap.Key };
					row.AddRange(ap.Value);
					altPriceData.Add(row);
				}

				Progress.UpdateTable(-1, -1, "Writing table");
				return TableAccess.Instance.WriteTable(Alias, AltPriceFilename, altPriceData);
			}
			catch (Exception ex)
			{
				if (BoostLog.Instance != null)
					BoostLog.Instance.WriteEntry(EventLogEntryType.Error, "Error creating AltPrices", "", ex, Alias);
				return "Error (request log for details)";
			}
		}

		private string GetAltPageLinks(out int itemCount)
		{
			itemCount = -1;
			try
			{
				if (!AltPageLinks.Any())
				{
					return "no data";
				}

				itemCount = AltPageLinks.Count;
				Progress.UpdateTable(itemCount, -1, "Formatting");
				var altPageData = new List<List<string>>();

				//create header row
				var numAltPages = AltPageLinks.First().Value.Count;
				var pageHeader = new List<string> { "ProductID" };
				for (var i = 1; i <= numAltPages; i++)
					pageHeader.Add("AltPage" + i);
				altPageData.Add(pageHeader);

				//add data rows
				foreach (var ap in AltPageLinks)
				{
					var row = new List<string> { ap.Key };
					row.AddRange(ap.Value);
					altPageData.Add(row);
				}

				Progress.UpdateTable(-1, -1, "Writing table");
				return TableAccess.Instance.WriteTable(Alias, AltPageFilename, altPageData);
			}
			catch (Exception ex)
			{
				if (BoostLog.Instance != null)
					BoostLog.Instance.WriteEntry(EventLogEntryType.Error, "Error creating AltPageLinks", "", ex, Alias);
				return "Error (request log for details)";
			}
		}

		private string GetAltImageLinks(out int itemCount)
		{
			itemCount = -1;
			try
			{
				if (!AltImageLinks.Any())
				{
					return "no data";
				}

				itemCount = AltImageLinks.Count;
				Progress.UpdateTable(itemCount, -1, "Formatting");
				var altImageData = new List<List<string>>();

				//create header row
				var numAltImages = AltImageLinks.First().Value.Count;
				var imageHeader = new List<string> { "ProductID" };
				for (var i = 1; i <= numAltImages; i++)
					imageHeader.Add("AltImage" + i);
				altImageData.Add(imageHeader);

				//add data rows
				foreach (var ap in AltImageLinks)
				{
					var row = new List<string> { ap.Key };
					row.AddRange(ap.Value);
					altImageData.Add(row);
				}

				Progress.UpdateTable(-1, -1, "Writing table");
				return TableAccess.Instance.WriteTable(Alias, AltImageFilename, altImageData);
			}
			catch (Exception ex)
			{
				if (BoostLog.Instance != null)
					BoostLog.Instance.WriteEntry(EventLogEntryType.Error, "Error creating AltImageLinks", "", ex, Alias);
				return "Error (request log for details)";
			}
		}

		private string GetAltTitles(out int itemCount)
		{
			itemCount = -1;
			try
			{
				if (!AltTitles.Any())
				{
					return "no data";
				}

				itemCount = AltTitles.Count;
				Progress.UpdateTable(itemCount, -1, "Formatting");
				var altTitleData = new List<List<string>>();

				//create header row
				var numAltTitles = AltTitles.First().Value.Count;
				var header = new List<string> { "ProductID" };
				for (var i = 1; i <= numAltTitles; i++)
					header.Add("AltTitle" + i);
				altTitleData.Add(header);

				//add data rows
				foreach (var ap in AltTitles)
				{
					var row = new List<string> { ap.Key };
					row.AddRange(ap.Value);
					altTitleData.Add(row);
				}

				Progress.UpdateTable(-1, -1, "Writing table");
				return TableAccess.Instance.WriteTable(Alias, AltTitleFilename, altTitleData);
			}
			catch (Exception ex)
			{
				if (BoostLog.Instance != null)
					BoostLog.Instance.WriteEntry(EventLogEntryType.Error, "Error creating AltTitles", "", ex, Alias);
				return "Error (request log for details)";
			}
		}

		private string GetExclusionCauses(out int itemCount)
		{
			itemCount = -1;
			try
			{
				if (!ExclusionCauses.Any())
				{
					return "no data";
				}

				itemCount = ExclusionCauses.Count;
				Progress.UpdateTable(itemCount, -1, "Formatting");
				var exclusionCauseData = new List<List<string>>();

				//create header row
				exclusionCauseData.Add(new List<string> { "ProductID", "Cause" }); 

				//add data rows
				exclusionCauseData.AddRange(ExclusionCauses.Select(ec => new List<string> {ec.Key, ec.Value}));

				Progress.UpdateTable(-1, -1, "Writing table");
				return TableAccess.Instance.WriteTable(Alias, ExclusionCauseFilename, exclusionCauseData);
			}
			catch (Exception ex)
			{
				if (BoostLog.Instance != null)
					BoostLog.Instance.WriteEntry(EventLogEntryType.Error, "Error creating ExclusionCauses", "", ex, Alias);
				return "Error (request log for details)";
			}
		}

		private string GetConfig(out int rowCount)
		{
			const string filename = "ConfigBoost.txt";
			var data = Rules.FormatGeneratorConfig();

			rowCount = data.ToString().Split('\r').Count();
			return TableAccess.Instance.WriteTable(Alias, filename, data);
		}

		public List<string> GetAllQueryableFieldNames(DataGroup group)
		{
			return Rules.Fields.GetActiveFields(group);
		}

		#endregion

		#region Abstract and Virtual Methods

		#region External
		//public methods called by dashboard and boost services

		public abstract bool ValidateCredentials(out string status);
		// Only required if the order confirmation page does not include order details, which we use to create the auto actions file based on real time updates,
        // Some platforms don't show order details on the confirmation page, just provide an order index so our JS can't scrape the order info we need, in this case our
        // service comes here to find the order information based on order id.
		public abstract void LogSalesOrder(string orderId);

        #endregion

		#region Internal
		//internal methods called by base

		protected abstract void ReleaseCartData(); //release persistent data records

		//The following methods all update the same progress object
		//  base methods create one progress table for each data type
		//  child process can update table count and extra status messages
		//  complicated processes should add progress tasks
		//  return string is the final table status message
		protected virtual string GetInventory(out int itemCount)
		{
			return Inventory.GetData(out itemCount);
		}

		protected virtual string GetCatalog(out int itemCount)
		{
			return Catalog.GetData(out itemCount);
		}

		protected virtual string GetSalesMonth(DateTime exportDate, string filename, out int itemCount)
		{
			Sales.Reset(exportDate);
			return Sales.GetData(out itemCount);
		}

		protected virtual string GetCustomers(DateTime exportDate, string filename, out int itemCount)
		{
			Customers.Reset(exportDate);
			return Customers.GetData(out itemCount);
		}

		protected virtual string GetAtt1Names(out int itemCount)
		{
			return CategoryNames.GetData(out itemCount);
		}

		protected virtual string GetAtt2Names(out int itemCount)
		{
			return BrandNames.GetData(out itemCount);
		}

		/// <summary>
		/// virtual placeholder for optional departments
		/// override in cart implementation when applicable
		/// </summary>
		/// <param name="itemCount"></param>
		/// <returns></returns>
		protected virtual string GetDepartmentNames(out int itemCount)
		{
			itemCount = -1;
			if (!Rules.ExportDepartmentNames && !Rules.UseDepartmentsAsCategories)
			  return "export is not required";

			return DepartmentNames.GetData(out itemCount);
		}

		#endregion
		
        #endregion

		#region Utilities

		public static CartExtractor GetCart(SiteRules rules)
		{
			CartExtractor cart = null;
			switch (rules.CartType)
			{
#if !CART_EXTRACTOR_TEST_SITE
                case CartType.ThreeDCart:
					cart = new ThreeDCartExtractor(rules);
					break;
#endif
				case CartType.BigCommerce:
					cart = new BigCommerceExtractor(rules);
					break;
                //case CartType.MivaMerchant:
                //    cart = new MivaMerchantExtractor(rules);
                //    break;
                //case CartType.Volusion:
                //    cart = new VolusionExtractor(rules);
                //    break;
                //case CartType.CommerceV3:
                //    cart = new CommerceV3Extractor(rules);
                //    break;
                //case CartType.Magento:
                //    if (rules.PluginVersion > 3) goto case CartType.JsonFeed;
                //    break;
				case CartType.Shopify:
                    cart = new ShopifyExtractor(rules);
                    break;
                case CartType.NetSuite:
                    cart = new NetSuiteExtractor(rules);
                    break;                
                //case CartType.AspDotNetStorefront:
                //case CartType.WebsitePipeline:
				case CartType.TabbedFeed:
				case CartType.JsonFeed:
					cart = new JsonFeedExtractor(rules);
					break;
				case CartType.XmlFeed:
					cart = new XmlFeedExtractor(rules);
					break;
                //case CartType.osCommerce:
                //case CartType.PrestaShop:
				case CartType.Other:
					break;
				case CartType.Test:
					cart = new TestExtractor(rules);
					break;
			}
			return cart;
		}

		protected static void SetDefaultIfEmpty(ref string field, string value)
		{
			if (String.IsNullOrEmpty(field)) field = value;
		}

		private void ReleaseGlobalData()
		{
			Catalog.Reset();
			Inventory.Reset();
			Sales.Reset();
			Customers.Reset();
			CategoryNames.Reset();
			BrandNames.Reset();
			DepartmentNames.Reset();
			
			Products = new List<ProductRecord>();
			FeaturedCrossSells = new FeaturedRecommendations();
			FeaturedUpSells = new FeaturedRecommendations();
			ExclusionStats = new Dictionary<string, int>();

			//TODO: Move all below to the CatalogHandler class or depricate
			Exclusions = new List<ExclusionRecord>();
			Replacements = new List<ReplacementRecord>();
			ParentProducts = new Dictionary<string, ParentItem>();
			AltPrices = new Dictionary<string, List<string>>();
			AltPageLinks = new Dictionary<string, List<string>>();
			AltImageLinks = new Dictionary<string, List<string>>();
			AltTitles = new Dictionary<string, List<string>>();
			Departments = new Dictionary<string, string>();
			ExclusionCauses = new Dictionary<string, string>();
		}

		public void AddExclusionCause(string productId, string cause)
		{
			//record individual cause
			string existingCauses;
			if (ExclusionCauses.TryGetValue(productId, out existingCauses))
			{
				if (!existingCauses.Contains(cause))
				{
					existingCauses += "," + cause;
					ExclusionCauses[productId] = existingCauses;
				}
			}
			else
				ExclusionCauses.Add(productId, cause);

			//add to summary
			int count;
			if (ExclusionStats.TryGetValue(cause, out count))
				ExclusionStats[cause] = count + 1;
			else
				ExclusionStats.Add(cause, 1);
		}

		public void MigrateSlaveOrders(ref List<SalesRecord> orders)
		{
			if (Rules.MigrationRules != null) Rules.MigrationRules.MigrateSlaveOrders(ref orders);

		}

		public string GetExtraFields()
		{
			//extra fields are any fields defined in ClientSettings fieldNames or rules that are not in the standard feed
			if (!Rules.OmitExtraFields)
			{
				// check rules and combine with active fields
				var extraList = Rules.GetRuleFields().Union(Rules.Fields.GetActiveFields(DataGroup.Catalog))
														.Except(Rules.Fields.GetStandardFields(DataGroup.Catalog), StringComparer.OrdinalIgnoreCase).ToList();
				// then convert into a comma-separated query string
				if (extraList.Count > 0)
					return extraList.Aggregate((w, j) => string.Format("{0},{1}", w, j));
			}
			return string.Empty;
		}

		public List<string> GetAllFields(DataGroup group)
		{
			var fields = Rules.Fields.GetActiveFields(group);
			if (group == DataGroup.Catalog) fields = fields.Union(Rules.GetRuleFields()).ToList();
			return fields;
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
		public void ApplyRules(ref ProductRecord p, XElement product)
		{
			try
			{
				if (Rules.ReverseVisibleFlag)
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
				if (Rules.CategoryRules.AnyExcluded(p.Att1Id))
				{
					Exclusions.Add(new ExclusionRecord(p.ProductId));
					AddExclusionCause(p.ProductId, ExcludedCategoryCause);
					excluded = true;
				}

				//then check all other exclusion rules (note: item replacements are handled in GetReplacements()
				else if (Rules.ExclusionRules != null)
				{
					//Rules.ExclusionRules.Any(c => c.Compare(Input.GetValue(product, c.ResultField)))
					foreach (var c in Rules.ExclusionRules.Where(c => c.Compare(Input.GetValue(product, c.ResultField))))
					{
						Exclusions.Add(new ExclusionRecord(p.ProductId));
						AddExclusionCause(p.ProductId, c.Name);
						excluded = true;
						break;
					}
				}
				if (!excluded && Rules.ExclusionSet != null)
				{
					List<string> matchingNames;
					excluded = Rules.ExclusionSet.Evaluate(product, out matchingNames);
					if (excluded) 
						foreach (var name in matchingNames)
							AddExclusionCause(p.ProductId, name);
				}

				//exclude items that do not have images if AllowMissingPhotos is false (which is the default)
				//this is checked last so that hidden and out-of-stock items don't count toward missing image count
				if (!excluded && String.IsNullOrEmpty(p.ImageLink) && !Rules.AllowMissingPhotos)
				{
					//provides a means for clients to exclude items if image link empty
					Exclusions.Add(new ExclusionRecord(p.ProductId));
					AddExclusionCause(p.ProductId, MissingImageCause);
					excluded = true;
				}

				//remove ignored categories
				p.Att1Id = Rules.CategoryRules.RemoveIgnored(p.Att1Id);

				//apply filters
				if (Rules.FiltersOn)
				{
					//check category rules
					var filters = Rules.CategoryRules.AnyFiltered(p.Att1Id);
					if (Rules.CategoryRules.AnyUniversal(p.Att1Id))
						filters.Add(Rules.UniversalFilterName);

					//check filter rules
					if (Rules.FilterRules != null && Rules.FilterRules.Any())
					{
						var matches = Rules.FilterRules.Where(c => c.Compare(Input.GetValue(product, c.ResultField))).Select(c => c.Name);
						if (matches.Any())
							filters.AddRange(matches);
					}

					//check filter parsing rules
					if (Rules.FilterParsingRules != null)
					{
						foreach (var f in Rules.FilterParsingRules)
						{
							List<string> results;
							if (f.ApplyRules(product, out results))
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
						p.Filter = Rules.UniversalFilterName;
				}

				//check for full catalog replacement 
				if (Rules.ReplacementRules != null && Rules.ReplacementRules.Count > 0
				    && Rules.ReplacementRules[0].Type.Equals(ReplacementCondition.RepType.Catalog))
				{
					var oldId = Input.GetValue(product, Rules.ReplacementRules[0].OldResultField);
					var newId = Input.GetValue(product, Rules.ReplacementRules[0].NewResultField);
					if (!String.IsNullOrEmpty(oldId) && !String.IsNullOrEmpty(newId)
					    && !Replacements.Any(r => r.OldId.Equals(oldId))) //can only have one replacement for each item
						Replacements.Add(new ReplacementRecord(oldId, newId));
				}

				//check for Featured recs
				if (Rules.FeaturedCrossSellOn)
					FeaturedCrossSells.AddRecords(p.ProductId, product, Rules.FeaturedCrossSellRules);
				if (Rules.FeaturedUpSellOn)
					FeaturedUpSells.AddRecords(p.ProductId, product, Rules.FeaturedUpSellRules);

				//check for migration mapping
				if (Rules.MigrationRules != null)
					Rules.MigrationRules.MapItem(p.ProductId, product);
			}
			catch (Exception ex)
			{
				if (Log != null)
					Log.WriteEntry(EventLogEntryType.Information, "Error applying rules", ex, Alias);
			}
		}

		//protected void CreateMigrationMap(string pid, XElement product)
		//{
		//  if (MigrationSubMap == null || Rules.MigrationRules == null || !Rules.MigrationRules.Enabled) return;

		//  var field = _migrationSlave ? Rules.MigrationRules.MapFromField : Rules.MigrationRules.MapToField;
		//  if (string.IsNullOrEmpty(field)) return;
		//  field = Input.RemoveTablePrefix(field);
		//  var key = Input.GetValue(product, field);
		//  string value;
		//  if (string.IsNullOrEmpty(key) 
		//    || (_migrationSlave && MigrationSubMap.TryGetValue(pid, out value))
		//    || (!_migrationSlave && MigrationSubMap.TryGetValue(key, out value)))
		//    return;

		//  //mapping goes from slave-id to slave-key to master-key to master-id
		//  if (_migrationSlave) MigrationSubMap.Add(pid, key);
		//  else MigrationSubMap.Add(key, pid);
		//}

		protected void AddOrUpdateParent(string parentId, string childId, XElement child, string[] fieldnames)
		{
			var inventory = Input.SafeIntConvert(Input.GetValue(child, Rules.Fields.GetName(FieldName.Inventory)), 0);
			float tempVal;
			var price = Input.GetValue(out tempVal, child, Rules.Fields.GetName(FieldName.Price)) ? tempVal : 0F;
			var salePrice = Input.GetValue(out tempVal, child, Rules.Fields.GetName(FieldName.SalePrice)) ? tempVal : 0F;
			var listPrice = Input.GetValue(out tempVal, child, Rules.Fields.GetName(FieldName.ListPrice)) ? tempVal : 0F;
			var cost = Input.GetValue(out tempVal, child, Rules.Fields.GetName(FieldName.Cost)) ? tempVal : 0F;
			var rating = Input.GetValue(out tempVal, child, Rules.Fields.GetName(FieldName.Rating)) ? tempVal : 0F;

			var childItem = new ChildItem
				{
					Id = childId,
					Inventory = inventory,
					Price = price,
					SalePrice = salePrice,
					ListPrice = listPrice,
					Cost = cost,
					Rating = rating
				};
			string salePriceCheck;
			AddOrUpdateParent(parentId, childItem, out salePriceCheck);

			//check for migration mapping
			var mapId = _migrationSlave ? childId : parentId; //map from slave children to master parents
			if (Rules.MigrationRules != null) Rules.MigrationRules.MapItem(mapId, child);
		}

		protected void AddOrUpdateParent(string parentId, ChildItem child, out string salePrice)
		{
			salePrice = "";
			if (string.IsNullOrEmpty(child.Id)) return;

			if (string.IsNullOrEmpty(parentId)) parentId = child.Id;

			//special handling in case parent id is a list --use only first id for replacements
			var parentList = new List<string> {parentId};
			if (parentId.Contains(","))
			{
				parentList = parentId.Split(new[] {','}, StringSplitOptions.RemoveEmptyEntries).ToList();
				parentId = parentList[0];
			}
			if (parentList.Count == 1 && !child.Id.Equals(parentId)) //don't add to replacements if there are multiple parents
				Replacements.Add(new ReplacementRecord(child.Id, parentId));

			//need to remove invalid sale prices (common issue)
			if (child.SalePrice >= child.Price) child.SalePrice = 0F;
			salePrice = child.SalePrice > 0 ? child.SalePrice.ToString("F2") : ""; // validated sale price is passed back out

			foreach (var p in parentList) //usually only one
			{
				//parent item needs to have the price range of the children
				//float newPrice;
				ParentItem parent;
				if (!ParentProducts.TryGetValue(p, out parent))
				{
					parent = new ParentItem {Id = p};
					ParentProducts.Add(p, parent);
					//check for parent in the full list to get price if any --is this ever non-zero?
					//var parentMatch =
					//  extraFieldsByItem.FirstOrDefault(
					//    x => Input.GetValue(x, bareFieldnames[(int) SiteRules.FieldName.ProductId]).Equals(p));
					//if (parentMatch != null
					//    && Input.GetValue(out newPrice, parentMatch, bareFieldnames[(int) SiteRules.FieldName.Price])
					//    && newPrice > 0)
					//  parent.Price = newPrice;
					//parentNonZero = true;
				}

				if (child.Inventory > 0 || Rules.IgnoreStockInPriceRange)
				{
					parent.Inventory += child.Inventory;
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
				ParentProducts[p] = parent;
			}
		}

		public void ApplyAltPricesAndLinks(ref ProductRecord p, ref XElement product)
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
			if (ParentProducts.TryGetValue(id, out parent))
			{
				//if parent has no inventory, check accumulated inventory of children
				if (Input.SafeIntConvert(p.Inventory) < 1)
				{ 
					p.Inventory = parent.Inventory.ToString("N");
					product.SetElementValue(Rules.Fields.GetName(FieldName.Inventory), p.Inventory);
				}

				//if parent has no rating, apply child ratings (leave blank for no ratings)
				if (string.IsNullOrEmpty(p.Rating))
				{
					if (Rules.UseAverageChildRating)
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
					product.SetElementValue(Rules.Fields.GetName(FieldName.Price), p.Price);

					//always use parent sale price when using parent price
					if (parent.SalePrice > 0)
					{
						if (parent.SalePrice < parent.Price)
						{
							p.SalePrice = parent.SalePrice.ToString("F2");
							showSalePrice = true;
						}
						else p.SalePrice = Rules.HiddenSalePriceText;
					}
					else
						p.SalePrice = "";
					product.SetElementValue(Rules.Fields.GetName(FieldName.SalePrice), p.SalePrice);

				}
				if (parent.ListPrice > 0)
				{
					p.ListPrice = parent.ListPrice.ToString("F2");
					product.SetElementValue(Rules.Fields.GetName(FieldName.ListPrice), p.ListPrice);
				}
				if (parent.Cost > 0)
				{
					p.Cost = parent.Cost.ToString("F2");
					product.SetElementValue(Rules.Fields.GetName(FieldName.Cost), p.Cost);
				}

				//alternate prices contain top end of ranges
				//NOTE: Altprices are not added to the product XElement so they cannot be used in rules
				altPriceList.Add(parent.TopPrice > parent.Price ? parent.TopPrice.ToString("F2") : "");
				altPriceList.Add(showSalePrice && parent.TopSalePrice > parent.SalePrice ? parent.TopSalePrice.ToString("F2") : "");
				altPriceList.Add(parent.TopListPrice > parent.ListPrice ? parent.TopListPrice.ToString("F2") : "");
				altPriceList.Add(parent.TopCost > parent.Cost ? parent.TopCost.ToString("F2") : "");
			}

			//process any AltPrice fields
			var productX = product; //must make copy to pass in lambda
			var fields = Rules.Fields.GetAltFields(AltFieldGroup.AltPriceFields);
			if (fields != null && fields.Any())
			{
				while (altPriceList.Count < 4)
					altPriceList.Add("");
				altPriceList.AddRange(fields.Select(t => FormatPrice(Input.GetValue(productX, t))));
			}
			if (altPriceList.Any(x => !string.IsNullOrEmpty(x)))
					AltPrices.Add(p.ProductId, altPriceList);

			//process any AltPage fields
			fields = Rules.Fields.GetAltFields(AltFieldGroup.AltPageFields);
			if (fields != null && fields.Any())
			{
				var altPageList = new List<string>();
				altPageList.AddRange(fields.Select(t => Input.GetValue(productX, t)));
				if (altPageList.Any(x => !string.IsNullOrEmpty(x)))
					AltPageLinks.Add(p.ProductId, altPageList);
			}

			//process any altImage fields
			fields = Rules.Fields.GetAltFields(AltFieldGroup.AltImageFields);
			if (fields != null && fields.Any())
			{
				var altImageList = new List<string>();
				altImageList.AddRange(fields.Select(t => Input.GetValue(productX, t)));
				if (altImageList.Any(x => !string.IsNullOrEmpty(x)))
					AltImageLinks.Add(p.ProductId, altImageList);
			}

			//process any altTitle fields
			fields = Rules.Fields.GetAltFields(AltFieldGroup.AltTitleFields);
			if (fields != null && fields.Any())
			{
				var altTitleList = new List<string>();
				altTitleList.AddRange(fields.Select(t => Input.GetValue(productX, t)));
				if (altTitleList.Any(x => !string.IsNullOrEmpty(x)))
					AltTitles.Add(p.ProductId, altTitleList);
			}
		}

		//public string UrlEncode(string source)
		//{
		//  if (source == null) return null;

		//  return HttpContext.Current.Server.UrlEncode(source);
		//}

		public string Unescape(string source)
		{
			var result = Regex.Replace(
													source,
													@"\\[Uu]([0-9A-Fa-f]{4})",
													m => char.ToString(
															(char)ushort.Parse(m.Groups[1].Value, NumberStyles.AllowHexSpecifier)));
			return result;
		}

		public string CleanUpTitle(string source)
		{
			if (source == null) return null;

			//first apply any special character mappings
			if (Rules.TitleCharMap != null)
				source = Rules.TitleCharMap.Aggregate(source, (current, map) => current.Replace(map.Key, map.Value));

			//force \u encoded unicode characters to use &#
			//question: how to tell the difference between decimal and hex input? (assuming decimal here)
			//source = source.Replace("\\u", "&#");
			source = Unescape(source);

			var title = "";
			foreach (var c in source)
			{
				var val = (int) c;
				if (val < 32 || val == 127) continue; //skip illegal characters

				//check for standard characters
				if (val == 34) title += "&quot;";
				else if (val == 169) title += "&copy;";
				else if (val == 174) title += "&reg;";
				else if (val == 176) title += "&deg;";
				else if (val == 8482) title += "&trade;";

				else if (val > 127) //extended ascii so encode
					title += String.Format("&#{0}", val);
				else
					title += c.ToString();
			}
			//standard characters above could have been escape coded with a preceeding backslash, so find and remove them
			if (title.Contains("\\&")) 
				title = title.Replace("\\&", "&");
			return title;
		}

		public string RemoveIllegalChars(string source, char[] illigalChars)
		{
			if (source == null) return null;

			int index;
			while ((index = source.IndexOfAny(illigalChars)) >= 0)
			{
				source = source.Remove(index, 1);
			}
			return source;
		}

		//standard Hash saved as a hex string
		public string GetHash(string source)
		{
			if (source == null) return null;

			var input = Encoding.UTF8.GetBytes(source);
			var result = Hash.ComputeHash(input);
			var sb = new StringBuilder();
			for (var i = 0; i < result.Length; i++)
			{
				sb.Append(result[i].ToString("X2"));
			}
			return sb.ToString();
		}

		public string RemoveHtmlFormatting(string source)
		{
			if (source == null) return null;

			var index = 0;
			while (true)
			{
				var begin = source.IndexOf('<', index);
				if (begin < 0) break;
				var end = source.IndexOf('>', begin);
				if (end < 0) break;
				//found some formatting
				source = source.Remove(begin, end - begin + 1);
				index = begin;
			}
			return source;
		}

		protected string StripUriPrefix(string uri)
		{
			var index = uri.IndexOf(Rules.StoreShortUrl, StringComparison.OrdinalIgnoreCase);
			if (index >= 0)
				return uri.Substring(index + Rules.StoreShortUrl.Length);

			index = uri.IndexOf("http", StringComparison.OrdinalIgnoreCase);
			if (index >= 0) index = uri.IndexOf(":"); //works for http or https
			if (index >= 0) return uri.Substring(index + 1);

			return uri;
		}

		public static string FormatPrice(string price)
		{
			if (String.IsNullOrWhiteSpace(price))
				return "";
			double priceVal;
			if (double.TryParse(price, out priceVal))
				return priceVal.ToString("F2");

			return price;
		}

		#endregion

		#region Feed Handling

		protected void SetFeedTypes()
		{
			//Need to examine all feed RuleUrls to see which are populated.
			//Inidvidual feeds take precedence. 
			//If any of those are blank then we use api or combined feed for those


			//First check for existence of Api or Combined Urls to use as defaults
			var apiExists = false;
			var defaultType = FeedType.None;
			if (!string.IsNullOrEmpty(Rules.ApiUrl))
			{
				Rules.ApiUrl = SetUrlProtocol(Rules.ApiUrl);
				apiExists = true;
				defaultType = FeedType.SingleApi;
			}
			if (!string.IsNullOrEmpty(Rules.CombinedFeedUrl))
			{
				Rules.CombinedFeedUrl = SetUrlProtocol(Rules.CombinedFeedUrl);
				if (!apiExists) defaultType = FeedType.CombinedFeed;
			}

			//then initialize all feedtypes to the default
			if (_feedTypes == null) _feedTypes = new Dictionary<DataGroup, FeedType>();
			else _feedTypes.Clear();
			foreach (var dg in Enum.GetValues(typeof(DataGroup)))
			{
				_feedTypes.Add((DataGroup)dg, defaultType);
			}

			//Now check for existence of individual feed urls to override default

			//Catalog
			if (!string.IsNullOrEmpty(Rules.CatalogFeedUrl))
			{
				Rules.CatalogFeedUrl = SetUrlProtocol(Rules.CatalogFeedUrl);
				_feedTypes[DataGroup.Catalog] = FeedType.IndividualFeeds;
			}
			if (_feedTypes[DataGroup.Catalog].Equals(FeedType.None)
				&& BoostLog.Instance != null)
				BoostLog.Instance.WriteEntry(EventLogEntryType.Error, "No catalog feed exists for: " + Alias);

			//Sales
			if (!string.IsNullOrEmpty(Rules.SalesFeedUrl))
			{
				Rules.SalesFeedUrl = SetUrlProtocol(Rules.SalesFeedUrl);
				_feedTypes[DataGroup.Sales] = FeedType.IndividualFeeds;
			}

			//Customers
			if (!string.IsNullOrEmpty(Rules.CustomerFeedUrl))
			{
				Rules.CustomerFeedUrl = SetUrlProtocol(Rules.CustomerFeedUrl);
				_feedTypes[DataGroup.Customers] = FeedType.IndividualFeeds;
			}

			//CategoryNames
			if (!string.IsNullOrEmpty(Rules.Att1NameFeedUrl))
			{
				Rules.Att1NameFeedUrl = SetUrlProtocol(Rules.Att1NameFeedUrl);
				_feedTypes[DataGroup.CategoryNames] = FeedType.IndividualFeeds;
			}

			//ManufacturerNames
			if (!string.IsNullOrEmpty(Rules.Att2NameFeedUrl))
			{
				Rules.Att2NameFeedUrl = SetUrlProtocol(Rules.Att2NameFeedUrl);
				_feedTypes[DataGroup.ManufacturerNames] = FeedType.IndividualFeeds;
			}

			//Inventory
			if (!string.IsNullOrEmpty(Rules.InventoryFeedUrl))
			{
				Rules.InventoryFeedUrl = SetUrlProtocol(Rules.InventoryFeedUrl);
				_feedTypes[DataGroup.Inventory] = FeedType.IndividualFeeds;
			}

			//DepartmentNames
			if (!string.IsNullOrEmpty(Rules.DepartmentNameFeedUrl))
			{
				Rules.DepartmentNameFeedUrl = SetUrlProtocol(Rules.DepartmentNameFeedUrl);
				_feedTypes[DataGroup.DepartmentNames] = FeedType.IndividualFeeds;
			}

			//Now set feed Urls (better to do this here so faster to lookup later
			if (_feedUrls == null) _feedUrls = new Dictionary<DataGroup, string>();
			else _feedUrls.Clear();
			foreach (DataGroup dg in Enum.GetValues(typeof(DataGroup)))
			{
				switch (_feedTypes[dg])
				{
					case FeedType.SingleApi:
						_feedUrls.Add(dg, Rules.ApiUrl);
						break;
					case FeedType.CombinedFeed:
						_feedUrls.Add(dg, Rules.CombinedFeedUrl);
						break;
					case FeedType.IndividualFeeds:
						switch (dg)
						{
							case DataGroup.Sales:
								_feedUrls.Add(dg, Rules.SalesFeedUrl);
								break;
							case DataGroup.Catalog:
								_feedUrls.Add(dg, Rules.CatalogFeedUrl);
								break;
							case DataGroup.CategoryNames:
								_feedUrls.Add(dg, Rules.Att1NameFeedUrl);
								break;
							case DataGroup.ManufacturerNames:
								_feedUrls.Add(dg, Rules.Att2NameFeedUrl);
								break;
							case DataGroup.DepartmentNames:
								_feedUrls.Add(dg, Rules.DepartmentNameFeedUrl);
								break;
							case DataGroup.Customers:
								_feedUrls.Add(dg, Rules.CustomerFeedUrl);
								break;
							case DataGroup.Inventory:
								_feedUrls.Add(dg, Rules.InventoryFeedUrl);
								break;
							case DataGroup.Custom:
								_feedUrls.Add(dg, "");
								break;
							default:
								throw new ArgumentOutOfRangeException();
						}
						break;
					case FeedType.None:
						_feedUrls.Add(dg, "");
						break;
					default:
						throw new ArgumentOutOfRangeException();
				}
			}
		}
		
		protected FeedType GetFeedType(DataGroup group)
		{
			return _feedTypes[group]; 
		}

		protected string SetUrlProtocol(string url)
		{
			if (string.IsNullOrEmpty(url)) return "";
			if (url.StartsWith("http") || url.StartsWith("file:")) 
				return url; //no changes needed

			var index = url.IndexOf("//", StringComparison.Ordinal);
			return (index < 0) ? "https://" + url : "https:" + url.Substring(index);
		}

		public int GetRowsPerRequest(DataGroup group, int count, int max)
		{
			var forceVal = 0;
			switch (group)
			{
				case DataGroup.Catalog:
					forceVal = Rules.ApiForceCatalogRowRange;
					break;
				case DataGroup.CategoryNames:
					forceVal = Rules.ApiForceCategoryRowRange;
					break;
				case DataGroup.Customers:
					forceVal = Rules.ApiForceCustomerRowRange;
					break;
				case DataGroup.Inventory:
					forceVal = Rules.ApiForceInventoryRowRange;
					break;
				case DataGroup.Sales:
					forceVal = Rules.ApiForceSalesRowRange;
					break;
			}
			return forceVal > 0 ? forceVal
						: Rules.ApiForceAllRowRanges > 0 ? Rules.ApiForceAllRowRanges
						: count > max ? max : 0;
		}

		protected NameValueCollection GetQueryParams(DataGroup group, DateTime exportDate, string range = "", string extraFields = "", bool countFlag = false)
		{
			//ServicePointManager.DefaultConnectionLimit = 48; //recommended to be 12 * number of logical CPUs
			//ServicePointManager.ServerCertificateValidationCallback = ((sender, certificate, chain, sslPolicyErrors) => true);
			//if (Rules.Expect100Continue)
			//  ServicePointManager.Expect100Continue = true;
			//if (Rules.ForceProtocolSsl3)
			//  ServicePointManager.SecurityProtocol = SecurityProtocolType.Ssl3;

			var queryParams = new NameValueCollection();
			if (!string.IsNullOrEmpty(Rules.ApiAliasParam))
				queryParams.Add(Rules.ApiAliasParam, _migrationSlave ? Rules.MigrationRules.MigrationAlias : Alias);
			if (!string.IsNullOrEmpty(Rules.ApiKeyParam))
				queryParams.Add(Rules.ApiKeyParam, Rules.ApiKey);

			if (GetFeedType(group).Equals(FeedType.SingleApi))
			{
				var groupParam = Rules.ApiVersion < 3 && countFlag
													 ? "DataCount"
													 : "DataGroup";
				queryParams.Add(groupParam, group.ToString());
				if (Rules.ApiVersion >= 3 && countFlag)
					queryParams.Add("ResultType", "Count");
			}

			switch (group)
			{
				case DataGroup.Sales:
				case DataGroup.Customers:
					var month = exportDate.Month;
					var year = exportDate.Year;
					if (!string.IsNullOrEmpty(Rules.ApiMonthParam)
							&& !string.IsNullOrEmpty(Rules.ApiYearParam))
					{
						queryParams.Add(Rules.ApiMonthParam, month.ToString("D2"));
						queryParams.Add(Rules.ApiYearParam, year.ToString("D4"));
					}
					else if ((group.Equals(DataGroup.Sales) && Rules.ApiSalesDateRangeEnabled)
						|| (group.Equals(DataGroup.Customers) && Rules.ApiCustomerDateRangeEnabled))
					{
						var dateParam = string.IsNullOrEmpty(Rules.ApiDateRangeParam)
															? "DateRange"
															: Rules.ApiDateRangeParam;
						var lastDay = DateTime.DaysInMonth(year, month);
						queryParams.Add(dateParam, string.Format("{0}-{1}-01,{0}-{1}-{2}",
																	year.ToString("D4"), month.ToString("D2"), lastDay.ToString("D2")));
					}
					break;
				case DataGroup.Catalog:
					if (!string.IsNullOrEmpty(range) && Rules.ApiVersion < 3) //older API IdRange only applied to catalog
					{
						var rangeParam = string.IsNullOrEmpty(Rules.ApiIdRangeParam)
														?  "IdRange"
														: Rules.ApiIdRangeParam;		
						queryParams.Add(rangeParam, range);
					}
					if (!string.IsNullOrEmpty(extraFields) && !Rules.OmitExtraFields)
					{
						var fieldParam = string.IsNullOrEmpty(Rules.ApiFieldParam)
															 ? "ExtraFields"
															 : Rules.ApiFieldParam;
						queryParams.Add(fieldParam, extraFields);
					}
					break;
				case DataGroup.CategoryNames:
					break;
				case DataGroup.ManufacturerNames:
					break;
				case DataGroup.DepartmentNames:
					break;
				case DataGroup.Inventory:
					break;
				default:
					throw new ArgumentOutOfRangeException("group");
			}

			//Newer API RowRange can apply to all data groups
			if (!string.IsNullOrEmpty(range) && Rules.ApiVersion >= 3)
			{
				var rangeParam = string.IsNullOrEmpty(Rules.ApiRowRangeParam)
												? "RowRange"
												: Rules.ApiRowRangeParam;
				queryParams.Add(rangeParam, range);
			}

			//get any additional query params defined in site rules
			if (Rules.ApiAddQueries != null && Rules.ApiAddQueries.Any())
			{
				NameValueCollection queries;
				if (Rules.ApiAddQueries.TryGetValue(group, out queries))
				{
					queryParams.Add(queries);
					//foreach (var q in queries)
					//  queryParams.Add(q);
				}
				if (Rules.ApiAddQueries.TryGetValue(DataGroup.All, out queries)) //some extra queries apply to all requests
				{
					queryParams.Add(queries);
				}
			}

			return queryParams;
		}

		protected string GetFeedUrl(DataGroup group)
		{
			//choose the feed url
			var feedUrl = _feedUrls[group];

			if (string.IsNullOrEmpty(feedUrl))
				throw new Exception("Feed Url not provided for " + @group);

			return feedUrl;
		}

		protected string GetAuthHeader(ref NameValueCollection queryParams, ref string feedUrl)
		{
			//setup credentials
			string authHeader = null;

			if (Rules.ExtractorCredentials != null)
			{
				var userNameParam = string.IsNullOrEmpty(Rules.ExtractorCredentials.UserNameParam)
															? "username"
															: Rules.ExtractorCredentials.UserNameParam;
				var passwordParam = string.IsNullOrEmpty(Rules.ExtractorCredentials.PasswordParam)
															? "password"
															: Rules.ExtractorCredentials.PasswordParam;
				if (Rules.ExtractorCredentials.RequireSsl
						&& !feedUrl.StartsWith("https", StringComparison.OrdinalIgnoreCase))
				{
					var index = feedUrl.IndexOf("//", StringComparison.Ordinal);
					if (index >= 0) feedUrl = "https:" + feedUrl.Substring(index);
					else feedUrl = "https://" + feedUrl;
				}
				switch (Rules.ExtractorCredentials.Type)
				{
					case AuthCredentials.AuthType.BasicAuth:
						authHeader = WebHelper.CreateAuthHeader(Rules.ExtractorCredentials);
						break;
					case AuthCredentials.AuthType.AuthParams:
						queryParams.Add(userNameParam, Rules.ExtractorCredentials.UserName);
						queryParams.Add(passwordParam, Rules.ExtractorCredentials.Password);
						break;
					case AuthCredentials.AuthType.LoginPage:
						throw new Exception("Illegal AuthType for ExtractorCredentials: LoginPage");
					case AuthCredentials.AuthType.HttpAuth:
					case AuthCredentials.AuthType.None:
						break;
				}
			}
			else
			{
				if (!string.IsNullOrEmpty(Rules.ApiUserName))
				{
					var userParam = string.IsNullOrEmpty(Rules.ApiUserParam)
														? "ClientAlias"
														: Rules.ApiUserParam;
					queryParams.Add(userParam, Rules.ApiUserName);
				}
				//ApiKey is already added in GetQueryParams, and there is no ApiPassword param other than the key
			}

			return authHeader;
		}

		protected NameValueCollection GetLogin()
		{
			NameValueCollection login = null;

			if (Rules.ExtractorLoginCredentials != null)
			{
				if (!Rules.ExtractorLoginCredentials.Type.Equals(AuthCredentials.AuthType.LoginPage))
					throw new Exception("Illegal AuthType for ExtractoLoginCredentials: must be LoginPage");
				var userNameParam = string.IsNullOrEmpty(Rules.ExtractorLoginCredentials.UserNameParam)
															? "username"
															: Rules.ExtractorLoginCredentials.UserNameParam;
				var passwordParam = string.IsNullOrEmpty(Rules.ExtractorLoginCredentials.PasswordParam)
															? "password"
															: Rules.ExtractorLoginCredentials.PasswordParam;
				login = new NameValueCollection
							{
								{userNameParam, Rules.ExtractorLoginCredentials.UserName},
								{passwordParam, Rules.ExtractorLoginCredentials.Password}
							};
			}

			return login;
		}

		protected void SetupFeedClient(string feedUrl, string authHeader = null, NameValueCollection queryParams = null, string acceptHeader = null)
		{
			const string DefaultAcceptHeader = "application/json";
			var cancelAuth = false;

			if (string.IsNullOrEmpty(acceptHeader))
				acceptHeader = string.IsNullOrEmpty(Rules.ApiAcceptHeader) ? DefaultAcceptHeader : Rules.ApiAcceptHeader;

			if (_feedClient != null)
			{
				// remove old query strings and replace them below
				_feedClient.QueryString.Clear();

				//if the existing web client is using an auth header, but the new request is not, then we need to kill it
				cancelAuth = !string.IsNullOrEmpty(_feedClient.Headers["Authorization"])
				                 && string.IsNullOrEmpty(authHeader);
			}

			if (_feedClient == null || _feedClient.SessionExpired() || cancelAuth)
			{
				//start over with a new client
				_feedClient = new CartWebClient(Rules.WebClientConfig)
					{
						Encoding = Encoding.UTF8,
						BaseAddress = "",
						Proxy = null
					};

				var login = GetLogin();
				if (login != null)
				{
					_feedClient.Headers.Set("user-agent", "Mozilla/5.0 (Windows NT 6.1; WOW64; rv:21.0) Gecko/20100101 Firefox/21.0");
					_feedClient.Headers.Set("accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
					//_feedClient.Headers.Set("user-agent", "Mozilla/5.0 (Windows NT 6.1; rv:18.0) Gecko/20100101 Firefox/18.0");
					//_feedClient.Headers.Set("accept", "*/*");
					//_feedClient.Headers.Set("content-type", "application/x-www-form-urlencoded");
					var loginUrl = string.IsNullOrEmpty(Rules.ExtractorLoginUrl) ? feedUrl : Rules.ExtractorLoginUrl;
					//var loginPage = _feedClient.DownloadString(loginUrl);
					var responseArray = _feedClient.UploadValues(loginUrl, "POST", login);
#if DEBUG
					var loginResponse = Encoding.UTF8.GetString(responseArray);
					Debug.Write(loginResponse);
#endif
				}
				else if (Rules.ExtractorCredentials != null && Rules.ExtractorCredentials.Type == AuthCredentials.AuthType.HttpAuth)
				{
					_feedClient.Credentials = new NetworkCredential(Rules.ExtractorCredentials.UserName, Rules.ExtractorCredentials.Password);
					ServicePointManager.ServerCertificateValidationCallback =
						((sender, certificate, chain, sslPolicyErrors) => true);
				}
			}

			//must set headers each time as previous call may remove them
			_feedClient.Headers.Set("user-agent", "Mozilla/5.0 (Windows NT 6.1; rv:18.0) Gecko/20100101 Firefox/18.0");
			_feedClient.Headers.Set("accept", acceptHeader);
			if (!string.IsNullOrEmpty(authHeader))
				_feedClient.Headers.Set("Authorization", authHeader);
			if (queryParams != null && queryParams.Count > 0)
				_feedClient.QueryString.Add(queryParams);

#if DEBUG
			if (BoostLog.Instance != null)
				BoostLog.Instance.WriteEntry(EventLogEntryType.Information, GetRequestDetails(feedUrl));
#endif
		}

		protected string GetRequestDetails(string feedUrl)
		{
			if (feedUrl.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) return feedUrl;

			var details = string.Format("Feed Request Details\nURL = {0}\nQuery Params:\n", feedUrl);
			var fullUrl = feedUrl;
			if (_feedClient == null)
				return details + "\tnone\n";
			try
			{
				var tempClient = new CartWebClient(_feedClient); //it's possible for _feedClient to be nulled in a separate thread
				if (tempClient.QueryString.HasKeys())
				{
					var separator = "?";
					foreach (string k in tempClient.QueryString.Keys)
					{
						var v = tempClient.QueryString.GetValues(k);
						var vals = v == null ? "" : v.Any() ? v.Aggregate((c, w) => string.Format("{0},{1}", c, w)) : "";
						details += string.Format("\t{0}:\t{1}\n", k, vals);
						fullUrl += separator + k + "=" + vals;
						separator = "&";
					}
				}
				else
					details += "\tnone\n";

				details += "Headers:\n";
				if (tempClient.Headers.HasKeys())
				{
					foreach (string k in tempClient.Headers.Keys)
					{
						var v = tempClient.Headers.GetValues(k);
						details += string.Format("\t{0}:\t{1}\n", k, v == null
							                                             ? ""
							                                             : v.Any()
								                                               ? v.Aggregate((c, w) => string.Format("{0},{1}", c, w))
								                                               : "");
					}
				}
				else
					details += "\tnone\n";
			}
			catch (Exception ex)
			{
				details += string.Format("\nError in GetRequestDetails: {0}", Input.GetExMessage(ex));
			}
			details += "\nFull Url: " + fullUrl;

			return details;
		}

		/// <summary>
		/// Get the number of rows for a given data group. 
		/// Never throws an exception --returns 0 on error.
		/// </summary>
		/// <param name="group"></param>
		/// <returns></returns>
		public int GetRowCount(DataGroup group, DateTime exportDate)
		{
            // See if rules override necessity to get count
			if (!Rules.ApiCountEnabled) 
                return 0;
			
            //can only request count if there is an API to query            
            if (GetFeedType(group) != FeedType.SingleApi) 
                return 0;

			var result = "";
			var queryParams = GetQueryParams(group, exportDate, "", "", true);
			using (var resultStream = GetQueryResponse(group, queryParams))
			{
				if (resultStream == null) return 0;
				using (var reader = new StreamReader(resultStream, Encoding.UTF8))
				{
					result = reader.ReadToEnd();
				}
			}
			int start;
			if (!string.IsNullOrEmpty(result) && (start = result.IndexOf(':')) > 0) //json response
			{
				var end = result.IndexOf('}', start);
				result = end > 0 ?  result.Substring(start + 1, end - start - 1) : result.Substring(start + 1);
			}
			return Input.SafeIntConvert(result);
		}

        private long _lastTickCount = 0;
        
        public string GetQueryResponse(DataGroup group, string feedUrl = null, int maxCallsPerSecond = 0)
        {
            // Use maxCallsPerSecond to limit the number of calls to the platform API, e.g. Shopify limits us to 2 calls per second
            if (maxCallsPerSecond > 0)
            {
                var tickCount = DateTime.Now.Ticks;
                int _minDelta = 1000/maxCallsPerSecond + 250;
                if (_lastTickCount > 0)
                {
                    var delta = (int)((tickCount - _lastTickCount) / TimeSpan.TicksPerMillisecond);
                    if (delta < _minDelta)
                        System.Threading.Thread.Sleep(_minDelta - delta);
                }
                _lastTickCount = tickCount;
            }
            
            var result = "";
            NameValueCollection queryParams = new NameValueCollection();
            using (var resultStream = GetQueryResponse(group, queryParams, string.IsNullOrEmpty(Rules.ApiUrl) ? feedUrl : Rules.ApiUrl + feedUrl))
            {
                if (resultStream == null) return "";
                using (var reader = new StreamReader(resultStream, Encoding.UTF8))
                {
                    result = reader.ReadToEnd();
                }
            }
            return result;
        }

		protected MemoryStream GetQueryResponse(DataGroup group, NameValueCollection queryParams, string feedUrl = null)
		{
			if (feedUrl == null) feedUrl = GetFeedUrl(group);
			var authHeader = GetAuthHeader(ref queryParams, ref feedUrl);

			try
			{
				SetupFeedClient(feedUrl, authHeader, queryParams);
				
				// Get response  
				var resultStream = new MemoryStream();
				using (var stream = _feedClient.TryOpenRead(feedUrl, ref Progress))
				{
					if (stream == null)
						throw new Exception(string.Format("No response on {0} query for {1}", @group, Alias));
					stream.CopyTo(resultStream);
				}
				resultStream.Seek(0, SeekOrigin.Begin);

#if DEBUG
                if (_feedClient != null && _feedClient.ResponseHeaders != null)
                {
                    var rh = new StringBuilder();
                    for (var i = 0; i < _feedClient.ResponseHeaders.Count; i++)
                        rh.Append(string.Format("{1} = {2}{0}", Environment.NewLine, _feedClient.ResponseHeaders.GetKey(i),
                                                                _feedClient.ResponseHeaders.Get(i)));
                    Debug.Write(rh.ToString());
                }
#endif
				return resultStream;
			}
			#region Faultcatching

			catch (WebException wex)
			{
				if (BoostLog.Instance != null)
					BoostLog.Instance.WriteEntry(EventLogEntryType.Error, "Error getting query response", GetRequestDetails(feedUrl) + "\n\nWex: ", wex, Alias);
			}
			catch (Exception ex)
			{
				if (BoostLog.Instance != null)
					BoostLog.Instance.WriteEntry(EventLogEntryType.Warning, "Error getting query response", GetRequestDetails(feedUrl), ex, Alias);
			}
			#endregion
			return null;
		}
            
        public string FindValue(NameValueCollection queryParams, string searchKey)
        {
            foreach (string key in queryParams)
                if (key == searchKey)
                    return queryParams[key];
            return null;
        }

        public string FindValueAsNonNullString(NameValueCollection queryParams, string searchKey)
        {
            string value = FindValue(queryParams, searchKey);
            if (value != null)
                return value;
            return "";
        }
        
        protected void GetFeedData(out XDocument doc, DataGroup group, DateTime exportDate, int firstRow = 1, int maxRows = 0, string extraFields = "")
		{
			doc = null;
			if (GetFeedType(group).Equals(FeedType.CombinedFeed))
			{
				if (_combinedFeed != null && (DateTime.Now - _lastFeedTime).TotalMinutes < _feedRefreshTime) 
				{
					doc = _combinedFeed;
					return;
				}
			}
			using (var resultStream = GetFeedData(group, exportDate, firstRow, maxRows, extraFields))
			{
				if (resultStream.Length < 1) return;

#if DEBUG
				using (var fileStream = File.Create("C:\\Temp\\preXmlParseData.txt"))
				{
					resultStream.CopyTo(fileStream);
				}
				resultStream.Seek(0, SeekOrigin.Begin);
#endif

				using (var xmlreader = new XmlSanitizingStream(ApplyMapping(resultStream)))
				{
					doc = XDocument.Load(xmlreader);
				}
			}

#if DEBUG
			doc.Save("C:\\Temp\\postXmlParse.xml");
#endif
			if (GetFeedType(group).Equals(FeedType.CombinedFeed))
			{
				_combinedFeed = doc;
				_lastFeedTime = DateTime.Now;
			}

		}

		public virtual void GetFeedData(out List<List<string>> data, DataGroup group, DateTime exportDate, int firstRow = 1, int maxRows = 0, string extraFields = "")
		{
			List<string> json;
			GetFeedData(out json, group, exportDate, firstRow, maxRows, extraFields);
			data = json.Select(Input.SplitJsonRow).ToList();
		}

		public void GetFeedData(out List<string> json, DataGroup group, DateTime exportDate, int firstRow = 1, int maxRows = 0, string extraFields = "")
		{
			var range = "";
			json = new List<string>();
			using (var resultStream = GetFeedData(group, exportDate, firstRow, maxRows, extraFields))
			{
				if (resultStream == null) return;
				if (!resultStream.CanRead)
				{
					resultStream.Close();
					return;
				}

#if DEBUG_FEED
				var filename = string.Format("C:\\Temp\\{0}{1}-{2}.json", Alias, group.ToString(), exportDate.ToString("yyyy-MM"));
				var mode = _lastJsonGroup.Equals(group) ? FileMode.Append : FileMode.Create;
				_lastJsonGroup = group;

				using (var fileStream = File.Open(filename, mode))
				{
					resultStream.CopyTo(fileStream);
				}
				resultStream.Seek(0, SeekOrigin.Begin);
#endif
				json = Input.GetRows(ApplyMapping(resultStream), Rules.ApiRowEnd, Rules.ApiTrimChars);
			}

			var rowCheck = maxRows;
			if (firstRow == 1 || !Rules.ApiHeaderIsOnlyOnFirstRow)
			{
				rowCheck++; //add one row for the column header row
				for (var i = 0; i < Rules.ApiExtraHeaders; i++)
					if (json.Count > 0) json.RemoveAt(0); //remove extra header rows
			}
#if DEBUG
			if (maxRows > 0 && json.Count != rowCheck)
					Log.WriteEntry(EventLogEntryType.Warning,
						string.Format("Feed Count Mismatch: expected {0}, received {1}", maxRows, json.Count), "", Alias);
#else
			if (json.Count < rowCheck)
				Log.WriteEntry(EventLogEntryType.Warning,
					string.Format("Feed Count Mismatch: expected {0}, received {1}", maxRows, json.Count), "", Alias);
#endif
		}

		protected MemoryStream GetFeedData(DataGroup group, DateTime exportDate, int firstRow, int maxRows, string extraFields = "")
		{
			var feedUrl = "";

			try
			{
				feedUrl = GetFeedUrl(group);
				MemoryStream resultStream = null;
				if (feedUrl.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
				{
					//special case for accessing local feed files
					//file should be placed in upload folder (or migration folder) and use file:filename as the feedUrl
					var subfolder = _migrationSlave ? "migration" : "upload";
					if (maxRows > 0) // use large file handling to pull only the rows requested
					{
						var data = GetCatalogFileRows(feedUrl, subfolder, maxRows);
						if (string.IsNullOrEmpty(data)) return new MemoryStream();
						return new MemoryStream(Encoding.UTF8.GetBytes(data));
					}
					//oterwise, read the whole file into the memory stream
					if (!TableAccess.Instance.ReadFeedFile(feedUrl, Alias, out resultStream, subfolder))
						throw new Exception(string.Format("Unable to read {0} feed from file", group));
				}
				else //Get result stream from FeedUrl
				{
					var range = "";
					if (maxRows > 0)
						range = string.Format("{0},{1}", firstRow, maxRows);
					var queryParams = GetQueryParams(group, exportDate, range, extraFields);
					resultStream = GetQueryResponse(group, queryParams, feedUrl);
				}

				return resultStream;
			}
			#region Faultcatching

			catch (WebException wex)
			{
				var errMsg = "\nStatus = " + wex.Status.ToString();
				// Get the response stream  
				if (wex.InnerException != null)
					errMsg += "\nInner Exception = " + wex.InnerException.Message;
				var wexResponse = (HttpWebResponse)wex.Response;
				if (wexResponse != null)
				{
					errMsg += "\nRequest = " + wexResponse.ResponseUri;
					wexResponse.Close();
				}
				if (!string.IsNullOrEmpty(feedUrl))
					errMsg += "\n" + GetRequestDetails(feedUrl);

				throw new Exception(wex.Message, new Exception(errMsg));
			}
			catch (Exception ex)
			{
				var errMsg = "\nStackTrace = " + ex.StackTrace;
				if (ex.InnerException != null)
					errMsg += "\nInner Exception = " + ex.InnerException.Message;
				if (!string.IsNullOrEmpty(feedUrl))
					errMsg += "\n" + GetRequestDetails(feedUrl);
				throw new Exception(ex.Message, new Exception(errMsg));
			}
			#endregion
		}

		private string GetCatalogFileRows(string feedUrl, string subfolder, int maxRows)
		{
			FileStream catalogFile = null;
			if (!TableAccess.Instance.ReadFeedFile(feedUrl, Alias, out catalogFile, subfolder))
				throw new Exception(string.Format("Unable to read {0}", feedUrl));
			if (catalogFile == null) return "";
			catalogFile.Seek(_catalogFileSeekPosition, SeekOrigin.Begin);

			var rowEnd = Rules.ApiRowEnd;
			if (rowEnd == null || rowEnd.Length < 1)
				rowEnd = new[] { "],[" }; //default to Json row endings
		
			var data = new StringBuilder();
			var rowCount = 0;
			var chars = new List<char>();
			int nextByte;
			while (rowCount < maxRows && (nextByte = catalogFile.ReadByte()) > 0)
			{
				chars.Add((char)nextByte);
				var test = new String(chars.ToArray());

				foreach (var d in rowEnd)
				{
					if (!test.EndsWith(d)) continue;
					//end of line delimiter found
					rowCount++;
					data.Append(test);
					chars.Clear();
				}
			}
			if (chars.Count > 0) //last row of file
				data.Append(new String(chars.ToArray()));
			_catalogFileSeekPosition = catalogFile.Position;
			catalogFile.Close();

			//using (var sr = new StreamReader(catalogFile))
			//{
			//  //this version uses only \r or \n or \r\n as delimiters
			//  //var oneLine = "";
			//  //while (rowCount < maxRows)
			//  //{
			//  //  oneLine = sr.ReadLine();
			//  //  if (oneLine == null) break; //EOF
			//  //  dat.Add(oneLine + "\n");
			//  //}

			//  while (rowCount < maxRows && sr.Peek() >= 0)
			//  {
			//    c = (char) sr.Read();
			//    chars.Add(c);
			//    var test = new String(chars.ToArray());

			//    foreach (var d in rowEnd)
			//    {
			//      if (!test.EndsWith(d)) continue;
			//      //end of line delimiter found
			//      rowCount++;
			//      data.Append(test);
			//      chars.Clear();
			//    }
			//  }
			//  if (chars.Count > 0) //last row
			//    dat.Add(new String(chars.ToArray()));
			//  _catalogFileSeekPosition = catalogFile.Position;
			//}
			return data.ToString();
		}

		private int GetRowRange(DataGroup group)
		{
			var range = 0;
			switch (group)
			{
				case DataGroup.Catalog:
					range = Rules.ApiForceCatalogRowRange;
					break;
				case DataGroup.Sales:
					range = Rules.ApiForceSalesRowRange;
					break;
				case DataGroup.Customers:
					range = Rules.ApiForceCustomerRowRange;
					break;
				case DataGroup.Inventory:
					range = Rules.ApiForceInventoryRowRange;
					break;
				case DataGroup.CategoryNames:
					range = Rules.ApiForceCategoryRowRange;
					break;
				case DataGroup.ManufacturerNames:
				case DataGroup.DepartmentNames:
				case DataGroup.All:
				case DataGroup.Custom:
					break;
				default:
					throw new ArgumentOutOfRangeException("group");
			}
			return range < 1 ? Rules.ApiForceAllRowRanges : range;
		}

		public MemoryStream ApplyMapping(MemoryStream dataStream)
		{
			//WARNING: inefficient! converts to string to apply character mapping and then converts back to stream
			
			if (dataStream == null) return null;
			if (!Rules.UnescapeFeedData &&
			    (Rules.Fields == null || Rules.Fields.FeedCharMap == null || !Rules.Fields.FeedCharMap.Any()))
				return dataStream;

			var result = "";
			using (var reader = new StreamReader(dataStream, Encoding.UTF8))
			{
				result = reader.ReadToEnd();
			}
			dataStream.Close();
			if (string.IsNullOrEmpty(result)) return null;

			ApplyMapping(ref result);
			return new MemoryStream(Encoding.UTF8.GetBytes(result));
		}

		public void ApplyMapping(ref string data)
		{
			if (string.IsNullOrEmpty(data)) return;

			if (Rules.UnescapeFeedData)
				data = Unescape(data);

			if (Rules.Fields.FeedCharMap != null && Rules.Fields.FeedCharMap.Any())
			{						
				//note: leave as a loop (instead of LINQ) to allow debugging
				foreach (var pair in Rules.Fields.FeedCharMap)
					data = data.Replace(pair.Key, pair.Value);
			}
		}

		/// <summary>	
		/// Send a GET request on the URL specified adn return the HttpWebResponse
		/// Note: This method does not use the CartWebClient
		/// </summary>
		/// <param name="url"></param>
		/// <param name="acceptHeader"></param>
		/// <param name="throwExceptions"></param>
		/// <returns></returns>
		public HttpWebResponse GetResponse(string url, string acceptHeader = "application/xml?", bool throwExceptions = true)
		{
			HttpWebResponse response = null;
			try
			{
				var request = WebRequest.Create(url) as HttpWebRequest;
				if (request == null)
					return null;

				request.Method = "GET";
				request.Accept = acceptHeader;
				//request.Timeout = ??
				request.Credentials = new NetworkCredential(Rules.ApiUserName, Rules.ApiKey);
				ServicePointManager.ServerCertificateValidationCallback =
					((sender, certificate, chain, sslPolicyErrors) => true);

				response = request.GetResponse() as HttpWebResponse;
				return response;
			}
			catch (Exception ex)
			{
				var errMsg = String.Format("Error getting response from {0}", Rules.CartType.ToString());
				var details = String.Format("Response {0}: {1}\n",
				                           response != null ? response.StatusCode.ToString() : "",
				                           response != null ? response.StatusDescription : "none");
				details += GetRequestDetails(url);
				if (Log != null)
					Log.WriteEntry(EventLogEntryType.Error, errMsg, details, ex, Alias);
				if (throwExceptions)
					throw ex;

				return null;
			}
		}

		#endregion				
	}

//class
}

//namespace