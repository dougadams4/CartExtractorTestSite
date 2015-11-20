using System;
using System.ComponentModel;
using System.Diagnostics;

namespace _4_Tell.Logs
{
	public class BoostError
	{
		public string Alias { get; set; }
		public string Message { get; set; }
		public string Details { get; set; }
		public EventLogEntryType Type { get; set; }
		public DateTime Time { get; set; }
		public bool SupportAlert { get; set; }

		public BoostError()
		{
			Alias = "";
			Message = "";
			Details = "";
			Type = EventLogEntryType.Information;
			Time = DateTime.Now;
			SupportAlert = false;
		}

		public BoostErrorExternal ToExternal()
		{
			return new BoostErrorExternal(this);
		}
	}

	public class BoostErrorExternal
	{
		public string Alias { get; set; }
		public string Message { get; set; }
		public EventLogEntryType Type { get; set; }
		public DateTime Time { get; set; }

		public BoostErrorExternal()
		{
			Alias = "";
			Message = "";
			Type = EventLogEntryType.Information;
			Time = DateTime.Now;
		}

		public BoostErrorExternal(BoostError error)
		{
			Alias = error.Alias;
			Message = error.Message;
			Type = error.Type;
			Time = error.Time;
		}
	}

	public class BoostErrorConverter : TypeConverter
	{
		public override bool CanConvertFrom(ITypeDescriptorContext context, Type sourceType)
		{
			if (sourceType == typeof(BoostError))
				return true;
			return base.CanConvertFrom(context, sourceType);
		}

		public override bool CanConvertTo(ITypeDescriptorContext context, Type destinationType)
		{
			if (destinationType == typeof(BoostError))
				return true;
			return base.CanConvertTo(context, destinationType);
		}

		public override object ConvertFrom(ITypeDescriptorContext context, System.Globalization.CultureInfo culture, object value)
		{
			var error = value as BoostError;
			if (error != null)
				return new BoostErrorExternal(error);
			return base.ConvertFrom(context, culture, value);
		}

		public override object ConvertTo(ITypeDescriptorContext context, System.Globalization.CultureInfo culture, object value, Type destinationType)
		{

			return base.ConvertTo(context, culture, value, destinationType);
		}
	}
}