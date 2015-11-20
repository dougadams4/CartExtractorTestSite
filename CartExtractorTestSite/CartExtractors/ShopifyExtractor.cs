using System;
using System.Collections.Specialized;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Timers;
using System.Xml.Linq;
#if !CART_EXTRACTOR_TEST_SITE
using _4_Tell.DashService;
#endif
using _4_Tell.CommonTools;
using _4_Tell.IO;
using _4_Tell.Logs;
using System.Runtime.CompilerServices;
using System.Web;
using System.Web.Services.Description;
using Microsoft.CSharp.RuntimeBinder;
using _4TellJsonDataObjects;
using System.Configuration;
using System.Net;
using System.Net.Http;
using Newtonsoft.Json;
using System.Text;
using System.Web.Script.Serialization;
using System.Collections.ObjectModel;
using Newtonsoft.Json.Linq;
using System.Globalization;

namespace _4_Tell.CartExtractors
{
    public sealed class ShopifyExtractor : CartExtractor
    {
        public ShopifyExtractor(SiteRules rules)
            : base(rules)
        {
            //TODO: Implement ValidateCredentials for all carts and move this check to the base class
            var status = "";
            HasCredentials = ValidateCredentials(out status);
            if (!HasCredentials && BoostLog.Instance != null)
                BoostLog.Instance.WriteEntry(EventLogEntryType.Error, "Invalid Shopify API Credentials", status, Alias);
            else
                // determine feed types
                SetFeedTypes();
        }

        #region Overrides of CartExtractor

        public override bool ValidateCredentials(out string status)
        {
            try
            {
                if (Rules == null)
                    throw new Exception("CartExtractor Rules cannot be null");

                // Check for shop API Url           
                if (string.IsNullOrEmpty(Rules.ApiUrl))
                    throw new Exception("apiUrl is undefined");

                // Check for API key
                if (string.IsNullOrWhiteSpace(Rules.ApiKey))
                    throw new Exception("apiKey is undefined");

                // Check for API user name
                if (string.IsNullOrWhiteSpace(Rules.ApiUserName))
                    throw new Exception("apiUserName is undefined");

                if (Rules.ExtractorCredentials == null) //should only happen the first time this site extracts data
                {
                    Rules.ExtractorCredentials = new AuthCredentials();
                    Rules.ExtractorCredentials.Type = AuthCredentials.AuthType.BasicAuth;
                    Rules.ExtractorCredentials.UserName = Rules.ApiUserName;
                    Rules.ExtractorCredentials.Password = Rules.ApiKey;
                    Rules.ExtractorCredentials.RequireSsl = true;
                    Rules.ApiAcceptHeader = "application/json";
                }

                // Call the api here to see if credetials work
                if (true)
                {
                    status = "Credentials Validated";
                    return true;
                }
                else
                {
                    status = "Could Not Validate Credentials";
                    return false;
                }
            }
            catch (Exception ex)
            {
                status = ex.Message;
                return false;
            }
        }

        private static Timer _cleanupTimer = null;
        
        protected override void ReleaseCartData()
        {
            // Free local storage containers
            FreeLocalStorage(true);
        }
        
        // Only required if the order confirmation page does not include order details, which we use to create the auto actions file based on real time updates,
        // Some platforms don't show order details on the confirmation page, just provide an order index so our JS can't scrape the order info we need, in this case our
        // service comes here to find the order information based on order id.
        public override void LogSalesOrder(string orderID)
        {
            throw new NotImplementedException();
        }

        #endregion

        protected override string GetAtt2Names(out int rowCount)
        {
            rowCount = -1;
            return "not used";
        }
        
        string queryResult;
        public int maxCallsPerSecond = 2; // Shopify limits us to 2 calls into their API per second
        
        public override void GetFeedData(out List<List<string>> data, DataGroup group, DateTime exportDate, int firstRow = 1, int maxRows = 0, string extraFields = "")
        {
            var range = "";
            if (maxRows > 0)
                range = string.Format("{0},{1}", firstRow, maxRows);
            var queryParams = GetQueryParams(group, exportDate, range, extraFields);

            string DateRange = FindValueAsNonNullString(queryParams, "DateRange");
            string RowRange = FindValueAsNonNullString(queryParams, "RowRange");
            string startdate = "";
            string enddate = "";
            if (DateRange != "")
            {
                string[] dates = DateRange.Split(',');
                startdate = dates[0];
                enddate = dates[1];
            }
            InitializeGroupData(group, extraFields, startdate, enddate);
            GetGroupData(group, startdate, enddate, RowRange);
            data = ReturnGroupData(group);
        }

        private long _lastTickCount = 0;
        
        public string ShopifyGet(string url)
        {
            //NOTE: Shopify limits us to 2 calls per second
            var tickCount = DateTime.Now.Ticks;
            const int _minDelta = 750;
            if (_lastTickCount > 0)
            {
                var delta = (int)((tickCount - _lastTickCount) / TimeSpan.TicksPerMillisecond);
                if (delta < _minDelta)
                    System.Threading.Thread.Sleep(_minDelta - delta);
            }
            _lastTickCount = tickCount;

            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(Rules.ApiUrl + url);
            request.ContentType = "application/json";
            request.Headers.Add("X-Shopify-Access-Token", Rules.ApiKey);
            request.Method = "GET";
            var response = (HttpWebResponse)request.GetResponse();
            string result = null;
            using (Stream stream = response.GetResponseStream())
            {
                StreamReader sr = new StreamReader(stream);
                result = sr.ReadToEnd();
                sr.Close();
            }
            return result;
        }
                     
        // platform agnostic collection save types
        public Catalog catalog = null;
        public Sales sales = null;
        public Customers customers = null;
        public Categories categories = null;
		
        // platform specific collection save types
        public class ColLink
        {
            public string ProductID;
            public string CollectionID;

            public ColLink(string id, string prodid)
            {
                ProductID = prodid;
                CollectionID = id;
            }
        }       
        public List<ColLink> collections = null;


        private void FreeLocalStorage(bool final)
        {
            catalog = null;
            sales = null;
            customers = null;
            categories = null;
            collections = null;
        }
        
        public void InitializeGroupData(DataGroup group, string extraFields, string startdate, string enddate)
        {
            FreeLocalStorage(false);

            switch (group)
            {
                case DataGroup.Catalog:
                    // Get category ids first to support catalog retrieval.           
                    collections = new List<ColLink>();
                    GetGroupData(DataGroup.Custom, "", "", "");
                    catalog = new Catalog(Rules, false);
                    catalog.SetHeader(extraFields.Split(',').ToList(), null);
                    break;
                case DataGroup.Sales:
                    sales = new Sales(Rules);
                    break;
                case DataGroup.Customers:
                    customers = new Customers(Rules);
                    break;
                case DataGroup.CategoryNames:
                    categories = new Categories(Rules);
                    GetGroupData(DataGroup.Options, "", "", "");
                    break;
                case DataGroup.Inventory:
                    catalog = new Catalog(Rules, true);
                    catalog.SetHeader(null, null);
                    break;
            }
        }

        private void SetGetParameters(DataGroup group, string groupIdentifier, int pageSize, string minModSpecifier, string maxModSpecifier, ref string startdate, ref string enddate, ref string rowrange, out int firstPage, out int rowStart, out int lastPage, out int rowEnd, out string taretGetString)
        {  
            // initialize count get string
            string countUrl = string.Format("{0}/count.json", groupIdentifier);
            if (group == DataGroup.Sales)
                countUrl += "?status=any";

            // handle date range
            string dateRange = "";
            if (startdate != "" && enddate != "")
            {
                startdate = startdate + " 00:01:00";
                // add one day to enddate because Shopify date range is not inclusive
                DateTime enddt = Convert.ToDateTime(enddate);
                enddt = enddt.AddDays(1);
                enddate = enddt.Year + "-" + enddt.Month + "-" + enddt.Day;
                enddate = enddate + " 00:00:00";
                dateRange = string.Format("{0}={1}&{2}={3}", minModSpecifier, startdate, maxModSpecifier, enddate);
                if (group == DataGroup.Sales)
                    countUrl += "&" + dateRange;
                else
                    countUrl += "?" + dateRange;
            }

            // get entity and page counts
            int entityCount = 0;
            queryResult = ShopifyGet(countUrl); // GetQueryResponse(group, countUrl, maxCallsPerSecond);
            if (queryResult != "")
            {
                //string result = response.Content.ReadAsStringAsync().Result;
                var jObj = JObject.Parse(queryResult);
                var token = jObj.SelectToken("count");
                entityCount = (int)token;
            }
            int pageCount = (int)Math.Ceiling((double)entityCount / pageSize);
            firstPage = 0;
            lastPage = pageCount - 1;

            // handle row range
            rowStart = 0;
            rowEnd = 0;
            if (rowrange != "")
            {
                string[] rows = rowrange.Split(',');
                rowStart = int.Parse(rows[0]);
                int numberOfRows = int.Parse(rows[1]);
                // check for legal row specifications
                if (rowStart < 0 || numberOfRows <= 0)
                    rowrange = "";
                else
                {
                    // estabblish start and end page
                    rowEnd = rowStart + numberOfRows - 1;
                    // check for legal rowEnd
                    if (rowEnd > entityCount - 1)
                        rowEnd = entityCount - 1;
                    firstPage = rowStart / pageSize;
                    lastPage = rowEnd / pageSize;
                    // check for legal start and end page
                    if (firstPage > pageCount - 1)
                        lastPage = -1;
                    else if (lastPage > pageCount - 1)
                        lastPage = pageCount - 1;
                }
            }

            // set target get string
            if (dateRange != "")
                dateRange += "&";
            taretGetString = string.Format("{0}.json?limit={1}&{2}", groupIdentifier, pageSize, dateRange);
            if (group == DataGroup.Sales)
                taretGetString += "status=any&fields=created_at,id,customer,line_items&";
        }

        public void GetGroupData(DataGroup group, string startdate, string enddate, string rowrange)
        {
            // set up platform specific call params
            int pageSize = 250; // default page size, override in each case below as needed.
            string groupIdentifier = "";
            string minModSpecifier = "";
            string maxModSpecifier = "";
            switch (group)
            {
                case DataGroup.Catalog:
                    groupIdentifier = "products";
                    minModSpecifier = "created_at_min";
                    maxModSpecifier = "created_at_max";
                    break;
                case DataGroup.Sales:
                    groupIdentifier = "orders";
                    minModSpecifier = "created_at_min";
                    maxModSpecifier = "created_at_max";
                    break;
                case DataGroup.Customers:
                    groupIdentifier = "customers";
                    minModSpecifier = "created_at_min";
                    maxModSpecifier = "created_at_max";
                    break;
                case DataGroup.CategoryNames:
                    groupIdentifier = "custom_collections";
                    break;
                case DataGroup.Inventory:
                    groupIdentifier = "products";
                    minModSpecifier = "updated_at_min";
                    maxModSpecifier = "updated_at_max";
                    break;
                case DataGroup.Custom:
                    groupIdentifier = "collects";
                    break;
                case DataGroup.Options:
                    groupIdentifier = "smart_collections";
                    break;
            }

            int lastPage;
            int firstPage;
            int rowStart;
            int rowEnd;
            string taretGetString;

            this.SetGetParameters(group, groupIdentifier, pageSize, minModSpecifier, maxModSpecifier, ref startdate, ref enddate, ref rowrange, out firstPage, out rowStart, out lastPage, out rowEnd, out taretGetString);

            if (lastPage >= firstPage)
            {
                for (int i = firstPage; i <= lastPage; i++)
                {
                    queryResult = ShopifyGet(taretGetString + "page=" + (i + 1).ToString()); // GetQueryResponse(group, taretGetString + "page=" + (i + 1).ToString(), maxCallsPerSecond);
                    JArray entities = JArray.Parse(JObject.Parse(queryResult).SelectToken(groupIdentifier).ToString());
                    int count = entities.Count;
                    int firstEntity = (rowrange != "" && i == firstPage) ? rowStart - firstPage * pageSize : 0;
                    int lastEntity = (rowrange != "" && i == lastPage) ? rowEnd - lastPage * pageSize : count - 1;
                    for (int j = firstEntity; j <= lastEntity; j++)
                        GetEntity(group, (JObject)(entities[j]));
                }
            }
        }

        public void GetEntity(DataGroup group, JObject entity)
        {
            switch (group)
            {
                case DataGroup.Catalog:
                case DataGroup.Inventory: 
                    //{
                    //  "product": {
                    //    "id": 632910392,
                    //    "title": "IPod Nano - 8GB",
                    //    "body_html": "<p>It's the small iPod with one very big idea: Video. Now the world's most popular music player, available in 4GB and 8GB models, lets you enjoy TV shows, movies, video podcasts, and more. The larger, brighter display means amazing picture quality. In six eye-catching colors, iPod nano is stunning all around. And with models starting at just $149, little speaks volumes.<\/p>",
                    //    "vendor": "Apple",
                    //    "product_type": "Cult Products",
                    //    "created_at": "2015-09-02T14:50:32-04:00",
                    //    "handle": "ipod-nano",
                    //    "updated_at": "2015-09-02T14:50:32-04:00",
                    //    "published_at": "2007-12-31T19:00:00-05:00",
                    //    "template_suffix": null,
                    //    "published_scope": "web",
                    //    "tags": "Emotive, Flash Memory, MP3, Music",
                    //    "variants": [
                    //      {
                    //        "id": 808950810,
                    //        "product_id": 632910392,
                    //        "title": "Pink",
                    //        "sku": "IPOD2008PINK",
                    //        "position": 1,
                    //        "grams": 200,
                    //        "inventory_policy": "continue",
                    //        "fulfillment_service": "manual",
                    //        "inventory_management": "shopify",
                    //        "price": "199.00",
                    //        "compare_at_price": null,
                    //        "option1": "Pink",
                    //        "option2": null,
                    //        "option3": null,
                    //        "created_at": "2015-09-02T14:50:32-04:00",
                    //        "updated_at": "2015-09-02T14:50:32-04:00",
                    //        "taxable": true,
                    //        "requires_shipping": true,
                    //        "barcode": "1234_pink",
                    //        "inventory_quantity": 10,
                    //        "old_inventory_quantity": 10,
                    //        "image_id": 562641783,
                    //        "weight": 0.2,
                    //        "weight_unit": "kg"
                    //      }
                    //    ],
                    //    "options": [
                    //      {
                    //        "id": 594680422,
                    //        "product_id": 632910392,
                    //        "name": "Color",
                    //        "position": 1,
                    //        "values": [
                    //          "Pink",
                    //          "Red",
                    //          "Green",
                    //          "Black"
                    //        ]
                    //      }
                    //    ],
                    //    "images": [
                    //      {
                    //        "id": 850703190,
                    //        "product_id": 632910392,
                    //        "position": 1,
                    //        "created_at": "2015-09-02T14:50:32-04:00",
                    //        "updated_at": "2015-09-02T14:50:32-04:00",
                    //        "src": "https:\/\/cdn.shopify.com\/s\/files\/1\/0006\/9093\/3842\/products\/ipod-nano.png?v=1441219832",
                    //        "variant_ids": [
                    //        ]
                    //      },
                    //      {
                    //        "id": 562641783,
                    //        "product_id": 632910392,
                    //        "position": 2,
                    //        "created_at": "2015-09-02T14:50:32-04:00",
                    //        "updated_at": "2015-09-02T14:50:32-04:00",
                    //        "src": "https:\/\/cdn.shopify.com\/s\/files\/1\/0006\/9093\/3842\/products\/ipod-nano-2.png?v=1441219832",
                    //        "variant_ids": [
                    //          808950810
                    //        ]
                    //      }
                    //    ],
                    //    "image": {
                    //      "id": 850703190,
                    //      "product_id": 632910392,
                    //      "position": 1,
                    //      "created_at": "2015-09-02T14:50:32-04:00",
                    //      "updated_at": "2015-09-02T14:50:32-04:00",
                    //      "src": "https:\/\/cdn.shopify.com\/s\/files\/1\/0006\/9093\/3842\/products\/ipod-nano.png?v=1441219832",
                    //      "variant_ids": [
                    //      ]
                    //    }
                    //  }
                    //}
                    string _PIdName = Rules.Fields.GetName(FieldName.ProductId);
                    string _NameName = Rules.Fields.GetName(FieldName.Name);
                    string _Att1Name = Rules.UseDepartmentsAsCategories
                                                ? Rules.Fields.GetName(FieldName.Department)
                                                : Rules.Fields.GetName(FieldName.Att1Id);
                    string _Att2Name = Rules.Fields.GetName(FieldName.Att2Id);
                    string _PriceName = Rules.Fields.GetName(FieldName.Price);
                    string _SaleName = Rules.Fields.GetName(FieldName.SalePrice);
                    string _ListName = Rules.Fields.GetName(FieldName.ListPrice);
                    string _CostName = Rules.Fields.GetName(FieldName.Cost);
                    string _InvName = Rules.Fields.GetName(FieldName.Inventory);
                    string _VisName = Rules.Fields.GetName(FieldName.Visible);
                    string _LinkName = Rules.Fields.GetName(FieldName.Link);
                    string _ImagName = Rules.Fields.GetName(FieldName.ImageLink);
                    string _RateName = Rules.Fields.GetName(FieldName.Rating);
                    string _ParentIdName = Rules.Fields.GetName(FieldName.ParentId);
                    string _CodeName = Rules.Fields.GetName(FieldName.StandardCode);

                    Catalog.Entry entry = new Catalog.Entry();

                    try
                    {
                        // Standard Fields
                        entry.ProductID = entity.SelectToken(_PIdName).ToString();
                        entry.Name = entity.SelectToken(_NameName).ToString();
                        if (collections != null)
                        {
                            foreach (var collection in collections)
                            {
                                if (collection.ProductID.Equals(entry.ProductID))
                                    entry.AddCategory(collection.CollectionID);
                            }
                        }
						entry.ManufacturerID = entity.SelectToken(_Att2Name) == null ? "" : entity.SelectToken(_Att2Name).ToString();
                        entry.Visible = entity.SelectToken(_VisName) == null ? 0 : 1;
                        string plink = Rules.ApiUrl.Replace("/admin/", "/products/") + entity.SelectToken(_LinkName).ToString();                   
                        entry.Link = plink.Replace(' ', '-');                       

                        if (entity.SelectToken("image") != null)
                            if (entity.SelectToken(_ImagName) != null)
                                entry.Imagelink = entity.SelectToken(_ImagName).ToString();                    
                                                                                            
                        // Extra Fields
                        if (catalog.extraFieldNames != null)
                            foreach (string extraFieldName in catalog.extraFieldNames)
                                if (extraFieldName != "")
                                    entry.ExtraFields.Add(entity.SelectToken(extraFieldName) == null ? "" : entity.SelectToken(extraFieldName).ToString());
                    }
                    catch (RuntimeBinderException e)
                    {
                        //throw;
                    }

                    catalog.AddEntry(entry);

                    // Now get product options as children
                    JArray variants = (JArray)entity.SelectToken("variants");
                    if (variants != null)
                    {
                        foreach (JObject variant in variants)
                        {
                            try
                            {                                
                                Catalog.Entry childEntry = new Catalog.Entry();

                                // Standard Fields
                                childEntry.ProductID = variant.SelectToken(_PIdName).ToString();
                                childEntry.Price = variant.SelectToken(_PriceName).ToString();
                                childEntry.ListPrice = variant.SelectToken(_ListName) == null ? "" : variant.SelectToken(_ListName).ToString();
                                childEntry.ParentProductID = entry.ProductID;
                                childEntry.StandardCode = variant.SelectToken(_CodeName).ToString();
                                childEntry.Inventory = variant.SelectToken(_InvName).ToString();
                                childEntry.ManufacturerID = entry.ManufacturerID;
                                childEntry.Visible = entry.Visible;
                                childEntry.Name = variant.SelectToken(_NameName) + " " + entry.Name;

                                // Extra Fields
                                if (catalog.extraFieldNames != null)
                                    foreach (string extraFieldName in catalog.extraFieldNames)
                                        if (extraFieldName != "")
                                            childEntry.ExtraFields.Add(entity.SelectToken(extraFieldName) == null ? "" : entity.SelectToken(extraFieldName).ToString());

                                catalog.AddEntry(childEntry);
                            }
                            catch (RuntimeBinderException e)
                            {
                                //throw;
                            }
                        }
                    }
                    break;
                case DataGroup.Sales:
                    //{
                    //"id": 450789469,
                    //"email": "bob.norman@hostmail.com",
                    //"closed_at": null,
                    //"created_at": "2008-01-10T11:00:00-05:00",
                    //"updated_at": "2008-01-10T11:00:00-05:00",
                    //"number": 1,
                    //"note": null,
                    //"token": "b1946ac92492d2347c6235b4d2611184",
                    //"gateway": "authorize_net",
                    //"test": false,
                    //"total_weight": 0,
                    //"taxes_included": false,
                    //"currency": "USD",
                    //"financial_status": "authorized",
                    //"confirmed": false,
                    //"cart_token": "68778783ad298f1c80c3bafcddeea02f",
                    //"buyer_accepts_marketing": false,
                    //"name": "#1001",
                    //"total_line_items_price": "398.00",
                    //"total_price": "409.94",
                    //"subtotal_price": "398.00",
                    //"total_tax": "11.94",
                    //"total_discounts": "0.00",
                    //"referring_site": "http:\/\/www.otherexample.com",
                    //"landing_site": "http:\/\/www.example.com?source=abc",
                    //"cancelled_at": null,
                    //"cancel_reason": null,
                    //"total_price_usd": "409.94",
                    //"checkout_token": null,
                    //"reference": "fhwdgads",
                    //"user_id": null,
                    //"location_id": null,
                    //"source_identifier": "fhwdgads",
                    //"source_url": null,
                    //"processed_at": "2008-01-10T11:00:00-05:00",
                    //"device_id": null,
                    //"browser_ip": null,
                    //"landing_site_ref": "abc",
                    //"order_number": 1001,
                    //"line_items": [
                    //  {
                    //    "id": 466157049,
                    //    "variant_id": 39072856,
                    //    "title": "IPod Nano - 8gb",
                    //    "quantity": 1,
                    //    "grams": 200,
                    //    "sku": "IPOD2008GREEN",
                    //    "variant_title": "green",
                    //    "vendor": null,
                    //    "fulfillment_service": "manual",
                    //    "price": "199.00",
                    //    "product_id": 632910392,
                    //    "taxable": true,
                    //    "requires_shipping": true,
                    //    "gift_card": false,
                    //    "name": "IPod Nano - 8gb - green",
                    //    "variant_inventory_management": "shopify",
                    //    "properties": [
                    //      {
                    //        "name": "Custom Engraving Front",
                    //        "value": "Happy Birthday"
                    //      },
                    //      {
                    //        "name": "Custom Engraving Back",
                    //        "value": "Merry Christmas"
                    //      }
                    //    ],
                    //    "product_exists": true,
                    //    "fulfillable_quantity": 1,
                    //    "total_discount": "0.00",
                    //    "fulfillment_status": null,
                    //    "tax_lines": [
                    //    ]
                    //  },                   
                    //],
                    //"customer": {
                    //  "id": 207119551,
                    //  "email": "bob.norman@hostmail.com",
                    //  "accepts_marketing": false,
                    //  "created_at": "2015-09-02T14:48:56-04:00",
                    //  "updated_at": "2015-09-02T14:48:56-04:00",
                    //  "first_name": "Bob",
                    //  "last_name": "Norman",
                    //  "orders_count": 1,
                    //  "state": "disabled",
                    //  "total_spent": "41.94",
                    //  "last_order_id": 450789469,
                    //  "note": null,
                    //  "verified_email": true,
                    //  "multipass_identifier": null,
                    //  "tax_exempt": false,
                    //  "tags": "",
                    //  "last_order_name": "#1001",
                    //  "default_address": {
                    //    "id": 207119551,
                    //    "first_name": null,
                    //    "last_name": null,
                    //    "company": null,
                    //    "address1": "Chestnut Street 92",
                    //    "address2": "",
                    //    "city": "Louisville",
                    //    "province": "Kentucky",
                    //    "country": "United States",                    
                    try
                    {
                        string orderId = entity.SelectToken(Rules.Fields.GetName(FieldName.OrderId)).ToString();
                        string customerId = entity.SelectToken(Rules.Fields.GetName(FieldName.OrderCustomerId)).ToString();
                        if (customerId != "0" && customerId != "")
                        {
                            DateTime temp = Convert.ToDateTime(entity.SelectToken(Rules.Fields.GetName(FieldName.OrderDate)).ToString());
                            string _OrderProductIdName = Rules.Fields.GetName(FieldName.OrderProductId);
                            string _OrderQuantityName = Rules.Fields.GetName(FieldName.OrderQuantity);

                            JArray Products = JArray.Parse(entity.SelectToken("line_items").ToString());
                            foreach (JObject product in Products)
                            {
                            	Sale s = new Sale();

                                s.ProductID = product.SelectToken(_OrderProductIdName).ToString();
                                s.Quantity = product.SelectToken(_OrderQuantityName).ToString();
                                s.OrderID = orderId;
                                s.CustomerID = customerId;
                                s.Date = temp.ToShortDateString();

                            	sales.AddSale(s);
                            }

                        }
                    }
                    catch (RuntimeBinderException e)
                    {
                        //throw;
                    }
                    break;
                case DataGroup.Customers:
                    // {
                    //  "id": 207119551,
                    //  "email": "bob.norman@hostmail.com",
                    //  "accepts_marketing": false,
                    //  "created_at": "2015-09-02T14:52:15-04:00",
                    //  "updated_at": "2015-09-02T14:52:15-04:00",
                    //  "first_name": "Bob",
                    //  "last_name": "Norman",
                    //  "orders_count": 1,
                    //  "state": "disabled",
                    //  "total_spent": "41.94",
                    //  "last_order_id": 450789469,
                    //  "note": null,
                    //  "verified_email": true,
                    //  "multipass_identifier": null,
                    //  "tax_exempt": false,
                    //  "tags": "",
                    //  "last_order_name": "#1001",
                    //  "default_address": {
                    //    "id": 207119551,
                    //    "first_name": null,
                    //    "last_name": null,
                    //    "company": null,
                    //    "address1": "Chestnut Street 92",
                    //    "address2": "",
                    //    "city": "Louisville",
                    //    "province": "Kentucky",
                    //    "country": "United States",
                    //    "zip": "40202",
                    //    "phone": "555-625-1199",
                    //    "name": "",
                    //    "province_code": "KY",
                    //    "country_code": "US",
                    //    "country_name": "United States",
                    //    "default": true
                    //  },
                    //  "addresses": [
                    //    {
                    //      "id": 207119551,
                    //      "first_name": null,
                    //      "last_name": null,
                    //      "company": null,
                    //      "address1": "Chestnut Street 92",
                    //      "address2": "",
                    //      "city": "Louisville",
                    //      "province": "Kentucky",
                    //      "country": "United States",
                    //      "zip": "40202",
                    //      "phone": "555-625-1199",
                    //      "name": "",
                    //      "province_code": "KY",
                    //      "country_code": "US",
                    //      "country_name": "United States",
                    //      "default": true
                    //    }
                    //  ]
                    //}                      
                    Customer c = new Customer();                 
                    try
                    {
                        c.CustomerID = entity.SelectToken(Rules.Fields.GetName(FieldName.CustomerId)).ToString();
                        c.Email = entity.SelectToken("email") == null ? "" : entity.SelectToken("email").ToString();
                        c.Name = entity.SelectToken("first_name").ToString() + " " + entity.SelectToken("last_name").ToString();
                        c.Address = (entity.SelectToken("default_address.address1") == null ? "" : entity.SelectToken("default_address.address1").ToString()) + " "
                            + (entity.SelectToken("default_address.address2") == null ? "" : entity.SelectToken("default_address.address2").ToString());
                        c.City = entity.SelectToken("default_address.city") == null ? "" : entity.SelectToken("default_address.city").ToString();
                        c.State = entity.SelectToken("default_address.province") == null ? "" : entity.SelectToken("default_address.province").ToString();
                        c.PostalCode = entity.SelectToken("default_address.zip") == null ? "" : entity.SelectToken("default_address.zip").ToString();
                        c.Country = entity.SelectToken("default_address.country") == null ? "" : entity.SelectToken("default_address.country").ToString();
                        c.Phone = entity.SelectToken("default_address.phone") == null ? "" : entity.SelectToken("default_address.phone").ToString();
                        c.Gender = "";
                        c.Birthday = "";
                        c.AgeRange = "";
                        c.AlternativeIDs = "";
                        c.DoNotTrack = "";
                    }
                    catch (RuntimeBinderException e)
                    {
                        //throw;
                    }
                    customers.AddCustomer(c);
                    break;
                case DataGroup.Custom:
                    // "collects": [
                    //  {
                    //    "id": 395646240,
                    //    "collection_id": 395646240,
                    //    "product_id": 632910392,
                    //    "featured": false,
                    //    "created_at": null,
                    //    "updated_at": null,
                    //    "position": 1,
                    //    "sort_value": "0000000001"
                    //  },
                    //]
                    try
                    {
                        collections.Add(new ColLink(entity.SelectToken("collection_id").ToString(), entity.SelectToken("product_id").ToString()));
                    }
                    catch (RuntimeBinderException e)
                    {
                        //throw;
                    }
                    break;      
                case DataGroup.CategoryNames:
                case DataGroup.Options:
                    // "custom_collection": {
                    //  "id": 841564295,
                    //  "handle": "ipods",
                    //  "title": "IPods",
                    //  "updated_at": "2008-02-01T19:00:00-05:00",
                    //  "body_html": "<p>The best selling ipod ever<\/p>",
                    //  "published_at": "2008-02-01T19:00:00-05:00",
                    //  "sort_order": "manual",
                    //  "template_suffix": null,
                    //  "products_count": 1,
                    //  "published_scope": "global",
                    //  "image": {
                    //    "created_at": "2015-09-02T14:48:56-04:00",
                    //    "src": "https:\/\/cdn.shopify.com\/s\/files\/1\/0006\/9093\/3842\/collections\/ipod_nano_8gb.jpg?v=1441219736"
                    //  }
                    //}
                
                    //"smart_collections": [
                    //{
                    //  "id": 482865238,
                    //  "handle": "smart-ipods",
                    //  "title": "Smart iPods",
                    //  "updated_at": "2008-02-01T19:00:00-05:00",
                    //  "body_html": "<p>The best selling ipod ever<\/p>",
                    //  "published_at": "2008-02-01T19:00:00-05:00",
                    //  "sort_order": "manual",
                    //  "template_suffix": null,
                    //  "published_scope": "global",
                    //  "disjunctive": false,
                    //  "rules": [
                    //    {
                    //      "column": "type",
                    //      "relation": "equals",
                    //      "condition": "Cult Products"
                    //    }
                    //  ],
                    //  "image": {
                    //    "created_at": "2015-10-27T15:26:33-04:00",
                    //    "src": "https:\/\/cdn.shopify.com\/s\/files\/1\/0006\/9093\/3842\/collections\/ipod_nano_8gb.jpg?v=1445973993"
                    //  }
                    try
                    {
                        categories.AddCategory(entity.SelectToken(Rules.Fields.GetName(FieldName.Att1NameId)).ToString(), entity.SelectToken(Rules.Fields.GetName(FieldName.Att1NameName)).ToString());
                    }
                    catch (RuntimeBinderException e)
                    {
                        //throw;
                    }
                    break;
                case DataGroup.DepartmentNames:
                case DataGroup.All:
                    break;
                default:
                    throw new ArgumentOutOfRangeException("group");
            }
        }

        public List<List<string>> ReturnGroupData(DataGroup group)
        {
            List<List<string>> result = null;
            switch (group)
            {
                case DataGroup.Catalog:
                    result = catalog.GetCatalog();
                    break;
                case DataGroup.Sales:
                    result = sales.GetSales();
                    break;
                case DataGroup.Customers:
                    result = customers.GetCustomers();
                    break;
                case DataGroup.CategoryNames:
                    result = categories.GetCategories();
                    break;
                case DataGroup.Inventory:
                    result = catalog.GetCatalog();
                    break;
                case DataGroup.ManufacturerNames:
                case DataGroup.DepartmentNames:
                case DataGroup.All:
                case DataGroup.Custom:
                case DataGroup.Options:
                    result = null;
                    break;
                default:
                    throw new ArgumentOutOfRangeException("group");
            }
            return result;
        }
    }
}