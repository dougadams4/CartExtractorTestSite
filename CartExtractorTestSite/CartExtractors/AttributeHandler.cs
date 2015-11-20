using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Web;
using System.Xml.Linq;
using _4_Tell.CommonTools;
using _4_Tell.Logs;
using _4_Tell.IO;

namespace _4_Tell.CartExtractors
{
	public class AttributeHandler : DataHandlerBase
	{
		public AttributeHandler(SiteRules rules, CartExtractor cart, ExtractorProgress progress, DataGroup group)
			: base(rules, cart, progress, group)
		{
		}

		public override string WriteTable(out int itemCount)
		{
			itemCount = -1;
			string filename;
			int iId, iName;
			switch (_group)
			{
				case DataGroup.CategoryNames:
					if (!_rules.Fields.Att1Enabled || _rules.UseDepartmentsAsCategories)
						return "rule is turned off";
					filename = CartExtractor.Att1Filename;
					iId = _rules.Fields.GetHeaderIndex(FieldName.Att1NameId);
					iName = _rules.Fields.GetHeaderIndex(FieldName.Att1NameName);
					break;
				case DataGroup.ManufacturerNames:
					if (!_rules.Fields.Att2Enabled)
						return "rule is turned off";
					if (!_rules.ExtractAtt2Names)
						return "export is not required";
					filename = CartExtractor.Att2Filename;
					iId = _rules.Fields.GetHeaderIndex(FieldName.Att2NameId);
					iName = _rules.Fields.GetHeaderIndex(FieldName.Att2NameName);
					break;
				case DataGroup.DepartmentNames:
					if (!_rules.ExportDepartmentNames && !_rules.UseDepartmentsAsCategories)
						return "export is not required";
					filename = _rules.UseDepartmentsAsCategories ? CartExtractor.Att1Filename : CartExtractor.DepartmentFilename;
					iId = _rules.Fields.GetHeaderIndex(FieldName.DepartmentNameId);
					iName = _rules.Fields.GetHeaderIndex(FieldName.DepartmentNameName);
					break;
				default:
					throw new Exception("cannot write attribute table for " + _group.ToString());
			}

			var indexes = new[] { iId, iName };
			if (indexes.Min<int>() < 0)
				return string.Format("(bad header: {0})", Header.Aggregate((w, j) => string.Format("{0},{1}", w, j)));
			var minCols = indexes.Max<int>();
			var errMsg = new StringBuilder();
			var attributes = new List<AttributeRecord>();
			try
			{
				attributes.AddRange(from cols in Rows
													where cols.Count() > minCols
													select new AttributeRecord
													{
														Id = cols[iId],
														Name = _cart.CleanUpTitle(cols[iName].Replace(",", "")) //remove commas
													});
			}
			catch (Exception ex)
			{
				errMsg.Append(string.Format("Error: {0}\n", Input.GetExMessage(ex)));
			}
			finally
			{
				if (BoostLog.Instance != null && errMsg.Length > 0)
					BoostLog.Instance.WriteEntry(EventLogEntryType.Warning, errMsg.ToString(), "", _rules.Alias);
			}
			if (attributes.Count < 1)
				return "no data";

			itemCount = attributes.Count;
			_progress.UpdateTable(itemCount, -1, "Writing table");

			if (_group.Equals(DataGroup.DepartmentNames))
			{
				_cart.Departments.Clear(); 
				//Departments.AddRange(attributes.Select(x => new ??? (x.id, x.Name)));
				foreach (var entry in attributes)
				{
					try
					{
						_cart.Departments.Add(entry.Id, entry.Name);
					}
					catch (Exception ex) //duplicate Id?
					{
						if (BoostLog.Instance != null)
							BoostLog.Instance.WriteEntry(EventLogEntryType.Warning, 
								string.Format("Error adding Department ({0}: {1}", entry.Id, entry.Name),
								ex, _rules.Alias);
					}
				}
			}
			return TableAccess.Instance.WriteTable(_rules.Alias, filename, attributes);
		}
	}
}