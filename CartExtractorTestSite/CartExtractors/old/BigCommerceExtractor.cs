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
    public sealed class BigCommerceExtractor : CartExtractor
    {
        private string _apiKey = "";
        private string _userName = "";
        private BigCommerceAdaptor adaptor = null;
        
        public BigCommerceExtractor(SiteRules rules)
            : base(rules)
        {
            //TODO: Implement ValidateCredentials for all carts and move this check to the base class
            var status = "";
            HasCredentials = ValidateCredentials(out status);
            if (!HasCredentials && BoostLog.Instance != null)
                BoostLog.Instance.WriteEntry(EventLogEntryType.Error, "Invalid BigCommerce API Credentials", status, Alias);
            else
                // determine feed types
                SetFeedTypes();

            adaptor = new BigCommerceAdaptor(_apiKey, _userName, Rules.ApiUrl, this);
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
                if (true) //s._BigCommerce != null)
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

        protected override void ReleaseCartData()
        {
        }

        // Only required if the order confirmation page does not include order details, which we use to create the auto actions file based on real time updates,
        // Some platforms don't show order details on the confirmation page, just provide an order index so our JS can't scrape the order info we need, in this case our
        // service comes here to find the order information based on order id.
        public override void LogSalesOrder(string orderID)
        {
            try
            {
                adaptor.LogSalesOrder(orderID);
            }
            catch (Exception ex)
            {
                if (Log != null)
                    Log.WriteEntry(EventLogEntryType.Error, "Error logging Sales Order", ex, Alias);
                return;
            }
        }

        protected override string GetInventory(out int itemCount)
        {
            return Inventory.GetData(out itemCount, DateTime.Now);
        }
        
		#endregion

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
            adaptor.InitializeGroupData(group, extraFields); 
            adaptor.GetGroupData(group, startdate, enddate, RowRange);      
            data = adaptor.ReturnGroupData(group);
        }   
    }

    public class BigCommerceAdaptor
    {       
        string api_key = "";
        string username = "";
        //HttpClient client = null;
        //HttpResponseMessage response = null;
        string queryResult; 
        BigCommerceExtractor extractor = null;

        public BigCommerceAdaptor(string api_key, string username, string baseUrl, BigCommerceExtractor extractor)
        {
            this.api_key = api_key;
            this.username = username;
            /*HttpClientHandler handler = new HttpClientHandler();
            handler.Credentials = new NetworkCredential(username, api_key);
            this.client = new HttpClient(handler)
            {
                BaseAddress = new Uri(baseUrl),
            };*/
            this.extractor = extractor;
        }       

        // platform agnostic collection save types
        public Catalog catalog = null;
        public Sales sales = null;
        public Customers customers = null;
        public Categories categories = null;
        public Brands brands = null;
        public CustomFields customfields = null;
        public Options options = null;

        public void InitializeGroupData(DataGroup group, string extraFields)
        {
            catalog = null;
            sales = null;
            customers = null;
            categories = null;
            brands = null;
            customfields = null;
            options = null;
            
            switch (group)
            {
                case DataGroup.Catalog:
                    // Get customfields, categories and brands first to support catalog retrieval.
                    customfields = new CustomFields();            
                    GetGroupData(DataGroup.Custom, "", "", "");
                    categories = new Categories(extractor.Rules);
                    GetGroupData(DataGroup.CategoryNames, "", "", "");
                    brands = new Brands(extractor.Rules);
                    GetGroupData(DataGroup.ManufacturerNames, "", "", "");
                    options = new Options();
                    GetGroupData(DataGroup.Options, "", "", "");
                    catalog = new Catalog(extractor.Rules, false);
                    catalog.SetHeader(extraFields.Split(',').ToList(), customfields.GetUniqueNames());
                    break;
                case DataGroup.Sales:
                    sales = new Sales(extractor.Rules);
                    break;
                case DataGroup.Customers:
                    customers = new Customers(extractor.Rules);
                    break;
                case DataGroup.CategoryNames:
                    categories = new Categories(extractor.Rules);
                    break;
                case DataGroup.ManufacturerNames:
                    brands = new Brands(extractor.Rules);
                    break;
                case DataGroup.Inventory:
                    customfields = new CustomFields();            
                    GetGroupData(DataGroup.Custom, "", "", "");
                    catalog = new Catalog(extractor.Rules, true);
                    catalog.SetHeader(extraFields.Split(',').ToList(), customfields.GetUniqueNames());
                    break;
            }
        }

        private void SetGetParameters(DataGroup group, string groupIdentifier, int pageSize, string minModSpecifier, string maxModSpecifier, ref string startdate, ref string enddate, ref string rowrange, out int firstPage, out int rowStart, out int lastPage, out int rowEnd, out string taretGetString)
        {           
            // initialize count get string
            string countUrl = string.Format("{0}/count.json", groupIdentifier);

            // handle date range
            string dateRange = "";
            if (startdate != "" && enddate != "")
            {
                DateTime startTime = Convert.ToDateTime(startdate); 
                DateTime endTime = Convert.ToDateTime(enddate);
                string bt = Rfc822DateTime.ToString(startTime.ToUniversalTime()).Replace("Z", "-0700").Trim();
                string et = Rfc822DateTime.ToString(endTime.ToUniversalTime()).Replace("Z", "-0700").Trim();
                dateRange = string.Format("{0}={1}&{2}={3}", minModSpecifier, startdate, maxModSpecifier, enddate);
                countUrl += "?" + dateRange;
            }

            // get entity and page counts
            int entityCount = 0;
            queryResult = extractor.GetQueryResponse(group, countUrl);
            //HttpResponseMessage response = client.GetAsync(countUrl).Result;
            //if (response.IsSuccessStatusCode)
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
        }

        public void GetGroupData(DataGroup group, string startdate, string enddate, string rowrange)
        {   
            // set up platform specific call params
            int pageSize = 0;
            string groupIdentifier = "";
            string minModSpecifier = "";
            string maxModSpecifier = ""; 
            switch (group)
            {
                case DataGroup.Catalog:
                    pageSize = 250;
                    groupIdentifier = "products";
                    minModSpecifier = "min_date_created";
                    maxModSpecifier = "max_date_created";
                    break;
                case DataGroup.Sales:
                    pageSize = 50;
                    groupIdentifier = "orders";
                    minModSpecifier = "min_date_created";
                    maxModSpecifier = "max_date_created";
                    break;
                case DataGroup.Customers:
                    pageSize = 100;
                    groupIdentifier = "customers";
                    minModSpecifier = "min_date_created";
                    maxModSpecifier = "max_date_created";
                    break;
                case DataGroup.CategoryNames:
                    startdate = "";
                    enddate = "";
                    rowrange = "";
                    pageSize = 250;
                    groupIdentifier = "categories";
                    break;
                case DataGroup.ManufacturerNames:
                    startdate = "";
                    enddate = "";
                    rowrange = "";
                    pageSize = 250;
                    groupIdentifier = "brands";
                    break;
                case DataGroup.Inventory:
                    pageSize = 250;
                    groupIdentifier = "products";
                    minModSpecifier = "min_date_modified";
                    maxModSpecifier = "max_date_modified";
                    break;
                case DataGroup.Custom:
                    startdate = "";
                    enddate = "";
                    rowrange = "";
                    pageSize = 250;
                    groupIdentifier = "products/customfields";
                    break;
                case DataGroup.Options:
                    startdate = "";
                    enddate = "";
                    rowrange = "";
                    pageSize = 250;
                    groupIdentifier = "products/rules";
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
                    //response = client.GetAsync(taretGetString + "page=" + (i + 1).ToString()).Result;
                    //result = response.Content.ReadAsStringAsync().Result;
                    queryResult = extractor.GetQueryResponse(group, taretGetString + "page=" + (i + 1).ToString());
                    JArray entities = JArray.Parse(queryResult);
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
                    //  "id": 32,
                    //  "keyword_filter": null,
                    //  "name": "[Sample] Tomorrow is today, Red printed scarf",
                    //  "type": "physical",
                    //  "sku": "",
                    //  "description": "Densely pack your descriptions with useful information and watch products fly off the shelf.",
                    //  "search_keywords": null,
                    //  "availability_description": "",
                    //  "price": "89.0000",
                    //  "cost_price": "0.0000",
                    //  "retail_price": "0.0000",
                    //  "sale_price": "0.0000",
                    //  "calculated_price": "89.0000",
                    //  "sort_order": 0,
                    //  "is_visible": true,
                    //  "is_featured": true,
                    //  "related_products": "-1",
                    //  "inventory_level": 0,
                    //  "inventory_warning_level": 0,
                    //  "warranty": null,
                    //  "weight": "0.3000",
                    //  "width": "0.0000",
                    //  "height": "0.0000",
                    //  "depth": "0.0000",
                    //  "fixed_cost_shipping_price": "10.0000",
                    //  "is_free_shipping": false,
                    //  "inventory_tracking": "none",
                    //  "rating_total": 0,
                    //  "rating_count": 0,
                    //  "total_sold": 0,
                    //  "date_created": "Fri, 21 Sep 2012 02:31:01 +0000",
                    //  "brand_id": 17,
                    //  "view_count": 4,
                    //  "page_title": "",
                    //  "meta_keywords": null,
                    //  "meta_description": null,
                    //  "layout_file": "product.html",
                    //  "is_price_hidden": false,
                    //  "price_hidden_label": "",
                    //  "categories": [
                    //    14
                    //  ],
                    //  "date_modified": "Mon, 24 Sep 2012 01:34:57 +0000",
                    //  "event_date_field_name": "Delivery Date",
                    //  "event_date_type": "none",
                    //  "event_date_start": "",
                    //  "event_date_end": "",
                    //  "myob_asset_account": "",
                    //  "myob_income_account": "",
                    //  "myob_expense_account": "",
                    //  "peachtree_gl_account": "",
                    //  "condition": "New",
                    //  "is_condition_shown": false,
                    //  "preorder_release_date": "",
                    //  "is_preorder_only": false,
                    //  "preorder_message": "",
                    //  "order_quantity_minimum": 0,
                    //  "order_quantity_maximum": 0,
                    //  "open_graph_type": "product",
                    //  "open_graph_title": "",
                    //  "open_graph_description": null,
                    //  "is_open_graph_thumbnail": true,
                    //  "upc": null,
                    //  "avalara_product_tax_code": "",
                    //  "date_last_imported": "",
                    //  "option_set_id": null,
                    //  "tax_class_id": 0,
                    //  "option_set_display": "right",
                    //  "bin_picking_number": "",
                    //  "custom_url": "/tomorrow-is-today-red-printed-scarf/",
                    //  "primary_image": {
                    //    "id": 247,
                    //    "zoom_url": "https://cdn.url.path/bcapp/et7xe3pz/products/32/images/247/in_123__14581.1393831046.1280.1280.jpg?c=1",
                    //    "thumbnail_url": "https://cdn.url.path/bcapp/et7xe3pz/products/32/images/247/in_123__14581.1393831046.220.290.jpg?c=1",
                    //    "standard_url": "https://cdn.url.path/bcapp/et7xe3pz/products/32/images/247/in_123__14581.1393831046.386.513.jpg?c=1",
                    //    "tiny_url": "https://cdn.url.path/bcapp/et7xe3pz/products/32/images/247/in_123__14581.1393831046.44.58.jpg?c=1"
                    //  },
                    //  "availability": "available",
                    //  "brand": {
                    //    "url": "https://store-et7xe3pz.mybigcommerce.com/api/v2/brands/17.json",
                    //    "resource": "/brands/17"
                    //  },
                    //  "images": {
                    //    "url": "https://store-et7xe3pz.mybigcommerce.com/api/v2/products/32/images.json",
                    //    "resource": "/products/32/images"
                    //  },
                    //  "discount_rules": {
                    //    "url": "https://store-et7xe3pz.mybigcommerce.com/api/v2/products/32/discountrules.json",
                    //    "resource": "/products/32/discountrules"
                    //  },
                    //  "configurable_fields": {
                    //    "url": "https://store-et7xe3pz.mybigcommerce.com/api/v2/products/32/configurablefields.json",
                    //    "resource": "/products/32/configurablefields"
                    //  },
                    //  "custom_fields": {
                    //    "url": "https://store-et7xe3pz.mybigcommerce.com/api/v2/products/32/customfields.json",
                    //    "resource": "/products/32/customfields"
                    //  },
                    //  "videos": {
                    //    "url": "https://store-et7xe3pz.mybigcommerce.com/api/v2/products/32/videos.json",
                    //    "resource": "/products/32/videos"
                    //  },
                    //  "skus": {
                    //    "url": "https://store-et7xe3pz.mybigcommerce.com/api/v2/products/32/skus.json",
                    //    "resource": "/products/32/skus"
                    //  },
                    //  "rules": {
                    //    "url": "https://store-et7xe3pz.mybigcommerce.com/api/v2/products/32/rules.json",
                    //    "resource": "/products/32/rules"
                    //  },
                    //  "option_set": null,
                    //  "options": {
                    //    "url": "https://store-et7xe3pz.mybigcommerce.com/api/v2/products/32/options.json",
                    //    "resource": "/products/32/options"
                    //  },
                    //  "tax_class": {
                    //    "url": "https://store-et7xe3pz.mybigcommerce.com/api/v2/taxclasses/0.json",
                    //    "resource": "/taxclasses/0"
                    //  }
                    //}
                    string _PIdName = extractor.Rules.Fields.GetName(FieldName.ProductId);
                    string _NameName = extractor.Rules.Fields.GetName(FieldName.Name);
                    string _Att1Name = extractor.Rules.UseDepartmentsAsCategories
                                                ? extractor.Rules.Fields.GetName(FieldName.Department)
                                                : extractor.Rules.Fields.GetName(FieldName.Att1Id);
                    string _Att2Name = extractor.Rules.Fields.GetName(FieldName.Att2Id);
                    string _PriceName = extractor.Rules.Fields.GetName(FieldName.Price);
                    string _SaleName = extractor.Rules.Fields.GetName(FieldName.SalePrice);
                    string _ListName = extractor.Rules.Fields.GetName(FieldName.ListPrice);
                    string _CostName = extractor.Rules.Fields.GetName(FieldName.Cost);
                    string _InvName = extractor.Rules.Fields.GetName(FieldName.Inventory);
                    string _VisName = extractor.Rules.Fields.GetName(FieldName.Visible);
                    string _LinkName = extractor.Rules.Fields.GetName(FieldName.Link);
                    string _ImagName = extractor.Rules.Fields.GetName(FieldName.ImageLink);
                    string _RateName = extractor.Rules.Fields.GetName(FieldName.Rating);
                    string _ParentIdName = extractor.Rules.Fields.GetName(FieldName.ParentId);
                    string _CodeName = extractor.Rules.Fields.GetName(FieldName.StandardCode);
                    
                    Catalog.Entry entry = new Catalog.Entry();
                    
                    try
                    {                                      
                        // Standard Fields
                        entry.ProductID = entity.SelectToken(_PIdName).ToString();
                        entry.Name = entity.SelectToken(_NameName).ToString();
                        JArray categoryIds = (JArray)entity.SelectToken(_Att1Name);
                        foreach (JValue categoryId in categoryIds)
                            entry.AddCategory(categoryId.ToString());
                        string brand = "";
                        if (brands != null && brands.d.TryGetValue(entity.SelectToken(_Att2Name).ToString(), out brand))
                            entry.ManufacturerID = brand;
                        entry.Visible = (int)entity.SelectToken(_VisName);
                        entry.Link = entity.SelectToken(_LinkName).ToString();
                        entry.Imagelink = entity.SelectToken(_ImagName).ToString();
						entry.Price = entity.SelectToken(_PriceName).ToString();
                        entry.SalePrice = entity.SelectToken(_SaleName).ToString();
                        entry.ListPrice = entity.SelectToken(_ListName).ToString();
                        entry.Cost = entity.SelectToken(_CostName).ToString();
                        entry.Inventory = entity.SelectToken(_InvName).ToString(); 
                        entry.Rating = entity.SelectToken(_RateName).ToString();                   
                        entry.StandardCode = entity.SelectToken(_CodeName).ToString();
                        
                        // Extra Fields
                        foreach (string extraFieldName in catalog.extraFieldNames)
                            if (extraFieldName != "")
                                entry.ExtraFields.Add(entity.SelectToken(extraFieldName) == null ? "" : entity.SelectToken(extraFieldName).ToString()); 
                        
                        // Custom Fields
                        // walk the column names and for each determine whether the product at hand has one of these custom field, if so post it's value in the position corresponding to the index of name in customFieldNames
                        bool found = false;
                        foreach (string customFileName in catalog.customFieldNames)
                        {
                            found = false;
                            foreach (CustomField field in customfields.fields)
                            {
                                // see if there's a customfield for this product and it matches the custom field we're on
                                if (field.ProductID == entry.ProductID && field.Name == customFileName) 
                                {
                                    found = true;
                                    entry.CustomFields.Add(field.Value);
                                    break;
                                }
                            }
                            if (found == false)
                                entry.CustomFields.Add("");
                        }
                    }
                    catch (RuntimeBinderException e)
                    {
                        //throw;
                    }

                    catalog.AddEntry(entry);
                                                
                    // Now get product options as children
                    List<Option> productOptions = options.GetProductOptions(entry.ProductID);
                    foreach (Option option in productOptions)
                    {
                        try
                        {
                            Catalog.Entry childEntry = new Catalog.Entry();
                            
                            // Standard Fields
                            childEntry.ProductID = option.ChildID;
                            if (option.PriceType == "absolute")
                                childEntry.Price = option.Price;
                            else
                            {
                                Single ePrice = 0.0f;
                                Single oPrice = 0.0f;
                                if (!Single.TryParse(entry.Price, out ePrice))
                                    throw new FormatException("Number format not supported");
                                if (!Single.TryParse(option.Price, out oPrice))
                                    throw new FormatException("Number format not supported");
                                childEntry.Price = (ePrice + oPrice).ToString();
                            }
                            childEntry.ParentProductID = option.ProductID;
                        
                            // Extra Fields
                            foreach (string extraFieldName in catalog.extraFieldNames)
                                if (extraFieldName != "")
                                    entry.ExtraFields.Add("");

                            // Custom Fields
                            // walk the column names and for each determine whether the product at hand has one of these custom field, if so post it's value in the position corresponding to the index of name in customFieldNames
                            foreach (string customFileName in catalog.customFieldNames)
                                entry.CustomFields.Add("");
                        
                            catalog.AddEntry(childEntry);
                        }
                        catch (RuntimeBinderException e)
                        {
                            //throw;
                        }
                    }
                    break;
                case DataGroup.Sales:
                    //[
                    //  {
                    //    "id": 100,
                    //    "customer_id": 10,
                    //    "date_created": "Wed, 14 Nov 2012 19:26:23 +0000",
                    //    "date_modified": "Wed, 14 Nov 2012 19:26:23 +0000",
                    //    "date_shipped": "",
                    //    "status_id": 11,
                    //    "status": "Awaiting Fulfillment",
                    //    "subtotal_ex_tax": "79.0000",
                    //    "subtotal_inc_tax": "79.0000",
                    //    "subtotal_tax": "0.0000",
                    //    "base_shipping_cost": "0.0000",
                    //    "shipping_cost_ex_tax": "0.0000",
                    //    "shipping_cost_inc_tax": "0.0000",
                    //    "shipping_cost_tax": "0.0000",
                    //    "shipping_cost_tax_class_id": 2,
                    //    "base_handling_cost": "0.0000",
                    //    "handling_cost_ex_tax": "0.0000",
                    //    "handling_cost_inc_tax": "0.0000",
                    //    "handling_cost_tax": "0.0000",
                    //    "handling_cost_tax_class_id": 2,
                    //    "base_wrapping_cost": "0.0000",
                    //    "wrapping_cost_ex_tax": "0.0000",
                    //    "wrapping_cost_inc_tax": "0.0000",
                    //    "wrapping_cost_tax": "0.0000",
                    //    "wrapping_cost_tax_class_id": 3,
                    //    "total_ex_tax": "79.0000",
                    //    "total_inc_tax": "79.0000",
                    //    "total_tax": "0.0000",
                    //    "items_total": 1,
                    //    "items_shipped": 0,
                    //    "payment_method": "cash",
                    //    "payment_provider_id": null,
                    //    "payment_status": "",
                    //    "refunded_amount": "0.0000",
                    //    "order_is_digital": false,
                    //    "store_credit_amount": "0.0000",
                    //    "gift_certificate_amount": "0.0000",
                    //    "ip_address": "50.58.18.2",
                    //    "geoip_country": "",
                    //    "geoip_country_iso2": "",
                    //    "currency_id": 1,
                    //    "currency_code": "USD",
                    //    "currency_exchange_rate": "1.0000000000",
                    //    "default_currency_id": 1,
                    //    "default_currency_code": "USD",
                    //    "staff_notes": "",
                    //    "customer_message": "",
                    //    "discount_amount": "0.0000",
                    //    "coupon_discount": "0.0000",
                    //    "shipping_address_count": 1,
                    //    "is_deleted": false,
                    //    "billing_address": {
                    //      "first_name": "Trisha",
                    //      "last_name": "McLaughlin",
                    //      "company": "",
                    //      "street_1": "12345 W Anderson Ln",
                    //      "street_2": "",
                    //      "city": "Austin",
                    //      "state": "Texas",
                    //      "zip": "78757",
                    //      "country": "United States",
                    //      "country_iso2": "US",
                    //      "phone": "",
                    //      "email": "elsie@example.com"
                    //    },
                    //    "products": {
                    //      "url": "https://store-bwvr466.mybigcommerce.com/api/v2/orders/100/products.json",
                    //      "resource": "/orders/100/products"
                    //    },
                    //    "shipping_addresses": {
                    //      "url": "https://store-bwvr466.mybigcommerce.com/api/v2/orders/100/shippingaddresses.json",
                    //      "resource": "/orders/100/shippingaddresses"
                    //    },
                    //    "coupons": {
                    //      "url": "https://store-bwvr466.mybigcommerce.com/api/v2/orders/100/coupons.json",
                    //      "resource": "/orders/100/coupons"
                    //    }
                    //  }
                    //]
                
                    // get the order id first, then retrieve products for it, use e.g. https://store-bwvr466.mybigcommerce.com/api/v2/orders/100/products.json
                    
                    //{
                    //  "id": 16,
                    //  "order_id": 115,
                    //  "product_id": 0,
                    //  "order_address_id": 16,
                    //  "name": "Cynthia Gilbert Signature Collection",
                    //  "sku": "",
                    //  "type": "physical",
                    //  "base_price": "93.1800",
                    //  "price_ex_tax": "93.1800",
                    //  "price_inc_tax": "93.1800",
                    //  "price_tax": "0.0000",
                    //  "base_total": "93.1800",
                    //  "total_ex_tax": "93.1800",
                    //  "total_inc_tax": "93.1800",
                    //  "total_tax": "0.0000",
                    //  "weight": "0",
                    //  "quantity": 1,
                    //  "base_cost_price": "0.0000",
                    //  "cost_price_inc_tax": "0.0000",
                    //  "cost_price_ex_tax": "0.0000",
                    //  "cost_price_tax": "0.0000",
                    //  "is_refunded": false,
                    //  "refund_amount": "0.0000",
                    //  "return_id": 0,
                    //  "wrapping_name": "",
                    //  "base_wrapping_cost": "0.0000",
                    //  "wrapping_cost_ex_tax": "0.0000",
                    //  "wrapping_cost_inc_tax": "0.0000",
                    //  "wrapping_cost_tax": "0.0000",
                    //  "wrapping_message": "",
                    //  "quantity_shipped": 0,
                    //  "event_name": null,
                    //  "event_date": "",
                    //  "fixed_shipping_cost": "0.0000",
                    //  "ebay_item_id": "",
                    //  "ebay_transaction_id": "",
                    //  "option_set_id": null,
                    //  "parent_order_product_id": null,
                    //  "is_bundled_product ": false,
                    //  "bin_picking_number": "",
                    //  "applied_discounts": [
                    //    {
                    //      "id": "coupon",
                    //      "amount": 4.66
                    //    }
                    //  ],
                    //  "product_options": [
                    //  ],
                    //  "configurable_fields": [
                    //  ]
                    //}
                    //response = client.GetAsync("orders/" + orderId + "/products.json").Result;
                    //result = response.Content.ReadAsStringAsync().Result;                                       
					try
                    {
                        string orderId = entity.SelectToken(extractor.Rules.Fields.GetName(FieldName.OrderId)).ToString();                    
                        string customerId = entity.SelectToken(extractor.Rules.Fields.GetName(FieldName.OrderCustomerId)).ToString();
                        DateTime temp = Convert.ToDateTime(entity.SelectToken(extractor.Rules.Fields.GetName(FieldName.OrderDate)).ToString());
                        string _OrderProductIdName = extractor.Rules.Fields.GetName(FieldName.OrderProductId);
                        string _OrderQuantityName = extractor.Rules.Fields.GetName(FieldName.OrderQuantity);
                        
                        queryResult = extractor.GetQueryResponse(group, "orders/" + orderId + "/products.json");
                        JArray Products = JArray.Parse(queryResult);
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
                    catch (RuntimeBinderException e)
                    {
                        //throw;
                    }
                    break;
                case DataGroup.Customers:
                    //{
                    //  "id": 1,
                    //  "company": "",
                    //  "first_name": "Random ",
                    //  "last_name": "Joe Bob",
                    //  "email": "random.joebob@example.com",
                    //  "phone": "252-101-2010",
                    //  "date_created": "Tue, 13 Nov 2012 21:16:41 +0000",
                    //  "date_modified": "Tue, 13 Nov 2012 21:16:41 +0000",
                    //  "store_credit": "0.0000",
                    //  "registration_ip_address": "50.58.18.2",
                    //  "customer_group_id": 0,
                    //  "notes": "",
                    //  "tax_exempt_category": "",
                    //  "addresses": {
                    //    "url": "https://store-bwvr466.mybigcommerce.com/api/v2/customers/1/addresses.json",
                    //    "resource": "/customers/1/addresses"
                    //  }
                    //},                                      
                    Customer c = new Customer();
                    try
                    {
                        //  {
                        //  "id": 1,
                        //  "customer_id": 10,
                        //  "first_name": "Trisha",
                        //  "last_name": "McLaughlin",
                        //  "company": "",
                        //  "street_1": "12345 W Anderson Ln",
                        //  "street_2": "",
                        //  "city": "Austin",
                        //  "state": "Texas",
                        //  "zip": "78757",
                        //  "country": "United States",
                        //  "country_iso2": "US",
                        //  "phone": ""
                        //}
                        //response = client.GetAsync("customers/" + customerId + "/addresses.json").Result;
                        //result = response.Content.ReadAsStringAsync().Result;
                        c.CustomerID = entity.SelectToken(extractor.Rules.Fields.GetName(FieldName.CustomerId)).ToString();
                        c.Email = entity.SelectToken("email").ToString();
                        c.Name = entity.SelectToken("first_name").ToString() + " " + entity.SelectToken("last_name").ToString();
                        // retrieve addresses for this customer and use the first one
						queryResult = extractor.GetQueryResponse(group, "customers/" + c.CustomerID + "/addresses.json");
                        if (queryResult != "")
                        {
                            JArray Addresses = JArray.Parse(queryResult);
                            JObject firstAddress = (JObject)Addresses[0];

                            c.Address = firstAddress.SelectToken("street_1").ToString() + "\n" + firstAddress.SelectToken("street_2").ToString();
                            c.City = firstAddress.SelectToken("city").ToString();
                            c.State = firstAddress.SelectToken("state").ToString();
                            c.PostalCode = firstAddress.SelectToken("zip").ToString();
                            c.Country = firstAddress.SelectToken("country").ToString();
                            c.Phone = firstAddress.SelectToken("phone").ToString();
                        }
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
                    //{
                    //  "id": 1,
                    //  "product_id": 30,
                    //  "name": "Toy manufactured in",
                    //  "text": "USA"
                    //}
                    try
                    {
                        customfields.AddCustomField(entity.SelectToken("product_id").ToString(), entity.SelectToken("name").ToString(), entity.SelectToken("text").ToString());
                    }
                    catch (RuntimeBinderException e)
                    {
                        //throw;
                    }
                    break;
                case DataGroup.Options:
                    //    "url": "https://store-bwvr466.mybigcommerce.com/api/v2/products/rules.json",
                    //{
                    //      "id":120,
                    //      "product_id":61
                    //      "price_adjuster":{"adjuster":"absolute","adjuster_value":13.99},"
                    //},
                    try
                    {
                        if (entity.SelectToken("price_adjuster").ToString() != "")
                            options.AddOption(entity.SelectToken("id").ToString(), entity.SelectToken("product_id").ToString(), entity.SelectToken("price_adjuster.adjuster_value").ToString(), entity.SelectToken("price_adjuster.adjuster").ToString());
                    }
                    catch (RuntimeBinderException e)
                    {
                        //throw;
                    }
                    break;
				case DataGroup.CategoryNames:
                    // {
                    //  "id": 1,
                    //  "parent_id": 0,
                    //  "name": "Shop Mac",
                    //  "description": "",
                    //  "sort_order": 0,
                    //  "page_title": "",
                    //  "meta_keywords": "",
                    //  "meta_description": "",
                    //  "layout_file": "category.html",
                    //  "parent_category_list": [
                    //    1
                    //  ],
                    //  "image_file": "",
                    //  "is_visible": true,
                    //  "search_keywords": "",
                    //  "url": "/shop-mac/"
                    //}
                    try
                    {
                        categories.AddCategory(entity.SelectToken(extractor.Rules.Fields.GetName(FieldName.Att1NameId)).ToString(), entity.SelectToken(extractor.Rules.Fields.GetName(FieldName.Att1NameName)).ToString());
                    }
                    catch (RuntimeBinderException e)
                    {
                        //throw;
                    }
                    break;
                case DataGroup.ManufacturerNames:
                    //{
                    //  "id": 1,
                    //  "name": "Apple",
                    //  "page_title": "",
                    //  "meta_keywords": "",
                    //  "meta_description": "",
                    //  "image_file": "",
                    //  "search_keywords": ""
                    //},
                    try
                    {
                        brands.AddBrand(entity.SelectToken(extractor.Rules.Fields.GetName(FieldName.Att2NameId)).ToString(), entity.SelectToken(extractor.Rules.Fields.GetName(FieldName.Att2NameName)).ToString());
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
                    result = brands.GetBrands();
                    break;
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
    
        public void LogSalesOrder(string orderID)
        {   
            // Get the order
            queryResult = extractor.GetQueryResponse(DataGroup.Sales, string.Format("orders/{0}", orderID));
            if (queryResult == "")
                throw new Exception(string.Format("Order id {0} not found", orderID));
            JObject order = JObject.Parse(queryResult);
            InitializeGroupData(DataGroup.Sales, "");
            GetEntity(DataGroup.Sales, order);
            if (sales.sales.Count == 0)
                throw new Exception(string.Format("No products found for Order id {0}", orderID));
            foreach (Sale sale in sales.sales)
            {
                DateTime oDate = (sale.Date.Length < 1) ? Input.DateTimeConvert(DateTime.Now, extractor.Rules.SiteTimeZone) : DateTime.Parse(sale.Date);
                int oQuantity;
                if (!int.TryParse(sale.Quantity, out oQuantity) || oQuantity < 1) continue;
                //Log in AutoActions-YY-MM.txt
#if !USAGE_READONLY
				DataLogProxy.Instance.LogSingleAction(extractor.Alias, orderID, sale.ProductID, sale.CustomerID, oQuantity, oDate);
#endif
            }
        }
    }
}