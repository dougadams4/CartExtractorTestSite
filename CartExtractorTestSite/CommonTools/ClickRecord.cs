using System;

namespace _4_Tell.CommonTools
{
	/// <summary>
	/// ClickRecord is a single click event to be recorded (click-stream data)
	/// </summary>
	public class ClickRecord
	{
		public string ProductId { get; set; }
		public string CustomerId { get; set; }
		public pageType PageType { get; set; }
		public DateTime Date { get; set; }

		public enum pageType
		{
			Home,
			PDP1,
			PDP2,
			Category,
			Search,
			Cart,
			Checkout,
			Bought,
			Admin,
			Other
		}

		public ClickRecord()
		{
			ProductId = "";
			CustomerId = "";
			PageType = pageType.Other;
			Date = DateTime.Now;
		}

		public ClickRecord(string productID, string customerID, string page, DateTime date)
		{
			ProductId = productID;
			CustomerId = customerID;
			PageType = ToPageTypeEnum(page);
			Date = date;
		}

		public ClickRecord(string productID, string customerID, pageType type, DateTime date)
		{
			ProductId = productID;
			CustomerId = customerID;
			PageType = type;
			Date = date;
		}

		public ClickRecord(string[] data)
		{
			if (data.Length !=4)
			{
				ProductId = "";
				CustomerId = "";
				PageType = pageType.Other;
				Date = DateTime.Now;
				return;
			}
			ProductId = data[0];
			CustomerId = data[1];
			PageType = ToPageTypeEnum(data[2]);
			DateTime d;
			Date = Input.TryGetDate(out d, data[3]) ? d : DateTime.Now;
		}

		static public string Header(string delimiter = "\t", string newLine = "\r\n")
		{
			return string.Format("ProductId{0}CustomerId{0}PageType{0}Date{1}",
				delimiter, newLine);
		}

		public string ToString(string delimiter = "\t", string newLine = "\r\n")
		{
			return string.Format("{2}{0}{3}{0}{4}{0}{5}{1}",
				delimiter, newLine, ProductId, CustomerId, ToPageTypeCode(PageType), Date.ToString("MM-dd-yyyy"));
		}

		public string Report(string delimiter)
		{
			var report = ProductId + delimiter + CustomerId + delimiter
			             + ToPageTypeCode(PageType) + delimiter + Date.ToString("MM-dd-yyyy");
			return report;
		}

		public static pageType ToPageTypeEnum(string code)
		{
			if (code == null) return pageType.Other;
			pageType type;
			if (code.Equals("Hm", StringComparison.OrdinalIgnoreCase))
				type = pageType.Home;
			else if (code.Equals("Pdp", StringComparison.OrdinalIgnoreCase))
				type = pageType.PDP1;
			else if (code.Equals("Pdp1", StringComparison.OrdinalIgnoreCase))
				type = pageType.PDP1;
			else if (code.Equals("Pdp2", StringComparison.OrdinalIgnoreCase))
				type = pageType.PDP2;
			else if (code.Equals("Cat", StringComparison.OrdinalIgnoreCase))
				type = pageType.Category;
			else if (code.Equals("Srch", StringComparison.OrdinalIgnoreCase))
				type = pageType.Search;
			else if (code.Equals("Cart", StringComparison.OrdinalIgnoreCase))
				type = pageType.Cart;
			else if (code.Equals("Chkout", StringComparison.OrdinalIgnoreCase))
				type = pageType.Checkout;
			else if (code.Equals("Bought", StringComparison.OrdinalIgnoreCase))
				type = pageType.Bought;
			else if (code.Equals("Admin", StringComparison.OrdinalIgnoreCase))
				type = pageType.Admin;
				//check alternate verbose names second
			else if (code.Equals("Home", StringComparison.OrdinalIgnoreCase))
				type = pageType.Home;
			else if (code.Equals("ProductDetail", StringComparison.OrdinalIgnoreCase))
				type = pageType.PDP1;
			else if (code.Equals("Category", StringComparison.OrdinalIgnoreCase))
				type = pageType.Category;
			else if (code.Equals("Search", StringComparison.OrdinalIgnoreCase))
				type = pageType.Search;
			else if (code.Equals("ViewCart", StringComparison.OrdinalIgnoreCase))
				type = pageType.Cart;
			else if (code.Equals("Checkout", StringComparison.OrdinalIgnoreCase))
				type = pageType.Checkout;
			else if (code.Equals("Invoice", StringComparison.OrdinalIgnoreCase))
				type = pageType.Bought;
			else
				type = pageType.Other;
			return type;
		}

		public static string ToPageTypeCode(pageType type)
		{
			switch (type)
			{
				case pageType.Home:
					return "Hm";
				case pageType.PDP1:
					return "Pdp1";
				case pageType.PDP2:
					return "Pdp2";
				case pageType.Category:
					return "Cat";
				case pageType.Search:
					return "Srch";
				case pageType.Cart:
					return "Cart";
				case pageType.Checkout:
					return "Chkout";
				case pageType.Bought:
					return "Bought";
				case pageType.Admin:
					return "Admin";
				default:
					return "Other";
			}
		}
	}
}