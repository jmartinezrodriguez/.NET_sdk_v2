﻿using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GSMA.MobileConnect.Json.Converters
{
    /// <summary>
    /// Converts a unix timestamp to a DateTime
    /// </summary>
    public class UnixTimestampConverter : JsonConverter
    {
        private static readonly DateTime _epoch = new DateTime(1970, 1, 1);

        /// <inheritdoc/>
        public override bool CanConvert(Type objectType)
        {
            return (objectType == typeof(DateTime) || objectType == typeof(DateTime?));
        }

        /// <inheritdoc/>
        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if(reader.Value == null || string.IsNullOrEmpty(reader.Value.ToString()))
            {
                return objectType == typeof(DateTime?) ? null : (DateTime?)DateTime.MinValue;
            }

            var timestampNum = Convert.ToDouble(reader.Value.ToString());

            return _epoch.AddSeconds(timestampNum);
        }

        /// <inheritdoc/>
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var date = value as DateTime?;

            if(date == null)
            {
                writer.WriteNull();
                return;
            }

            var diff = date.Value - _epoch;
            writer.WriteValue((Int64)Math.Round(diff.TotalSeconds));
        }
    }
}
