using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using _4_Tell.CommonTools;

namespace _4_Tell.CartExtractors
{
	public class FeaturedRecLists
	{
		public List<string> Recommended; //ordered list of recommeded ids (order = ranking)
		public List<string> Blocked;		//list of blocked ids

		public FeaturedRecLists()
		{
			Recommended = new List<string>();
			Blocked = new List<string>();
		}
	}

	public class FeaturedRecommendations
	{
		public int Count
		{ get { return Records.Count; } }
		
		public Dictionary<string, FeaturedRecLists> Records { get; private set; } //key is primary id

		public FeaturedRecommendations()
		{
			Records = new Dictionary<string, FeaturedRecLists>();
		}

		public FeaturedRecommendations(List<string[]> data)
		{
			if (data == null) return;

			//get column order (data[0] holds the column headers) and determine whether the file has ranking or likelihood columns
			var columnNames = new string[] { "ProductId", "RecId", "Ranking", "Likelihood" };
			var headerPositions = new int[columnNames.Length];
			var headerRow = data[0];
			data.RemoveAt(0);
			for (var i = 0; i < columnNames.Length; i++)
			{
				var found = false;
				for (var j = 0; j < headerRow.Length; j++)
				{
					if (!headerRow[j].Equals(columnNames[i], StringComparison.OrdinalIgnoreCase))
						continue;
					headerPositions[i] = j;
					found = true;
					break;
				}
				if (!found)
				{
					if (i > 1) //ranking and Likelihood columns are optional
						headerPositions[i] = -1;
					else
						throw new Exception(string.Format("Cannot read Featured Recs. Missing column {0}", columnNames[i]));
				}
			}
			var convertLikelihood = headerPositions[2] == -1 && headerPositions[3] > -1; //Ranking is missing and Likelihood exists

			//first parse the rows into an initial dictionary that preserves any rankings from the file
			var firstPass = new Dictionary<string, List<GeneratorFeaturedRec>>();
			foreach (var row in data)
			{
				var rec = new GeneratorFeaturedRec
				{
					PrimaryId = row[headerPositions[0]],
					RecommendedId = row[headerPositions[1]]
				};
				if (convertLikelihood)
				{
					var likelihood = Input.SafeFloatConvert(row[headerPositions[3]]);
					rec.Ranking = likelihood <= 0F ? 0 : (int)Math.Floor(100 * (1.0 - likelihood) + .001);
				}
				else if (headerPositions[2] >= 0)
					rec.Ranking = Input.SafeIntConvert(row[headerPositions[2]]);
				else
					rec.Ranking = -1; //set it below instead

				List<GeneratorFeaturedRec> existing;
				if (firstPass.TryGetValue(rec.PrimaryId, out existing))
				{
					if (rec.Ranking == -1) rec.Ranking = existing.Select(x => x.Ranking).Max() + 1;
					firstPass[rec.PrimaryId].Add(rec);
				}
				else
				{
					if (rec.Ranking == -1) rec.Ranking = 1;
					firstPass.Add(rec.PrimaryId, new List<GeneratorFeaturedRec> { rec });
				}
			}

			//now cleanup rankings and convert to ManualRec format	
			Records = new Dictionary<string, FeaturedRecLists>();
			foreach (var f in firstPass)
			{
				var rec = new FeaturedRecLists();
				var blocked = f.Value.Where(x => x.Ranking.Equals(0));
				if (blocked.Any())
					rec.Blocked.AddRange(blocked.Select(x => x.RecommendedId));
				var recommended = f.Value.Where(x => x.Ranking > 0).ToList();
				if (recommended.Any())
				{
					recommended.Sort(); //sorts by ranking
					rec.Recommended.AddRange(recommended.Select(x => x.RecommendedId));
				}
				Records.Add(f.Key, rec);
			}
		}

		public List<GeneratorFeaturedRec> ToGeneratorRecs()
		{
			var genRecs = new List<GeneratorFeaturedRec>();
			foreach (var rec in Records)
			{
				var rank = 1;
				if (rec.Value.Recommended.Any())
					genRecs.AddRange(rec.Value.Recommended.Select(recommended => new GeneratorFeaturedRec
					{
						PrimaryId = rec.Key,
						RecommendedId = recommended,
						Ranking = rank++
					}));
				if (rec.Value.Blocked.Any())
					genRecs.AddRange(rec.Value.Blocked.Select(blocked => new GeneratorFeaturedRec
					{
						PrimaryId = rec.Key,
						RecommendedId = blocked,
						Ranking = 0
					}));
			}
			return genRecs;
		}

		public void AddRecords(string primaryId, List<string> header, List<string> data, List<FeaturedRecCondition> conditions)
		{
			if (conditions == null || !conditions.Any()) return;

			//each condition defines all recs for a given primary id
			FeaturedRecLists recs;
			if (!Records.TryGetValue(primaryId, out recs))
			{
				recs = new FeaturedRecLists();
				Records.Add(primaryId, recs);
			}

			//note: there could be a second condition for the same id if one is for excludes
			foreach (var c in conditions)
			{
				var value = Input.GetValue(header, data, c.ResultField);
				if (string.IsNullOrEmpty(value)) continue;

				FinishAddingRecord(primaryId, value, recs, c);
			}
		}

		public void AddRecords(string primaryId, XElement product, List<FeaturedRecCondition> conditions)
		{
			if (conditions == null || !conditions.Any()) return;

			//each condition defines all recs for a given primary id
			FeaturedRecLists recs;
			if (!Records.TryGetValue(primaryId, out recs))
			{
				recs = new FeaturedRecLists();
				Records.Add(primaryId, recs);
			}

			//note: there could be a second condition for the same id if one is for excludes
			foreach (var c in conditions)
			{
				var value = Input.GetValue(product, c.ResultField);
				if (string.IsNullOrEmpty(value)) continue;

				FinishAddingRecord(primaryId, value, recs, c);
			}
		}

		private void FinishAddingRecord(string primaryId, string value, FeaturedRecLists recs, FeaturedRecCondition c)
		{
			//NOTE: This assumes one product field with a comma separated list for includes
			//			and a second for excludes for each product
			//			includes will be ranked in order of how they are listed
			var newRecs = value.Split(',').Select(p => p.Trim())
																			.Where(r => !string.IsNullOrEmpty(r) && !r.Equals("-1")).ToList();
			if (!newRecs.Any()) return;

			foreach (var r in newRecs)
			{
				if (c.Include) //items are recommended
				{
					if (recs.Blocked.Count > 0 && recs.Blocked.Any(x => x.Equals(r))) //remove from blocked list
						Records[primaryId].Blocked.Remove(r);
					if (recs.Recommended.Count > 0 && recs.Recommended.Any(x => x.Equals(r))) continue; //already in the recommended list
					Records[primaryId].Recommended.Add(r);
				}
				else //items are blocked
				{
					if (recs.Recommended.Count > 0 && recs.Recommended.Any(x => x.Equals(r))) //remove from recommended list
						Records[primaryId].Recommended.Remove(r);
					if (recs.Blocked.Count > 0 && recs.Blocked.Any(x => x.Equals(r))) continue; //already in the blocked list
					Records[primaryId].Blocked.Add(r);
				}
			}
		}
	
	}
}