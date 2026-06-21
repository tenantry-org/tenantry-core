using System.Text.Json.Serialization;
using Tenantry.Samples.Aot.Models;

namespace Tenantry.Samples.Aot.Serialization;

[JsonSerializable(typeof(IEnumerable<Order>))]
[JsonSerializable(typeof(TenantResponse))]
[JsonSerializable(typeof(string))]
internal partial class AppJsonSerializerContext : JsonSerializerContext;
