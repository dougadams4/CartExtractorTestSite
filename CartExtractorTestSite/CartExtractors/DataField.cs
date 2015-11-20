using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using _4_Tell.CommonTools;

namespace _4_Tell.CartExtractors
{
	#region Enums
	public enum FieldName
	{
		ProductGroupId,
		ProductId,
		Name,
		Att1Id,
		Att2Id,
		Price,
		SalePrice,
		ListPrice,
		Cost,
		PriceTop,
		SalePriceTop,
		ListPriceTop,
		CostTop,
		OnSale,
		Filter,
		Rating,
		StandardCode,
		Link,
		ImageLink,
		Visible,
		Inventory,
		UseInventory,
		ImageId,
		ParentId,
		Department,
		OrderGroupId,
		OrderId,
		OrderCustomerId,
		OrderCustomerName,
		OrderCustomerEmail,
		OrderCustomerPhone,
		OrderCustomerAddress,
		OrderCustomerCity,
		OrderCustomerState,
		OrderCustomerZip,
		OrderCustomerGender,
		OrderDate,
		OrderDetailsGroupId,
		OrderProductId,
		OrderQuantity,
		Att1NameGroupId,
		Att1NameId,
		Att1NameName,
		Att2NameGroupId,
		Att2NameId,
		Att2NameName,
		DepartmentNameGroupId,
		DepartmentNameId,
		DepartmentNameName,
		CustomerGroupId,
		CustomerId,
		CustomerPersona,
		CustomerName,
		CustomerEmail,
		CustomerAddress,
		CustomerState,
		CustomerZip,
		CustomerGender,
		InventoryGroupId,
		InventoryProductId,
		InventoryQuantity
	};

	public enum AltFieldGroup
	{
		ExtraStandardFields,
		ExtraQueryFields,
		AltPriceFields,
		AltPageFields,
		AltImageFields,
		AltTitleFields
	}
	#endregion


	public class CharMap : IEnumerable<KeyValuePair<string, string>>
	{
		private Dictionary<string, string> _charMap;
		//private Dictionary<string, string> _defaultCharMap;
		//private Dictionary<string, string> _overrideCharMap;

		public CharMap()
		{
			_charMap = new Dictionary<string, string>();
			//_overrideCharMap = null;
		}

		public IEnumerator<KeyValuePair<string, string>> GetEnumerator()
    {
        return _charMap.GetEnumerator();
    }

		IEnumerator IEnumerable.GetEnumerator()
		{
			return this.GetEnumerator();
		}

		public bool IsDefault
		{
			get; private set;
			//{
			//  if (_overrideCharMap == null) return true;
			//  if (!_overrideCharMap.Count.Equals(_charMap.Count)) return false;
			//  return _overrideCharMap.All(o => o.Value.Equals(_charMap[o.Key]));
			//}
		}

		public Dictionary<string, string>.KeyCollection Keys
		{
			get { return _charMap == null ? null : _charMap.Keys; }
		}

		public string this[string key]
		{
			get
			{
				string value;
				if (key != null && _charMap.TryGetValue(key, out value))
					return value;
				return "";
			}
			//set
			//{
			//  AddOrSet(key, value);
			//}
		}

		public void Set(Dictionary<string, string> map, bool setDefault)
		{
			if (map == null) return;
			if (_charMap.Equals(map)) return; //make sure they are different

			_charMap = new Dictionary<string, string>(map);
			IsDefault = setDefault;

			//if (!isDefault)
			//  _charMap = new Dictionary<string, string>(map);
			//else
			//  _overrideCharMap = new Dictionary<string, string>(map);
		}

		//public bool Any() //not supporting predicates at this time
		//{
		//  return _charMap.Count > 0;
		//}

		//public bool Set(string key, string value)
		//{
		//  string test;
		//  if (!_charMap.TryGetValue(key, out test))
		//    return false; //key does not exist
		//  _charMap[key] = value;
		//  return true;
		//}

		//public bool Add(string key, string value)
		//{
		//  string test;
		//  if (_charMap.TryGetValue(key, out test))
		//    return false; //key is already in the map
		//  _charMap.Add(key, value);
		//  return true;
		//}

		//public void AddOrSet(string key, string value)
		//{
		//  string test;
		//  if (_charMap.TryGetValue(key, out test))
		//    _charMap[key] = value; //key is already in the map
		//  _charMap.Add(key, value);
		//}

	}


	public class DataField
	{
		public string DefaultName { get; set; }
		private string _name;
		public string Name
		{
			get { return string.IsNullOrEmpty(_name) ? DefaultName : _name; }
		}

		public bool IsDefault
		{
			get { return string.IsNullOrEmpty(_name); }
		}
		public DataGroup Group { get; set; }
		public bool IsQueryable { get; set; }
		public int HeaderIndex { get; set; }

		public DataField(string name, DataGroup group, bool setDefault = false, bool isQueryable = true)
		{
			DefaultName = setDefault ? name : "";
			_name = setDefault ? "" : name;
			Group = group;
			IsQueryable = isQueryable;
		}

		public void Update(string name, bool setDefault = false)
		{
			if (setDefault) DefaultName = name;
			else if (!name.Equals(DefaultName)) _name = name;
		}

		public int SetIndex(List<string> header)
		{
			HeaderIndex = header.FindIndex(x => x.Equals(Name, StringComparison.OrdinalIgnoreCase));
			return HeaderIndex;
		}
	}

		
	public class DataFieldList
	{
		//TODO: See notes below under GetGroupFromField and SetDefaultField below
		public static int NumFieldNames
		{
			get { return (int)FieldName.InventoryQuantity + 1; }
		}

		public static int NumCatalogFields
		{
			get { return (int)FieldName.Department + 1; }
		}

		private readonly Dictionary<FieldName, DataField> _fields;
		private readonly Dictionary<AltFieldGroup, AltFieldList> _altFieldGroups;
		private readonly string _alias;
		public bool Att1Enabled { get; set; }
		public bool Att2Enabled { get; set; }
		public string Att1Name { get; set; }
		public string Att2Name { get; set; }
		public bool AltPriceExists { get; private set; }
		public CharMap FeedCharMap; //character pairs to map in feed results

		public DataFieldList(string @alias)
		{
			_alias = alias;
			_fields = new Dictionary<FieldName, DataField>();
			_altFieldGroups = new Dictionary<AltFieldGroup, AltFieldList>();
			Att1Enabled = true;
			Att1Name = SiteRules.DefaultAtt1Name;
			Att2Enabled = true;
			Att2Name = SiteRules.DefaultAtt2Name;
			AltPriceExists = false;
		}

		public void Add(FieldName index, string name, DataGroup group, bool setDefault = false, bool isQueryable = true)
		{
			if (string.IsNullOrEmpty(name)) return;
			DataField tempField;
			if (!_fields.TryGetValue(index, out tempField))
				_fields.Add(index, new DataField(name, @group, setDefault, isQueryable));
			else
				_fields[index].Update(name, setDefault);
		}

		public void Update(FieldName index, string name, bool setDefault = false)
		{
			if (string.IsNullOrEmpty(name)) return;
			DataField tempField;
			if (!_fields.TryGetValue(index, out tempField))
				throw new Exception(string.Format("Cannot update {0}. Field does not exist", index));

			_fields[index].Update(name, setDefault); 
		}


		public void InitializeFields(XElement settings, bool setDefault)
		{
			if (settings == null) return;

			//feed char map allows mapping characters in the feed to fix formatting errors
			var charMapXml = settings.Element("feedCharMap");
			if (charMapXml == null) charMapXml = settings.Element("apiFeedCharMap"); //depricated
			if (charMapXml != null)
			{
				var map = SiteRules.GetCharMapPairs(charMapXml, _alias);
				if (map != null)
				{
					if (FeedCharMap == null) FeedCharMap = new CharMap();
					FeedCharMap.Set(map, setDefault);
				}
			}

			//fieldname overrides (if missing, defaults will be set in each derived cart class)
			string field;
			var group = DataGroup.Catalog;
			field = Input.GetValue(settings, "productGroupIdField");
			if (!string.IsNullOrEmpty(field)) Add(FieldName.ProductGroupId, field, @group, setDefault, false);
			field = Input.GetValue(settings, "productIdField");
			if (!string.IsNullOrEmpty(field)) Add(FieldName.ProductId, field, @group, setDefault);
			field = Input.GetValue(settings, "nameField");
			if (!string.IsNullOrEmpty(field)) Add(FieldName.Name, field, @group, setDefault);
			field = null;
			Att1Enabled = true; //default to true
			var att = settings.Element("attribute1");
			if (att != null)
			{
				Att1Enabled = !Input.GetAttribute(att, "enabled").Equals("false"); //default is true
				field = Input.GetAttribute(att, "fieldName");
				if (!string.IsNullOrEmpty(field)) Add(FieldName.Att1Id, field, @group, setDefault);
				var name = Input.GetAttribute(att, "name");
				if (!string.IsNullOrEmpty(name)) Att1Name = name;
			}
			if (Att1Enabled && string.IsNullOrEmpty(field))
			{
				field = Input.GetValue(settings, "att1IdField");
				if (!string.IsNullOrEmpty(field)) Add(FieldName.Att1Id, field, @group, setDefault);
			}
			field = null;
			Att2Enabled = true; //default to true
			att = settings.Element("attribute2");
			if (att == null) att = settings.Element("secondAttribute"); //legacy depricated
			if (att != null)
			{
				Att2Enabled = !Input.GetAttribute(att, "enabled").Equals("false"); //default is true
				field = Input.GetAttribute(att, "fieldName");
				if (!string.IsNullOrEmpty(field)) Add(FieldName.Att2Id, field, @group, setDefault);
				var name = Input.GetAttribute(att, "name");
				if (!string.IsNullOrEmpty(name)) Att2Name = name;
			}
			if (Att2Enabled && string.IsNullOrEmpty(field))
			{
				field = Input.GetValue(settings, "att2IdField");
				if (!string.IsNullOrEmpty(field)) Add(FieldName.Att2Id, field, @group, setDefault);
			}
			field = Input.GetValue(settings, "priceField");
			if (!string.IsNullOrEmpty(field)) Add(FieldName.Price, field, @group, setDefault);
			field = Input.GetValue(settings, "salePriceField");
			if (!string.IsNullOrEmpty(field)) Add(FieldName.SalePrice, field, @group, setDefault);
			field = Input.GetValue(settings, "listPriceField");
			if (!string.IsNullOrEmpty(field)) Add(FieldName.ListPrice, field, @group, setDefault);
			field = Input.GetValue(settings, "costField");
			if (!string.IsNullOrEmpty(field)) Add(FieldName.Cost, field, @group, setDefault);
			field = Input.GetValue(settings, "priceTopField");
			if (!string.IsNullOrEmpty(field)) Add(FieldName.PriceTop, field, @group, setDefault);
			field = Input.GetValue(settings, "salePriceTopField");
			if (!string.IsNullOrEmpty(field)) Add(FieldName.SalePriceTop, field, @group, setDefault);
			field = Input.GetValue(settings, "listPriceTopField");
			if (!string.IsNullOrEmpty(field)) Add(FieldName.ListPriceTop, field, @group, setDefault);
			field = Input.GetValue(settings, "costTopField");
			if (!string.IsNullOrEmpty(field)) Add(FieldName.CostTop, field, @group, setDefault);
			field = Input.GetValue(settings, "onSaleField");
			if (!string.IsNullOrEmpty(field)) Add(FieldName.OnSale, field, @group, setDefault);
			field = Input.GetValue(settings, "filterField");
			if (!string.IsNullOrEmpty(field)) Add(FieldName.Filter, field, @group, setDefault);
			field = Input.GetValue(settings, "ratingField");
			if (!string.IsNullOrEmpty(field)) Add(FieldName.Rating, field, @group, setDefault);
			field = Input.GetValue(settings, "standardCodeField");
			if (!string.IsNullOrEmpty(field)) Add(FieldName.StandardCode, field, @group, setDefault);
			field = Input.GetValue(settings, "linkField");
			if (!string.IsNullOrEmpty(field)) Add(FieldName.Link, field, @group, setDefault);
			field = Input.GetValue(settings, "imageLinkField");
			if (!string.IsNullOrEmpty(field)) Add(FieldName.ImageLink, field, @group, setDefault);
			field = Input.GetValue(settings, "visibleField");
			if (!string.IsNullOrEmpty(field)) Add(FieldName.Visible, field, @group, setDefault);
			field = Input.GetValue(settings, "inventoryField");
			if (!string.IsNullOrEmpty(field)) Add(FieldName.Inventory, field, @group, setDefault);
			field = Input.GetValue(settings, "useInventoryField");
			if (!string.IsNullOrEmpty(field)) Add(FieldName.UseInventory, field, @group, setDefault);
			field = Input.GetValue(settings, "imageIdField");
			if (!string.IsNullOrEmpty(field)) Add(FieldName.ImageId, field, @group, setDefault);
			field = Input.GetValue(settings, "parentIdField");
			if (!string.IsNullOrEmpty(field)) Add(FieldName.ParentId, field, @group, setDefault);
			field = Input.GetValue(settings, "departmentField");
			if (!string.IsNullOrEmpty(field)) Add(FieldName.Department, field, @group, setDefault);

			group = DataGroup.Sales;
			field = Input.GetValue(settings, "orderGroupIdField");
			if (!string.IsNullOrEmpty(field)) Add(FieldName.OrderGroupId, field, @group, setDefault, false);
			field = Input.GetValue(settings, "orderIdField");
			if (!string.IsNullOrEmpty(field)) Add(FieldName.OrderId, field, @group, setDefault);
			field = Input.GetValue(settings, "orderCustomerIdField");
			if (!string.IsNullOrEmpty(field)) Add(FieldName.OrderCustomerId, field, @group, setDefault);
			field = Input.GetValue(settings, "orderCustomerNameField");
			if (!string.IsNullOrEmpty(field)) Add(FieldName.OrderCustomerName, field, @group, setDefault);
			field = Input.GetValue(settings, "orderCustomerEmailField");
			if (!string.IsNullOrEmpty(field)) Add(FieldName.OrderCustomerEmail, field, @group, setDefault);
			field = Input.GetValue(settings, "orderCustomerPhoneField");
			if (!string.IsNullOrEmpty(field)) Add(FieldName.OrderCustomerPhone, field, @group, setDefault);
			field = Input.GetValue(settings, "orderCustomerAddressField");
			if (!string.IsNullOrEmpty(field)) Add(FieldName.OrderCustomerAddress, field, @group, setDefault);
			field = Input.GetValue(settings, "orderCustomerCityField");
			if (!string.IsNullOrEmpty(field)) Add(FieldName.OrderCustomerCity, field, @group, setDefault);
			field = Input.GetValue(settings, "orderCustomerStateField");
			if (!string.IsNullOrEmpty(field)) Add(FieldName.OrderCustomerState, field, @group, setDefault);
			field = Input.GetValue(settings, "orderCustomerZipField");
			if (!string.IsNullOrEmpty(field)) Add(FieldName.OrderCustomerZip, field, @group, setDefault);
			field = Input.GetValue(settings, "orderCustomerGenderField");
			if (!string.IsNullOrEmpty(field)) Add(FieldName.OrderCustomerGender, field, @group, setDefault);
			field = Input.GetValue(settings, "orderDateField");
			if (!string.IsNullOrEmpty(field)) Add(FieldName.OrderDate, field, @group, setDefault);
			field = Input.GetValue(settings, "orderDetailsGroupIdField");
			if (!string.IsNullOrEmpty(field)) Add(FieldName.OrderDetailsGroupId, field, @group, setDefault, false);
			field = Input.GetValue(settings, "orderProductIdField");
			if (!string.IsNullOrEmpty(field)) Add(FieldName.OrderProductId, field, @group, setDefault);
			field = Input.GetValue(settings, "orderQuantityField");
			if (!string.IsNullOrEmpty(field)) Add(FieldName.OrderQuantity, field, @group, setDefault);

			group = DataGroup.CategoryNames;
			field = Input.GetValue(settings, "att1NameGroupIdField");
			if (!string.IsNullOrEmpty(field)) Add(FieldName.Att1NameGroupId, field, @group, setDefault, false);
			field = Input.GetValue(settings, "att1NameIdField");
			if (!string.IsNullOrEmpty(field)) Add(FieldName.Att1NameId, field, @group, setDefault);
			field = Input.GetValue(settings, "att1NameNameField");
			if (!string.IsNullOrEmpty(field)) Add(FieldName.Att1NameName, field, @group, setDefault);

			group = DataGroup.ManufacturerNames;
			field = Input.GetValue(settings, "att2NameGroupIdField");
			if (!string.IsNullOrEmpty(field)) Add(FieldName.Att2NameGroupId, field, @group, setDefault, false);
			field = Input.GetValue(settings, "att2NameIdField");
			if (!string.IsNullOrEmpty(field)) Add(FieldName.Att2NameId, field, @group, setDefault);
			field = Input.GetValue(settings, "att2NameNameField");
			if (!string.IsNullOrEmpty(field)) Add(FieldName.Att2NameName, field, @group, setDefault);

			group = DataGroup.DepartmentNames;
			field = Input.GetValue(settings, "departmentNameGroupIdField");
			if (!string.IsNullOrEmpty(field)) Add(FieldName.DepartmentNameGroupId, field, @group, setDefault, false);
			field = Input.GetValue(settings, "departmentNameIdField");
			if (!string.IsNullOrEmpty(field)) Add(FieldName.DepartmentNameId, field, @group, setDefault);
			field = Input.GetValue(settings, "departmentNameNameField");
			if (!string.IsNullOrEmpty(field)) Add(FieldName.DepartmentNameName, field, @group, setDefault);

			group = DataGroup.Customers;
			field = Input.GetValue(settings, "customerGroupIdField");
			if (!string.IsNullOrEmpty(field)) Add(FieldName.CustomerGroupId, field, @group, setDefault, false);
			field = Input.GetValue(settings, "customerIdField");
			if (!string.IsNullOrEmpty(field)) Add(FieldName.CustomerId, field, @group, setDefault);
			field = Input.GetValue(settings, "customerPersonaField");
			if (!string.IsNullOrEmpty(field)) Add(FieldName.CustomerPersona, field, @group, setDefault);

			group = DataGroup.Inventory;
			field = Input.GetValue(settings, "inventoryGroupIdField");
			if (!string.IsNullOrEmpty(field)) Add(FieldName.InventoryGroupId, field, @group, setDefault, false);
			field = Input.GetValue(settings, "inventoryProductIdField");
			if (!string.IsNullOrEmpty(field)) Add(FieldName.InventoryProductId, field, @group, setDefault);
			field = Input.GetValue(settings, "inventoryQuantityField");
			if (!string.IsNullOrEmpty(field)) Add(FieldName.InventoryQuantity, field, @group, setDefault);

			//Additions to the standard field list
			var fields = Input.GetValue(settings, "addStandardFields");
			if (!string.IsNullOrEmpty(fields))
				SetAltFields(AltFieldGroup.ExtraStandardFields, fields, setDefault);
			fields = Input.GetValue(settings, "addQueryFields");
			if (!string.IsNullOrEmpty(fields))
				SetAltFields(AltFieldGroup.ExtraQueryFields, fields, setDefault);

			////TODO: Magento Patch --add to MagentoCartRules.xml
			//else if (this.CartType.Equals(CartType.Magento))
			//  AddStandardFields = new List<string> { "ProductType", "Visibility", "Status", "StockAvailability" };

			//Alternate Price/Page/Image/Title Fields
			fields = Input.GetValue(settings, "alternatePriceFields");
			if (!string.IsNullOrEmpty(fields))
				SetAltFields(AltFieldGroup.AltPriceFields, fields, setDefault);
			fields = Input.GetValue(settings, "alternatePageFields");
			if (!string.IsNullOrEmpty(fields))
				SetAltFields(AltFieldGroup.AltPageFields, fields, setDefault);
			fields = Input.GetValue(settings, "alternateImageFields");
			if (!string.IsNullOrEmpty(fields))
				SetAltFields(AltFieldGroup.AltImageFields, fields, setDefault);
			fields = Input.GetValue(settings, "alternateTitleFields");
			if (!string.IsNullOrEmpty(fields))
				SetAltFields(AltFieldGroup.AltTitleFields, fields, setDefault);
		}

		public void SaveSettings(ref XElement settings)
		{
			if (FeedCharMap != null && !FeedCharMap.IsDefault)
			{
				var pairs = new XElement("feedCharMap");
				foreach (var pair in FeedCharMap)
				{
					char test;
					//quotes require special handling
					var from = pair.Key.Replace("\"", "0x22");
					var to = pair.Value.Replace("\"", "0x22");
					//single character codes need double conversion
					if (Input.TryConvert(from, out test))
						Input.TryConvert(test, out from);
					if (Input.TryConvert(to, out test))
						Input.TryConvert(test, out to);
					pairs.Add(new XElement("mapPair",
													new XAttribute("from", from),
													new XAttribute("to", to)));
				}
				settings.Add(pairs);
			}

			var fieldName = GetName(FieldName.Att1Id);
			if (fieldName != null && Att1Name != null
				&& (!Att1Enabled || !fieldName.Equals(SiteRules.DefaultAtt1Name)
					|| !Att1Name.Equals(SiteRules.DefaultAtt1Name))) //only include Att1 for non-standard use
			{
				settings.Add(new XElement("attribute1",
													 new XAttribute("enabled", Att1Enabled),
													 new XAttribute("name", Att1Name),
													 new XAttribute("fieldName", fieldName)));
			}
			fieldName = GetName(FieldName.Att2Id);
			if (fieldName != null && Att2Name != null
				&& (!Att2Enabled || !fieldName.Equals(SiteRules.DefaultAtt2Name)
					|| !Att2Name.Equals(SiteRules.DefaultAtt2Name))) //only include Att1 for non-standard use
			{
				settings.Add(new XElement("attribute2",
													 new XAttribute("enabled", Att2Enabled),
													 new XAttribute("name", Att2Name),
													 new XAttribute("fieldName", fieldName)));
			}

			//Need to only save fieldnames if current value does not equal the default
			foreach (var f in _fields.Where(f => !f.Value.IsDefault))
			{
				settings.Add(new XElement(GetRuleNameFromField(f.Key), f.Value.Name));
			}

			//Alternate Fields
			AltFieldList altGroup;
			if (_altFieldGroups.TryGetValue(AltFieldGroup.ExtraStandardFields, out altGroup) && !altGroup.IsDefault)
				settings.Add(new XElement("addStandardFields", altGroup.GetAggregate()));

			if (_altFieldGroups.TryGetValue(AltFieldGroup.ExtraQueryFields, out altGroup) && !altGroup.IsDefault)
				settings.Add(new XElement("addQueryFields", altGroup.GetAggregate()));

			if (_altFieldGroups.TryGetValue(AltFieldGroup.AltPriceFields, out altGroup) && !altGroup.IsDefault)
				settings.Add(new XElement("alternatePriceFields", altGroup.GetAggregate()));

			if (_altFieldGroups.TryGetValue(AltFieldGroup.AltPageFields, out altGroup) && !altGroup.IsDefault)
				settings.Add(new XElement("alternatePageFields", altGroup.GetAggregate()));

			if (_altFieldGroups.TryGetValue(AltFieldGroup.AltImageFields, out altGroup) && !altGroup.IsDefault)
				settings.Add(new XElement("alternateImageFields", altGroup.GetAggregate()));

			if (_altFieldGroups.TryGetValue(AltFieldGroup.AltTitleFields, out altGroup) && !altGroup.IsDefault)
				settings.Add(new XElement("alternateTitleFields", altGroup.GetAggregate()));
		}

		public void SetAltFields(AltFieldGroup altGroupName, string addFields, bool isDefault)
		{
			if (string.IsNullOrEmpty(addFields)) return;
			SetAltFields(altGroupName, addFields.Split(new[] {','}).ToList(), isDefault);
		}

		public void SetAltFields(AltFieldGroup altGroupName, List<string> addFields, bool isDefault)
		{
			if (addFields == null || addFields.Count < 1) return;
			AltFieldList altGroup;
			if (!_altFieldGroups.TryGetValue(altGroupName, out altGroup)) _altFieldGroups.Add(altGroupName, new AltFieldList());
			_altFieldGroups[altGroupName].Set(addFields, isDefault);

			if (altGroupName.Equals(AltFieldGroup.AltPriceFields)) AltPriceExists = true;
		}

		public void SetFieldHeaderIndices(DataGroup group, string header)
		{
			if (string.IsNullOrEmpty(header)) return;
			SetFieldHeaderIndices(group, header.Split(new[] { ',' }).ToList());
		}

		public void SetFieldHeaderIndices(DataGroup group, List<string> header)
		{
			foreach (var field in _fields.Where(x => x.Value.Group.Equals(group)))
			{
				field.Value.SetIndex(header);
			}
		}

		private string GetRuleNameFromField(FieldName name)
		{
			//NOTE: This requires all fieldname rules to conform to this naming convention (camel-case FieldName + "Field")
			var ruleName = name + "Field";
			var firstLetter = char.ToLower(ruleName[0]);
			ruleName = firstLetter + ruleName.Substring(1);
			return ruleName;
		}

		public int GetHeaderIndex(FieldName fieldName)
		{
			DataField tempField;
			if (!_fields.TryGetValue(fieldName, out tempField)) return -1;
			return tempField.HeaderIndex;
		}

		public string GetName(FieldName fieldName)
		{
			DataField tempField;
			if (!_fields.TryGetValue(fieldName, out tempField)) return "";
			return tempField.Name;
		}

		public List<string> GetActiveFields(DataGroup group)
		{
			//return (from field in _fieldNames where !string.IsNullOrEmpty(field.Value) select field.Value).ToList();
			var fields = _fields.Where(x => (x.Value.Group.Equals(group) && x.Value.IsQueryable)).Select(x => x.Value.Name);
			if (group.Equals(DataGroup.Catalog))
			{
				AltFieldList altGroup;
				if (_altFieldGroups.TryGetValue(AltFieldGroup.ExtraStandardFields, out altGroup))
					fields = fields.Union(altGroup.Get());
			}
			return fields.ToList();
		}

		public List<string> GetStandardFields(DataGroup group)
		{
			var fields = _fields.Where(x => (x.Value.Group.Equals(group) && x.Value.IsQueryable)).Select(x => x.Value.DefaultName);
			if (group.Equals(DataGroup.Catalog))
			{
				AltFieldList altGroup;
				if (_altFieldGroups.TryGetValue(AltFieldGroup.ExtraStandardFields, out altGroup))
					fields = fields.Union(altGroup.Get());
			}
			return fields.ToList();
		}

		public List<string> GetNonStandardFields(DataGroup group)
		{
			var fields =
				_fields.Where(x => (x.Value.Group.Equals(group) && x.Value.IsQueryable && !x.Value.IsDefault)).Select(x => x.Value.Name);
			if (group.Equals(DataGroup.Catalog))
			{
				AltFieldList altGroup;
				if (_altFieldGroups.TryGetValue(AltFieldGroup.ExtraStandardFields, out altGroup))
					fields = fields.Except(altGroup.Get());
			}
			return fields.ToList();
		}

		public List<string> GetAltRuleFields()
		{
			//skip extrafields and only get alts
			var fields = new List<string>();
			AltFieldList altGroup;
			if (_altFieldGroups.TryGetValue(AltFieldGroup.AltPriceFields, out altGroup))
				fields.AddRange(altGroup.Get());
			if (_altFieldGroups.TryGetValue(AltFieldGroup.AltPageFields, out altGroup))
				fields.AddRange(altGroup.Get());
			if (_altFieldGroups.TryGetValue(AltFieldGroup.AltImageFields, out altGroup))
				fields.AddRange(altGroup.Get());
			if (_altFieldGroups.TryGetValue(AltFieldGroup.AltTitleFields, out altGroup))
				fields.AddRange(altGroup.Get());
			return fields.Distinct().ToList();
		}

		public List<string> GetAllAltFields()
		{
			var fields = new List<string>();
			if (_altFieldGroups.Any())
			{
				foreach (var ag in _altFieldGroups.Where(x => x.Value != null))
					fields.AddRange(ag.Value.Get());
			}
			return fields;
		}

		public List<string> GetAltFields(AltFieldGroup groupName)
		{
			var fields = new List<string>();
			AltFieldList altGroup;
			if (_altFieldGroups.TryGetValue(groupName, out altGroup))
			{
				fields = altGroup.Get();
			}
			return fields;
		}
	}

	public class AltFieldList
	{
		private List<string> _default;
		private List<string> _override;

		public bool IsDefault
		{ 
			get { return _override == null || _override.Count < 1; }
		}

		public List<string> Get()
		{
			return IsDefault ? _default : _override;
		}

		public string GetAggregate()
		{
			var fields =  IsDefault ? _default : _override;
			return fields == null ? "" : fields.Aggregate((c, j) => string.Format("{0},{1}", c, j));
		}

		public void Set(List<string> fields, bool isDefault)
		{
			if (fields == null) return;
			if (isDefault)
				_default = new List<string>(fields.Distinct());
			else if (_default == null || !fields.Count.Equals(_default.Count) || fields.Any(x => !_default.Contains(x)))
				_override = new List<string>(fields.Distinct());
		}

		public void Add(List<string> fields, bool isDefault)
		{
			if (isDefault)
				_default = _default == null ? new List<string>(fields.Distinct()) : _default.Union(fields.Distinct()).ToList();
			else
				_override = _override == null ? new List<string>(fields.Distinct()) : _override.Union(fields.Distinct()).ToList();
		}
	}
}