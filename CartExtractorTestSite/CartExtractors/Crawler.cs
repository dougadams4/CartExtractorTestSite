using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using HtmlAgilityPack;
using _4_Tell.CommonTools;
using _4_Tell.Logs;

namespace _4_Tell.CartExtractors
{
	/// <summary>
	/// Class containg various methods related to site-crawling and page-scraping
	/// </summary>
	public class Crawler
	{
		public Uri Url { get; set; }

		/// <summary>
		/// Enum to describe the category pagetype for a site
		/// </summary>
		internal enum CategoryDisplayType
		{
			Tree, // the site has a Category tree structure with sub-pages (Ex: http://www.manukanatural.com/.com)
			Flat // the site has Categories flatened, so we scrape a sorted collection of all prodocuts in all categories 
			//(Ex: http://www.shopakira.com/categories?sort=priceasc)
		};

		/// <summary>
		/// Fetch images from the site Catagories page based on the DisplayType 
		/// </summary>
		/// <param name="catUrls"></param>
		/// <param name="imageCatalog"></param>
		/// <param name="itemCount"></param>
		/// <param name="progress"></param>
		internal static void LoadProductImagesForCategory(SiteRules rules, List<string> catUrls,
		                                                  ref Dictionary<string, string> imageCatalog,
		                                                  int itemCount, ExtractorProgress progress
			)
		{
			var nodeSelector = rules.ProductNodeSelector; //Xpath to find product nodes
			if (string.IsNullOrEmpty(nodeSelector))
				nodeSelector = "//div[@class='ProductImage QuickView']//img[contains(@src,'products')]";
					
			//determine whether the categories have a tree or flat structure
			var displayType = rules.ForceCategoryTreeScrape ? CategoryDisplayType.Tree : CategoryDisplayType.Flat;
			var maxPages = 1;

			var uri = new Uri(String.Format("http://{0}/categories?sort=priceasc&page=1", rules.StoreShortUrl));
			var thisWeb = new HtmlWeb();
			var thisDoc = thisWeb.Load(uri.AbsoluteUri);
			var searchSpring = false;
			//while (!rules.ForceCategoryTreeScrape) //NOTE: could skip while loop in this case, but left out for testing
			while (true) //single-pass loop to set display type
			{
				//check for use of searchsprring --Ajax response is not seen by HtmlAgilityPack
				var matching = thisDoc.DocumentNode.SelectNodes("//div[@class='searchspring-results_container']");
				//var matching = thisDoc.DocumentNode.SelectNodes("//div[@id='searchspring-main']");
				if (matching != null)
				{
					searchSpring = true;
					matching = thisDoc.DocumentNode.SelectNodes("//div[@id='searchspring-main']");
					if (matching == null)
						break;
					if (!matching[0].Attributes["class"].Value.Contains("searchspring-no_results"))
						//var noResults = thisDoc.DocumentNode.SelectNodes("//div[@class='searchspring-no_results']");
						//if (noResults == null)
						break;

					displayType = CategoryDisplayType.Tree;
					break;
				}


				matching = thisDoc.DocumentNode.SelectNodes(nodeSelector);
				if (matching != null) break;
				var testSelector = "//div[starts-with(@class,'ProductImage')]";
					//"//div[@class='ProductImage']//img[@src]";
				matching = thisDoc.DocumentNode.SelectNodes(testSelector);
				if (matching != null)
				{
					nodeSelector = testSelector;
					break;
				}

				displayType = CategoryDisplayType.Tree;
				break;
			}


			var pageStatusFormat = progress.ExtraStatus + " --page {0} of {1} (max)";
			if (displayType == CategoryDisplayType.Flat)
			{
				// we have a catalog setup with a cover page, so we need to resolve the Catalog first.

				//get page count
				if (searchSpring)
				{
					var totalPages = thisDoc.DocumentNode.SelectNodes("//div[@class='searchspring-total_pages']");
					if (totalPages != null)
						maxPages = Input.SafeIntConvert(totalPages[0].InnerText);
				}
				else
				{
					//estimate max pages 
					//var nodes = thisDoc.DocumentNode.SelectNodes("//div[@id='CategoryContent']//ul/li");
					var nodes = thisDoc.DocumentNode.SelectNodes(nodeSelector);
					var imagesPerPage = nodes == null ? 1 : nodes.Count;
					maxPages = (int) Math.Ceiling((decimal) itemCount/(decimal) imagesPerPage);
				}

				//do // loop through pages incrementally until we dont have anythign to match
				for (var page = 1; page <= maxPages; page++)
				{
					try
					{
						//we already got the first page above, so process it and then grab the next
						var pageStatus = string.Format(pageStatusFormat, page, maxPages);
						progress.UpdateTask(imageCatalog.Count, -1, null, pageStatus);

						var imageNodes = thisDoc.DocumentNode.SelectNodes(nodeSelector);
						if (imageNodes == null)
							break; // not all products usually have images, so this could end before we run out of pages

						var imageUrl = "";
						var pid = "";
						foreach (var node in imageNodes)
						{
							imageUrl = GetUrlFromMatch(node, out pid, ref rules);
							if (string.IsNullOrEmpty(pid)) continue;

							if (!imageCatalog.ContainsKey(pid))
							{
								imageCatalog.Add(pid, imageUrl);
								progress.UpdateTask(imageCatalog.Count);
							}
						}

						// we have a catalog setup with a cover page, so we need to resolve the Catalog first.
						uri = new Uri(String.Format("http://{0}/categories?sort=priceasc&page={1}", rules.StoreShortUrl, page + 1));
						thisDoc = thisWeb.Load(uri.AbsoluteUri);
					}
					catch (Exception ex)
					{
						Debug.WriteLine(ex.Message);
					}
				}
			}
			else //Tree structure
			{
				try
				{
					var catCount = 0;
					var totalCats = catUrls.Count;
					foreach (var catUrl in catUrls)
					{
						var details = string.Format("Pulling images for {0} ({1} of {2} categories)", catUrl.Replace("/", ""), ++catCount,
						                            totalCats);
						progress.UpdateTask(imageCatalog.Count, -1, null, details);

						//get the first page of results
						var thisCatUrl = String.Format("http://{0}{1}?sort=priceasc", rules.StoreShortUrl, catUrl);
						var catUri = new Uri(thisCatUrl);
						try
						{
							thisDoc = thisWeb.Load(catUri.AbsoluteUri);
						}
						catch (Exception ex)
						{
							if (BoostLog.Instance != null)
								BoostLog.Instance.WriteEntry(EventLogEntryType.Information, "Crawler: Unable to Load " + catUri.AbsoluteUri, ex);
							continue;
						}
						//get page count
						maxPages = 0;
						if (searchSpring)
						{
							var totalPages = thisDoc.DocumentNode.SelectNodes("//div[@class='searchspring-total_pages']");
							if (totalPages != null)
								maxPages = Input.SafeIntConvert(totalPages[0].InnerText);

						}
						if (maxPages < 1)
						{
							//TODO: See if there is a better way to calculate pages.
							maxPages = itemCount/10;
						}
						var page = 1;
						do
						{
							var matching = thisDoc.DocumentNode.SelectNodes(nodeSelector);
							//var matching = thisDoc.DocumentNode.SelectNodes("//div[starts-with(@class,'ProductImage')]//img[@src]");
							//if (matching == null)
							//  matching = thisDoc.DocumentNode.SelectNodes("//div[@class='ProductImage[QuickView]?')]//img[@src]");
							//if (matching == null)
							//  matching = thisDoc.DocumentNode.SelectNodes("//div[@class='ProductImage']//img[@src]");
							//if (matching == null) 
							//  matching = thisDoc.DocumentNode.SelectNodes("//div[@class='ProductImage QuickView']//img[@src]");
							if (!string.IsNullOrEmpty(rules.CommentParseKey)) //sometimes the nodes we want are hidden in a comment
							{
								var commentNodes = thisDoc.DocumentNode.SelectNodes(string.Format("//comment()[contains(., {0})]", rules.CommentParseKey));
								if (commentNodes != null)
								{
									foreach (var c in commentNodes)
									{
										try
										{
											var comment = new HtmlDocument();
											comment.LoadHtml(c.InnerHtml.Replace("<!--", "").Replace("-->", ""));
											var partialMatch = comment.DocumentNode.SelectNodes(nodeSelector);
											if (partialMatch != null)
												if (matching == null) matching = partialMatch;
												else
													foreach (var match in partialMatch)
														matching.Add(match);
										}
										catch (Exception ex)
										{
											if (BoostLog.Instance != null)
												BoostLog.Instance.WriteEntry(EventLogEntryType.Information, "Crawler: Unable to parse comment node", ex);
										}
									}
								}
							}
							if (matching == null) break;

							var matchCount = matching.Count;
							var oldCount = imageCatalog.Count;
							var imageUrl = "";
							var pid = "";
							foreach (var node in matching)
							{
								imageUrl = GetUrlFromMatch(node, out pid, ref rules);
								if (string.IsNullOrEmpty(pid)) continue;

								if (!imageCatalog.ContainsKey(pid))
								{
									imageCatalog.Add(pid, imageUrl);
									progress.UpdateTask(imageCatalog.Count);
								}
							}
							if (imageCatalog.Count == oldCount) break; //no new images found

							var pageStatus = string.Format(pageStatusFormat, page, maxPages);
							progress.UpdateTask(imageCatalog.Count, -1, null, details + pageStatus);
							if (++page > maxPages) break;

							thisCatUrl = String.Format("http://{0}{1}?sort=priceasc&page={2}", rules.StoreShortUrl, catUrl, page);
							catUri = new Uri(thisCatUrl);
							thisDoc = thisWeb.Load(catUri.AbsoluteUri);

						} while (true);
					}
				}
				catch (Exception ex)
				{
					Debug.WriteLine(ex.Message);
				}
			}
		}


		/// <summary>
		/// Return CategoryDisplayType reflecting the category display the site uses
		/// </summary>
		/// <returns></returns>
		internal static CategoryDisplayType SetCategoryDisplayType(string storeShortUrl)
		{
			var uri = new Uri(String.Format("http://{0}/categories?sort=priceasc&page=1", storeShortUrl));
			var thisWeb = new HtmlWeb();
			var thisDoc = thisWeb.Load(uri.AbsoluteUri);

			//check for use of searchsprring
			var matching = thisDoc.DocumentNode.SelectNodes("//div[@class='searchspring-results_container']");
			if (matching != null)
			{
				matching = thisDoc.DocumentNode.SelectNodes("//div[@class='searchspring-no_results']");
				if (matching != null) return CategoryDisplayType.Flat;

				matching = thisDoc.DocumentNode.SelectNodes("//div[@class='searchspring-total_pages']");
				if (matching != null) return CategoryDisplayType.Tree;
			}

			matching = thisDoc.DocumentNode.SelectNodes("//div[@class='ProductImage QuickView']//img[@src]");
			if (matching == null) return CategoryDisplayType.Tree;

			return CategoryDisplayType.Flat;
		}


		/// <summary>
		/// Extract the URL from a mached HtmlNode and add it to the catalog (if not already there)
		/// </summary>
		/// <param name="node"></param>
		/// <param name="imageCatalog"></param>
		/// <returns></returns>
		internal static string GetUrlFromMatch(HtmlNode node, out string pid, ref SiteRules rules)
		{
			pid = null;

			//stamdard link looks like this
			//http://cdn2.bigcommerce.com/server3300/46048/products/10676/images/91127/Hunter_Original_Tall_Boot_Aubergine_4__95507.1343675413.185.279.jpg
			var imageUrlSelector = rules.ImageUrlSelector;
			var imageUrlPrefix = rules.ImageUrlPrefix;
			if (string.IsNullOrEmpty(imageUrlPrefix))
				imageUrlPrefix = "src=\"";
			var imageUrlSuffix = rules.ImageUrlSuffix;
			if (string.IsNullOrEmpty(imageUrlSuffix))
				imageUrlSuffix = "\"";
			var pidSelector = rules.PidSelector;
			var pidPrefix = rules.PidPrefix;
			if (string.IsNullOrEmpty(pidPrefix))
				pidPrefix = "/products/";
			var pidSuffix = rules.PidSuffix;
			if (string.IsNullOrEmpty(pidSuffix))
				pidSuffix = "/";

			//find the image url
			HtmlNode imageNode;
			if (string.IsNullOrEmpty(imageUrlSelector))
				imageNode = node;
			else
			{
				var nodeList = node.SelectNodes(imageUrlSelector);
				imageNode = nodeList == null ? node : nodeList.FirstOrDefault();
			}
			if (imageNode == null) return "";
			var html = imageNode.OuterHtml;
			var src = html.IndexOf(imageUrlPrefix);
			if (src < 0) return "";
			src += imageUrlPrefix.Length;
			var start = html.IndexOf("//", src); //skip protocol
			if (start < 0) start = src;
			var end = html.IndexOf(imageUrlSuffix, start);
			var url = end < 0 ? html.Substring(start) : html.Substring(start, end - start);

			//find the product id
			HtmlNode pidNode;
			if (string.IsNullOrEmpty(pidSelector))
				pidNode = node;
			else
			{
				var nodeList = node.SelectNodes(pidSelector);
				pidNode = nodeList == null ? node : nodeList.FirstOrDefault();
			}
			if (pidNode == null) return url;
			html = pidNode.OuterHtml;
			start = html.IndexOf(pidPrefix);
			if (start < 0) return url;
			start += pidPrefix.Length;
			end = html.IndexOf(pidSuffix, start);
			pid = end < 0 ? html.Substring(start) : html.Substring(start, end - start);

			return url;
		}


		/// <summary>
		/// For testing - disposable
		/// </summary>
		/// <param name="imageCatalog"></param>
		internal static void DumpCatalog(Dictionary<string, string> imageCatalog)
		{
			using (var sw = new StreamWriter(@"C:\ProgramData\4-Tell2.0\ShopAkra\CatalogDump.txt"))
			{
				foreach (var line in imageCatalog)
				{
					sw.WriteLine(line.Key + "= " + line.Value);
				}
			}
		}
	}

	//EDN class
}