using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Xml.Linq;
#if !CART_EXTRACTOR_TEST_SITE
using _4_Tell.DashService;
#endif
using _4_Tell.Logs;

//XElement

namespace _4_Tell.CommonTools
{
	/// <summary>
	/// Overall Set Logic:
	///		LogicSet has list of LogicNodes and a LogicEquation to combine them.
	///		LogicEquation has elements that are either a LogicOperand (not, and, or, xor) or the id for a LogicNode
	/// 
	///		To Evaluate the LogicEquation, a tree of LogicTreeNodes are temporarily created  
	///		Each LogicTreeNode contains two nullable LogicTreeNodes along with two values and the logic operand to combine them
	///		If the LogicTreeNodes are not null then they are used to calculate the values
	/// 
	///		LogicNodes evaluate to true or false (formerly called a condition)
	///		LogicNodes have one Comparator with two ValueNodes
	/// 
	///		ValueNodes evaluate to a string
	///		ValueNode can contain either a simple value or else and equation consisting of two ValueNodes and a ValueOperand
	///		Calculation of the value is done by converting the string values from each subnode to a type that depends on the operand, 
	///			and then performing the operation.
	///		The final value from the Equation is saved back as a string
	///		Converting to and from strings is slightly inefficient, but allows for huge flexibility.
	/// </summary>
	public class LogicSet
	{
		public string Alias { get; set; }
		public LogicEquation Logic { get; set; }
		public Dictionary<int, LogicNode> Nodes { get; private set; }
		private int _maxNodeId
		{
			get { return Nodes == null ? 0 : Nodes.Select(x => x.Key).Max(); }
		}
		public LogicSet(string alias)
		{
			Alias = alias;
			Logic = new LogicEquation(alias, 0);
			Nodes = new Dictionary<int, LogicNode>();
		}

		public LogicSet(string alias, XElement xml)
		{
			Alias = alias;
			Nodes = new Dictionary<int, LogicNode>();
			ParseXml(xml);
		}

		public void Add(int nodeId, LogicNode newNode)
		{
			LogicNode c;
			if (Nodes.TryGetValue(nodeId, out c))
				Nodes[nodeId] = newNode;
			else
				Nodes.Add(nodeId, newNode);
		}

		public void Remove(int nodeId)
		{
			if (!Nodes.Keys.Contains(nodeId)) return;
			Nodes.Remove(nodeId);
		}

		public List<string> GetFields()
		{
			var allFields = new List<string>();
			foreach (var node in Nodes)
			{
				var fields = node.Value.GetFields();
				if (fields.Any()) allFields = allFields.Union(fields).Distinct().ToList();
			}
			return allFields;
		}

		/// <summary>
		/// Apply all the rules in the condition set against the data provided
		/// overloaded version to use data lists instead of xml
		/// </summary>
		/// <param name="data"></param>
		/// <param name="matchingNames">output list of names for conditions that were true</param>
		/// <returns>true if the rules produce a true result</returns>
		public bool Evaluate(List<string> header, List<string> data, out List<string> matchingNames)
		{
			matchingNames = new List<string>();
			var results = new Dictionary<int, bool>();
			foreach (var c in Nodes)
			{
				var thisResult = c.Value.Evaluate(header, data);
				if (thisResult) matchingNames.Add(c.Value.Name);
				results.Add(c.Key, thisResult);
			}

			return Logic.Apply(results, _maxNodeId);
		}

		/// <summary>
		/// Apply all the rules in the condition set against the data provided
		/// </summary>
		/// <param name="data"></param>
		/// <param name="matchingNames">output list of names for conditions that were true</param>
		/// <returns>true if the rules produce a true result</returns>
		public bool Evaluate(XElement data, out List<string> matchingNames)
		{
			matchingNames = new List<string>();
			var results = new Dictionary<int, bool>();
			foreach (var c in Nodes)
			{
				var thisResult = c.Value.Evaluate(data);
				if (thisResult) matchingNames.Add(c.Value.Name);
				results.Add(c.Key, thisResult);
			}

			return Logic.Apply(results, _maxNodeId);
		}

		public void ParseXml(XElement data)
		{
			var logicNodes = data.Elements("logicNode").ToList();
			for (var i = 0; i < logicNodes.Count; i++)
			{
				try
				{
					int id;
					var idTest = Input.GetAttribute(logicNodes[i], "id");
					if (string.IsNullOrWhiteSpace(idTest)) id = i; //if no id is supplied use the index
					else 
					{
						idTest = idTest.ToUpper();
						if (idTest[0] >= 'A' && idTest[0] <= 'Z')  //preferred format is A, B, C, etc. to match equation
							id = idTest[0] - 'A';
						else if (!int.TryParse(idTest, out id)) //numerical ids are also accepted where 0 = A
						{
							if (BoostLog.Instance != null)
								BoostLog.Instance.WriteEntry(EventLogEntryType.Error,
									string.Format("Illegal LogicNode id {0}", idTest), "", Alias);
							id = i; //in case of error, use index
						}
					}
					var lNode = new LogicNode(Alias, logicNodes[i]);
					Add(id, lNode);
				}
				catch
				{
					continue;
				}
			}


			var equation = data.Element("logicEquation");
			Logic = new LogicEquation(Alias, equation, _maxNodeId);
		}

		public XElement GetXml(string setName)
		{
			var xml = new XElement(setName);
			for (var i = 0; i < Nodes.Count; i++)
			{
				if (Nodes[i] == null)
				{
					if (BoostLog.Instance != null)
						BoostLog.Instance.WriteEntry(EventLogEntryType.Error,
							string.Format("Missing LogicNode {0}", i), "", Alias);
					continue;
				}
				xml.Add(Nodes[i].GetXml(i));
			}
			var equation = Logic.GetXml();
			xml.Add(equation);
			return xml;
		}
	}

	/// <summary>
	/// Basic component of a ConditionSet to define exclusions and filters
	/// </summary>
	public class LogicNode
	{
		#region Enums & Support Classes
		public enum Comparator
		{
			Eq, //	=		(string or numerical)
			Ne, //	!=	(string or numerical)
			Lt, //	<		(numerical only)
			Gt, //	>		(numerical only)
			Le, //	<=	(numerical only)
			Ge, //	>=	(numerical only)
			Contains, //	contains (string only)
			StartsWith, //	startsWith (string only)
			EndsWith, //	endsWith (string only)
			IsOneOf,
			Copy // no comparison required just copy the value as a result
		}
		#endregion

		#region Members
		private readonly string _alias;
		private bool _validated;
		public bool Enabled { get; protected set; }
		public Comparator Op { get; set; }
		public ValueNodeBase[] Nodes;
		public string Name
		{
			get
			{
				if (Nodes == null || Nodes.Length != 2) return "Undefined";
				return string.Format("{0} {1} {2}", Nodes[0] == null ? "undefined" : Nodes[0].Name, Op, Nodes[1] == null ? "undefined" : Nodes[1].Name);
			}
		}
		#endregion

		#region Constructors
		public LogicNode(string alias, XElement xml)
		{
			_alias = alias;
			_validated = false;
			Enabled = true;
			Nodes = new ValueNodeBase[2];
			ParseXml(xml);
		}
		#endregion

		#region Basic Methods
		public List<string> GetFields()
		{
			var allFields = new List<string>();
			foreach (var node in Nodes)
			{
				if (node == null) continue;
				var fields = node.GetFields();
				if (fields.Any()) allFields = allFields.Union(fields).Distinct().ToList();
			}
			return allFields;
		}

		public static Comparator GetComparitor(string value)
		{
			Comparator c;
			if (!Enum.TryParse(value, true, out c))
				throw new Exception("Unknown comparator: " + value);
			return c;
		}

#if !CART_EXTRACTOR_TEST_SITE
        public static DashComparisonDef GetComparisonDef(Comparator c)
		{
			switch (c)
			{
				case Comparator.Eq:
					return new DashComparisonDef { Value = "eq", Name = "equals" };
				case Comparator.Ne:
					return new DashComparisonDef { Value = "ne", Name = "is not equal to" };
				case Comparator.Lt:
					return new DashComparisonDef { Value = "lt", Name = "is less than" };
				case Comparator.Gt:
					return new DashComparisonDef { Value = "gt", Name = "is greater than" };
				case Comparator.Le:
					return new DashComparisonDef { Value = "le", Name = "is less than or equal to" };
				case Comparator.Ge:
					return new DashComparisonDef { Value = "ge", Name = "is greater than or equal to" };
				case Comparator.Contains:
					return new DashComparisonDef { Value = "contains", Name = "contains" };
				case Comparator.StartsWith:
					return new DashComparisonDef { Value = "startsWith", Name = "starts with" };
				case Comparator.EndsWith:
					return new DashComparisonDef { Value = "endsWith", Name = "ends with" };
				case Comparator.IsOneOf:
					return new DashComparisonDef { Value = "isOneOf", Name = "is one of" };
				case Comparator.Copy:
					return new DashComparisonDef { Value = "Copy", Name = "Copy" };
				default:
					throw new Exception("Illegal comparator: " + c);
			}
		}

		public static DashComparisonDef[] GetComparisons()
		{
			var comparisons = new List<DashComparisonDef>
					{
						GetComparisonDef(Comparator.Eq),
						GetComparisonDef(Comparator.Ne),
						GetComparisonDef(Comparator.Lt),
						GetComparisonDef(Comparator.Gt),
						GetComparisonDef(Comparator.Le),
						GetComparisonDef(Comparator.Ge),
						GetComparisonDef(Comparator.Contains),
						GetComparisonDef(Comparator.StartsWith),
						GetComparisonDef(Comparator.EndsWith),
						GetComparisonDef(Comparator.IsOneOf),
						GetComparisonDef(Comparator.Copy)
					};

			return comparisons.ToArray();
		}

		public static string GetComparisonName(string value)
		{
			var c = GetComparitor(value);
			var d = GetComparisonDef(c);
			return d.Name;
		}
#endif

		public bool Equals(LogicNode testNode)
		{
			//only compare names if one is provided
			if (!string.IsNullOrEmpty(Name) && !string.IsNullOrEmpty(testNode.Name) && !Name.Equals(testNode.Name)) return false;
			if (!Op.Equals(testNode.Op)) return false;
			if (!Nodes[0].Equals(testNode.Nodes[0])) return false;
			return Nodes[1].Equals(testNode.Nodes[1]);
		}
		#endregion

		#region Logic

		/// <summary>
		/// Quick compare just for integers 
		/// </summary>
		/// <param name="a"></param>
		/// <param name="b"></param>
		/// <returns></returns>
		private bool Compare(int a, int b)
		{
			switch (Op)
			{
				case Comparator.Eq:
					return a.Equals(b);
				case Comparator.Ne:
					return !a.Equals(b);
				case Comparator.Lt:
					return a < b;
				case Comparator.Gt:
					return a > b;
				case Comparator.Le:
					return a <= b;
				case Comparator.Ge:
					return a >= b;
				default:
					if (BoostLog.Instance != null)
						BoostLog.Instance.WriteEntry(EventLogEntryType.Error,
							string.Format("Illegal comparison ({0})\nActual = {1}\nValue = {2}", Op, a, b),
							"", _alias);
					return false;
			}
		}

		/// <summary>
		/// Quick compare just for floats
		/// </summary>
		/// <param name="a"></param>
		/// <param name="b"></param>
		/// <returns></returns>
		private bool Compare(float a, float b)
		{
			switch (Op)
			{
				case Comparator.Eq:
					return a.Equals(b);
				case Comparator.Ne:
					return !a.Equals(b);
				case Comparator.Lt:
					return a < b;
				case Comparator.Gt:
					return a > b;
				case Comparator.Le:
					return a <= b;
				case Comparator.Ge:
					return a >= b;
				default:
					if (BoostLog.Instance != null)
						BoostLog.Instance.WriteEntry(EventLogEntryType.Error,
							string.Format("Illegal comparison ({0})\nActual = {1}\nValue = {2}", Op, a, b),
							"", _alias);
					return false;
			}
		}

		private bool Compare(DateTime a, DateTime b)
		{
			//compare as dates
			switch (Op)
			{
				case Comparator.Eq:
					return a.Equals(b);
				case Comparator.Ne:
					return !a.Equals(b);
				case Comparator.Lt:
					return a < b;
				case Comparator.Gt:
					return a > b;
				case Comparator.Le:
					return a <= b;
				case Comparator.Ge:
					return a >= b;
				default:
					if (BoostLog.Instance != null)
						BoostLog.Instance.WriteEntry(EventLogEntryType.Error,
						                             string.Format("Illegal date comparison ({0})\nVal1 = {1}\nVal2 = {2}", Op, a, b),
						                             "", _alias);
					return false;
			}
		}

		private bool Compare(TimeSpan a, TimeSpan b)
		{
			switch (Op)
			{
				case Comparator.Eq:
					return a.Equals(b);
				case Comparator.Ne:
					return !a.Equals(b);
				case Comparator.Lt:
					return a < b;
				case Comparator.Gt:
					return a > b;
				case Comparator.Le:
					return a <= b;
				case Comparator.Ge:
					return a >= b;
				default:
					if (BoostLog.Instance != null)
						BoostLog.Instance.WriteEntry(EventLogEntryType.Error,
						                             string.Format("Illegal TimeSpan comparison ({0})\nVal1 = {1}\nVal2 = {2}", Op, a, b),
						                             "", _alias);
					return false;
			}
		}

		private bool Compare(string a, string b)
		{
			//only compare as strings (and ignore case) --null values are equivalenbt to "null"
			a = a == null ? "null" : a.ToLower();
			b = b == null ? "null" : b.ToLower();

			switch (Op)
			{
				case Comparator.Eq:
					return a.Equals(b);
				case Comparator.Ne:
					return !a.Equals(b);
				case Comparator.Contains:
					return a.Contains(b);
				case Comparator.StartsWith:
					return a.StartsWith(b);
				case Comparator.EndsWith:
					return a.EndsWith(b);
				case Comparator.IsOneOf:
					var values = b.Split(',').ToList();
					return values.Any(x => x.Equals(a));
				default:
					if (BoostLog.Instance != null)
						BoostLog.Instance.WriteEntry(EventLogEntryType.Error,
																				 string.Format("Illegal comparison ({0})\nVal1 = {1}\nVal2 = {2}", Op, a, b),
																				 "", _alias);
					return false;
			}
		}

		/// <summary>
		/// Full compare for all data types
		/// </summary>
		/// <returns></returns>
		//private bool Compare(string actual)
		//{

		//  if (string.IsNullOrEmpty(actual))
		//  {
		//    //empty actual is valid for eq and ne
		//    if (Op.Equals(Comparator.Eq)) return string.IsNullOrEmpty(Value) || Value.ToLower().Equals("null");
		//    if (Op.Equals(Comparator.Ne)) return !string.IsNullOrEmpty(Value) && !Value.ToLower().Equals("null");
		//    return false;
		//  }
		//  //special case for Copy
		//  if (Op == Comparator.Copy)
		//  {
		//    Name = actual;
		//    return true; //always true, just copy the result
		//  }

		//  double a, b;
		//  if (double.TryParse(actual, out a) && double.TryParse(Value, out b))
		//  {
		//    //compare as numbers
		//    switch (Op)
		//    {
		//      case Comparator.Eq:
		//        return a.Equals(b);
		//      case Comparator.Ne:
		//        return !a.Equals(b);
		//      case Comparator.Lt:
		//        return a < b;
		//      case Comparator.Gt:
		//        return a > b;
		//      case Comparator.Le:
		//        return a <= b;
		//      case Comparator.Ge:
		//        return a >= b;
		//      case Comparator.Contains:
		//        return actual.Contains(Value);
		//      case Comparator.StartsWith:
		//        return actual.StartsWith(Value);
		//      case Comparator.EndsWith:
		//        return actual.EndsWith(Value);
		//      case Comparator.IsOneOf:
		//        var values = Value.Split(',').ToList();
		//        return values.Any(x => x.Equals(actual));
		//      default:
		//      if (BoostLog.Instance != null)
		//        BoostLog.Instance.WriteEntry(EventLogEntryType.Error, 
		//          string.Format("Illegal comparison ({0})\nActual = {1}\nValue = {2}", Op, actual, Value),
		//          "", _alias);
		//        return false;
		//    }
		//  }

		//  DateTime dateA, dateB;
		//  if (Value != null && DateTime.TryParse(actual, out dateA) && DateTime.TryParse(Value, out dateB))
		//  {
		//    //compare as dates
		//    switch (Op)
		//    {
		//      case Comparator.Eq:
		//        return dateA.Equals(dateB);
		//      case Comparator.Ne:
		//        return !dateA.Equals(dateB);
		//      case Comparator.Lt:
		//        return dateA < dateB;
		//      case Comparator.Gt:
		//        return dateA > dateB;
		//      case Comparator.Le:
		//        return dateA <= dateB;
		//      case Comparator.Ge:
		//        return dateA >= dateB;
		//      case Comparator.Contains:
		//        return actual.Contains(Value);
		//      case Comparator.StartsWith:
		//        return actual.StartsWith(Value);
		//      case Comparator.EndsWith:
		//        return actual.EndsWith(Value);
		//      case Comparator.IsOneOf:
		//        var values = Value.Split(',').ToList();
		//        return values.Any(x => x.Equals(actual));
		//      default:
		//      if (BoostLog.Instance != null)
		//        BoostLog.Instance.WriteEntry(EventLogEntryType.Error, 
		//          string.Format("Illegal comparison ({0})\nActual = {1}\nValue = {2}", Op, actual, Value),
		//          "", _alias);
		//        return false;
		//    }
		//  }

		//  //only compare as strings (and ignore case)
		//  if (Value == null) return false; //null actual handled above
		//  actual = actual.ToLower();
		//  var valToLower = Value.ToLower();

		//  switch (Op)
		//  {
		//    case Comparator.Eq:
		//      return actual.Equals(valToLower);
		//    case Comparator.Ne:
		//      return !actual.Equals(valToLower);
		//    case Comparator.Contains:
		//      return actual.Contains(valToLower);
		//    case Comparator.StartsWith:
		//      return actual.StartsWith(valToLower);
		//    case Comparator.EndsWith:
		//      return actual.EndsWith(valToLower);
		//    case Comparator.IsOneOf:
		//      var values = valToLower.Split(',').ToList();
		//      return values.Any(x => x.Equals(actual));
		//    case Comparator.Lt:
		//    case Comparator.Gt:
		//    case Comparator.Le:
		//    case Comparator.Ge:
		//    default:
		//      if (BoostLog.Instance != null)
		//        BoostLog.Instance.WriteEntry(EventLogEntryType.Error, 
		//          string.Format("Illegal comparison ({0})\nActual = {1}\nValue = {2}", Op, actual, Value),
		//          "", _alias);
		//      return false;
		//  }
		//}

		/// <summary>
		/// Apply this condition against the data provided
		/// overloaded version to use data lists instead of xml
		/// </summary>
		/// <param name="data"></param>
		/// <returns>true if the condition is met</returns>
		public bool Evaluate(List<string> header, List<string> data)
		{
			if (!Enabled) return false;
			if (Nodes[0] == null || Nodes[1] == null)
			{
				Enabled = false;
				return false;
			}

			//NOTE: no special handling for fields is needed
			//because evaluation will return result node type based on the value. 
			ValueNodeBase.ValueNodeType nodetype1, nodetype2;
			var val1 = Nodes[0].Evaluate(header, data, out nodetype1);
			var val2 = Nodes[1].Evaluate(header, data, out nodetype2);

			return FinishEvaluation(val1, nodetype1, val2, nodetype2);
		}

		/// <summary>
		/// Apply this condition against the data provided
		/// </summary>
		/// <param name="data"></param>
		/// <returns>true if the condition is met</returns>
		public bool Evaluate(XElement data)
		{
			if (!Enabled) return false;
			if (Nodes[0] == null || Nodes[1] == null)
			{
				Enabled = false;
				return false;
			}

			//NOTE: no special handling for fields is needed
			//because evaluation will return result node type based on the value. 
			ValueNodeBase.ValueNodeType nodetype1, nodetype2;
			var val1 = Nodes[0].Evaluate(data, out nodetype1);
			var val2 = Nodes[1].Evaluate(data, out nodetype2);

			return FinishEvaluation(val1, nodetype1, val2, nodetype2);
		}

		private bool FinishEvaluation(string val1, ValueNodeBase.ValueNodeType nodetype1, 
																	string val2, ValueNodeBase.ValueNodeType nodetype2)
		{
			do  //single-pass loop to allow breaking on error
			{
				//ints
				if (nodetype1 == ValueNodeBase.ValueNodeType.Int
				   && nodetype2 == ValueNodeBase.ValueNodeType.Int)
				{
					int a, b;
					if (!int.TryParse(val1, out a)) break;
					if (!int.TryParse(val2, out b)) break;
					return Compare(a, b);
				}

				//floats or in/float mix
				if ((nodetype1 == ValueNodeBase.ValueNodeType.Float
				    || nodetype1 == ValueNodeBase.ValueNodeType.Int)
				   && (nodetype2 == ValueNodeBase.ValueNodeType.Float
				    || nodetype2 == ValueNodeBase.ValueNodeType.Int))
				{
					float a, b;
					if (!float.TryParse(val1, out a)) break;
					if (!float.TryParse(val2, out b)) break;
					return Compare(a, b);
				}

				//dates
				if (nodetype1 == ValueNodeBase.ValueNodeType.Date
				   && nodetype2 == ValueNodeBase.ValueNodeType.Date)
				{
					DateTime a, b;
					if (!DateTime.TryParse(val1, out a)) break;
					if (!DateTime.TryParse(val2, out b)) break;
					return Compare(a, b);
				}

				//timespans
				if (nodetype1 == ValueNodeBase.ValueNodeType.DateSpan
				    && nodetype2 == ValueNodeBase.ValueNodeType.DateSpan)
				{
					TimeSpan a, b;
					ValueNodeBase.ValueUnits units1, units2;
					if (!Input.TryGetTimeSpan(val1, out a, out units1)) break;
					if (!Input.TryGetTimeSpan(val2, out b, out units2)) break;
					return Compare(a, b);
				}

				//mix of int/float/tiemspan
				if ((nodetype1 == ValueNodeBase.ValueNodeType.DateSpan
				     || nodetype1 == ValueNodeBase.ValueNodeType.Float
				     || nodetype1 == ValueNodeBase.ValueNodeType.Int)
				    && (nodetype2 == ValueNodeBase.ValueNodeType.DateSpan
				        || nodetype2 == ValueNodeBase.ValueNodeType.Float
				        || nodetype2 == ValueNodeBase.ValueNodeType.Int))
				{
					string span, val;
					float a, b;
					if (nodetype1 == ValueNodeBase.ValueNodeType.DateSpan)
					{
						span = val1;
						val = val2;
					}
					else
					{
						span = val2;
						val = val1;
					}
					var end = span.IndexOf(" ");
					if (end < 1) break;
					if (!float.TryParse(span.Substring(0, end), out a)) break;
					if (!float.TryParse(val, out b)) break;
					return Compare(a, b);
				}

				//special case for handling null dates (i.e. field node returned null)
				if (((nodetype1 == ValueNodeBase.ValueNodeType.DateSpan
				      || nodetype1 == ValueNodeBase.ValueNodeType.Date)
				     && val2 == null)
				    || ((nodetype2 == ValueNodeBase.ValueNodeType.DateSpan
				         || nodetype2 == ValueNodeBase.ValueNodeType.Date)
				        && val1 == null))
					return false;

			} while (false); 

			//if all else fails, compare as strings
			return Compare(val1, val2);
		}

		//public List<string> Evaluate(IEnumerable<XElement> items, string returnField)
		//{
		//  var results =
		//    items.Where(x => Compare(Input.GetValue(x, ResultField))).Select(x => Input.GetValue(x, returnField)).
		//          ToList();
		//  return results;
		//}

		//public List<string> Evaluate(IEnumerable<Dictionary<string, string>> items, string returnField)
		//{
		//  var matchingRecords =
		//    from row in items
		//    where Compare(row[ResultField])
		//    select row;

		//  return matchingRecords.Select(record => record[returnField]).ToList();
		//}
		#endregion

		#region Xml
		void ParseXml(XElement data)
		{
			try
			{
				var comparison = Input.GetAttribute(data, "comparison");
				Comparator c;
				if (!Enum.TryParse(comparison, true, out c))
					throw new Exception(string.Format("Illegal comparitor {0}", comparison));
				Op = c;

				var valNodes = data.Elements("valueNode").ToList();
				if (valNodes.Count != 2) 
					throw new Exception("each logicNode must have two ValueNodes.");

				for (var i = 0; i < 2; i++)
				{
					var typeName = Input.GetAttribute(valNodes[i], "type");
					ValueNodeBase.ValueNodeType vType;
					if (!Enum.TryParse(typeName, true, out vType))
						vType = ValueNodeBase.ValueNodeType.String;

					Nodes[i] = ValueNodeBase.CreateNewNode(vType, valNodes[i]);
				}
			}
			catch (Exception ex)
			{
				if (BoostLog.Instance != null)
					BoostLog.Instance.WriteEntry(EventLogEntryType.Error,
						string.Format("LogicNode {0} is disabled.", Name), ex, _alias);
				Enabled = false;
			}
		}

		public XElement GetXml(int id)
		{
			var idLetter = (char) ((int) 'A' + id);
			var xml = new XElement("logicNode",
														 new XAttribute("id", idLetter),
														 new XAttribute("comparison", Op.ToString())
														);
			for (var i = 0; i < 2; i++)
			{
				if (Nodes[i] == null)
				{
					if (BoostLog.Instance != null)
						BoostLog.Instance.WriteEntry(EventLogEntryType.Error,
							string.Format("Missing LogicNode for complex rule ({0})", Name), "", _alias);
					continue;
				}
				xml.Add(Nodes[i].GetXml());
			}
			return xml;
		}

		#endregion
	}

	/// <summary>
	///	LogicEquation has a list of LogicElements. 
	/// Each LogicElement contains a LogicElementType and a value. 
	/// LogivElement Types are either a LogicOperand (And, or, Xor, Not, OpenParen, Closeparen) or the id for a LogicNode. 
	/// </summary>
	public class LogicEquation
	{
		#region Enums & Support Classes

		public enum LogicOperand
		{
			And,
			Or,
			Xor,
			Not,
			OpenParen,
			CloseParen
		}

		public enum LogicElementType
		{
			Op,
			NodeId
		}

		public class LogicElement
		{
			public LogicElementType ElementType;
			public object Value; //value depends on LType. Op: Value = LogicOperator; NodeId: Value = int
		}

		#endregion

		#region Members
		public string Alias { get; set; }
		private bool _validated = false;
		private Dictionary<int, bool> _results;
		private List<LogicElement> _elements;
		public List<LogicElement> Elements
		{
			get //validate on get
			{
				if (MaxLogicNodeId < 1) return new List<LogicElement>();

				ValidateElements();
				return _elements;
			}
			set
			{
				_elements = value;
				_validated = false;
			}
		}
		public int MaxLogicNodeId { get; set; }
		#endregion

		#region Constructors
		public LogicEquation(string alias, int maxLogicNodeId)
		{
			Alias = alias;
			MaxLogicNodeId = maxLogicNodeId;
			ValidateElements(); //creates an initial list with all elements connected by OR
		}

		public LogicEquation(string alias, XElement xml, int maxLogicNodeId)
		{
			Alias = alias;
			MaxLogicNodeId = maxLogicNodeId;
			ParseXml(xml);
		}
		#endregion

		#region Logic
		/// <summary>
		/// Find the maximum referenced nodeId
		/// </summary>
		/// <returns></returns>
		private int FindNumLogicNodes()
		{
			if (_elements == null) return 0;

			var ids = _elements.Where(x => x.ElementType == LogicElementType.NodeId).Select(x => (int)x.Value);
			if (!ids.Any()) return 0;

			return ids.Max() + 1; //nodeIds are zero-based
		}

		/// <summary>
		/// Check to make sure that the logic elements are defined correctly
		/// </summary>
		/// <param name="numConditions"></param>
		public void ValidateElements()
		{
			//validation rules:
			//	possible elements are open/close paren, logic op, NOT, and nodeId
			//	logic-op's are AND, OR, and XOR
			//	all open-parens must be closed
			//	cannot have a close-paren without an open first
			//
			//	each item will setup requirements for the next:
			//  first element: open-paren, NOT, nodeId
			//	after nodeId: close-paren or a logic-op
			//  after logic-op: open-paren, NOT, nodeId
			//	after open-paren: open-paren, NOT, nodeId
			//	after close paren: logic-ops, close-paren
			//  after NOT: open-paren, nodeId

			if (_elements == null) _elements = new List<LogicElement>();
			if (MaxLogicNodeId < 0) return;

			var nodeReferenced = new bool[MaxLogicNodeId + 1];
			if (_elements.Count > 0) //we have some elements to validate
			{
				try
				{
					var removeList = new List<int>(); //list of indices to remove (illegal close parens, etc.)
					var openCount = 0; //running total of opens that have not closed
					var logicOps = new List<LogicOperand> { LogicOperand.And, LogicOperand.Or, LogicOperand.Xor };
					var nextValidId = true;
					var nextValidOps = new List<LogicOperand> { LogicOperand.OpenParen, LogicOperand.Not }; //first element

					for (var i = 0; i < _elements.Count; i++)
					{
						var e = _elements[i];
						if (e.ElementType == LogicElementType.Op && !nextValidOps.Contains((LogicOperand)e.Value)
								|| (!nextValidId && e.ElementType == LogicElementType.NodeId))
						{
							removeList.Add(i); //illegal element found
							continue;
						}
						switch (e.ElementType)
						{
							case LogicElementType.NodeId: //rule id
								var nodeId = (int)e.Value; //zero index of condition in the total list
								nextValidId = false;
								nextValidOps = logicOps.Union(new[] { LogicOperand.CloseParen }).ToList();
								if (nodeId > MaxLogicNodeId) continue; //extra referecnes are ignored (more conditions could still be added)
								nodeReferenced[nodeId] = true;
								break;
							case LogicElementType.Op:
								var lo = (LogicOperand)e.Value;
								if (logicOps.Contains(lo)) //logic op
								{
									nextValidId = true;
									nextValidOps = new List<LogicOperand> { LogicOperand.OpenParen, LogicOperand.Not };
									break;
								}
								switch (lo)
								{
									case LogicOperand.OpenParen: //open paren
										openCount++;
										nextValidId = true;
										nextValidOps = new List<LogicOperand> { LogicOperand.OpenParen, LogicOperand.Not };
										break;
									case LogicOperand.CloseParen: //close paren
										if (openCount < 1) removeList.Add(i); //illegal close paren
										else openCount--;
										nextValidId = false;
										nextValidOps = logicOps.Union(new[] { LogicOperand.CloseParen }).ToList();
										break;
									case LogicOperand.Not: //not
										nextValidId = true;
										nextValidOps = new List<LogicOperand> { LogicOperand.OpenParen };
										break;
								}
								break;
						}
					}
					//now clean-up all the parens 					
					for (var j = removeList.Count - 1; j >= 0; j--) //must go backward through the list so indices are not changed
						_elements.RemoveAt(removeList[j]);
					while (openCount-- > 0)
						_elements.Add(new LogicElement { ElementType = LogicElementType.Op, Value = LogicOperand.CloseParen });
				}
				catch (Exception ex)
				{
					if (BoostLog.Instance != null)
						BoostLog.Instance.WriteEntry(EventLogEntryType.Error, "Error validating ConditionLogic", ex, Alias);
					_elements = new List<LogicElement>();
					nodeReferenced = new bool[MaxLogicNodeId];
				}
				finally
				{
					_validated = true;
				}
			}

			//now add any unreferenced rules to the end of the list with OR's
			for (var i = 0; i <= MaxLogicNodeId; i++)
			{
				if (!nodeReferenced[i]) //found an unreferenced condition so add it to the end
				{
					if (i > 0) //first element does not need an OR
						_elements.Add(new LogicElement { ElementType = LogicElementType.Op, Value = LogicOperand.Or });
					_elements.Add(new LogicElement { ElementType = LogicElementType.NodeId, Value = i });
				}
			}
		}

		/// <summary>
		/// Step through the logic tree and evaluate the results
		/// </summary>
		/// <param name="results"></param>
		/// <returns></returns>
		public bool Apply(Dictionary<int, bool> results, int maxLogicNodeId)
		{
			MaxLogicNodeId = maxLogicNodeId;
			_results = results;
			//make sure the logic elements are validated
			if (!_validated)
			{
				//var numLogicNodes = FindNumLogicNodes();
				ValidateElements();
			}

			//walk through the logic elements and build a node tree to evaluate
			var tree = BuildTree(0, _elements.Count - 1);
			return tree.Evaluate();
		}

		/// <summary>
		/// Recursive function to build the tree of logic nodes
		/// Once the tree is built, calling Evaluate on the root node will evaluate the whole tree
		/// </summary>
		/// <param name="startIndex">the starting index should always point to either an open paren, a nodeId. or a NOT</param>
		/// <param name="endIndex">endIndex will be adjusted to recurse through separate secetions of the tree</param>
		/// <returns></returns>
		private LogicTreeNode BuildTree(int startIndex, int endIndex)
		{
			var thisNode = new LogicTreeNode();
			//nodePos tracks which part of the node is currently being processed (1 = val1, 2 = op, 3 = val2, 4 = complete)
			//note: nodePos is not incrementd in all cases. It depends on the location and type of element found
			var nodePos = 1;
			try
			{
				for (var i = startIndex; i <= endIndex; i++)
				{
					var e = _elements[i];
					switch (e.ElementType)
					{
						case LogicElementType.NodeId:
							if (nodePos == 2)
								throw new Exception(string.Format("Invalid Logic at index {0} --no op found between val1 and val2", i));
							bool val;
							if (!_results.TryGetValue((int) e.Value, out val)) val = false;
							if (nodePos == 1)
							{
								thisNode.Val1 = val;
								nodePos++;
								break;
							}
							if (i == endIndex) //last element
								thisNode.Val2 = val;
							else
								thisNode.Node2 = BuildTree(i, endIndex); //more elements to examine
							return thisNode;
						case LogicElementType.Op:
							var op = (LogicOperand) e.Value;
							switch (op)
							{
								case LogicOperand.OpenParen:
									if (nodePos == 2)
										throw new Exception(string.Format("Invalid Logic at index {0} --no op found between val1 and val2", i));
									if (nodePos == 3)
									{
										thisNode.Node2 = BuildTree(i, endIndex); //handle it at the beginning of the next node
										return thisNode;
									}
									var newStart = i + 1; //drop opening paren
									var close = _elements.FindIndex(newStart,
									                                x =>
									                                x.ElementType == LogicElementType.Op &&
									                                (LogicOperand) x.Value == LogicOperand.CloseParen);
									if (close > endIndex)
										throw new Exception(string.Format("Invalid Logic at index {0} --matching close paren is beyond index range", i));
									var newEnd = close - 1; //drop closing paren
									thisNode.Node1 = BuildTree(newStart, newEnd); //evaluate the contents between the parens
									nodePos++;
									break;
								case LogicOperand.CloseParen:
									throw new Exception(string.Format(
										"Invalid Logic at index {0} --should not encounter close paren without open", i));
								case LogicOperand.Not:
									if (nodePos == 2)
										throw new Exception(string.Format("Invalid Logic at index {0} --no op found between val1 and NOT", i));
									if (nodePos == 1) thisNode.NotVal1 = true;
									else if (i == endIndex - 1) thisNode.NotVal2 = true;
									else
									{
										thisNode.Node2 = BuildTree(i, endIndex); //handle it at the beginning of the next node
										return thisNode;
									}
									break;
								default:
									if (nodePos != 2)
										throw new Exception(string.Format("Invalid Logic at index {0} --op found in wrong location", i));
									thisNode.Op = op;
									nodePos++;
									break;
							}
							break;
						default:
							throw new ArgumentOutOfRangeException();
					}
				}
			}
			catch (Exception ex)
			{
				if (BoostLog.Instance != null)
					BoostLog.Instance.WriteEntry(EventLogEntryType.Error,
																			 "Error building logic tree.", ex);
			}
			//fill defaults if not enough emelemnts 
			if (nodePos == 1) return null; //no elements
			if (nodePos == 2) thisNode.Op = LogicOperand.Or;
			if (nodePos < 4) thisNode.Val2 = false;
			return thisNode;
		}
		#endregion

		#region Xml
		public void ParseXml(XElement xml)
		{
			if (_elements == null) _elements = new List<LogicElement>();
			if (xml != null)
			{
				var elements = xml.Elements("logicElement");
				if (elements.Any())
				{
					foreach (var e in elements)
					{
						var et = Input.GetAttribute(e, "type");
						LogicElementType eType;
						if (!Enum.TryParse(et, true, out eType)) continue; //type is required
						var val = Input.GetAttribute(e, "value");
						if (string.IsNullOrEmpty(val)) continue; //value is required

						object oVal;
						if (eType == LogicElementType.Op)
						{
							LogicOperand op;
							if (!Enum.TryParse(val, true, out op)) continue; //invalid op
							oVal = (object) op;
						}
						else
						{
							int nodeId;
							if (!TryGetNodeId(val, out nodeId)) continue; //invalid nodeId
							oVal = (object) nodeId;
						}
						_elements.Add(new LogicElement {ElementType = eType, Value = oVal});
					}
				}
				else
				{
					var equation = Input.GetAttribute(xml, "value");
					if (!string.IsNullOrWhiteSpace(equation))
					{
						equation = equation.ToLower();
						equation = equation.Replace("and", ".");
						equation = equation.Replace("&amp;", ".");
						equation = equation.Replace("xor", "x");
						equation = equation.Replace("or", "+");
						equation = equation.Replace("|", "+");
						equation = equation.Replace(" ", "");
						LogicElementType eType;
						LogicOperand op;
						int nodeId;
						object oVal;
						foreach (var c in equation)
						{
							switch (c)
							{
								case '(':
									eType = LogicElementType.Op;
									op = LogicOperand.OpenParen;
									oVal = (object) op;
									break;
								case ')':
									eType = LogicElementType.Op;
									op = LogicOperand.CloseParen;
									oVal = (object)op;
									break;
								case '.': //and
									eType = LogicElementType.Op;
									op = LogicOperand.And;
									oVal = (object)op;
									break;
								case '+': //or
									eType = LogicElementType.Op;
									op = LogicOperand.Or;
									oVal = (object)op;
									break;
								case 'x': //xor
									eType = LogicElementType.Op;
									op = LogicOperand.Xor;
									oVal = (object)op;
									break;
								case '-': //not
									eType = LogicElementType.Op;
									op = LogicOperand.Not;
									oVal = (object)op;
									break;
								default: //node id
									if (c < 'a' || c > 'z')
									{
										if (BoostLog.Instance != null)
											BoostLog.Instance.WriteEntry(EventLogEntryType.Error, string.Format("Illegal equation character {0}", c));
										continue;
									}
									eType = LogicElementType.NodeId;
									nodeId = (int) c - (int)'a';
									oVal = (object) nodeId;
									break;
							}
							_elements.Add(new LogicElement { ElementType = eType, Value = oVal });
						}
					}
				}
			}
			ValidateElements();
		}

		public XElement GetXml()
		{
			string value = "";

			foreach (var e in _elements)
			{
				switch (e.ElementType)
				{
					case LogicElementType.NodeId:
						value += (char) ('A' + (int) e.Value);
						break;
					case LogicElementType.Op:
						switch ((LogicOperand) e.Value)
						{
							case LogicOperand.And:
								value += " and ";
								break;
							case LogicOperand.Or:
								value += " or ";
								break;
							case LogicOperand.Xor:
								value += " xor ";
								break;
							case LogicOperand.Not:
								value += "-";
								break;
							case LogicOperand.OpenParen:
								value += "(";
								break;
							case LogicOperand.CloseParen:
								value += ")";
								break;
							default:
								throw new ArgumentOutOfRangeException();
						}
						break;
					default:
						throw new ArgumentOutOfRangeException();
				}
			}
			var xml = new XElement("logicEquation",
														new XAttribute("value", value));


			//foreach (var e in _elements)
			//{
			//  string val;
			//  if (e.ElementType == LogicElementType.Op)
			//    val = ((LogicOperand)e.Value).ToString();
			//  else
			//    val = GetAlphaId((int)e.Value);
			//  var le = new XElement("logicElement",
			//                        new XAttribute("type", e.ElementType.ToString()),
			//                        new XAttribute("value", val)
			//    );
			//  xml.Add(le);
			//}
			return xml;
		}

		private string GetAlphaId(int nodeId)
		{
			var id = (char) ((int) 'A' + nodeId);
			return id.ToString();
		}

		private bool TryGetNodeId(string alphaId, out int nodeId)
		{
			nodeId = 0;
			if (string.IsNullOrWhiteSpace(alphaId)) return false;
			var id = alphaId[0];
			if (id < 'A' || id > 'Z') return false;
			nodeId = id - 'A';
			return true;
		}

		#endregion
	}

	//public class EquationNode
	//{
	//  public enum LogicOperand
	//  {
			
	//  }

	//  public ValueNodeBase.ValueOperand Op;
	//  public EquationNode Node1 = null;
	//  public EquationNode Node2 = null;
	//  public float Val1;
	//  public float Val2;

	//  public bool Evaluate()
	//  {
	//    if (Node1 != null) Val1 = Node1.Evaluate();
	//    if (Node2 != null) Val2 = Node2.Evaluate();

	//    switch (Op)
	//    {
	//      case LogicEquation.LogicOperand.And:
	//        return Val1 && Val2;
	//      case LogicEquation.LogicOperand.Or:
	//        return Val1 || Val2;
	//      case LogicEquation.LogicOperand.Xor:
	//        return (Val1 && !Val2) || (!Val1 && Val2);
	//      default: //additional ops cannot be evaluated here
	//        throw new ArgumentOutOfRangeException("Op");
	//    }
	//  }
	//}

	public class LogicTreeNode
	{
		public bool NotVal1 = false;
		public bool NotVal2 = false;
		public LogicEquation.LogicOperand Op;
		public LogicTreeNode Node1 = null;
		public LogicTreeNode Node2 = null;
		public bool Val1;
		public bool Val2;

		public bool Evaluate()
		{
			if (Node1 != null) Val1 = Node1.Evaluate();
			if (Node2 != null) Val2 = Node2.Evaluate();
			Val1 = NotVal1 ? !Val1 : Val1;
			Val2 = NotVal2 ? !Val2 : Val2;

			switch (Op)
			{
				case LogicEquation.LogicOperand.And:
					return Val1 && Val2;
				case LogicEquation.LogicOperand.Or:
					return Val1 || Val2;
				case LogicEquation.LogicOperand.Xor:
					return (Val1 && !Val2) || (!Val1 && Val2);
				default: //additional ops cannot be evaluated here
					throw new ArgumentOutOfRangeException("Op");
			}
		}
	}


	public abstract class ValueNodeBase
	{
		#region Enums
		public enum ValueNodeType
		{
			String,
			Int,
			Float,
			Date,
			DateSpan,
			Field
		}
		public enum ValueOperand
		{
			NoOp,
			OpenParen,
			CloseParen,
			Plus,
			Minus,
			Times,
			DividedBy
		}
		public enum ValueUnits
		{
			General,
			Days,
			Hours,
			Minutes
		}
		#endregion

		#region Members
		public bool Simple; //simple nodes just contain a single value or a field (no nodes and no Val2
		public ValueNodeType NodeType { get; set; }
		public ValueUnits Units { get; set; }
		public ValueOperand Op { get; set; }
		protected string[] Vals;
		protected ValueNodeBase[] Nodes;
		protected bool DateReversed;


		protected string _name;
		public string Name
		{
			get { return GetName(); }
			set { _name = value; }
		}
		#endregion

		#region Constructors
		protected ValueNodeBase(ValueNodeType nodeType, XElement xml)
		{
			NodeType = nodeType;
			Vals = new string[2];
			Nodes = new ValueNodeBase[2];
			ParseXml(xml);
		}
		#endregion

		#region Utilities
		private void ParseXml(XElement xml)
		{
			var subnodes = xml.Elements("valueNode").ToList();
			switch (subnodes.Count)
			{
				case 0: //simple node
					Simple = true;
					_name = Input.GetAttribute(xml, "name"); //optional
					Vals[0] = Input.GetAttribute(xml, "value");
					return;
				case 1: //malformed definition 
					if (BoostLog.Instance != null)
						BoostLog.Instance.WriteEntry(EventLogEntryType.Error,
						                             "ValueNode equations require two sub-nodes. Treating as a simple node instead.");
					goto case 0;
				case 2: //equation
					ValueOperand op;
					var opName = Input.GetAttribute(xml, "operand");
					if (!Enum.TryParse(opName, true, out op))
					{
						if (BoostLog.Instance != null)
							BoostLog.Instance.WriteEntry(EventLogEntryType.Error, string.Format("Illegal Value Operand {0}", opName));
						op = ValueOperand.NoOp;
					}
					for (var i = 0; i < 2; i++)
					{
						var valTypeName = Input.GetAttribute(subnodes[i], "type");
						ValueNodeType vType;
						if (!Enum.TryParse(valTypeName, true, out vType))
						{
							if (BoostLog.Instance != null)
								BoostLog.Instance.WriteEntry(EventLogEntryType.Error, string.Format("Illegal Value Type {0}", valTypeName));
							op = ValueOperand.NoOp;
						}
						Nodes[i] = CreateNewNode(vType, subnodes[i]);
					}
					break;
				default: //too many nodes in equation --only first two will be used
					if (BoostLog.Instance != null)
						BoostLog.Instance.WriteEntry(EventLogEntryType.Error,
							string.Format("ValueNode equations may only have two sub-nodes. This one has {0}. Extra sub-nodes will be ignored", subnodes.Count));
					goto case 2;
			}
		}

		public static ValueNodeBase CreateNewNode(ValueNodeType vType, XElement xml)
		{
			switch (vType)
			{
				case ValueNodeType.String:
				case ValueNodeType.Int:
				case ValueNodeType.Float:
					return new NumericalValueNode(vType, xml);
				case ValueNodeType.Date:
				case ValueNodeType.DateSpan:
					return new DateValueNode(vType, xml);
				case ValueNodeType.Field:
					return new FieldValueNode(vType, xml);
				default:
					throw new ArgumentOutOfRangeException("vType");
			}
		}

		public XElement GetXml()
		{
			if (Simple)
			{
				return new XElement("valueNode",
														 new XAttribute("type", NodeType),
			                       new XAttribute("value", Vals[0]),
			                       new XAttribute("name", GetName())
														);
			}
			
			//equation
			var xml = new XElement("valueNode",
														 new XAttribute("type", NodeType)
														);
			for (var i = 0; i < 2; i++)
			{
				if (Nodes[i] == null)
				{
					if (BoostLog.Instance != null)
						BoostLog.Instance.WriteEntry(EventLogEntryType.Error,
							string.Format("Missing ValueNode for complex rule ({0})", GetName()), "");
					continue;
				}
				xml.Add(Nodes[i].GetXml());
			}
			return xml;
		}

		public List<string> GetFields()
		{
			var allFields = new List<string>();
			if (Simple)
			{
				if (NodeType == ValueNodeType.Field) allFields.Add(Name);
				return allFields;
			}

			foreach (var node in Nodes)
			{
				if (node == null) continue;
				var fields = node.GetFields();
				if (fields.Any()) allFields = allFields.Union(fields).Distinct().ToList();
			}
			return allFields;
		}


		#endregion

		#region Abstract Methods
		public abstract string Evaluate(XElement data, out ValueNodeType nodetype);
		public abstract string Evaluate(List<string> header, List<string> data, out ValueNodeType nodetype);
		protected abstract string GetName();
		#endregion
	}

	/// <summary>
	/// This ValueNode is used for String, Int and Float NodeTypes
	/// </summary>
	public class NumericalValueNode : ValueNodeBase
	{
		#region Members
		#endregion

		#region Constructors
		public NumericalValueNode(ValueNodeType nodeType, XElement xml)
			: base(nodeType, xml)
		{
		}
		#endregion

		#region Methods
		public override string Evaluate(List<string> header, List<string> data, out ValueNodeType nodetype)
		{
			nodetype = NodeType;
			if (Simple) return Vals[0];

			try
			{
				var nodetype1 = NodeType;
				var nodetype2 = NodeType;
				if (Nodes[0] != null) Vals[0] = Nodes[0].Evaluate(header, data, out nodetype1);
				if (Nodes[1] != null) Vals[1] = Nodes[1].Evaluate(header, data, out nodetype2);

				return FinishEvaluation(nodetype1, nodetype2);
			}
			catch (Exception ex)
			{
				if (BoostLog.Instance != null)
					BoostLog.Instance.WriteEntry(EventLogEntryType.Error,
																			 string.Format("Error evaluating NumericalValueNode. Val1 = {0}. Val2 = {1}", Vals[0], Vals[1]),
					                             ex);
				return null;
			}
		}

		public override string Evaluate(XElement data, out ValueNodeType nodetype)
		{
			nodetype = NodeType;
			if (Simple) return Vals[0];

			try
			{
				var nodetype1 = NodeType;
				var nodetype2 = NodeType;
				if (Nodes[0] != null) Vals[0] = Nodes[0].Evaluate(data, out nodetype1);
				if (Nodes[1] != null) Vals[1] = Nodes[1].Evaluate(data, out nodetype2);

				return FinishEvaluation(nodetype1, nodetype2);
			}
			catch (Exception ex)
			{
				if (BoostLog.Instance != null)
					BoostLog.Instance.WriteEntry(EventLogEntryType.Error,
																			 string.Format("Error evaluating NumericalValueNode. Val1 = {0}. Val2 = {1}", Vals[0], Vals[1]),
					                             ex);
				return null;
			}
		}

		private string FinishEvaluation(ValueNodeType nodetype1, ValueNodeType nodetype2)
		{
			double _numerical1, _numerical2, result;
			if (string.IsNullOrWhiteSpace(Vals[0]) || !double.TryParse(Vals[0], out _numerical1))
				return null;
			if (!string.IsNullOrWhiteSpace(Vals[1]) || !double.TryParse(Vals[1], out _numerical2))
				return null;

			switch (Op)
			{
				case ValueOperand.NoOp:
					result = _numerical1;
					break;
				case ValueOperand.Plus:
					result = _numerical1 + _numerical1;
					break;
				case ValueOperand.Minus:
					result = _numerical1 - _numerical1;
					break;
				case ValueOperand.Times:
					result = _numerical1*_numerical1;
					break;
				case ValueOperand.DividedBy:
					result = _numerical1/_numerical1;
					break;
				default:
					throw new ArgumentOutOfRangeException("Op");
			}
			if (NodeType == ValueNodeType.Int)
			{
				var resultInt = (int) Math.Floor(result);
				return resultInt.ToString();
			}
			return result.ToString("n");
		}

		protected override string GetName()
		{
			if (Simple) return _name;

			return string.Format("{0} {1} {2}", Nodes[0] == null ? "no op" : Nodes[0].Name, Op.ToString(), Nodes[1] == null ? "no op" : Nodes[1].Name);
		}

		#endregion
	}

	/// <summary>
	/// This ValueNode is used for Date and DateSpan NodeTypes
	/// </summary>
	public class DateValueNode : ValueNodeBase
	{
		#region Members
		#endregion

		#region Constructors
		public DateValueNode(ValueNodeType nodeType, XElement xml)
			: base(nodeType, xml)
		{
		}
		#endregion

		#region Methods
		public override string Evaluate(List<string> header, List<string> data, out ValueNodeType nodetype)
		{
			nodetype = NodeType;
			if (Simple) return CheckValue(Vals[0]);

			try
			{
				//only simle date nodes can exist without nodes
				//otherwise we'd need to know the node type for each val
				if (Nodes[0] == null || Nodes[1] == null)
					throw new Exception("All non-simple date nodes must have two sub-nodes");

				ValueNodeType nodetype1, nodetype2;
				Vals[0] = Nodes[0].Evaluate(header, data, out nodetype1);
				Vals[1] = Nodes[1].Evaluate(header, data, out nodetype2);

				return FinishEvaluation(nodetype1, nodetype2, out nodetype);
			}
			catch (Exception ex)
			{
				if (BoostLog.Instance != null)
					BoostLog.Instance.WriteEntry(EventLogEntryType.Error, 
																			 string.Format("Error evaluating DateValueNode. Val1 = {0}. Val2 = {1}", Vals[0], Vals[1]), 
																			 ex);
				return null;
			}
		}

		public override string Evaluate(XElement data, out ValueNodeType nodetype)
		{
			nodetype = NodeType;
			if (Simple) return CheckValue(Vals[0]);

			try
			{
				//only simle date nodes can exist without nodes
				//otherwise we'd need to know the node type for each val
				if (Nodes[0] == null || Nodes[1] == null)
					throw new Exception("All non-simple date nodes must have two sub-nodes");

				ValueNodeType nodetype1, nodetype2;
				Vals[0] = Nodes[0].Evaluate(data, out nodetype1);
				Vals[1] = Nodes[1].Evaluate(data, out nodetype2);

				return FinishEvaluation(nodetype1, nodetype2, out nodetype);
			}
			catch (Exception ex)
			{
				if (BoostLog.Instance != null)
					BoostLog.Instance.WriteEntry(EventLogEntryType.Error, 
																			 string.Format("Error evaluating DateValueNode. Val1 = {0}. Val2 = {1}", Vals[0], Vals[1]), 
																			 ex);
				return null;
			}
		}

		private string FinishEvaluation(ValueNodeType nodetype1, ValueNodeType nodetype2, out ValueNodeType nodetype)
		{
				//for NoOp, just return the value of the first node
				if (Op != ValueOperand.NoOp) 
				{
					nodetype = nodetype1;
					return Vals[0];
				}

				//Oterwise valid ops depend whether the values are dates or datespans (3 cases)
				//case 1: two dates
				if (nodetype1 == ValueNodeType.Date
				    && nodetype2 == ValueNodeType.Date)
				{
					if (Op != ValueOperand.Minus)
						throw new Exception(string.Format("Two dates can only be subtracted. {0} cannot be evaluated", Op.ToString()));

					DateTime date1, date2;
					if (!Input.TryGetDate(out date1, Vals[0], DateReversed))
						throw new Exception(string.Format("Illegal DateTime: {0} cannot be evaluated", Vals[0]));
					if (!Input.TryGetDate(out date2, Vals[1], DateReversed))
						throw new Exception(string.Format("Illegal DateTime: {0} cannot be evaluated", Vals[1]));

					var result = date1 - date2;
					nodetype = ValueNodeType.DateSpan;
					if (Units == ValueUnits.General) Units = ValueUnits.Minutes;
					return Input.EncodeTimeSpan(result, Units);
				}

				//case 2: two timespans
				if (nodetype1 == ValueNodeType.DateSpan
				    && nodetype2 == ValueNodeType.DateSpan)
				{
					TimeSpan span1, span2, result;
					ValueUnits units1, units2;
					if (!Input.TryGetTimeSpan(Vals[0], out span1, out units1))
						throw new Exception(string.Format("Illegal timespan. {0} cannot be evaluated", Vals[0]));
					if (!Input.TryGetTimeSpan(Vals[1], out span2, out units2))
						throw new Exception(string.Format("Illegal timespan. {0} cannot be evaluated", Vals[1]));

					switch (Op)
					{
						case ValueOperand.Plus:
							result = span1 + span2;
							break;
						case ValueOperand.Minus:
							result = span1 - span2;
							break;
						default:
							throw new Exception(string.Format("Two time spans can only be added or subtracted. {0} cannot be evaluated",
							                                  Op.ToString()));
					}

					nodetype = ValueNodeType.DateSpan;
					if (Units == ValueUnits.General) Units = units1;
					return Input.EncodeTimeSpan(result, Units);
				}

				//case 3: one value is a timespan and the other is a date
				DateTime date;
				TimeSpan span;
				ValueUnits units;
				if (nodetype1 == ValueNodeType.DateSpan)
				{
					if (!Input.TryGetDate(out date, Vals[0], DateReversed))
						throw new Exception(string.Format("Illegal DateTime: {0} cannot be evaluated", Vals[0]));
					if (!Input.TryGetTimeSpan(Vals[1], out span, out units))
						throw new Exception(string.Format("Illegal timespan. {0} cannot be evaluated", Vals[1]));
				}
				else
				{
					if (!Input.TryGetDate(out date, Vals[1], DateReversed))
						throw new Exception(string.Format("Illegal DateTime: {0} cannot be evaluated", Vals[1]));
					if (!Input.TryGetTimeSpan(Vals[0], out span, out units))
						throw new Exception(string.Format("Illegal timespan. {0} cannot be evaluated", Vals[0]));
				}

				var subtract = false;
				switch (Op)
				{
					case ValueOperand.Plus:
						break;
					case ValueOperand.Minus:
						subtract = true;
						break;
					default:
						throw new Exception(string.Format("Two time spans can only be added or subtracted. {0} cannot be evaluated",
						                                  Op.ToString()));
				}
				int val;
				switch (units)
				{
					case ValueUnits.Days:
						val = subtract ? span.Days : -1*span.Days;
						date = date.AddDays(val);
						break;
					case ValueUnits.Hours:
						val = subtract ? span.Hours : -1*span.Hours;
						date = date.AddHours(val);
						break;
					case ValueUnits.Minutes:
					case ValueUnits.General:
						val = subtract ? span.Minutes : -1*span.Minutes;
						date = date.AddMinutes(val);
						break;
					default:
						throw new ArgumentOutOfRangeException();
				}
				nodetype = ValueNodeType.Date;
				return date.ToString("G");
		}

		protected override string GetName()
		{
			if (Simple) return string.IsNullOrWhiteSpace(_name) ? Vals[0] : _name;

			return string.Format("{0} {1} {2}", Nodes[0] == null ? "no op" : Nodes[0].Name, Op.ToString(), Nodes[1] == null ? "no op" : Nodes[1].Name);
		}

		/// <summary>
		/// Check for special value names
		/// </summary>
		/// <param name="val"></param>
		/// <returns></returns>
		private string CheckValue(string val)
		{
			//TODO: NEED TO ADD TIMEZONE HANDLING
			//right now this is in pacific time
			return val.ToLower().Equals("today") ? DateTime.Now.ToString("G") : val;
		}

		#endregion
	}

	/// <summary>
	/// This ValueNode is used for Field NodeTypes
	/// Field nodes are always simple.
	/// The Name is the field name and can be passed in the constructor as val1
	/// The result value type is calculated and returned in the Evaluate method
	/// </summary>
	public class FieldValueNode : ValueNodeBase
	{
		#region Members
		#endregion

		#region Constructors
		//protected FieldValueNode(ValueNodeType nodeType, ValueOperand op = ValueOperand.NoOp, string val1 = null, string val2 = null, bool simple = true)
		public FieldValueNode(ValueNodeType nodeType, XElement xml)
			: base(nodeType, xml)
		{
		}
		#endregion

		#region Methods
		public override string Evaluate(List<string> header, List<string> data, out ValueNodeType nodetype)
		{
			//return node type is based on what the field evaluates to
			nodetype = ValueNodeType.String; //default is a string
			if (data == null) return null; //no data to evaluate

			try
			{
				var val = Input.GetValue(header, data, Name);
				if (string.IsNullOrEmpty(val)) return null; //no data matches this field

				return FinishEvaluation(val, ref nodetype);
			}
			catch (Exception ex)
			{
				if (BoostLog.Instance != null)
					BoostLog.Instance.WriteEntry(EventLogEntryType.Error,
																			 string.Format("Error evaluating NumericalValueNode. Val1 = {0}. Val2 = {1}", Vals[0], Vals[1]),
					                             ex);
				return null;
			}
		}

		public override string Evaluate(XElement data, out ValueNodeType nodetype)
		{
			//return node type is based on what the field evaluates to
			nodetype = ValueNodeType.String; //default is a string
			if (data == null) return null; //no data to evaluate

			try
			{
				var val = Input.GetValue(data, Name);
				if (string.IsNullOrEmpty(val)) return null; //no data matches this field

				return FinishEvaluation(val, ref nodetype);
			}
			catch (Exception ex)
			{
				if (BoostLog.Instance != null)
					BoostLog.Instance.WriteEntry(EventLogEntryType.Error,
																			 string.Format("Error evaluating NumericalValueNode. Val1 = {0}. Val2 = {1}", Vals[0], Vals[1]),
					                             ex);
				return null;
			}
		}

		private string FinishEvaluation(string val, ref ValueNodeType nodetype)
		{
			//determine node type
			DateTime testDate;
			TimeSpan testSpan;
			ValueUnits testUnits;
			int testInt;
			float testFloat;

			if (Input.TryGetDate(out testDate, val, DateReversed))
				nodetype = ValueNodeType.Date;
			else if (Input.TryGetTimeSpan(val, out testSpan, out testUnits))
				nodetype = ValueNodeType.DateSpan;
			else if (int.TryParse(val, out testInt))
				nodetype = ValueNodeType.Int;
			else if (float.TryParse(val, out testFloat))
				nodetype = ValueNodeType.Float;
			return val;
		}

		protected override string GetName()
		{
			return _name;
		}

		#endregion
	}

	/// <summary>
	/// More complex rule condition using a list of elements to create an exquation
	/// Also used for exclusions and filters
	/// </summary>
	//public class ValueNode
	//{
	//  #region Enums & Support Classes
	//  public enum EquationElementType
	//  {
	//    Op, //operand
	//    Int, //integer constant
	//    Float, //decimal constant
	//    Date, //today or custom (any others?)
	//    DateSpan, //length of time in days, weeks, months, or years
	//    Field
	//  }

	//  public class EquationElement
	//  {
	//    public EquationElementType ElementType { get; set; }
	//    public object Value;
	//  }
	//  #endregion

	//  #region Members
	//  private XElement _data;
	//  private List<EquationElement> _elements;
	//  public List<EquationElement> Elements
	//  {
	//    get //validate on get
	//    {
	//      ValidateElements();
	//      return _elements;
	//    }
	//    set
	//    {
	//      _elements = value;
	//    }
	//  }
	//  #endregion

	//  #region Constructors
	//  public ValueNode(string alias)
	//    :base(alias)
	//  {
	//    _data = null;
	//    ValidateElements(); //creates an initial empty list
	//  }

	//  public ValueNode(string alias, XElement xml)
	//    :base(alias)
	//  {
	//    _data = null;
	//    ParseElementXml(xml);
	//  }
	//  #endregion

	//  #region Logic

	//  public void ValidateElements()
	//  {
	//    //validation rules:
	//    //	possible elements are open/close paren, op, value
	//    //	op's are plus, minus, times, divideby
	//    //	values are int, float, date, datespan, field
	//    //	all open-parens must be closed
	//    //	cannot have a close-paren without an open first
	//    //
	//    //	each item will setup requirements for the next:
	//    //  first element: open-paren, value
	//    //	after value: close-paren or op
	//    //  after op: open-paren, value
	//    //	after open-paren: open-paren, value
	//    //	after close paren: op, close-paren

	//    if (_elements == null) _elements = new List<EquationElement>();

	//    if (_elements.Count < 1)
	//    {
	//      _validated = false; //no equation to validate
	//      Enabled = false;
	//      return;
	//    }

	//    //we have some elements to validate
	//    try
	//    {
	//      var removeList = new List<int>(); //list of indices to remove (illegal close parens, etc.)
	//      var openCount = 0; //running total of opens that have not closed
	//      var valueTypes = new List<EquationElementType>
	//        {
	//          EquationElementType.Int,
	//          EquationElementType.Float,
	//          EquationElementType.Date,
	//          EquationElementType.DateSpan,
	//          EquationElementType.Field
	//        };
	//      var opTypes = new List<EquationNode.ValueOperand>
	//        {
	//          EquationNode.ValueOperand.Plus,
	//          EquationNode.ValueOperand.Minus,
	//          EquationNode.ValueOperand.Times,
	//          EquationNode.ValueOperand.DividedBy
	//        };
	//      var nextValidValue = true;
	//      var nextValidOps = new List<EquationNode.ValueOperand> {EquationOperand.OpenParen}; //first element

	//      for (var i = 0; i < _elements.Count; i++)
	//      {
	//        var e = _elements[i];
	//        if (e.ElementType == EquationElementType.Op && !nextValidOps.Contains((EquationOperand) e.Value)
	//            || (!nextValidValue && !valueTypes.Contains(e.ElementType)))
	//        {
	//          removeList.Add(i); //illegal element found
	//          continue;
	//        }
	//        switch (e.ElementType)
	//        {
	//          case EquationElementType.Int:
	//          case EquationElementType.Float:
	//          case EquationElementType.Date:
	//          case EquationElementType.DateSpan:
	//          case EquationElementType.Field:
	//            nextValidValue = false;
	//            nextValidOps = opTypes.Union(new[] {EquationOperand.CloseParen}).ToList();
	//            break;
	//          case EquationElementType.Op:
	//            var op = (EquationOperand) e.Value;
	//            if (opTypes.Contains(op)) //equation operand
	//            {
	//              nextValidValue = true;
	//              nextValidOps = new List<EquationOperand> {EquationOperand.OpenParen};
	//              break;
	//            }
	//            switch (op)
	//            {
	//              case EquationOperand.OpenParen: //open paren
	//                openCount++;
	//                nextValidValue = true;
	//                nextValidOps = new List<EquationOperand> {EquationOperand.OpenParen};
	//                break;
	//              case EquationOperand.CloseParen: //close paren
	//                if (openCount < 1) removeList.Add(i); //illegal close paren
	//                else openCount--;
	//                nextValidValue = false;
	//                nextValidOps = opTypes.Union(new[] {EquationOperand.CloseParen}).ToList();
	//                break;
	//            }
	//            break;
	//        }
	//      }
	//      //now clean-up all the parens 					
	//      for (var j = removeList.Count - 1; j >= 0; j--) //must go backward through the list so indices are not changed
	//        _elements.RemoveAt(removeList[j]);
	//      while (openCount-- > 0)
	//        _elements.Add(new EquationElement {ElementType = EquationElementType.Op, Value = EquationOperand.CloseParen});
	//    }
	//    catch (Exception ex)
	//    {
	//      if (BoostLog.Instance != null)
	//        BoostLog.Instance.WriteEntry(EventLogEntryType.Error, "Error validating ConditionLogic", ex, _alias);
	//      _elements = new List<EquationElement>();
	//    }
	//    finally
	//    {
	//      if (_elements.Count < 1)
	//      {
	//        _validated = false; //no equation to validate
	//        Enabled = false;
	//      }
	//      else
	//      {
	//        _validated = true;
	//        Enabled = true;
	//      }
	//    }
	//  }

	//  public override bool Evaluate(XElement data, XElement data)
	//  {
	//    if (!Enabled || data == null) return false;

	//    _data = data;
	//    //build the equation tree and replace fields elements with their values
	//    var tree = BuildTree(0, _elements.Count - 1);
	//    _data = null;
	//    return tree.Evaluate();
	//  }

	//  private EquationNode BuildTree(int startIndex, int endIndex)
	//  {
	//    var thisNode = new LogicTreeNode();
	//    var nodePos = 1; //nodePos tracks which part of the node is currently being processed (1 = val1, 2 = op, 3 = val2, 4 = complete)
	//    for (var i = startIndex; i <= endIndex; i++)
	//    {
	//      var e = _elements[i];
	//      switch (e.ElementType)
	//      {
	//        case LogicElementType.NodeId:
	//          if (nodePos == 2)
	//            throw new Exception(string.Format("Invalid Logic at index {0} --no op found between val1 and val2", i));
	//          bool val;
	//          if (!_results.TryGetValue((int)e.Value, out val)) val = false;
	//          if (nodePos == 1)
	//          {
	//            thisNode.Vals[0] = val;
	//            nodePos++;
	//            break;
	//          }
	//          if (i == endIndex) //last element
	//            thisNode.Vals[1] = val;
	//          else
	//            thisNode.Nodes[1] = BuildTree(i, endIndex); //more elements to examine
	//          return thisNode;
	//        case LogicElementType.Op:
	//          var op = (LogicOperator)e.Value;
	//          switch (op)
	//          {
	//            case LogicOperator.OpenParen:
	//              if (nodePos == 2)
	//                throw new Exception(string.Format("Invalid Logic at index {0} --no op found between val1 and val2", i));
	//              if (nodePos == 3)
	//              {
	//                thisNode.Nodes[1] = BuildTree(i, endIndex); //handle it at the beginning of the next node
	//                return thisNode;
	//              }
	//              var newStart = i + 1; //drop opening paren
	//              var close = _elements.FindIndex(newStart,
	//                                             x =>
	//                                             x.ElementType == LogicElementType.Op &&
	//                                             (LogicOperator)x.Value == LogicOperator.CloseParen);
	//              if (close > endIndex)
	//                throw new Exception(string.Format("Invalid Logic at index {0} --matching close paren is beyond index range", i));
	//              var newEnd = close - 1; //drop closing paren
	//              thisNode.Nodes[0] = BuildTree(newStart, newEnd); //evaluate the contents between the parens
	//              nodePos++;
	//              break;
	//            case LogicOperator.CloseParen:
	//              throw new Exception(string.Format("Invalid Logic at index {0} --should not encounter close paren without open", i));
	//            case LogicOperator.Not:
	//              if (nodePos == 2)
	//                throw new Exception(string.Format("Invalid Logic at index {0} --no op found between val1 and NOT", i));
	//              if (nodePos == 1) thisNode.NotVal1 = true;
	//              else if (i == endIndex - 1) thisNode.NotVal2 = true;
	//              else
	//              {
	//                thisNode.Nodes[1] = BuildTree(i, endIndex); //handle it at the beginning of the next node
	//                return thisNode;
	//              }
	//              nodePos++;
	//              break;
	//            default:
	//              if (nodePos != 2)
	//                throw new Exception(string.Format("Invalid Logic at index {0} --op found in wrong location", i));
	//              thisNode.Op = op;
	//              nodePos++;
	//              break;
	//          }
	//          break;
	//        default:
	//          throw new ArgumentOutOfRangeException();
	//      }
	//    }

	//    //fill defaults if not enough emelemnts 
	//    if (nodePos == 1) return null; //no elements
	//    if (nodePos == 2) thisNode.Op = LogicOperator.Or;
	//    if (nodePos < 4) thisNode.Val2 = false;
	//    return thisNode;
	//  }

	//  #endregion

	//  #region Xml
	//  public void ParseElementXml(XElement xml)
	//  {
	//    if (_elements == null) _elements = new List<EquationElement>();
	//    if (xml != null)
	//    {
	//      var elements = xml.Elements("equationElement");
	//      if (elements.Any())
	//      {
	//        foreach (var e in elements)
	//        {
	//          var et = Input.GetAttribute(e, "type");
	//          EquationElementType eType;
	//          if (!Enum.TryParse(et, true, out eType)) continue; //type is required
	//          var val = Input.GetAttribute(e, "value");
	//          if (string.IsNullOrEmpty(val)) continue; //value is required

	//          object oVal;
	//          switch (eType)
	//          {
	//            case EquationElementType.Op:
	//              EquationOperand op;
	//              if (!Enum.TryParse(val, true, out op)) continue; //invalid op
	//              oVal = (object)op;
	//              break;
	//            case EquationElementType.Int:
	//              int iVal;
	//              if (!int.TryParse(val, out iVal)) continue; //invalid constant
	//              oVal = (object)iVal;
	//              break;
	//            case EquationElementType.Float:
	//              float fVal;
	//              if (!float.TryParse(val, out fVal)) continue; //invalid constant
	//              oVal = (object)fVal;
	//              break;
	//            case EquationElementType.Date:
	//              DateTime date;
	//              if (!Input.TryGetDate(out date, val, DateReversed)) continue; //invalid date
	//              oVal = (object)date;
	//              break;
	//            case EquationElementType.DateSpan:
	//              var split = val.Split(' ');
	//              if (split.Length != 2) continue; //must be of the form 
	//              int span;
	//              if (!Input.TryGetDate(out date, val, DateReversed)) continue; //invalid date
	//              oVal = (object)date;
	//              break;
	//            default:
	//            case EquationElementType.Field:
	//              if (string.IsNullOrEmpty(val)) continue; //invalid field name
	//              oVal = (object)val;
	//              break;
	//          }
	//          _elements.Add(new EquationElement { ElementType = eType, Value = oVal });
	//        }
	//      }
	//    }
	//    ValidateElements();
	//  }

	//  public XElement GetElementXml()
	//  {
	//    var xml = new XElement("condition",
	//                           new XAttribute("type", "equation")
	//      );
	//    foreach (var e in _elements)
	//    {
	//      string val;
	//      switch (e.ElementType)
	//      {
	//        case EquationElementType.Op:
	//          val = ((EquationOperand)e.Value).ToString();
	//          break;
	//        case EquationElementType.Int:
	//          val = ((int)e.Value).ToString();
	//          break;
	//        case EquationElementType.Float:
	//          val = ((float)e.Value).ToString();
	//          break;
	//        case EquationElementType.Date:
	//          val = ((DateTime)e.Value).ToString("yyyy-MM-dd");
	//          break;
	//        default:
	//        case EquationElementType.DateSpan:
	//        case EquationElementType.Field:
	//          val = ((string)e.Value);
	//          break;
	//      }
	//      var le = new XElement("logicElement",
	//                            new XAttribute("type", e.ElementType.ToString()),
	//                            new XAttribute("value", val)
	//        );
	//      xml.Add(le);
	//    }
	//    return xml;
	//  }
	//  #endregion
	//}

	
	
	////////////////////////////////////////////////////////////////////////////
	///									LEGACY CLASSES
	//////////////////////////////////////////////////////////////////////////// 
	
	/// <summary>
	/// Basic component of a ConditionSet to define exclusions and filters
	/// </summary>
	public class Condition
	{
		#region Enums & Support Classes
		public enum Comparator
		{
			Eq, //	=		(string or numerical)
			Ne, //	!=	(string or numerical)
			Lt, //	<		(numerical only)
			Gt, //	>		(numerical only)
			Le, //	<=	(numerical only)
			Ge, //	>=	(numerical only)
			Contains, //	contains (string only)
			StartsWith, //	startsWith (string only)
			EndsWith, //	endsWith (string only)
			IsOneOf,
			Copy // no comparison required just copy the value as a result
		}
		#endregion

		#region Members
		private readonly string _alias;
		private bool _validated;
		public bool Enabled { get; protected set; }
		public string Name;
		public Comparator Comparison;
		public string Value;
		public string QueryField;
		public string ResultField;
		#endregion

		#region Constructors
		public Condition(string name, string comparison, string value, string queryField, string resultField = null)
		{
			_alias = "";
			_validated = false;
			Enabled = true;

			try
			{
				if (string.IsNullOrEmpty(name)) name = string.Format("{0} {1} {2}", queryField, comparison, value);
				Name = name;
				Value = value;
				QueryField = queryField;
				ResultField = string.IsNullOrEmpty(resultField) ? queryField : resultField;
				Comparison = GetComparitor(comparison);
				if (string.IsNullOrEmpty(queryField))
					throw new Exception("Conditions must include a query field");
			}
			catch (Exception ex)
			{
				if (BoostLog.Instance != null)
					BoostLog.Instance.WriteEntry(EventLogEntryType.Error,
						string.Format("Condition {0} is disabled.", name), ex, _alias);
				Enabled = false;
			}
		}

		public Condition(string alias, XElement xml)
		{
			_alias = alias;
			_validated = false;
			Enabled = true;
			ParseElementXml(xml);
		}
		#endregion

		#region Basic Methods
		public static Comparator GetComparitor(string value)
		{
			Comparator c;
			if (!Enum.TryParse(value, true, out c))
				throw new Exception("Unknown comparator: " + value);
			return c;
		}

#if !CART_EXTRACTOR_TEST_SITE
        public static DashComparisonDef GetComparisonDef(Comparator c)
		{
			switch (c)
			{
				case Comparator.Eq:
					return new DashComparisonDef { Value = "eq", Name = "equals" };
				case Comparator.Ne:
					return new DashComparisonDef { Value = "ne", Name = "is not equal to" };
				case Comparator.Lt:
					return new DashComparisonDef { Value = "lt", Name = "is less than" };
				case Comparator.Gt:
					return new DashComparisonDef { Value = "gt", Name = "is greater than" };
				case Comparator.Le:
					return new DashComparisonDef { Value = "le", Name = "is less than or equal to" };
				case Comparator.Ge:
					return new DashComparisonDef { Value = "ge", Name = "is greater than or equal to" };
				case Comparator.Contains:
					return new DashComparisonDef { Value = "contains", Name = "contains" };
				case Comparator.StartsWith:
					return new DashComparisonDef { Value = "startsWith", Name = "starts with" };
				case Comparator.EndsWith:
					return new DashComparisonDef { Value = "endsWith", Name = "ends with" };
				case Comparator.IsOneOf:
					return new DashComparisonDef { Value = "isOneOf", Name = "is one of" };
				case Comparator.Copy:
					return new DashComparisonDef { Value = "Copy", Name = "Copy" };
				default:
					throw new Exception("Illegal comparator: " + c);
			}
		}

		public static DashComparisonDef[] GetComparisons()
		{
			var comparisons = new List<DashComparisonDef>
					{
						GetComparisonDef(Comparator.Eq),
						GetComparisonDef(Comparator.Ne),
						GetComparisonDef(Comparator.Lt),
						GetComparisonDef(Comparator.Gt),
						GetComparisonDef(Comparator.Le),
						GetComparisonDef(Comparator.Ge),
						GetComparisonDef(Comparator.Contains),
						GetComparisonDef(Comparator.StartsWith),
						GetComparisonDef(Comparator.EndsWith),
						GetComparisonDef(Comparator.IsOneOf),
						GetComparisonDef(Comparator.Copy)
					};

			return comparisons.ToArray();
		}

		public static string GetComparisonName(string value)
		{
			var c = GetComparitor(value);
			var d = GetComparisonDef(c);
			return d.Name;
		}
#endif

		public bool Equals(string name, string comparison, string value, string field)
		{
			//only compare names if one is provided
			if (!string.IsNullOrEmpty(name) && !name.Equals(Name)) return false;
			if (!field.Equals(QueryField, StringComparison.OrdinalIgnoreCase)) return false;
			if (!comparison.Equals(Comparison.ToString(), StringComparison.OrdinalIgnoreCase)) return false;

			//for value equality, allow some leniency when comparing numbers or dates
			double a, b;
			if (double.TryParse(value, out a) && double.TryParse(Value, out b)) return a.Equals(b);
			DateTime dateA, dateB;
			if (DateTime.TryParse(value, out dateA) && DateTime.TryParse(Value, out dateB)) return dateA.Equals(dateB);
			return value.Equals(Value);
		}

		public string GetSqlEquation()
		{
			var equation = QueryField;
			switch (Comparison)
			{
				case Comparator.Eq:
					equation += " = ";
					break;
				case Comparator.Ne:
					equation += " <> ";
					break;
				case Comparator.Lt:
					equation += " < ";
					break;
				case Comparator.Gt:
					equation += " > ";
					break;
				case Comparator.Le:
					equation += " <= ";
					break;
				case Comparator.Ge:
					equation += " >= ";
					break;
				//case Comparitor.Contains: //should not call GetSqlEquation for these types
				//case Comparitor.StartsWith:
				//case Comparitor.EndsWith:
				//case Comparitor.IsOneOf:
				//case Comparitor.Copy:
				default:
					throw new Exception("Illegal comparison operator (" + Comparison + ")");
			}
			equation += Value;
			return equation;
		}

		#endregion

		#region Logic
		/// <summary>
		/// Quick compare just for integers
		/// </summary>
		/// <param name="a"></param>
		/// <returns></returns>
		public bool Compare(int a)
		{
			int b;
			if (!int.TryParse(Value, out b)) return false;

			switch (Comparison)
			{
				case Comparator.Eq:
					return a.Equals(b);
				case Comparator.Ne:
					return !a.Equals(b);
				case Comparator.Lt:
					return a < b;
				case Comparator.Gt:
					return a > b;
				case Comparator.Le:
					return a <= b;
				case Comparator.Ge:
					return a >= b;
				default:
					if (BoostLog.Instance != null)
						BoostLog.Instance.WriteEntry(EventLogEntryType.Error,
							string.Format("Illegal comparison ({0})\nActual = {1}\nValue = {2}", Comparison, a, Value),
							"", _alias);
					return false;
			}
		}

		/// <summary>
		/// Full compare for all data types
		/// </summary>
		/// <param name="actual"></param>
		/// <returns></returns>
		public bool Compare(string actual)
		{
			if (string.IsNullOrEmpty(actual))
			{
				//empty actual is valid for eq and ne
				if (Comparison.Equals(Comparator.Eq)) return string.IsNullOrEmpty(Value) || Value.ToLower().Equals("null");
				if (Comparison.Equals(Comparator.Ne)) return !string.IsNullOrEmpty(Value) && !Value.ToLower().Equals("null");
				return false;
			}
			//special case for Copy
			if (Comparison == Comparator.Copy)
			{
				Name = actual;
				return true; //always true, just copy the result
			}

			double a, b;
			if (double.TryParse(actual, out a) && double.TryParse(Value, out b))
			{
				//compare as numbers
				switch (Comparison)
				{
					case Comparator.Eq:
						return a.Equals(b);
					case Comparator.Ne:
						return !a.Equals(b);
					case Comparator.Lt:
						return a < b;
					case Comparator.Gt:
						return a > b;
					case Comparator.Le:
						return a <= b;
					case Comparator.Ge:
						return a >= b;
					case Comparator.Contains:
						return actual.Contains(Value);
					case Comparator.StartsWith:
						return actual.StartsWith(Value);
					case Comparator.EndsWith:
						return actual.EndsWith(Value);
					case Comparator.IsOneOf:
						var values = Value.Split(',').ToList();
						return values.Any(x => x.Equals(actual));
					default:
						if (BoostLog.Instance != null)
							BoostLog.Instance.WriteEntry(EventLogEntryType.Error,
								string.Format("Illegal comparison ({0})\nActual = {1}\nValue = {2}", Comparison, actual, Value),
								"", _alias);
						return false;
				}
			}

			DateTime dateA, dateB;
			if (Value != null && DateTime.TryParse(actual, out dateA) && DateTime.TryParse(Value, out dateB))
			{
				//compare as dates
				switch (Comparison)
				{
					case Comparator.Eq:
						return dateA.Equals(dateB);
					case Comparator.Ne:
						return !dateA.Equals(dateB);
					case Comparator.Lt:
						return dateA < dateB;
					case Comparator.Gt:
						return dateA > dateB;
					case Comparator.Le:
						return dateA <= dateB;
					case Comparator.Ge:
						return dateA >= dateB;
					case Comparator.Contains:
						return actual.Contains(Value);
					case Comparator.StartsWith:
						return actual.StartsWith(Value);
					case Comparator.EndsWith:
						return actual.EndsWith(Value);
					case Comparator.IsOneOf:
						var values = Value.Split(',').ToList();
						return values.Any(x => x.Equals(actual));
					default:
						if (BoostLog.Instance != null)
							BoostLog.Instance.WriteEntry(EventLogEntryType.Error,
								string.Format("Illegal comparison ({0})\nActual = {1}\nValue = {2}", Comparison, actual, Value),
								"", _alias);
						return false;
				}
			}

			//only compare as strings (and ignore case)
			if (Value == null) return false; //null actual handled above
			actual = actual.ToLower();
			var valToLower = Value.ToLower();

			switch (Comparison)
			{
				case Comparator.Eq:
					return actual.Equals(valToLower);
				case Comparator.Ne:
					return !actual.Equals(valToLower);
				case Comparator.Contains:
					return actual.Contains(valToLower);
				case Comparator.StartsWith:
					return actual.StartsWith(valToLower);
				case Comparator.EndsWith:
					return actual.EndsWith(valToLower);
				case Comparator.IsOneOf:
					var values = valToLower.Split(',').ToList();
					return values.Any(x => x.Equals(actual));
				case Comparator.Lt:
				case Comparator.Gt:
				case Comparator.Le:
				case Comparator.Ge:
				default:
					if (BoostLog.Instance != null)
						BoostLog.Instance.WriteEntry(EventLogEntryType.Error,
							string.Format("Illegal comparison ({0})\nActual = {1}\nValue = {2}", Comparison, actual, Value),
							"", _alias);
					return false;
			}
		}

		/// <summary>
		/// Apply this condition against the data provided
		/// Overloaded version for data lists
		/// </summary>
		/// <param name="data"></param>
		/// <returns>true if the condition is met</returns>
		public bool Evaluate(List<string> header, List<string> data)
		{
			if (!Enabled) return false;
			return Compare(Input.GetValue(header, data, ResultField));
		}

		/// <summary>
		/// Apply this condition against the data provided
		/// </summary>
		/// <param name="data"></param>
		/// <returns>true if the condition is met</returns>
		public bool Evaluate(XElement data)
		{
			if (!Enabled) return false;
			return Compare(Input.GetValue(data, ResultField));
		}

		public List<string> Evaluate(List<string> header, IEnumerable<List<string>> data, string returnField)
		{
			var results =
				data.Where(x => Compare(Input.GetValue(header, x, ResultField)))
							.Select(x => Input.GetValue(header, x, returnField)).ToList();
			return results;
		}

		public List<string> Evaluate(IEnumerable<XElement> items, string returnField)
		{
			var results =
				items.Where(x => Compare(Input.GetValue(x, ResultField)))
							.Select(x => Input.GetValue(x, returnField)).ToList();
			return results;
		}

		public List<string> Evaluate(IEnumerable<Dictionary<string, string>> items, string returnField)
		{
			var matchingRecords =
				from row in items
				where Compare(row[ResultField])
				select row;

			return matchingRecords.Select(record => record[returnField]).ToList();
		}
		#endregion

		#region Xml
		void ParseElementXml(XElement ec)
		{
			try
			{
				Name = Input.GetAttribute(ec, "name");
				Value = Input.GetAttribute(ec, "value");
				QueryField = Input.GetAttribute(ec, "fieldName");
				ResultField = Input.GetAttribute(ec, "resultFieldName");
				if (string.IsNullOrEmpty(ResultField)) ResultField = QueryField;
				var comparison = Input.GetAttribute(ec, "comparison");
				Comparison = GetComparitor(comparison);
				if (string.IsNullOrEmpty(Name)) Name = string.Format("{0} {1} {2}", QueryField, comparison, Value);
				if (string.IsNullOrEmpty(QueryField))
					throw new Exception("Conditions must include a query field");
			}
			catch (Exception ex)
			{
				if (BoostLog.Instance != null)
					BoostLog.Instance.WriteEntry(EventLogEntryType.Error,
						string.Format("Condition {0} is disabled.", Name), ex, _alias);
				Enabled = false;
			}
		}
		#endregion
	}

	/// <summary>
	/// Rule conditions used to define replacements
	/// </summary>
	public class ReplacementCondition
	{
		public enum RepType
		{
			Catalog,
			Item,
			Invalid
		}

		public string Name;
		public RepType Type;
		public string OldName;
		public string NewName;
		public string OldResultField;
		public string NewResultField;

		public ReplacementCondition(string name, string type, string oldName, string newName, string oldResultField = null,
																string newResultField = null)
		{
			Name = name;
			OldName = oldName;
			NewName = newName;
			try
			{
				Type = (RepType)Enum.Parse(typeof(RepType), type, true);
			}
			catch
			{
				Type = RepType.Invalid;
			}
			if (Type == RepType.Catalog)
			{
				OldResultField = string.IsNullOrEmpty(oldResultField) ? oldName : oldResultField;
				NewResultField = string.IsNullOrEmpty(newResultField) ? newName : newResultField;
			}
		}

		public override string ToString()
		{
			return "[Name = " + Name + ", Type = " + Type.ToString() + ", OldName = " + OldName + ", NewName = " + NewName
						 + ", OldResultField = " + OldResultField + ", NewResultField = " + NewResultField + "]";
		}
	}

	/// <summary>
	/// Rule conditions used to define Featured cross-sell and Featured up-sell
	/// </summary>
	public class FeaturedRecCondition
	{
		public string QueryField;
		public string ResultField;
		public bool Include; //false = exclude
		public bool Enabled;

		public FeaturedRecCondition(string queryField, string resultField = null, bool include = true, bool enabled = true)
		{
			QueryField = queryField;
			ResultField = string.IsNullOrEmpty(resultField) ? queryField : resultField;
			Include = include;
			Enabled = enabled;
		}
	}

	/// <summary>
	/// Rules that relate to categories
	///		categories to ignore
	///		categories to exclude
	///		categories to filter and which filter groups are universal
	///		category heirachy (parentage)
	/// </summary>
	public class CategoryConditions
	{
		public struct FilterCatDef
		{
			public string CatId;
			public string GroupId;

			public FilterCatDef(string catid, string groupid)
			{
				CatId = catid;
				GroupId = groupid;
			}
		}

		public enum CatConditionType
		{
			Ignore,
			CrossSell,
			Exclude,
			Filter,
			Universal
		}

		private readonly List<string> _ignore;
		private readonly List<string> _crossSellCats;
		private readonly List<string> _excludeCats;
		private readonly List<FilterCatDef> _filterCats;
		private readonly List<string> _universalCats;
		private readonly Dictionary<string, IEnumerable<string>> _parentList;

		public bool FiltersExist
		{
			get { return _filterCats.Count > 0; }
		}

		public List<FilterCatDef> Filters
		{
			get { return _filterCats; }
		}

		public bool ExclusionsExist
		{
			get { return _excludeCats.Count > 0; }
		}

		public List<string> Exclusions
		{
			get { return _excludeCats; }
		}

		public bool OptimizationsExist
		{
			get { return _ignore.Count > 0; }
		}

		public List<string> Optimizations
		{
			get { return _ignore; }
		}

		public bool CrossCategoryExist
		{
			get { return _crossSellCats.Count > 0; }
		}

		public List<string> CrossSellCats
		{
			get { return _crossSellCats; }
		}

		public bool UniversalExist
		{
			get { return _universalCats.Count > 0; }
		}

		public List<string> Universals
		{
			get { return _universalCats; }
		}

		public CategoryConditions()
		{
			_ignore = new List<string>();
			_crossSellCats = new List<string>();
			_excludeCats = new List<string>();
			_filterCats = new List<FilterCatDef>();
			_universalCats = new List<string>();
			_parentList = new Dictionary<string, IEnumerable<string>>();
		}

		public bool AddCat(string type, string value, string groupId = null)
		{
			if (string.IsNullOrEmpty(type) || (string.IsNullOrEmpty(value)))
				return false;

			CatConditionType catType;
			if (!Enum.TryParse(type, true, out catType)) return false;
			return AddCat(catType, value, groupId);
		}

		public bool AddCat(CatConditionType catType, string value, string groupId = null)
		{
			if (string.IsNullOrEmpty(value)) return false;

			switch (catType)
			{
				case CatConditionType.Ignore:
					if (_ignore.Contains(value)) return false;
					_ignore.Add(value);
					return true;
				case CatConditionType.CrossSell:
					if (_crossSellCats.Contains(value)) return false;
					_crossSellCats.Add(value);
					return true;
				case CatConditionType.Exclude:
					if (_excludeCats.Contains(value)) return false;
					_excludeCats.Add(value);
					return true;
				case CatConditionType.Filter:
					var group = string.IsNullOrEmpty(groupId) ? value : groupId;
					if (_filterCats.Any(x => x.CatId.Equals(value) && x.GroupId.Equals(group))) return false;
					_filterCats.Add(new FilterCatDef(value, group));
					return true;
				case CatConditionType.Universal:
					if (_universalCats.Contains(value)) return false;
					_universalCats.Add(value);
					return true;
			}
			return false;
		}

		public bool RemoveCat(string type, string value, string groupId = null)
		{
			if (string.IsNullOrEmpty(type) || (string.IsNullOrEmpty(value)))
				return false;

			CatConditionType catType;
			if (!Enum.TryParse(type, true, out catType)) return false;
			return RemoveCat(catType, value, groupId);
		}

		public bool RemoveCat(CatConditionType catType, string value, string groupId = null)
		{
			if (string.IsNullOrEmpty(value)) return false;

			var found = false;
			switch (catType)
			{
				case CatConditionType.Ignore:
					found = _ignore.Remove(value);
					break;
				case CatConditionType.CrossSell:
					found = _crossSellCats.Remove(value);
					break;
				case CatConditionType.Exclude:
					found = _excludeCats.Remove(value);
					break;
				case CatConditionType.Filter:
					found = _filterCats.Remove(new FilterCatDef(value, string.IsNullOrEmpty(groupId) ? value : groupId));
					break;
				case CatConditionType.Universal:
					found = _universalCats.Remove(string.IsNullOrEmpty(groupId) ? value : groupId);
					break;
			}
			return found;
		}

		public void RemoveAll(CatConditionType catType)
		{
			switch (catType)
			{
				case CatConditionType.Ignore:
					_ignore.RemoveAll(x => true);
					break;
				case CatConditionType.CrossSell:
					_crossSellCats.RemoveAll(x => true);
					break;
				case CatConditionType.Exclude:
					_excludeCats.RemoveAll(x => true);
					break;
				case CatConditionType.Filter:
					_filterCats.RemoveAll(x => true);
					break;
				case CatConditionType.Universal:
					_universalCats.RemoveAll(x => true);
					break;
			}
		}

		public bool Ignored(string value)
		{
			return _ignore.Contains(value);
		}

		public bool Excluded(string value)
		{
			return _excludeCats.Contains(value);
		}

		public bool Universal(string value)
		{
			return _universalCats.Contains(value);
		}

		public List<string> Filtered(string value) //returns a list of all filter groups defined for the given att1ID
		{
			var groups = new List<string>();
			try
			{
				groups = _filterCats.Where(x => x.CatId.Equals(value)).Select(x => x.GroupId).ToList();
			}
			catch
			{
			}

			return groups;
		}

		public bool AnyExcluded(string values) //comma separated list of categories
		{
			if ((_excludeCats.Count < 1) || (string.IsNullOrEmpty(values)))
				return false;

			var catList = values.Split(',').Select(p => p.Trim()).ToList();
			return catList.Any(Excluded);
		}

		public bool AnyUniversal(string values) //comma separated list of categories
		{
			if (string.IsNullOrEmpty(values))
				return false;

			var catList = values.Split(',').Select(p => p.Trim()).ToList();
			return catList.Any(Universal);
		}

		public List<string> AnyFiltered(string values) //list of categories
		{
			var matches = new List<string>();
			if (string.IsNullOrEmpty(values))
				return matches;

			var catList = values.Split(',').Select(p => p.Trim()).ToList();
			foreach (var cat in catList)
				matches.AddRange(Filtered(cat));
			return matches;
		}

		public string RemoveIgnored(string values) //comma separated list of categories
		{
			if ((_ignore.Count < 1) || (string.IsNullOrEmpty(values)))
				return values;

			var catList = values.Split(',').Select(p => p.Trim()).ToList();
			catList = catList.Where(cat => !Ignored(cat)).ToList();
			return catList.Count < 1 ? "" : catList.Aggregate((w, j) => string.Format("{0},{1}", w, j));
		}

		public IEnumerable<string> RemoveIgnored(IEnumerable<string> catList) //list of categories
		{
			return catList.Where(cat => !Ignored(cat)).ToList();
		}

		//add one parent/child relationship to the parent list
		public void AddParent(string child, string parent)
		{
			AddParents(child, new List<string> { parent });
		}

		//add a many-parent/child relationship to the parent list
		public void AddParents(string child, IEnumerable<string> newParents)
		{
			//make sure there are no duplicates and child is not one of the new parents
			newParents = RemoveIgnored(newParents.Distinct().Where(p => !p.Equals("0") && !child.Equals(p)));
			if (!newParents.Any()) return;

			IEnumerable<string> parents;
			if (_parentList.TryGetValue(child, out parents)) //existing child
			{
				parents = parents.Union(newParents); //no duplicates added
				_parentList[child] = parents;
			}
			else //new child
				_parentList.Add(child, newParents);
		}

		//find all distinct parents and ancestors for a list of categories
		public IEnumerable<string> GetAllParents(IEnumerable<string> catList)
		{
			var parents = new List<string>();
			foreach (var cat in catList)
			{
				var ancestors = GetAllParents(cat, catList.Union(parents));
				if (ancestors.Any())
					parents = parents.Union(ancestors).ToList();
			}
			return catList.Union(parents);
		}

		//find all distinct parents and ancestors for a single category, ignoring those in an existing list
		public IEnumerable<string> GetAllParents(string child, IEnumerable<string> existing = null)
		{
			var parents = new List<string>();
			IEnumerable<string> closeParents;
			if (!_parentList.TryGetValue(child, out closeParents))
				return parents; //no parents

			//reduce recursion be eliminating duplicates
			if (existing != null)
			{
				closeParents = closeParents.Except(existing);
				existing = existing.Union(closeParents);
			}
			else
				existing = closeParents;
			parents = closeParents.ToList();

			//return closeParents.Select(GetAllParents).Aggregate(closeParents, (current, ancestors) => current.Union(ancestors).ToList());
			return closeParents.Select(cat => GetAllParents(cat, existing))
												 .Where(ancestors => ancestors.Any())
												 .Aggregate(parents, (current, ancestors) => current.Union(ancestors).ToList());
		}
	}

}