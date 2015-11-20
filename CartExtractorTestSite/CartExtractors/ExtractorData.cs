using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Remoting.Messaging;
using System.Web;
using _4_Tell.CommonTools;
using _4_Tell.CartExtractors;

namespace _4TellJsonDataObjects
{ 
    public class Customers
    {
        List<List<string>> output = new List<List<string>>();

        public Customers(SiteRules Rules)
        { 
            List<string> header = new List<string>();
            header.Add(Rules.Fields.GetName(FieldName.CustomerId));
            header.Add("Email");
            header.Add("Name");
            header.Add("Address");
            header.Add("City");
            header.Add("State");
            header.Add("PostalCode");
            header.Add("Country");
            header.Add("Phone");
            header.Add("Gender");
            header.Add("Birthday");
            header.Add("AgeRange");
            header.Add("AlternativeIDs");
            header.Add("DoNotTrack");
            output.Add(header);
        }

        public void AddCustomer(Customer c)
        {
            List<string> customer = new List<string>();
            customer.Add(c.CustomerID);
            customer.Add(c.Email);
            customer.Add(c.Name);
            customer.Add(c.Address);
            customer.Add(c.City);
            customer.Add(c.State);
            customer.Add(c.PostalCode);
            customer.Add(c.Country);
            customer.Add(c.Phone);
            customer.Add(c.Gender);
            customer.Add(c.Birthday);
            customer.Add(c.AgeRange);
            customer.Add(c.AlternativeIDs);
            customer.Add(c.DoNotTrack);
            output.Add(customer);
        }

        public List<List<string>> GetCustomers()
        {
            return output;
        }
    }

    public class Customer
    {
        public string CustomerID = "";
        public string Email = "";
        public string Name = "";
        public string Address = "";
        public string City = "";
        public string State = "";
        public string PostalCode = "";
        public string Country = "";
        public string Phone = "";
        public string Gender = "";
        public string Birthday = "";
        public string AgeRange = "";
        public string AlternativeIDs = "";
        public string DoNotTrack = "";
    }
        
    public class Catalog
    {
        List<List<string>> output = new List<List<string>>();
        public bool inventoryOnly = false;
        public List<string> extraFieldNames = null;
        public List<string> customFieldNames = null; 
        public SiteRules Rules;

        public Catalog(SiteRules Rules, bool inventoryOnly)
        {
            this.Rules = Rules;
            this.inventoryOnly = inventoryOnly;
        }
    
        // Set the output header (column names). 
        //
        // Note: Call this only after we know all extra fields (defined in SiteRules) or shop specific vendor fields (that are only dynamically discoverable, i.e. can't be put in SitRules.
        public void SetHeader(List<string> extraFieldNames, List<string> customFieldNames)
        {
            this.extraFieldNames = extraFieldNames;
            this.customFieldNames = customFieldNames;
            
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
            
            List<string> header = new List<string>();
            header.Add(_PIdName);
            if (inventoryOnly == false)
            {
                header.Add(_NameName);
                header.Add(_Att1Name);
                header.Add(_Att2Name);
                header.Add(_PriceName);
                header.Add(_SaleName);
                header.Add(_ListName);
                header.Add(_CostName);
            }
            header.Add(_InvName);
            if (inventoryOnly == false)
            {
                header.Add(_VisName);
                header.Add(_LinkName);
                header.Add(_ImagName);
                header.Add(_RateName);
                header.Add(_ParentIdName);
                header.Add(_CodeName);
                if (extraFieldNames != null) 
                    foreach (string fieldname in extraFieldNames)
                        if (fieldname != "")
                            header.Add(fieldname);
                if (customFieldNames != null)
                    foreach (string fieldname in customFieldNames)
                        header.Add(fieldname); 
            }
            output.Add(header);
        }
        
        public class Entry
        {
            public string ProductID = "";
            public string Name = "";
            public List<string> CategoryIDs = new List<string>();
            public string ManufacturerID = "";
            public string Price = "";
            public string SalePrice = "";
            public string ListPrice = "";
            public string Cost = "";
            public string Inventory = "";
            public int Visible = 0;
            public string Link = "";
            public string Imagelink = "";
            public string Rating = "";
            public string StandardCode = "";
            public string ParentProductID = "";
            public List<string> ExtraFields = new List<string>();
            public List<string> CustomFields = new List<string>(); 

            public void AddCategory(string id)
            {
                CategoryIDs.Add(id);
            }
        }
 
        public void AddEntry(Entry e)
        {
            List<string> entry = new List<string>();
            entry.Add(e.ProductID);
            if (inventoryOnly == false)
            {
                //entry.Add(@"""" + e.Name + @"""");
                entry.Add(e.Name);
                entry.Add(e.CategoryIDs.Count == 0 ? "" : e.CategoryIDs.Aggregate((w, z) => string.Format("{0},{1}", w, z)));
                entry.Add(e.ManufacturerID);
                entry.Add(e.Price);
                entry.Add(e.SalePrice);
                entry.Add(e.ListPrice);
                entry.Add(e.Cost);
            }
            entry.Add(e.Inventory);
            if (inventoryOnly == false)
            {
                entry.Add(e.Visible.ToString());
                entry.Add(e.Link);
                entry.Add(e.Imagelink);
                entry.Add(e.Rating);
                entry.Add(e.ParentProductID);
                entry.Add(e.StandardCode); 
                foreach (string field in e.ExtraFields)
                    entry.Add(field);
                foreach (string field in e.CustomFields)
                    entry.Add(field);
            }
            output.Add(entry);
        }

        public List<List<string>> GetCatalog()
        {
            return output;
        }       
    }

    public class Categories
    {
        public Dictionary<string, string> d;
        SiteRules Rules = null;

        public Categories(SiteRules Rules)
        {
            d = new Dictionary<string, string>();
            this.Rules = Rules;
        }

        public void AddCategory(string CategoryID, string CategoryName)
        {
            d.Add(CategoryID,CategoryName);
        }

        public List<List<string>> GetCategories()
        {
            List<List<string>> output = new List<List<string>>();

            List<string> header = new List<string>();
            header.Add(Rules.Fields.GetName(FieldName.Att1NameId));
            header.Add(Rules.Fields.GetName(FieldName.Att1NameName));
            output.Add(header);
            
            foreach (KeyValuePair<string,string> k in d)
            {
                List<string> category = new List<string>();
                category.Add(k.Key);
                category.Add(k.Value);
                output.Add(category);
            }
            
            return output;
        }
    }

    public class Brands
    {
        public Dictionary<string, string> d;
        SiteRules Rules = null;

        public Brands(SiteRules Rules)
        {
            d = new Dictionary<string, string>();
            this.Rules = Rules;
        }

        public void AddBrand(string BrandID, string BrandName)
        {
            d.Add(BrandID, BrandName);
        }

        public List<List<string>> GetBrands()
        {
            List<List<string>> output = new List<List<string>>();

            List<string> header = new List<string>();
            header.Add(Rules.Fields.GetName(FieldName.Att2NameId));
            header.Add(Rules.Fields.GetName(FieldName.Att2NameName));
            output.Add(header);

            foreach (KeyValuePair<string, string> k in d)
            {
                List<string> brand = new List<string>();
                brand.Add(k.Key);
                brand.Add(k.Value);
                output.Add(brand);
            }

            return output;
        }
    }

    public class CustomFields
    {
        public List<CustomField> fields = new List<CustomField>();
        
        public void AddCustomField(string product_id, string name, string value)
        {
            fields.Add(new CustomField(product_id, name, value));
        }
        
        public List<string> GetUniqueNames()
        {
            // Get and return the unique list of values in this dictionary
            List<string> customfieldnames = new List<string>();
            foreach (CustomField field in fields)
                if (!customfieldnames.Contains(field.Name))
                    customfieldnames.Add(field.Name);
            return customfieldnames;
        }
    }

    public class CustomField
    {
        public string ProductID = "";
        public string Name = "";
        public string Value = "";

        public CustomField(string productid, string name, string value)
        {
            ProductID = productid;
            Name = name;
            Value = value;
        }
    }

    public class Options
    {
        public List<Option> options = new List<Option>();

        public void AddOption(string child_id, string product_id, string price, string price_type)
        {
            options.Add(new Option(child_id, product_id, price, price_type));
        }

        public List<Option> GetProductOptions(string productid)
        {
            List<Option> ProductOptions = new List<Option>();
            foreach (Option option in options)
                if (option.ProductID == productid)
                    ProductOptions.Add(option);
            return ProductOptions;
        }
    }

    public class Option
    {
        public string ChildID = "";
        public string ProductID = "";
        public string Price = "";
        public string PriceType = "";

        public Option(string childid, string productid, string price, string pricetype)
        {
            ChildID = childid;
            ProductID = productid;
            Price = price;
            PriceType = pricetype;
        }
    }

    public class OrderProducts
    {
        public List<OrderProduct> orderProducts = new List<OrderProduct>();

        public void AddOrderProduct(string orderid, string productid, string quantity)
        {
            orderProducts.Add(new OrderProduct(orderid, productid, quantity));
        }

        public List<OrderProduct> GetOrderProducts(string orderid)
        {
            List<OrderProduct> OrderProducts = new List<OrderProduct>();
            foreach (OrderProduct orderproduct in orderProducts)
                if (orderproduct.OrderID == orderid)
                    OrderProducts.Add(orderproduct);
            return OrderProducts;
        }
    }

    public class OrderProduct
    {
        public string OrderID = "";
        public string ProductID = "";
        public string ProductQuantity = "";

        public OrderProduct(string orderid, string productid, string quantity)
        {
            OrderID = orderid;
            ProductID = productid;
            ProductQuantity = quantity;
        }
    }

    public class CustomerAddresses
    {
        public List<CustomerAddress> customerAddresses = new List<CustomerAddress>();

        public void AddCustomerAddress(string CustomerID, string Address, string City, string State, string PostalCode, string Country, string Phone)
        {
            customerAddresses.Add(new CustomerAddress(CustomerID, Address, City, State, PostalCode, Country, Phone));
        }

        public CustomerAddress GetCustomerAddress(string CustomerID)
        {
            foreach (CustomerAddress CustomerAddress in customerAddresses)
                if (CustomerAddress.CustomerID == CustomerID)
                    return CustomerAddress;
            return null;
        }
    }

    public class CustomerAddress
    {
        public string CustomerID = "";
        public string Address = "";
        public string City = "";
        public string State = "";
        public string PostalCode = "";
        public string Country = "";
        public string Phone = "";

        public CustomerAddress(string CustomerID, string Address, string City, string State, string PostalCode, string Country, string Phone)
        {
            this.CustomerID = CustomerID;
            this.Address = Address;
            this.City = City;
            this.State = State;
            this.PostalCode = PostalCode;
            this.Country = Country;
            this.Phone = Phone;
        }
    }

    public class Sales
    {
        List<List<string>> output = new List<List<string>>();
        public List<Sale> sales = new List<Sale>();

        public Sales(SiteRules Rules)
        {
            List<string> header = new List<string>();
            header.Add(Rules.Fields.GetName(FieldName.OrderId));
            header.Add(Rules.Fields.GetName(FieldName.OrderProductId));
            header.Add(Rules.Fields.GetName(FieldName.OrderCustomerId));
            header.Add(Rules.Fields.GetName(FieldName.OrderQuantity));
            header.Add(Rules.Fields.GetName(FieldName.OrderDate));
            output.Add(header);
        }
        
        public void AddSale(Sale s)
        {
            sales.Add(s);
            List<string> sale = new List<string>();
            sale.Add(s.OrderID);
            sale.Add(s.ProductID);
            sale.Add(s.CustomerID);
            sale.Add(s.Quantity);
            sale.Add(s.Date);
            output.Add(sale);
        }

        public List<List<string>> GetSales()
        {
            return output;
        }
    }
        
    public class Sale
    {
        public string OrderID;
        public string ProductID;
        public string CustomerID;
        public string Quantity;
        public string Date;
        private string output;
    }   
}