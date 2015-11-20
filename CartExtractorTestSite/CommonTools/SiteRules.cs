using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.ServiceModel.Web;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using _4_Tell.CartExtractors;
#if !CART_EXTRACTOR_TEST_SITE
using _4_Tell.DashService;
#endif
using _4_Tell.IO;
using _4_Tell.Logs;
using System.Text.RegularExpressions;

//XElement

namespace _4_Tell.CommonTools
{
	//TODO: Create message resource file for all service messages and displayed text
  //TODO: Add optional culture parameter to all dash methods that return text 

  #region Enums
	public enum DashControlPage
	{
		Rules,
		Uploads
	}

  public enum BoostTier
	{
		Basic,
		Pro1,
		Pro2,
		Pro3,
		Enterprise1,
		Enterprise2,
		Enterprise3
	}

	public enum CartType
	{
		BigCommerce,
		CommerceV3,
		Magento,
		MivaMerchant,
		AspDotNetStorefront,
		NetSuite,
		osCommerce,
		PrestaShop,
		Shopify,
		ThreeDCart,
		Volusion,
		WebsitePipeline,
		JsonFeed,
		XmlFeed,
		TabbedFeed,
		Other,
		Test //Signifies a client alias used for testing --not a customer site
	}

	public enum MagentoLevel
	{
		Go,
		Community,
		Pro,
		Enterprise
	}

	public enum ThreeDCartLevel
	{
		Standard, //Access
		Enterprise //SQL Server
	}

	public enum DataGroup
	{
		All,
		Sales,
		Catalog,
		CategoryNames,
		ManufacturerNames,
		DepartmentNames,
		Customers,
		Inventory,
		Custom,
        Options,
        OrderProducts,
        CustomerAddresses
	}

	public enum DataUpdateStatus
	{
		Ready,
		ExtractorQueued,
		ExtractorBusy,
		GeneratorQueued,
		GeneratorBusy,
		LockedOut
	}

	public enum RecTableType
	{
		None,
		CrossSell,
		Similar,
		Personal 
	}

    #endregion

	/// <summary>
	/// Rule settings for a DashSite
	/// Used to define all necessary settings for the cart extractor
	/// </summary>
	public class SiteRules
	{
		#region Support Classes

		/// <summary>
		/// This list defines the types of rules that are possible
		/// </summary>
		public enum RuleType
		{
			Exclusion,
			Filter,
			CategoryOptimization,
			Upsell,
			CrossCategory,
			AttributeRules,
			Promotion,
			Featured,
			FeaturedCrossSell,
			FeaturedUpSell,
			FeaturedAtt1,
			Replacements,
			Resell,
			ExtractorSchedule,
			RunExtractor,
			ManualUpload
		}

		public class ParseRule
		{
			public string FromField { get; set; }
			public string ToField { get; set; }
			public string RegexMatch { get; set; }
			public int RegexGroup { get; set; }
			public bool Expand { get; set; }
			public int Modulo { get; set; }
			public string Delimiter { get; set; }
			public string Format { get; set; }
		}

		public class ParseGroup
		{
			//public enum ParseGroupType
			//{
			//  combineDistinct,
			//  aggregate
			//}
			public string Delimiter { get; set; }
			//public string RequireAllVales { get; set; }
			//public ParseGroupType GroupType { get; set; }
			public List<ParseRule> ParseRules { get; set; }

			public bool ApplyRules(List<string> header, List<string> data, out List<string> results)
			{
				results = new List<string>();
				var singleVals = new List<string>();
				var expandedVals = new List<string>();
				foreach (var pr in ParseRules)
				{
					//get the raw value to examine
					var raw = Input.GetValue(header, data, pr.FromField);
					if (string.IsNullOrEmpty(raw)) return false;
					//use regex to extract the desired value
					var match = Regex.Match(raw, pr.RegexMatch);
					if (!match.Success) return false;
					var value = match.Groups[pr.RegexGroup].Value;
					if (string.IsNullOrEmpty(value)) return false;

					FinishApplyingRules(value, pr, ref singleVals, ref expandedVals);
				}

				//first, concatenate the single values
				var prefix = singleVals.Aggregate((w, j) => string.Format("{0}{1}{2}", w, Delimiter, j));
				//then add each expanded value
				if (expandedVals.Any(x => !string.IsNullOrEmpty(x)))
					results.AddRange(expandedVals.Select(x => string.Format("{0}{1}{2}", prefix, Delimiter, x)));
				else
					results.Add(prefix);
				return true;
			}

			public bool ApplyRules(XElement record, out List<string> results)
			{
				results = new List<string>();
				var singleVals = new List<string>();
				var expandedVals = new List<string>();
				foreach (var pr in ParseRules)
				{
					//get the raw value to examine
					var raw = Input.GetValue(record, pr.FromField);
					if (string.IsNullOrEmpty(raw)) return false;
					//use regex to extract the desired value
					var match = Regex.Match(raw, pr.RegexMatch);
					if (!match.Success) return false;
					var value = match.Groups[pr.RegexGroup].Value;
					if (string.IsNullOrEmpty(value)) return false;

					FinishApplyingRules(value, pr, ref singleVals, ref expandedVals);
				}

				//first, concatenate the single values
				var prefix = singleVals.Aggregate((w, j) => string.Format("{0}{1}{2}", w, Delimiter, j));
				//then add each expanded value
				if (expandedVals.Any(x => !string.IsNullOrEmpty(x)))
					results.AddRange(expandedVals.Select(x => string.Format("{0}{1}{2}", prefix, Delimiter, x)));
				else
					results.Add(prefix);
				return true;
			}

			private void FinishApplyingRules(string value, ParseRule pr, ref List<string> singleVals, ref List<string> expandedVals)
			{
				if (pr.Expand && !string.IsNullOrEmpty(pr.Delimiter))
				{
					var format = string.IsNullOrEmpty(pr.Format) ? "D" : pr.Format;
					var vals = value.Split(new[] { pr.Delimiter }, StringSplitOptions.RemoveEmptyEntries).ToList();
					//for an integer range, fill in the middle values
					int a, b;
					if (vals.Count() == 2 && int.TryParse(vals[0], out a) && int.TryParse(vals[1], out b))
					{
						var lower = a + 1;
						var upper = b;
						if (pr.Modulo > 0 && upper < lower)
							upper += pr.Modulo;
						var lastVal = vals[1];
						vals.RemoveAt(1);
						for (int i = lower; i < upper; i++)
						{
							var newVal = pr.Modulo > 0 ? i % pr.Modulo : i;
							vals.Add(newVal.ToString(format));
						}
						vals.Add(lastVal);
					}
					expandedVals.AddRange(vals);
				}
				else
					singleVals.Add(value);
			}
		}


		/// <summary>
		/// Data extractor timers to schedule data extraction
		/// </summary>
		public class ExtractorSchedule
		{
			public enum ExtractRate //warning this enum is shared with the dashoard as an int!!
			{
				Hourly,
				Daily,
				Weekly,
				Monthly
			}

			public bool Enabled = false;
			public CartExtractor.ExtractType ExtractType = CartExtractor.ExtractType.Update;
			public ExtractRate Rate = ExtractRate.Daily;
			public int HourOfDay = 0;
			public DayOfWeek DayOfWeek = DayOfWeek.Monday;
			public int WeekOfMonth = 0;
			public int DayOfMonth = 0;

			public ExtractorSchedule(XElement settings)
			{
				if (settings == null)
				{
					//use defaults set above
					return;
				}
				ParseTimerSettings(settings);
			}

			public ExtractorSchedule()
			{
			}

			private void ParseTimerSettings(XElement settings)
			{
				Enabled = Input.GetAttribute(settings, "enabled").Equals("true", StringComparison.OrdinalIgnoreCase);

				var type = Input.GetAttribute(settings, "extractType");
				if (string.IsNullOrEmpty(type) || !Enum.TryParse(type, true, out ExtractType))
					ExtractType = CartExtractor.ExtractType.Update;

				var rate = Input.GetAttribute(settings, "rate");
				if (string.IsNullOrEmpty(rate) || !Enum.TryParse(rate, true, out Rate))
					Rate = ExtractRate.Daily;

				var hourOfDay = Input.GetAttribute(settings, "hourOfDay");
				if (string.IsNullOrEmpty(hourOfDay) || !int.TryParse(hourOfDay, out HourOfDay))
					HourOfDay = 0;

				var dayOfWeek = Input.GetAttribute(settings, "dayOfWeek");
				if (string.IsNullOrEmpty(dayOfWeek) || !Enum.TryParse(dayOfWeek, true, out DayOfWeek))
					DayOfWeek = DayOfWeek.Monday;

				var weekOfMonth = Input.GetAttribute(settings, "weekOfMonth");
				if (string.IsNullOrEmpty(weekOfMonth) || !int.TryParse(weekOfMonth, out WeekOfMonth))
					WeekOfMonth = 1;

				var dayOfMonth = Input.GetAttribute(settings, "dayOfMonth");
				if (string.IsNullOrEmpty(dayOfMonth) || !int.TryParse(dayOfMonth, out DayOfMonth))
					DayOfMonth = 0;
			}

			public bool IsItTime(DateTime now)
			{
				if (!Enabled) return false;
				if (Rate == ExtractRate.Hourly) return true; //always true because we only check hourly
				if (now.Hour != HourOfDay) return false;
				if (Rate == ExtractRate.Daily) return true;
				if (DayOfMonth > 0)
				{
					if (now.Day == DayOfMonth) return true;
					return false;
				}
				if (now.DayOfWeek != DayOfWeek) return false;
				if (Rate == ExtractRate.Weekly) return true;
				//monthly
				switch (WeekOfMonth)
				{
					case 1:
						if (now.Day < 8) return true;
						break;
					case 2:
						if ((now.Day > 7) && (now.Day < 15)) return true;
						break;
					case 3:
						if ((now.Day > 14) && (now.Day < 22)) return true;
						break;
					case 4:
						if ((now.Day > 21) && (now.Day < 29)) return true;
						break;
					case 5:
						if (now.Day > 28) return true;
						break;
					default:
						return false;
				}
				return false;
			}
		}

		#endregion

		#region Default Values

		public const string DefaultTimeZoneId = "Pacific Standard Time";
		public const int DefaultSalesMonths = 18;
		public const int DefaultTopSellDays = 90;
		public const int DefaultClickWeeks = 10;
		public const int DefaultMinLikelihood = 3;
		public const int DefaultMinCommon = 2;
		public const string DefaultAtt1Name = "Category";
		public const string DefaultAtt2Name = "Brand";
		public const int DefaultExtractorLockoutMinutes = 60; //one hour lockout
		public const string DefaultSimilarTopSellerRule = "Att1";
		public const string DefaultSimilarClickStreamRule = "Att1";
		public RecTableType DefaultRecTypesDisabled = RecTableType.None;
		public const float DefaultUpsellFactor = 0.2F;
		private const int DefaultMissingImageThreshold = 10; //10% of items can be excluded due to no images
		//private const int DefaultApiSessionTimeout = 600;
		//private const int DefaultApiResponseTimeout = 600; //timeout in seconds 600 = 10 min
		//private const int DefaultApiMaxTries = 1; //Maximum number of ApiResponseTimeouts allows

		#endregion

		#region External Params

		public const int MaxDashControlPage = (int) DashControlPage.Uploads;

		#region Site Definition
		public string Alias { get; private set; }
		public BoostTier Tier { get; private set; }
		public string StoreShortUrl { get; private set; }
		public List<User> Users = new List<User>();
		public string CartName { get; private set; }
		public CartType CartType { get; private set; } //set and get using CartName and assign matching enum to CartType
		public int CartLevel { get; set; } //must translate to/from level for specific cart
		public float PluginVersion { get; private set; }
		public bool EnableServiceCallDetailLog { get; private set; }
		public TimeZoneInfo SiteTimeZone { get; set; }
		public CatalogMigration MigrationRules { get; set; } //rules that allow catalog migration from an old platform to this one
		#endregion
		
		#region ConfigBoost Parameters
		public bool Resell { get; set; }
		public int MinLikelihood { get; set; }
		public int MinCommon { get; set; }
		public string CurrencySymbol { get; set; }
		public string DecimalSeparator { get; set; }
		public string UniversalFilterName { get; set; }
		public bool FilterTopSellers { get; set; }
		public string SimilarTopSellerRule { get; set; } //valid settings are Att1, Att2 or Att1&2. The default is Att1&2
		public string SimilarClickStreamRule { get; set; } //valid settings are None, Att1 (default), AnyAtt1, Att2, AnyAtt1&2, or Att1&2.
		public float UpsellFactor { get; set; }
		public string FilterFilename { get; set; }
		public bool CreateWhyItems { get; set; }
		public bool CreateCampaignOptimization { get; set; }
		public List<RecTableType> RecTypesDisabled { get; set; }
		public List<string> SalesFilenames { get; set; }
		public List<string> OrdersFilenames { get; set; }
		public List<string> OrderDetailsFilenames { get; set; }
		public List<string> ClickStreamFilenames { get; set; }
		#endregion

		#region Access Rules
		public bool RequireSecureBoost { get; private set; }
		public bool RequireSecureUpload { get; private set; }
		public AuthCredentials AccessCredentials = null;
		public AuthCredentials ExtractorCredentials = null;
		public string ExtractorLoginUrl = null;
		public AuthCredentials ExtractorLoginCredentials = null;

		// Onboarding Credentials (not used by service but sometimes needed for onboarding)
		public string AdminUser { get; private set; }
		public string AdminPassword { get; private set; }
		public string FtpType { get; private set; }
		public string FtpUrl { get; private set; }
		public string FtpUser { get; private set; }
		public string FtpPassword { get; private set; }
		#endregion

		#region Rule Accordions
		public bool UpsellOn { get; set; }
		public bool ResellOn { get; set; }

		private bool _exclusionsOn;
		public bool ExclusionsOn
		{
			get { return _exclusionsOn || !AllowMissingPhotos; }
			set { _exclusionsOn = value; }
		}

		private bool _filtersOn;
		public bool FiltersOn
		{
			get { return _filtersOn || MapCategoriesToFilters; }
			set { _filtersOn = value; }
		}

		public bool PromotionsOn { get; set; }
		public bool ReplacementsOn { get; set; }
		public bool FeaturedOn { get; set; }
		public bool FeaturedCrossSellOn { get; set; }
		public bool FeaturedUpSellOn { get; set; }
		public bool FeaturedAtt1On { get; set; }
		public bool CategoryOptimizationsOn { get; set; }
		public bool CrossCategoryOn { get; set; }
		public bool AttributeRulesOn { get; set; } //TODO: add support for AttributeRules.txt
		public List<ParseGroup> FilterParsingRules { get; private set; }
		public List<Condition> ExclusionRules { get; private set; }
		public LogicSet ExclusionSet { get; private set; }
		public List<Condition> FilterRules { get; private set; }
		public List<Condition> PromotionRules { get; private set; }
		public List<ReplacementCondition> ReplacementRules { get; set; }
		public List<FeaturedRecCondition> FeaturedRules { get; set; }
		public List<FeaturedRecCondition> FeaturedCrossSellRules { get; set; }
		public List<FeaturedRecCondition> FeaturedUpSellRules { get; set; }
		public CategoryConditions CategoryRules { get; private set; } //note some of this needs to be translated for dashboard
		#endregion

		#region Data Extraction
		#region Data Extraction -Dashboard params
		public bool AllowManualUpload; //only blocks client dashboard user (not SA)
		public bool AllowUserExtraction; //only blocks client dashboard user (not SA)
		public CartExtractor.ExtractType LastExtractionType { get; set; }
		public CartExtractor.ExtractType LastDynamicUpdateType { get; set; }
		public DateTime LastExtractionTime { get; set; }
		public DateTime LastDynamicUpdateTime { get; set; }
		public DateTime LastGeneratorTime { get; set; }
		public DateTime LastRuleChangeTime { get; set; }
		public int LastExtractorDuration { get; set; }
		public int LastGeneratorDuration { get; set; }
		public int ExtractorLockoutMinutes { get; set; }
		public List<string> UploadAddresses { get; private set; }
		public List<ExtractorSchedule> ExtractorSchedules { get; private set; }
		public Dictionary<string, int> ExclusionStats { get; set; }
		#endregion

		#region Data Extraction -API connection
		public string ApiUrl { get; set; } //single url to get all data selectively using parameters
		public float ApiVersion { get; set; } //for Json and Xml feed extractors
		public bool ApiCountEnabled { get; set; } //for Json and Xml feed extractors
		public bool ApiCustomerDateRangeEnabled { get; set; } //for Json and Xml feed extractors
		public bool ApiSalesDateRangeEnabled { get; set; } //for Json and Xml feed extractors
		public bool ApiHeaderIsOnlyOnFirstRow { get; set; }
		public string[] ApiRowEnd { get; set; }
		public char[] ApiTrimChars { get; set; }
		public int ApiMinimumCatalogSize { get; set; } //for Json and Xml feed extractors (optional)
		public int ApiForceAllRowRanges { get; set; } //for Json & 3dcart feed extractors (optional)
		public int ApiForceCatalogRowRange { get; set; }
		public int ApiForceCategoryRowRange { get; set; }
		public int ApiForceSalesRowRange { get; set; }
		public int ApiForceCustomerRowRange { get; set; }
		public int ApiForceInventoryRowRange { get; set; }
		public bool ApiAllowExtraRows { get; set; }		//for Json and Xml feed extractors (required for Shopify --allows row range to return extra results)
		public int ApiMaxDaysPerRequest { get; set; } //for 3dcart (could add to others later)
		public string ApiUserName { get; set; }
		public string ApiKey { get; set; }
		public string ApiSecret { get; set; }
		public string ApiAliasParam { get; set; }
		public string ApiUserParam { get; set; }
		public string ApiKeyParam { get; set; }
		public string ApiFieldParam { get; set; }
		public string ApiIdRangeParam { get; set; }
		public string ApiRowRangeParam { get; set; }
		public string ApiDateRangeParam { get; set; }
		public string ApiYearParam { get; set; }
		public string ApiMonthParam { get; set; }
		public string ApiModeParam { get; set; }
		public string ApiResponseFormat { get; set; }
		public string ApiAcceptHeader { get; set; }
		public int ApiExtraHeaders { get; set; }
		public Dictionary<DataGroup, NameValueCollection> ApiAddQueries;
		public CartWebClientConfig WebClientConfig { get; private set; }
		#endregion

		#region Data Extraction -non-API connection
		public string CombinedFeedUrl { get; set; } //single url to get all data (no query parameters)
		public string CatalogFeedUrl { get; set; } //individual feed urls only active if ApiUrl and CombinedFeedUrl are blank
		public string SalesFeedUrl { get; set; }
		public string CustomerFeedUrl { get; set; }
		public string Att1NameFeedUrl { get; set; }
		public string Att2NameFeedUrl { get; set; }
		public string DepartmentNameFeedUrl { get; set; }
		public string InventoryFeedUrl { get; set; }
		#endregion

		#region Data Extraction -parsing rules
		//general
		public int SalesMonthsToExport { get; set; }
		public int CustomerMonthsToExport { get; set; }
		public int ClickStreamWeeksToExport { get; set; }
		public int TopSellRangeInDays { get; set; }
		public bool UnescapeFeedData { get; set; }
		public bool ReduceXmlFeed { get; set; } //used in Volusion
		public bool OmitExtraFields { get; set; }
		public bool UseLargeCatalogHandling { get; set; }
		//sales
		public bool ExtractSalesUpdate { get; set; }
		public bool ExtractSalesFull { get; set; }
		public bool ExtractSalesFromXmlFile { get; set; }
		public bool OrderDateReversed { get; set; }
		//customers
		public bool ExtractCustomerData { get; set; }
		public bool RequireEmailOptIn { get; set; }
		public bool TrackShopperActivity { get; set; }
		public string PersonaMappingFields { get; set; }
		//catalog
		public bool InvalidatePricesOnExtract { get; set; }
		public bool InvalidatePricesOnExtractComplete { get; set; }
		public bool ExtractCatalogFromXmlFile { get; set; }
		public bool ExtractAtt2Names { get; set; }
		public bool AllowLowerCatalogCount { get; set; } //lower than expected
		public bool AllowLowerSalesCount { get; set; } //lower than expected
		public bool AllowLowerCustomerCount { get; set; } //lower than expected
		public bool MapStockToVisibility { get; set; }
		public bool ReverseVisibleFlag { get; set; }
		public bool IgnoreStockUseFlag { get; set; }
		public bool IgnoreStockInPriceRange { get; set; }
		public bool IncludeChildrenInCatalog { get; set; }
		public bool UseAverageChildRating { get; set; }
		public string HiddenSalePriceText { get; set; }
		//categories
		public bool MapCategoriesToFilters { get; set; }
		public bool IncludeCategoryParents { get; set; }
		public bool ExportDepartmentNames { get; set; }
		public bool UseDepartmentsAsCategories { get; set; }
		public string CategorySeparator { get; set; }
		//images
		public string ImageLinkBaseUrl { get; set; }
		public string ImageLinkFormat { get; set; }
		public string PageLinkFormat { get; set; }
		public bool AllowMissingPhotos { get; set; } //do not exclude them
		public int MissingImageThreshold { get; set; } //if !AllowMissingPhotos then what percent missing (excluded) is acceptable?
		public bool ExtrapolateThumbnailPath { get; set; }
		public bool ScrapeCategoryThumbnails { get; set; } //useCacheThumb

		//BigCommerce Image strategy:
		//productNodeSelect: product node selector to obtain a node with both image url and product id
		//imageURLSelect: optional image url selector to select image url inside of product node
		//imageURLPrefix: optional image url prefix (src=")
		//pidSelect: optional pid selector to select product id inside of product node
		//pidPrefix: optional pid prefix (data-products=)

		//for original case (where pid is part of the image url):
		//productNodeSelect contains the old imageURLSelect string
		//imageURLSelect is blank (use the product node outer html)
		//imageURLPrefix = “src=\”"
		//pidSelect is blank (continue using the image url)
		//pidPrefix = “/product/"		

		//used for BigCommerce to get CDN image links
		public bool ForceCategoryTreeScrape { get; set; } //forces tree instead of flat list
		public string ProductNodeSelector { get; set; } //required (in BC) to obtain a node with both image url and product id
		public string ImageUrlSelector { get; set; }		//optional image url selector to select image url inside of product node (null)
		public string ImageUrlPrefix { get; set; }			//optional image url prefix (src=\")
		public string ImageUrlSuffix { get; set; }			//optional image url suffix \")
		public string PidSelector { get; set; }			//optional pid selector to select product id inside of product node (null)
		public string PidPrefix { get; set; }				//optional pid prefix (/product/ OR data-products=\")
		public string PidSuffix { get; set; }				//optional pid suffix (/ OR \")
		public string CommentParseKey { get; set;  } //optional parse key. If exists then convert comments that include the comment parse key to a new node
		#endregion
		
		#region Data Extraction -fieldname overrides
		public DataFieldList Fields { get; private set; }
		#endregion

		#region Data Extraction -internally calculated parameters
		public bool ShopperDataExists;
		public bool CartExtractorExists;
		public bool PluginHandlesExclusions;
		public bool PluginHandlesFilters;
		public bool PluginHandlesCategoryOptimization;
		public bool PluginHandlesReplacements;
		public Dictionary<string, string> TitleCharMap; //defined by specific cartExtractor (can be added to in rules)
		public string[] DataFormatRowEnds { get; set; } //defined by specific cartExtractor
		public char[] DataFormatRowTrims { get; set; } //defined by specific cartExtractor
		#endregion

		#region Data Extraction -RuleAccordions
		public string CrossSellCats
		{
			get
			{
				return CategoryRules.CrossSellCats.Any()
					       ? CategoryRules.CrossSellCats.Aggregate((w, j) => string.Format("{0},{1}", w, j))
					       : "";
			}
		}

		public string RulesEnabled
		{
			get
			{
				var rules = new List<string>();
				if (UpsellOn) rules.Add("UpSell");
				if (ResellOn) rules.Add("Resell");
				if (ExclusionsOn) rules.Add("Exclusions");
				if (FiltersOn) rules.Add("Filter");
				if (CrossCategoryOn) rules.Add("CrossAtt1");
				if (AttributeRulesOn) rules.Add("AttributeRules");
				if (CategoryOptimizationsOn) rules.Add("CatOptimize");
				if (PromotionsOn) rules.Add("Promotions");
				if (ReplacementsOn) rules.Add("Replacements");
				if (FeaturedCrossSellOn) rules.Add("ManualCrossSell");
				if (FeaturedUpSellOn) rules.Add("ManualUpSell");
				if (FeaturedOn) rules.Add("ManualTopSellers");
				if (FeaturedAtt1On) rules.Add("ManualAtt1Items");
				return rules.Any() ? rules.Aggregate((w, j) => string.Format("{0},{1}", w, j)) : "";
			}
		}

		public string RulesDisabled
		{
			get
			{
				var rules = new List<string>();
				if (!UpsellOn) rules.Add("UpSell");
				if (!ResellOn) rules.Add("Resell");
				if (!ExclusionsOn) rules.Add("Exclusions");
				if (!FiltersOn) rules.Add("Filter");
				if (!CrossCategoryOn) rules.Add("CrossAtt1");
				if (!AttributeRulesOn) rules.Add("AttributeRules");
				if (!CategoryOptimizationsOn) rules.Add("CatOptimize");
				if (!PromotionsOn) rules.Add("Promotions");
				if (!ReplacementsOn) rules.Add("Replacements");
				if (!FeaturedOn) rules.Add("ManualTopSellers");
				if (!FeaturedCrossSellOn) rules.Add("ManualCrossSell");
				if (!FeaturedUpSellOn) rules.Add("ManualUpSell");
				if (!FeaturedAtt1On) rules.Add("ManualAtt1Item");
				return rules.Any() ? rules.Aggregate((w, j) => string.Format("{0},{1}", w, j)) : "";
			}
		}
		#endregion
		#endregion

		#endregion

		#region Initialization
		private static Dictionary<CartType, XElement> _cartRules;

		private void InitializeStructures()
		{
			CategoryRules = new CategoryConditions();
			Fields = new DataFieldList(Alias);
			if (_cartRules == null) 
				_cartRules = new Dictionary<CartType, XElement>();
		}

		private void SetDefaultValues()
		{
			//set defaults
			SalesMonthsToExport = DefaultSalesMonths;
			TopSellRangeInDays = DefaultTopSellDays;
			CustomerMonthsToExport = DefaultSalesMonths;
			ClickStreamWeeksToExport = DefaultClickWeeks;
			SimilarTopSellerRule = DefaultSimilarTopSellerRule;
			SimilarClickStreamRule = DefaultSimilarClickStreamRule;
			ExtractorLockoutMinutes = DefaultExtractorLockoutMinutes;
			MissingImageThreshold = DefaultMissingImageThreshold;
			CreateCampaignOptimization = false;
			UpsellFactor = DefaultUpsellFactor;
			MinLikelihood = DefaultMinLikelihood;
			MinCommon = DefaultMinCommon;
			Resell = false;
			SiteTimeZone = TimeZoneInfo.FindSystemTimeZoneById(DefaultTimeZoneId);
			CurrencySymbol = "$";
			DecimalSeparator = ".";
			MigrationRules = null;
		}

		/// <summary>
		/// inialize the site rules using a saved xml blob
		/// </summary>
		/// <param name="alias"></param>
		/// <param name="tier"></param>
		/// <param name="settings"></param>
		public SiteRules(string alias, int tier, XElement settings)
		{
			Alias = alias;
			Tier = (BoostTier) tier;

			InitializeStructures();
			SetDefaultValues();
			ParseSettings(settings);
		}

		/// <summary>
		/// Creates a newset of SiteRules using the default settings for the given cart type
		/// </summary>
		/// <param name="alias"></param>
		/// <param name="url"></param>
		/// <param name="tier"></param>
		/// <param name="cart"></param>
		/// <param name="key"></param>
		public SiteRules(string alias, string url, BoostTier tier, CartType cart, string key, bool saveRules = true)
		{
			Alias = alias;
			StoreShortUrl = CleanUpUrl(url);
			Tier = tier;
			CartType = cart;
			ApiKey = key; //auto-calculated key is removed for certain platforms in SetCartDefaults

			InitializeStructures();
			SetDefaultValues();
			SetCartDefaults();
			if (saveRules)
				QueueSettings();
		}

		/// <summary>
		/// initialize the fieldNames array
		/// needed as a separate call so that it can be updated after the defaults are set
		/// names should be accessed as FieldNames[(int)SiteRules.FieldName]
		/// </summary>
		//public void InitFieldNames()
		//{
		//  for (var i = 0; i < NumFieldNames; i++)
		//    FieldNames[i] = GetFieldName((FieldName) i);
		//}

		//public void SetFieldHeaderIndices(DataGroup group, List<string> header)
		//{
		//  foreach (var field in _fieldNames.Where(x => x.Value.Group.Equals(group)))
		//  {
		//    field.Value.SetIndex(header);
		//  }
		//}

		/// <summary>
		/// Set default values that are cart specific
		/// If init flag is true then some extra defaults will also be set 
		/// such as ApiUrl and Exclusions
		/// </summary>
		/// <param name="init"></param>
		/// <param name="level"></param>
		protected void SetCartDefaults(bool init = true, string level = "")
		{
			//our preferred method is to gather sales realtime
			//this can be overriden by the platform-specific CartExtractor
			ExtractSalesUpdate = false;
			ExtractSalesFull = true;
			ExtractSalesFromXmlFile = false;
			ExtractCatalogFromXmlFile = false;
			InvalidatePricesOnExtract = false;
			InvalidatePricesOnExtractComplete = false;
			ExtractAtt2Names = false;
			ExtractCustomerData = false;
			RequireEmailOptIn = false;
			TrackShopperActivity = false;
			AllowLowerCatalogCount = false;
			AllowLowerSalesCount = false;
			AllowLowerCustomerCount = false;
			AllowManualUpload = true;
			AllowUserExtraction = true;
			ApiCountEnabled = true;
			ApiCustomerDateRangeEnabled = true;
			ApiSalesDateRangeEnabled = true;
			//ApiResponseTimeout = DefaultApiResponseTimeout;
			//ApiSessionTimeout = DefaultApiSessionTimeout;
			//ApiMaxTries = DefaultApiMaxTries;
			EnableServiceCallDetailLog = false;
			ExportDepartmentNames = false;
			//Att1Enabled = true;
			//Att2Enabled = true;
			CartName = CartType.ToString();

			switch (CartType)
			{
				//platform specific feed types
				case CartType.AspDotNetStorefront:
					if (init)
					{
						ApiUrl = "https://" + StoreShortUrl + "/skins/4Tell/FourTellVortxExport.aspx";
						ExtractCustomerData = true;
						ExtractorCredentials = new AuthCredentials
						{
							Type = AuthCredentials.AuthType.AuthParams,
							RequireSsl = true,
							UserName = Alias,
							UserNameParam = "ClientAlias",
							Password = ClientData.Instance.GetServiceKey(Alias),
							PasswordParam = "ServiceKey"
						};
					}
					ExportDepartmentNames = true;
					goto case CartType.JsonFeed;
				case CartType.BigCommerce:
					if (init)
					{
						ApiKey = ""; //clear auto-calculated key --must get it from the cart
						ScrapeCategoryThumbnails = true;
						ExclusionsOn = true;
						ExclusionRules = new List<Condition>();
						ExclusionRules.Add(new Condition("Out Of Stock", "lt", "1", "inventory_level"));
						ExclusionRules.Add(new Condition("Free", "lt", ".01", "price"));
						ImageLinkBaseUrl = "/product_images/";
						OrderDateReversed = true; //default to day/month/year instead of month/day/year --this is not always true
					}
					CartExtractorExists = true;
					PluginHandlesExclusions = false;
					PluginHandlesFilters = false;
					PluginHandlesCategoryOptimization = false;
					PluginHandlesReplacements = false;
					ScrapeCategoryThumbnails = false;
					break;
				case CartType.CommerceV3:
					goto case CartType.JsonFeed;
				case CartType.Magento:
					if (init)
					{
						ApiUrl = "https://" + StoreShortUrl + "/index.php/recommend/api";
						ApiVersion = 3.5F;
						PluginVersion = 3.5F;
						ExtractCustomerData = true;
						ExtractAtt2Names = true;
						ExtractorCredentials = new AuthCredentials
						{
							Type = AuthCredentials.AuthType.AuthParams,
							RequireSsl = true,
							UserName = Alias,
							UserNameParam = "ClientAlias",
							Password = ClientData.Instance.GetServiceKey(Alias),
							PasswordParam = "ServiceKey"
						};
						ApiForceAllRowRanges = 1000;
						ExclusionRules = new List<Condition>();
						ExclusionRules.Add(new Condition("Out Of Stock", "Eq", "Out of Stock", "StockAvailability"));
						ExclusionRules.Add(new Condition("Free", "lt", ".01", "Price"));
					}
					if (PluginVersion > 3) goto case CartType.JsonFeed;
					goto case CartType.Other;
				case CartType.MivaMerchant:
					if (init)
					{
						ApiUrl = "https://" + StoreShortUrl + "/mm5/";
						ExtractorCredentials = new AuthCredentials
							{
								Type = AuthCredentials.AuthType.AuthParams,
								RequireSsl = true,
								UserName = Alias,
								UserNameParam = "ClientAlias",
								Password = ClientData.Instance.GetServiceKey(Alias),
								PasswordParam = "ServiceKey"
							};
						ApiVersion = 3.5F;
						ExtractCustomerData = false;
						ExtractSalesUpdate = true;
						ExclusionsOn = true;
						ExclusionRules = new List<Condition>();
						ExclusionRules.Add(new Condition("Out Of Stock", "lt", "1", "StockLevel"));
						ExclusionRules.Add(new Condition("Free", "lt", ".01", "Price"));
						init = false;
					}					
					goto case CartType.JsonFeed;
				case CartType.NetSuite:
				    if (init)
					{
						ApiKey = ""; //clear auto-calculated key --must get it from the cart
						ScrapeCategoryThumbnails = true;
						ExclusionsOn = true;
						ExclusionRules = new List<Condition>();
						ExclusionRules.Add(new Condition("Out Of Stock", "lt", "1", "inventory_level"));
						ExclusionRules.Add(new Condition("Free", "lt", ".01", "price"));
					}
					CartExtractorExists = true;
					PluginHandlesExclusions = false;
					PluginHandlesFilters = false;
					PluginHandlesCategoryOptimization = false;
					PluginHandlesReplacements = false;
					ScrapeCategoryThumbnails = false;
					ExtractCustomerData = true;
                    break;
				case CartType.Shopify:
				    if (init)
					{
						ApiKey = ""; //clear auto-calculated key --must get it from the cart
						ScrapeCategoryThumbnails = true;
						ExclusionsOn = true;
						ExclusionRules = new List<Condition>();
						ExclusionRules.Add(new Condition("Out Of Stock", "lt", "1", "inventory_level"));
						ExclusionRules.Add(new Condition("Free", "lt", ".01", "price"));
					}
					CartExtractorExists = true;
					PluginHandlesExclusions = false;
					PluginHandlesFilters = false;
					PluginHandlesCategoryOptimization = false;
					PluginHandlesReplacements = false;
					ScrapeCategoryThumbnails = false;
					ExtractCustomerData = true;
                    break;
				case CartType.ThreeDCart:
					if (init)
					{
						ApiKey = ""; //clear auto-calculated key --must get it from the cart
						ApiUrl = StoreShortUrl;
						ExclusionsOn = true;
						ExtractCustomerData = true;
						ExtractSalesUpdate = true;
						ExclusionsOn = true;
						ExclusionRules = new List<Condition>();
						ExclusionRules.Add(new Condition("Not For Sale", "eq", "1", "notforsale"));
						ExclusionRules.Add(new Condition("Non-Searchable", "eq", "1", "nonsearchable"));//
						ExclusionRules.Add(new Condition("Hidden", "eq", "1", "hide"));
						ExclusionRules.Add(new Condition("Out Of Stock", "lt", "1", "stock"));
						ExclusionRules.Add(new Condition("Free", "lt", ".01", "price"));
					}
					CartExtractorExists = true;
					PluginHandlesExclusions = false;
					PluginHandlesFilters = false;
					PluginHandlesCategoryOptimization = false;
					PluginHandlesReplacements = false;
					ThreeDCartLevel tLevel;
					CartLevel = Enum.TryParse(level, true, out tLevel) ? (int) tLevel : (int) ThreeDCartLevel.Standard;
					break;
				case CartType.Volusion:
					if (init)
					{
						ApiKey = ""; //clear auto-calculated key --must get it from the cart
						ExclusionsOn = true;
						ExtractCustomerData = true;
						ExtractSalesUpdate = false;
						ExtractSalesFromXmlFile = false;
						ExtractCatalogFromXmlFile = false;
						//TODO: set (detect?) Volusion CartLevel and use to set XmlFile's true if < gold
						ExclusionRules = new List<Condition>();
						ExclusionRules.Add(new Condition("Free", "lt", ".01", "pe.ProductPrice"));
						ExclusionRules.Add(new Condition("No Price", "eq", "null", "pe.ProductPrice"));
					}
					CartExtractorExists = true;
					PluginHandlesExclusions = false;
					PluginHandlesFilters = false;
					PluginHandlesCategoryOptimization = false;
					PluginHandlesReplacements = false;
					ExtrapolateThumbnailPath = true;
					break;
				case CartType.WebsitePipeline:

				//generic feed types
				case CartType.JsonFeed:
					CartExtractorExists = true;
					PluginHandlesExclusions = false;
					PluginHandlesFilters = false;
					PluginHandlesCategoryOptimization = false;
					PluginHandlesReplacements = false;
					if (DataFormatRowEnds == null || !DataFormatRowEnds.Any())
						DataFormatRowEnds = new[] {"],[", "], [", "],\n[","],\r\n["}; //default to Json row endings
					if (DataFormatRowTrims == null || DataFormatRowTrims.Count() < 1)
						DataFormatRowTrims = new[] {' ', '[', ']', ','}; //quotes must be left for the SplitRow logic
					if (init)
					{
						ApiVersion = 3.5F;
						ExtractCustomerData = true;
						ExtractSalesUpdate = false;
						ExclusionsOn = true;
						if (ExclusionRules == null)
						{
							ExclusionRules = new List<Condition>();
							ExclusionRules.Add(new Condition("Out Of Stock", "lt", "1", "Inventory"));
							ExclusionRules.Add(new Condition("Free", "lt", ".01", "Price"));
						}
					}
					break;
				case CartType.TabbedFeed:
					//TabbedFeedRules.xml maps tabbed feed to json format
					//just need to remove the header row
					if (init)
					{
						ApiExtraHeaders = 1;
					}
					goto case CartType.JsonFeed;
				case CartType.XmlFeed:
					CartExtractorExists = true;
					PluginHandlesExclusions = false;
					PluginHandlesFilters = false;
					PluginHandlesCategoryOptimization = false;
					PluginHandlesReplacements = false;
					break;
				default:
				case CartType.osCommerce:
				case CartType.PrestaShop:
				case CartType.Other:
					CartExtractorExists = false;
					PluginHandlesExclusions = true;
					PluginHandlesFilters = true;
					PluginHandlesCategoryOptimization = true;
					PluginHandlesReplacements = true;
					AllowUserExtraction = false;
					ApiCountEnabled = false;
					ApiCustomerDateRangeEnabled = false;
					ApiSalesDateRangeEnabled = false;
					break;
				case CartType.Test:
					CartExtractorExists = true;
					PluginHandlesExclusions = false;
					PluginHandlesFilters = false;
					PluginHandlesCategoryOptimization = false;
					PluginHandlesReplacements = false;
					break;
			}
		}

		protected void ParseSettings(XElement settings)
		{
			//site definition
			StoreShortUrl = CleanUpUrl(Input.GetValue(settings, "storeUrl"));
			EnableServiceCallDetailLog = Input.GetValue(settings, "enableServiceCallDetailLog").Equals("true", StringComparison.OrdinalIgnoreCase);
			var zone = Input.GetValue(settings, "siteTimeZone");
			if (!string.IsNullOrEmpty(zone))
			{
				try
				{
					SiteTimeZone = TimeZoneInfo.FindSystemTimeZoneById(zone);
				}
				catch {}
			}

			//Security Rules
			RequireSecureBoost = Input.GetValue(settings, "requireSecureBoost").Equals("true");
			RequireSecureUpload = Input.GetValue(settings, "requireSecureUpload").Equals("true");
			UploadAddresses = null;
			var ipList = settings.Elements("approvedUploadIP");
			if (ipList.Any())
				UploadAddresses = ipList.Select(ip => ip.Value).ToList();
			AllowManualUpload = !Input.GetValue(settings, "allowManualUpload").Equals("false"); //default is true
			AllowUserExtraction = !Input.GetValue(settings, "allowUserExtraction").Equals("false"); //default is true

			//track last extraction/generation 
			var extract = Input.GetValue(settings, "lastExtractionType");
			CartExtractor.ExtractType lastExtractType;
			if (Enum.TryParse(extract, true, out lastExtractType))
				LastExtractionType = lastExtractType;
			extract = Input.GetValue(settings, "lastDynamicUpdateType");
			if (Enum.TryParse(extract, true, out lastExtractType))
				LastDynamicUpdateType = lastExtractType;
			var lastDate = Input.GetValue(settings, "lastExtractionTime");
			LastExtractionTime = Input.SafeDateConvert(lastDate, DateTime.MinValue);
			lastDate = Input.GetValue(settings, "lastDynamicUpdateTime");
			LastDynamicUpdateTime = Input.SafeDateConvert(lastDate, DateTime.MinValue);
			lastDate = Input.GetValue(settings, "lastGeneratorTime");
			LastGeneratorTime = Input.SafeDateConvert(lastDate, DateTime.MinValue);
			lastDate = Input.GetValue(settings, "lastRuleChange");
			LastRuleChangeTime = Input.SafeDateConvert(lastDate, DateTime.MinValue);
			LastExtractorDuration = Input.SafeIntConvert(Input.GetValue(settings, "lastExtractorDuration"));
			if (LastExtractorDuration.Equals(0)) 
				LastExtractorDuration = Input.SafeIntConvert(Input.GetValue(settings, "lastExtractionDuration")); //legacy
			LastGeneratorDuration = Input.SafeIntConvert(Input.GetValue(settings, "lastGeneratorDuration"));
			var lockout = Input.SafeIntConvert(Input.GetValue(settings, "extractorLockoutMinutes"), -1);
			ExtractorLockoutMinutes = lockout > -1 ? lockout : DefaultExtractorLockoutMinutes;

			//contact info
			UserContact poc = ParseLegacySettings(settings);
			var userElement = settings.Element("users");
			if (userElement != null)
			{
				var users = userElement.Descendants("user");
				if (users.Any())
				{
					foreach (var u in users)
					{
						Users.Add(new User(u));
					}
					if (poc == null)
					{
						var pocUser = Users.First(x => x.ContactRole.Equals(UserContactRole.Technical));
						if (pocUser != null)
							poc = new UserContact { Email = pocUser.Email, Name = pocUser.Name };
					}
					//else
					//	DataLogProxy.Instance.AddSite(Alias, poc); //add client to the log
				}
			}
		
			#region Data Source Rules

			//Migration rulescatalogMigration
			var migration = settings.Element("catalogMigration");
			if (migration != null)
			{
				var startDate = Input.SafeDateConvert(Input.GetAttribute(migration, "startDate"), DateTime.MinValue);
				var fromField = Input.GetAttribute(migration, "mapFromField");
				var toField = Input.GetAttribute(migration, "mapToField");
				var enabled = !Input.GetAttribute(migration, "enabled").Equals("false");
				var use4TellCatalog = Input.GetAttribute(migration, "use4TellCatalog").Equals("true");
				var use4TellSales = Input.GetAttribute(migration, "use4TellSales").Equals("true");
				MigrationRules = new CatalogMigration(Alias, (int)Tier, startDate, fromField, toField, enabled, true, 
																							SalesMonthsToExport, use4TellCatalog, use4TellSales);
			}

			//set cart extraction defaults
			try
			{
				CartName = Input.GetValue(settings, "cartType");
				PluginVersion = Input.SafeFloatConvert(Input.GetValue(settings, "pluginVersion"));
				CartType cartType;
				CartType = Enum.TryParse(CartName, true, out cartType) ? cartType : CartType.Other;
				var level = Input.GetValue(settings, "cartLevel");
				CartLevel = 0;
				SetCartDefaults(false, level);
			}
			catch (Exception ex)
			{
				CartType = CartType.Other;
				SetCartDefaults();
			}

			//Onboarding Credentials (not used by service but sometimes needed for onboarding)
			AdminUser = Input.GetValue(settings, "adminUser");
			AdminPassword = Input.GetValue(settings, "adminPassword");
			FtpType = Input.GetValue(settings, "ftpType");
			FtpUrl = Input.GetValue(settings, "ftpUrl");
			FtpUser = Input.GetValue(settings, "ftpUser");
			FtpPassword = Input.GetValue(settings, "ftpPassword");

			//API access
			ApiUrl = Input.GetValue(settings, "apiUrl");
			float apiVersion;
			ApiVersion = Input.GetValue(out apiVersion, settings, "apiVersion") ? apiVersion : 0F;	//default zero means no API	
			ApiCountEnabled = !Input.GetValue(settings, "apiCountEnabled").Equals("false", StringComparison.OrdinalIgnoreCase);	//default true	
			ApiCustomerDateRangeEnabled = !Input.GetValue(settings, "apiCustomerDateRangeEnabled").Equals("false", StringComparison.OrdinalIgnoreCase);	//default true	
			ApiSalesDateRangeEnabled = !Input.GetValue(settings, "apiSalesDateRangeEnabled").Equals("false", StringComparison.OrdinalIgnoreCase);	//default true	
			ApiHeaderIsOnlyOnFirstRow = Input.GetValue(settings, "apiHeaderIsOnlyOnFirstRow").Equals("true", StringComparison.OrdinalIgnoreCase);	//default false	
			ApiRowEnd = Input.GetCsvStringList(settings, "apiRowEnd");
			ApiTrimChars = Input.GetCsvCharList(settings, "apiTrimChars");
			ApiMinimumCatalogSize = Input.SafeIntConvert(Input.GetValue(settings, "apiMinimumCatalogSize"), 0);
			ApiForceAllRowRanges = Input.SafeIntConvert(Input.GetValue(settings, "apiForceAllRowRanges"), 0);
			if (ApiForceAllRowRanges < 1)
					ApiForceAllRowRanges = Input.SafeIntConvert(Input.GetValue(settings, "apiForceRowRange"), 0); //depricated
			ApiForceCatalogRowRange = Input.SafeIntConvert(Input.GetValue(settings, "apiForceCatalogRowRange"), 0);
			ApiForceCategoryRowRange = Input.SafeIntConvert(Input.GetValue(settings, "apiForceCategoryRowRange"), 0);
			ApiForceSalesRowRange = Input.SafeIntConvert(Input.GetValue(settings, "apiForceSalesRowRange"), 0);
			ApiForceCustomerRowRange = Input.SafeIntConvert(Input.GetValue(settings, "apiForceCustomerRowRange"), 0);
			ApiForceInventoryRowRange = Input.SafeIntConvert(Input.GetValue(settings, "apiForceInventoryRowRange"), 0);
			ApiAllowExtraRows = Input.GetValue(settings, "apiAllowExtraRows").Equals("true", StringComparison.OrdinalIgnoreCase);
			ApiMaxDaysPerRequest = Input.SafeIntConvert(Input.GetValue(settings, "apiMaxDaysPerRequest"), 0);
			ApiUserName = Input.GetValue(settings, "apiUserName");
			ApiKey = Input.GetValue(settings, "apiKey");
			ApiSecret = Input.GetValue(settings, "apiSecret");
			ApiAliasParam = Input.GetValue(settings, "apiAliasParam");
			ApiUserParam = Input.GetValue(settings, "apiUserParam");
			ApiKeyParam = Input.GetValue(settings, "apiKeyParam");
			ApiFieldParam = Input.GetValue(settings, "apiFieldParam");
			ApiIdRangeParam = Input.GetValue(settings, "apiIdRangeParam");
			ApiRowRangeParam = Input.GetValue(settings, "apiRowRangeParam");
			ApiDateRangeParam = Input.GetValue(settings, "apiDateRangeParam");
			ApiYearParam = Input.GetValue(settings, "apiYearParam");
			ApiMonthParam = Input.GetValue(settings, "apiMonthParam");
			ApiModeParam = Input.GetValue(settings, "apiModeParam");
			ApiResponseFormat = Input.GetValue(settings, "apiResponseFormat");
			ApiAcceptHeader = Input.GetValue(settings, "apiAcceptHeader");
			ApiExtraHeaders = Input.SafeIntConvert(Input.GetValue(settings, "apiExtraHeaders"));
			var additionalQueries = settings.Element("apiAddQueries");
			if (additionalQueries != null)
			{
				var queries = additionalQueries.Descendants("addQuery");
				if (queries.Any())
				{
					ApiAddQueries = new Dictionary<DataGroup, NameValueCollection>();
					foreach (var q in queries)
					{
						//parse query details
						var queryGroup = Input.GetAttribute(q, "group");
						if (string.IsNullOrEmpty(queryGroup)) continue;
						DataGroup dataGroup;
						if (!Enum.TryParse(queryGroup, true, out dataGroup)) continue;
						var name = Input.GetAttribute(q, "name");
						if (string.IsNullOrEmpty(name)) continue;
						var value = Input.GetAttribute(q, "value");
						if (string.IsNullOrEmpty(value)) continue;

						NameValueCollection queryList;
						if (!ApiAddQueries.TryGetValue(dataGroup, out queryList))
						{
							ApiAddQueries.Add(dataGroup, new NameValueCollection {{name, value}});
						}
						else ApiAddQueries[dataGroup].Add(name, value);
					}
				}
			}
			var charMapXml = settings.Element("titleCharMap");
			TitleCharMap = GetCharMapPairs(charMapXml, Alias);
			WebClientConfig = new CartWebClientConfig(settings);

			//cart extraction settings
			CombinedFeedUrl = Input.GetValue(settings, "combinedFeedUrl");
			if (string.IsNullOrEmpty(CombinedFeedUrl))
				CombinedFeedUrl = Input.GetValue(settings, "combinedFeed"); //old name
			CatalogFeedUrl = Input.GetValue(settings, "catalogFeedUrl");
			if (string.IsNullOrEmpty(CatalogFeedUrl))
				CatalogFeedUrl = Input.GetValue(settings, "catalogFeed"); //old name
			SalesFeedUrl = Input.GetValue(settings, "salesFeedUrl");
			if (string.IsNullOrEmpty(SalesFeedUrl))
				SalesFeedUrl = Input.GetValue(settings, "salesFeed"); //old name
			CustomerFeedUrl = Input.GetValue(settings, "customerFeedUrl");
			Att1NameFeedUrl = Input.GetValue(settings, "att1NameFeedUrl");
			if (string.IsNullOrEmpty(Att1NameFeedUrl))
				Att1NameFeedUrl = Input.GetValue(settings, "att1NameFeed"); //old name
			Att2NameFeedUrl = Input.GetValue(settings, "att2NameFeedUrl");
			if (string.IsNullOrEmpty(Att2NameFeedUrl))
				Att2NameFeedUrl = Input.GetValue(settings, "att2NameFeed"); //old name
			DepartmentNameFeedUrl = Input.GetValue(settings, "departmentNameFeedUrl");
			InventoryFeedUrl = Input.GetValue(settings, "inventoryFeedUrl");

			//Access Rules
			var accessCreds = settings.Element("accessCredentials");
			if (accessCreds != null) 
				AccessCredentials = new AuthCredentials(accessCreds);
			var extractorCreds = settings.Element("extractorCredentials");
			if (extractorCreds != null) 
				ExtractorCredentials = new AuthCredentials(extractorCreds);
			else if (AccessCredentials != null) //if no extractor creds provided, make them match the access creds
				ExtractorCredentials = new AuthCredentials(AccessCredentials);
			var loginUrl = Input.GetValue(settings, "extractorLoginUrl");
			if (loginUrl != null)
				ExtractorLoginUrl = loginUrl;
			var loginCreds = settings.Element("extractorLoginCredentials");
			if (loginCreds != null)
				ExtractorLoginCredentials = new AuthCredentials(loginCreds);

			//Extractor Flags	-- only override defaults if a value is set in siterules

			//general (timeframes are set in legacy settings)
			var flag = Input.GetValue(settings, "unescapeFeedData");
			if (!string.IsNullOrEmpty(flag))
				UnescapeFeedData = flag.Equals("true", StringComparison.OrdinalIgnoreCase);
			flag = Input.GetValue(settings, "reduceXmlFeed");
			if (!string.IsNullOrEmpty(flag))
				ReduceXmlFeed = flag.Equals("true", StringComparison.OrdinalIgnoreCase);
			flag = Input.GetValue(settings, "omitExtraFields");
			if (!string.IsNullOrEmpty(flag))
				OmitExtraFields = flag.Equals("true", StringComparison.OrdinalIgnoreCase);
			UseLargeCatalogHandling = Input.GetValue(settings, "useLargeCatalogHandling").Equals("true", StringComparison.OrdinalIgnoreCase);

			//sales
			flag = Input.GetValue(settings, "extractSalesUpdate");
			if (!string.IsNullOrEmpty(flag))
				ExtractSalesUpdate = flag.Equals("true", StringComparison.OrdinalIgnoreCase);
			flag = Input.GetValue(settings, "extractSalesFull");
			if (!string.IsNullOrEmpty(flag))
				ExtractSalesFull = !flag.Equals("false", StringComparison.OrdinalIgnoreCase); //default true
			flag = Input.GetValue(settings, "extractSalesFromXmlFile");
			if (!string.IsNullOrEmpty(flag))
				ExtractSalesFromXmlFile = flag.Equals("true", StringComparison.OrdinalIgnoreCase);
			flag = Input.GetValue(settings, "orderDateReversed");
			if (!string.IsNullOrEmpty(flag))
				OrderDateReversed = flag.Equals("true", StringComparison.OrdinalIgnoreCase);

			//customers
			flag = Input.GetValue(settings, "extractCustomerData");
			if (!string.IsNullOrEmpty(flag))
				ExtractCustomerData = flag.Equals("true", StringComparison.OrdinalIgnoreCase);
			flag = Input.GetValue(settings, "requireEmailOptIn");
			if (!string.IsNullOrEmpty(flag))
				RequireEmailOptIn = flag.Equals("true", StringComparison.OrdinalIgnoreCase);
			flag = Input.GetValue(settings, "trackShopperActivity");
			if (!string.IsNullOrEmpty(flag))
				TrackShopperActivity = flag.Equals("true", StringComparison.OrdinalIgnoreCase);
			PersonaMappingFields = Input.GetValue(settings, "personaMappingFields");

			//catalog
			flag = Input.GetValue(settings, "invalidatePricesOnExtract");
			if (!string.IsNullOrEmpty(flag))
				InvalidatePricesOnExtract = flag.Equals("true", StringComparison.OrdinalIgnoreCase);
			flag = Input.GetValue(settings, "invalidatePricesOnExtractComplete");
			if (!string.IsNullOrEmpty(flag))
				InvalidatePricesOnExtractComplete = flag.Equals("true", StringComparison.OrdinalIgnoreCase);
			flag = Input.GetValue(settings, "extractCatalogFromXmlFile");
			if (!string.IsNullOrEmpty(flag))
				ExtractCatalogFromXmlFile = flag.Equals("true", StringComparison.OrdinalIgnoreCase);
			flag = Input.GetValue(settings, "extractAtt2Names");
			if (string.IsNullOrEmpty(flag))
				flag = Input.GetValue(settings, "exportAtt2Names"); //legacy name
			if (!string.IsNullOrEmpty(flag))
				ExtractAtt2Names = flag.Equals("true", StringComparison.OrdinalIgnoreCase);
			flag = Input.GetValue(settings, "allowLowerCatalogCount");
			if (!string.IsNullOrEmpty(flag))
				AllowLowerCatalogCount = flag.Equals("true", StringComparison.OrdinalIgnoreCase);
			flag = Input.GetValue(settings, "allowLowerSalesCount");
			if (!string.IsNullOrEmpty(flag))
				AllowLowerSalesCount = flag.Equals("true", StringComparison.OrdinalIgnoreCase);
			flag = Input.GetValue(settings, "allowLowerCustomerCount");
			if (!string.IsNullOrEmpty(flag))
				AllowLowerCustomerCount = flag.Equals("true", StringComparison.OrdinalIgnoreCase);
			flag = Input.GetValue(settings, "mapStockToVisibility");
			if (!string.IsNullOrEmpty(flag))
				MapStockToVisibility = flag.Equals("true", StringComparison.OrdinalIgnoreCase);
			flag = Input.GetValue(settings, "reverseVisibleFlag");
			if (!string.IsNullOrEmpty(flag))
				ReverseVisibleFlag = flag.Equals("true", StringComparison.OrdinalIgnoreCase);
			flag = Input.GetValue(settings, "ignoreStockUseFlag");
			if (!string.IsNullOrEmpty(flag))
				IgnoreStockUseFlag = flag.Equals("true", StringComparison.OrdinalIgnoreCase);
			flag = Input.GetValue(settings, "ignoreStockInPriceRange");
			if (!string.IsNullOrEmpty(flag))
				IgnoreStockInPriceRange = flag.Equals("true", StringComparison.OrdinalIgnoreCase);
			flag = Input.GetValue(settings, "includeChildrenInCatalog");
			if (!string.IsNullOrEmpty(flag))
				IncludeChildrenInCatalog = flag.Equals("true", StringComparison.OrdinalIgnoreCase);
			flag = Input.GetValue(settings, "useAverageChildRating");
			if (!string.IsNullOrEmpty(flag))
				UseAverageChildRating = flag.Equals("true", StringComparison.OrdinalIgnoreCase);
			HiddenSalePriceText = Input.GetValue(settings, "hiddenSalePriceText");

			//categories
			MapCategoriesToFilters = Input.GetValue(settings, "mapCategoriesToFilters")
																		.Equals("true", StringComparison.OrdinalIgnoreCase);
			IncludeCategoryParents = Input.GetValue(settings, "includeCategoryParents")
																 .Equals("true", StringComparison.OrdinalIgnoreCase);
			flag = Input.GetValue(settings, "exportDepartmentNames");
			if (!string.IsNullOrEmpty(flag))
				ExportDepartmentNames = flag.Equals("true", StringComparison.OrdinalIgnoreCase);
			flag = Input.GetValue(settings, "useDepartmentsAsCategories");
			if (!string.IsNullOrEmpty(flag))
				UseDepartmentsAsCategories = flag.Equals("true", StringComparison.OrdinalIgnoreCase);
			var catSep = Input.GetValue(settings, "categorySeparator");
			if (!string.IsNullOrEmpty(catSep))
			{
				char charTest;
				if (Input.TryConvert(catSep, out charTest))
					CategorySeparator = charTest.ToString();
				else
					CategorySeparator = catSep;
			}

			//images
			AllowMissingPhotos = false;
			var amp = settings.Element("allowMissingPhotos");
			if (amp != null)
			{
				if (amp.Value != null && amp.Value.Equals("true"))
					AllowMissingPhotos = true;
				else
					AllowMissingPhotos = Input.GetAttribute(amp, "enabled").Equals("true");
			}
			var imageThreshold = Input.SafeIntConvert(Input.GetValue(settings, "missingImageThreshold"), -1);
			if (imageThreshold > -1) MissingImageThreshold = imageThreshold;
			flag = Input.GetValue(settings, "extrapolateThumbnailPath");
			if (!string.IsNullOrEmpty(flag))
				ExtrapolateThumbnailPath = flag.Equals("true");
			flag = Input.GetValue(settings, "scrapeCategoryThumbnails");
			if (string.IsNullOrEmpty(flag))
				flag = Input.GetValue(settings, "useCacheThumb"); //depricated
			if (!string.IsNullOrEmpty(flag))
				ScrapeCategoryThumbnails = !flag.Equals("false"); //default true
			flag = Input.GetValue(settings, "forceCategoryTreeScrape");
			if (!string.IsNullOrEmpty(flag))
				ForceCategoryTreeScrape = flag.Equals("true");
			flag = Input.GetValue(settings, "imageLinkBaseUrl");
			if (string.IsNullOrEmpty(flag))
				flag = Input.GetValue(settings, "photoBaseUrl"); //legacy name
			if (!string.IsNullOrEmpty(flag))
				ImageLinkBaseUrl = flag;
			flag = Input.GetValue(settings, "imageLinkFormat"); //a string.Format parameter that the product id gets plugged into
			if (!string.IsNullOrEmpty(flag))
			{
				if (flag.IndexOf("{0}") >= 0) 
					ImageLinkFormat = flag;
				else if (string.IsNullOrEmpty(ImageLinkBaseUrl))	//missing locator for the id so use it as a baseUrl instead
						ImageLinkBaseUrl = flag;
			}
			if (!string.IsNullOrEmpty(ImageLinkBaseUrl))
			{
				if (!ImageLinkBaseUrl.StartsWith("/") && !ImageLinkBaseUrl.StartsWith("http"))
					ImageLinkBaseUrl = "/" + ImageLinkBaseUrl; //add initial slash
				if (ImageLinkBaseUrl.EndsWith("/"))
					ImageLinkBaseUrl = ImageLinkBaseUrl.Substring(0, ImageLinkBaseUrl.Length - 1); //remove final slash
			}
			flag = Input.GetValue(settings, "pageLinkFormat"); //a string.Format parameter that the product id gets plugged into
			if (!string.IsNullOrEmpty(flag) && flag.Contains("{0}"))
					PageLinkFormat = flag;
			ProductNodeSelector = Input.GetValue(settings, "productNodeSelector");
			ImageUrlSelector = Input.GetValue(settings, "imageUrlSelector");
			ImageUrlPrefix = Input.GetValue(settings, "imageUrlPrefix");
			ImageUrlSuffix = Input.GetValue(settings, "imageUrlSuffix");
			PidSelector = Input.GetValue(settings, "pidSelector");
			PidPrefix = Input.GetValue(settings, "pidPrefix");
			PidSuffix = Input.GetValue(settings, "pidSuffix");
			CommentParseKey = Input.GetValue(settings, "commentParseKey");
			if (string.IsNullOrEmpty(ProductNodeSelector))
				ProductNodeSelector = Input.GetValue(settings, "imageNodeSelector"); //depricated
			if (string.IsNullOrEmpty(ImageUrlPrefix))
				ImageUrlPrefix = Input.GetValue(settings, "imageSrcSelector"); //depricated

			//First check CartRules for defaults
			var cartRules = ReadCartRules(CartType);
			Fields.InitializeFields(cartRules, true);
			Fields.InitializeFields(settings, false);

			//conditional rules
			ExclusionRules = new List<Condition>();
			var exConditions = settings.Element("exclusionConditions");
			if (exConditions != null)
			{
				foreach (var ec in exConditions.Elements("condition"))
				{
					var name = Input.GetAttribute(ec, "name");
					var comparison = Input.GetAttribute(ec, "comparison");
					var value = Input.GetAttribute(ec, "value");
					var fieldName = Input.GetAttribute(ec, "fieldName");
					var conditionType = Input.GetAttribute(ec, "type");
					//var resultField = FieldsIncludeTablePrefix ? StripTablePrefix(fieldName) : fieldName;
					try
					{
						ExclusionRules.Add(new Condition(name, comparison, value, fieldName)); //, resultField));
					}
					catch (Exception ex)
					{
						if (BoostLog.Instance != null)
							BoostLog.Instance.WriteEntry(EventLogEntryType.Error, "Error creating exclusion rule", ex, Alias);
					}
				}
			}
			var exSet = settings.Element("exclusionSet");
			if (exSet != null)
			{
				ExclusionSet = new LogicSet(Alias, exSet);
			}

			FilterParsingRules = null;
			var filterParsing = settings.Element("filterParsingRules");
			if (filterParsing != null)
			{
				FilterParsingRules = new List<ParseGroup>();
				var parseGroups = filterParsing.Elements("parseGroup");
				if (parseGroups != null)
				{
					var tempVal = "";
					foreach (var g in parseGroups)
					{
						var newGroup = new ParseGroup
																{
																	Delimiter = Input.GetAttribute(g, "delimiter")
																};

						var parseRules = g.Elements("parseRule");
						if (parseRules == null) continue;
						newGroup.ParseRules = new List<ParseRule>();
						foreach (var pr in parseRules)
						{
							newGroup.ParseRules.Add(new ParseRule
																			{
																				FromField = Input.GetAttribute(pr, "fromField"),
																				ToField = Input.GetAttribute(pr, "toField"),
																				RegexMatch = Input.GetAttribute(pr, "regexMatch"),
																				RegexGroup = Input.SafeIntConvert(Input.GetAttribute(pr, "regexGroup")),
																				Expand = Input.GetAttribute(pr, "expand").Equals("true"),
																				Modulo = Input.SafeIntConvert(Input.GetAttribute(pr, "modulo")),
																				Delimiter = Input.GetAttribute(pr, "delimiter"),
																				Format = Input.GetAttribute(pr, "format")
																			});
						}
						FilterParsingRules.Add(newGroup);
					}
				}
			}

			FilterRules = new List<Condition>();
			var filterConditions = settings.Element("filterConditions");
			if (filterConditions != null)
			{
				foreach (var f in filterConditions.Elements("condition"))
				{
					var name = Input.GetAttribute(f, "name");
					var comparison = Input.GetAttribute(f, "comparison");
					var value = Input.GetAttribute(f, "value");
					var fieldName = Input.GetAttribute(f, "fieldName");
					//var resultField = FieldsIncludeTablePrefix ? StripTablePrefix(fieldName) : fieldName;
					try
					{
						FilterRules.Add(new Condition(name, comparison, value, fieldName)); //, resultField));
					}
					catch (Exception ex)
					{
						if (BoostLog.Instance != null)
							BoostLog.Instance.WriteEntry(EventLogEntryType.Error, "Error creating filter rule", ex, Alias);
					}
				}
			}
			UniversalFilterName = Input.GetValue(settings, "universalFilterName");
			if (UniversalFilterName.Length < 1)
				UniversalFilterName = "Universal";
			FilterTopSellers = Input.GetValue(settings, "filterTopSellers").Equals("true", StringComparison.CurrentCultureIgnoreCase);

			ReplacementRules = new List<ReplacementCondition>();
			var repConditions = settings.Element("replacementConditions");
			if (repConditions != null)
			{
				foreach (var rep in repConditions.Elements("condition"))
				{
					var name = Input.GetAttribute(rep, "name");
					var type = Input.GetAttribute(rep, "type");
					var oldName = Input.GetAttribute(rep, "oldFieldName");
					var newName = Input.GetAttribute(rep, "newFieldName");
					var oldResultField = oldName; //FieldsIncludeTablePrefix ? StripTablePrefix(oldName) : oldName;
					var newResultField = newName; //FieldsIncludeTablePrefix ? StripTablePrefix(newName) : newName;
					ReplacementRules.Add(new ReplacementCondition(name, type, oldName, newName, oldResultField, newResultField));
				}
			}

			var catConditions = settings.Element("categoryConditions");
			if (catConditions != null)
			{
				foreach (var cat in catConditions.Elements("condition"))
				{
					var groupId = Input.GetAttribute(cat, "groupId");
					var type = Input.GetAttribute(cat, "type");
					var value = Input.GetAttribute(cat, "value");
					CategoryRules.AddCat(type, value, groupId);
				}
			}

			//Featured selections
			var featuredConditions = settings.Element("featuredConditions") ??
															 settings.Element("manualTopSellConditions"); //depricated
			if (featuredConditions != null)
			{
				FeaturedRules = new List<FeaturedRecCondition>();
				foreach (var cat in featuredConditions.Elements("condition"))
				{
					var queryField = Input.GetAttribute(cat, "fieldName");
					var resultField = queryField; //FieldsIncludeTablePrefix ? StripTablePrefix(queryField) : queryField;
					var include = Input.GetAttribute(cat, "type").Equals("include", StringComparison.OrdinalIgnoreCase);
					var enabled = !Input.GetAttribute(cat, "enabled").Equals("false", StringComparison.OrdinalIgnoreCase);
					FeaturedRules.Add(new FeaturedRecCondition(queryField, resultField, include, enabled));
				}
			}

			var featuredCrossSellConditions = settings.Element("featuredCrossSellConditions") ??
																				settings.Element("manualCrossSellConditions");  //depricated
			if (featuredCrossSellConditions != null)
			{
				FeaturedCrossSellRules = new List<FeaturedRecCondition>();
				foreach (var cat in featuredCrossSellConditions.Elements("condition"))
				{
					var queryField = Input.GetAttribute(cat, "fieldName");
					var resultField = queryField; //FieldsIncludeTablePrefix ? StripTablePrefix(queryField) : queryField;
					var include = Input.GetAttribute(cat, "type").Equals("include", StringComparison.OrdinalIgnoreCase);
					var enabled = !Input.GetAttribute(cat, "enabled").Equals("false", StringComparison.OrdinalIgnoreCase);
					FeaturedCrossSellRules.Add(new FeaturedRecCondition(queryField, resultField, include, enabled));
				}
			}

			var featuredUpSellConditions = settings.Element("featuredUpSellConditions") ??
																		 settings.Element("manualUpSellConditions");  //depricated
			if (featuredUpSellConditions != null)
			{
				FeaturedUpSellRules = new List<FeaturedRecCondition>();
				foreach (var cat in featuredUpSellConditions.Elements("condition"))
				{
					var queryField = Input.GetAttribute(cat, "fieldName");
					var resultField = queryField; //FieldsIncludeTablePrefix ? StripTablePrefix(queryField) : queryField;
					var include = Input.GetAttribute(cat, "type").Equals("include", StringComparison.OrdinalIgnoreCase);
					var enabled = !Input.GetAttribute(cat, "enabled").Equals("false", StringComparison.OrdinalIgnoreCase);
					FeaturedUpSellRules.Add(new FeaturedRecCondition(queryField, resultField, include, enabled));
				}
			}

			//timers
			var upTimers = settings.Elements("updateTimer");
			if (!upTimers.Any()) 
				upTimers = settings.Elements("uploadTimer"); //depricated
			if (upTimers.Any())
			{
				ExtractorSchedules = new List<ExtractorSchedule>();
				foreach (var ut in upTimers)
				{
					ExtractorSchedules.Add(new ExtractorSchedule(ut));
				}
			}

			//Exclusion Stats
			var exclusionStats = settings.Elements("exclusionStats");
			if (exclusionStats.Any())
			{
				var stats = exclusionStats.Elements("stat");
				if (stats != null && stats.Any())
				{
					ExclusionStats = new Dictionary<string, int>();
					foreach (var s in stats)
					{
						var key = Input.GetAttribute(s, "name");
						var value = Input.SafeIntConvert(Input.GetAttribute(s, "value"));
						ExclusionStats.Add(key, value);
					}
				}
			}

			#endregion
		}

		/// <summary>
		/// Apply legacy settings. These could come from SiteRules.xml or ConfigBoost.txt
		/// </summary>
		public UserContact ParseLegacySettings(XElement settings)
		{
			var attName = Input.GetValue(settings, "attribute1Name");
			if (!string.IsNullOrEmpty(attName))
				Fields.Att1Name = attName;
			attName = Input.GetValue(settings, "attribute2Name");
			if (!string.IsNullOrEmpty(attName))
				Fields.Att2Name = attName;
			var rules = Input.GetValue(settings, "rulesEnabled");
			if (string.IsNullOrEmpty(rules))
				rules = Input.GetValue(settings, "rulesType"); //older version
			if (!string.IsNullOrEmpty(rules))
			{
				rules = rules.ToLower();
				var ruleList = rules.Split(new[] {','});
				UpsellOn = ruleList.Contains("upsell");
				ResellOn = ruleList.Contains("resell");
				ExclusionsOn = ruleList.Contains("exclusions");
				FiltersOn = ruleList.Contains("filter");
				CrossCategoryOn = ruleList.Contains("crossatt1");
				AttributeRulesOn = ruleList.Contains("attributerules");
				CategoryOptimizationsOn = ruleList.Contains("catoptimize");
				PromotionsOn = ruleList.Contains("promotions");
				ReplacementsOn = ruleList.Contains("replacements");
				FeaturedCrossSellOn = ruleList.Contains("manualcrosssell");
				FeaturedUpSellOn = ruleList.Contains("manualupsell");
				FeaturedOn = ruleList.Contains("manualtopsellers") || ruleList.Contains("manualtopsell");
				FeaturedAtt1On = ruleList.Contains("manualatt1item");
			}
			int rangeToExport; //cannot use a property in an "out" variable
			if (Input.GetValue(out rangeToExport, settings, "salesMonthsToExport"))
				SalesMonthsToExport = rangeToExport;
			else if (Input.GetValue(out rangeToExport, settings, "monthsToExport")) //depricated
				SalesMonthsToExport = rangeToExport;
			else if (Input.GetValue(out rangeToExport, settings, "maxSalesDataAgeInMonths")) //name used in configboost
				SalesMonthsToExport = rangeToExport;
			if (Input.GetValue(out rangeToExport, settings, "topSellRangeInDays"))
				TopSellRangeInDays = rangeToExport;
			if (Input.GetValue(out rangeToExport, settings, "customerMonthsToExport"))
				CustomerMonthsToExport = rangeToExport;
			else if (Input.GetValue(out rangeToExport, settings, "maxCustomersDataAgeInMonths")) //name used in configboost
				CustomerMonthsToExport = rangeToExport;
			if (Input.GetValue(out rangeToExport, settings, "clickStreamWeeksToExport"))
				ClickStreamWeeksToExport = rangeToExport;
			else if (Input.GetValue(out rangeToExport, settings, "maxClickStreamDataAgeInWeeks")) //name used in configboost
				ClickStreamWeeksToExport = rangeToExport;
			int minLikelihood;
			if (Input.GetValue(out minLikelihood, settings, "minLikelihood"))
				MinLikelihood = minLikelihood;
			int minCommon;
			if (Input.GetValue(out minCommon, settings, "minCommon"))
				MinCommon = minCommon;
			var currency = Input.GetValue(settings, "currencySymbol");
			if (string.IsNullOrEmpty(currency)) 
				currency = Input.GetValue(settings, "currency"); //legacy name
			CurrencySymbol = string.IsNullOrEmpty(currency) ? "$" : currency;
			currency = Input.GetValue(settings, "decimalSeparator");
			DecimalSeparator = string.IsNullOrEmpty(currency) ? "." : currency;
			SimilarTopSellerRule = Input.GetValue(settings, "similarTopSellerRule");
			if (string.IsNullOrEmpty(SimilarTopSellerRule)) SimilarTopSellerRule = DefaultSimilarTopSellerRule;
			SimilarClickStreamRule = Input.GetValue(settings, "similarClickStreamRule");
			if (string.IsNullOrEmpty(SimilarClickStreamRule)) SimilarClickStreamRule = DefaultSimilarClickStreamRule;
			float factor;
			UpsellFactor = Input.GetValue(out factor, settings, "upsellFactor") ? factor : DefaultUpsellFactor;
			FilterFilename = Input.GetValue(settings, "filterFilename");
			CreateWhyItems = Input.GetValue(settings, "createWhyItems").Equals("true"); //default is false
			CreateCampaignOptimization = Input.GetValue(settings, "createCampaignOptimization").Equals("true"); //default is false

			RecTypesDisabled = new List<RecTableType>();
			var disabledTypes = Input.GetValue(settings, "RecTypesDisabled");
			if (!string.IsNullOrEmpty(disabledTypes))
			{
				RecTableType t;
				foreach (var d in disabledTypes.Split(new[] {','}))
				{
					if (string.IsNullOrEmpty(d)
					    || !Enum.TryParse(d, true, out t)
					    || t.Equals(RecTableType.None)) continue;
					RecTypesDisabled.Add(t);
				}
			}
			List<string> filenames;
			SalesFilenames = Input.GetValue(out filenames, settings, "salesFilenames") ? filenames : null;
			OrdersFilenames = Input.GetValue(out filenames, settings, "ordersFilenames") ? filenames : null;
			OrderDetailsFilenames = Input.GetValue(out filenames, settings, "orderDetailsFilenames") ? filenames : null;
			ClickStreamFilenames = Input.GetValue(out filenames, settings, "clickStreamFilenames") ? filenames : null;


			UserContact poc = null;
			var pocEmail = Input.GetValue(settings, "pocEmail");
			if (!string.IsNullOrEmpty(pocEmail))
			{
				var atindex = pocEmail.IndexOf('@');
				if (atindex > 0 && !Users.Any(x => x.Email.Equals(pocEmail, StringComparison.OrdinalIgnoreCase)))
				{
					var pocName = Input.GetValue(settings, "pocName");
					if (string.IsNullOrEmpty(pocName)) pocName = pocEmail.Substring(0, atindex);
					//Traditionaly the clientSettings POC was the technical contact
					poc = new UserContact {Email = pocEmail, Name = pocName};
					Users.Add(new User
						{
							Name = poc.Name,
							Email = poc.Email,
							ContactRole = UserContactRole.Technical
						});
					var level = Input.GetValue(settings, "reportLevel");
					if (!string.IsNullOrEmpty(level))
					{
						try
						{
							var report = (ReportType) Enum.Parse(typeof (ReportType), level, true);
							if (BoostLog.Instance != null)
								BoostLog.Instance.Subscriptions.Add(Alias, poc, report);
						}
						catch
						{
						}
					}
				}
			}
			return poc;
		}

		/// <summary>
		/// Cleanup Urls to remove prefixes and leading and trailing slashes.
		/// Optional flag will force a https prefix
		/// </summary>
		/// <param name="url"></param>
		/// <returns></returns>
		private static string CleanUpUrl(string url, bool forceSsl = false)
		{
			if (string.IsNullOrEmpty(url)) return "";

			//adjust prefix
			var prefix = url.IndexOf("//", System.StringComparison.Ordinal) + 2;
			if (prefix > 1) url = url.Remove(0, prefix); 
			if (forceSsl) url = "https://" + url;

			//remove trailing slash
			if (url.EndsWith("/")) url = url.Remove(url.Length - 1); 

			return url;
		}

		public void QueueSettings(bool ruleChange = true)
		{
			var wc = new WebContextProxy();
#if WEB
			var wh = new WebHelper();
			wc = wh.GetContextOfRequest();
#endif
			Task.Factory.StartNew(() => SaveSettings(wc, ruleChange));
		}

		public void SaveSettings(WebContextProxy wc = null, bool ruleChange = true)
		{
			try
			{
				var settings = new XElement("siteRules");

				//basic site definition
				settings.Add(new XElement("alias", Alias));
				settings.Add(new XElement("tier", Tier.ToString()));
				settings.Add(new XElement("storeUrl", StoreShortUrl));
				if (!SiteTimeZone.Id.Equals(DefaultTimeZoneId))
					settings.Add(new XElement("siteTimeZone", SiteTimeZone.Id));
				settings.Add(new XElement("cartType", CartName)); //set and get using CartName and assign matching enum to CartType
				if (PluginVersion > 0) 
					settings.Add(new XElement("pluginVersion", PluginVersion));
				//TODO: save and retrieve cartlevel as a string (with legacy support for ints)
				settings.Add(new XElement("cartLevel", CartLevel));
				if (EnableServiceCallDetailLog) settings.Add(new XElement("enableServiceCallDetailLog", "true"));

				//Access Rules
				if (RequireSecureBoost) 
					settings.Add(new XElement("requireSecureBoost", RequireSecureBoost));
				if (RequireSecureUpload) 
					settings.Add(new XElement("requireSecureUpload", RequireSecureUpload));
				if (AccessCredentials != null) 
					settings.Add(AccessCredentials.ToXml("accessCredentials"));
				if (ExtractorCredentials != null) 
					settings.Add(ExtractorCredentials.ToXml("extractorCredentials"));
				if (ExtractorLoginCredentials != null) 
					settings.Add(ExtractorLoginCredentials.ToXml("extractorLoginCredentials"));
				if (!string.IsNullOrEmpty(ExtractorLoginUrl)) 
					settings.Add(new XElement("extractorLoginUrl", ExtractorLoginUrl));
				if (UploadAddresses != null && UploadAddresses.Any())
					settings.Add(new XElement("uploadAddresses", UploadAddresses.Aggregate((w, j) => string.Format("{0},{1}", w, j))));
				settings.Add(new XElement("lastExtractionType", LastExtractionType));
				if (LastDynamicUpdateType != CartExtractor.ExtractType.None)
					settings.Add(new XElement("lastDynamicUpdateType", LastDynamicUpdateType));
				if (LastExtractionTime != DateTime.MinValue)
					settings.Add(new XElement("lastExtractionTime", LastExtractionTime));
				if (LastDynamicUpdateTime != DateTime.MinValue)
					settings.Add(new XElement("lastDynamicUpdateTime", LastDynamicUpdateTime));
				if (LastGeneratorTime != DateTime.MinValue)
					settings.Add(new XElement("lastGeneratorTime", LastGeneratorTime));
				if (ruleChange) LastRuleChangeTime = DateTime.Now;
				if (LastRuleChangeTime != DateTime.MinValue)
					settings.Add(new XElement("lastRuleChange", LastRuleChangeTime));
				if (LastExtractorDuration > 0)
					settings.Add(new XElement("lastExtractorDuration", LastExtractorDuration));
				if (LastGeneratorDuration > 0)
					settings.Add(new XElement("lastGeneratorDuration", LastGeneratorDuration));
				if (ExtractorLockoutMinutes != DefaultExtractorLockoutMinutes)
					settings.Add(new XElement("extractorLockoutMinutes", ExtractorLockoutMinutes));
				if (!AllowManualUpload)
					settings.Add(new XElement("allowManualUpload", "false")); //default is true
				if (!AllowUserExtraction)
					settings.Add(new XElement("allowUserExtraction", "false")); //default is true


				//Data Extraction -parsing rules
				//general

				//hidden admin rules
				if (Resell)
					settings.Add(new XElement("resell", "true"));
				if (!MinLikelihood.Equals(DefaultMinLikelihood))
					settings.Add(new XElement("minLikelihood", MinLikelihood));
				if (!MinCommon.Equals(DefaultMinCommon))
					settings.Add(new XElement("minCommon", MinCommon));
				if (!CurrencySymbol.Equals("$"))
					settings.Add(new XElement("currencySymbol", CurrencySymbol));
				if (!DecimalSeparator.Equals("."))
					settings.Add(new XElement("decimalSeparator", DecimalSeparator));
				if (!SimilarTopSellerRule.Equals(DefaultSimilarTopSellerRule))
					settings.Add(new XElement("similarTopSellerRule", SimilarTopSellerRule));
				if (!SimilarClickStreamRule.Equals(DefaultSimilarClickStreamRule))
					settings.Add(new XElement("similarClickStreamRule", SimilarClickStreamRule));
				if (!UpsellFactor.Equals(DefaultUpsellFactor))
					settings.Add(new XElement("upsellFactor", UpsellFactor));
				if (!string.IsNullOrEmpty(FilterFilename))
					settings.Add(new XElement("filterFilename", FilterFilename));
				if (CreateWhyItems)
					settings.Add(new XElement("createWhyItems", "true"));
				if (CreateCampaignOptimization)
					settings.Add(new XElement("createCampaignOptimization", "true"));
				if (RecTypesDisabled != null && RecTypesDisabled.Any())
					settings.Add(new XElement("recTypesDisabled", RecTypesDisabled.Aggregate("", (w, j) => string.Format("{0},{1}", w, j.ToString()))));
				if (SalesFilenames != null && SalesFilenames.Any(x => !string.IsNullOrEmpty(x)))
					settings.Add(new XElement("salesFilenames", SalesFilenames.Aggregate((w, j) => string.Format("{0},{1}", w, j))));
				if (OrdersFilenames != null && OrdersFilenames.Any(x => !string.IsNullOrEmpty(x)))
					settings.Add(new XElement("ordersFilenames", OrdersFilenames.Aggregate((w, j) => string.Format("{0},{1}", w, j))));
				if (OrderDetailsFilenames != null && OrderDetailsFilenames.Any(x => !string.IsNullOrEmpty(x)))
					settings.Add(new XElement("orderDetailsFilenames", OrderDetailsFilenames.Aggregate((w, j) => string.Format("{0},{1}", w, j))));
				if (ClickStreamFilenames != null && ClickStreamFilenames.Any(x => !string.IsNullOrEmpty(x)))
					settings.Add(new XElement("clickStreamFilenames", ClickStreamFilenames.Aggregate((w, j) => string.Format("{0},{1}", w, j))));
					

				//Onboarding Credentials (not used by service but sometimes needed for onboarding)
				if (!string.IsNullOrEmpty(AdminUser))
					settings.Add(new XElement("adminUser", AdminUser));
				if (!string.IsNullOrEmpty(AdminPassword))
					settings.Add(new XElement("adminPassword", AdminPassword));
				if (!string.IsNullOrEmpty(FtpType))
					settings.Add(new XElement("ftpType", FtpType));
				if (!string.IsNullOrEmpty(FtpUrl))
					settings.Add(new XElement("ftpUrl", FtpUrl));
				if (!string.IsNullOrEmpty(FtpUser))
					settings.Add(new XElement("ftpUser", FtpUser));
				if (!string.IsNullOrEmpty(FtpPassword))
					settings.Add(new XElement("ftpPassword", FtpPassword));

				//Api settings
				if (!string.IsNullOrEmpty(ApiUrl))
					settings.Add(new XElement("apiUrl", ApiUrl));
				if (!string.IsNullOrEmpty(ApiUserName))
					settings.Add(new XElement("apiUserName", ApiUserName));
				if (ApiVersion > 0)
					settings.Add(new XElement("apiVersion", ApiVersion));
				if (!ApiCountEnabled)
					settings.Add(new XElement("apiCountEnabled", "false"));
				if (!ApiCustomerDateRangeEnabled)
					settings.Add(new XElement("apiCustomerDateRangeEnabled", "false"));
				if (!ApiSalesDateRangeEnabled)
					settings.Add(new XElement("apiSalesDateRangeEnabled", "false"));
				if (ApiHeaderIsOnlyOnFirstRow)
					settings.Add(new XElement("apiHeaderIsOnlyOnFirstRow", "true"));
				var rowEnd = Input.SetCsvStringList(ApiRowEnd);
				if (!string.IsNullOrEmpty(rowEnd))
					settings.Add(new XElement("apiRowEnd", rowEnd));
				var trimChars = Input.SetCsvCharList(ApiTrimChars);
				if (!string.IsNullOrEmpty(trimChars))
					settings.Add(new XElement("apiTrimChars", trimChars));
				if (ApiMinimumCatalogSize > 0)
					settings.Add(new XElement("apiMinimumCatalogSize", ApiMinimumCatalogSize));
				if (ApiForceAllRowRanges > 0)
					settings.Add(new XElement("apiForceAllRowRanges", ApiForceAllRowRanges));
				if (ApiForceCatalogRowRange > 0)
					settings.Add(new XElement("apiForceCatalogRowRange", ApiForceCatalogRowRange));
				if (ApiForceCategoryRowRange > 0)
					settings.Add(new XElement("apiForceCategoryRowRange", ApiForceCategoryRowRange));
				if (ApiForceSalesRowRange > 0)
					settings.Add(new XElement("apiForceSalesRowRange", ApiForceSalesRowRange));
				if (ApiForceCustomerRowRange > 0)
					settings.Add(new XElement("apiForceCustomerRowRange", ApiForceCustomerRowRange));
				if (ApiForceInventoryRowRange > 0)
					settings.Add(new XElement("apiForceInventoryRowRange", ApiForceInventoryRowRange));
				if (ApiAllowExtraRows)
					settings.Add(new XElement("apiAllowExtraRows", "true"));
				if (ApiMaxDaysPerRequest > 0)
					settings.Add(new XElement("apiMaxDaysPerRequest", ApiMaxDaysPerRequest));
				if (!string.IsNullOrEmpty(ApiKey))
					settings.Add(new XElement("apiKey", ApiKey));
				if (!string.IsNullOrEmpty(ApiSecret))
					settings.Add(new XElement("apiSecret", ApiSecret));
				if (!string.IsNullOrEmpty(ApiAliasParam))
					settings.Add(new XElement("apiAliasParam", ApiAliasParam));
				if (!string.IsNullOrEmpty(ApiUserParam))
					settings.Add(new XElement("apiUserParam", ApiUserParam));
				if (!string.IsNullOrEmpty(ApiKeyParam))
					settings.Add(new XElement("apiKeyParam", ApiKeyParam));
				if (!string.IsNullOrEmpty(ApiFieldParam))
					settings.Add(new XElement("apiFieldParam", ApiFieldParam));
				if (!string.IsNullOrEmpty(ApiRowRangeParam))
					settings.Add(new XElement("apiRowRangeParam", ApiRowRangeParam));
				if (!string.IsNullOrEmpty(ApiIdRangeParam))
					settings.Add(new XElement("apiIdRangeParam", ApiIdRangeParam));
				if (!string.IsNullOrEmpty(ApiDateRangeParam))
					settings.Add(new XElement("apiDateRangeParam", ApiDateRangeParam));
				if (!string.IsNullOrEmpty(ApiYearParam))
					settings.Add(new XElement("apiYearParam", ApiYearParam));
				if (!string.IsNullOrEmpty(ApiMonthParam))
					settings.Add(new XElement("apiMonthParam", ApiMonthParam));
				if (!string.IsNullOrEmpty(ApiModeParam))
					settings.Add(new XElement("apiModeParam", ApiModeParam));
				if (!string.IsNullOrEmpty(ApiResponseFormat))
					settings.Add(new XElement("apiResponseFormat", ApiResponseFormat));
				if (!string.IsNullOrEmpty(ApiAcceptHeader))
					settings.Add(new XElement("apiAcceptHeader", ApiAcceptHeader));
				if (ApiExtraHeaders > 0)
					settings.Add(new XElement("apiExtraHeaders", ApiExtraHeaders));
				if (ApiAddQueries != null && ApiAddQueries.Any())
				{
					var queries = new XElement("apiAddQueries");
					foreach (var group in ApiAddQueries)
					{
						var groupName = group.Key;
						foreach (var name in group.Value.AllKeys)
						{
							var add1 = new XElement("addQuery", 
																 new XAttribute("group", groupName),
																 new XAttribute("name", name),
																 new XAttribute("value", group.Value[name]));
							queries.Add(add1);
						}
					}
					settings.Add(queries);
				}
				if (TitleCharMap != null && TitleCharMap.Any())
				{
					var pairs = new XElement("titleCharMap");
					foreach (var pair in TitleCharMap)
					{
						char test;
						string from, to;
						if (!Input.TryConvert(pair.Key, out test)
							|| !Input.TryConvert(test, out from))
							from = pair.Key;
						if (!Input.TryConvert(pair.Value, out test)
							|| !Input.TryConvert(test, out to))
							to = pair.Value;
						pairs.Add(new XElement("mapPair",
														new XAttribute("from", from),
														new XAttribute("to", to)));
					}
					settings.Add(pairs);
				}
				if (WebClientConfig != null)
					settings.Add(WebClientConfig.Xml);

				if (!string.IsNullOrEmpty(CombinedFeedUrl))
					settings.Add(new XElement("combinedFeedUrl", CombinedFeedUrl));
				if (!string.IsNullOrEmpty(CatalogFeedUrl))
					settings.Add(new XElement("catalogFeedUrl", CatalogFeedUrl));
				if (!string.IsNullOrEmpty(SalesFeedUrl))
					settings.Add(new XElement("salesFeedUrl", SalesFeedUrl));
				if (!string.IsNullOrEmpty(CustomerFeedUrl))
					settings.Add(new XElement("customerFeedUrl", CustomerFeedUrl));
				if (!string.IsNullOrEmpty(Att1NameFeedUrl))
					settings.Add(new XElement("att1NameFeedUrl", Att1NameFeedUrl));
				if (!string.IsNullOrEmpty(Att2NameFeedUrl))
					settings.Add(new XElement("att2NameFeedUrl", Att2NameFeedUrl));
				if (!string.IsNullOrEmpty(DepartmentNameFeedUrl))
					settings.Add(new XElement("departmentNameFeedUrl", DepartmentNameFeedUrl));
				if (!string.IsNullOrEmpty(InventoryFeedUrl))
					settings.Add(new XElement("inventoryFeedUrl", InventoryFeedUrl));

				//Data Extraction -parsing rules
				//general
				if (!SalesMonthsToExport.Equals(DefaultSalesMonths))
					settings.Add(new XElement("salesMonthsToExport", SalesMonthsToExport));
				if (!CustomerMonthsToExport.Equals(DefaultSalesMonths))
					settings.Add(new XElement("customerMonthsToExport", CustomerMonthsToExport));
				if (!ClickStreamWeeksToExport.Equals(DefaultClickWeeks))
					settings.Add(new XElement("clickStreamWeeksToExport", ClickStreamWeeksToExport));
				if (!TopSellRangeInDays.Equals(DefaultTopSellDays))
					settings.Add(new XElement("topSellRangeInDays", TopSellRangeInDays));
				if (UnescapeFeedData)
					settings.Add(new XElement("unescapeFeedData", "true"));
				if (ReduceXmlFeed)
					settings.Add(new XElement("reduceXmlFeed", "true"));
				if (OmitExtraFields)
					settings.Add(new XElement("omitExtraFields", "true"));
				if (UseLargeCatalogHandling)
					settings.Add(new XElement("useLargeCatalogHandling", "true"));
				//sales
				if (ExtractSalesUpdate)
					settings.Add(new XElement("extractSalesUpdate", "true"));
				if (!ExtractSalesFull)
					settings.Add(new XElement("extractSalesFull", "false")); //default true
				if (ExtractSalesFromXmlFile)
					settings.Add(new XElement("extractSalesFromXmlFile", "true"));
				if (OrderDateReversed)
					settings.Add(new XElement("orderDateReversed", "true"));
				//customers
				if (ExtractCustomerData)
					settings.Add(new XElement("extractCustomerData", "true"));
				if (RequireEmailOptIn)
					settings.Add(new XElement("requireEmailOptIn", "true"));
				if (TrackShopperActivity)
					settings.Add(new XElement("trackShopperActivity", "true"));
				if (!string.IsNullOrEmpty(PersonaMappingFields))
					settings.Add(new XElement("personaMappingFields", PersonaMappingFields));
				//catalog
				if (InvalidatePricesOnExtract)
					settings.Add(new XElement("invalidatePricesOnExtract", "true"));
				if (InvalidatePricesOnExtractComplete)
					settings.Add(new XElement("invalidatePricesOnExtractComplete", "true"));
				if (ExtractCatalogFromXmlFile)
					settings.Add(new XElement("extractCatalogFromXmlFile", "true"));
				if (ExtractAtt2Names)
					settings.Add(new XElement("extractAtt2Names", "true"));
				if (AllowLowerCatalogCount)
					settings.Add(new XElement("allowLowerCatalogCount", "true"));
				if (AllowLowerSalesCount)
					settings.Add(new XElement("allowLowerSalesCount", "true"));
				if (AllowLowerCustomerCount)
					settings.Add(new XElement("allowLowerCustomerCount", "true"));
				if (MapStockToVisibility)
					settings.Add(new XElement("mapStockToVisibility", "true"));
				if (ReverseVisibleFlag)
					settings.Add(new XElement("reverseVisibleFlag", "true")); 
				if (IgnoreStockUseFlag)
					settings.Add(new XElement("ignoreStockUseFlag", "true")); 
				if (IgnoreStockInPriceRange)
					settings.Add(new XElement("ignoreStockInPriceRange", "true")); 
				if (IncludeChildrenInCatalog)
					settings.Add(new XElement("includeChildrenInCatalog", "true"));
				if (UseAverageChildRating)
					settings.Add(new XElement("useAverageChildRating", "true")); 
				if (!string.IsNullOrEmpty(HiddenSalePriceText))
					settings.Add(new XElement("hiddenSalePriceText", HiddenSalePriceText));
				//categories
				if (MapCategoriesToFilters)
					settings.Add(new XElement("mapCategoriesToFilters", "true"));
				if (IncludeCategoryParents)
					settings.Add(new XElement("includeCategoryParents", "true"));
				if (ExportDepartmentNames)
					settings.Add(new XElement("exportDepartmentNames", "true"));
				if (UseDepartmentsAsCategories)
					settings.Add(new XElement("useDepartmentsAsCategories", "true"));
				if (!string.IsNullOrEmpty(CategorySeparator))
					settings.Add(new XElement("categorySeparator", CategorySeparator));
				//inages
				if (AllowMissingPhotos)
					settings.Add(new XElement("allowMissingPhotos", "true"));
				if (!MissingImageThreshold.Equals(DefaultMissingImageThreshold))
					settings.Add(new XElement("missingImageThreshold", MissingImageThreshold));
				if (ExtrapolateThumbnailPath)
					settings.Add(new XElement("extrapolateThumbnailPath", "true"));
				if (!ScrapeCategoryThumbnails)
					settings.Add(new XElement("scrapeCategoryThumbnails", "false"));
				if (ForceCategoryTreeScrape)
					settings.Add(new XElement("forceCategoryTreeScrape", "true"));
				if (!string.IsNullOrEmpty(ImageLinkBaseUrl))
					settings.Add(new XElement("imageLinkBaseUrl", ImageLinkBaseUrl));
				if (!string.IsNullOrEmpty(ImageLinkFormat))
					settings.Add(new XElement("imageLinkFormat", ImageLinkFormat));
				if (!string.IsNullOrEmpty(PageLinkFormat))
					settings.Add(new XElement("pageLinkFormat", PageLinkFormat));
				if (!string.IsNullOrEmpty(ProductNodeSelector))
					settings.Add(new XElement("productNodeSelector", ProductNodeSelector));
				if (!string.IsNullOrEmpty(ImageUrlSelector))
					settings.Add(new XElement("imageUrlSelector", ImageUrlSelector));
				if (!string.IsNullOrEmpty(ImageUrlPrefix))
					settings.Add(new XElement("imageUrlPrefix", ImageUrlPrefix));
				if (!string.IsNullOrEmpty(ImageUrlSuffix))
					settings.Add(new XElement("imageUrlSuffix", ImageUrlSuffix));
				if (!string.IsNullOrEmpty(PidSelector))
					settings.Add(new XElement("pidSelector", PidSelector));
				if (!string.IsNullOrEmpty(PidPrefix))
					settings.Add(new XElement("pidPrefix", PidPrefix));
				if (!string.IsNullOrEmpty(PidSuffix))
					settings.Add(new XElement("pidSuffix", PidSuffix));
				if (!string.IsNullOrEmpty(CommentParseKey))
					settings.Add(new XElement("commentParseKey", CommentParseKey));


				//user details
				if (Users.Any())
				{
					var users = new XElement("users");
					foreach (var u in Users)
					{
						users.Add(u.ToXml("user"));
					}
					settings.Add(users);
				}

				//catalog migration
				if (MigrationRules != null)
				{
					//test validity date each time rules are saved
					MigrationRules.CheckDate(SalesMonthsToExport);
					var migration = new XElement("catalogMigration",
																new XAttribute("enabled", MigrationRules.Enabled),
						                    new XAttribute("startDate", MigrationRules.StartDate.ToShortDateString()),
																new XAttribute("mapFromField", MigrationRules.MapFromField),
																new XAttribute("mapToField", MigrationRules.MapToField),
																new XAttribute("use4TellCatalog", MigrationRules.Use4TellCatalog),
																new XAttribute("use4TellSales", MigrationRules.Use4TellSales)
						);
					settings.Add(migration);
				}

				// moved to DataField.cs
				////Need to only save fieldnames if current value does not equal the default
				//foreach (var f in _fieldNames.Where(f => !f.Value.IsDefault))
				//{
				//  settings.Add(new XElement(GetRuleNameFromField(f.Key), f.Value.Name));
				//}

				////Additions to the standard field list
				//if (AddStandardFields != null && AddStandardFields.Any())
				//  settings.Add(new XElement("addStandardFields", AddStandardFields.Aggregate((c, j) => string.Format("{0},{1}", c, j))));

				////Alternate Price/Page/Image Fields
				//if (AlternatePriceFields != null && AlternatePriceFields.Any())
				//  settings.Add(new XElement("alternatePriceFields", AlternatePriceFields.Aggregate((c, j) => string.Format("{0},{1}", c, j))));
				//if (AlternatePageFields != null && AlternatePageFields.Any())
				//  settings.Add(new XElement("alternatePageFields", AlternatePageFields.Aggregate((c, j) => string.Format("{0},{1}", c, j))));
				//if (AlternateImageFields != null && AlternateImageFields.Any())
				//  settings.Add(new XElement("alternateImageFields", AlternateImageFields.Aggregate((c, j) => string.Format("{0},{1}", c, j))));

				//conditional rules
				settings.Add(new XElement("rulesEnabled", RulesEnabled));
				if (ExclusionRules != null && ExclusionRules.Any())
				{
					var rules = new XElement("exclusionConditions");
					foreach (var rule in ExclusionRules)
					{
						var c = new XElement("condition",
						                     new XAttribute("name", rule.Name),
						                     new XAttribute("comparison", rule.Comparison),
						                     new XAttribute("value", rule.Value),
						                     new XAttribute("fieldName", rule.QueryField)
							);
						rules.Add(c);
					}
					settings.Add(rules);
				}
				if (ExclusionSet != null)
				{
					settings.Add(ExclusionSet.GetXml("exclusionSet"));
				}

				if (FilterRules != null && FilterRules.Any())
				{
					var rules = new XElement("filterConditions");
					foreach (var rule in FilterRules)
					{
						var c = new XElement("condition",
						                     new XAttribute("name", rule.Name),
						                     new XAttribute("comparison", rule.Comparison),
						                     new XAttribute("value", rule.Value),
						                     new XAttribute("fieldName", rule.QueryField)
							);
						rules.Add(c);
					}
					settings.Add(rules);
				}
				if (!string.IsNullOrEmpty(UniversalFilterName) && !UniversalFilterName.Equals("universal", StringComparison.OrdinalIgnoreCase))
					settings.Add(new XElement("universalFilterName", UniversalFilterName));
				if (FilterTopSellers)
					settings.Add(new XElement("filterTopSellers", "true"));

				if (FilterParsingRules != null && FilterParsingRules.Any())
				{
					var rules = new XElement("filterParsingRules");
					foreach (var group in FilterParsingRules)
					{
						var g = new XElement("parseGroup",
																 new XAttribute("delimiter", group.Delimiter)
							);
						foreach (var rule in group.ParseRules)
						{
							XElement r;
							if (rule.Expand)
								r = new XElement("parseRule",
																		 new XAttribute("fromField", rule.FromField),
																		 new XAttribute("toField", rule.ToField),
																		 new XAttribute("regexMatch", rule.RegexMatch),
																		 new XAttribute("regexGroup", rule.RegexGroup),
																		 new XAttribute("expand", "true"),
																		 new XAttribute("modulo", rule.Modulo),
																		 new XAttribute("delimiter", rule.Delimiter),
																		 new XAttribute("format", rule.Format)
									);
							else
								r = new XElement("parseRule",
																		 new XAttribute("fromField", rule.FromField),
																		 new XAttribute("toField", rule.ToField),
																		 new XAttribute("regexMatch", rule.RegexMatch),
																		 new XAttribute("regexGroup", rule.RegexGroup)
									);
							g.Add(r);
						}
						rules.Add(g);
					}
					settings.Add(rules);
				}

				if (FeaturedCrossSellRules != null && FeaturedCrossSellRules.Any())
				{
					var rules = new XElement("featuredCrossSellConditions");
					foreach (var rule in FeaturedCrossSellRules)
					{
						var c = new XElement("condition",
						                     new XAttribute("type", rule.Include ? "include" : "exclude"),
																 new XAttribute("fieldName", rule.QueryField),
																 new XAttribute("enabled", rule.Enabled)
							);
						rules.Add(c);
					}
					settings.Add(rules);
				}
				if (FeaturedUpSellRules != null && FeaturedUpSellRules.Any())
				{
					var rules = new XElement("featuredUpSellConditions");
					foreach (var rule in FeaturedUpSellRules)
					{
						var c = new XElement("condition",
						                     new XAttribute("type", rule.Include ? "include" : "exclude"),
																 new XAttribute("fieldName", rule.QueryField),
																 new XAttribute("enabled", rule.Enabled)
							);
						rules.Add(c);
					}
					settings.Add(rules);
				}
				if (FeaturedRules != null && FeaturedRules.Any())
				{
					var rules = new XElement("featuredTopSellConditions");
					foreach (var rule in FeaturedRules)
					{
						var c = new XElement("condition",
						                     new XAttribute("type", rule.Include ? "include" : "exclude"),
						                     new XAttribute("fieldName", rule.QueryField),
						                     new XAttribute("enabled", rule.Enabled)
							);
						rules.Add(c);
					}
					settings.Add(rules);
				}
				if (ReplacementRules != null && ReplacementRules.Any())
				{
					var rules = new XElement("replacementConditions");
					foreach (var rule in ReplacementRules)
					{
						var c = new XElement("condition",
						                     new XAttribute("name", rule.Name),
						                     new XAttribute("type", rule.Type.ToString()),
																 new XAttribute("oldFieldName", rule.OldName),
																 new XAttribute("newFieldName", rule.NewName)
							);
						rules.Add(c);
					}
					settings.Add(rules);
				}
				if (CategoryRules != null)
				{
					var rules = new XElement("categoryConditions");
					if (CategoryRules.OptimizationsExist)
					{
						foreach (var cat in CategoryRules.Optimizations)
						{
							var c = new XElement("condition",
							                     new XAttribute("type", "ignore"),
							                     new XAttribute("value", cat)
								);
							rules.Add(c);
						}
					}
					if (CategoryRules.CrossCategoryExist)
					{
						foreach (var cat in CategoryRules.CrossSellCats)
						{
							var c = new XElement("condition",
							                     new XAttribute("type", "crossSell"),
							                     new XAttribute("value", cat)
								);
							rules.Add(c);
						}
					}
					if (CategoryRules.ExclusionsExist)
					{
						foreach (var cat in CategoryRules.Exclusions)
						{
							var c = new XElement("condition",
							                     new XAttribute("type", "exclude"),
							                     new XAttribute("value", cat)
								);
							rules.Add(c);
						}
					}
					if (CategoryRules.FiltersExist)
					{
						foreach (var filter in CategoryRules.Filters)
						{
							var c = new XElement("condition",
							                     new XAttribute("type", "filter"),
							                     new XAttribute("value", filter.CatId),
							                     new XAttribute("groupId", filter.GroupId)
								);
							rules.Add(c);
						}
					}
					if (CategoryRules.UniversalExist)
					{
						foreach (var cat in CategoryRules.Universals)
						{
							var c = new XElement("condition",
							                     new XAttribute("type", "universal"),
							                     new XAttribute("value", cat)
								);
							rules.Add(c);
						}
					}
					if (rules.Descendants().Any())
						settings.Add(rules);
				}
				//upload timers
				if (ExtractorSchedules != null && ExtractorSchedules.Any())
				{
					foreach (var timer in ExtractorSchedules)
					{
						var t = new XElement("updateTimer",
																 new XAttribute("enabled", timer.Enabled),
																 new XAttribute("extractType", timer.ExtractType),
																 new XAttribute("rate", timer.Rate)
							);
						if (timer.Rate != ExtractorSchedule.ExtractRate.Hourly)
						{
							t.Add(new XAttribute("hourOfDay", timer.HourOfDay));
							if (timer.Rate == ExtractorSchedule.ExtractRate.Weekly)
								t.Add(new XAttribute("dayOfWeek", timer.DayOfWeek));
							else if (timer.Rate == ExtractorSchedule.ExtractRate.Monthly)
							{
								t.Add(new XAttribute("dayOfWeek", timer.DayOfWeek));
								t.Add(new XAttribute("dayOfMonth", timer.DayOfMonth));
							}
						}
						settings.Add(t);
					}
				}

				//Exclusion Stats
				if (ExclusionStats != null && ExclusionStats.Any())
				{
					var stats = new XElement("exclusionStats");
					foreach (var es in ExclusionStats)
					{
						var s = new XElement("stat",
																	new XAttribute("name", es.Key),
																	new XAttribute("value", es.Value)
							);
						stats.Add(s);
					}
					settings.Add(stats);
				}
				if (Fields != null) Fields.SaveSettings(ref settings);

				ClientData.Instance.SaveSiteRules(Alias, settings, this, wc);
			}
			catch (Exception ex)
			{
				if (BoostLog.Instance != null)
					BoostLog.Instance.WriteEntry(EventLogEntryType.Error, "Error in SaveSettings", ex, Alias);
			}
		}

		public StringBuilder FormatGeneratorConfig()
		{
			var data = new StringBuilder();

			data.Append("Version\t3\r\n");
			if (Fields != null)
			{
				if (Fields.Att1Enabled)
					data.Append("Attribute1Name\t" + Fields.Att1Name + "\r\n");
				if (Fields.Att2Enabled)
					data.Append("Attribute2Name\t" + Fields.Att2Name + "\r\n");
			}
			var enabled = RulesEnabled; //copy to local since this is calculated on-th-fly
			if (!string.IsNullOrEmpty(enabled))
				data.Append("RulesEnabled\t" + enabled + "\r\n");
			if (FiltersOn && !string.IsNullOrEmpty(UniversalFilterName))
				data.Append("UniversalFilterIDs\t" + UniversalFilterName + "\r\n");
			if (FilterTopSellers)
				data.Append("FilterTopSellers\ttrue\r\n");
			if (CrossCategoryOn)
			{
				var crossAtt1Ids = CrossSellCats; //copy to local since this is calculated on-th-fly
				if (!string.IsNullOrEmpty(crossAtt1Ids))
					data.Append("CrossAtt1IDs\t" + crossAtt1Ids + "\r\n");
			}

			//Infrequently used (or usually default)
			if (!string.IsNullOrEmpty(CurrencySymbol) && !CurrencySymbol.Equals("$"))
				data.Append("CurrencySymbol\t" + CurrencySymbol + "\r\n");
			if (!string.IsNullOrEmpty(DecimalSeparator) && !DecimalSeparator.Equals("."))
				data.Append("DecimalSeparator\t" + DecimalSeparator + "\r\n");
			if (MinLikelihood != DefaultMinLikelihood)
				data.Append("MinLikelihood\t" + MinLikelihood.ToString(CultureInfo.InvariantCulture) + "\r\n");
			if (MinCommon != DefaultMinCommon)
				data.Append("MinCommon\t" + MinCommon.ToString(CultureInfo.InvariantCulture) + "\r\n");
			if (TopSellRangeInDays != DefaultTopSellDays)
				data.Append("TopSellRangeInDays\t" + TopSellRangeInDays + "\r\n");
			if (SalesMonthsToExport != DefaultSalesMonths)
				data.Append("MaxSalesDataAgeInMonths\t" + SalesMonthsToExport + "\r\n");
			if (CustomerMonthsToExport != DefaultSalesMonths)
				data.Append("MaxCustomersDataAgeInMonths\t" + CustomerMonthsToExport + "\r\n");
			if (ClickStreamWeeksToExport != DefaultClickWeeks)
				data.Append("MaxClickStreamDataAgeInWeeks\t" + ClickStreamWeeksToExport + "\r\n");
			if (SalesFilenames != null && SalesFilenames.Any(x => !string.IsNullOrEmpty(x)))
				data.Append("SalesFilenames\t" + SalesFilenames.Aggregate((w, j) => string.Format("{0},{1}", w, j)) + "\r\n");
			if (OrdersFilenames != null && OrdersFilenames.Any(x => !string.IsNullOrEmpty(x)))
				data.Append("OrdersFilenames\t" + OrdersFilenames.Aggregate((w, j) => string.Format("{0},{1}", w, j)) + "\r\n");
			if (OrderDetailsFilenames != null && OrderDetailsFilenames.Any(x => !string.IsNullOrEmpty(x)))
				data.Append("OrderDetailsFilenames\t" + OrderDetailsFilenames.Aggregate((w, j) => string.Format("{0},{1}", w, j)) + "\r\n");
			if (ClickStreamFilenames != null && ClickStreamFilenames.Any(x => !string.IsNullOrEmpty(x)))
				data.Append("ClickStreamFilenames\t" + ClickStreamFilenames.Aggregate((w, j) => string.Format("{0},{1}", w, j)) + "\r\n");
			if (!SimilarTopSellerRule.Equals(DefaultSimilarTopSellerRule))
				data.Append("SimilarTopSellerRule\t" + SimilarTopSellerRule + "\r\n");
			if (!SimilarClickStreamRule.Equals(DefaultSimilarClickStreamRule))
				data.Append("SimilarClickStreamRule\t" + SimilarClickStreamRule + "\r\n");
			if (!UpsellFactor.Equals(DefaultUpsellFactor))
				data.Append("UpsellFactor\t" + UpsellFactor.ToString("N") + "\r\n");
			if (CreateWhyItems)
				data.Append("CreateWhyItems\ttrue\r\n");
			if (CreateCampaignOptimization)
				data.Append("CreateCampaignOptimization\ttrue\r\n");
			if (RecTypesDisabled != null && RecTypesDisabled.Any())
			{
				var disabledTypes = RecTypesDisabled.Where(x => !x.Equals(RecTableType.None));
				if (disabledTypes.Any())
					data.Append("RecTypesDisabled\t" + disabledTypes.Aggregate("",(w, j) => string.Format("{0},{1}", w, j.ToString())) + "\r\n");
			}

			return data;
		}

#if !CART_EXTRACTOR_TEST_SITE
        public License.LicenseInfo GetLicense()
		{
			return ClientData.Instance.GetLicense(Alias);
		}
#endif
		#endregion

		#region Dashboard Support

#if !USAGE_READONLY
		public DashSiteRule[] GetSiteRuleDefs(ref DashSite site, int page = 0)
		{
			if (site == null) //should never be possible
				throw new Exception(string.Format("Unable to GetSiteRuleDefs for {0}: the site is invalid", Alias));

			var dashpage = (page >= 0 && page <= MaxDashControlPage) ? (DashControlPage) page : DashControlPage.Rules;
			
			const string reasonTierTooLow = "This rule requires a Pro or Enterprise subscription";
			const string reasonHandledByPlugin = "This rule is controled in your plugin configuration";
			const string reasonNoExtractor = "This rule must be setup in the system that feeds your data to our service";

			var rules = new List<DashSiteRule>();
			bool enabled;
			string statusDesc;

			if (dashpage.Equals(DashControlPage.Rules))
			{
				//Exclusions
				enabled = CartExtractorExists && !PluginHandlesExclusions; //available for all tiers 
				statusDesc = PluginHandlesExclusions
					             ? reasonHandledByPlugin
					             : (!CartExtractorExists
						                ? reasonNoExtractor
						                : "");
				rules.Add(new DashSiteRule
					{
						RuleId = (int) RuleType.Exclusion,
						RuleName = RuleType.Exclusion.ToString(),
						RuleDisplayName = "Exclusions",
						RuleDesc = "Create rules to identify items that will be excluded from recommendations",
						IsVisible = true,
						IsEnabled = enabled,
						StatusDesc = statusDesc,
						IsOn = enabled && ExclusionsOn
					});

				//Filters
				enabled = CartExtractorExists && !PluginHandlesFilters && (Tier > BoostTier.Basic);
				statusDesc = PluginHandlesFilters
					             ? reasonHandledByPlugin
					             : (!CartExtractorExists
						                ? reasonNoExtractor
						                : (!(Tier > BoostTier.Basic)
							                   ? reasonTierTooLow
							                   : ""));
				rules.Add(new DashSiteRule
					{
						RuleId = (int) RuleType.Filter,
						RuleName = RuleType.Filter.ToString(),
						RuleDisplayName = "Filters",
						RuleDesc = "Create rules to define filter groups and universal items in the catalog."
						           + " Only items that share a filter group or are universal will be recommended.",
						IsVisible = true,
						IsEnabled = enabled,
						StatusDesc = statusDesc,
						IsOn = enabled && FiltersOn
					});

				//Category Cleanup
				enabled = !PluginHandlesCategoryOptimization; // && (Tier > BoostTier.Basic);	//available for all carts
				statusDesc = PluginHandlesCategoryOptimization
					             ? reasonHandledByPlugin
					             //: (!(Tier > BoostTier.Basic) ? reasonTierTooLow
					             : ""; //);
				rules.Add(new DashSiteRule
					{
						RuleId = (int) RuleType.CategoryOptimization,
						RuleName = RuleType.CategoryOptimization.ToString(),
						RuleDisplayName = "Category Cleanup",
						RuleDesc = "When exporting your catalog, we only want to record key categories that help describe the products."
						           + " Select categories here that should be trimmed from our list."
						           +
						           " For example: 'Sales Items' and 'Featured Items' are groupings that do not help describe the items and should be trimmed.",
						IsVisible = true,
						IsEnabled = enabled,
						StatusDesc = statusDesc,
						IsOn = enabled && CategoryOptimizationsOn
					});

				//Upsell
				enabled = true; //available for all carts and all tiers
				statusDesc = "";
				rules.Add(new DashSiteRule
					{
						RuleId = (int) RuleType.Upsell,
						RuleName = RuleType.Upsell.ToString(),
						RuleDisplayName = "Upsell",
						RuleDesc = "Choose whether to consider price when ranking similar items",
						IsVisible = true,
						IsEnabled = enabled,
						StatusDesc = statusDesc,
						IsOn = enabled && UpsellOn
					});

				//CrossCategory
				enabled = (Tier > BoostTier.Basic); //available for all carts
				statusDesc = !enabled
					             ? reasonTierTooLow
					             : "";
				rules.Add(new DashSiteRule
					{
						RuleId = (int) RuleType.CrossCategory,
						RuleName = RuleType.CrossCategory.ToString(),
						RuleDisplayName = "Cross-Category",
						RuleDesc = "List categories where cross-sell recommendations should not be in the same category as the main item."
						           +
						           " For example: If you sell shoes, and a customer is looking at a shoe you may want to force cross-sell"
						           + " recommendations to come from other categories like pants or belts.",
						IsVisible = true,
						IsEnabled = enabled,
						StatusDesc = statusDesc,
						IsOn = enabled && CrossCategoryOn
					});

				//Promotions
				rules.Add(new DashSiteRule
					{
						RuleId = (int) RuleType.Promotion,
						RuleName = RuleType.Promotion.ToString(),
						RuleDisplayName = "Promotions",
						RuleDesc = "List items that should be promoted when relevent to the page",
						IsVisible = true,
						IsEnabled = enabled,
						StatusDesc = statusDesc,
						IsOn = enabled && PromotionsOn
					});

				//Featured
				rules.Add(new DashSiteRule
					{
						RuleId = (int)RuleType.Featured,
						RuleName = RuleType.Featured.ToString(),
						RuleDisplayName = "Featured Items",
						RuleDesc = "List items that should always be featured first before any top-seller recs.",
						IsVisible = true,
						IsEnabled = enabled,
						StatusDesc = statusDesc,
						IsOn = enabled && FeaturedOn
					});

				//FeaturedCrossSell
				rules.Add(new DashSiteRule
					{
						RuleId = (int) RuleType.FeaturedCrossSell,
						RuleName = RuleType.FeaturedCrossSell.ToString(),
						RuleDisplayName = "Featured Cross-Sell",
						RuleDesc = "List items that should always (or never) be shown with a speciic item as the first cross-sell recs.",
						IsVisible = true,
						IsEnabled = enabled,
						StatusDesc = statusDesc,
						IsOn = enabled && FeaturedCrossSellOn
					});

				//FeaturedUpSell
				rules.Add(new DashSiteRule
					{
						RuleId = (int) RuleType.FeaturedUpSell,
						RuleName = RuleType.FeaturedUpSell.ToString(),
						RuleDisplayName = "Featured Up-Sell",
						RuleDesc = "List items that should always (or never) be shown with a speciic item as the first up-sell recs.",
						IsVisible = true,
						IsEnabled = enabled,
						StatusDesc = statusDesc,
						IsOn = enabled && FeaturedUpSellOn
					});


				//Replacements
				enabled = !PluginHandlesReplacements && (Tier > BoostTier.Basic); //available for all carts
				statusDesc = PluginHandlesReplacements
					             ? reasonHandledByPlugin
					             : (!(Tier > BoostTier.Basic)
						                ? reasonTierTooLow
						                : "");
				rules.Add(new DashSiteRule
					{
						RuleId = (int) RuleType.Replacements,
						RuleName = RuleType.Replacements.ToString(),
						RuleDisplayName = "Replacements",
						RuleDesc = "List items that should replace another item",
						IsVisible = true, //!PluginHandlesReplacements,
						IsEnabled = enabled,
						StatusDesc = statusDesc,
						IsOn = enabled && ReplacementsOn
					});
			}

			else if (dashpage.Equals(DashControlPage.Uploads))
			{
				//most of the accordions depend on the site's DataUpdateStatus
				if (DashSiteList.Instance == null)
					throw new WebFaultException<string>("Service not ready.", HttpStatusCode.ServiceUnavailable);
				var siteStatus = DashSiteList.Instance.GetDataUpdateStatus(Alias);

				//Extractor Schedule
				enabled = CartExtractorExists; //available for all tiers 
				statusDesc = !CartExtractorExists
					             ? reasonNoExtractor
					             : "";
				rules.Add(new DashSiteRule
					{
						RuleId = (int) RuleType.ExtractorSchedule,
						RuleName = RuleType.ExtractorSchedule.ToString(),
						RuleDisplayName = "Update Schedules",
						RuleDesc = "Schedule our service to automatically update data from your site",
						IsVisible = true,
						IsEnabled = enabled,
						StatusDesc = statusDesc,
						IsOn = enabled
					});

				//Run Extractor
				enabled = enabled && AllowUserExtraction; //available for all tiers 
				statusDesc = !AllowUserExtraction 
													? "Data updates on your site are currently blocked. Pease request assistance." 
													: siteStatus.StatusDesc;
				rules.Add(new DashSiteRule
					{
						RuleId = (int) RuleType.RunExtractor,
						RuleName = RuleType.RunExtractor.ToString(),
						RuleDisplayName = "Update Now",
						RuleDesc = "Queue your data to be updated immediately",
						IsVisible = true,
						IsEnabled = enabled,
						StatusDesc = statusDesc,
						IsOn = siteStatus.StatusType.Equals((int) DataUpdateStatus.Ready)
					});

				//Manual Upload
				enabled = AllowManualUpload; //available for all tiers 
				statusDesc = !AllowManualUpload
					             ? "Data upload is not permitted on your site"
											 : siteStatus.StatusDesc;
				rules.Add(new DashSiteRule
					{
						RuleId = (int) RuleType.ManualUpload,
						RuleName = RuleType.ManualUpload.ToString(),
						RuleDisplayName = "Manual Upload",
						RuleDesc = "Manually upload data files for your site",
						IsVisible = true,
						IsEnabled = enabled,
						StatusDesc = statusDesc,
						IsOn = siteStatus.StatusType.Equals((int)DataUpdateStatus.Ready)
					});
			}
			return rules.ToArray();
		}

		public List<DashRuleItem> GetExclusionRules()
		{
			var exclusionDashRules = new List<DashRuleItem>();

			//first add category exclusions
			if (CategoryRules != null && CategoryRules.ExclusionsExist)
			{
				exclusionDashRules.Add(new DashRuleItem
				                       	{
				                       		ItemGroup = "Excluded Categories",
				                       		Comparison = Condition.GetComparisonDef(Condition.Comparator.IsOneOf),
				                       		Value = CategoryRules.Exclusions.Aggregate((c, j) => string.Format("{0},{1}", c, j)),
																	Field = "Category_4T"
				                       	});
			}

			//then add standard exclusion rules
			if (ExclusionRules != null && ExclusionRules.Count > 0)
			{
				exclusionDashRules.AddRange(from rule in ExclusionRules
				                            where !string.IsNullOrEmpty(rule.QueryField)
				                            select new DashRuleItem
				                                   	{
				                                   		ItemGroup = rule.Name,
				                                   		Comparison = Condition.GetComparisonDef(rule.Comparison),
				                                   		Value = rule.Value,
				                                   		Field = rule.QueryField
				                                   	});
			}
			return exclusionDashRules;
		}

		public List<DashRuleItem> GetFilterRules()
		{
			var filterDashRules = new List<DashRuleItem>();

			//first add category filters
			if (CategoryRules != null && CategoryRules.FiltersExist)
			{
				//for cat filters, we need a separate rule for each group id
				var groupIds = CategoryRules.Filters.Select(x => x.GroupId).Distinct();
				var filterCatGroups = groupIds.ToDictionary(g => g, 
																										g => CategoryRules.Filters.Where(x => x.GroupId.Equals(g)).Select(x => x.CatId).ToList());
				filterDashRules.AddRange(filterCatGroups.Select(x => new DashRuleItem
				                                                     	{
				                                                     		ItemGroup = x.Key,
				                                                     		Comparison = Condition.GetComparisonDef(Condition.Comparator.IsOneOf),
				                                                     		Value = x.Value.Aggregate((c, j) => string.Format("{0},{1}", c, j)),
																																Field = "Category_4T"
				                                                     	}));
			}

			//then add standard filter rules
			if (FilterRules != null && FilterRules.Count > 0)
			{	
				filterDashRules.AddRange(from rule in FilterRules
																 where !string.IsNullOrEmpty(rule.QueryField)
																 select new DashRuleItem
			                                	{
																					ItemGroup = rule.Name,
																					Comparison = Condition.GetComparisonDef(rule.Comparison), 
																					Value = rule.Value, 
			                                		Field = rule.QueryField
			                                	});
			}
			return filterDashRules;
		}
	
		public List<DashAttributeDef> GetCatOptimizations()
		{
			var optimizations = new List<DashAttributeDef>();
			if (CategoryRules == null || !CategoryRules.OptimizationsExist) return optimizations;

			var attIds = CategoryRules.Optimizations;
			var att1Defs = ClientData.Instance.GetAttribute1Defs(Alias);
			optimizations.AddRange(attIds.Select(attId => att1Defs.FirstOrDefault(x => x.Id.Equals(attId))).Where(att1 => att1 != null));
			return optimizations;
		}

		public List<DashAttributeDef> GetCrossSellCats()
		{
			var crossSellCats = new List<DashAttributeDef>();
			if (CategoryRules == null || !CategoryRules.CrossCategoryExist) return crossSellCats;

			var attIds = CategoryRules.CrossSellCats;
			var att1Defs = ClientData.Instance.GetAttribute1Defs(Alias);
			crossSellCats.AddRange(attIds.Select(attId => att1Defs.FirstOrDefault(x => x.Id.Equals(attId))).Where(att1 => att1 != null));
			return crossSellCats;
		}

		public DashExtractorSchedule[] GetExtractorSchedules()
		{
			if (ExtractorSchedules == null) return null;

			var id = 0;
			var schedules = ExtractorSchedules.Select(e => new DashExtractorSchedule
				{
					Id = id++, 
					Enabled = e.Enabled,
					Rate = (int) e.Rate, 
					HourOfDay = e.HourOfDay, 
					DayOfWeek = (int) e.DayOfWeek, 
					WeekOfMonth = e.WeekOfMonth, 
					DayOfMonth = e.DayOfMonth
				}).ToList();
			return schedules.ToArray();
		}

		public int AddExtractorSchedule(bool enabled, int rate, int hourOfDay, int dayOfWeek = 0,
		                                int weekOfMonth = 0, int dayOfMonth = 0)
		{
			var s = new ExtractorSchedule
				{
					Enabled = enabled,
					Rate = (ExtractorSchedule.ExtractRate) rate,
					HourOfDay = hourOfDay,
					DayOfWeek = (DayOfWeek) dayOfWeek,
					WeekOfMonth = weekOfMonth,
					DayOfMonth = dayOfMonth
				};

			if (ExtractorSchedules == null)
				ExtractorSchedules = new List<ExtractorSchedule>();
			else
			{
				var index = ExtractorSchedules.IndexOf(s);
				if (index >= 0) return index;
			}

			ExtractorSchedules.Add(s);
			QueueSettings();
			return ExtractorSchedules.Count - 1;
		}

		public bool EnableExtractorSchedule(int id)
		{
			if (ExtractorSchedules == null)
				throw new Exception(string.Format("Cannot enable extractor {0}. No Extractor Schedules exist", id));

			if (id < 0 || id >= ExtractorSchedules.Count)
				throw new Exception(string.Format("Cannot enable extractor {0}. Index is out of range", id));

			ExtractorSchedules[id].Enabled = true;
			QueueSettings();
			return true;
		}

		public bool DisableExtractorSchedule(int id)
		{
			if (ExtractorSchedules == null)
				throw new Exception(string.Format("Cannot disable extractor {0}. No Extractor Schedules exist", id));

			if (id < 0 || id >= ExtractorSchedules.Count)
				throw new Exception(string.Format("Cannot disable extractor {0}. Index is out of range", id));

			ExtractorSchedules[id].Enabled = false;
			QueueSettings();
			return true;
		}

		public bool RemoveExtractorSchedule(int id)
		{
			if (ExtractorSchedules == null)
				throw new Exception(string.Format("Cannot remove extractor {0}. No Extractor Schedules exist", id));

			if (id < 0 || id >= ExtractorSchedules.Count)
				throw new Exception(string.Format("Cannot remove extractor {0}. Index is out of range", id));

			ExtractorSchedules.RemoveAt(id);
			QueueSettings();
			return true;
		}

		public string SetDashRule(int ruleId, bool hasRule)
		{
			try
			{
				switch ((RuleType)ruleId)
				{
					case RuleType.Exclusion:
						ExclusionsOn = hasRule;
						break;
					case RuleType.Filter:
						FiltersOn = hasRule;
						break;
					case RuleType.CategoryOptimization:
						CategoryOptimizationsOn = hasRule;
						break;
					case RuleType.Upsell:
						UpsellOn = hasRule;
						break;
					case RuleType.CrossCategory:
						CrossCategoryOn = hasRule;
						break;
					case RuleType.AttributeRules:
						AttributeRulesOn = hasRule;
						break;
					case RuleType.Promotion:
						PromotionsOn = hasRule;
						break;
					case RuleType.FeaturedCrossSell:
						FeaturedCrossSellOn = hasRule;
						break;
					case RuleType.FeaturedUpSell:
						FeaturedUpSellOn = hasRule;
						break;
					case RuleType.Featured:
						FeaturedOn = hasRule;
						break;
					case RuleType.FeaturedAtt1:
						FeaturedAtt1On = hasRule;
						break;
					case RuleType.Replacements:
						ReplacementsOn = hasRule;
						break;
					case RuleType.Resell:
						ResellOn = hasRule;
						break;
					default:
						throw new Exception(); 
				}
                QueueSettings();
                return string.Format("{0} = {1}", (RuleType)ruleId, hasRule ? "on" : "off");
			}
			catch (Exception)
			{
				throw new Exception(string.Format("RuleId {0} not found", ruleId));
			}
		}

		public bool AddExclusion(string name, string comparison, string value, string field)
		{
			//special case for category rules
			if (field.Equals("Category_4T"))
			{
				if (string.IsNullOrEmpty(value)) return false;
				if (CategoryRules == null)
					CategoryRules = new CategoryConditions();
				else
				{
					//new list replaces all old ones
					if (CategoryRules.ExclusionsExist)
						CategoryRules.RemoveAll(CategoryConditions.CatConditionType.Exclude);
				}
				foreach (var id in value.Split(new[] { ',' }))
					CategoryRules.AddCat(CategoryConditions.CatConditionType.Exclude, id);
			}
			else
			{
				if (ExclusionRules == null)
					ExclusionRules = new List<Condition>();
				else
				{
					//ignore name for exclusion equality
					if (ExclusionRules.Any(x => x.Equals("", comparison, value, field)))
						throw new Exception(string.Format("Unable to add Exclusion {0} for {1}: equivalent rule already exists",
						                                  string.IsNullOrEmpty(name) ? field : name, Alias));
				}
				var rule = new Condition(name, comparison, value, field);
				ExclusionRules.Add(rule);
			}
			QueueSettings();
			return true;
		}

		public bool RemoveExclusion(string comparison, string value, string field)
		{
			//special case for category rules
			if (field.Equals("Category_4T"))
			{
				if (CategoryRules == null) return false;
				if (!CategoryRules.ExclusionsExist) return false;
				CategoryRules.RemoveAll(CategoryConditions.CatConditionType.Exclude);
			}
			else
			{
				//for normal exclusion rules, find matches but ignore the name
				if (ExclusionRules == null) return false;
				var match = ExclusionRules.First(x => x.Equals("", comparison, value, field));
				if (match == null) return false;
				if (!ExclusionRules.Remove(match)) return false;
			}
			QueueSettings();
			return true;
		}

		public bool AddFilter(string groupId, string comparison, string value, string field)
		{
			//special case for category rules
			if (field.Equals("Category_4T"))
			{
				if (string.IsNullOrEmpty(value)) return false;
				if (CategoryRules == null) 
					CategoryRules = new CategoryConditions();
				else
				{
					//new list replaces all old ones
					if (CategoryRules.FiltersExist)
						CategoryRules.Filters.RemoveAll(x => x.GroupId.Equals(groupId));
				}
				foreach (var id in value.Split(new[] { ',' }))
					CategoryRules.AddCat(CategoryConditions.CatConditionType.Filter, id, groupId);
			}
			else
			{
				//for normal filters, make sure they are unique
				if (FilterRules == null)
					FilterRules = new List<Condition>();
				else
				{
					if (FilterRules.Any(x => x.Equals(groupId, comparison, value, field)))
						throw new Exception(string.Format("Unable to add Filter {0} for {1}: equivalent rule already exists",
						                                  groupId, Alias));
				}
				var rule = new Condition(groupId, comparison, value, field);
				FilterRules.Add(rule);
			}
			QueueSettings();
			return true;
		}

		public bool RemoveFilter(string groupId, string comparison, string value, string field)
		{
			//special case for category rules
			if (field.Equals("Category_4T"))
			{
				if (CategoryRules == null) return false;
				if (!CategoryRules.FiltersExist) return false;
				var count = CategoryRules.Filters.RemoveAll(x => x.GroupId.Equals(groupId));
				if (count < 1) return false;
			}
			else
			{
				if (FilterRules == null) return false;
				var match = FilterRules.First(x => x.Equals(groupId, comparison, value, field));
				if (match == null) return false;
				if (!FilterRules.Remove(match)) return false;
			}
			QueueSettings();
			return true;
		}

		public bool AddCatOptimization(string value)
		{
			if (CategoryRules == null) CategoryRules = new CategoryConditions();

			if (!CategoryRules.AddCat(CategoryConditions.CatConditionType.Ignore, value)) return false;
			QueueSettings();
			return true;
		}

		public bool RemoveCatOptimization(string value)
		{
			if (CategoryRules == null) return false;
			if (!CategoryRules.RemoveCat(CategoryConditions.CatConditionType.Ignore, value)) return false;
			QueueSettings();
			return true;
		}

		public bool AddCrossAtt1(string value)
		{
			if (CategoryRules == null) CategoryRules = new CategoryConditions();

			if (!CategoryRules.AddCat(CategoryConditions.CatConditionType.CrossSell, value)) return false;
			QueueSettings();
			return true;
		}

		public bool RemoveCrossAtt1(string value)
		{
			if (CategoryRules == null) return false;
			if (!CategoryRules.RemoveCat(CategoryConditions.CatConditionType.CrossSell, value)) return false;
			QueueSettings();
			return true;
		}
#endif

		#endregion

		#region Utilities

		public static Dictionary<string, string> GetCharMapPairs(XElement charMapXml, string alias = "")
		{
			if (charMapXml == null) return null;

			var pairs = charMapXml.Descendants("mapPair");
			if (!pairs.Any()) return null;

			var charMapPairs = new Dictionary<string, string>();
			foreach (var pair in pairs)
			{
				char charTest;
				var input = Input.GetAttribute(pair, "from");
				if (string.IsNullOrEmpty(input)) continue; //must have a from character or string
				var from = Input.TryConvert(input, out charTest) ? 
					charTest.ToString() : input.Replace("0x22", "\""); //quotes require special handling

				var to = Input.GetAttribute(pair, "to");
				if (string.IsNullOrEmpty(to)) to = ""; // ok to have empty output to remove characters
				else to = Input.TryConvert(to, out charTest) ? 
					charTest.ToString() : to.Replace("0x22", "\""); //quotes require special handling

				string test;
				if (charMapPairs.TryGetValue(@from, out test))
				{
					if (BoostLog.Instance != null)
						BoostLog.Instance.WriteEntry(EventLogEntryType.Error,
						                             string.Format(
							                             "Cannot add CharMap from {0} to {1}. Map already exists to {2}", @from, to,
							                             test), "", alias);
					continue;
				}
				charMapPairs.Add(@from, to);
			}
			return charMapPairs.Any() ? charMapPairs : null;
		}

		//private string GetRuleNameFromField(FieldName name)
		//{
		//  //NOTE: This requires all fieldname rules to conform to this naming convention (camel-case FieldName + "Field")
		//  var ruleName = name + "Field";
		//  var firstLetter = char.ToLower(ruleName[0]);
		//  ruleName = firstLetter + ruleName.Substring(1);
		//  return ruleName;
		//}

		//private DataGroup GetGroupFromField	(FieldName name)
		//{
		//  //NOTE: This depends on the FieldName enum to maintain the existing group order as new enums are added
		//  if ((int)name < (int)FieldName.OrderGroupId) 
		//    return DataGroup.Catalog;
		//  if ((int)name < (int)FieldName.Att1NameGroupId) 
		//    return DataGroup.Sales;
		//  if ((int)name < (int)FieldName.Att2NameGroupId) 
		//    return DataGroup.CategoryNames;
		//  if ((int)name < (int)FieldName.CustomerGroupId) 
		//    return DataGroup.ManufacturerNames;
		//  return DataGroup.Customers;
		//}

		/// <summary>
		/// Set default vaule for the named field only if it is empty
		/// </summary>
		/// <param name="name"></param>
		/// <param name="value"></param>
		//public void SetDefaultField(FieldName name, string value)
		//{
		//  if (string.IsNullOrEmpty(value)) return; //only add or update with valid names

		//  DataField currentEntry;
		//  var exists = _fieldNames.TryGetValue(name, out currentEntry);
		//  if (exists)
		//  {
		//    currentEntry.DefaultName = value;
		//  }
		//  else
		//  {
		//    var group = GetGroupFromField(name);
		//    //NOTE: This depends on all xml group names to have "Group" in the name and for all other parameters to be queryable
		//    //      Safer alternative is to make a huge switch statement here with all possible field names and map them to the proper boolean
		//    //			And throw an exception for any unknown fieldnames to ensure future compliance when fieldNames are added
		//    var isQueryable = !name.ToString().Contains("Group");
		//    _fieldNames.Add(name, new DataField(value, @group, setDefault: true, isQueryable: isQueryable));
		//  }
		//}

		/// <summary>
		/// Try to get the current field name for an optional field
		/// </summary>
		/// <param name="name"></param>
		/// <returns>Returns an empty string if not found</returns>
		//private string GetFieldName(FieldName name)
		//{
		//  DataField field;
		//  return (_fieldNames.TryGetValue(name, out field)) ? field.Name : "";
		//}

		/// <summary>
		/// Try to get the current field name for a required field
		/// </summary>
		/// <param name="name"></param>
		/// <returns>Throws an exception if not found</returns>
		//private string GetFieldRequired(FieldName name)
		//{
		//  DataField field;
		//  if (!_fieldNames.TryGetValue(name, out field))
		//    throw new NullReferenceException(name.ToString() + " is a required field");
		//  return field.Name;
		//}

		//public bool GetField(FieldName name, out string value)
		//{
		//  value = "";
		//  DataField field;
		//  if (_fieldNames.TryGetValue(name, out field))
		//  {
		//    value = field.Name;
		//    return true;
		//  }
		//  return false;
		//}

		//public List<string> GetActiveFields(DataGroup group)
		//{
		//  //return (from field in _fieldNames where !string.IsNullOrEmpty(field.Value) select field.Value).ToList();
		//  var fields = _fieldNames.Where(x => (x.Value.Group.Equals(group) && x.Value.IsQueryable)).Select(x => x.Value.Name);
		//  if (AddStandardFields != null) fields = fields.Union(AddStandardFields);
		//  return fields.ToList();
		//}

		//public List<string> GetStandardFields(DataGroup group)
		//{
		//  var fields = _fieldNames.Where(x => (x.Value.Group.Equals(group) && x.Value.IsQueryable)).Select(x => x.Value.DefaultName);
		//  if (AddStandardFields != null) fields = fields.Union(AddStandardFields);
		//  return fields.ToList();
		//}

		//public List<string> GetNonStandardFields(DataGroup group)
		//{
		//  var fields = 
		//    _fieldNames.Where(x => (x.Value.Group.Equals(group) && x.Value.IsQueryable && !x.Value.Name.Equals(x.Value.DefaultName)))
		//                .Select(x => x.Value.Name);
		//  if (AddStandardFields != null) fields = fields.Except(AddStandardFields);
		//  return fields.ToList();
		//}

		//Get a list of all fields needed by any of the catalog extraction rules
		public List<string> GetRuleFields()
		{
			var fields = new List<string>();
			if (FiltersOn)
			{
				if ((FilterRules != null) && (FilterRules.Count > 0))
					fields.AddRange(FilterRules.Select(c => c.QueryField));
				if ((FilterParsingRules != null) && (FilterParsingRules.Count > 0))
					fields.AddRange(from g in FilterParsingRules from r in g.ParseRules select r.FromField);
			}

			if (ExclusionsOn && (ExclusionRules != null) && (ExclusionRules.Count > 0))
				fields.AddRange(ExclusionRules.Select(c => c.QueryField));
			if (ExclusionSet != null)
			{
				var exSetFields = ExclusionSet.GetFields();
				if (exSetFields.Any()) fields.AddRange(exSetFields);
			}

			if (FeaturedCrossSellOn && (FeaturedCrossSellRules != null) && (FeaturedCrossSellRules.Count > 0))
				fields.AddRange(FeaturedCrossSellRules.Select(c => c.QueryField));

			if (FeaturedUpSellOn && (FeaturedUpSellRules != null) && (FeaturedUpSellRules.Count > 0))
				fields.AddRange(FeaturedUpSellRules.Select(c => c.QueryField));

			if (FeaturedOn && (FeaturedRules != null) && (FeaturedRules.Count > 0))
				fields.AddRange(FeaturedRules.Select(c => c.QueryField));

			//Add alternate prices/pages/images to the list for querying
			var altFields = Fields.GetAltRuleFields();
			if (altFields != null && altFields.Count > 0)
				fields.AddRange(altFields);
			//if (AlternatePriceFields != null && AlternatePriceFields.Count > 0)
			//  fields = fields.Union(AlternatePriceFields).ToList();
			//if (AlternatePageFields != null && AlternatePageFields.Count > 0)
			//  fields = fields.Union(AlternatePageFields).ToList();
			//if (AlternateImageFields != null && AlternateImageFields.Count > 0)
			//  fields = fields.Union(AlternateImageFields).ToList();

			//add migration fields if any
			if (MigrationRules != null && MigrationRules.Enabled)
			{
				var field = MigrationRules.IsMigrationMaster ? MigrationRules.MapToField : MigrationRules.MapFromField;
				if (!string.IsNullOrEmpty(field))
					fields.Add(field);
			}

			return fields.Distinct().ToList();
		}

		public string StripTablePrefix(string fieldName)
		{
			if (string.IsNullOrEmpty(fieldName)) return "";
			var index = fieldName.IndexOf('.') + 1;
			return (index > 0) ? fieldName.Substring(index) : fieldName;
		}

		public List<List<string>> ParseRowsOfColumns(string data)
		{
			var rows = GetRows(data);
			return rows.Count < 1 ? new List<List<string>>() : rows.Select(x => SplitRow(x)).ToList();
		}

		public List<string> GetRows(string data)
		{
			var rows = new List<string>();
			if (string.IsNullOrEmpty(data)) return rows;

			rows.AddRange(data.Trim().Trim('\r', '\n')
											.Split(DataFormatRowEnds, StringSplitOptions.RemoveEmptyEntries)
											.Select(x => x.Trim(DataFormatRowTrims)).ToList());
			rows.RemoveAll(string.IsNullOrWhiteSpace);
			return rows;
		}

		public List<string> SplitRow(string row, string columnEnd = null, string columnStart = "[")
		{
			if (string.IsNullOrEmpty(row)) return new List<string>();
			if (columnEnd == null || columnEnd.Length < 1)
				columnEnd = ","; //default to comma-separated (works with Json or CSV)

			//Logic Considerations:
			//can't just split on columnEnd as these characters could exist inside a field
			//can't assume field ends with a quote because numerical fields may not have quotes
			//and some fields could have internal \"'s
			//so if it starts with a quote, then it must end with a quote-columnEnd
			//if no quote at the start then end at next columnEnd

			bool startsWithQuote = false;
			var trimStart = new[] { ' ', '[', ']', '\t', '\r', '\n' }; //commas and quotes trimmed separately
			var trimChars = new[] { ' ', '[', ']', ',', '\t' }; //quotes trimmed separately
			var trimQuotes = new[] { '\"' };
			string separator1 = "\"" + columnEnd; //if startswithquote
			string separator2 = columnEnd;

			var cols = new List<string>();
			//trim off any extra characters before the first '[' (or other columnStart characters)
			var start = 0;
			if (columnStart != null && columnStart.Length > 0)
			{
				start = row.IndexOf(columnStart);
				if (start > 0) row = row.Substring(start);
			}
			while (true)
			{
				row = row.TrimStart(trimStart); //don't trim quotes or commas yet
				startsWithQuote = row.StartsWith("\"");
				var separator = startsWithQuote ? separator1 : separator2;
				start = startsWithQuote ? 1 : 0; //look past the first quote
				var end = row.IndexOf(separator, start, StringComparison.Ordinal);
				var item = end < 0 ? row : row.Substring(0, end);
				if (startsWithQuote) item = item.Trim(trimQuotes); //only trim qoutes here
				item = item.Trim(trimChars);
				cols.Add(item);
				if (end < 0) break;
				row = startsWithQuote ? row.Substring(end + 2) : row.Substring(end + 1);
			}

			return cols;
		}

		public XElement ReadCartRules(CartType cartType)
		{
			//TODO: define rules to read files uploaded from osCommerce
			if (cartType.Equals(CartType.osCommerce) || cartType.Equals(CartType.Other)) 
				return null;

			XElement cartRules;
			lock (_cartRules)
			{
				if (!_cartRules.TryGetValue(cartType, out cartRules))
				{
					var path = "";
					try
					{
						path = DataPath.Instance.Root + cartType.ToString() + "Rules.xml";
						cartRules = XElement.Load(path);
						if (!cartRules.IsEmpty)
							_cartRules.Add(cartType, cartRules);
					}
					catch (Exception ex)
					{
						if (BoostLog.Instance != null)
							BoostLog.Instance.WriteEntry(EventLogEntryType.Error, "Error reading CartRules from " + path, ex, "");
					}
				}
			}
			return cartRules;
		}


		#endregion
	}

//class
}

//namespace