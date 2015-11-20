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
            try
            {
                // Get the order
                queryResult = GetQueryResponse(DataGroup.Sales, string.Format("orders/{0}", orderID));
                if (queryResult == "")
                    throw new Exception(string.Format("Order id {0} not found", orderID));
                JObject order = JObject.Parse(queryResult);
                InitializeGroupData(DataGroup.Sales, "", "", "");
                GetEntity(DataGroup.Sales, order);
                if (sales.sales.Count == 0)
                    throw new Exception(string.Format("No products found for Order id {0}", orderID));
                foreach (Sale sale in sales.sales)
                {
                    DateTime oDate = (sale.Date.Length < 1) ? Input.DateTimeConvert(DateTime.Now, Rules.SiteTimeZone) : DateTime.Parse(sale.Date);
                    int oQuantity;
                    if (!int.TryParse(sale.Quantity, out oQuantity) || oQuantity < 1) continue;
                    //Log in AutoActions-YY-MM.txt
    #if !USAGE_READONLY
			    DataLogProxy.Instance.LogSingleAction(Alias, orderID, sale.ProductID, sale.CustomerID, oQuantity, oDate);
    #endif
                }
            }
            catch (Exception ex)
            {
                if (Log != null)
                    Log.WriteEntry(EventLogEntryType.Error, "Error logging Sales Order", ex, Alias);
                return;
            }
        }

        #endregion

        string queryResult;
        public int maxCallsPerSecond = 2; // Bigcommerce limits us to 2 calls into their API per second
        
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

        // platform agnostic collection save types
        public Catalog catalog = null;
        public Sales sales = null;
        public Customers customers = null;
        public Categories categories = null;
        public Brands brands = null;
		
        // platform specific collection save types
		public CustomFields customfields = null;
        public Options options = null;
        public OrderProducts orderproducts = null;
        public CustomerAddresses customeraddresses = null;

        private void FreeLocalStorage(bool final)
        {
            catalog = null;
            sales = null;
            customers = null;
            categories = null;
            brands = null;
            customfields = null;
            options = null;
            if (final == true)
            {
                // Keep these around in between calls to GetSales and GetCustomers
                orderproducts = null;
                customeraddresses = null;
            }
        }

        public void InitializeGroupData(DataGroup group, string extraFields, string startdate, string enddate)
        {
            FreeLocalStorage(false);

            switch (group)
            {
                case DataGroup.Catalog:
                    // Get customfields, categories and brands first to support catalog retrieval.
                    customfields = new CustomFields();
                    GetGroupData(DataGroup.Custom, "", "", "");
                    brands = new Brands(Rules);
                    GetGroupData(DataGroup.ManufacturerNames, "", "", "");
                    options = new Options();
                    GetGroupData(DataGroup.Options, "", "", "");
                    catalog = new Catalog(Rules, false);
                    catalog.SetHeader(extraFields.Split(',').ToList(), customfields.GetUniqueNames());
                    break;
                case DataGroup.Sales:
                    if (orderproducts == null)
                    {
                        orderproducts = new OrderProducts();
                        GetGroupData(DataGroup.OrderProducts, "", "", "");
                    }
                    sales = new Sales(Rules);
                    break;
                case DataGroup.Customers:
                    if (customeraddresses == null)
                    {
                        customeraddresses = new CustomerAddresses();
                        GetGroupData(DataGroup.CustomerAddresses, "", "", "");
                    }
                    customers = new Customers(Rules);
                    break;
                case DataGroup.CategoryNames:
                    categories = new Categories(Rules);
                    break;
                case DataGroup.ManufacturerNames:
                    brands = new Brands(Rules);
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

            // handle date range
            string dateRange = "";
            if (startdate != "" && enddate != "")
            {
                // add one day to enddate because BigCommerce date range is not inclusive
                DateTime endDateTime = Convert.ToDateTime(enddate);
                endDateTime = endDateTime.AddDays(1);
                enddate = endDateTime.Year + "-" + endDateTime.Month.ToString("00") + "-" + endDateTime.Day.ToString("00");
                dateRange = string.Format("{0}={1}&{2}={3}", minModSpecifier, startdate, maxModSpecifier, enddate);
                countUrl += "?" + dateRange;
            }

            // get entity and page counts
            int entityCount = 0;
            queryResult = GetQueryResponse(group, countUrl, maxCallsPerSecond);
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
            int pageSize = 250; // default page size, override in each case below as needed.
            string groupIdentifier = "";
            string minModSpecifier = "";
            string maxModSpecifier = "";
            switch (group)
            {
                case DataGroup.Catalog:
                    groupIdentifier = "products";
                    minModSpecifier = "min_date_created";
                    maxModSpecifier = "max_date_created";
                    break;
                case DataGroup.Sales:
                    groupIdentifier = "orders";
                    minModSpecifier = "min_date_created";
                    maxModSpecifier = "max_date_created";
                    break;
                case DataGroup.Customers:
                    groupIdentifier = "customers";
                    minModSpecifier = "min_date_created";
                    maxModSpecifier = "max_date_created";
                    break;
                case DataGroup.CategoryNames:
                    groupIdentifier = "categories";
                    break;
                case DataGroup.Inventory:
                    groupIdentifier = "products";
                    minModSpecifier = "min_date_modified";
                    maxModSpecifier = "max_date_modified";
                    break;
                case DataGroup.ManufacturerNames:
                    groupIdentifier = "brands";
                    break;
				case DataGroup.Custom:
                    groupIdentifier = "products/customfields";
                    break;
                case DataGroup.Options:
                    groupIdentifier = "products/rules";
                    break;
                case DataGroup.OrderProducts:
                    groupIdentifier = "orders/products";
                    break;
                case DataGroup.CustomerAddresses:
                    groupIdentifier = "customers/addresses";
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
                    queryResult = GetQueryResponse(group, taretGetString + "page=" + (i + 1).ToString(), maxCallsPerSecond);
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
                        JArray categoryIds = (JArray)entity.SelectToken(_Att1Name);
                        if (categoryIds != null)
                            foreach (JValue categoryId in categoryIds)
                                entry.AddCategory(categoryId.ToString());
                        string brand = "";
                        if (brands != null && brands.d.TryGetValue(entity.SelectToken(_Att2Name) == null ? "" : entity.SelectToken(_Att2Name).ToString(), out brand))
                            entry.ManufacturerID = brand;
                        entry.Visible = (int)entity.SelectToken(_VisName);
                        entry.Link = entity.SelectToken(_LinkName) == null ? "" : entity.SelectToken(_LinkName).ToString();
                        entry.Imagelink = entity.SelectToken(_ImagName) == null ? "" : entity.SelectToken(_ImagName).ToString();
                        entry.Price = entity.SelectToken(_PriceName) == null ? "" : entity.SelectToken(_PriceName).ToString();
                        entry.SalePrice = entity.SelectToken(_SaleName) == null ? "" : entity.SelectToken(_SaleName).ToString();
                        entry.ListPrice = entity.SelectToken(_ListName) == null ? "" : entity.SelectToken(_ListName).ToString();
                        entry.Cost = entity.SelectToken(_CostName) == null ? "" : entity.SelectToken(_CostName).ToString();
                        entry.Inventory = entity.SelectToken(_InvName) == null ? "" : entity.SelectToken(_InvName).ToString();
                        entry.Rating = entity.SelectToken(_RateName) == null ? "" : entity.SelectToken(_RateName).ToString();
                        entry.StandardCode = entity.SelectToken(_CodeName) == null ? "" : entity.SelectToken(_CodeName).ToString();

                        // Extra Fields
                        if (catalog.extraFieldNames != null)
                            foreach (string extraFieldName in catalog.extraFieldNames)
                                if (extraFieldName != "")
                                    entry.ExtraFields.Add(entity.SelectToken(extraFieldName) == null ? "" : entity.SelectToken(extraFieldName).ToString());

                        // Custom Fields
                        // walk the column names and for each determine whether the product at hand has one of these custom field, if so post it's value in the position corresponding to the index of name in customFieldNames
                        if (catalog.customFieldNames != null)
                        {
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
                    }
                    catch (RuntimeBinderException e)
                    {
                        //throw;
                    }

                    catalog.AddEntry(entry);

                    // Now get product options as children
                    if (options != null)
                    {
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
                                if (catalog.extraFieldNames != null)
                                    foreach (string extraFieldName in catalog.extraFieldNames)
                                        if (extraFieldName != "")
                                            entry.ExtraFields.Add("");

                                // Custom Fields
                                // walk the column names and for each determine whether the product at hand has one of these custom field, if so post it's value in the position corresponding to the index of name in customFieldNames
                                if (catalog.customFieldNames != null) 
                                    foreach (string customFileName in catalog.customFieldNames)
                                        entry.CustomFields.Add("");

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
                    try
                    {
                        string orderId = entity.SelectToken(Rules.Fields.GetName(FieldName.OrderId)).ToString();
                        string customerId = entity.SelectToken(Rules.Fields.GetName(FieldName.OrderCustomerId)).ToString();
                        if (customerId != "0" && customerId != "")
                        {
                            DateTime temp = Convert.ToDateTime(entity.SelectToken(Rules.Fields.GetName(FieldName.OrderDate)).ToString());

                            if (orderproducts != null)
                            {
                                List<OrderProduct> orderProducts = orderproducts.GetOrderProducts(orderId);
                                foreach (OrderProduct orderproduct in orderProducts)
                                {
                                    Sale s = new Sale();

                                    s.ProductID = orderproduct.ProductID;
                                    s.Quantity = orderproduct.ProductQuantity;
                                    s.OrderID = orderId;
                                    s.CustomerID = customerId;
                                    s.Date = temp.ToShortDateString();

                                    sales.AddSale(s);
                                }
                            }
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
                        c.CustomerID = entity.SelectToken(Rules.Fields.GetName(FieldName.CustomerId)).ToString();
                        c.Email = entity.SelectToken("email").ToString();
                        c.Name = entity.SelectToken("first_name").ToString() + " " + entity.SelectToken("last_name").ToString();
                        // retrieve address for this customer
                        CustomerAddress customerAddress = customeraddresses.GetCustomerAddress(c.CustomerID);
                        if (customerAddress != null)
                        {
                            c.Address = customerAddress.Address;
                            c.City = customerAddress.City;
                            c.State = customerAddress.State;
                            c.PostalCode = customerAddress.PostalCode;
                            c.Country = customerAddress.Country;
                            c.Phone = customerAddress.Phone;
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
                case DataGroup.OrderProducts:
                    //    "url": "https://store-bwvr466.mybigcommerce.com/api/v2/orders/products.json",
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
                    try
                    {
                        orderproducts.AddOrderProduct(entity.SelectToken("order_id").ToString(), entity.SelectToken(Rules.Fields.GetName(FieldName.OrderProductId)).ToString(), entity.SelectToken(Rules.Fields.GetName(FieldName.OrderQuantity)).ToString());
                    }
                    catch (RuntimeBinderException e)
                    {
                        //throw;
                    }
                    break;
                case DataGroup.CustomerAddresses:
                    //    "url": "https://store-bwvr466.mybigcommerce.com/api/v2/customers/addresses.json",
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
                    try
                    {
                        customeraddresses.AddCustomerAddress(entity.SelectToken("customer_id").ToString(),
                                                                entity.SelectToken("street_1").ToString() + "\n" + entity.SelectToken("street_2").ToString(),
                                                                entity.SelectToken("city").ToString(),
                                                                entity.SelectToken("state").ToString(),
                                                                entity.SelectToken("zip").ToString(),
                                                                entity.SelectToken("country").ToString(),
                                                                entity.SelectToken("phone").ToString());
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
                        categories.AddCategory(entity.SelectToken(Rules.Fields.GetName(FieldName.Att1NameId)).ToString(), entity.SelectToken(Rules.Fields.GetName(FieldName.Att1NameName)).ToString());
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
                        brands.AddBrand(entity.SelectToken(Rules.Fields.GetName(FieldName.Att2NameId)).ToString(), entity.SelectToken(Rules.Fields.GetName(FieldName.Att2NameName)).ToString());
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
    }
}