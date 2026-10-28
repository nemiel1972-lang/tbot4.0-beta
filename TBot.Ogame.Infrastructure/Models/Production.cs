using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace TBot.Ogame.Infrastructure.Models {
	public class Production {
		public int ID { get; set; }
		[JsonConverter(typeof(ClampedIntConverter))]
		public int Nbr { get; set; }
	}

	public class ClampedIntConverter : JsonConverter {
		public override bool CanConvert(Type objectType) => objectType == typeof(int);

		public override bool CanWrite => false;

		public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer) {
			if (reader.TokenType == JsonToken.Null)
				return 0;
			if (reader.TokenType == JsonToken.Integer) {
				if (reader.Value is System.Numerics.BigInteger big)
					return big > int.MaxValue ? int.MaxValue : big < int.MinValue ? int.MinValue : (int) big;
				long l = Convert.ToInt64(reader.Value, System.Globalization.CultureInfo.InvariantCulture);
				return l > int.MaxValue ? int.MaxValue : l < int.MinValue ? int.MinValue : (int) l;
			}
			if (reader.TokenType == JsonToken.Float) {
				double d = Convert.ToDouble(reader.Value, System.Globalization.CultureInfo.InvariantCulture);
				return d >= int.MaxValue ? int.MaxValue : d <= int.MinValue ? int.MinValue : (int) d;
			}
			if (reader.TokenType == JsonToken.String && long.TryParse((string?) reader.Value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out long s))
				return s > int.MaxValue ? int.MaxValue : s < int.MinValue ? int.MinValue : (int) s;
			throw new JsonSerializationException($"Cannot read '{reader.Value}' as a number for Production.Nbr.");
		}

		public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer) {
			writer.WriteValue((int) value!);
		}
	}
}
